using UnityEditor;
using UnityEngine;

namespace EchoPuzzle.EditorTools
{
    /// <summary>
    /// 菜单工具：生成 Gravel 的 Prefab。
    ///
    /// ── 为什么用工具而不是手写 prefab ────────────────────────
    ///   .prefab 里要写脚本的 GUID、组件 fileID 和它们之间的互相引用。
    ///   手写很容易错一个字节就成"missing script"。
    ///   交给 Unity 自己序列化最稳。
    /// </summary>
    public static class GravelPrefabTool
    {
        private const string PrefabPath = "Assets/EchoPuzzle/Prefabs/Gravel.prefab";

        [MenuItem("EchoPuzzle/生成 Gravel 预置体", priority = 80)]
        private static void CreateGravelPrefab()
        {
            // 已经存在就不覆盖（里面可能已经有你调好的参数）
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            {
                bool replace = EditorUtility.DisplayDialog("EchoPuzzle",
                    $"{PrefabPath} 已经存在。\n要覆盖它吗？（你在里面调过的参数会丢失）",
                    "覆盖", "取消");

                if (!replace) return;

                AssetDatabase.DeleteAsset(PrefabPath);
            }

            var go = new GameObject("Gravel");

            // ★ 和 TriggerVolume 一样：没有实体，只有一个 Trigger 碰撞盒
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size      = Vector3.one;

            // Gravel 组件
            var gravel = go.AddComponent<Gravel>();

            // ★ 默认挂 Hide（hiding = 0，也就是启用）
            var hide = go.AddComponent<Hide>();
            hide.hiding = 0;

            // 保证目录存在
            System.IO.Directory.CreateDirectory("Assets/EchoPuzzle/Prefabs");

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);

            Object.DestroyImmediate(go);

            if (prefab != null)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // 选中它，方便直接拖进场景
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);

                Debug.Log($"[EchoPuzzle] 已生成 {PrefabPath}");
            }
            else
            {
                Debug.LogError("[EchoPuzzle] 生成 Gravel 预置体失败。");
            }
        }
    }
}
