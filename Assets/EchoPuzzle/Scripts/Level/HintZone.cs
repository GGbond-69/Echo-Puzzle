using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>一段提示文字的切换方式。</summary>
    public enum HintTriggerKind
    {
        EnterZone = 0,   // 进入区域时立刻显示（通常只有第一重用它）
        Space     = 1,   // 玩家按下空格
        Look      = 2,   // 玩家转动视角
        Zoom      = 3,   // 玩家滚轮缩放
        Move      = 4,   // 玩家移动角色
        Marker    = 5,   // 玩家按 E 放下标记
        AutoTime  = 6,   // 经过一段时间后自动切换
    }

    /// <summary>文本栈里的一重文字。</summary>
    [System.Serializable]
    public sealed class HintEntry
    {
        [TextArea(2, 5)]
        public string text = "";

        [Tooltip("这一重文字是怎么被切换出来的")]
        public HintTriggerKind trigger = HintTriggerKind.Space;

        [Tooltip("trigger = AutoTime 时用：进入这一重之后等多少秒自动切到下一重\n" +
                 "其他 trigger 时用：检测到操作后等多少秒才切换")]
        [Min(0f)] public float delay = 1f;
    }

    /// <summary>
    /// 提示空间的文字显示区。
    ///
    /// **文本栈**：每一重文字自带一个"切换方式"。玩家进入区域显示第一重，
    /// 满足某一重的条件就切到下一重，前一段文字被替换掉。可以反复响应。
    ///
    /// 基本行为：玩家**进入**显示、**离开**隐藏（没有时长概念）。
    /// 文字显示 / 隐藏都有淡入淡出（时长在 GameUI 上配）。
    ///
    /// 新建的重会排到**最底层**（数组末尾），也就是最后才被切换出来；
    /// 删除删的也是**最底层**那一重。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class HintZone : MonoBehaviour
    {
        [Header("文本栈（从上往下依次切换）")]
        [Tooltip("每一重文字配一个切换方式。第一重建议用 EnterZone。\n" +
                 "新加的会排到最底层 —— 也就是最后才被切换出来。")]
        public List<HintEntry> entries = new List<HintEntry>();

        [Header("重复触发")]
        [Tooltip("勾上 = 整局只触发一次，走完文本栈就不再响应\n" +
                 "取消勾选 = 玩家再次进入时从头开始（重新显示第一重）")]
        public bool triggerOnce = false;

        [Header("判定")]
        [Tooltip("除了碰撞触发，还额外用玩家脚底位置做范围判定（更可靠）")]
        public bool useBoundsCheck = true;

        [Header("显示样式")]
        [Tooltip("这块区域文字的字号。0 = 用 GameUI 上的默认字号")]
        [Min(0)] public int textSize = 40;

        [Tooltip("这块区域文字的颜色。alpha = 0 时用 GameUI 上的默认颜色")]
        public Color textColor = new Color(1f, 1f, 1f, 1f);

        // ------------------------------------------------------------------ 运行时

        /// <summary>玩家现在在不在这个区域里。</summary>
        public bool PlayerInside { get; private set; }

        /// <summary>本局是否已经触发过（配合 triggerOnce）。</summary>
        public bool Consumed { get; private set; }

        /// <summary>当前显示的是第几重（-1 = 还没开始）。</summary>
        public int CurrentIndex { get; private set; } = -1;

        /// <summary>当前该显示的文字。</summary>
        public string CurrentText { get; private set; } = "";

        /// <summary>
        /// 当前正在显示文字的那块提示区。
        ///
        /// 这是**唯一的事实来源** —— ZoneTrigger 每帧读它来刷新显示，
        /// 所以不会出现"全局文字被清掉之后没人补回来"的情况。
        /// 同时有多块区域重叠时，后进入的那块会接管。
        /// </summary>
        public static HintZone ActiveZone { get; private set; }

        private float m_ActionPending;  // > 0 表示正在等延迟
        private int   m_PendingIndex = -1;

        public Bounds WorldBounds
        {
            get
            {
                var col = GetComponent<BoxCollider>();
                if (col == null) return new Bounds(transform.position, Vector3.zero);

                Vector3 c = transform.TransformPoint(col.center);
                Vector3 s = Vector3.Scale(col.size, Abs(transform.lossyScale));
                return new Bounds(c, s);
            }
        }

        private static Vector3 Abs(Vector3 v)
            => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private void Reset()
        {
            var col = GetComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = Vector3.one;
        }

        private PlayerController m_Player;

        private void Awake()
        {
            var col = GetComponent<BoxCollider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnEnable()  => ZoneSystem.RegisterHint(this);
        private void OnDisable() => ZoneSystem.UnregisterHint(this);

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null) return;
            OnPlayerEnter();
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null) return;
            OnPlayerExit();
        }

        private void Update()
        {
            if (!Application.isPlaying) return;
            if (GameUI.Current != GameState.Playing) return;

            if (useBoundsCheck)
            {
                // 玩家全场只有一个，缓存起来 —— 不用每帧全场景扫一遍找它
                if (m_Player == null) m_Player = FindObjectOfType<PlayerController>();
                var player = m_Player;

                if (player != null)
                {
                    bool inside = WorldBounds.Contains(player.FootPosition);

                    // 脚底点只负责"补进入"（防瞬移 / 漏检）。
                    // 退出只由 OnTriggerExit 判定：胶囊体表面比脚底点早约 0.34m 碰到区域，
                    // 若拿脚底点判"离开"，进入瞬间会误触发 OnPlayerExit，
                    // 把 triggerOnce 刚显示的文字清掉（表现为勾了 triggerOnce 反而看不到字）。
                    if (inside && !PlayerInside) OnPlayerEnter();
                }
            }

            if (!PlayerInside) return;
            if (entries == null || entries.Count == 0) return;

            // ★ triggerOnce 用完了：不再接管文字（否则会把残留文字一直推出去）
            if (triggerOnce && Consumed) return;

            // 玩家在区域内，这块就是当前显示的那块。
            // 每帧重申一次，是为了兜住"ActiveZone 被复位 / 被别的区域抢走"的情况。
            ActiveZone = this;

            // 还没显示第一重（比如靠 useBoundsCheck 判定进来、或者刚被复位）—— 立刻补上
            if (CurrentIndex < 0)
            {
                AdvanceTo(0);
                return;
            }

            float now = Time.timeSinceLevelLoad;

            // ---- 已经在等延迟：倒计时到点就切 ----
            if (m_ActionPending > 0f)
            {
                m_ActionPending -= Time.deltaTime;

                if (m_ActionPending <= 0f)
                {
                    m_ActionPending = 0f;
                    AdvanceTo(m_PendingIndex);
                }

                return;
            }

            // 已经到最后一重，没有"下一重"了
            if (CurrentIndex >= entries.Count - 1) return;

            // ---- 看下一重的切换条件满足了没 ----
            HintEntry next = entries[CurrentIndex + 1];
            if (next == null) return;

            if (next.trigger == HintTriggerKind.AutoTime)
            {
                // 自动切换：从"进入这一重"开始计时，到点直接切
                if (now - m_TextSetTime >= next.delay) AdvanceTo(CurrentIndex + 1);
                return;
            }

            // 需要玩家操作：问 PlayerController 最近做了什么。
            // 用缓存，别每帧全场景找一次。
            if (next.trigger != HintTriggerKind.Marker)
            {
                PlayerAction want = TriggerToAction(next.trigger);
                if (want == PlayerAction.None) return;

                if (m_Player == null) m_Player = FindObjectOfType<PlayerController>();
                PlayerController pc = m_Player;
                if (pc == null) return;

                // 必须是"切到当前这一重之后"发生的操作才算数
                if (pc.LastAction != want) return;
                if (pc.LastActionTime <= m_TextSetTime) return;
            }
            else
            {
                bool pressed =
                    Input.GetKeyDown(KeyCode.Alpha1) ||
                    Input.GetKeyDown(KeyCode.Alpha2) ||
                    Input.GetKeyDown(KeyCode.Alpha3) ||
                    Input.GetKeyDown(KeyCode.Alpha4) ||
                    Input.GetKeyDown(KeyCode.Alpha5);
                if (!pressed) return;
                if (Time.timeSinceLevelLoad <= m_TextSetTime) return;
            }

            if (next.delay <= 0f) AdvanceTo(CurrentIndex + 1);
            else
            {
                m_ActionPending = next.delay;
                m_PendingIndex  = CurrentIndex + 1;
            }
        }

        // ------------------------------------------------------------------ 状态推进

        private float m_TextSetTime;

        private void AdvanceTo(int index)
        {
            if (entries == null || index < 0 || index >= entries.Count) return;

            HintEntry e = entries[index];
            if (e == null) return;

            CurrentIndex  = index;
            CurrentText   = e.text ?? "";
            m_TextSetTime = Time.timeSinceLevelLoad;

            ZoneTrigger.SetActiveHint(CurrentText, textSize, textColor);
        }

        private static PlayerAction TriggerToAction(HintTriggerKind kind)
        {
            switch (kind)
            {
                case HintTriggerKind.Space:  return PlayerAction.Space;
                case HintTriggerKind.Look:   return PlayerAction.Look;
                case HintTriggerKind.Zoom:   return PlayerAction.Zoom;
                case HintTriggerKind.Move:   return PlayerAction.Move;
                case HintTriggerKind.Marker: return PlayerAction.Marker;
                default:                     return PlayerAction.None;
            }
        }

        // ------------------------------------------------------------------ 进出

        private void OnPlayerEnter()
        {
            // ★ 重入保护。
            //
            //   一次"走进区域"会触发两条路径：
            //     1) 碰撞体回调 OnTriggerEnter
            //     2) Update 里的 useBoundsCheck 范围判定
            //   它们都会调到这里，于是每次进入都会跑两遍。
            //
            //   跑两遍的后果很严重：
            //     · triggerOnce=false 时，第二遍会把 CurrentIndex 重置成 -1、
            //       又从第一重重新开始（白跑一遍，还会打断正在进行的切换）
            //     · triggerOnce=true 时更糟：第一遍设好文字后 Consumed 已经是 true，
            //       第二遍立刻走"阻止显示"分支把文字抹掉 ——
            //       表现就是"勾了 triggerOnce 反而一个字都看不到"
            if (PlayerInside) return;

            PlayerInside = true;

            // triggerOnce 且已经用完：这块区域彻底不再显示任何东西
            if (triggerOnce && Consumed)
            {
                if (ActiveZone == this)
                {
                    ActiveZone = null;
                    ZoneTrigger.ClearActiveHint();
                }

                CurrentText = "";
                return;
            }

            // 接管文字显示
            ActiveZone = this;

            m_ActionPending = 0f;
            m_PendingIndex  = -1;

            // 重新进入 -> 从第一重重新开始
            CurrentIndex = -1;
            AdvanceTo(0);

            // 注意：这里**不**置 Consumed。triggerOnce 的"消耗"推迟到离开时（OnPlayerExit）：
            // 若在首次进入就置 true，Update 里 `if (triggerOnce && Consumed) return;`
            // 会把文本栈推进逻辑整个挡死 —— 第一重能显示，但后续切换全部失效。
        }

        private void OnPlayerExit()
        {
            PlayerInside    = false;
            m_ActionPending = 0f;
            m_PendingIndex  = -1;

            // triggerOnce 在"离开"这一刻消耗：首次进入期间 Consumed 保持 false，
            // 文本栈才能正常推进；离开后再进入，OnPlayerEnter 会用这个标志拦下。
            if (triggerOnce)
            {
                Consumed = true;

                // ★ 需求：把本区域的 Hide.hiding 改成 1（"删除"），
                //   而不是只靠 Consumed 标志在逻辑上拦住。
                //
                //   这样它就和场景里其它"删除"的物体走同一套机制，
                //   重开游戏时也能被快照正确恢复。
                Hide h = GetComponent<Hide>();
                if (h != null) h.SetHiding(1);
            }

            // 离开就隐藏（没有时长，靠进出控制）
            CurrentText = "";

            if (ActiveZone == this)
            {
                ActiveZone = null;
                ZoneTrigger.ClearActiveHint();
            }
        }

        private void OnDestroy()
        {
            if (ActiveZone == this) ActiveZone = null;
        }

        /// <summary>重玩时复位。</summary>
        public void ResetHint()
        {
            if (ActiveZone == this) ActiveZone = null;

            Consumed        = false;
            PlayerInside    = false;
            CurrentIndex    = -1;
            CurrentText     = "";
            m_ActionPending = 0f;
            m_PendingIndex  = -1;
            m_TextSetTime   = 0f;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Bounds b = WorldBounds;
            Gizmos.color = new Color(0.35f, 0.9f, 1f, 0.18f);
            Gizmos.DrawCube(b.center, b.size);
            Gizmos.color = new Color(0.35f, 0.9f, 1f, 0.85f);
            Gizmos.DrawWireCube(b.center, b.size);
        }
#endif
    }
}
