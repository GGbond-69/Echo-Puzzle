using UnityEngine;

namespace EchoPuzzle.Dungeon
{
    /// <summary>
    /// 【地牢模式】蓝量池。
    ///
    /// ── 职责 ────────────────────────────────────────────────
    ///   · 存一个 0..max 的整数蓝量
    ///   · 给武器提供 <see cref="TrySpend"/>，给击杀回复提供 <see cref="Restore"/>
    ///   · 变化时发 <see cref="Changed"/>，HUD 只负责画，不存数据
    ///
    /// ── 和探秘模式的关系 ────────────────────────────────────
    ///   ★ 与 <see cref="BatterySystem"/>（探秘模式的电池）**没有任何关系**。
    ///     那个是"捡到才有光"的道具，这个是地牢模式的技能资源；
    ///     两者不会在同一个模式里同时出现，也不共享数值。
    /// </summary>
    [AddComponentMenu("Echo Puzzle/Dungeon/Dungeon Mana")]
    public sealed class DungeonMana : MonoBehaviour
    {
        public static DungeonMana Instance { get; private set; }

        [Header("容量")]
        [Min(1)] public int maxMana = 100;

        [Tooltip("开局蓝量。重开一局（ResetMana）也会回到这个值")]
        [Min(0)] public int startMana = 100;

        /// <summary>当前蓝量。</summary>
        public int Current { get; private set; }

        /// <summary>上限（至少 1，避免除零）。</summary>
        public int Max => Mathf.Max(1, maxMana);

        /// <summary>0~1，给 HUD 画条用。</summary>
        public float Ratio01 => Mathf.Clamp01((float)Current / Max);

        /// <summary>蓝量变化（当前, 最大）。</summary>
        public event System.Action<int, int> Changed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // 场景里已经有了一份（比如手滑挂了两个）—— 多余的自己消失
                Destroy(this);
                return;
            }

            Instance = this;

            Current = Mathf.Clamp(startMana, 0, Max);
            Changed?.Invoke(Current, Max);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>蓝够不够。</summary>
        public bool CanAfford(int cost) => Current >= Mathf.Max(0, cost);

        /// <summary>够就扣，返回是否真的扣了。</summary>
        public bool TrySpend(int cost)
        {
            cost = Mathf.Max(0, cost);
            if (Current < cost) return false;

            Set(Current - cost);
            return true;
        }

        /// <summary>回蓝（击杀怪物 / 通关奖励）。</summary>
        public void Restore(int amount)
        {
            if (amount <= 0) return;
            Set(Current + amount);
        }

        /// <summary>回到开局蓝量（重开 / 换层用）。</summary>
        public void ResetMana() => Set(Mathf.Clamp(startMana, 0, Max));

        /// <summary>改上限（buff 用）。当前值会被夹到新上限内。</summary>
        public void SetMax(int newMax)
        {
            maxMana = Mathf.Max(1, newMax);
            Set(Current);
        }

        private void Set(int value)
        {
            value = Mathf.Clamp(value, 0, Max);
            if (value == Current) return;

            Current = value;
            Changed?.Invoke(Current, Max);
        }
    }
}