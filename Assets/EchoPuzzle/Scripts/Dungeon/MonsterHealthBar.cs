using UnityEngine;

namespace EchoPuzzle.Dungeon
{
    /// <summary>
    /// 【地牢模式】怪物头顶的血条。
    ///
    /// ══════════════════════════════════════════════════════════════
    /// 需求
    ///   · 显示在怪物"上方"—— 不是世界坐标的 +Y，而是**相机视角的上方**
    ///     （所以用屏幕空间画：把怪物位置投影到屏幕，再往上挪一段像素）
    ///   · 离镜头越远，血条**越短**
    ///   · 是一道槽：受伤后**从右侧开始**变空心
    ///   · 没有分隔符 / 格子 / 数字
    ///
    /// ══════════════════════════════════════════════════════════════
    /// 实现
    ///   用 IMGUI 在 OnGUI 里画（和 HUD 一致，不占渲染管线的开销）。
    ///   每帧：
    ///     1) WorldToScreenPoint 拿到怪物在屏幕上的位置
    ///     2) 按"相机距离"算出血条长度（越远越短）
    ///     3) 在怪物屏幕位置的**上方**画一条槽 + 红色的血量部分
    ///
    ///   ★ 只在地牢模式、并且怪物还活着时显示。
    /// ══════════════════════════════════════════════════════════════
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Echo Puzzle/Dungeon/Monster Health Bar")]
    public sealed class MonsterHealthBar : MonoBehaviour
    {
        // ---------------------------------------------------------------- 外观

        [Header("尺寸")]
        [Tooltip("离相机最近时的血条长度（屏幕像素，按 1080 高换算）")]
        [Min(4f)] public float fullLengthPixels = 110f;

        [Tooltip("离相机最远时的血条长度（屏幕像素）。不会小于这个值")]
        [Min(2f)] public float minLengthPixels = 18f;

        [Tooltip("血条厚度（屏幕像素）")]
        [Min(1f)] public float thicknessPixels = 8f;

        [Tooltip("血条底边在怪物屏幕位置上方多少像素")]
        public float offsetPixels = 42f;

        [Header("距离缩放")]
        [Tooltip("相机距离 <= 这个值时血条最长")]
        [Min(0.1f)] public float nearDistance = 6f;

        [Tooltip("相机距离 >= 这个值时血条最短")]
        [Min(0.2f)] public float farDistance = 40f;

        [Header("颜色")]
        [Tooltip("血量部分的颜色（红色）")]
        public Color fillColor = new Color(0.95f, 0.15f, 0.18f, 1f);

        [Tooltip("空槽的背景色")]
        public Color emptyColor = new Color(0f, 0f, 0f, 0.55f);

        [Tooltip("外描边颜色")]
        public Color borderColor = new Color(1f, 1f, 1f, 0.35f);

        [Tooltip("描边宽度（像素）")]
        [Min(0f)] public float borderPixels = 1f;

        [Header("显隐")]
        [Tooltip("血量变化后到开始淡出之前，保持完全可见的时长（秒）")]
        [Min(0f)] public float holdSeconds = 3f;

        [Tooltip("淡入时长（秒）")]
        [Min(0.01f)] public float fadeInSeconds = 0.5f;

        [Tooltip("淡出时长（秒）")]
        [Min(0.01f)] public float fadeOutSeconds = 0.5f;

        // ---------------------------------------------------------------- 运行时

        private PuzzleMonster m_Monster;
        private Camera         m_Cam;

        private Texture2D m_White;

        // ---- 显隐状态 ----
        //
        //   平时 alpha = 0（不可见）；血量变化后从 0 淡入到 1，
        //   保持 holdSeconds 秒，再用 fadeOutSeconds 淡出到 0。
        private float m_Alpha;

        /// <summary>本次"可见窗口"开始淡出的时刻。0 = 还没开始。</summary>
        private float m_HoldUntil;
        private bool  m_VisibleWindow;

        /// <summary>上次已知的血量，用来检测"发生变化"。</summary>
        private int m_LastHealth = -1;

        // ---------------------------------------------------------------- 生命周期

        private void Awake()
        {
            m_Monster = GetComponent<PuzzleMonster>();
            if (m_Monster == null) m_Monster = GetComponentInParent<PuzzleMonster>();

            m_White = MakeWhite();
        }

        private void OnDestroy()
        {
            if (m_White != null) Destroy(m_White);
        }

        // ---------------------------------------------------------------- 绘制

        // ---------------------------------------------------------------- 临时测试

        // ★★ 临时测试键：按 H 让**场上所有怪物**各扣 4 血 ★★
        //
        //   只在地牢模式、且游戏正在进行中才响应（暂停 / 结算时按无效）。
        //
        //   ★ 子弹机制做好之后请把 HandleTestInput() 和这两个常量删掉。

        [Header("临时测试")]
        [Tooltip("勾掉可禁用测试按键")]
        public bool enableTestKeys = true;

        /// <summary>测试用伤害键。</summary>
        public KeyCode testDamageKey = KeyCode.H;

        /// <summary>测试用伤害值。</summary>
        public int testDamageAmount = 4;

        /// <summary>
        /// 处理临时测试按键。**场上有几只怪就调几次会重复扣血**，
        /// 所以这里用一个静态标志保证"一次按键只处理一次"。
        /// </summary>
        private void HandleTestInput()
        {
            if (!enableTestKeys) return;
            if (!GameUI.IsDungeonMode) return;

            // 只在真正的"进行中"响应
            if (GameUI.Current != GameState.Playing) return;

            // 正在输密码时不抢键
            if (GameUI.PasswordInputActive) return;

            if (!Input.GetKeyDown(testDamageKey)) return;

            // ★ 一次按键只处理一次
            //   （场上有 N 只怪，N 个组件都会跑到这里，用帧号去重）
            if (s_LastHandledFrame == Time.frameCount) return;
            s_LastHandledFrame = Time.frameCount;

            int amount = Mathf.Max(1, testDamageAmount);

            PuzzleMonster[] all = FindObjectsOfType<PuzzleMonster>();

            int hurt = 0;

            for (int i = 0; i < all.Length; i++)
            {
                PuzzleMonster m = all[i];

                if (m == null) continue;
                if (m.DungeonIsDead) continue;

                Hide h = m.GetComponent<Hide>();
                if (h != null && h.hiding != 0) continue;

                m.DungeonTakeDamage(amount);
                hurt++;
            }

            Debug.Log($"[临时测试] 按 {testDamageKey}：对 {hurt} 只怪物各造成 {amount} 点伤害。");
        }

        /// <summary>上次处理测试按键的帧号（防止 N 个怪物组件重复执行）。</summary>
        private static int s_LastHandledFrame = -1;

        private void Update()
        {
            if (!GameUI.IsDungeonMode) return;

            // ---- 临时测试按键 ----
            HandleTestInput();

            if (m_Monster == null) return;

            float now = Time.unscaledTime;

            // ---- 1) 检测血量变化 -> 开启可见窗口 ----
            int cur = m_Monster.DungeonHealth;

            if (m_LastHealth < 0)
            {
                // 第一次运行：记下初始值，不触发显示
                m_LastHealth = cur;
            }
            else if (cur != m_LastHealth)
            {
                m_LastHealth = cur;

                // 血变了（增加或减少都算）-> 重新开始"淡入 + 保持 + 淡出"
                m_VisibleWindow = true;
                m_HoldUntil     = now + Mathf.Max(0f, holdSeconds) + Mathf.Max(0.01f, fadeInSeconds);
            }

            // ---- 2) 推进 alpha ----
            //
            //   窗口内（淡入 + 保持）目标 = 1，之后目标 = 0。
            //   用 MoveTowards 按各自的时长线性趋近，
            //   自然就得到"0.5s 淡入 -> 3s 保持 -> 0.5s 淡出"。
            bool inWindow = m_VisibleWindow && now < m_HoldUntil;

            float target = inWindow ? 1f : 0f;

            float speed = target > m_Alpha
                ? 1f / Mathf.Max(0.01f, fadeInSeconds)
                : 1f / Mathf.Max(0.01f, fadeOutSeconds);

            m_Alpha = Mathf.MoveTowards(m_Alpha, target, speed * Time.unscaledDeltaTime);

            if (m_VisibleWindow && !inWindow)
                m_VisibleWindow = false;
        }

        private void OnGUI()
        {
            if (!GameUI.IsDungeonMode) return;
            if (m_Monster == null) return;

            // ★ 暂停 / 结算 / 首页时**不显示**
            if (GameUI.Current != GameState.Playing) return;

            // 完全透明 -> 不画（平时就是这个状态）
            if (m_Alpha <= 0.001f) return;

            // 死了 / 被隐藏 -> 不画
            if (m_Monster.DungeonIsDead) return;

            Hide h = m_Monster.GetComponent<Hide>();
            if (h != null && h.hiding != 0) return;

            if (m_Cam == null) m_Cam = Camera.main;
            if (m_Cam == null) return;

            // ---- 1) 怪物在屏幕上的位置 ----
            Vector3 monsterPos = m_Monster.transform.position;

            Vector3 sp = m_Cam.WorldToScreenPoint(monsterPos);

            // 在相机后面 -> 不画
            if (sp.z <= 0f) return;

            // 屏幕外（留一点余量）-> 不画
            if (sp.x < -80f || sp.x > Screen.width  + 80f) return;
            if (sp.y < -80f || sp.y > Screen.height + 80f) return;

            // ---- 2) 按相机距离算缩放 ----
            float dist = Vector3.Distance(m_Cam.transform.position, monsterPos);

            float t = Mathf.InverseLerp(
                Mathf.Max(0.1f, nearDistance),
                Mathf.Max(nearDistance + 0.1f, farDistance),
                dist);

            t = Mathf.Clamp01(t);

            // ★ 离镜头越远 -> 血条越短
            float len   = Mathf.Lerp(fullLengthPixels, minLengthPixels, t);
            float thick = Mathf.Lerp(thicknessPixels, Mathf.Max(1f, thicknessPixels * 0.45f), t);
            float offY  = Mathf.Lerp(offsetPixels,    Mathf.Max(6f, offsetPixels * 0.4f),   t);

            // ---- 3) 屏幕上"相机视角的上方" ----
            //
            //   ★ 直接用屏幕像素往上挪，而不是用世界空间 +Y ——
            //     俯视时 +Y 投影到屏幕几乎不动，看不出"在怪物上方"。
            //     IMGUI 的 y 轴向下，所以往上 = y 减小。
            float cx = sp.x;
            float cy = (Screen.height - sp.y) - offY;

            DrawBar(cx, cy, len, thick);
        }

        /// <summary>画一条血条：外描边 + 空槽 + 左侧的红色血量。</summary>
        private void DrawBar(float cx, float cy, float len, float thick)
        {
            if (m_White == null) m_White = MakeWhite();

            float halfLen = len * 0.5f;

            float left = cx - halfLen;
            float top  = cy - thick * 0.5f;

            // ★ 整体透明度（平时 0 = 完全看不见）
            float a = Mathf.Clamp01(m_Alpha);

            // ---- 外描边 ----
            if (borderPixels > 0f)
            {
                Color prev = GUI.color;

                Color bc = borderColor;
                bc.a *= a;
                GUI.color = bc;

                GUI.DrawTexture(new Rect(left - borderPixels,
                                         top  - borderPixels,
                                         len  + borderPixels * 2f,
                                         thick + borderPixels * 2f), m_White);

                GUI.color = prev;
            }

            // ---- 空槽 ----
            {
                Color prev = GUI.color;

                Color ec = emptyColor;
                ec.a *= a;
                GUI.color = ec;

                GUI.DrawTexture(new Rect(left, top, len, thick), m_White);

                GUI.color = prev;
            }

            // ---- 血量：从左侧开始，受伤后**从右侧**收 ----
            float ratio = m_Monster.DungeonHealth01;

            if (ratio > 0f)
            {
                Color prev = GUI.color;

                Color fc = fillColor;
                fc.a *= a;
                GUI.color = fc;

                GUI.DrawTexture(new Rect(left, top, len * ratio, thick), m_White);

                GUI.color = prev;
            }
        }

        // ---------------------------------------------------------------- 工具

        private static Texture2D MakeWhite()
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
