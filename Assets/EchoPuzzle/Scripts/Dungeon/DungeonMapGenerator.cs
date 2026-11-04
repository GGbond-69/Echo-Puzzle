using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle.Dungeon
{
    /// <summary>
    /// 【地牢模式】地图生成器。
    ///
    /// ══════════════════════════════════════════════════════════════
    /// ★★ 你要改的矩阵在下面 <see cref="MapMatrix"/> 里 ★★
    ///
    ///   77 行 × 21 列：
    ///     · 行（第一个下标）= X 轴，0..76
    ///     · 列（第二个下标）= Z 轴，0..20
    ///
    ///   格子里的数字含义：
    ///     0 -> 什么都不放
    ///     1 -> 地板：y=0 放一个立方体，y=6 放一个立方体
    ///     2 -> 墙  ：y=0..6 叠 7 个立方体
    ///     3 -> 玩家出生点：y=1 处放玩家
    ///     4 -> 下一层触发区（TriggerVolume，NextStage 类别）
    ///
    ///   直接改数字就能改地图，保存后重新进游戏即可。
    /// ══════════════════════════════════════════════════════════════
    ///
    /// ── 坐标系 ──────────────────────────────────────────────
    ///   矩阵的 [x, z] 直接当作世界坐标的 (x, z) 使用：
    ///     格子在世界的中心 = (x, 0, z)
    ///     立方体尺寸 1×1×1，中心 y = 0 / 1 / ... / 6
    ///
    /// ── 生成时机 ────────────────────────────────────────────
    ///   · 进入 DungeonMode 场景时自动生成第 1 层
    ///   · 玩家踩到 NextStage 触发区时：
    ///       地图淡出 -> 清掉旧地图 -> 生成新的 -> 淡入 -> 玩家回到 3 的位置
    ///
    /// ── 生成的物体 ──────────────────────────────────────────
    ///   全部挂在生成器自己的孩子节点下，名字以 "[生成]" 开头，
    ///   方便一次清空（不会误删场景里你手工摆的东西）。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("Echo Puzzle/Dungeon/Dungeon Map Generator")]
    public sealed class DungeonMapGenerator : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════════════════════
        //                          ★★★ 地图矩阵 ★★★
        //
        //   77 行 × 21 列。第一个下标 = X 轴(0..76)，第二个下标 = Z 轴(0..20)。
        //
        //   格子里的数字：
        //     0 -> 什么都不放
        //     1 -> 地板：y=0 和 y=6 各放一个 1×1×1 立方体
        //     2 -> 墙  ：y=0..6 叠 7 个立方体
        //     3 -> 怪物区：可通行的地板，玩家踏入后触发"怪物潮"并封锁整片区域
        //     8 -> 玩家出生点：地板 + y=1 处放玩家
        //     9 -> 下一层（终点）：地板 + 放一个 TriggerVolume，类别 NextStage
        //
        //   ★ 直接改下面的数字即可，一行就是一条 X，从左到右是 Z=0..20。
        //     改完保存，重新进游戏就生效。
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>地图矩阵（77 × 21），第一维 = X（行），第二维 = Z（列）。</summary>
        public static readonly int[,] MapMatrix = new int[RowsX, ColsZ]
        {
            /* x= 0 */ { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            /* x= 1 */ { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            /* x= 2 */ { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            /* x= 3 */ { 0, 0, 0, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 0, 0, 0 },
            /* x= 4 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x= 5 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x= 6 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x= 7 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x= 8 */ { 0, 0, 0, 2, 1, 1, 1, 1, 9, 9, 9, 9, 9, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x= 9 */ { 0, 0, 0, 2, 1, 1, 1, 1, 9, 9, 9, 9, 9, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=10 */ { 0, 0, 0, 2, 1, 1, 1, 1, 9, 9, 9, 9, 9, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=11 */ { 0, 0, 0, 2, 1, 1, 1, 1, 9, 9, 9, 9, 9, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=12 */ { 0, 0, 0, 2, 1, 1, 1, 1, 9, 9, 9, 9, 9, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=13 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=14 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=15 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=16 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=17 */ { 0, 0, 0, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 0, 0, 0 },
            /* x=18 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=19 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=20 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=21 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=22 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=23 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=24 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=25 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=26 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=27 */ { 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2 },
            /* x=28 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=29 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=30 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=31 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=32 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=33 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=34 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=35 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=36 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=37 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=38 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=39 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=40 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=41 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=42 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=43 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=44 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=45 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=46 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=47 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=48 */ { 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2 },
            /* x=49 */ { 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2 },
            /* x=50 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=51 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=52 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=53 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=54 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=55 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=56 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=57 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=58 */ { 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 1, 1, 1, 2, 0, 0, 0, 0, 0, 0, 0 },
            /* x=59 */ { 0, 0, 0, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 0, 0, 0 },
            /* x=60 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=61 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=62 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=63 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=64 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=65 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=66 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 8, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=67 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=68 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=69 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=70 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=71 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=72 */ { 0, 0, 0, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 0 },
            /* x=73 */ { 0, 0, 0, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 0, 0, 0 },
            /* x=74 */ { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            /* x=75 */ { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            /* x=76 */ { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        };

        // ══════════════════════════════════════════════════════════════════════

        // ★ 矩阵尺寸：77 行（X 轴）× 21 列（Z 轴）。
        //   deepseek 写到一半时这两个常量没定义，导致编译失败（CS0103），
        //   这里补上。矩阵声明和遍历都用它们。
        public const int RowsX = 77;   // X 轴（第一个下标）
        public const int ColsZ = 21;   // Z 轴（第二个下标）

        // ══════════════════════════════════════════════════════════════════════

        // ══════════════════════════════════════════════════════════
        //                       以下为生成逻辑
        //  （一般不需要改；要调尺寸 / 材质 / 淡入淡出时长就改 Inspector）
        // ══════════════════════════════════════════════════════════

        [Header("几何")]
        [Tooltip("地板立方体的 y 高度。1 = 在 y=0 和 y=6 各放一个")]
        public int floorBottomY = 0;
        public int floorTopY    = 6;

        [Tooltip("墙体：y 从 WallBottomY 到 WallTopY（含）每层放一个立方体")]
        public int wallBottomY = 0;
        public int wallTopY    = 6;

        [Tooltip("立方体尺寸（米）。矩阵一格 = 这么多米")]
        public float cellSize = 1f;

        [Header("玩家")]
        [Tooltip("玩家出生时在 y 方向的高度（格子 3 处）")]
        public float playerSpawnY = 1f;

        [Header("淡入淡出")]
        [Tooltip("切层时黑幕淡出 / 淡入的时长（秒）")]
        [Min(0f)] public float fadeDuration = 0.8f;

        [Header("外观")]
        [Tooltip("地板材质。\n\n" +
                 "★ 留空 = 用 EchoPuzzle/Invisible（**完全透明**，只留碰撞）。\n" +
                 "  地牢模式靠声波光带感知形状，地板墙体本来就不该看得见。")]
        public Material floorMaterial;

        [Tooltip("墙体材质。\n\n" +
                 "★ 留空 = 用 EchoPuzzle/Invisible（**完全透明**，只留碰撞）。")]
        public Material wallMaterial;

        [Header("下一层触发区")]
        [Tooltip("NextStage 触发区被踩到时光带的外观。默认与 Win 类相同（琥珀色强光柱）")]
        public SonarEmitSettings nextStageSonar = SonarEmitSettings.DefaultWin;

        [Header("终点触发区：射入光带的外观")]
        [Tooltip("射入终点触发区的光带，粗细改成多少（米）")]
        [Min(0.001f)] public float nextStageWaveWidth = 0.22f;

        [Tooltip("射入终点触发区的光带，不透明度（1 = 保持全亮）")]
        [Range(0f, 1f)] public float nextStageWaveAlpha = 1f;

        [Tooltip("射入终点触发区的光带，颜色")]
        public Color nextStageWaveColor = Color.white;

        // ------------------------------------------------------------------ 运行时

        /// <summary>当前层数（从 1 开始）。</summary>
        public int CurrentStage { get; private set; } = 0;

        /// <summary>所有生成出来的东西都挂在这个节点下，方便一次清空。</summary>
        private Transform m_Root;

        /// <summary>当前地图上的 NextStage 触发区（生成时创建）。</summary>
        private readonly List<TriggerVolume> m_NextStageVolumes = new List<TriggerVolume>();

        /// <summary>当前这层的玩家出生点（矩阵里数字 8 的世界坐标）。</summary>
        private Vector3 m_SpawnPosition;
        private bool    m_HasSpawn;

        /// <summary>
        /// 把玩家瞬移到当前层的出生点。
        ///
        /// ★ 给 GameUI 在"开始新游戏 / 重新开始"时调用 ——
        ///   地牢模式的起点不是 GameUI 记的那个场景原始坐标，
        ///   而是由地图矩阵里的数字 8 决定的。
        /// </summary>
        public void TeleportPlayerToSpawn()
        {
            if (!m_HasSpawn) return;

            PlacePlayer(m_SpawnPosition);
        }

        /// <summary>是否正在切层（防止连点）。</summary>
        private bool m_Transitioning;

        private PlayerController m_Player;

        // 淡入淡出
        private float m_FadeAlpha;      // 0 = 全黑，1 = 全亮
        private bool  m_Fading;
        private bool  m_FadeIn;         // true = 正在淡入（黑 -> 亮）

        private Texture2D m_Black;

        // ------------------------------------------------------------------ 生命周期

        private void Awake()
        {
            m_Black = MakeBlack();

            // 生成出来的东西统一挂在这个节点下
            var rootGo = new GameObject("[生成] 地牢地图");
            rootGo.transform.SetParent(transform, false);
            m_Root = rootGo.transform;
        }

        private void OnEnable()
        {
            // 玩家踩到 NextStage -> 切下一层
            TriggerVolume.OnNextStage -= HandleNextStage;
            TriggerVolume.OnNextStage += HandleNextStage;
        }

        private void OnDisable()
        {
            TriggerVolume.OnNextStage -= HandleNextStage;
        }

        private void OnDestroy()
        {
            if (m_Black != null) Destroy(m_Black);
        }

        private void Start()
        {
            // ★ 进场景就生成第 1 层
            RebuildStage();
        }

        private void Update()
        {
            // 淡入淡出的推进
            if (!m_Fading) return;

            float dt = Time.unscaledDeltaTime;
            float speed = fadeDuration > 0.0001f ? 1f / fadeDuration : 999f;

            m_FadeAlpha = Mathf.MoveTowards(m_FadeAlpha, m_FadeIn ? 0f : 1f, speed * dt);

            if (m_FadeIn && m_FadeAlpha <= 0f)
            {
                m_FadeAlpha = 0f;
                m_Fading    = false;
            }
            else if (!m_FadeIn && m_FadeAlpha >= 1f)
            {
                m_FadeAlpha = 1f;
                m_Fading    = false;
            }
        }

        // ------------------------------------------------------------------ 切层

        /// <summary>玩家进入 NextStage：淡出 -> 重建 -> 淡入。</summary>
        private void HandleNextStage()
        {
            if (m_Transitioning) return;

            StartCoroutine(NextStageRoutine());
        }

        private IEnumerator NextStageRoutine()
        {
            m_Transitioning = true;

            // ---- 1) 淡出到全黑 ----
            m_FadeIn  = false;
            m_Fading  = true;

            while (m_Fading) yield return null;

            // ---- 2) 清空场上残留 + 重建地图 ----
            ClearTransientEffects();
            RebuildStage();

            // ---- 3) 淡入 ----
            m_FadeIn = true;
            m_Fading = true;

            while (m_Fading) yield return null;

            m_Transitioning = false;
        }

        /// <summary>
        /// 清掉上一张地图遗留在场上的东西：光带、标记、脚印。
        ///
        /// ★ 换地图时这些必须一起消失 —— 否则上一层的声波残留会留在
        ///   新地图里，玩家会以为那是新地图的轮廓。
        ///
        /// ★ 也清掉正在飞的石子 / 匕首的残留，避免跨层残留。
        /// </summary>
        private void ClearTransientEffects()
        {
            // ---- 光带 ----
            SonarSystem sonar = FindObjectOfType<SonarSystem>();
            if (sonar != null)
            {
                var trail = sonar.GetComponentInChildren<SonarTrailRenderer>(true);
                if (trail != null) trail.ClearTrails();
            }

            // ---- 标记 ----
            MarkerSystem markers = FindObjectOfType<MarkerSystem>(true);
            if (markers != null) markers.ClearMarkers();

            // ---- 脚印 ----
            FootprintSystem prints = FindObjectOfType<FootprintSystem>(true);
            if (prints != null) prints.ClearFootprints();

            // ---- 石子（地牢模式里没有，但保险起见也清一下）----
            StoneThrowSystem stones = FindObjectOfType<StoneThrowSystem>(true);
            if (stones != null) stones.ClearStones();
        }

        /// <summary>清掉旧地图并按矩阵重建一层。</summary>
        public void RebuildStage()
        {
            CurrentStage++;

            Debug.Log($"[地牢] 生成第 {CurrentStage} 层。");

            ClearStage();
            BuildStage();

            // 生成完立刻全亮（第一次生成时没有淡出）
            m_FadeAlpha = 0f;
            m_FadeIn    = true;
            m_Fading    = true;
        }

        /// <summary>清空上一层的所有生成物。</summary>
        private void ClearStage()
        {
            m_NextStageVolumes.Clear();

            if (m_Root == null) return;

            for (int i = m_Root.childCount - 1; i >= 0; i--)
            {
                Destroy(m_Root.GetChild(i).gameObject);
            }
        }

        // ------------------------------------------------------------------ 建图

        /// <summary>按 <see cref="MapMatrix"/> 生成整张地图。</summary>
        private void BuildStage()
        {
            Vector3 playerSpawn = Vector3.zero;
            bool    hasSpawn    = false;

            // ★ 数字 3 的格子（怪物区地板），建完后交给 MonsterZone
            var monsterCells = new List<Vector2Int>();

            // ---- 地板 / 墙 / 出生点 / 下一层 ----
            for (int x = 0; x < RowsX; x++)
            {
                for (int z = 0; z < ColsZ; z++)
                {
                    int v = MapMatrix[x, z];

                    switch (v)
                    {
                        case 1:   // 地板：上下各一块
                            SpawnCube(x, floorBottomY, z, floorMaterial);
                            SpawnCube(x, floorTopY,    z, floorMaterial);
                            break;

                        case 2:   // 墙：从下到上一整排
                            for (int y = wallBottomY; y <= wallTopY; y++)
                                SpawnCube(x, y, z, wallMaterial);
                            break;

                        case 3:   // ★ 怪物区：地板 + 登记坐标（封锁/怪物潮由 MonsterZone 处理）
                            SpawnCube(x, floorBottomY, z, floorMaterial);
                            SpawnCube(x, floorTopY,    z, floorMaterial);
                            monsterCells.Add(new Vector2Int(x, z));
                            break;

                        case 8:   // 玩家出生点：地板 + 记下坐标
                            SpawnCube(x, floorBottomY, z, floorMaterial);
                            SpawnCube(x, floorTopY,    z, floorMaterial);

                            playerSpawn = new Vector3(x * cellSize, playerSpawnY, z * cellSize);
                            hasSpawn    = true;
                            break;

                        case 9:   // 下一层（终点）：地板 + 触发区
                            SpawnCube(x, floorBottomY, z, floorMaterial);
                            SpawnCube(x, floorTopY,    z, floorMaterial);

                            SpawnNextStage(x, z);
                            break;

                        // 0 = 什么都不放
                    }
                }
            }

            // ---- 玩家：放到格子 8 的位置 ----
            if (hasSpawn)
            {
                // ★ 记住出生点：开局那一刻 GameUI 还可能再调一次 Teleport
                //   （它有自己的时序），所以这里存下来，
                //   由 GameUI 在地牢模式下改调 TeleportPlayerToSpawn()。
                m_SpawnPosition = playerSpawn;
                m_HasSpawn      = true;

                PlacePlayer(playerSpawn);
            }
            else
            {
                Debug.LogWarning("[地牢] 矩阵里没有格子 8（玩家出生点），玩家不会被移动。");
            }

            // ---- 怪物区：把登记到的格子交给 MonsterZone 管理 ----
            if (MonsterZone.Instance != null)
            {
                MonsterZone.Instance.RegisterCells(monsterCells, cellSize);
            }

            // ---- ★★ 重新烘焙 NavMesh ★★ ----
            //
            //   地图是**运行时才建出来**的，而 NavMeshRuntimeBaker.Ensure()
            //   只烘焙一次。如果它在地图生成之前就被调过（比如场景里有
            //   PuzzleMonster，它的 Start 比这里早），烘焙出来的就是空 NavMesh，
            //   之后所有 Agent 都会报
            //   "Failed to create agent because there is no valid NavMesh"。
            //
            //   所以每次建完图都强制重建一次。
            NavMeshRuntimeBaker.Rebuild();
        }

        /// <summary>在 (x, y, z) 放一个立方体（基础 Cube，不是 WallOccluder）。</summary>
        private void SpawnCube(int x, int y, int z, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);

            go.name = $"[生成] Cube ({x},{y},{z})";
            go.transform.SetParent(m_Root, false);
            go.transform.position   = new Vector3(x * cellSize, y * cellSize, z * cellSize);
            go.transform.localScale = Vector3.one * cellSize;

            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                // ★★ 完全透明 ★★
                //
                //   地牢模式靠声波光带感知地形，地板和墙**本来就不该看得见**。
                //   材质留空时用 EchoPuzzle/Invisible —— 那个 shader 直接 discard
                //   所有像素，连深度都不写，所以绝对不会看到任何东西
                //   （比"画成黑色"更彻底：黑色在 Bloom / 后处理下会透出灰）。
                //
                //   注意：Cube 自带 MeshRenderer + BoxCollider，
                //   只是不画而已，碰撞照常生效。
                r.sharedMaterial = mat != null ? mat : InvisibleMaterialOrDefault();
            }
        }

        /// <summary>完全透明材质的共享实例（只建一次）。</summary>
        private static Material s_InvisibleMat;

        /// <summary>取 EchoPuzzle/Invisible 材质。找不到就退化成 URP 的 Unlit 透明材质。</summary>
        private static Material InvisibleMaterialOrDefault()
        {
            if (s_InvisibleMat != null) return s_InvisibleMat;

            // 优先用项目里的资产
            s_InvisibleMat = Resources.Load<Material>("EchoRuntime/EchoInvisible");

#if UNITY_EDITOR
            if (s_InvisibleMat == null)
                s_InvisibleMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/EchoPuzzle/Materials/EchoInvisible.mat");
#endif

            // 退路：现场用 shader 建
            if (s_InvisibleMat == null)
            {
                Shader sh = Shader.Find("EchoPuzzle/Invisible");

                if (sh != null)
                {
                    s_InvisibleMat = new Material(sh)
                    {
                        name      = "DungeonInvisibleRuntime",
                        hideFlags = HideFlags.DontSave,
                    };
                }
            }

            if (s_InvisibleMat == null)
            {
                Debug.LogWarning("[地牢] 找不到 EchoPuzzle/Invisible 材质，墙体可能可见。");
            }

            return s_InvisibleMat;
        }

        /// <summary>在格子 (x, z) 放一个 NextStage 触发区。</summary>
        private void SpawnNextStage(int x, int z)
        {
            var go = new GameObject($"[生成] NextStage ({x},{z})");

            go.transform.SetParent(m_Root, false);
            go.transform.position = new Vector3(x * cellSize, 1f, z * cellSize);

            // 触发盒：覆盖 y=0..6 整层
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size      = new Vector3(cellSize, 6f, cellSize);
            box.center    = new Vector3(0f, 2f, 0f);

            // Hide：和场景里其它物体一样走同一套"启用/删除"机制
            var hide = go.AddComponent<Hide>();
            hide.hiding = 0;

            var tv = go.AddComponent<TriggerVolume>();

            // 设为 NextStage 类别。
            // ★ 光带外观默认与 Win 相同（nextStageSonar 默认 = DefaultWin），
            //   玩家进入时 EmitTriggerSonar 会释放这套光带 + Destination 音效；
            //   实际"淡出 -> 生成下一层 -> 淡入"由 OnNextStage 事件驱动（见上方）。
            tv.effect = TriggerEffect.NextStage;

            // 光带参数：默认用 Win 那一套，Inspector 上可单独调
            tv.sonar = nextStageSonar;

            // ★★ 射入这个触发区的光带，外观强制改成：★★
            //     粗细 = 0.22 米
            //     不透明度 = 1（保持全亮）
            //     颜色 = 纯白
            //
            //   三个 override 默认分别是 false / true / false，
            //   只开 overrideAlpha 的话宽度和颜色还是原样，所以这里三个都开。
            tv.overrideWidth = true;
            tv.waveWidth     = nextStageWaveWidth;

            tv.overrideAlpha = true;
            tv.waveAlpha     = nextStageWaveAlpha;

            tv.overrideColor = true;
            tv.waveColor     = nextStageWaveColor;

            // ★ 光带进入这个触发区时，也在区内产生一排红色竖线
            //   （粗细 / 颜色 / 不透明度直接取上面这几个 tv.waveXxx 的值）
            if (go.GetComponent<SenseSphereVisual>() == null)
                go.AddComponent<SenseSphereVisual>();

            m_NextStageVolumes.Add(tv);
        }

        /// <summary>把玩家瞬移到出生点（并同步相机）。</summary>
        private void PlacePlayer(Vector3 pos)
        {
            if (m_Player == null) m_Player = FindObjectOfType<PlayerController>();
            if (m_Player == null) return;

            m_Player.Teleport(pos, m_Player.transform.eulerAngles.y, m_Player.Pitch);
        }

        // ------------------------------------------------------------------ 黑幕

        private void OnGUI()
        {
            if (!m_Fading && m_FadeAlpha <= 0.001f) return;

            if (m_Black == null) m_Black = MakeBlack();

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(m_FadeAlpha));
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), m_Black);
            GUI.color = prev;
        }

        private static Texture2D MakeBlack()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            tex.SetPixel(0, 0, Color.black);
            tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------------ 编辑器辅助

#if UNITY_EDITOR
        /// <summary>把矩阵打印到 Console（方便检查填得对不对）。</summary>
        [ContextMenu("打印当前矩阵")]
        private void PrintMatrix()
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"[地牢] 当前矩阵（{RowsX} 行 × {ColsZ} 列）：");

            for (int x = 0; x < RowsX; x++)
            {
                sb.Append($"x={x,3}: ");

                for (int z = 0; z < ColsZ; z++)
                    sb.Append(MapMatrix[x, z]);

                sb.AppendLine();
            }

            Debug.Log(sb.ToString());
        }
#endif
    }
}
