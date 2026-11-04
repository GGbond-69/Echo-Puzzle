using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoPuzzle.EditorTools
{
    /// <summary>
    /// 菜单工具：搭建双模式所需的场景。
    ///
    /// ── 做的事 ──────────────────────────────────────────────
    ///   1) 生成 Home 场景（只有主页 UI）
    ///   2) 生成 DungeonMode 场景（复制 PuzzleMode 的内容）
    ///   3) 把三个场景按顺序写进 Build Settings
    ///
    /// ── 你需要先做的 ────────────────────────────────────────
    ///   · 在 Project 窗口里把 Level1 改名成 PuzzleMode
    ///   · 删掉 TestScene
    ///
    /// ★ 这些我都做成独立按钮，可以分开点。
    /// </summary>
    public static class SceneSetupTool
    {
        private const string ScenesDir   = "Assets/Scenes";
        private const string HomePath    = "Assets/Scenes/Home.unity";
        private const string PuzzlePath  = "Assets/Scenes/PuzzleMode.unity";
        private const string DungeonPath = "Assets/Scenes/DungeonMode.unity";

        private const string HomeName    = "Home";
        private const string PuzzleName  = "PuzzleMode";
        private const string DungeonName = "DungeonMode";

        // ------------------------------------------------------------------ Home

        [MenuItem("EchoPuzzle/场景/① 生成 Home 场景（主页）", priority = 100)]
        private static void CreateHomeScene()
        {
            if (File.Exists(HomePath))
            {
                bool replace = EditorUtility.DisplayDialog("EchoPuzzle",
                    $"{HomePath} 已经存在，要重建吗？", "重建", "取消");

                if (!replace) return;
            }

            // 新建一个空场景（先问一下要不要保存当前场景）
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ★ 主页界面由 GameUI 自己负责（它在 Home 场景里会显示"选模式"界面），
            //   所以这里**不需要**往场景里挂任何组件 ——
            //   EchoRuntimeBootstrap 会自动补上 GameUI。
            //
            //   空场景是刻意保留的：放任何额外 UI 都会和 GameUI 叠在一起。

            Directory.CreateDirectory(ScenesDir);
            EditorSceneManager.SaveScene(scene, HomePath);

            AssetDatabase.Refresh();

            Debug.Log($"[EchoPuzzle] 已生成 {HomePath}");
        }

        // ------------------------------------------------------------------ Dungeon

        [MenuItem("EchoPuzzle/场景/② 生成 DungeonMode 场景（复制 PuzzleMode）", priority = 101)]
        private static void CreateDungeonScene()
        {
            if (!File.Exists(PuzzlePath))
            {
                EditorUtility.DisplayDialog("EchoPuzzle",
                    $"找不到 {PuzzlePath}。\n\n请先把 Level1 改名为 PuzzleMode。", "好");
                return;
            }

            if (File.Exists(DungeonPath))
            {
                bool replace = EditorUtility.DisplayDialog("EchoPuzzle",
                    $"{DungeonPath} 已经存在，要覆盖吗？（里面的改动会丢失）", "覆盖", "取消");

                if (!replace) return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // ★ 直接复制场景文件：内容一模一样，GUID 会由 Unity 在导入时重新分配
            File.Copy(PuzzlePath, DungeonPath, true);

            AssetDatabase.Refresh();

            Debug.Log($"[EchoPuzzle] 已生成 {DungeonPath}（PuzzleMode 的副本）");
        }

        // ------------------------------------------------------------------ Build Settings

        [MenuItem("EchoPuzzle/场景/③ 把三个场景写进 Build Settings", priority = 102)]
        private static void SetupBuildSettings()
        {
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>();

            // Home 放在第 0 个：打包后启动直接进主页
            AddIfExists(list, HomePath);
            AddIfExists(list, PuzzlePath);
            AddIfExists(list, DungeonPath);

            EditorBuildSettings.scenes = list.ToArray();

            Debug.Log($"[EchoPuzzle] Build Settings 已写入 {list.Count} 个场景：" +
                      string.Join(", ", list.ConvertAll(s => s.path)));
        }

        private static void AddIfExists(System.Collections.Generic.List<EditorBuildSettingsScene> list,
                                        string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[EchoPuzzle] Build Settings：找不到 {path}，已跳过。");
                return;
            }

            list.Add(new EditorBuildSettingsScene(path, true));
        }

        // ------------------------------------------------------------------ 一键

        [MenuItem("EchoPuzzle/场景/全部完成（① + ② + ③）", priority = 99)]
        private static void SetupAll()
        {
            CreateHomeScene();
            CreateDungeonScene();
            SetupBuildSettings();
        }
    }
}
