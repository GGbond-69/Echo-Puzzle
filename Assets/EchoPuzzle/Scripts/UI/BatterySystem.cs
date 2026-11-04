using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 电池系统。
    ///
    /// ── 规则 ────────────────────────────────────────────────
    ///   · 最高 3 点
    ///   · 开局默认 3 点
    ///   · 有电池时光剑完整；没电池时 BladeMesh 消失
    ///   · 没电池时光刃无法移除怪物
    ///
    /// ── 显示 ────────────────────────────────────────────────
    ///   画面左上角三个圆：
    ///     空心 = 该点电池已消耗
    ///     实心 = 该点电池还在
    ///
    ///   用 IMGUI 画（和 GameUI 同一套），不依赖任何 UI 预制体 / Canvas。
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public sealed class BatterySystem : MonoBehaviour
    {
        public static BatterySystem Instance { get; private set; }

        /// <summary>电池上限。</summary>
        public const int MaxBatteries = 3;

        // ---------------------------------------------------------------- 配置

        [Header("初始值")]
        [Tooltip("开局拥有的电池数量（0 ~ MaxBatteries）")]
        [Range(0, MaxBatteries)] public int startingBatteries = 0;

        [Header("显示")]
        [Tooltip("圆环半径（像素，按 1080 高度等比缩放）")]
        public float dotRadius = 16f;

        [Tooltip("圆环线条粗细（像素）")]
        public float dotThickness = 4f;

        [Tooltip("实心圆颜色")]
        public Color filledColor = new Color(1f, 1f, 1f, 0.95f);

        [Tooltip("空心圆颜色")]
        public Color emptyColor = new Color(1f, 1f, 1f, 0.35f);

        [Tooltip("左上角起始位置（像素，按 1080 高度等比缩放）")]
        public Vector2 screenOrigin = new Vector2(48f, 56f);

        [Tooltip("两个圆之间的水平间距（像素）")]
        public float dotSpacing = 42f;

        // ---------------------------------------------------------------- 状态

        /// <summary>当前电池数。</summary>
        public int Current { get; private set; }

        /// <summary>是否还有电池。</summary>
        public bool HasBattery => Current > 0;

        // ---------------------------------------------------------------- 绘制资源

        private Texture2D m_Dot;
        private GUIStyle  m_LabelStyle;
        private Font      m_Font;

        // ---------------------------------------------------------------- 生命周期

        private void Awake()
        {
            Instance = this;
            Current  = Mathf.Clamp(startingBatteries, 0, MaxBatteries);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (m_Dot != null) Destroy(m_Dot);
        }

        // ---------------------------------------------------------------- 公共接口

        /// <summary>消耗一点电池。没有电池时返回 false。</summary>
        public bool Consume()
        {
            if (Current <= 0) return false;

            Current--;
            return true;
        }

        /// <summary>增加电池（暂时没有补充途径，留给以后用）。</summary>
        public void Add(int n = 1)
        {
            Current = Mathf.Clamp(Current + n, 0, MaxBatteries);
        }

        /// <summary>重置到开局数量（重玩时用）。</summary>
        public void ResetToStart()
        {
            Current = Mathf.Clamp(startingBatteries, 0, MaxBatteries);
        }

        // ---------------------------------------------------------------- 绘制

        private void OnGUI()
        {
            // ★★ 地牢模式（DungeonMode）不显示电池 ★★
            //
            //   地牢模式是独立的肉鸽射击玩法，左上角只显示生命值
            //   （见 Scripts/Dungeon/PlayerHealth.cs + Scripts/Dungeon/DungeonHUD.cs）。
            //   电池是探秘模式（匕首/光刃）的资源，在地牢里没有意义。
            if (GameUI.IsDungeonMode) return;

            // 只在"游戏中 / 胜利"时显示，和 GameUI 的其它 HUD 一致
            if (!GameUI.GameplayInputActive && GameUI.Current != GameState.Playing) return;

            EnsureResources();

            // 和 GameUI 一样：按 1080 高度等比缩放
            float scale  = Screen.height / 1080f;
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                                       new Vector3(scale, scale, 1f));

            float r  = dotRadius;
            float sp = dotSpacing;

            Color prev = GUI.color;

            for (int i = 0; i < MaxBatteries; i++)
            {
                // 从左到右依次表示第 1、2、3 点
                bool filled = i < Current;

                float cx = screenOrigin.x + i * sp;
                float cy = screenOrigin.y;

                GUI.color = filled ? filledColor : emptyColor;

                if (filled)
                {
                    // 实心：画一个半径 r 的圆
                    DrawFilledCircle(cx, cy, r);
                }
                else
                {
                    // 空心：画一圈
                    DrawRing(cx, cy, r, dotThickness);
                }
            }

            GUI.color = prev;
            GUI.matrix = saved;
        }

        /// <summary>用点阵拼一个实心圆。</summary>
        private void DrawFilledCircle(float cx, float cy, float radius)
        {
            float step = 3f;

            for (float y = -radius; y <= radius; y += step)
            {
                // 该行圆的半宽
                float halfW = Mathf.Sqrt(Mathf.Max(0f, radius * radius - y * y));

                if (halfW <= 0.01f) continue;

                GUI.DrawTexture(new Rect(cx - halfW, cy + y, halfW * 2f, step), m_Dot);
            }
        }

        /// <summary>用点阵拼一个圆环。</summary>
        private void DrawRing(float cx, float cy, float radius, float thickness)
        {
            // 沿圆周铺点，点的大小 = thickness
            float inner = Mathf.Max(0.5f, radius - thickness * 0.5f);
            float circumference = 2f * Mathf.PI * inner;

            float dotSize = Mathf.Max(1.2f, thickness);
            int   count   = Mathf.Max(16, Mathf.CeilToInt(circumference / (dotSize * 0.8f)));

            for (int i = 0; i < count; i++)
            {
                float a = (float)i / count * Mathf.PI * 2f;
                float x = cx + Mathf.Cos(a) * inner;
                float y = cy + Mathf.Sin(a) * inner;

                GUI.DrawTexture(new Rect(x - dotSize * 0.5f, y - dotSize * 0.5f,
                                         dotSize, dotSize), m_Dot);
            }
        }

        private void EnsureResources()
        {
            if (m_Dot == null) m_Dot = MakeDot();
        }

        /// <summary>1×1 白色纹理（用 GUI.color 染色）。</summary>
        private static Texture2D MakeDot()
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
