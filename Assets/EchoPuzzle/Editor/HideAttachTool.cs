using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoPuzzle.EditorTools
{
    /// <summary>
    /// 菜单工具：把 <see cref="Hide"/> 组件挂到当前场景里**所有还没有 Hide 的物体**上。
    ///
    /// ── 规则 ────────────────────────────────────────────────
    ///   · 遍历当前场景里所有根物体（含未激活的），递归到每个子物体
    ///   · **只处理还没有 Hide 的物体** —— 已经挂了的完全不动（不重置它的 Hiding）
    ///   · 新挂上的 Hide 默认 hiding = 0（启用），所以挂完之后场景行为完全不变
    ///   · 整个操作**只注册一次 Undo**，可以 Ctrl+Z 一次性撤销
    /// </summary>
    public static class HideAttachTool
    {
        [MenuItem("EchoPuzzle/Hide/给场景里所有物体挂上 Hide", priority = 70)]
        private static void AttachToAll()
        {
            Scene scene = SceneManager.GetActiveScene();

            if (!scene.IsValid())
            {
                EditorUtility.DisplayDialog("EchoPuzzle", "没有打开的场景。", "好");
                return;
            }

            // 收集所有根物体（含未激活）
            GameObject[] roots = scene.GetRootGameObjects();

            var targets = new List<GameObject>(512);

            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null) continue;
                CollectRecursive(roots[i].transform, targets);
            }

            // ★ 只挑出**还没有 Hide** 的物体：已有的完全不动
            var toAdd = new List<GameObject>(targets.Count);

            for (int i = 0; i < targets.Count; i++)
            {
                GameObject go = targets[i];
                if (go == null) continue;
                if (go.GetComponent<Hide>() != null) continue;    // 已有 -> 跳过

                toAdd.Add(go);
            }

            if (toAdd.Count == 0)
            {
                EditorUtility.DisplayDialog("EchoPuzzle",
                    $"场景里共 {targets.Count} 个物体，全部都挂过 Hide 了。", "好");
                return;
            }

            // ---- 一次性 Undo ----
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName($"给 {toAdd.Count} 个物体挂上 Hide");
            int group = Undo.GetCurrentGroup();

            for (int i = 0; i < toAdd.Count; i++)
            {
                GameObject go = toAdd[i];
                if (go == null) continue;

                Undo.AddComponent<Hide>(go);
            }

            Undo.CollapseUndoOperations(group);

            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[EchoPuzzle] 已给 {toAdd.Count} 个物体挂上 Hide" +
                      $"（场景共 {targets.Count} 个物体，跳过 {targets.Count - toAdd.Count} 个已挂过的）。\n" +
                      "★ 新挂上的 Hiding 默认 = 0（启用），场景行为不变。");
        }

        /// <summary>递归收集一个 Transform 及其所有子物体。</summary>
        private static void CollectRecursive(Transform t, List<GameObject> output)
        {
            if (t == null) return;

            output.Add(t.gameObject);

            for (int i = 0; i < t.childCount; i++)
            {
                CollectRecursive(t.GetChild(i), output);
            }
        }

        // ==================================================================
        // 给所有 Prefab 资产补 Hide
        // ==================================================================

        private const string PrefabSearchFolder = "Assets";

        [MenuItem("EchoPuzzle/Hide/给所有 Prefab 挂上 Hide", priority = 73)]
        private static void AttachToAllPrefabs()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabSearchFolder });

            int changedPrefabs = 0;
            int addedComponents = 0;

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (string.IsNullOrEmpty(path)) continue;

                    EditorUtility.DisplayProgressBar("给 Prefab 挂 Hide",
                        path, (float)i / Mathf.Max(1, guids.Length));

                    // 直接改资产内容
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    if (root == null) continue;

                    bool touched = false;

                    Transform[] all = root.GetComponentsInChildren<Transform>(true);

                    for (int k = 0; k < all.Length; k++)
                    {
                        GameObject go = all[k].gameObject;

                        if (go.GetComponent<Hide>() != null) continue;

                        var h = go.AddComponent<Hide>();
                        h.hiding = 0;          // 默认启用，不改变 Prefab 的外观

                        touched = true;
                        addedComponents++;
                    }

                    if (touched)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        changedPrefabs++;
                    }

                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[EchoPuzzle] Prefab 补 Hide 完成：检查 {guids.Length} 个，" +
                      $"改动 {changedPrefabs} 个，共添加 {addedComponents} 个 Hide 组件。");

            EditorUtility.DisplayDialog("EchoPuzzle",
                $"检查了 {guids.Length} 个 Prefab。\n" +
                $"改动 {changedPrefabs} 个，共添加 {addedComponents} 个 Hide 组件。",
                "好");
        }

        [MenuItem("EchoPuzzle/Hide/检查哪些 Prefab 还缺 Hide", priority = 74)]
        private static void ReportPrefabsMissingHide()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabSearchFolder });

            var missing = new List<string>(16);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path)) continue;

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) continue;

                if (root.GetComponent<Hide>() == null) missing.Add(path);

                PrefabUtility.UnloadPrefabContents(root);
            }

            if (missing.Count == 0)
            {
                Debug.Log($"[EchoPuzzle] {guids.Length} 个 Prefab 的根物体都已有 Hide。");
            }
            else
            {
                Debug.LogWarning($"[EchoPuzzle] {missing.Count} 个 Prefab 的根物体缺 Hide：\n  " +
                                 string.Join("\n  ", missing));
            }
        }
    }
}
