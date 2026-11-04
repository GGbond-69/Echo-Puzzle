using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace EchoPuzzle
{
    /// <summary>
    /// 运行时构建 NavMesh，无需在编辑器里手动烘焙。
    ///
    /// ★ 核心概念（之前理解错了，这里说明清楚）：
    ///
    ///   NavMeshBuilder 是**体素化**的：它把所有 source 投影到同一张高度场里，
    ///   然后按 agent 半径从"不可走区域"的边缘向内收缩。
    ///
    ///   所以**墙必须作为 source 传进去、并标记为 NotWalkable**，
    ///   它才会在地面上"挖出洞"，NavMeshAgent 才知道要绕开。
    ///
    ///   如果只传地板、不传墙，得到的是一整片连续的可走平面 ——
    ///   墙的位置根本没有缺口，怪物就会直线穿墙（或被物理挡住而卡死）。
    ///   这正是"怪物只会直线走 / 撞墙卡住"的根因。
    ///
    /// 分类规则（手工关卡）：
    ///   1) 世界 y 坐标为 **0 或 8** 的物体        -> 地板，Walkable
    ///   2) 挂了 **SlopeRamp** 组件的物体          -> 斜坡，Walkable
    ///   3) **HintZone / TriggerVolume**           -> 忽略（既不是地板也不是墙）
    ///   4) 玩家 / 怪物（CharacterController 等）  -> 忽略
    ///   5) 其它**一切有碰撞体的实体**（墙体、天花板、WallOccluder…）
    ///                                             -> 障碍，NotWalkable
    ///
    /// ★ 安全设计：
    ///   · 整个烘焙过程包 try-catch —— 异常只打一条警告，绝不向调用方扩散。
    ///     烘焙失败时怪物自动退化为直线移动，游戏照常启动。
    ///   · 触发时机放在怪物的 Start：场景初始化完成后再烘焙，不阻塞加载。
    ///   · 重复调用只烘焙一次（m_Tried 去重）。
    /// </summary>
    public static class NavMeshRuntimeBaker
    {
        private const float FloorY0 = 0f;
        private const float FloorY8 = 8f;

        private const float YTolerance = 0.05f;

        /// <summary>Walkable 区域的 area 索引（0 是 Unity 默认的 "Walkable"）。</summary>
        private const int AreaWalkable = 0;

        /// <summary>
        /// "Not Walkable" 的 area 索引。
        ///
        /// Unity 里 0 = Walkable、1 = Not Walkable 是内建的两个特殊 area，
        /// 它们本身就是"不可走"语义，不需要在 Navigation 窗口里额外配置。
        ///
        /// 用它标记墙体，投影到地面后就成了 NavMesh 上的洞。
        /// </summary>
        private const int AreaNotWalkable = 1;

        private static NavMeshData        m_Data;
        private static NavMeshDataInstance m_Instance;
        private static bool               m_Tried;

        /// <summary>
        /// 强制重新烘焙（清掉旧 NavMesh 再建）。
        ///
        /// ★ 给**运行时生成地图**的场景用（地牢模式）：
        ///   地图是运行时才建出来的，而 <see cref="Ensure"/> 只会烘焙一次 ——
        ///   如果它在地图建好之前被调用过，NavMesh 就是空的，
        ///   之后再也不会重建（Agent 全部报
        ///   "Failed to create agent because there is no valid NavMesh"）。
        /// </summary>
        public static void Rebuild()
        {
            // 先移除旧的
            if (m_Instance.valid) m_Instance.Remove();

            m_Data    = null;
            m_Instance = default;
            m_Tried   = false;

            Ensure();
        }

        public static void Ensure()
        {
            if (m_Tried) return;
            m_Tried = true;

            try
            {
                Build();
            }
            catch (System.Exception e)
            {
                // 烘焙失败绝不影响游戏运行：只警告，怪物退回直线移动
                Debug.LogWarning("[EchoPuzzle] NavMesh 运行时烘焙失败，怪物将退回直线移动。\n" + e);
                if (m_Data != null)
                {
                    if (m_Instance.valid) m_Instance.Remove();
                    m_Data = null;
                }
            }
        }

        private static void Build()
        {
            var sources = new List<NavMeshBuildSource>(1024);
            Bounds bounds = default;
            bool hasBounds = false;

            int floorCount   = 0;
            int obstacleCount = 0;

            // 场景里所有 MeshFilter —— 地板和墙都是 MeshFilter（Cube primitive 等）
            var filters = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);

            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter mf = filters[i];
                if (mf == null || mf.gameObject == null) continue;

                // ---- 1) 触发区 / 提示区：既不是地板也不是墙，完全不参与 ----
                if (mf.GetComponentInParent<HintZone>() != null) continue;
                if (mf.GetComponentInParent<TriggerVolume>() != null) continue;

                // ---- 2) 玩家 / 怪物等动态物体：排除 ----
                if (mf.GetComponentInParent<CharacterController>() != null) continue;
                if (mf.GetComponentInParent<NavMeshAgent>() != null) continue;

                // ---- 3) 没有碰撞体的纯动态网格（光带 / 标记 / 脚印）：
                //         顶点运行时才生成，进烘焙器会报 invalid vertex data ----
                if (mf.GetComponentInParent<Collider>() == null) continue;

                Mesh mesh = mf.sharedMesh;
                if (mesh == null) continue;

                // ---- 4) 分类：地板 or 墙 ----
                Vector3 pos = mf.transform.position;
                bool isFloor = Mathf.Abs(pos.y - FloorY0) < YTolerance
                            || Mathf.Abs(pos.y - FloorY8) < YTolerance
                            || mf.GetComponentInParent<SlopeRamp>() != null;

                sources.Add(new NavMeshBuildSource
                {
                    transform    = mf.transform.localToWorldMatrix,
                    shape        = NavMeshBuildSourceShape.Mesh,
                    sourceObject = mesh,

                    // ★ 关键：墙也要进 sources，但 area 标成 NotWalkable
                    area         = isFloor ? AreaWalkable : AreaNotWalkable,
                });

                if (isFloor) floorCount++;
                else         obstacleCount++;

                // 合并整体包围盒（BuildNavMeshData 的 localBounds 要盖住所有源）
                Vector3 center = mf.transform.TransformPoint(mesh.bounds.center);
                Vector3 size   = mf.transform.TransformVector(mesh.bounds.size);

                if (!hasBounds)
                {
                    bounds    = new Bounds(center, size);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(new Bounds(center, size));
                }
            }

            if (floorCount == 0 || !hasBounds)
            {
                Debug.LogWarning($"[EchoPuzzle] NavMesh 烘焙：没有收集到任何地板（floor={floorCount}）。\n" +
                                 "  地板判定规则是「物体 y ≈ 0 或 8，或挂了 SlopeRamp 组件」。\n" +
                                 "  如果你的地板不满足这个条件，就会全部被当成墙 —— 怪物只能走直线。");
                return;
            }

            // 包围盒兜底：太小说明收集异常，扩到足够大避免烘焙被裁掉
            if (bounds.size.magnitude < 1f)
                bounds = new Bounds(bounds.center, new Vector3(200f, 20f, 200f));

            // 稍微外扩一点，避免最外圈的边缘被 localBounds 裁掉
            bounds.Expand(new Vector3(2f, 4f, 2f));

            // ---- 烘焙设置 ----
            var settings = NavMesh.GetSettingsByID(0);

            // ★ agentRadius 必须**大于**怪物的实际碰撞半径，才不会被墙卡住。
            //
            //   NavMesh 按 agentRadius 从墙面**向内收缩**，可走边界距墙面 = agentRadius。
            //   怪物的 CharacterController 实际碰撞半径 = bodyRadius + skinWidth。
            //
            //   如果 agentRadius < 碰撞半径：
            //     agent 会把路径规划到"人根本挤不进去"的区域，
            //     怪物走到那里就被墙顶住 —— 想走却走不动，永远卡住。
            //
            //   当前配置：
            //     碰撞半径 = bodyRadius 0.5 + skinWidth 0.01 = 0.51
            //     agentRadius = 0.53  -> 余量 0.02m
            //
            //   ★ 余量只有 0.02m 是偏薄的（NavMesh 是体素化烘焙，
            //     栅格误差可能吃掉一部分）。如果实测仍偶尔贴墙卡住，
            //     把这里调到 0.55 ~ 0.60 会稳很多。
            settings.agentRadius = 0.53f;

            // 头部空间。怪物悬浮 2m，层高足够即可
            settings.agentHeight = 1f;

            // ★ 坡度上限：必须**大于**怪物能力，否则 NavMesh 会把陡坡当墙面，
            //   根本不会规划一条上去的路 —— 表现为"怪物永远绕开斜坡"。
            //   需求要能爬 70° 的坡，这里留一点余量给体素化误差。
            settings.agentSlope  = 85f;

            settings.agentClimb  = 0.4f;    // 能跨多高的台阶

            // ★★ 关键：**强制使用小体素** ★★
            //
            //   NavMesh 的"这块地面能不能站"是在**体素栅格**上判断的。
            //   默认体素 = agentRadius * 2/3 ≈ 0.35m。
            //
            //   对 70° 的陡坡，每前进一个体素就要上升 0.35 * tan70° ≈ 0.96m，
            //   跨度接近 3 个体素 —— 采样严重失真，
            //   坡面很容易被判成"过陡的台阶"整片丢掉。
            //   这就是"坡看着有、怪物却完全不走"的真正原因。
            //
            //   把体素压到 0.1m（70° 时每体素只升 0.27m，不到 3 个体素），
            //   坡面就能被正确识别为可走。
            //   overrideVoxelSize 必须**显式打开**，否则设置被忽略。
            settings.overrideVoxelSize = true;
            settings.voxelSize         = 0.1f;

            // ★ 这个值太大会把**狭窄的坡面**当成"孤岛"整片丢掉。
            //   小体素 + 小 minRegionArea，窄坡才能保住。
            settings.minRegionArea = 0.1f;

            m_Data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds,
                                                     Vector3.zero, Quaternion.identity);
            if (m_Data == null)
            {
                Debug.LogWarning("[EchoPuzzle] NavMesh 烘焙：BuildNavMeshData 返回 null。");
                return;
            }

            m_Instance = NavMesh.AddNavMeshData(m_Data);
        }
    }
}
