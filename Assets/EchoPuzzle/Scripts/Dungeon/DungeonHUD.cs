using UnityEngine;

namespace EchoPuzzle.Dungeon
{
    /// <summary>
    /// 【地牢模式】左上角的生命值 HUD：六个爱心。
    ///
    /// ── 表现 ────────────────────────────────────────────────
    ///   · 满血 = 实心红心，掉血 = 空心红心
    ///   · 心和心之间等距排列，位置 / 大小和探秘模式的电池一致
    ///   · 只在「游戏中」显示（和 BatterySystem / StoneSystem 一个口径）
    ///
    /// ── 和探秘模式的关系 ────────────────────────────────────
    ///   ★ 完全独立的组件，只在 DungeonMode 场景里使用。
    ///     探秘模式的左上角仍是电池 + 石头，不受影响。
    ///     （那两套 HUD 已经在地牢模式下自动不画了。）
    ///
    /// ── 依赖 ────────────────────────────────────────────────
    ///   读 <see cref="PlayerHealth"/> 的血量。找不到就不画。
    /// </summary>
    [AddComponentMenu("Echo Puzzle/Dungeon/Dungeon HUD")]
    public sealed class DungeonHUD : MonoBehaviour
    {
        // ------------------------------------------------------------------ 外观

        [Header("位置")]
        [Tooltip("第一颗爱心的位置（左上角，按 1080 高度基准的像素）")]
        public Vector2 screenOrigin = new Vector2(48f, 56f);

        [Tooltip("爱心之间的水平间距")]
        public float heartSpacing = 42f;

        [Tooltip("爱心大小（半宽，像素）")]
        public float heartRadius = 14f;

        [Header("颜色")]
        [Tooltip("满血时的爱心颜色")]
        public Color filledColor = new Color(1f, 0.28f, 0.32f, 1f);

        [Tooltip("掉血后的空心颜色")]
        public Color emptyColor = new Color(1f, 0.28f, 0.32f, 0.35f);

        /// <summary>参考高度：所有尺寸都按它等比缩放。</summary>
        private const float RefHeight = 1080f;

        // ------------------------------------------------------------------ 运行时

        private PlayerHealth m_Health;
        private Texture2D    m_Pixel;

        private void Awake()
        {
            m_Pixel = MakePixel();
        }

        private void OnDestroy()
        {
            if (m_Pixel != null) Destroy(m_Pixel);
        }

        private void Update()
        {
            if (m_Health == null)
                m_Health = FindObjectOfType<PlayerHealth>();
        }

        // ------------------------------------------------------------------ 绘制

        private void OnGUI()
        {
            // ★ 只有地牢模式才画
            if (!GameUI.IsDungeonMode) return;

            // 和探秘模式的电池 / 石头一个口径：只在游戏中 / 胜利时显示
            if (!GameUI.GameplayInputActive && GameUI.Current != GameState.Playing) return;

            if (m_Health == null) return;

            // 和 GameUI 一样：按 1080 高度等比缩放
            float scale = Screen.height / RefHeight;
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                                       new Vector3(scale, scale, 1f));

            int   max    = Mathf.Max(1, m_Health.maxHealth);
            int   cur    = m_Health.Current;
            float r      = heartRadius;
            float sp     = heartSpacing;

            Color prev = GUI.color;

            for (int i = 0; i < max; i++)
            {
                // 从左到右依次表示第 1、2、3...颗心
                bool filled = i < cur;

                float cx = screenOrigin.x + i * sp;
                float cy = screenOrigin.y;

                GUI.color = filled ? filledColor : emptyColor;

                DrawHeart(cx, cy, r, filled);
            }

            GUI.color = prev;
            GUI.matrix = saved;
        }

        /// <summary>
        /// 画一颗爱心。
        ///
        /// 用"上半两个圆 + 下半一个三角"拼出来：
        ///   · filled = true  -> 实心
        ///   · filled = false -> 只画轮廓（内缩后再画一圈实心，挖空）
        ///
        /// 全部用 1×1 白色纹理 + GUI.color 染色，不依赖任何贴图资源。
        /// </summary>
        private void DrawHeart(float cx, float cy, float r, bool filled)
        {
            if (m_Pixel == null) return;

            // 实心心形的"点阵"参数
            float step = Mathf.Max(1.5f, r * 0.14f);

            // 心形由两部分合成：
            //   上半：两个半径为 r*0.5 的圆，圆心分别在 (±r*0.5, -r*0.25)
            //   下半：一个向下收窄的三角，从 y = -r*0.25 到 y = r

            float lobeR = r * 0.52f;
            float lobeY = cy - r * 0.22f;      // 两个圆的圆心（屏幕坐标 y 向下）
            float lobeDx = r * 0.48f;

            float tipY   = cy + r * 0.95f;     // 心尖
            float bodyTopY = lobeY;            // 三角形从这里往下收

            for (float y = cy - r; y <= tipY; y += step)
            {
                // 这一行的左右边界
                float lo, hi;

                if (y <= bodyTopY)
                {
                    // ---- 上半：两个圆 ----
                    // 左圆
                    float dy = y - lobeY;
                    float halfL = Mathf.Sqrt(Mathf.Max(0f, lobeR * lobeR - dy * dy));
                    lo = cx - lobeDx - halfL;
                    hi = cx + lobeDx + halfL;
                }
                else
                {
                    // ---- 下半：三角形向下收窄 ----
                    float t = Mathf.InverseLerp(bodyTopY, tipY, y);   // 0 -> 1

                    // 三角形顶部半宽 = 两圆外缘
                    float topHalf = lobeDx + r * 0.62f;
                    float halfW   = Mathf.Lerp(topHalf, 0f, t);

                    lo = cx - halfW;
                    hi = cx + halfW;
                }

                if (hi - lo <= 0.01f) continue;

                if (filled)
                {
                    GUI.DrawTexture(new Rect(lo, y, hi - lo, step), m_Pixel);
                }
                else
                {
                    // 空心：只画左右两条边
                    float t = Mathf.Max(1.5f, r * 0.13f);      // 线宽

                    GUI.DrawTexture(new Rect(lo, y, t, step), m_Pixel);
                    GUI.DrawTexture(new Rect(hi - t, y, t, step), m_Pixel);
                }
            }
        }

        /// <summary>1×1 白色纹理（用 GUI.color 染色）。</summary>
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
