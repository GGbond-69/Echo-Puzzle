using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 关卡终点：玩家进入触发区即通关。
    /// 通关界面（YOU WIN! + 通关时间 + 按钮）由 GameUI 绘制，这里只负责通知它。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class LevelGoal : MonoBehaviour
    {
        [Tooltip("通关后是否冻结玩家操作。GameUI 会接管光标，默认不需要冻结。")]
        public bool freezePlayerOnClear = false;

        public bool Cleared { get; private set; }

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (Cleared) return;

            // 玩家身上挂的是 CharacterController（本身也是 Collider）
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;

            // 只在进行中才算通关；首页 / 暂停 / 已通关都不触发
            if (GameUI.Current != GameState.Playing) return;

            Cleared = true;

            if (freezePlayerOnClear) player.enabled = false;

            GameUI.NotifyGoalReached();
        }

        /// <summary>重玩时调用，让终点可以再次触发。</summary>
        public void ResetCleared()
        {
            Cleared = false;
        }
    }
}
