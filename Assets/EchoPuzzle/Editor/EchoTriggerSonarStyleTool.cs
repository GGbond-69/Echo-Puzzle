using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoPuzzle.EditorTools
{
    /// <summary>
    /// 按 TriggerVolume 的 <see cref="TriggerEffect"/> 类型，批量设置它的**特殊声波**
    /// 颜色与宽度：
    ///
    ///   Win  -> 颜色纯白，宽度 0.22
    ///   Lose -> 颜色 (255, 0, 0)，宽度 0.15
    ///   其它 -> 不动
    ///
    /// 和 EchoTriggerSonarResetter 一样同时处理两边：
    ///   · Prefab 资产：改的是"源"
    ///   · 场景实例：改的是"覆盖值"（覆盖优先，只改 Prefab 不够）
    ///
    /// 只改 sonar.color 与 sonar.width 两个字段，其余（射线数量、寿命、
    /// 触发效果、物体名单…）一律不动。
    /// </summary>
    public static class EchoTriggerSonarStyleTool
    {
        // ---- 两类效果的样式 ----

        private static readonly Color WinColor  = Color.white;
        private const float WinWidth  = 0.22f;

        private static readonly Color LoseColor = new Color(255f / 255f, 0f, 0f, 1f);
        private const float LoseWidth = 0.15f;

        [MenuItem("EchoPuzzle/按效果类型设置触发区声波颜色与宽度", priority = 52)]
        public static void ApplyByEffect()
        {
            int winners = 0, losers = 0, skipped = 0;

            // Prefab 资产
            int prefabFiles = ApplyToPrefabAssets(ref winners, ref losers, ref skipped);

            // 场景实例
            int sceneCount = ApplyToSceneInstances(ref winners, ref losers, ref skipped);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[EchoPuzzle] 已按效果类型设置触发区声波样式：\n" +
                $"  Win  组：{winners} 个 -> 颜色 纯白，宽度 {WinWidth}\n" +
                $"  Lose 组：{losers} 个 -> 颜色 {LoseColor}，宽度 {LoseWidth}\n" +
                $"  其它效果（跳过）：{skipped} 个\n" +
                $"  涉及 Prefab 资产 {prefabFiles} 个、场景实例 {sceneCount} 个。");
        }

        // ------------------------------------------------------------------ 核心

        /// <summary>按效果给一个 TriggerVolume 的 sonar 套上颜色与宽度。返回是否改动了。</summary>
        private static bool ApplyOne(TriggerVolume tv, ref int winners, ref int losers, ref int skipped)
        {
            if (tv == null) return false;

            Color color;
            float width;

            switch (tv.effect)
            {
                case TriggerEffect.Win:
                    color = WinColor;
                    width = WinWidth;
                    winners++;
                    break;

                case TriggerEffect.Lose:
                    color = LoseColor;
                    width = LoseWidth;
                    losers++;
                    break;

                default:
                    skipped++;
                    return false;
            }

            var so = new SerializedObject(tv);
            SerializedProperty sonar = so.FindProperty("sonar");
            if (sonar == null)
            {
                Debug.LogWarning($"[EchoPuzzle] 《{tv.name}》上找不到 sonar 字段，跳过。");
                return false;
            }

            bool changed = false;

            SerializedProperty colorProp = sonar.FindPropertyRelative("color");
            if (colorProp != null && colorProp.colorValue != color)
            {
                colorProp.colorValue = color;
                changed = true;
            }

            SerializedProperty widthProp = sonar.FindPropertyRelative("width");
            if (widthProp != null && !Mathf.Approximately(widthProp.floatValue, width))
            {
                widthProp.floatValue = width;
                changed = true;
            }

            if (changed) so.ApplyModifiedProperties();

            return changed;
        }

        // ------------------------------------------------------------------ 场景实例

        private static int ApplyToSceneInstances(ref int winners, ref int losers, ref int skipped)
        {
            Scene scene = SceneManager.GetActiveScene();
            int n = 0;

            foreach (TriggerVolume tv in Object.FindObjectsOfType<TriggerVolume>(true))
            {
                if (tv == null) continue;
                if (tv.gameObject.scene != scene) continue;   // 排除 Prefab 资产内部

                Undo.RecordObject(tv, "Set Trigger Sonar Style");

                if (ApplyOne(tv, ref winners, ref losers, ref skipped))
                {
                    EditorUtility.SetDirty(tv);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(tv);
                    n++;
                }
            }

            return n;
        }

        // ------------------------------------------------------------------ Prefab 资产

        private static int ApplyToPrefabAssets(ref int winners, ref int losers, ref int skipped)
        {
            int n = 0;

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;

                // 先便宜地筛一遍，免得把所有 Prefab 都 LoadPrefabContents
                if (root.GetComponentInChildren<TriggerVolume>(true) == null) continue;

                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    TriggerVolume[] all = contents.GetComponentsInChildren<TriggerVolume>(true);
                    if (all.Length == 0) continue;

                    bool changed = false;

                    foreach (TriggerVolume tv in all)
                    {
                        if (ApplyOne(tv, ref winners, ref losers, ref skipped)) changed = true;
                    }

                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        n++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            return n;
        }

        // ------------------------------------------------------------------ 检查

        [MenuItem("EchoPuzzle/检查触发区声波的颜色与宽度", priority = 53)]
        public static void Verify()
        {
            var lines = new List<string>();
            int ok = 0, bad = 0;

            foreach (TriggerVolume tv in Object.FindObjectsOfType<TriggerVolume>(true))
            {
                if (tv == null) continue;

                var so = new SerializedObject(tv);
                SerializedProperty sonar = so.FindProperty("sonar");
                if (sonar == null) continue;

                Color c = sonar.FindPropertyRelative("color")?.colorValue ?? Color.magenta;
                float w = sonar.FindPropertyRelative("width")?.floatValue ?? -1f;

                string owner = tv.gameObject.scene.IsValid()
                             ? $"场景《{tv.name}》"
                             : $"Prefab《{tv.name}》";

                bool pass;
                string expect;

                switch (tv.effect)
                {
                    case TriggerEffect.Win:
                        pass   = ApproxColor(c, WinColor) && Mathf.Approximately(w, WinWidth);
                        expect = $"期望 纯白 / {WinWidth}";
                        break;

                    case TriggerEffect.Lose:
                        pass   = ApproxColor(c, LoseColor) && Mathf.Approximately(w, LoseWidth);
                        expect = $"期望 (255,0,0) / {LoseWidth}";
                        break;

                    default:
                        lines.Add($"  ·  {owner} effect={tv.effect}（不参与，跳过）");
                        continue;
                }

                if (pass) ok++; else bad++;

                lines.Add($"  {(pass ? "✅" : "⚠")} {owner} effect={tv.effect} " +
                          $"颜色=({c.r * 255f:F0},{c.g * 255f:F0},{c.b * 255f:F0}) 宽度={w:F3}  {expect}");
            }

            Debug.Log($"[EchoPuzzle] 触发区声波样式检查：符合 {ok} 个，不符合 {bad} 个。\n" +
                      string.Join("\n", lines));
        }

        private static bool ApproxColor(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.004f &&
                   Mathf.Abs(a.g - b.g) < 0.004f &&
                   Mathf.Abs(a.b - b.b) < 0.004f;
        }
    }
}
