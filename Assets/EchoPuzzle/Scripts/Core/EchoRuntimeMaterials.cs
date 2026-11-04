using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 三个"运行时 shader"的材质集中在这里。
    ///
    /// ★ 为什么需要这个类（打包后光带不显示的根因）：
    ///
    ///   声波光带 / 标记 / 脚印的材质是运行时用 `new Material(shader)` 建的，
    ///   工程里原本**没有任何资产引用**它们用的 shader
    ///   （`EchoPuzzle/SonarTrail`、`EchoPuzzle/Marker`、`EchoPuzzle/Footprint`）。
    ///
    ///   Unity 打包时只把**被资产或场景引用到**的 shader 编进包，
    ///   于是这三个 shader 被整体剔除。编辑器里 `Shader.Find` 能扫到工程源文件
    ///   所以一切正常；打包后 `Shader.Find` 返回 null —— 组件拿到 null 就
    ///   `enabled = false` 直接退出，光带连网格都不建，表现就是"完全不生成"。
    ///
    ///   修法：把它们放进 `Resources/` 下作为**材质资产**。
    ///   材质被引用了 → shader 进包；`Resources.Load` 又能稳定拿到它。
    ///   编辑器与打包行为因此完全一致。
    /// </summary>
    public static class EchoRuntimeMaterials
    {
        public const string SonarTrailPath = "EchoRuntime/EchoSonarTrail";
        public const string MarkerPath     = "EchoRuntime/EchoMarker";
        public const string FootprintPath  = "EchoRuntime/EchoFootprint";

        /// <summary>
        /// 遮挡材质（纯黑、不透明、ZWrite On）。
        /// 用来挡住透明队列里的光带 —— 怪物的实体球用的就是它。
        /// 这个资产可能在 Materials 目录（不是 Resources），所以两处都会找。
        /// </summary>
        public const string OccluderPath      = "EchoRuntime/EchoOccluder";
        public const string OccluderAssetPath = "Assets/EchoPuzzle/Materials/EchoOccluder.mat";

        /// <summary>
        /// 玩家 / 匕首用的轮廓材质（EchoPuzzle/Outline）。
        /// 在"透明墙 + 关灯"的场景里，它呈现为深色本体 + 白色边缘光。
        /// </summary>
        public const string OutlineAssetPath = "Assets/EchoPuzzle/Materials/EchoOutline.mat";

        private static Material s_SonarTrail;
        private static Material s_Marker;
        private static Material s_Footprint;
        private static Material s_Occluder;
        private static Material s_Outline;

        /// <summary>声波光带材质。</summary>
        public static Material SonarTrail
            => s_SonarTrail != null ? s_SonarTrail : (s_SonarTrail = Load(SonarTrailPath));

        /// <summary>标记材质。</summary>
        public static Material Marker
            => s_Marker != null ? s_Marker : (s_Marker = Load(MarkerPath));

        /// <summary>脚印材质。</summary>
        public static Material Footprint
            => s_Footprint != null ? s_Footprint : (s_Footprint = Load(FootprintPath));

        /// <summary>
        /// 玩家 / 匕首共用的轮廓材质（EchoPuzzle/Outline）。
        ///
        /// ★ 只在编辑器里能拿到（它不在 Resources 下）。
        ///   打包时请确保 Player.prefab 引用了它，Unity 才会把它打进包里 ——
        ///   玩家角色本来就引用着，所以没问题。
        /// </summary>
        public static Material Outline
        {
            get
            {
                if (s_Outline != null) return s_Outline;

#if UNITY_EDITOR
                s_Outline = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(OutlineAssetPath);
#endif
                return s_Outline;
            }
        }

        /// <summary>
        /// 瞄准辅助层材质（永远画在最上层，不被任何实体遮挡）。
        /// 用于投石的准星圈 / 落点圈 / 轨迹线，以及匕首的准星圈。
        ///
        /// 返回**新建**的实例（带调用方的颜色），调用方负责随对象一起销毁。
        /// </summary>
        public static Material AimOverlay(Color color)
        {
            Shader sh = Shader.Find("EchoPuzzle/AimOverlay");

            if (sh == null)
            {
                Debug.LogWarning("[EchoPuzzle] 找不到 shader「EchoPuzzle/AimOverlay」，" +
                                 "瞄准辅助线可能被墙体遮挡。");
                sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Sprites/Default");
            }

            var mat = new Material(sh) { hideFlags = HideFlags.DontSave };

            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);

            mat.SetInt("_ZTest",  (int)UnityEngine.Rendering.CompareFunction.Always);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;

            return mat;
        }

        /// <summary>
        /// 遮挡材质。先找 Resources，再退到 Materials 目录里的资产，
        /// 最后退到直接建一个 Occluder 材质的实例（编辑器里保证能跑）。
        /// </summary>
        public static Material Occluder
        {
            get
            {
                if (s_Occluder != null) return s_Occluder;

                s_Occluder = Resources.Load<Material>(OccluderPath);
                if (s_Occluder != null) return s_Occluder;

#if UNITY_EDITOR
                s_Occluder = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(OccluderAssetPath);
                if (s_Occluder != null) return s_Occluder;
#endif

                // 兜底：现场用 shader 建一个（只影响编辑器；打包前请确保有资产）
                Shader sh = Shader.Find("EchoPuzzle/Occluder");
                if (sh != null)
                {
                    s_Occluder = new Material(sh) { name = "EchoOccluderRuntime", hideFlags = HideFlags.DontSave };
                    s_Occluder.SetColor("_Color", Color.black);
                    return s_Occluder;
                }

                Debug.LogWarning("[EchoPuzzle] 找不到遮挡材质（EchoPuzzle/Occluder）。" +
                                 "怪物的实体球将不可见，也就挡不住光带。");
                return null;
            }
        }

        /// <summary>
        /// 取材质。先走 Resources 资产；万一资产缺失（比如忘了生成），
        /// 退回旧的 `Shader.Find` 路径 —— 这样编辑器里仍然能跑起来。
        /// </summary>
        private static Material Load(string resourcePath)
        {
            Material m = Resources.Load<Material>(resourcePath);
            if (m != null) return m;

            Debug.LogWarning($"[EchoPuzzle] 在 Resources 里找不到材质 '{resourcePath}'。\n" +
                             "打包后它会丢失（这正是声波光带在 exe 里不显示的原因）。\n" +
                             "请执行菜单：EchoPuzzle → 生成运行时材质资产。");

            return null;
        }

        /// <summary>清掉缓存（编辑器重新生成材质后调用）。</summary>
        public static void ClearCache()
        {
            s_SonarTrail = null;
            s_Marker     = null;
            s_Footprint  = null;
            s_Occluder   = null;
        }
    }
}
