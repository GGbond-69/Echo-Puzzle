using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 碎石区：和 TriggerVolume 一样**没有实体**，但会改变射入其中的声波光带外观。
    ///
    /// ── 进入区域的效果 ──────────────────────────────────────
    ///   · 把玩家的**石头补满**
    ///   · 在区域内投石**不消耗石头**
    ///
    /// ── 光带外观 ────────────────────────────────────────────
    ///   射入本区域的光带会被改成下面的粗细 / 不透明度 / 颜色。
    ///   默认颜色 RGB(175, 75, 0)（暗橙色）。
    ///
    /// ── 潜行加成 ────────────────────────────────────────────
    ///   玩家在本区域内**潜行**时：
    ///     · 声波速度改为 <see cref="sneakSpeedOverride"/>
    ///     · 波前存在时长（Lifetime Cap）改为 <see cref="sneakLifetimeOverride"/>
    ///
    /// ── 实现 ────────────────────────────────────────────────
    ///   外观改写在 <see cref="ZoneSystem.Find"/> 里通过"空间优先级"生效 ——
    ///   和 TriggerVolume 的 sonar 外观走同一套机制（见 Listener 钩子）。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class Gravel : MonoBehaviour
    {
        // ---------------------------------------------------------------- 编辑期边框

        /// <summary>
        /// 编辑期边框颜色（全局，淡绿色）。
        ///
        /// ★ 和 WallOccluder.OutlineColor 一样是静态字段 ——
        ///   进 Play 前在 Scene 视图里能看到这块区域在哪，方便摆地图。
        ///   第 4 个参数是 alpha：越小越淡。
        /// </summary>
        public static Color OutlineColor = new Color(0.4f, 1f, 0.4f, 0.25f);

        [Header("编辑期边框")]
        [Tooltip("在 Scene 视图里用直线画出这个碎石区的轮廓。\n\n" +
                 "★ 只在编辑状态下显示，进 Play 后就没了。\n" +
                 "边框颜色是全局的，见 Gravel.OutlineColor。")]
        public bool showEditorOutline = true;

        [Header("外观（射入的光带会被改成这些值）")]
        [Tooltip("光带颜色。默认 RGB(175, 75, 0)")]
        public Color color = new Color(175f / 255f, 75f / 255f, 0f, 1f);

        [Tooltip("光带宽度（米）")]
        [Min(0.01f)] public float width = 0.20f;

        [Tooltip("光带不透明度 0..1")]
        [Range(0f, 1f)] public float alpha = 1f;

        [Header("潜行加成（玩家在本区域内潜行时生效）")]
        [Tooltip("声波速度覆盖值")]
        [Min(0.1f)] public float sneakSpeedOverride = 8f;

        [Tooltip("波前存在时长（Lifetime Cap）覆盖值")]
        [Min(0.1f)] public float sneakLifetimeOverride = 1f;

        /// <summary>玩家当前是否在这个区域内。</summary>
        public bool PlayerInside { get; private set; }

        /// <summary>玩家是否正**潜行**待在本区域内。</summary>
        public bool SneakingInside =>
            PlayerInside && m_Player != null && m_Player.Mode == MoveMode.Sneak;

        private PlayerController m_Player;
        private BoxCollider m_Box;

        /// <summary>场上所有 Gravel（ZoneSystem 用来做空间判定）。</summary>
        private static readonly List<Gravel> s_All = new List<Gravel>(8);

        public static IReadOnlyList<Gravel> All => s_All;

        // ---------------------------------------------------------------- 生命周期

        private void Awake()
        {
            m_Box = GetComponent<BoxCollider>();
            m_Box.isTrigger = true;
        }

        private void OnEnable()
        {
            if (!s_All.Contains(this)) s_All.Add(this);
        }

        private void OnDisable()
        {
            s_All.Remove(this);
            PlayerInside = false;
        }

        private void Update()
        {
            if (GameUI.Current != GameState.Playing) return;

            if (m_Player == null) m_Player = FindObjectOfType<PlayerController>();
            if (m_Player == null) return;

            bool inside = Contains(m_Player.FootPosition);

            if (inside && !PlayerInside)
            {
                // ★ 进入区域：把石头补满
                PlayerInside = true;

                if (StoneSystem.Instance != null) StoneSystem.Instance.Refill();
            }
            else if (!inside && PlayerInside)
            {
                PlayerInside = false;
            }

            // 同步"在 Gravel 内"标志（投石不消耗用）
            if (StoneSystem.Instance != null)
                StoneSystem.Instance.InGravel = AnyPlayerInside();
        }

        /// <summary>玩家是否在**任意**某个 Gravel 区域内。</summary>
        public static bool AnyPlayerInside()
        {
            for (int i = 0; i < s_All.Count; i++)
            {
                Gravel g = s_All[i];
                if (g != null && g.PlayerInside) return true;
            }

            return false;
        }

        /// <summary>某个世界坐标是否在本区域内。</summary>
        public bool Contains(Vector3 worldPos)
        {
            if (m_Box == null) m_Box = GetComponent<BoxCollider>();
            if (m_Box == null) return false;

            Vector3 local = transform.InverseTransformPoint(worldPos);
            Vector3 c     = m_Box.center;
            Vector3 h     = m_Box.size * 0.5f;

            return Mathf.Abs(local.x - c.x) <= h.x
                && Mathf.Abs(local.y - c.y) <= h.y
                && Mathf.Abs(local.z - c.z) <= h.z;
        }

        /// <summary>以世界坐标表示的范围（给 ZoneSystem 用）。</summary>
        public Bounds WorldBounds
        {
            get
            {
                if (m_Box == null) m_Box = GetComponent<BoxCollider>();

                Vector3 c = transform.TransformPoint(m_Box.center);
                Vector3 s = Vector3.Scale(m_Box.size, transform.lossyScale);

                return new Bounds(c, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
            }
        }

        // ---------------------------------------------------------------- 编辑期边框

        /// <summary>
        /// 在 Scene 视图里画这个区域的线框（12 条棱）。
        ///
        /// 用 BoxCollider 的 center / size 换算到世界坐标，
        /// 所以边框永远和实际判定范围一致。
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!showEditorOutline) return;

            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null) return;

            Gizmos.color = OutlineColor;

            // 局部空间的 8 个角
            Vector3 c = box.center;
            Vector3 h = box.size * 0.5f;

            Vector3[] p = new Vector3[8]
            {
                c + new Vector3(-h.x, -h.y, -h.z),
                c + new Vector3( h.x, -h.y, -h.z),
                c + new Vector3( h.x, -h.y,  h.z),
                c + new Vector3(-h.x, -h.y,  h.z),
                c + new Vector3(-h.x,  h.y, -h.z),
                c + new Vector3( h.x,  h.y, -h.z),
                c + new Vector3( h.x,  h.y,  h.z),
                c + new Vector3(-h.x,  h.y,  h.z),
            };

            Transform tr = transform;
            for (int i = 0; i < 8; i++) p[i] = tr.TransformPoint(p[i]);

            // 底面
            Gizmos.DrawLine(p[0], p[1]); Gizmos.DrawLine(p[1], p[2]);
            Gizmos.DrawLine(p[2], p[3]); Gizmos.DrawLine(p[3], p[0]);

            // 顶面
            Gizmos.DrawLine(p[4], p[5]); Gizmos.DrawLine(p[5], p[6]);
            Gizmos.DrawLine(p[6], p[7]); Gizmos.DrawLine(p[7], p[4]);

            // 竖棱
            Gizmos.DrawLine(p[0], p[4]); Gizmos.DrawLine(p[1], p[5]);
            Gizmos.DrawLine(p[2], p[6]); Gizmos.DrawLine(p[3], p[7]);
        }
    }
}
