using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 运行时创建所有材质。关卡和角色都不依赖工程里的任何美术资源。
    /// </summary>
    public static class EchoMaterials
    {
        public static readonly Color WallFinal  = Color.black;
        public static readonly Color WallDebug  = new Color(0.46f, 0.46f, 0.50f, 1f);
        public static readonly Color GoalColor  = new Color(0.35f, 1f, 0.65f, 1f);

        /// <summary>墙体 / 地面 / 天花板。debug = true 时是可见的灰色并启用弱光照。</summary>
        public static Material CreateWall(bool debug)
        {
            Shader s = Shader.Find("EchoPuzzle/Matte");
            if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Unlit/Color");

            var m = new Material(s) { name = "EchoWall" };

            if (m.HasProperty("_Color"))    m.SetColor("_Color", debug ? WallDebug : WallFinal);
            if (m.HasProperty("_DebugLit")) m.SetFloat("_DebugLit", debug ? 1f : 0f);
            if (m.HasProperty("_DebugLight"))
                m.SetVector("_DebugLight", new Vector4(0.35f, 0.82f, 0.45f, 0f));

            return m;
        }

        /// <summary>
        /// 完全不可见的地板 / 天花板材质。
        ///
        /// 不用"画成黑色"的做法，因为黑色材质仍然参与渲染，在 Bloom / 环境光 /
        /// 后处理下有可能透出一点灰。这个材质直接 discard 所有像素，绝对不会被看到。
        /// </summary>
        public static Material CreateInvisible()
        {
            Shader s = Shader.Find("EchoPuzzle/Invisible");
            if (s == null)
            {
                // 退路：普通 Matte 也给纯黑
                s = Shader.Find("EchoPuzzle/Matte");
                if (s == null) s = Shader.Find("Unlit/Color");
            }

            var m = new Material(s) { name = "EchoInvisible" };
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.black);
            return m;
        }

        /// <summary>
        /// **完全不透光**的遮挡墙体材质。
        ///
        /// 和 <see cref="CreateInvisible"/> 的区别：
        ///   Invisible —— discard 掉所有像素、不写深度，只是看不见，不挡任何东西
        ///   Occluder  —— 画成纯黑并写深度，挡住玩家 / 声波光带 / 标记
        ///
        /// 它的 shader 用 Cull Off，所以玩家站进墙体内部时也能挡住（否则从里面看是透明的）。
        /// </summary>
        public static Material CreateOccluder()
        {
            Shader s = Shader.Find("EchoPuzzle/Occluder");
            if (s == null)
            {
                Debug.LogError("[EchoPuzzle] 找不到 shader「EchoPuzzle/Occluder」，" +
                               "确认 Assets/EchoPuzzle/Shaders/EchoOccluder.shader 已编译。");
                return CreateWall(false);
            }

            var m = new Material(s) { name = "EchoOccluder" };
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.black);
            return m;
        }

        /// <summary>角色：只显示发光轮廓，内部全黑。</summary>
        public static Material CreateOutline()
        {
            Shader s = Shader.Find("EchoPuzzle/Outline");
            if (s == null) return null;

            // 玩家：白色轮廓，亮度明显高于声波
            var m = new Material(s) { name = "EchoOutline" };
            if (m.HasProperty("_OutlineColor")) m.SetColor("_OutlineColor", new Color(1f, 1f, 1f, 1f));
            if (m.HasProperty("_Glow"))         m.SetFloat("_Glow", 1.15f);
            if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", 0.026f);
            if (m.HasProperty("_RimStrength"))  m.SetFloat("_RimStrength", 0.85f);
            return m;
        }

        /// <summary>终点标记：自发光，全黑场景里也看得见。</summary>
        public static Material CreateEmissive(Color color)
        {
            Shader s = Shader.Find("EchoPuzzle/Matte");
            if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Unlit/Color");

            var m = new Material(s) { name = "EchoEmissive" };
            if (m.HasProperty("_Color"))    m.SetColor("_Color", color);
            if (m.HasProperty("_DebugLit")) m.SetFloat("_DebugLit", 0f);
            return m;
        }
    }
}
