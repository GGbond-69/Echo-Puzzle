using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle.Dungeon
{
    /// <summary>
    /// 【地牢模式】玩家已获得的 buff，以及它们叠出来的最终数值。
    ///
    /// ── 分工 ────────────────────────────────────────────────
    ///   · 这里只负责**累加**：把 N 条 buff 的乘区乘起来、加法加起来
    ///   · <see cref="DungeonWeapon"/> 每开一枪读一次这些值，不自己存
    ///   · 谁想加 buff 就 <see cref="Add"/>，谁想清空就 <see cref="Clear"/>
    ///
    /// ── 叠加规则 ────────────────────────────────────────────
    ///   乘区相乘：两个「蓝耗 -30%」= 0.7 × 0.7 = 0.49
    ///   加法相加：两个「蓝上限 +50」= +100
    ///   同一 buff 能叠几层由它自己的 maxStacks 决定（抽卡时会过滤）。
    /// </summary>
    [AddComponentMenu("Echo Puzzle/Dungeon/Dungeon Buff State")]
    public sealed class DungeonBuffState : MonoBehaviour
    {
        public static DungeonBuffState Instance { get; private set; }

        /// <summary>buff 列表或数值变化时触发（UI 想刷新就挂这个）。</summary>
        public event System.Action Changed;

        private readonly List<DungeonBuffDef> m_Owned = new List<DungeonBuffDef>(16);

        /// <summary>已获得的 buff（按获得顺序，允许重复 id）。</summary>
        public IReadOnlyList<DungeonBuffDef> Owned => m_Owned;

        /// <summary>一共拿了几个。</summary>
        public int Count => m_Owned.Count;

        // ---- 叠完之后的最终乘区（1 = 无效果）----

        public float DamageMul      { get; private set; } = 1f;
        public float FireRateMul    { get; private set; } = 1f;
        public float ManaCostMul    { get; private set; } = 1f;
        public float KillManaMul    { get; private set; } = 1f;
        public float BulletSpeedMul { get; private set; } = 1f;
        public float BulletWidthMul { get; private set; } = 1f;

        /// <summary>蓝上限加成（加法）。</summary>
        public int MaxManaAdd { get; private set; }

        /// <summary>上一次已经应用过的上限加成，用来算「这次又涨了多少」。</summary>
        private int m_AppliedMaxBonus;

        // ------------------------------------------------------------------ 生命周期

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            Recompute();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ 对外

        /// <summary>某个 buff 已经叠了几层。</summary>
        public int StacksOf(DungeonBuffDef def)
        {
            if (def == null) return 0;

            int n = 0;
            for (int i = 0; i < m_Owned.Count; i++)
                if (m_Owned[i] != null && m_Owned[i].id == def.id) n++;

            return n;
        }

        /// <summary>获得一条 buff。</summary>
        public void Add(DungeonBuffDef def)
        {
            if (def == null) return;

            m_Owned.Add(def);
            Recompute();
        }

        /// <summary>清空（重开一局 / 换层不保留时用）。</summary>
        public void Clear()
        {
            if (m_Owned.Count == 0) return;

            m_Owned.Clear();
            Recompute();
        }

        /// <summary>把已获得 buff 的乘区重新算一遍。</summary>
        public void Recompute()
        {
            float dmg = 1f, rate = 1f, mana = 1f, kill = 1f, speed = 1f, width = 1f;
            int   maxAdd = 0;

            for (int i = 0; i < m_Owned.Count; i++)
            {
                DungeonBuffDef b = m_Owned[i];
                if (b == null) continue;

                dmg   *= b.damageMul;
                rate  *= b.fireRateMul;
                mana  *= b.manaCostMul;
                kill  *= b.killManaMul;
                speed *= b.bulletSpeedMul;
                width *= b.bulletWidthMul;

                maxAdd += b.maxManaAdd;
            }

            DamageMul      = dmg;
            FireRateMul    = rate;
            ManaCostMul    = mana;
            KillManaMul    = kill;
            BulletSpeedMul = speed;
            BulletWidthMul = width;

            MaxManaAdd = maxAdd;

            ApplyMaxMana(maxAdd);

            Changed?.Invoke();
        }

        /// <summary>
        /// 把「蓝上限加成」落实到 <see cref="DungeonMana"/> 上。
        ///
        /// ★ 上限涨了要把**涨出来的这部分补成实蓝**，
        ///   否则拿了「扩容电池」只是条变长、当前蓝没变，玩家会觉得没生效。
        /// </summary>
        private void ApplyMaxMana(int add)
        {
            DungeonMana mana = DungeonMana.Instance;
            if (mana == null) return;

            int delta = add - m_AppliedMaxBonus;
            if (delta == 0) return;

            m_AppliedMaxBonus = add;

            mana.SetMax(mana.BaseMax + add);

            if (delta > 0) mana.Restore(delta);
        }
    }
}