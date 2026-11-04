using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 运行时自动给"点 Play 后才新建的物体"挂上 <see cref="Hide"/>。
    ///
    /// ── 为什么需要它 ────────────────────────────────────────
    ///   场景里已有的物体可以用菜单工具批量挂 Hide，
    ///   但有些物体是**进 Play 之后才生成的**（关卡生成、运行时补的 UI 等），
    ///   它们身上不会有 Hide，于是 OperateObjects 就管不到它们。
    ///
    ///   这里每帧检查一次场景里所有物体的 Hide 组件，
    ///   发现还没挂的就补上（默认 hiding = 0，也就是启用 —— 不改变它的显示状态）。
    ///
    /// ── 实现要点 ────────────────────────────────────────────
    ///   · 用 <see cref="Resources.FindObjectsOfTypeAll{T}()"/> 扫场景，
    ///     只处理**当前激活场景**里的物体，跳过 Prefab 资产 / 隐藏对象
    ///   · 每个物体只查一次 `GetComponent<Hide>()`，命中就跳过
    ///   · 用一个"已检查过的实例 ID"集合做缓存，避免每帧重复查
    /// </summary>
    [DefaultExecutionOrder(-450)]
    public static class HideAutoAttach
    {
        /// <summary>总开关。关掉就不会自动补 Hide。</summary>
        public static bool Enabled = true;

        /// <summary>已经检查过的物体（实例 ID），避免每帧重复扫。</summary>
        private static readonly System.Collections.Generic.HashSet<int> s_Checked
            = new System.Collections.Generic.HashSet<int>();

        /// <summary>上一次扫描的场景句柄，换场景时清空缓存。</summary>
        private static UnityEngine.SceneManagement.Scene s_LastScene;

        /// <summary>
        /// 每帧扫一次。
        ///
        /// 用 RuntimeInitializeOnLoadMethod 挂一次循环，不必往场景里塞物体。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Enabled) return;

            // 换场景时由 SceneManager 重置缓存
            s_LastScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            s_Checked.Clear();

            var driverGo = new GameObject("[EchoPuzzle] HideAutoAttach");
            Object.DontDestroyOnLoad(driverGo);
            driverGo.hideFlags = HideFlags.HideAndDontSave;
            driverGo.AddComponent<Driver>();
        }

        /// <summary>立即扫一遍（不做帧节流），给 Bootstrap 用完可以手动调一次。</summary>
        public static void ScanNow()
        {
            if (!Enabled) return;

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            if (scene != s_LastScene)
            {
                s_LastScene = scene;
                s_Checked.Clear();
            }

            // 拿到场景里所有的 Hide（含未激活物体上的）
            // 注意：这里要扫的是"没有 Hide 的物体"，所以不能只扫 Hide。

            GameObject[] roots = scene.GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null) continue;
                Walk(roots[i].transform);
            }
        }

        private static void Walk(Transform t)
        {
            if (t == null) return;

            GameObject go = t.gameObject;

            int id = go.GetInstanceID();

            if (!s_Checked.Contains(id))
            {
                s_Checked.Add(id);

                if (go.GetComponent<Hide>() == null)
                {
                    // 默认 hiding = 0（启用）—— 不会改变它当前的显示状态
                    var h = go.AddComponent<Hide>();
                    h.hiding = 0;
                }
            }

            for (int i = 0; i < t.childCount; i++)
            {
                Walk(t.GetChild(i));
            }
        }

        /// <summary>挂在隐藏物体上的驱动器，每帧调一次 ScanNow。</summary>
        private class Driver : MonoBehaviour
        {
            private void Update() => ScanNow();
        }
    }
}
