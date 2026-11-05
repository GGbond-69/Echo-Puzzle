using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle.Dungeon
{
    /// <summary>
    /// 【地牢模式】每通关一层弹出的「三选一」强化的界面（最小模板）。
    ///
    /// ── 触发时机 ────────────────────────────────────────────
    ///   监听 <see cref="TriggerVolume.OnNextStage"/>（玩家走进下一层传送点），
    ///   等 <see cref="openDelay"/> 秒（让 DungeonMapGenerator 把新一层生成完、
    ///   淡入结束）再弹出。**不改齐放的地图代码**，只订阅它已有的静态事件。
    ///
    /// ── 交互 ────────────────────────────────────────────────
    ///   · 打开时 Time.timeScale = 0（世界冻结）、光标解锁
    ///   · 点卡片，或按 1 / 2 / 3 选一项
    ///   · 选完立刻恢复 timeScale、锁定光标
    ///
    /// ── 版式 ────────────────────────────────────────────────
    ///   和 GameUI / DungeonHUD 完全同一套：IMGUI + 按 1080 高度等比缩放。
    ///   全黑背景、纯白文字、没有底框和图标。
    ///
    /// ── 和探秘模式的关系 ────────────────────────────────────
    ///   ★ 只在 <see cref="GameUI.IsDungeonMode"/> 时工作；
    ///     探秘模式的 TriggerVolume 触发 NextStage 时这里直接返回。
    /// </summary>
    [AddComponentMenu("Echo Puzzle/Dungeon/Dungeon Buff Select UI")]
    public sealed class DungeonBuffSelectUI : MonoBehaviour
    {
        public static DungeonBuffSelectUI Instance { get; private set; }

        // ------------------------------------------------------------------ 参数

        [Header("触发")]
        [Tooltip("走进下一层后等多久再弹（等地图生成 + 淡入）。秒")]
        [Min(0f)] public float openDelay = 1.2f;

        [Tooltip("一次给几个选项")]
        [Min(1)] public int optionCount = 3;

        // ------------------------------------------------------------------ 版式

        [Header("版式（1080 高度基准的像素）")]
        public float titleSize  = 34f;
        public float optionSize = 24f;
        public float descSize   = 18f;

        [Tooltip("卡片宽度")]
        public float cardWidth = 620f;

        [Tooltip("卡片高度")]
        public float cardHeight = 96f;

        [Tooltip("卡片之间的竖直间距")]
        public float cardSpacing = 18f;

        [Tooltip("标题距屏幕顶部的距离")]
        public float titleTop = 200f;

        [Header("颜色")]
        public Color textColor     = Color.white;
        public Color dimColor      = new Color(1f, 1f, 1f, 0.55f);
        public Color backdropColor = new Color(0f, 0f, 0f, 0.78f);

        private const float RefHeight = 1080f;

        // ------------------------------------------------------------------ 运行时

        /// <summary>界面是否正在显示。</summary>
        public bool IsOpen { get; private set; }

        private readonly List<DungeonBuffDef> m_Options = new List<DungeonBuffDef>(3);

        /// <summary>&gt;= 0 表示正在倒计时，到 0 就弹。</summary>
        private float m_PendingTimer = -1f;

        private float     m_PrevTimeScale = 1f;
        private Texture2D m_Pixel;

        private GUIStyle m_TitleStyle;
        private GUIStyle m_OptionStyle;
        private GUIStyle m_DescStyle;
        private GUIStyle m_ClickArea;

        // ------------------------------------------------------------------ 生命周期

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            m_Pixel  = MakePixel();
        }

        private void OnEnable()  => TriggerVolume.OnNextStage += OnNextStage;
        private void OnDisable() => TriggerVolume.OnNextStage -= OnNextStage;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (m_Pixel != null) Destroy(m_Pixel);
        }

        // ------------------------------------------------------------------ 触发

        private void OnNextStage()
        {
            // 只在地牢模式里给 buff
            if (!GameUI.IsDungeonMode) return;

            m_PendingTimer = Mathf.Max(0f, openDelay);
        }

        private void Update()
        {
            if (!GameUI.IsDungeonMode)
            {
                if (IsOpen) Close(false);
                return;
            }

            // ---- 倒计时：等新一层生成完再弹 ----
            if (!IsOpen && m_PendingTimer >= 0f)
            {
                m_PendingTimer -= Time.unscaledDeltaTime;

                if (m_PendingTimer <= 0f)
                {
                    m_PendingTimer = -1f;
                    Open();
                }
            }

            if (!IsOpen) return;

            // ---- 数字键快速选择 ----
            if (Input.GetKeyDown(KeyCode.Alpha1)) Pick(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Pick(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) Pick(2);
        }

        // ------------------------------------------------------------------ 开关

        private void Open()
        {
            if (IsOpen) return;

            // 正在结算 / 暂停时不弹（等回到游戏中再说）
            if (GameUI.Current != GameState.Playing) return;

            m_Options.Clear();
            m_Options.AddRange(DungeonBuffPool.Roll(Mathf.Max(1, optionCount), DungeonBuffState.Instance));

            // 池子里的 buff 全叠满了 -> 这一层就没有强化可选
            if (m_Options.Count == 0)
            {
                Debug.Log("[地牢] 所有 buff 都已叠满，本层不给强化。");
                return;
            }

            IsOpen = true;

            m_PrevTimeScale = Time.timeScale;
            Time.timeScale  = 0f;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        /// <param name="restoreCursor">是否把光标锁回去（重开 / 换场景时不需要）</param>
        private void Close(bool restoreCursor = true)
        {
            if (!IsOpen) return;

            IsOpen = false;
            m_Options.Clear();

            // 之前如果是暂停状态（0），别把它当成「恢复成 0」再冻住
            Time.timeScale = m_PrevTimeScale <= 0.0001f ? 1f : m_PrevTimeScale;

            if (restoreCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible   = false;
            }
        }

        private void Pick(int index)
        {
            if (!IsOpen) return;
            if (index < 0 || index >= m_Options.Count) return;

            DungeonBuffDef def = m_Options[index];

            if (DungeonBuffState.Instance != null)
                DungeonBuffState.Instance.Add(def);

            Debug.Log($"[地牢] 获得强化：{def.name}（{def.desc}）");

            Close();
        }

        // ------------------------------------------------------------------ 绘制

        private void OnGUI()
        {
            if (!IsOpen) return;

            EnsureStyles();

            float scale = Screen.height / RefHeight;
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                                       new Vector3(scale, scale, 1f));

            float w = Screen.width / scale;

            // 全屏黑底
            Color prev = GUI.color;
            GUI.color = backdropColor;
            GUI.DrawTexture(new Rect(0f, 0f, w, RefHeight * 1.5f), m_Pixel);
            GUI.color = prev;

            // 标题
            GUI.Label(new Rect(0f, titleTop, w, 60f), "选择一项强化", m_TitleStyle);

            // 卡片
            float x = (w - cardWidth) * 0.5f;
            float y = titleTop + 96f;

            for (int i = 0; i < m_Options.Count; i++)
            {
                DungeonBuffDef d = m_Options[i];

                var card = new Rect(x, y, cardWidth, cardHeight);

                GUI.Label(new Rect(card.x + 24f, card.y + 10f, card.width - 48f, 34f),
                          $"{i + 1}. {d.name}", m_OptionStyle);

                GUI.Label(new Rect(card.x + 24f, card.y + 50f, card.width - 48f, 30f),
                          d.desc, m_DescStyle);

                // 整张卡片都是点击区（样式透明，所以只画出上面的文字）
                if (GUI.Button(card, GUIContent.none, m_ClickArea)) Pick(i);

                y += cardHeight + cardSpacing;
            }

            // 底部提示
            GUI.Label(new Rect(0f, y + 16f, w, 30f), "点击卡片，或按 1 / 2 / 3", m_DescStyle);

            GUI.matrix = saved;
        }

        private void EnsureStyles()
        {
            if (m_TitleStyle != null) return;

            Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");

            m_TitleStyle = new GUIStyle(GUI.skin.label)
            {
                font      = f,
                fontSize  = Mathf.RoundToInt(titleSize),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            m_TitleStyle.normal.textColor = textColor;

            m_OptionStyle = new GUIStyle(GUI.skin.label)
            {
                font      = f,
                fontSize  = Mathf.RoundToInt(optionSize),
                alignment = TextAnchor.MiddleLeft,
            };
            m_OptionStyle.normal.textColor = textColor;

            m_DescStyle = new GUIStyle(GUI.skin.label)
            {
                font      = f,
                fontSize  = Mathf.RoundToInt(descSize),
                alignment = TextAnchor.MiddleCenter,
            };
            m_DescStyle.normal.textColor = dimColor;

            // 透明点击区：所有状态都去掉底图，只用来收点击
            m_ClickArea = new GUIStyle(GUI.skin.button);
            m_ClickArea.normal.background   = null;
            m_ClickArea.hover.background    = null;
            m_ClickArea.active.background   = null;
            m_ClickArea.focused.background  = null;
            m_ClickArea.onNormal.background = null;
            m_ClickArea.onHover.background  = null;
            m_ClickArea.onActive.background = null;
        }

        /// <summary>1×1 白色纹理（用 GUI.color 染色），和其他 HUD 一样不依赖贴图资源。</summary>
        private static Texture2D MakePixel()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return tex;
        }
    }
}