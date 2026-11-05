using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle.Dungeon
{
    /// <summary>
    /// 【地牢模式】一条 buff 的定义（纯数据，不是 ScriptableObject）。
    ///
    /// ── 为什么不用 ScriptableObject ──────────────────────────
    ///   文档要求"不要往场景 / 工程里加东西，一切代码生成"，而且现在只是
    ///   搭框架阶段。放在代码里改一个字就能加一条 buff，不用建资产、不用管 GUID。
    ///   以后要交给策划配表，再把这个类换成 SO 即可，字段完全一致。
    ///
    /// ── 数值怎么表达 ────────────────────────────────────────
    ///   **乘区**：1 = 不变。0.7 = 减 30%，1.5 = 加 50%。
    ///   可叠加的 buff 就是多次乘上去（0.7 × 0.7 = 0.49）。
    ///
    ///   ★ 文档要求"越强的 buff 蓝耗越大" —— 实现方式就是：
    ///     强效果的那条同时把 <see cref="manaCostMul"/> 设成 &gt; 1。
    /// </summary>
    public sealed class DungeonBuffDef
    {
        public string id   = "buff";
        public string name = "未命名";
        public string desc = "";

        /// <summary>抽卡权重（越大越常见）。</summary>
        [Range(0.01f, 100f)] public float weight = 1f;

        /// <summary>最多能叠几层。</summary>
        [Min(1)] public int maxStacks = 5;

        // ---- 效果（乘区）----

        /// <summary>伤害倍率。</summary>
        public float damageMul = 1f;

        /// <summary>射速倍率（&gt;1 = 更快，内部是 fireInterval ÷ 本值）。</summary>
        public float fireRateMul = 1f;

        /// <summary>蓝耗倍率（&lt;1 = 更省，&gt;1 = 更费）。</summary>
        public float manaCostMul = 1f;

        /// <summary>击杀回蓝倍率。</summary>
        public float killManaMul = 1f;

        /// <summary>子弹速度倍率。</summary>
        public float bulletSpeedMul = 1f;

        /// <summary>拖尾宽度倍率（纯视觉，但粗一点更容易看清弹道）。</summary>
        public float bulletWidthMul = 1f;

        /// <summary>蓝上限加成（加法，每次加这么多）。</summary>
        public int maxManaAdd;

        public bool HasEffect =>
            !Mathf.Approximately(damageMul, 1f) ||
            !Mathf.Approximately(fireRateMul, 1f) ||
            !Mathf.Approximately(manaCostMul, 1f) ||
            !Mathf.Approximately(killManaMul, 1f) ||
            !Mathf.Approximately(bulletSpeedMul, 1f) ||
            !Mathf.Approximately(bulletWidthMul, 1f) ||
            maxManaAdd != 0;
    }

    /// <summary>
    /// 【地牢模式】buff 池 + 抽卡。
    ///
    /// 现在放的是 8 条"足够验证框架"的 buff，效果都不重样，且能互相叠加。
    /// 加新 buff 只要往 <see cref="All"/> 里加一行。
    /// </summary>
    public static class DungeonBuffPool
    {
        public static readonly List<DungeonBuffDef> All = new List<DungeonBuffDef>
        {
            // ---------------- 进攻类 ----------------

            new DungeonBuffDef
            {
                id = "dmg", name = "高爆弹头",
                desc = "伤害 +50%",
                weight = 10f, maxStacks = 5,
                damageMul = 1.5f,
            },

            new DungeonBuffDef
            {
                id = "rate", name = "快扳机",
                desc = "射速 +25%",
                weight = 10f, maxStacks = 5,
                fireRateMul = 1.25f,
            },

            new DungeonBuffDef
            {
                id = "heavy", name = "重弹",
                desc = "伤害 +90%，但蓝耗 ×1.6",
                weight = 6f, maxStacks = 3,
                damageMul = 1.9f,
                manaCostMul = 1.6f,
            },

            // ---------------- 资源类 ----------------

            new DungeonBuffDef
            {
                id = "eco", name = "省电模式",
                desc = "蓝耗 -30%",
                weight = 10f, maxStacks = 3,
                manaCostMul = 0.7f,
            },

            new DungeonBuffDef
            {
                id = "recycle", name = "回收装置",
                desc = "击杀回蓝 +60%",
                weight = 9f, maxStacks = 4,
                killManaMul = 1.6f,
            },

            new DungeonBuffDef
            {
                id = "battery", name = "扩容电池",
                desc = "蓝上限 +50（并立即补满这些蓝）",
                weight = 8f, maxStacks = 4,
                maxManaAdd = 50,
            },

            // ---------------- 手感 / 视觉 ----------------

            new DungeonBuffDef
            {
                id = "velocity", name = "高初速",
                desc = "子弹速度 +50%（更好打移动目标）",
                weight = 8f, maxStacks = 3,
                bulletSpeedMul = 1.5f,
            },

            new DungeonBuffDef
            {
                id = "beam", name = "粗光束",
                desc = "拖尾 +80%，蓝耗 -10%",
                weight = 7f, maxStacks = 2,
                bulletWidthMul = 1.8f,
                manaCostMul = 0.9f,
            },
        };

        /// <summary>
        /// 抽 <paramref name="count"/> 条**互不相同**的 buff。
        /// 已经叠满的（达到 maxStacks）不会再出现。
        /// </summary>
        public static List<DungeonBuffDef> Roll(int count, DungeonBuffState state)
        {
            var result = new List<DungeonBuffDef>(count);
            if (count <= 0) return result;

            // 候选：过滤掉叠满的
            var candidates = new List<DungeonBuffDef>(All.Count);
            for (int i = 0; i < All.Count; i++)
            {
                DungeonBuffDef d = All[i];
                if (d == null) continue;
                if (state != null && state.StacksOf(d) >= d.maxStacks) continue;
                candidates.Add(d);
            }

            for (int n = 0; n < count && candidates.Count > 0; n++)
            {
                // 加权随机挑一条，挑完移出候选，保证三张不重复
                float total = 0f;
                for (int i = 0; i < candidates.Count; i++) total += Mathf.Max(0.01f, candidates[i].weight);

                float pick = Random.value * total;
                int chosen = candidates.Count - 1;

                for (int i = 0; i < candidates.Count; i++)
                {
                    pick -= Mathf.Max(0.01f, candidates[i].weight);
                    if (pick <= 0f) { chosen = i; break; }
                }

                result.Add(candidates[chosen]);
                candidates.RemoveAt(chosen);
            }

            return result;
        }
    }
}