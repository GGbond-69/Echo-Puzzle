using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 检测玩家进出了哪些特殊空间：
    ///   陷阱空间  -> 立刻死亡
    ///   机关空间  -> 触发效果，然后这块空间永久消失
    ///   提示空间  -> 只记录，交给 GameUI 在右下角显示文字
    ///
    /// 判定点用**脚底**：胶囊底部在 Z=0，所以贴地 1m 高的陷阱空间一定碰得到。
    /// </summary>
    public sealed class ZoneTrigger : MonoBehaviour
    {
        [Tooltip("留空会自动找场景里的 PlayerController")]
        public PlayerController player;

        /// <summary>玩家当前是否在某个提示区里。文字内容由 HintZone 通过 ActiveHintText 提供。</summary>
        public static bool HintVisible => !string.IsNullOrEmpty(s_ActiveHint);

        // ---------------------------------------------------------------- 文字提示
        //
        // 两类来源：
        //   1) HintZone 组件（拖进场景的提示区）—— 进入就显示、离开就隐藏，没有时长概念。
        //      它通过 SetActiveHint / ClearActiveHint 直接设置。
        //   2) TriggerVolume 的 ShowHint 效果 —— 带时长，过期自动消失。
        //
        // 两者同时存在时以 HintZone 为准（它代表"玩家正站在提示区里"）。

        private static string s_ActiveHint = "";   // HintZone 设的，长期有效

        // HintZone 可以单独指定字号 / 颜色。0 表示"用 GameUI 上的默认值"。
        private static int   s_ActiveHintSize;
        private static Color s_ActiveHintColor = new Color(0f, 0f, 0f, 0f);

        private static string s_TimedText = "";    // TriggerVolume 推的，带时长
        private static float  s_TimedUntil;

        /// <summary>当前应该显示在右下角的提示文字（没有就返回空串）。</summary>
        public static string ActiveHintText
        {
            get
            {
                if (!string.IsNullOrEmpty(s_ActiveHint)) return s_ActiveHint;
                return Time.timeSinceLevelLoad < s_TimedUntil ? s_TimedText : "";
            }
        }

        /// <summary>当前提示文字要用的字号（0 = 用 GameUI 的默认值）。</summary>
        public static int ActiveHintSize => s_ActiveHintSize;

        /// <summary>当前提示文字要用的颜色（alpha = 0 = 用 GameUI 的默认值）。</summary>
        public static Color ActiveHintColor => s_ActiveHintColor;

        /// <summary>
        /// 每帧自检：把当前提示区该显示的文字**重新推一遍**。
        ///
        /// 为什么需要"每帧重推"：文字原本是 HintZone 在切换时推一次、
        /// 之后就不再管。可这个状态是全局静态的，任何一次 ClearActiveHint
        /// （另一块区域离开、复位等）都会把它抹掉 —— 抹掉之后就再也没人补回来，
        /// 表现就是"明明站在提示区里却一个字都不显示"（勾了 triggerOnce 时尤其容易踩）。
        ///
        /// 改成每帧以 <see cref="HintZone.ActiveZone"/> 为准重新推，
        /// 状态就不可能卡死在"该显示但没有文字"上。
        /// </summary>
        public static void RefreshHint()
        {
            HintZone zone = HintZone.ActiveZone;

            if (zone == null)
            {
                // ★ 只清文字，**不动 size / color**。
                //   淡出要持续零点几秒，这期间 GameUI 还得知道该用多大字号 ——
                //   之前这里把 size 一起清成 0，淡出时字就会突然缩回默认大小。
                s_ActiveHint = "";
                return;
            }

            s_ActiveHint      = zone.CurrentText ?? "";
            s_ActiveHintSize  = zone.textSize;
            s_ActiveHintColor = zone.textColor;
        }

        /// <summary>HintZone 进入区域 / 切换文字时调用。</summary>
        public static void SetActiveHint(string text, int size = 0, Color color = default)
        {
            s_ActiveHint      = text ?? "";
            s_ActiveHintSize  = size;
            s_ActiveHintColor = color;
        }

        /// <summary>HintZone 离开区域时调用。</summary>
        public static void ClearActiveHint()
        {
            s_ActiveHint      = "";
            s_ActiveHintSize  = 0;
            s_ActiveHintColor = new Color(0f, 0f, 0f, 0f);
        }

        /// <summary>推一条**带时长**的文字提示（TriggerVolume 用这个）。</summary>
        public static void PushHint(string text, float duration)
        {
            if (string.IsNullOrEmpty(text)) return;

            s_TimedText  = text;
            s_TimedUntil = Time.timeSinceLevelLoad + Mathf.Max(0.5f, duration);
        }

        /// <summary>清掉所有文字提示。</summary>
        public static void ClearHint()
        {
            s_ActiveHint      = "";
            s_ActiveHintSize  = 0;
            s_ActiveHintColor = new Color(0f, 0f, 0f, 0f);
            s_TimedText       = "";
            s_TimedUntil      = 0f;
        }

        /// <summary>最近一次触发的机关空间，可用于播放音效 / 特效。</summary>
        public EventHandlerTrigger LastMechanism;

        public delegate void EventHandlerTrigger(SpecialZone zone);

        private void Awake()
        {
            if (player == null) player = FindObjectOfType<PlayerController>();
        }

        private void OnDisable()
        {
            ClearHint();
        }

        private void Update()
        {
            // 每帧把提示文字重推一遍，避免状态卡死在"该显示但没文字"
            RefreshHint();

            if (player == null) return;
            if (GameUI.Current != GameState.Playing) return;

            Vector3 foot = player.FootPosition;
            SpecialZone zone = ZoneSystem.Find(foot);
            if (zone == null) return;

            switch (zone.kind)
            {
                case ZoneKind.Trap:
                    // 踩到陷阱：死亡
                    GameUI.NotifyPlayerDied();
                    break;

                case ZoneKind.Mechanism:
                    Trigger(zone);
                    break;

                // 提示文字现在由 HintZone 组件自己管（它有自己的进出判定和淡入淡出），
                // 所以这里不用管 ZoneKind.Hint
            }
        }

        /// <summary>
        /// 设置一个物体"被机关关闭 / 打开"的状态。
        ///
        /// ★ 一律走 <see cref="Hide.hiding"/>，**不直接 SetActive**。
        ///
        ///   物体的启用状态只应该由 hiding 决定。如果这里绕过它去 SetActive，
        ///   之后重开时 WorldSnapshot.RestoreStart() 就算把 hiding 改对了，
        ///   这个错误的 activeSelf 也会一直留着 ——
        ///   表现就是"hiding 恢复了，但物体没有重新启用"。
        /// </summary>
        private static void SetTargetActive(GameObject target, bool active)
        {
            if (target == null) return;

            Hide h = target.GetComponent<Hide>();

            if (h != null) h.SetHiding(active ? 0 : 1);
            else           target.SetActive(active);      // 没挂 Hide 时退化
        }

        /// <summary>触发一个机关空间：关掉它关联的物体，然后让这块空间永久消失。</summary>
        private void Trigger(SpecialZone zone)
        {
            if (zone.consumed) return;

            zone.consumed = true;

            if (zone.disableOnTrigger != null)
            {
                for (int i = 0; i < zone.disableOnTrigger.Length; i++)
                {
                    SetTargetActive(zone.disableOnTrigger[i], false);
                }
            }

            LastMechanism?.Invoke(zone);
        }

        /// <summary>重玩时调用：恢复所有机关空间与触发立方体，并重新启用它们关掉的东西。</summary>
        public static void ResetAllMechanisms()
        {
            SpecialZone[] zones = FindObjectsOfType<SpecialZone>(true);

            for (int i = 0; i < zones.Length; i++)
            {
                SpecialZone z = zones[i];
                if (z == null) continue;

                if (z.kind == ZoneKind.Mechanism)
                {
                    // 先把关联物体打开，再把空间恢复成未触发
                    if (z.disableOnTrigger != null)
                    {
                        for (int k = 0; k < z.disableOnTrigger.Length; k++)
                        {
                            SetTargetActive(z.disableOnTrigger[k], true);
                        }
                    }

                    z.consumed = false;
                }
            }

            // 触发立方体也一并复位。
            // 按名字改动过的东西由 TriggerVolume 自己按"原始状态"还原，
            // 所以这里不需要（也不该）在这里猜测该开还是该关。
            TriggerVolume[] vols = FindObjectsOfType<TriggerVolume>(true);
            for (int i = 0; i < vols.Length; i++)
            {
                TriggerVolume v = vols[i];
                if (v == null) continue;

                v.ResetTrigger();
            }

            ClearHint();

            // 提示空间复位（重新进入时重新显示最初那段文字）
            ZoneSystem.ResetAllHints();
        }
    }
}
