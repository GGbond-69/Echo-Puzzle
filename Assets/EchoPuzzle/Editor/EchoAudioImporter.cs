using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EchoPuzzle.EditorTools
{
    /// <summary>
    /// 把 `Assets/AudioPack/<分类>/` 里的音频复制到
    /// `Assets/Resources/AudioPack/<分类>/`，供 <see cref="AudioSystem"/> 用 Resources.LoadAll 加载。
    ///
    /// 为什么必须复制到 Resources：
    ///   `Resources.LoadAll` 只能从 **Resources** 目录里读资源。
    ///   而且放进 Resources 还有个好处 —— 这些 clip 一定是"被引用的"，
    ///   打包时不会被剔除（和之前声波 shader 被剔除是同一类问题）。
    ///
    /// 源目录保持不动（`AudioPack` 在项目根目录，Unity 本来也不导入它）。
    /// 重复执行只会刷新内容，不会产生重复文件。
    /// </summary>
    public static class EchoAudioImporter
    {
        private const string SourceRoot = "Assets/AudioPack";
        private const string TargetRoot = "Assets/Resources/AudioPack";

        private static readonly string[] AudioExtensions =
        {
            ".ogg", ".wav", ".mp3", ".m4a", ".aiff", ".aif", ".flac",
        };

        [MenuItem("EchoPuzzle/导入音效到 Resources", priority = 60)]
        public static void Import()
        {
            if (!AssetDatabase.IsValidFolder(SourceRoot))
            {
                EditorUtility.DisplayDialog("EchoPuzzle",
                    $"找不到源目录：\n{SourceRoot}\n\n" +
                    "请确认 AudioPack 文件夹里有 Click / Walk / Run / Stomp / Trigger / Destination / Death 这些子目录。",
                    "好");
                return;
            }

            EnsureFolder(TargetRoot);

            int copied = 0, skipped = 0;
            var categories = new List<string>();

            // 逐个分类目录处理
            string[] subDirs = AssetDatabase.GetSubFolders(SourceRoot);

            foreach (string srcDir in subDirs)
            {
                string category = Path.GetFileName(srcDir);
                string dstDir   = $"{TargetRoot}/{category}";

                // 只处理含音频的目录
                string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { srcDir });
                if (guids.Length == 0)
                {
                    skipped++;
                    continue;
                }

                EnsureFolder(dstDir);
                categories.Add($"{category}({guids.Length})");

                foreach (string guid in guids)
                {
                    string srcPath = AssetDatabase.GUIDToAssetPath(guid);
                    string fileName = Path.GetFileName(srcPath);
                    string dstPath  = $"{dstDir}/{fileName}";

                    // 已经是同一份就跳过
                    if (srcPath == dstPath) continue;

                    AssetDatabase.CopyAsset(srcPath, dstPath);
                    copied++;
                }
            }

            // 也允许音频直接放在 AudioPack 根下（没有分类子目录）
            string[] rootGuids = AssetDatabase.FindAssets("t:AudioClip", new[] { SourceRoot });
            foreach (string guid in rootGuids)
            {
                string srcPath = AssetDatabase.GUIDToAssetPath(guid);
                // 只处理直接位于根目录的（GetSubFolders 已经覆盖了子目录）
                string dir = Path.GetDirectoryName(srcPath).Replace('\\', '/');
                if (dir != SourceRoot) continue;

                string dstPath = $"{TargetRoot}/{Path.GetFileName(srcPath)}";
                if (srcPath == dstPath) continue;

                AssetDatabase.CopyAsset(srcPath, dstPath);
                copied++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[EchoPuzzle] 音效导入完成：复制 {copied} 个文件，跳过 {skipped} 个无音频目录。\n" +
                      $"  分类：{string.Join("、", categories)}\n" +
                      $"  目标：{TargetRoot}/");

            EditorUtility.DisplayDialog("EchoPuzzle",
                $"完成。\n\n复制 {copied} 个音频文件到：\n{TargetRoot}/\n\n" +
                $"分类：{string.Join("、", categories)}\n\n" +
                "现在按 Play 就能听到音效了。",
                "好");
        }

        // ------------------------------------------------------------------ 检查

        [MenuItem("EchoPuzzle/检查音效资源", priority = 61)]
        public static void Verify()
        {
            var lines = new List<string>();
            int ok = 0, bad = 0;

            foreach (AudioSystem.Cue cue in System.Enum.GetValues(typeof(AudioSystem.Cue)))
            {
                string dir = $"{TargetRoot}/{cue}";

                if (!AssetDatabase.IsValidFolder(dir))
                {
                    lines.Add($"  ❌ {cue,-12} 缺少目录 {dir}");
                    bad++;
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { dir });
                if (guids.Length == 0)
                {
                    lines.Add($"  ❌ {cue,-12} 目录里没有音频文件");
                    bad++;
                    continue;
                }

                var names = new List<string>();
                foreach (string g in guids)
                {
                    names.Add(Path.GetFileName(AssetDatabase.GUIDToAssetPath(g)));
                }

                lines.Add($"  ✅ {cue,-12} {guids.Length} 条：{string.Join("、", names)}");
                ok++;
            }

            Debug.Log($"[EchoPuzzle] 音效检查：{ok} 个分类正常，{bad} 个缺失。\n" +
                      string.Join("\n", lines) +
                      (bad > 0 ? "\n\n执行菜单：EchoPuzzle → 导入音效到 Resources" : ""));
        }

        // ------------------------------------------------------------------ 工具

        /// <summary>逐级创建目录（Assets/Resources、Assets/Resources/AudioPack/...）。</summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf   = Path.GetFileName(path);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
