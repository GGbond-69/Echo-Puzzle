using EchoPuzzle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;          // ShadowCastingMode / AmbientMode / DefaultReflectionMode
using UnityEngine.SceneManagement;

namespace EchoPuzzle.EditorTools
{
    /// <summary>
    /// 一键切换关卡外观：墙体透明 / 灰色，光照开 / 关。
    ///
    /// 菜单：EchoPuzzle / 关卡外观 / ...
    ///
    /// 为什么做成工具而不是直接改场景文件：
    ///   墙体的材质是运行时由 CircleWall / HemisphereDome 组件的 Generate() 赋的
    ///   （Prefab 里的 material 字段是空的，会回落到内置的灰色材质），
    ///   所以"把材质改掉"其实是要给场景里的这些组件指定 material 字段。
    ///   用编辑器工具做，场景会被正常标记为已修改、可以撤销，也不会写坏 YAML。
    ///
    /// 光照的原始设置会被记下来（存在 EditorPrefs 里，所以进 Play 触发的
    /// 域重载不会把它清掉），点"开灯"时原样恢复。
    /// </summary>
    public static class EchoAppearanceTool
    {
        private const string InvisiblePath = "Assets/EchoPuzzle/Materials/EchoInvisible.mat";
        private const string OccluderPath  = "Assets/EchoPuzzle/Materials/EchoOccluder.mat";
        private const string WallPath      = "Assets/EchoPuzzle/Materials/EchoWall.mat";
        private const string OutlinePath   = "Assets/EchoPuzzle/Materials/EchoOutline.mat";

        // EditorPrefs 的键名。用 EditorPrefs 而不是静态字段，
        // 因为进 Play 会触发域重载，静态字段会被清空。
        private const string K_Saved      = "EchoPuzzle.LightSaved";
        private const string K_AmbMode    = "EchoPuzzle.AmbMode";
        private const string K_AmbR       = "EchoPuzzle.AmbR";
        private const string K_AmbG       = "EchoPuzzle.AmbG";
        private const string K_AmbB       = "EchoPuzzle.AmbB";
        private const string K_AmbInt     = "EchoPuzzle.AmbIntensity";
        private const string K_ReflMode   = "EchoPuzzle.ReflMode";
        private const string K_ReflInt    = "EchoPuzzle.ReflIntensity";

        // 天空盒 / 自定义反射贴图用 GlobalObjectId 记，**不能用资产路径** ——
        // level1 用的是 Unity 内置的 Default-Skybox，而 AssetDatabase.GetAssetPath()
        // 对内置资源返回空字符串，记下来就是个空路径、恢复时什么也加载不出来。
        private const string K_Skybox     = "EchoPuzzle.SkyboxGid";
        private const string K_ReflTex    = "EchoPuzzle.ReflTexGid";

        // ------------------------------------------------------------------ 菜单

        [MenuItem("EchoPuzzle/关卡外观/墙体完全透明 + 去掉光照", false, 20)]
        public static void MakeTransparentNoLight()
        {
            ApplyWallMaterial(true);
            KillLighting();
        }

        [MenuItem("EchoPuzzle/关卡外观/墙体恢复灰色 + 恢复光照", false, 21)]
        public static void MakeOpaqueWithLight()
        {
            ApplyWallMaterial(false);
            RestoreLighting();
        }

        [MenuItem("EchoPuzzle/关卡外观/开灯（恢复光照设置）", false, 22)]
        public static void RestoreLightingOnly()
        {
            RestoreLighting();
        }

        [MenuItem("EchoPuzzle/关卡外观/关灯（去掉光照）", false, 23)]
        public static void KillLightingOnly()
        {
            KillLighting();
        }

        [MenuItem("EchoPuzzle/关卡外观/墙体完全透明（不动光照）", false, 25)]
        public static void WallTransparentOnly()
        {
            ApplyWallMaterial(true);
        }

        [MenuItem("EchoPuzzle/关卡外观/墙体恢复灰色（不动光照）", false, 26)]
        public static void WallOpaqueOnly()
        {
            ApplyWallMaterial(false);
        }

        [MenuItem("EchoPuzzle/关卡外观/重新捕获光照设置（清掉旧记录）", false, 40)]
        public static void ForgetLightingSnapshot()
        {
            EditorPrefs.DeleteKey(K_Saved);
            EditorPrefs.DeleteKey(K_AmbMode);
            EditorPrefs.DeleteKey(K_AmbR);
            EditorPrefs.DeleteKey(K_AmbG);
            EditorPrefs.DeleteKey(K_AmbB);
            EditorPrefs.DeleteKey(K_AmbInt);
            EditorPrefs.DeleteKey(K_Skybox);
            EditorPrefs.DeleteKey(K_ReflTex);
            EditorPrefs.DeleteKey(K_ReflMode);
            EditorPrefs.DeleteKey(K_ReflInt);

            EditorUtility.DisplayDialog("EchoPuzzle",
                "已清除光照记录。\n\n" +
                "接下来：\n" +
                "1) 在 Lighting 窗口里把 Skybox Material 设成你要的样子\n" +
                "2) 点「关灯（去掉光照）」—— 这时会重新记录当前设置\n" +
                "3) 再点「开灯（恢复光照设置）」验证能不能还原",
                "好");

            Debug.Log("[EchoPuzzle] 已清除光照记录（skybox 等）。下次关灯时会重新捕获当前值。");
        }

        // ------------------------------------------------------------------ 墙体材质

        private static void ApplyWallMaterial(bool transparent)
        {
            Scene scene = SceneManager.GetActiveScene();

            if (!scene.IsValid() || !scene.isLoaded)
            {
                EditorUtility.DisplayDialog("EchoPuzzle", "没有已打开的场景。", "好");
                return;
            }

            Material mat = transparent
                ? GetOrCreateInvisible()
                : AssetDatabase.LoadAssetAtPath<Material>(WallPath);

            if (mat == null)
            {
                EditorUtility.DisplayDialog("EchoPuzzle",
                    $"找不到材质：{(transparent ? InvisiblePath : WallPath)}", "好");
                return;
            }

            int walls = AssignWallMaterial(mat);

            MarkDirty();

            if (transparent)
                Debug.Log($"[EchoPuzzle] {walls} 个墙体 -> 完全透明（{mat.name}）");
            else
                Debug.Log($"[EchoPuzzle] {walls} 个墙体 -> 灰色（{mat.name}）");
        }

        /// <summary>给场景里所有"墙"指定材质。返回改动数量。</summary>
        private static int AssignWallMaterial(Material mat)
        {
            int count = 0;
            int occluders = 0;
            bool transparent = mat.name == "EchoInvisible";

            // ---- 1) CircleWall / HemisphereDome 组件：设它们的 material 字段 ----
            foreach (var w in Object.FindObjectsOfType<CircleWall>(true))
            {
                Undo.RecordObject(w, "Set wall material");
                w.material = mat;
                w.Generate();                 // 立刻重建，编辑器里就能看到效果
                EditorUtility.SetDirty(w);
                count++;
            }

            foreach (var d in Object.FindObjectsOfType<HemisphereDome>(true))
            {
                Undo.RecordObject(d, "Set wall material");
                d.material = mat;
                d.Generate();
                EditorUtility.SetDirty(d);
                count++;
            }

            // 三角斜坡：之前漏了，所以点"墙体透明"时它不变
            foreach (var s in Object.FindObjectsOfType<SlopeRamp>(true))
            {
                Undo.RecordObject(s, "Set wall material");
                s.material = mat;
                s.Generate();
                EditorUtility.SetDirty(s);
                count++;
            }

            // ---- 2) 遮挡墙：无论什么模式都保持"纯黑 + 写深度" ----
            //   它是用来挡住玩家 / 声波光带 / 标记的，切成透明就失去意义了。
            //   这里必须**在**下面的通用循环之前处理，否则通用循环会把它改掉。
            var occluderMat = GetOrCreateOccluder();

            foreach (var o in Object.FindObjectsOfType<WallOccluder>(true))
            {
                Undo.RecordObject(o, "Set occluder material");
                o.material = occluderMat;
                o.Generate();
                EditorUtility.SetDirty(o);
                occluders++;
            }

            // ---- 3) 普通方块墙：直接换 renderer 上的材质 ----
            //   跳过玩家身体（它用的是 EchoOutline，需要保留轮廓）
            //   跳过遮挡墙（上面已经处理过，保持纯黑）
            var outline = AssetDatabase.LoadAssetAtPath<Material>(OutlinePath);

            foreach (var r in Object.FindObjectsOfType<MeshRenderer>(true))
            {
                if (r == null) continue;
                if (r.sharedMaterial == outline) continue;      // 玩家轮廓不动

                GameObject go = r.gameObject;
                if (go.GetComponent<PlayerController>() != null) continue;
                if (go.GetComponentInParent<PlayerController>() != null) continue;
                if (go.GetComponent<WallOccluder>() != null) continue;          // 遮挡墙不动
                if (go.GetComponentInParent<WallOccluder>() != null) continue;

                // 只处理看起来是墙 / 地板的东西
                string n = go.name;
                bool looksLikeWall =
                    n.StartsWith("Cube") ||
                    n.StartsWith("Wall") ||
                    n.StartsWith("Circle") ||
                    n.StartsWith("Hemi") ||
                    n.StartsWith("Slope") ||
                    n.StartsWith("Ramp") ||
                    n.StartsWith("Level") ||
                    r.sharedMaterial == null ||
                    r.sharedMaterial.name == "EchoWall" ||
                    r.sharedMaterial.name == "EchoInvisible";

                if (!looksLikeWall) continue;

                Undo.RecordObject(r, "Set wall material");
                r.sharedMaterial = mat;
                EditorUtility.SetDirty(r);

                // 透明材质别再投影，免得露出一团灰影；恢复灰色时把阴影也还原
                r.shadowCastingMode = transparent ? ShadowCastingMode.Off
                                                  : ShadowCastingMode.On;
                r.receiveShadows    = !transparent;
                count++;
            }

            // 遮挡墙的阴影一直是关的（它不参与光照）
            if (occluders > 0)
                Debug.Log($"[EchoPuzzle] 其中 {occluders} 个遮挡墙保持纯黑遮挡材质，未参与透明/灰色切换。");

            return count;
        }

        /// <summary>
        /// 拿（或创建）遮挡墙体材质。它和透明材质是分开的两份资产：
        ///   EchoInvisible.mat —— 完全看不见，也不挡任何东西
        ///   EchoOccluder.mat  —— 纯黑 + 写深度，挡住玩家 / 声波 / 标记
        /// </summary>
        private static Material GetOrCreateOccluder()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(OccluderPath);
            if (mat != null) return mat;

            Shader shader = Shader.Find("EchoPuzzle/Occluder");
            if (shader == null)
            {
                Debug.LogError("[EchoPuzzle] 找不到 shader「EchoPuzzle/Occluder」。" +
                               "确认 Assets/EchoPuzzle/Shaders/EchoOccluder.shader 已编译。");
                return null;
            }

            if (!AssetDatabase.IsValidFolder("Assets/EchoPuzzle/Materials"))
                AssetDatabase.CreateFolder("Assets/EchoPuzzle", "Materials");

            mat = new Material(shader) { name = "EchoOccluder" };
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.black);

            AssetDatabase.CreateAsset(mat, OccluderPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[EchoPuzzle] 已创建材质 {OccluderPath}");
            return AssetDatabase.LoadAssetAtPath<Material>(OccluderPath);
        }

        private static Material GetOrCreateInvisible()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(InvisiblePath);
            if (mat != null) return mat;

            Shader shader = Shader.Find("EchoPuzzle/Invisible");
            if (shader == null)
            {
                Debug.LogError("[EchoPuzzle] 找不到 shader「EchoPuzzle/Invisible」。" +
                               "确认 Assets/EchoPuzzle/Shaders/EchoInvisible.shader 已编译。");
                return null;
            }

            if (!AssetDatabase.IsValidFolder("Assets/EchoPuzzle/Materials"))
                AssetDatabase.CreateFolder("Assets/EchoPuzzle", "Materials");

            mat = new Material(shader) { name = "EchoInvisible" };
            AssetDatabase.CreateAsset(mat, InvisiblePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[EchoPuzzle] 已创建材质 {InvisiblePath}");
            return AssetDatabase.LoadAssetAtPath<Material>(InvisiblePath);
        }

        // ------------------------------------------------------------------ 光照：关

        private static void KillLighting()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                EditorUtility.DisplayDialog("EchoPuzzle", "没有已打开的场景。", "好");
                return;
            }

            // 只在第一次关灯时记下原始设置，重复点不会把"全黑"记成原始值
            if (EditorPrefs.GetBool(K_Saved, false) == false)
            {
                EditorPrefs.SetInt  (K_AmbMode, (int)RenderSettings.ambientMode);
                EditorPrefs.SetFloat(K_AmbR,    RenderSettings.ambientLight.r);
                EditorPrefs.SetFloat(K_AmbG,    RenderSettings.ambientLight.g);
                EditorPrefs.SetFloat(K_AmbB,    RenderSettings.ambientLight.b);
                EditorPrefs.SetFloat(K_AmbInt,  RenderSettings.ambientIntensity);

                EditorPrefs.SetString(K_Skybox,  ToGlobalId(RenderSettings.skybox));
                EditorPrefs.SetString(K_ReflTex, ToGlobalId(RenderSettings.customReflectionTexture));

                EditorPrefs.SetInt  (K_ReflMode, (int)RenderSettings.defaultReflectionMode);
                EditorPrefs.SetFloat(K_ReflInt,  RenderSettings.reflectionIntensity);

                EditorPrefs.SetBool (K_Saved, true);

                Debug.Log($"[EchoPuzzle] 已记录原始光照：ambientMode={RenderSettings.ambientMode} " +
                          $"ambient={RenderSettings.ambientLight} " +
                          $"skybox={Describe(RenderSettings.skybox)}");
            }

            // ---- 关掉所有 Light 组件 ----
            int lights = 0;
            foreach (var l in Object.FindObjectsOfType<Light>(true))
            {
                Undo.RecordObject(l, "Disable light");
                l.enabled = false;
                EditorUtility.SetDirty(l);
                lights++;
            }

            // ---- 关掉所有反射探针 ----
            int probes = 0;
            foreach (var p in Object.FindObjectsOfType<ReflectionProbe>(true))
            {
                Undo.RecordObject(p, "Disable reflection probe");
                p.enabled = false;
                EditorUtility.SetDirty(p);
                probes++;
            }

            // ---- 环境光 / 天空盒 / 反射全部置零 ----
            // 全黑，避免墙体在 Bloom 下透出灰边
            RenderSettings.ambientMode      = AmbientMode.Flat;
            RenderSettings.ambientLight     = Color.black;
            RenderSettings.ambientIntensity = 0f;
            RenderSettings.skybox           = null;
            RenderSettings.reflectionIntensity     = 0f;
            RenderSettings.defaultReflectionMode   = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;

            MarkDirty();

            Debug.Log($"[EchoPuzzle] 光照已关闭：Light {lights} 个、反射探针 {probes} 个，" +
                      "环境光/天空盒/反射强度均置零。");
        }

        // ------------------------------------------------------------------ 光照：开

        private static void RestoreLighting()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                EditorUtility.DisplayDialog("EchoPuzzle", "没有已打开的场景。", "好");
                return;
            }

            bool hadSaved = EditorPrefs.GetBool(K_Saved, false);

            // ---- 打开所有 Light 组件 ----
            int lights = 0;
            foreach (var l in Object.FindObjectsOfType<Light>(true))
            {
                Undo.RecordObject(l, "Enable light");
                l.enabled = true;
                EditorUtility.SetDirty(l);
                lights++;
            }

            // ---- 打开所有反射探针 ----
            int probes = 0;
            foreach (var p in Object.FindObjectsOfType<ReflectionProbe>(true))
            {
                Undo.RecordObject(p, "Enable reflection probe");
                p.enabled = true;
                EditorUtility.SetDirty(p);
                probes++;
            }

            // ---- 恢复环境光 / 天空盒 / 反射 ----
            if (hadSaved)
            {
                RenderSettings.ambientMode = (AmbientMode)EditorPrefs.GetInt(K_AmbMode, 0);
                RenderSettings.ambientLight = new Color(
                    EditorPrefs.GetFloat(K_AmbR, 0.212f),
                    EditorPrefs.GetFloat(K_AmbG, 0.227f),
                    EditorPrefs.GetFloat(K_AmbB, 0.259f));
                RenderSettings.ambientIntensity = EditorPrefs.GetFloat(K_AmbInt, 1f);

                RenderSettings.skybox = FromGlobalId<Material>(K_Skybox);
                RenderSettings.customReflectionTexture = FromGlobalId<Texture>(K_ReflTex);

                RenderSettings.defaultReflectionMode = (DefaultReflectionMode)
                    EditorPrefs.GetInt(K_ReflMode, (int)DefaultReflectionMode.Skybox);
                RenderSettings.reflectionIntensity = EditorPrefs.GetFloat(K_ReflInt, 1f);

                if (RenderSettings.skybox == null)
                {
                    Debug.LogWarning("[EchoPuzzle] 天空盒没能还原（记录为空或资源已不存在）。" +
                                     "可以在 Lighting 窗口里重新指定，或手动把 Skybox Material 设回 Default-Skybox。");
                }
            }
            else
            {
                // 没记录过（比如一上来就点开灯）—— 用 Unity 的常规默认值
                RenderSettings.ambientMode      = AmbientMode.Skybox;
                RenderSettings.ambientIntensity = 1f;
                RenderSettings.reflectionIntensity   = 1f;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

                Debug.Log("[EchoPuzzle] 没有记录过原始光照设置，已套用 Unity 默认值。");
            }

            MarkDirty();

            Debug.Log($"[EchoPuzzle] 光照已恢复：Light {lights} 个、反射探针 {probes} 个" +
                      (hadSaved
                          ? $"，环境光按记录还原，skybox={Describe(RenderSettings.skybox)}"
                          : "，环境光用默认值。"));
        }

        // ------------------------------------------------------------------ GlobalObjectId 存取

        /// <summary>
        /// 把一个对象存成 GlobalObjectId 字符串。
        /// 内置资源（比如 Default-Skybox）**必须**用这个 ——
        /// AssetDatabase.GetAssetPath() 对它们返回空字符串，存路径会丢掉。
        /// </summary>
        private static string ToGlobalId(Object obj)
        {
            if (obj == null) return "";
            return GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
        }

        /// <summary>把 GlobalObjectId 字符串还原成对象。失败返回 null。</summary>
        private static T FromGlobalId<T>(string key) where T : Object
        {
            string raw = EditorPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(raw)) return null;

            if (!GlobalObjectId.TryParse(raw, out GlobalObjectId gid))
            {
                Debug.LogWarning($"[EchoPuzzle] 无法解析记录的对象 id：{raw}");
                return null;
            }

            Object obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
            if (obj == null)
            {
                Debug.LogWarning($"[EchoPuzzle] 记录的对象已不存在：{raw}");
                return null;
            }

            return obj as T;
        }

        /// <summary>给日志用的名字（内置资源也能显示出来）。</summary>
        private static string Describe(Object obj)
        {
            if (obj == null) return "无";

            string path = AssetDatabase.GetAssetPath(obj);
            return string.IsNullOrEmpty(path) ? $"{obj.name}(内置)" : path;
        }

        // ------------------------------------------------------------------ 工具

        private static void MarkDirty()
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }
    }
}
