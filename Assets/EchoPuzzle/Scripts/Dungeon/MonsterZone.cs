using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle.Dungeon
{
    /// <summary>
    /// 【地牢模式】怪物区。
    ///
    /// ══════════════════════════════════════════════════════════════
    /// 玩法
    ///   · 地图矩阵里数字 3 的格子就是「怪物区」（怪物房的地板）
    ///   · 玩家踏入怪物区 -> 与之**相连的整片**怪物区一起被封锁
    ///   · 同时在玩家脚下触发「怪物潮」事件
    ///   · 怪物潮结束后 -> 解除封锁，玩家可以离开
    ///   · **再次进入不会刷新怪物**（同一层里每个区域只触发一次）
    ///
    /// ══════════════════════════════════════════════════════════════
    /// ★★ 交接说明（怪物潮的具体内容由别人实现）★★
    ///
    ///   这个脚本**不生成任何怪物**，只负责：
    ///     1. 检测玩家进入怪物区
    ///     2. 计算"相连的一整片"并封锁
    ///     3. 广播 <see cref="OnMonsterWave"/> 事件
    ///     4. 等事件结束后解除封锁
    ///
    ///   要接上怪物潮，只需订阅：
    ///
    ///       MonsterZone.OnMonsterWave += zone => StartCoroutine(MyWave(zone));
    ///
    ///   其中 <c>zone</c> 是 <see cref="MonsterZoneArgs"/>，带这些信息：
    ///     · <c>Cells</c>      这片怪物区包含哪些格子（可以在里面刷怪）
    ///     · <c>Center</c>     这片区域的世界坐标中心
    ///     · <c>Finish</c>()   怪物潮打完后**必须**调一次，否则玩家永远被锁在里面
    ///
    ///   典型写法：
    ///
    ///       IEnumerator MyWave(MonsterZoneArgs zone)
    ///       {
    ///           foreach (var cell in zone.Cells) SpawnEnemy(cell);
    ///           while (AnyEnemyAlive()) yield return null;
    ///           zone.Finish();       // ← 一定要调
    ///       }
    /// ══════════════════════════════════════════════════════════════
    /// </summary>
    [DefaultExecutionOrder(-90)]
    [AddComponentMenu("Echo Puzzle/Dungeon/Monster Zone")]
    public sealed class MonsterZone : MonoBehaviour
    {
        public static MonsterZone Instance { get; private set; }

        // ------------------------------------------------------------------ 怪物潮事件

        /// <summary>
        /// 玩家踏入一片新的怪物区时触发。
        ///
        /// ★ 订阅者**必须**在怪物潮结束时调用 <see cref="MonsterZoneArgs.Finish"/>，
        ///   否则封锁永远不会解除。
        ///
        /// ★ 如果**没有**任何订阅者，会当作"怪物潮瞬间结束"，
        ///   立即解除封锁（方便先跑通流程）。
        /// </summary>
        public static event System.Action<MonsterZoneArgs> OnMonsterWave;

        /// <summary>怪物潮的参数。</summary>
        public sealed class MonsterZoneArgs
        {
            /// <summary>这片怪物区包含的格子（矩阵坐标）。</summary>
            public readonly List<Vector2Int> Cells;

            /// <summary>这片区域的世界坐标中心。</summary>
            public readonly Vector3 Center;

            /// <summary>矩阵一格对应的世界尺寸。</summary>
            public readonly float CellSize;

            private readonly System.Action m_OnFinish;
            private bool m_Finished;

            public MonsterZoneArgs(List<Vector2Int> cells, Vector3 center,
                                   float cellSize, System.Action onFinish)
            {
                Cells    = cells;
                Center   = center;
                CellSize = cellSize;
                m_OnFinish = onFinish;
            }

            /// <summary>
            /// 怪物潮结束：解除封锁。
            ///
            /// ★ 重复调用无副作用。
            /// </summary>
            public void Finish()
            {
                if (m_Finished) return;
                m_Finished = true;

                m_OnFinish?.Invoke();
            }
        }

        // ------------------------------------------------------------------ 配置

        [Header("封锁外观")]
        [Tooltip("封锁时在区域边界生成的立方体材质。留空则用默认材质")]
        public Material barrierMaterial;

        [Tooltip("封锁用的立方体边长（米）")]
        [Min(0.1f)] public float barrierCellSize = 1f;

        [Header("调试")]
        [Tooltip("没有订阅者时，等多久自动结束怪物潮（秒）。0 = 立刻结束")]
        [Min(0f)] public float autoFinishDelay = 3f;

        // ------------------------------------------------------------------ 运行时

        /// <summary>这一层所有怪物区的格子 -> 是否已经触发过。</summary>
        private readonly HashSet<Vector2Int> m_AllCells = new HashSet<Vector2Int>();

        /// <summary>已经触发过的怪物区（用格子集合里的任一格作代表）。</summary>
        private readonly HashSet<Vector2Int> m_Consumed = new HashSet<Vector2Int>();

        /// <summary>当前正在封锁中的区域格子。</summary>
        private readonly HashSet<Vector2Int> m_Locked = new HashSet<Vector2Int>();

        /// <summary>封锁用的方块。</summary>
        private readonly List<GameObject> m_Barriers = new List<GameObject>();

        /// <summary>正在等待的怪物潮数量。</summary>
        private int m_PendingWaves;

        private float m_CellSize = 1f;
        private Transform m_BarrierRoot;
        private Texture2D m_Black;

        private PlayerController m_Player;

        // ------------------------------------------------------------------ 生命周期

        private void Awake()
        {
            Instance = this;

            var rootGo = new GameObject("[生成] 怪物区封锁");
            rootGo.transform.SetParent(transform, false);
            m_BarrierRoot = rootGo.transform;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (m_Black != null) Destroy(m_Black);
        }

        private void Update()
        {
            if (m_Player == null) m_Player = FindObjectOfType<PlayerController>();
            if (m_Player == null) return;

            // 正在封锁 / 正在打怪物潮 -> 不检测新的区域
            if (m_PendingWaves > 0) return;

            Vector2Int cell = WorldToCell(m_Player.FootPosition);

            if (!m_AllCells.Contains(cell)) return;       // 不在怪物区
            if (m_Consumed.Contains(cell)) return;        // 这片已经打过了

            EnterZone(cell);
        }

        // ------------------------------------------------------------------ 注册（由地图生成器调用）

        /// <summary>
        /// 地图生成后调用：把这层的怪物区格子交给本组件。
        ///
        /// ★ 每次建图都会重置"已触发"记录 —— 新的一层怪物区要重新打。
        /// </summary>
        /// <param name="cells">矩阵里所有数字 3 的格子。</param>
        /// <param name="cellSize">矩阵一格的世界尺寸。</param>
        public void RegisterCells(List<Vector2Int> cells, float cellSize)
        {
            m_CellSize = Mathf.Max(0.1f, cellSize);

            m_AllCells.Clear();
            m_Consumed.Clear();

            ClearBarriers();
            m_Locked.Clear();
            m_PendingWaves = 0;

            if (cells == null) return;

            for (int i = 0; i < cells.Count; i++)
                m_AllCells.Add(cells[i]);

            Debug.Log($"[地牢] 怪物区已注册：{m_AllCells.Count} 个格子。");
        }

        // ------------------------------------------------------------------ 进入

        /// <summary>玩家踏入了某个怪物区格子。</summary>
        private void EnterZone(Vector2Int startCell)
        {
            // ---- 1) 找出相连的一整片 ----
            List<Vector2Int> region = FloodFill(startCell);

            if (region.Count == 0) return;

            // ---- 2) 标记为"已触发" ----
            for (int i = 0; i < region.Count; i++)
                m_Consumed.Add(region[i]);

            // ---- 3) 封锁 ----
            m_Locked.Clear();
            for (int i = 0; i < region.Count; i++)
                m_Locked.Add(region[i]);

            BuildBarriers(region);

            // ---- 4) 算中心（给怪物潮刷怪用）----
            Vector3 center = Vector3.zero;
            for (int i = 0; i < region.Count; i++)
                center += new Vector3(region[i].x, 0f, region[i].y) * m_CellSize;
            center /= Mathf.Max(1, region.Count);
            center.y = 1f;

            Debug.Log($"[地牢] 进入怪物区：{region.Count} 个格子，封锁开始。");

            // ---- 5) 广播怪物潮事件 ----
            TriggerWave(region, center);
        }

        /// <summary>触发怪物潮。</summary>
        private void TriggerWave(List<Vector2Int> region, Vector3 center)
        {
            m_PendingWaves++;

            var args = new MonsterZoneArgs(region, center, m_CellSize, OnWaveFinished);

            if (OnMonsterWave != null)
            {
                // ★ 有订阅者：交给它处理，它打完后会调 args.Finish()
                OnMonsterWave.Invoke(args);
            }
            else
            {
                // ★ 没有订阅者（还没接怪物潮）：按 autoFinishDelay 自动结束，
                //   这样流程能先跑通。
                Debug.Log("[地牢] 没有订阅者处理怪物潮，按自动延时解除封锁。");
                StartCoroutine(AutoFinish(args));
            }
        }

        private IEnumerator AutoFinish(MonsterZoneArgs args)
        {
            if (autoFinishDelay > 0f)
                yield return new WaitForSeconds(autoFinishDelay);

            args.Finish();
        }

        /// <summary>怪物潮结束：解除封锁。</summary>
        private void OnWaveFinished()
        {
            m_PendingWaves = Mathf.Max(0, m_PendingWaves - 1);

            // 还有别的怪物潮在跑 -> 先不解除
            if (m_PendingWaves > 0) return;

            m_Locked.Clear();
            ClearBarriers();

            Debug.Log("[地牢] 怪物潮结束，封锁解除。");
        }

        // ------------------------------------------------------------------ 洪水填充

        /// <summary>
        /// 从 <paramref name="start"/> 出发，找出所有**相连**的怪物区格子
        /// （上下左右四邻接）。
        /// </summary>
        private List<Vector2Int> FloodFill(Vector2Int start)
        {
            var result  = new List<Vector2Int>(64);
            var visited = new HashSet<Vector2Int>();

            var stack = new Stack<Vector2Int>();
            stack.Push(start);
            visited.Add(start);

            while (stack.Count > 0)
            {
                Vector2Int c = stack.Pop();
                result.Add(c);

                TryPush(c + Vector2Int.right);
                TryPush(c + Vector2Int.left);
                TryPush(c + new Vector2Int(0, 1));
                TryPush(c + new Vector2Int(0, -1));
            }

            return result;

            void TryPush(Vector2Int n)
            {
                if (visited.Contains(n)) return;
                if (!m_AllCells.Contains(n)) return;

                visited.Add(n);
                stack.Push(n);
            }
        }

        // ------------------------------------------------------------------ 封锁几何

        /// <summary>
        /// 沿这片区域的**边界**生成一圈方块，把玩家关在里面。
        ///
        /// 做法：对区域里每个格子，检查四邻接 —— 邻居不在这片区域里
        /// 就在那一侧立一块（从这里可以走出去，需要挡住）。
        /// </summary>
        private void BuildBarriers(List<Vector2Int> region)
        {
            var inRegion = new HashSet<Vector2Int>(region);

            for (int i = 0; i < region.Count; i++)
            {
                Vector2Int c = region[i];

                if (!inRegion.Contains(c + Vector2Int.right)) SpawnWall(c, Vector3.right);
                if (!inRegion.Contains(c + Vector2Int.left))  SpawnWall(c, Vector3.left);
                if (!inRegion.Contains(c + new Vector2Int(0,  1))) SpawnWall(c, Vector3.forward);
                if (!inRegion.Contains(c + new Vector2Int(0, -1))) SpawnWall(c, Vector3.back);
            }
        }

        /// <summary>在格子 c 的某一侧立一块墙。</summary>
        private void SpawnWall(Vector2Int c, Vector3 dir)
        {
            float s = m_CellSize;

            // 格子的世界中心
            Vector3 center = new Vector3(c.x * s, 0f, c.y * s);

            // 半格偏移 -> 贴到格子边缘
            Vector3 pos = center + dir * (s * 0.5f);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);

            go.name = $"[封锁] ({c.x},{c.y}) {dir}";
            go.transform.SetParent(m_BarrierRoot, false);

            // 薄墙：沿墙方向厚 0.2，高 6
            Vector3 scale;
            if (Mathf.Abs(dir.x) > 0.5f) scale = new Vector3  (0.2f, 6f, s);
            else                          scale = new Vector3  (s, 6f, 0.2f);

            go.transform.position   = pos + Vector3.up * 3f;
            go.transform.localScale = scale;

            // 墙要有碰撞体才会真的挡住玩家（Primitive Cube 自带 BoxCollider）

            // ★ 材质留空 -> 用完全透明的 EchoPuzzle/Invisible
            //   （封锁墙在游戏里应该看不见，玩家只能从"走不过去"察觉）
            var r = go.GetComponent<Renderer>();
            if (r != null)
                r.sharedMaterial = barrierMaterial != null ? barrierMaterial : InvisibleMat();

            m_Barriers.Add(go);
        }

        /// <summary>封锁墙的默认材质（完全透明）。</summary>
        private static Material s_InvisibleMat;

        private static Material InvisibleMat()
        {
            if (s_InvisibleMat != null) return s_InvisibleMat;

            s_InvisibleMat = Resources.Load<Material>("EchoRuntime/EchoInvisible");

#if UNITY_EDITOR
            if (s_InvisibleMat == null)
                s_InvisibleMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/EchoPuzzle/Materials/EchoInvisible.mat");
#endif

            if (s_InvisibleMat == null)
            {
                Shader sh = Shader.Find("EchoPuzzle/Invisible");
                if (sh != null)
                {
                    s_InvisibleMat = new Material(sh)
                    {
                        name      = "MonsterZoneInvisibleRuntime",
                        hideFlags = HideFlags.DontSave,
                    };
                }
            }

            return s_InvisibleMat;
        }

        private void ClearBarriers()
        {
            for (int i = 0; i < m_Barriers.Count; i++)
                if (m_Barriers[i] != null) Destroy(m_Barriers[i]);

            m_Barriers.Clear();
        }

        // ------------------------------------------------------------------ 工具

        /// <summary>世界坐标 -> 矩阵格子。</summary>
        private Vector2Int WorldToCell(Vector3 world)
        {
            float s = Mathf.Max(0.1f, m_CellSize);

            return new Vector2Int(
                Mathf.RoundToInt(world.x / s),
                Mathf.RoundToInt(world.z / s));
        }
    }
}
