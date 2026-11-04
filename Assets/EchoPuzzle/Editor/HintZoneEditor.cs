using System.Collections.Generic;
using EchoPuzzle;
using UnityEditor;
using UnityEngine;

namespace EchoPuzzle.EditorTools
{
    /// <summary>
    /// HintZone 的自定义检查器。
    ///
    /// 为什么必须自定义：需求是"创建文本 / 删除文本"两个按钮，并且
    /// 新建的排到**最底层**、删除的也是**最底层**。Unity 默认的数组 UI
    /// 只能手动改 Size 和用右键 Duplicate/Delete，做不到这个语义。
    ///
    /// 布局：
    ///   文本栈
    ///     [0] 文字 + 切换方式 + 延迟        ← 最先显示
    ///     [1] ...
    ///     [n] 文字 + 切换方式 + 延迟        ← 最后显示（新建的加在这里）
    ///   [+ 创建文本]  [- 删除文本]           ← 作用在数组末尾
    /// </summary>
    [CustomEditor(typeof(HintZone))]
    [CanEditMultipleObjects]
    public sealed class HintZoneEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var entriesProp = serializedObject.FindProperty("entries");

            // ---------------- 文本栈 ----------------
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("文本栈（从上往下依次切换）", EditorStyles.boldLabel);

            int count = entriesProp.arraySize;

            if (count == 0)
            {
                EditorGUILayout.HelpBox(
                    "还没有文本。点下面的「创建文本」加一重。\n" +
                    "第一重建议把切换方式设成「进入区域」，这样玩家一进来就会看到。",
                    MessageType.Info);
            }

            for (int i = 0; i < count; i++)
            {
                SerializedProperty e = entriesProp.GetArrayElementAtIndex(i);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                string tag = i == 0 ? "第 1 重（最先显示）"
                           : i == count - 1 ? $"第 {i + 1} 重（最后显示）"
                           : $"第 {i + 1} 重";

                EditorGUILayout.LabelField(tag, EditorStyles.miniBoldLabel);

                SerializedProperty text    = e.FindPropertyRelative("text");
                SerializedProperty trigger = e.FindPropertyRelative("trigger");
                SerializedProperty delay   = e.FindPropertyRelative("delay");

                EditorGUILayout.PropertyField(text);

                var kind = (HintTriggerKind)trigger.enumValueIndex;

                EditorGUILayout.PropertyField(trigger, new GUIContent("切换方式"));

                if (kind == HintTriggerKind.AutoTime)
                    EditorGUILayout.PropertyField(delay, new GUIContent("自动切换延时（秒）"));
                else if (kind == HintTriggerKind.EnterZone)
                    EditorGUILayout.LabelField(" ", "进入区域时立刻显示（不需要操作）",
                                               EditorStyles.miniLabel);
                else
                    EditorGUILayout.PropertyField(delay, new GUIContent("检测到操作后延迟（秒）"));

                EditorGUILayout.EndVertical();
            }

            // ---------------- 两个按钮 ----------------
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("+ 创建文本", GUILayout.Height(26f)))
            {
                // 新建的排到最底层（数组末尾）= 最后才被切换出来
                int idx = entriesProp.arraySize;
                entriesProp.InsertArrayElementAtIndex(idx);
                InitEntry(entriesProp.GetArrayElementAtIndex(idx));

                serializedObject.ApplyModifiedProperties();
                return;
            }

            using (new EditorGUI.DisabledScope(entriesProp.arraySize == 0))
            {
                if (GUILayout.Button("- 删除文本", GUILayout.Height(26f)))
                {
                    // 删的也是最底层（数组末尾）
                    entriesProp.DeleteArrayElementAtIndex(entriesProp.arraySize - 1);
                    serializedObject.ApplyModifiedProperties();
                    return;
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                $"当前共 {entriesProp.arraySize} 重。新建的排到最底层（最后显示），删除的也是最底层。",
                EditorStyles.miniLabel);

            EditorGUILayout.Space(6f);

            // ---------------- 其余字段 ----------------
            // 注意：自定义 Editor 不会自动绘制没写到的字段。
            // 这里**只跳过文本栈**，其余字段（triggerOnce / 判定 / 显示样式）
            // 一律交给 DrawRemainingFields 画出来。
            //
            // 之前把 onceProp、boundsProp 也放进了 skip 列表，但前面又没画它们，
            // 结果 triggerOnce 和 useBoundsCheck 在检查器里彻底消失了。
            serializedObject.ApplyModifiedProperties();
            DrawRemainingFields(entriesProp);

            // ---------------- Prefab 提示 ----------------
            // 最常见的坑：在「项目」面板里改 Prefab 资源，而场景里用的是旧实例。
            // 这里显式提醒一下，省得改了半天没效果。
            var t = target as HintZone;
            if (t != null && PrefabUtility.IsPartOfPrefabInstance(t))
            {
                EditorGUILayout.Space(6f);

                var root = PrefabUtility.GetNearestPrefabInstanceRoot(t);
                string prefabName = root != null ? root.name : "?";

                EditorGUILayout.HelpBox(
                    $"这是一个 Prefab 实例（源：{prefabName}）。\n\n" +
                    "你在这里改的是**场景里这个实例**，改动会立刻生效。\n" +
                    "但如果你去「项目」面板双击打开 Prefab 资源改，\n" +
                    "场景里已经存在的实例**不会**同步 —— 那种情况要到这里重新拖一个。",
                    MessageType.None);
            }
        }

        /// <summary>画剩下那些还没画过的字段（显示样式等）。</summary>
        private void DrawRemainingFields(params SerializedProperty[] skip)
        {
            serializedObject.Update();

            var skipPaths = new List<string>();
            foreach (var s in skip) skipPaths.Add(s.propertyPath);

            SerializedProperty it = serializedObject.GetIterator();
            bool enterChildren = true;

            while (it.NextVisible(enterChildren))
            {
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
                if (path.StartsWith(s + ".")) return true;
            }
            return false;
        }

        /// <summary>
        /// 把新插入的一重初始化成空白。
        /// InsertArrayElementAtIndex 会复制上一项的内容，所以必须重置，
        /// 否则新加的那一重会带着上一重的文字。
        /// </summary>
        private static void InitEntry(SerializedProperty e)
        {
            e.FindPropertyRelative("text").stringValue       = "";
            e.FindPropertyRelative("trigger").enumValueIndex = (int)HintTriggerKind.Space;
            e.FindPropertyRelative("delay").floatValue       = 1f;
        }
    }
}
