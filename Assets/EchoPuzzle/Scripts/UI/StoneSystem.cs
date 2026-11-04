using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 石头资源：最多 3 个，显示在左上角电池下方。
    ///
    /// ── 外观 ────────────────────────────────────────────────
    ///   三个**正方形**：有石头 = 实心方块，没有 = 空心方框。
    ///
    /// ── 规则 ────────────────────────────────────────────────
    ///   · 每次投石消耗 1 个
    ///   · 耗尽后不可投掷
    ///   · 站在 <see cref="Gravel"/> 区域内时：
    ///       - 进入区域即补满
    ///       - 在区域内投石**不消耗**
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public sealed class StoneSystem : MonoBehaviour
    {
        public static StoneSystem Instance { get; private set; }

        /// <summary>石头上限。</summary>
        public const int MaxStones = 3;

        [Header("数量")]
        [Tooltip("开局拥有的石头数量")]
        [Range(0, MaxStones)] public int startingStones = 0;

        [Header("外观")]
        [Tooltip("方块的半边长（像素，按 1080 高度基准）")]
        public float squareRadius = 14f;

        [Tooltip("空心方框的线宽")]
        public float squareThickness = 4f;

        [Tooltip("有石头时的颜色")]
        public Color filledColor = new Color(1f, 1f, 1f, 0.95f);

        [Tooltip("没石头时的颜色")]
        public Color emptyColor = new Color(1f, 1f, 1f, 0.35f);

        [Tooltip("绘制位置（左上角，1080 基准像素）。默认在电池下面")]
        public Vector2 screenOrigin = new Vector2(48f, 110f);

        [Tooltip("两个方块之间的间距")]
        public float squareSpacing = 42f;

        /// <summary>当前石头数量。</summary>
        public int Current { get; private set; }

        /// <summary>还有石头吗。</summary>
        public bool HasStone => Current > 0;

        /// <summary>
        /// 当前是否在某个 Gravel 区域内（在区域内投石不消耗）。
        /// 由 <see cref="Gravel"/> 每帧更新。
        /// </summary>
        public bool InGravel { get; set; }

        private Texture2D m_Dot;

        // ---------------------------------------------------------------- 生命周期

        private void Awake()
        {
            Instance = this;

            Current = Mathf.Clamp(startingStones, 0, MaxStones);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (m_Dot != null) Destroy(m_Dot);
        }

        // ---------------------------------------------------------------- 接口

        /// <summary>消耗一个石头。成功返回 true。</summary>
        public bool Consume()
        {
            // ★ 在 Gravel 区域内投石不消耗
            if (InGravel) return true;

            if (Current <= 0) return false;

            Current--;
            return true;
        }

        /// <summary>补充石头（不会超过上限）。</summary>
        public void Add(int n = 1)
        {
            Current = Mathf.Clamp(Current + n, 0, MaxStones);
        }

        /// <summary>补满。</summary>
        public void Refill() => Current = MaxStones;

        /// <summary>回到开局数量（重开时用）。</summary>
        public void ResetToStart()
        {
            Current  = Mathf.Clamp(startingStones, 0, MaxStones);
            InGravel = false;
        }

        // ---------------------------------------------------------------- 绘制

        private void OnGUI()
        {
            // ★★ 地牢模式（DungeonMode）不显示石头 ★★
            //
            //   地牢模式没有投石（武器是枪），左上角只显示生命值
            //   （见 Scripts/Dungeon/PlayerHealth.cs + Scripts/Dungeon/DungeonHUD.cs）。
            if (GameUI.IsDungeonMode) return;

            if (!GameUI.GameplayInputActive && GameUI.Current != GameState.Playing) return;

            if (m_Dot == null) m_Dot = MakeDot();

            // 和电池一样：按 1080 高度等比缩放
            float scale = Screen.height / 1080f;
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                                       new Vector3(scale, scale, 1f));

            float r  = squareRadius;
            float sp = squareSpacing;

            Color prev = GUI.color;

            for (int i = 0; i < MaxStones; i++)
            {
                bool filled = i < Current;

                float cx = screenOrigin.x + i * sp;
                float cy = screenOrigin.y;

                GUI.color = filled ? filledColor : emptyColor;

                if (filled)
                {
                    // 实心方块
                    GUI.DrawTexture(new Rect(cx - r, cy - r, r * 2f, r * 2f), m_Dot);
                }
                else
                {
                    // 空心方框：四条边
                    float t = Mathf.Max(1f, squareThickness);
                    float size = r * 2f;

                    // 上
                    GUI.DrawTexture(new Rect(cx - r, cy - r, size, t), m_Dot);
                    // 下
                    GUI.DrawTexture(new Rect(cx - r, cy + r - t, size, t), m_Dot);
                    // 左
                    GUI.DrawTexture(new Rect(cx - r, cy - r, t, size), m_Dot);
                    // 右
                    GUI.DrawTexture(new Rect(cx + r - t, cy - r, t, size), m_Dot);
                }
            }

            GUI.color = prev;
            GUI.matrix = saved;
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
