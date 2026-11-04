using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>特殊空间的类型。</summary>
    public enum ZoneKind
    {
        /// <summary>提示空间（蓝）：声波光带无变化；玩家进入时右下角显示提示文字。</summary>
        Hint = 0,

        /// <summary>机关空间（黄）：声波光带变黄、粗细和不透明度不变；玩家进入后消失并触发效果。</summary>
        Mechanism = 1,

        /// <summary>陷阱空间（红）：声波光带变红、粗细和不透明度不变；玩家进入即死亡。</summary>
        Trap = 2,

        /// <summary>终点空间（绿）：声波光带的粗细和不透明度都变成固定值。</summary>
        Goal = 3,
    }

    /// <summary>
    /// 一块特殊空间。和终点空间一样是轴对齐的立方体体积。
    ///
    /// 声波光带落到里面时的表现：
    ///   Hint      —— 什么都不变（只是给玩家一个"这里会出提示"的心理准备）
    ///   Mechanism —— 只把颜色换成 <see cref="waveColor"/>，粗细/不透明度不动
    ///   Trap      —— 只把颜色换成 <see cref="waveColor"/>，粗细/不透明度不动
    ///   Goal      —— 粗细和不透明度都换成 <see cref="goalWidth"/> / <see cref="goalAlpha"/>
    /// </summary>
    public sealed class SpecialZone : MonoBehaviour
    {
        [Header("类型与体积")]
        public ZoneKind kind = ZoneKind.Hint;

        [Tooltip("区域中心（世界坐标）")]
        public Vector3 center = Vector3.zero;

        [Tooltip("区域尺寸（米）")]
        public Vector3 size = new Vector3(4f, 4f, 4f);

        [Header("声波光带的表现")]
        [Tooltip("光带在这个区域里的颜色。Hint 类型忽略这一项")]
        public Color waveColor = Color.white;

        [Tooltip("Goal 类型专用：区域内光带的固定宽度（米）")]
        public float goalWidth = 0.22f;

        [Tooltip("Goal 类型专用：区域内光带的固定不透明度")]
        [Range(0f, 1f)] public float goalAlpha = 1f;

        [Header("机关 / 提示")]
        [Tooltip("机关空间触发后要消失的物体（比如机关门、陷阱空间）")]
        public GameObject[] disableOnTrigger;

        [Tooltip("提示空间要显示的文字。暂时留空即可，文本框会自动留出来")]
        [TextArea] public string hintText = "";

        [Tooltip("是否已经被触发过（机关空间触发一次就消失）")]
        public bool consumed;

        // ⚠ 性能关键：原来这里是 `=> new Bounds(center, size)` 这种**属性**写法，
        //    每次访问都构造一个新的 Bounds 结构体。而 ZoneSystem.Find 会对
        //    每块空间调用它一次 —— 铺光带时每步都要调，于是变成每帧几万次
        //    属性访问 + 结构体构造。（Profiler 实测：光带渲染占了 85% 帧时间。）
        //
        //    改成缓存字段后，访问就是一次字段读取。数据变化时由 RecacheBounds() 刷新。
        private Bounds m_CachedBounds;
        private bool   m_BoundsReady;

        /// <summary>世界包围盒（内部有缓存，别在每帧路径上重建）。</summary>
        public Bounds WorldBounds
        {
            get
            {
                if (!m_BoundsReady) RecacheBounds();
                return m_CachedBounds;
            }
        }

        /// <summary>重新计算缓存的包围盒。改了 center / size / 位置之后要调一次。</summary>
        public void RecacheBounds()
        {
            m_CachedBounds = new Bounds(center, size);
            m_BoundsReady  = true;
        }

        public bool Contains(Vector3 worldPos) => WorldBounds.Contains(worldPos);

        /// <summary>
        /// 超廉价的预筛：只比较中心距离和半尺寸，不做任何结构体构造。
        /// 用来在正式做 Contains 之前快速排除掉绝大多数空间。
        /// </summary>
        public bool QuickReject(Vector3 worldPos)
        {
            // 直接读缓存字段，绕开 getter —— 这是每步都要走的热路径
            if (!m_BoundsReady) RecacheBounds();

            Vector3 c = m_CachedBounds.center;
            Vector3 e = m_CachedBounds.extents;

            float dx = worldPos.x - c.x; if (dx < 0f) dx = -dx;
            float dy = worldPos.y - c.y; if (dy < 0f) dy = -dy;
            float dz = worldPos.z - c.z; if (dz < 0f) dz = -dz;

            return dx > e.x || dy > e.y || dz > e.z;
        }

        private void Awake()    => RecacheBounds();
        private void OnEnable() { RecacheBounds(); ZoneSystem.Register(this); }
        private void OnDisable() => ZoneSystem.Unregister(this);

#if UNITY_EDITOR
        private void OnValidate() => RecacheBounds();

        private void OnDrawGizmosSelected() => DrawGizmo(0.55f);
        private void OnDrawGizmos()
        {
            if (Application.isPlaying) DrawGizmo(0.10f);
        }

        private void DrawGizmo(float alpha)
        {
            Gizmos.color = GizmoColor(alpha);
            Gizmos.DrawCube(center, size);
            Gizmos.color = GizmoColor(Mathf.Min(1f, alpha + 0.3f));
            Gizmos.DrawWireCube(center, size);
        }

        private Color GizmoColor(float a)
        {
            switch (kind)
            {
                case ZoneKind.Mechanism: return new Color(1f, 0.75f, 0f, a);
                case ZoneKind.Trap:      return new Color(1f, 0.15f, 0.15f, a);
                case ZoneKind.Goal:      return new Color(0.2f, 0.9f, 0.25f, a);
                default:                 return new Color(0.15f, 0.85f, 0.95f, a);
            }
        }
#endif
    }

    /// <summary>
    /// 所有特殊空间的登记处。光带渲染器和玩家都通过它查询"某个位置落在哪种空间里"。
    /// 数量很少（十几块），所以直接线性遍历，不需要空间划分。
    /// </summary>
    public static class ZoneSystem
    {
        private static readonly List<SpecialZone>   s_Zones    = new List<SpecialZone>(16);
        private static readonly List<TriggerVolume> s_Triggers = new List<TriggerVolume>(16);
        private static readonly List<HintZone>      s_Hints    = new List<HintZone>(8);

        // ---------------------------------------------------------------- 提示空间

        public static void RegisterHint(HintZone h)
        {
            if (h != null && !s_Hints.Contains(h)) s_Hints.Add(h);
        }

        public static void UnregisterHint(HintZone h)
        {
            s_Hints.Remove(h);
        }

        /// <summary>
        /// 把所有提示空间复位（重玩时用）。
        ///
        /// ★ 不能只遍历 s_Hints 注册列表。
        ///
        ///   为什么：勾了 triggerOnce 的 HintZone 在玩家离开时会把 hiding 改成 1，
        ///   从而 SetActive(false) —— 这会触发 OnDisable -> UnregisterHint，
        ///   把它从 s_Hints 里移掉。于是重开时这个列表里根本没有它，
        ///   Consumed 永远停在 true，再进去就什么都看不到。
        ///
        ///   所以这里直接扫场景里**所有** HintZone（含未激活的），
        ///   保证已经"删除"的那些也能被复位。
        /// </summary>
        public static void ResetAllHints()
        {
            // 先处理注册列表里的（快）
            for (int i = 0; i < s_Hints.Count; i++)
                if (s_Hints[i] != null) s_Hints[i].ResetHint();

            // ★ 再补扫场景里所有 HintZone —— 包括因 hiding = 1 而失活、
            //   已经从注册列表里掉出去的那些。
            HintZone[] all = Resources.FindObjectsOfTypeAll<HintZone>();

            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            for (int i = 0; i < all.Length; i++)
            {
                HintZone h = all[i];
                if (h == null) continue;

                GameObject go = h.gameObject;
                if (go.scene != activeScene) continue;                     // 排除 Prefab 资产
                if ((go.hideFlags & HideFlags.DontSave) != 0) continue;

                if (s_Hints.Contains(h)) continue;                        // 上面已经复位过了

                h.ResetHint();
            }
        }

        // ---------------------------------------------------------------- 触发立方体

        public static void RegisterTrigger(TriggerVolume v)
        {
            if (v != null && !s_Triggers.Contains(v)) s_Triggers.Add(v);
        }

        public static void UnregisterTrigger(TriggerVolume v)
        {
            s_Triggers.Remove(v);
        }

        /// <summary>
        /// 当前场上的怪物（缓存，避免每帧 FindObjectsOfType）。
        /// 怪物在 OnEnable / OnDisable 里自己登记。
        /// </summary>
        private static readonly List<PuzzleMonster> s_Monsters = new List<PuzzleMonster>(4);

        public static void RegisterMonster(PuzzleMonster m)
        {
            if (m != null && !s_Monsters.Contains(m)) s_Monsters.Add(m);
        }

        public static void UnregisterMonster(PuzzleMonster m)
        {
            s_Monsters.Remove(m);
        }

        /// <summary>
        /// 找出**变色球包含 worldPos** 的怪物。
        ///
        /// 声波光带进入怪物周围那一圈球形空间时，外观要变得和陷阱一样 ——
        /// 这里就是那个判定。和 TriggerVolume 的立方体判定并行存在。
        ///
        /// ★ 光带采样里每步都会调它，所以绝不能在这里 FindObjectsOfType
        ///   （那是 O(场景物体数) + 每次分配数组）。改用登记好的列表。
        ///   没有怪物时列表为空，这个函数立刻返回 —— 开销接近零。
        /// </summary>
        public static PuzzleMonster FindMonster(Vector3 worldPos)
        {
            for (int i = 0; i < s_Monsters.Count; i++)
            {
                PuzzleMonster m = s_Monsters[i];
                if (m == null) continue;
                if (!m.isActiveAndEnabled) continue;
                if (m.SenseContains(worldPos)) return m;
            }
            return null;
        }

        /// <summary>某个位置落在哪个触发立方体里（用于声波外观）。没命中返回 null。</summary>
        public static TriggerVolume FindTrigger(Vector3 worldPos)
        {
            for (int i = 0; i < s_Triggers.Count; i++)
            {
                TriggerVolume v = s_Triggers[i];
                if (v == null) continue;

                // 先廉价预筛，再做需要查组件的判断
                if (v.QuickReject(worldPos)) continue;
                if (!v.isActiveAndEnabled) continue;
                if (!v.Contains(worldPos)) continue;

                return v;
            }
            return null;
        }

        /// <summary>
        /// 某个位置落在哪个碎石区（Gravel）里（用于声波外观）。没命中返回 null。
        ///
        /// ★ 光带段会**逐段**调用它；碎石区通常只有几块，线性扫一遍代价可以忽略。
        /// </summary>
        public static Gravel FindGravel(Vector3 worldPos)
        {
            var all = Gravel.All;

            for (int i = 0; i < all.Count; i++)
            {
                Gravel g = all[i];
                if (g == null) continue;
                if (!g.isActiveAndEnabled) continue;

                if (!g.Contains(worldPos)) continue;

                return g;
            }

            return null;
        }

        public static void ResetAllTriggers()
        {
            for (int i = 0; i < s_Triggers.Count; i++)
                if (s_Triggers[i] != null) s_Triggers[i].ResetTrigger();
        }

        /// <summary>包含 <paramref name="worldPos"/> 的空间。没命中返回 null。</summary>
        /// <param name="kind">命中的空间类型。</param>
        /// <param name="priority">
        /// 同一个位置被多块空间覆盖时，用这个值决定谁优先（数值大的赢）。
        /// 终点 > 机关 > 陷阱 > 提示。
        /// </param>
        public static SpecialZone Find(Vector3 worldPos)
        {
            SpecialZone best = null;
            int bestPrio = int.MinValue;

            for (int i = 0; i < s_Zones.Count; i++)
            {
                SpecialZone z = s_Zones[i];
                if (z == null) continue;

                // 先做超廉价的预筛（纯浮点比较），再做需要查组件的判断。
                // 顺序很重要：QuickReject 不碰任何 Unity 属性，
                // 绝大多数空间在这一步就被排除，省下 isActiveAndEnabled 的开销。
                if (z.QuickReject(worldPos)) continue;

                if (!z.isActiveAndEnabled || z.consumed) continue;
                if (!z.Contains(worldPos)) continue;

                int prio = Priority(z.kind);
                if (prio > bestPrio) { bestPrio = prio; best = z; }
            }

            return best;
        }

        /// <summary>玩家当前所在的提示空间（用于右下角文字）。不在任何提示空间里就返回 null。</summary>
        public static SpecialZone FindHint(Vector3 worldPos)
        {
            for (int i = 0; i < s_Zones.Count; i++)
            {
                SpecialZone z = s_Zones[i];
                if (z == null || !z.isActiveAndEnabled || z.consumed) continue;
                if (z.kind != ZoneKind.Hint) continue;
                if (z.WorldBounds.Contains(worldPos)) return z;
            }
            return null;
        }

        private static int Priority(ZoneKind k)
        {
            switch (k)
            {
                case ZoneKind.Goal:      return 3;
                case ZoneKind.Mechanism: return 2;
                case ZoneKind.Trap:      return 1;
                default:                 return 0;
            }
        }

        public static void Register(SpecialZone z)
        {
            if (z != null && !s_Zones.Contains(z)) s_Zones.Add(z);
        }

        public static void Unregister(SpecialZone z)
        {
            s_Zones.Remove(z);
        }
    }
}
