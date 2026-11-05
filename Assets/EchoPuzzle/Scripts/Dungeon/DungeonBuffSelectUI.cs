using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle.Dungeon
{
    /// <summary>
    /// 【地牢模式】每通关一层弹出的「三选一」强化界面。
    ///
    /// ── 触发时机 ────────────────────────────────────────────
    ///   监听 <see cref="TriggerVolume.OnNextStage"/>（玩家走进下一层传送点），
    ///   等 <see cref="openDelay"/> 秒（让 DungeonMapGenerator 把新一层生成完、
    ///   淡入结束）再弹出。**不改齐放的地图代码**，只订阅它已有的静态事件。
    ///
    /// ── 版式（3 列大卡片）──────────────────────────────────
    ///   三张等宽卡片横排，几乎铺满整个画面：
    ///     · 左右各留 sideMargin，卡片之间留 columnGap
    ///     · 顶部留出标题和上下边距，其余高度全部给卡片
    ///   卡片只有一层极淡的底色 + 细边框（悬停时底色会亮一点），
    ///   文字纯白居中 —— 和 GameUI 那套"没有底框、只有文字"的风格一致。
    ///
    /// ── 交互 ────────────────────────────────────────────────
    ///   · 打开时 Time.timeScale = 0（世界冻结）、光标解锁
    ///   · 点卡片，或按 1 / 2 / 3 选一项
    ///   · 选完立刻恢复 timeScale、锁定光标
    ///
    /// ── 和探秘模式的关系 ────────────────────────────────────
    ///   ★ 只在 <see cref="GameUI.IsDungeonMode"/> 时工作。
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
        [Tooltip("左右边距")]
        public float sideMargin = 72f;

        [Tooltip("卡片之间的间距")]
        public float columnGap = 28f;

        [Tooltip("卡片顶部起始位置（标题下面）")]
        public float cardsTop = 196f;

        [Tooltip("卡片底部距屏幕底边的距离（留给底部提示）")]
        public float cardsBottom = 132f;

        [Tooltip("标题距顶部的距离")]
        public float titleTop = 96f;

        [Header("字号")]
        public float titleSize  = 40f;
        public float nameSize   = 36f;
        public float descSize   = 21f;
        public float footerSize = 19f;

        [Header("卡片外观")]
        [Tooltip("卡片底色（很淡，只用来划分三列）")]
        public Color cardColor = new Color(1f, 1f, 1f, 0.045f);

        [Tooltip("鼠标悬停时的底色")]
        public Color cardHoverColor = new Color(1f, 1f, 1f, 0.13f);

        [Tooltip("边框颜色")]
        public Color cardBorderColor = new Color(1f, 1f, 1f, 0.22f);

        [Tooltip("边框粗细")]
        [Min(1f)] public float borderThickness = 2f;

        [Header("文字颜色")]
        public Color textColor     = Color.white;
        public Color dimColor      = new Color(1f, 1f, 1f, 0.55f);
        public Color backdropColor = new Color(0f, 0f, 0f, 0.82f);

        private const float RefHeight = 1080f;

        // ------------------------------------------------------------------ 运行时

        /// <summary>界面是否正在显示。</summary>
        public bool IsOpen { get; private set; }

        private readonly List<DungeonBuffDef> m_Options = new List<DungeonBuffDef>(3);

        /// <summary>&gt;= 0 表示正在倒计时，到 0 就弹。</summary>
        private float m_PendingTimer = -1f;

        private float     m_PrevTimeScale = 1f;

        private Texture2D m_Pixel;
        private Texture2D m_CardTex;
        private Texture2D m_CardHoverTex;

        private GUIStyle m_TitleStyle;
        private GUIStyle m_NameStyle;
        private GUIStyle m_DescStyle;
        private GUIStyle m_IndexStyle;
        private GUIStyle m_FooterStyle;
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

            m_Pixel        = MakeSolid(Color.white);
            m_CardTex      = MakeSolid(cardColor);
            m_CardHoverTex = MakeSolid(cardHoverColor);
        }

        private void OnEnable()  => TriggerVolume.OnNextStage += OnNextStage;
        private void OnDisable() => TriggerVolume.OnNextStage -= OnNextStage;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            DestroyTex(m_Pixel);
            DestroyTex(m_CardTex);
            DestroyTex(m_CardHoverTex);
        }

        private static void DestroyTex(Texture2D t)
        {
            if (t != null) Destroy(t);
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

        /// <param name="restoreCursor">是否把光标锁回去（换场景 / 退出时不需要）</param>
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

            Debug.Log($"[地牢] 获得强化：{def.name}（{def.desc.Replace("\n", " ")}）");

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

            // 全屏黑底（多画高一点，防高分屏下有缝）
            Color prev = GUI.color;
            GUI.color = backdropColor;
            GUI.DrawTexture(new Rect(0f, 0f, w, RefHeight * 1.5f), m_Pixel);
            GUI.color = prev;

            // 标题
            GUI.Label(new Rect(0f, titleTop, w, 64f), "选择一项强化", m_TitleStyle);

            // ---- 三列等宽卡片 ----
            int   n       = m_Options.Count;
            float usable  = w - sideMargin * 2f - columnGap * (n - 1);
            float cardW   = usable / Mathf.Max(1, n);
            float cardH   = RefHeight - cardsTop - cardsBottom;
            float cardY   = cardsTop;

            for (int i = 0; i < n; i++)
            {
                DungeonBuffDef d = m_Options[i];

                float cardX = sideMargin + i * (cardW + columnGap);
                var   card  = new Rect(cardX, cardY, cardW, cardH);

                // 1) 整张卡片就是点击区（背景 + 悬停高亮都由这个样式画）
                bool clicked = GUI.Button(card, GUIContent.none, m_ClickArea);

                // 2) 细边框
                DrawBorder(card);

                // 3) 文字（画在按钮之上）
                float pad = Mathf.Max(16f, cardW * 0.07f);

                //    左上角的编号，提示"按数字键也能选"
                GUI.Label(new Rect(cardX + pad, cardY + 20f, 60f, 40f), (i + 1).ToString(), m_IndexStyle);

                //    名称（居中，卡片上部）
                GUI.Label(new Rect(cardX + pad, cardY + cardH * 0.22f, cardW - pad * 2f, 58f),
                          d.name, m_NameStyle);

                //    名称下面一条分隔线
                Color pc = GUI.color;
                GUI.color = cardBorderColor;
                GUI.DrawTexture(new Rect(cardX + pad, cardY + cardH * 0.22f + 66f, cardW - pad * 2f, 1f), m_Pixel);
                GUI.color = pc;

                //    描述（自动换行）
                GUI.Label(new Rect(cardX + pad, cardY + cardH * 0.22f + 92f, cardW - pad * 2f, cardH * 0.36f),
                          d.desc, m_DescStyle);

                //    已拥有层数（卡片底部）
                int stacks = DungeonBuffState.Instance != null
                           ? DungeonBuffState.Instance.StacksOf(d)
                           : 0;

                string stackText = stacks > 0 ? $"已拥有 ×{stacks}（上限 ×{d.maxStacks}）"
                                              : $"还未拥有（上限 ×{d.maxStacks}）";

                GUI.Label(new Rect(cardX + pad, cardY + cardH - 72f, cardW - pad * 2f, 40f),
                          stackText, m_FooterStyle);

                if (clicked) Pick(i);
            }

            // 底部提示
            GUI.Label(new Rect(0f, RefHeight - cardsBottom + 44f, w, 40f),
                      "点击卡片，或按 1 / 2 / 3", m_FooterStyle);

            GUI.matrix = saved;
        }

        /// <summary>画一圈细边框（四条 1×1 拉伸的矩形）。</summary>
        private void DrawBorder(Rect r)
        {
            Color prev = GUI.color;
            GUI.color = cardBorderColor;

            float t = Mathf.Max(1f, borderThickness);

            GUI.DrawTexture(new Rect(r.x,           r.y,            r.width, t),          m_Pixel); // 上
            GUI.DrawTexture(new Rect(r.x,           r.yMax - t,     r.width, t),          m_Pixel); // 下
            GUI.DrawTexture(new Rect(r.x,           r.y,            t,       r.height),   m_Pixel); // 左
            GUI.DrawTexture(new Rect(r.xMax - t,    r.y,            t,       r.height),   m_Pixel); // 右

            GUI.color = prev;
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

            m_NameStyle = new GUIStyle(GUI.skin.label)
            {
                font      = f,
                fontSize  = Mathf.RoundToInt(nameSize),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            m_NameStyle.normal.textColor = textColor;

            m_DescStyle = new GUIStyle(GUI.skin.label)
            {
                font      = f,
                fontSize  = Mathf.RoundToInt(descSize),
                alignment = TextAnchor.UpperCenter,
                wordWrap  = true,
                richText  = false,
            };
            m_DescStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);

            m_IndexStyle = new GUIStyle(GUI.skin.label)
            {
                font      = f,
                fontSize  = Mathf.RoundToInt(footerSize),
                alignment = TextAnchor.UpperLeft,
            };
            m_IndexStyle.normal.textColor = dimColor;

            m_FooterStyle = new GUIStyle(GUI.skin.label)
            {
                font      = f,
                fontSize  = Mathf.RoundToInt(footerSize),
                alignment = TextAnchor.MiddleCenter,
                wordWrap  = true,
            };
            m_FooterStyle.normal.textColor = dimColor;

            // 卡片点击区：只负责底色和悬停，不画字
            m_ClickArea = new GUIStyle(GUI.skin.button)
            {
                border  = new RectOffset(0, 0, 0, 0),
                margin  = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
            };

            m_ClickArea.normal.background   = m_CardTex;
            m_ClickArea.hover.background    = m_CardHoverTex;
            m_ClickArea.active.background   = m_CardHoverTex;
            m_ClickArea.focused.background  = m_CardTex;
            m_ClickArea.onNormal.background = m_CardTex;
            m_ClickArea.onHover.background  = m_CardHoverTex;
            m_ClickArea.onActive.background = m_CardHoverTex;
        }

        /// <summary>生成一张 1×1 纯色纹理（带 alpha），不依赖任何贴图资源。</summary>
        private static Texture2D MakeSolid(Color c)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            tex.SetPixel(0, 0, c);
            tex.Apply();
            return tex;
        }
    }
}