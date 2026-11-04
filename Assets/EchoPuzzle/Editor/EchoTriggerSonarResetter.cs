using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoPuzzle.EditorTools
{
    /// <summary>
    /// 把所有 TriggerVolume 的"触发时发声波"参数（<see cref="TriggerVolume.sonar"/>）
    /// 重置成代码里的默认值 <see cref="SonarEmitSettings.DefaultShout"/>。
    ///
    /// 为什么需要这个工具：
    ///   `sonar` 是序列化字段。改了 <see cref="SonarEmitSettings.DefaultShout"/> 之后，
    ///   只有**新建**的 TriggerVolume 会拿到新默认值；
    ///   已经存在的（Prefab 资产里的、以及场景里的实例覆盖）都还留着旧数值。
    ///   这个工具就是把这些旧数值刷成新默认值。
    ///
    /// 同时处理两边：
    ///   · Prefab 资产：改的是"源"，所有引用它的实例都会跟着变
    ///   · 场景实例：改的是"覆盖值"。如果实例上有 sonar 的覆盖项，
    ///     光改 Prefab 是不够的（覆盖值优先），所以两边都要刷
    ///
    /// 只会改 `sonar` 这一个字段，TriggerVolume 的其它设置（效果、物体名单、
    /// 声波外观、判定方式）一律不动。
    /// </summary>
    public static class EchoTriggerSonarResetter
    {
        /// <summary>Prefab 资产的搜索范围。</summary>
        private const string PrefabSearchRoot = "Assets";

        [MenuItem("EchoPuzzle/把所有触发区的声波参数重置为默认值", priority = 50)]
        public static void ResetAll()
        {
            int sceneCount = ResetSceneInstances(out int sceneOverrides);
            int prefabCount = ResetPrefabAssets(out int prefabNested);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[EchoPuzzle] 已把 sonar 参数重置为默认值：\n" +
                $"  Prefab 资产：{prefabCount} 个（其中嵌套在其它 Prefab 里的 {prefabNested} 个）\n" +
                $"  场景实例：  {sceneCount} 个（其中带 sonar 覆盖项的 {sceneOverrides} 个）\n" +
                $"  默认值：90°={SonarEmitSettings.DefaultShout.rayCount90} " +
                $"30°={SonarEmitSettings.DefaultShout.rayCount30} " +
                $"其它层=0，maxBounces={SonarEmitSettings.DefaultShout.maxBounces}，" +
                $"falloff={SonarEmitSettings.DefaultShout.bounceFalloff}，" +
                $"holdAlpha={SonarEmitSettings.DefaultShout.holdAlpha}，" +
                $"color={SonarEmitSettings.DefaultShout.color}");

            // 选中一个，方便肉眼核对
            TriggerVolume first = FindFirstInScene();
            if (first != null)
            {
                Selection.activeGameObject = first.gameObject;
                EditorGUIUtility.PingObject(first.gameObject);
            }
        }

        // ------------------------------------------------------------------ 场景实例

        /// <summary>刷所有**当前场景**里的 TriggerVolume 实例。返回处理个数。</summary>
        private static int ResetSceneInstances(out int overrideCount)
        {
            overrideCount = 0;
            Scene scene = SceneManager.GetActiveScene();

            // FindObjectsOfType(true) 连未激活的也包含
            TriggerVolume[] all = Object.FindObjectsOfType<TriggerVolume>(true);
            int n = 0;

            foreach (TriggerVolume tv in all)
            {
                if (tv == null) continue;

                // 排除 Prefab 资产内部的对象（编辑器里 FindObjectsOfType 也会返回它们）
                if (tv.gameObject.scene != scene) continue;

                var so = new SerializedObject(tv);
                SerializedProperty sonarProp = so.FindProperty("sonar");
                if (sonarProp == null)
                {
                    Debug.LogWarning($"[EchoPuzzle] 《{tv.name}》上找不到 sonar 字段，跳过。");
                    continue;
                }

                // 在 Prefab 实例上写字段，Unity 会自动登记成 override
                Undo.RecordObject(tv, "Reset Trigger Sonar");

                ApplyDefaults(sonarProp, SonarEmitSettings.DefaultShout);
                so.ApplyModifiedProperties();

                EditorUtility.SetDirty(tv);
                PrefabUtility.RecordPrefabInstancePropertyModifications(tv);

                if (PrefabUtility.IsPartOfPrefabInstance(tv)) overrideCount++;

                n++;
            }

            return n;
        }

        // ------------------------------------------------------------------ Prefab 资产

        /// <summary>
        /// 刷所有包含 TriggerVolume 的 Prefab 资产（含嵌套在别的 Prefab 里的）。
        /// 用 LoadPrefabContents / SaveAsPrefabAsset 安全地改，不碰场景。
        /// </summary>
        private static int ResetPrefabAssets(out int nestedCount)
        {
            nestedCount = 0;
            int n = 0;

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabSearchRoot });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;

                // 先看这个 Prefab 里有没有 TriggerVolume，没有就跳过，
                // 免得为了扫描把所有 Prefab 都加载进来
                if (root.GetComponentInChildren<TriggerVolume>(true) == null) continue;

                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    TriggerVolume[] all = contents.GetComponentsInChildren<TriggerVolume>(true);
                    if (all.Length == 0) continue;

                    bool changed = false;

                    foreach (TriggerVolume tv in all)
                    {
                        var so = new SerializedObject(tv);
                        SerializedProperty sonarProp = so.FindProperty("sonar");
                        if (sonarProp == null) continue;

                        ApplyDefaults(sonarProp, SonarEmitSettings.DefaultShout);
                        so.ApplyModifiedProperties();

                        changed = true;

                        // 这个 TriggerVolume 是"嵌套在别的 Prefab 里"的实例吗
                        if (PrefabUtility.IsPartOfPrefabInstance(tv)) nestedCount++;
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

        // ------------------------------------------------------------------ 工具

        /// <summary>把一套默认值写进 sonar 这个 SerializedProperty。</summary>
        private static void ApplyDefaults(SerializedProperty sonar, SonarEmitSettings d)
        {
            SetInt(sonar, "rayCount90",        d.rayCount90);
            SetInt(sonar, "rayCount30",        d.rayCount30);
            SetInt(sonar, "rayCount60",        d.rayCount60);
            SetInt(sonar, "diffuseCount15",    d.diffuseCount15);
            SetInt(sonar, "diffuseCount45",    d.diffuseCount45);
            SetInt(sonar, "diffuseCount75",    d.diffuseCount75);
            SetInt(sonar, "hemisphereCount",   d.hemisphereCount);

            SetFloat(sonar, "range",           d.range);
            SetFloat(sonar, "speed",           d.speed);
            SetFloat(sonar, "trailSpacing",    d.trailSpacing);
            SetInt(sonar, "maxBounces",        d.maxBounces);
            SetFloat(sonar, "bounceFalloff",   d.bounceFalloff);
            SetFloat(sonar, "bounceLifeLoss",  d.bounceLifeLoss);

            SetFloat(sonar, "lifetime",        d.lifetime);
            SetFloat(sonar, "tailLinger",      d.tailLinger);
            SetFloat(sonar, "intensity",       d.intensity);
            SetBool(sonar, "holdAlpha",        d.holdAlpha);

            SetColor(sonar, "color",           d.color);
            SetFloat(sonar, "width",           d.width);
        }

        private static void SetInt(SerializedProperty parent, string name, int v)
        {
            SerializedProperty p = parent.FindPropertyRelative(name);
            if (p != null) p.intValue = v;
        }

        private static void SetFloat(SerializedProperty parent, string name, float v)
        {
            SerializedProperty p = parent.FindPropertyRelative(name);
            if (p != null) p.floatValue = v;
        }

        private static void SetBool(SerializedProperty parent, string name, bool v)
        {
            SerializedProperty p = parent.FindPropertyRelative(name);
            if (p != null) p.boolValue = v;
        }

        private static void SetColor(SerializedProperty parent, string name, Color v)
        {
            SerializedProperty p = parent.FindPropertyRelative(name);
            if (p != null) p.colorValue = v;
        }

        private static TriggerVolume FindFirstInScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            foreach (TriggerVolume tv in Object.FindObjectsOfType<TriggerVolume>(true))
            {
                if (tv != null && tv.gameObject.scene == scene) return tv;
            }
            return null;
        }

        // ------------------------------------------------------------------ 检查

        [MenuItem("EchoPuzzle/检查触发区的声波参数", priority = 51)]
        public static void Verify()
        {
            SonarEmitSettings d = SonarEmitSettings.DefaultShout;

            var lines = new List<string>();
            int total = 0, mismatch = 0;

            foreach (TriggerVolume tv in Object.FindObjectsOfType<TriggerVolume>(true))
            {
                if (tv == null) continue;
                total++;

                string owner = tv.gameObject.scene.IsValid()
                             ? $"场景《{tv.name}》"
                             : $"Prefab《{tv.name}》";

                var so = new SerializedObject(tv);
                SerializedProperty s = so.FindProperty("sonar");
                if (s == null) { lines.Add($"  {owner}: 找不到 sonar 字段"); continue; }

                int r90 = s.FindPropertyRelative("rayCount90")?.intValue ?? -1;
                int r30 = s.FindPropertyRelative("rayCount30")?.intValue ?? -1;
                int r60 = s.FindPropertyRelative("rayCount60")?.intValue ?? -1;
                int b   = s.FindPropertyRelative("maxBounces")?.intValue ?? -1;

                bool ok = r90 == d.rayCount90 && r30 == d.rayCount30 &&
                          r60 == d.rayCount60 && b == d.maxBounces;

                if (!ok) mismatch++;

                lines.Add($"  {(ok ? "✅" : "⚠")} {owner}: 90°={r90} 30°={r30} 60°={r60} bounces={b}");
            }

            Debug.Log($"[EchoPuzzle] 触发区声波参数检查：共 {total} 个，与默认值一致 {total - mismatch} 个，" +
                      $"不一致 {mismatch} 个。\n" +
                      $"  期望默认值：90°={d.rayCount90} 30°={d.rayCount30} 60°={d.rayCount60} " +
                      $"bounces={d.maxBounces}\n" +
                      string.Join("\n", lines));
        }
    }
}
