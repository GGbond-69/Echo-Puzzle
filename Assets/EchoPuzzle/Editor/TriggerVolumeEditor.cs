using System.Collections.Generic;
using EchoPuzzle;
using UnityEditor;
using UnityEngine;

namespace EchoPuzzle.EditorTools
{
    /// <summary>
    /// TriggerVolume 的自定义检查器。
    ///
    /// 选中 OperateObjects 时，把配置分成两个面板：
    ///   Spawn Objects  —— 要**启用**的物体名单，带「新建对象 / 删除对象」按钮
    ///   Remove Objects —— 要**删除**的物体名单，带同样的按钮
    ///
    /// 名单是**名字**（不是引用），运行时查找场景里所有同名物体一起处理。
    /// 每个名字旁边还有个引用槽：从「层级」面板把物体拖进去就会自动填名字，
    /// 省得手打。
    /// </summary>
    [CustomEditor(typeof(TriggerVolume))]
    [CanEditMultipleObjects]
    public sealed class TriggerVolumeEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var effectProp  = serializedObject.FindProperty("effect");
            var spawnProp   = serializedObject.FindProperty("spawnObjects");
            var removeProp  = serializedObject.FindProperty("removeObjects");
            var destroyProp = serializedObject.FindProperty("destroySelfAfterTrigger");

            EditorGUILayout.PropertyField(effectProp);

            var kind = (TriggerEffect)effectProp.enumValueIndex;

            if (kind == TriggerEffect.OperateObjects)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(
                    "进入后按**名字**查找场景里的物体（同名的一起处理）：\n" +
                    "  先启用 Spawn Objects 里列出的\n" +
                    "  再删除 Remove Objects 里列出的\n" +
                    "然后删除自己。\n\n" +
                    "名字可以直接手打，也可以从「层级」面板把物体拖到右边的槽里自动填。",
                    MessageType.Info);

                DrawNameList("Spawn Objects（要启用的）", spawnProp, "启用");
                EditorGUILayout.Space(6f);
                DrawNameList("Remove Objects（要删除的）", removeProp, "删除");

                EditorGUILayout.Space(4f);
                EditorGUILayout.PropertyField(destroyProp,
                    new GUIContent("触发后删除自己"));
            }

            EditorGUILayout.Space(8f);

            // ---- 其余字段照常显示，但跳过上面已经画过的 ----
            serializedObject.ApplyModifiedProperties();

            DrawRemainingFields(effectProp, spawnProp, removeProp, destroyProp);
        }

        /// <summary>画一个名字名单：标题 + 若干条目 + 新建/删除按钮。</summary>
        private void DrawNameList(string title, SerializedProperty list, string verb)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            if (list.arraySize == 0)
            {
                EditorGUILayout.HelpBox($"还没有条目。点下面的「新建对象」加一个，然后填名字" +
                                        $"（或用拖拽自动填）。", MessageType.None);
            }

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty item = list.GetArrayElementAtIndex(i);
                SerializedProperty nameProp = item.FindPropertyRelative("objectName");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField($"{i + 1}", GUILayout.Width(20f));
                nameProp.stringValue = EditorGUILayout.TextField(nameProp.stringValue);

                // 拖拽槽：拖进来就自动填名字
                EditorGUI.BeginChangeCheck();
                var dropped = (GameObject)EditorGUILayout.ObjectField(
                    null, typeof(GameObject), true, GUILayout.Width(60f));

                if (EditorGUI.EndChangeCheck() && dropped != null)
                    nameProp.stringValue = dropped.name;

                EditorGUILayout.EndHorizontal();

                // 提示这个名字能不能在场景里找到
                if (!string.IsNullOrEmpty(nameProp.stringValue))
                {
                    int found = TriggerVolume.FindAllByName(nameProp.stringValue).Length;
                    if (found == 0)
                        EditorGUILayout.LabelField($"场景里找不到叫「{nameProp.stringValue}」的物体",
                                                   EditorStyles.miniLabel);
                    else
                        EditorGUILayout.LabelField($"场景里有 {found} 个同名物体，会一起{verb}",
                                                   EditorStyles.miniLabel);
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("+ 新建对象", GUILayout.Height(24f)))
            {
                int idx = list.arraySize;
                list.InsertArrayElementAtIndex(idx);

                SerializedProperty item = list.GetArrayElementAtIndex(idx);
                item.FindPropertyRelative("objectName").stringValue = "";

                serializedObject.ApplyModifiedProperties();
                return;
            }

            using (new EditorGUI.DisabledScope(list.arraySize == 0))
            {
                if (GUILayout.Button("- 删除对象", GUILayout.Height(24f)))
                {
                    list.DeleteArrayElementAtIndex(list.arraySize - 1);
                    serializedObject.ApplyModifiedProperties();
                    return;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>画剩下那些还没画过的字段（声波外观、判定等）。</summary>
        private void DrawRemainingFields(params SerializedProperty[] skip)
        {
            serializedObject.Update();

            var skipPaths = new List<string>();
            foreach (var s in skip) skipPaths.Add(s.propertyPath);

            SerializedProperty it = serializedObject.GetIterator();
            bool enterChildren = true;

            while (it.NextVisible(enterChildren))
            {
                // 只对顶层字段深入子级；顶层字段处理完就停，避免把整个树走一遍
                enterChildren = false;

                if (it.propertyPath == "m_Script") continue;
                if (IsSkipped(it.propertyPath, skipPaths)) continue;

                EditorGUILayout.PropertyField(it, true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>路径本身被跳过，或者它是某个被跳过字段的子级（比如数组元素）。</summary>
        private static bool IsSkipped(string path, List<string> skipPaths)
        {
            for (int i = 0; i < skipPaths.Count; i++)
            {
                string s = skipPaths[i];
                if (path == s) return true;
                if (path.StartsWith(s + ".")) return true;   // 数组元素 / 子字段
            }
            return false;
        }
    }
}
