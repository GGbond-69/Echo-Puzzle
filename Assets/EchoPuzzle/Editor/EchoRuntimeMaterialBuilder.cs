using System.IO;
using UnityEditor;
using UnityEngine;

namespace EchoPuzzle.EditorTools
{
    /// <summary>
    /// 生成"运行时 shader"对应的材质资产。
    ///
    /// 声波光带 / 标记 / 脚印的材质原来是运行时 `new Material(shader)` 建的，
    /// 工程里**没有任何资产引用**它们用的 shader，于是打包时被 Unity 整体剔除 ——
    /// 这就是 exe 里光带（以及标记、脚印）完全不显示的根因。
    ///
    /// 这个工具把三份材质生成为真实资产，放在 `Assets/EchoPuzzle/Resources/EchoRuntime/`。
    /// 材质引用了 shader ⇒ shader 进包；`Resources.Load` 又能稳定拿到材质。
    /// </summary>
    public static class EchoRuntimeMaterialBuilder
    {
        private const string ResourceFolder = "Assets/EchoPuzzle/Resources/EchoRuntime";

        private struct Spec
        {
            public string   fileName;
            public string   shaderName;
            public string   materialName;
            public string[] floatProps;
            public float[]  floatValues;

            public Spec(string file, string shader, string mat,
                        string[] props, float[] values)
            {
                fileName     = file;
                shaderName   = shader;
                materialName = mat;
                floatProps   = props;
                floatValues  = values;
            }
        }

        [MenuItem("EchoPuzzle/生成运行时材质资产", priority = 40)]
        public static void Generate()
        {
            EnsureFolder();

            var specs = new[]
            {
                new Spec("EchoSonarTrail.mat",
                         "EchoPuzzle/SonarTrail",
                         "EchoSonarTrail",
                         new[] { "_Intensity" },
                         new[] { 1.8f }),

                new Spec("EchoMarker.mat",
                         "EchoPuzzle/Marker",
                         "EchoMarker",
                         new[] { "_Intensity", "_PulseRadius", "_CoreScale" },
                         new[] { 2.0f, 1f, 0.75f }),

                new Spec("EchoFootprint.mat",
                         "EchoPuzzle/Footprint",
                         "EchoFootprint",
                         new[] { "_Intensity" },
                         new[] { 1.6f }),
            };

            int made = 0, kept = 0;

            foreach (Spec spec in specs)
            {
                string path = $"{ResourceFolder}/{spec.fileName}";

                // 已经存在就沿用 —— 保留你在 Inspector 里调过的数值
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null)
                {
                    if (existing.shader == null || existing.shader.name != spec.shaderName)
                    {
                        Shader fix = Shader.Find(spec.shaderName);
                        if (fix != null) existing.shader = fix;
                        EditorUtility.SetDirty(existing);
                        Debug.Log($"[EchoPuzzle] 修复材质 shader 引用：{path}");
                    }

                    ApplyFloats(existing, spec);
                    kept++;
                    continue;
                }

                Shader shader = Shader.Find(spec.shaderName);
                if (shader == null)
                {
                    Debug.LogError($"[EchoPuzzle] 找不到 shader '{spec.shaderName}'，" +
                                   $"无法生成 {path}");
                    continue;
                }

                var mat = new Material(shader) { name = spec.materialName };
                ApplyFloats(mat, spec);

                AssetDatabase.CreateAsset(mat, path);
                made++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EchoRuntimeMaterials.ClearCache();

            Debug.Log($"[EchoPuzzle] 运行时材质生成完毕：新建 {made} 个、沿用 {kept} 个。\n" +
                      $"目录：{ResourceFolder}\n" +
                      "这三个材质让对应的 shader 被真正引用进包，打包后光带 / 标记 / 脚印才能显示。");

            // 选中生成出来的资产，方便肉眼确认
            var sel = AssetDatabase.LoadAssetAtPath<Material>($"{ResourceFolder}/EchoSonarTrail.mat");
            if (sel != null) EditorGUIUtility.PingObject(sel);
        }

        /// <summary>把已知的浮点属性写进材质。shader 里没有的属性会被静默忽略。</summary>
        private static void ApplyFloats(Material mat, Spec spec)
        {
            if (spec.floatProps == null || spec.floatValues == null) return;

            int n = Mathf.Min(spec.floatProps.Length, spec.floatValues.Length);
            for (int i = 0; i < n; i++)
            {
                if (!mat.HasProperty(spec.floatProps[i])) continue;
                mat.SetFloat(spec.floatProps[i], spec.floatValues[i]);
            }

            EditorUtility.SetDirty(mat);
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(ResourceFolder)) return;

            // Resources 可能还不存在，逐级建
            if (!AssetDatabase.IsValidFolder("Assets/EchoPuzzle/Resources"))
                AssetDatabase.CreateFolder("Assets/EchoPuzzle", "Resources");

            if (!AssetDatabase.IsValidFolder(ResourceFolder))
                AssetDatabase.CreateFolder("Assets/EchoPuzzle/Resources", "EchoRuntime");
        }

        // ------------------------------------------------------------------ 校验

        /// <summary>
        /// 检查三个 shader 是否真的会被打进包 —— 也就是有没有资产引用它们。
        /// 打包前跑一次，能提前发现"编辑器正常、exe 里丢失"这类问题。
        /// </summary>
        [MenuItem("EchoPuzzle/检查运行时 shader 是否会被打包", priority = 41)]
        public static void Verify()
        {
            var checks = new[]
            {
                new[] { "EchoPuzzle/SonarTrail", $"{ResourceFolder}/EchoSonarTrail.mat" },
                new[] { "EchoPuzzle/Marker",     $"{ResourceFolder}/EchoMarker.mat"     },
                new[] { "EchoPuzzle/Footprint",  $"{ResourceFolder}/EchoFootprint.mat"  },
            };

            int ok = 0, bad = 0;

            foreach (string[] c in checks)
            {
                string shaderName = c[0];
                string matPath    = c[1];

                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

                if (mat == null)
                {
                    Debug.LogError($"[EchoPuzzle] ❌ 缺少材质资产：{matPath}\n" +
                                   $"   → shader '{shaderName}' 不会被打进包，运行时将不可见。\n" +
                                   "   执行菜单：EchoPuzzle → 生成运行时材质资产");
                    bad++;
                    continue;
                }

                if (mat.shader == null)
                {
                    Debug.LogError($"[EchoPuzzle] ❌ 材质丢失 shader 引用：{matPath}");
                    bad++;
                    continue;
                }

                if (mat.shader.name != shaderName)
                {
                    Debug.LogWarning($"[EchoPuzzle] ⚠ {matPath} 用的是 '{mat.shader.name}'，" +
                                     $"期望 '{shaderName}'");
                    bad++;
                    continue;
                }

                Debug.Log($"[EchoPuzzle] ✅ {shaderName} 已被 {Path.GetFileName(matPath)} 引用，会进包。");
                ok++;
            }

            Debug.Log($"[EchoPuzzle] 检查完毕：{ok} 个正常，{bad} 个有问题。");
        }
    }
}
