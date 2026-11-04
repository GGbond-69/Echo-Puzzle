using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 挂在任意物体上，标记它当前是"启用"还是"删除"。
    ///
    /// ★ 只有这一个效果，不做别的事。
    ///
    /// ── Hiding 的含义 ──────────────────────────────────────
    ///   Hiding = 0  ->  物体**启用**（正常出现在场景里）
    ///   Hiding = 1  ->  物体**删除**（不可见、不参与物理、不与外界交互）
    ///
    ///   物体的实际激活状态**完全由 Hiding 决定**。
    ///
    ///   "删除"只是 SetActive(false)，**不是真的 Destroy** ——
    ///   物体一直在场景里，可以无限次启用 / 删除。
    ///
    /// ── 谁改 Hiding ─────────────────────────────────────────
    ///   · 开局：按各物体的 Hiding 值初始化（见 TriggerVolume.HideAllMarkedObjects）
    ///   · TriggerVolume 的 OperateObjects 效果：
    ///       待启用的物体 -> Hiding = 0
    ///       待删除的物体 -> Hiding = 1
    ///
    /// ── 编辑器里 ────────────────────────────────────────────
    ///   ★ Hiding = 1 时**不会在 Scene 视图里隐藏物体**，
    ///     方便摆地图时看清楚位置。只有进 Play 才会真正消失。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Echo Puzzle/Hide")]
    public class Hide : MonoBehaviour
    {
        [Tooltip("0 = 启用（正常显示）\n1 = 删除（不可见、不参与物理）\n\n" +
                 "★ 改这个值会同步改变物体的激活状态。\n" +
                 "  但**编辑状态下不会真的隐藏** —— 摆地图时照样看得见，\n" +
                 "  进 Play 后才按这个值生效。")]
        [Range(0, 1)]
        public int hiding = 0;

        /// <summary>当前是否处于"删除"状态。等价于 <c>hiding != 0</c>。</summary>
        public bool IsHiding => hiding != 0;

        /// <summary>
        /// 本组件被 SetHiding(0) 解除隐藏时的**全局回调**。
        ///
        /// ★ 用它在"解除隐藏"这一刻做清理 —— 不需要时间戳之类间接手段。
        /// </summary>
        public static event System.Action<Hide> Unhidden;

        /// <summary>
        /// 从"删除"变成"启用"的那一刻是否已经发生过（本次隐藏周期内）。
        /// 供 PuzzleMonster 判断"是否需要丢弃隐藏期间记住的东西"。
        /// </summary>
        public int HiddenGeneration { get; private set; }

        /// <summary>
        /// 把 Hiding 设为指定值，并同步物体的激活状态。
        ///
        /// ★ 只在**运行时**才真的 SetActive；编辑状态下只改数值，
        ///   保证摆地图时模型依然可见。
        /// </summary>
        public void SetHiding(int value)
        {
            int before = hiding;

            hiding = value != 0 ? 1 : 0;

            if (!Application.isPlaying) return;      // 编辑期不隐藏，方便摆地图

            ApplyActiveState();

            // ★ 从"删除"(1) 变成"启用"(0)：通知所有监听者
            //
            //   用来丢弃"隐藏期间记住的一切"（比如怪物记住的声源坐标）。
            if (before != 0 && hiding == 0)
            {
                HiddenGeneration++;
                Unhidden?.Invoke(this);
            }
        }

        /// <summary>
        /// 按当前 hiding 同步 **activeSelf**，并在需要启用时把祖先链也确保启用。
        ///
        /// ★ 为什么还要管祖先：
        ///   物体的实际可见性 = activeSelf && 所有祖先的 activeSelf。
        ///   如果只改自己的 activeSelf，而某个祖先还关着，
        ///   物体依然不可见 —— 表现为"hiding 是 0 但物体没启用"。
        /// </summary>
        private void ApplyActiveState()
        {
            bool shouldBeActive = hiding == 0;

            // 自己的 activeSelf
            if (gameObject.activeSelf != shouldBeActive)
                gameObject.SetActive(shouldBeActive);

            // 启用时，把整条祖先链也打开（沿路遇到关闭的就打开）
            if (shouldBeActive)
            {
                Transform p = transform.parent;

                while (p != null)
                {
                    if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
                    p = p.parent;
                }
            }
        }

        // ---------------------------------------------------------------- 旧字段迁移
        //
        // 旧版本用的是 bool 字段 hideOnStart。这里保留它**只用于反序列化迁移**：
        // 场景里已有的 Hide 组件会沿袭原本的 hideOnStart 值。
        //
        //   hideOnStart = true   ->  hiding = 1
        //   hideOnStart = false  ->  hiding = 0

        [SerializeField, HideInInspector] private bool hideOnStart = false;

        [SerializeField, HideInInspector] private bool hidingMigrated = false;

        private void Awake()
        {
            MigrateLegacy();

            // 运行时：按 Hiding 初始化激活状态
            if (Application.isPlaying)
            {
                bool shouldBeActive = hiding == 0;

                if (gameObject.activeSelf != shouldBeActive)
                    gameObject.SetActive(shouldBeActive);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            MigrateLegacy();

            // ★ 编辑状态下**不**改激活状态 ——
            //   否则把 Hiding 设成 1 时模型就看不见了，没法摆地图。
            hiding = hiding != 0 ? 1 : 0;
        }
#endif

        /// <summary>第一次遇到旧值时把 hideOnStart 搬到 hiding。</summary>
        private void MigrateLegacy()
        {
            if (hidingMigrated) return;

            hiding = hideOnStart ? 1 : 0;
            hidingMigrated = true;
        }
    }
}
