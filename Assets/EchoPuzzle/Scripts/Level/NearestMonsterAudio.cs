using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 「只听最近的 N 个怪物」的判定中心。
    ///
    /// ── 需求 ────────────────────────────────────────────────
    ///   玩家只会接收到离他**最近的三个 hiding = 0 的怪物**的音效。
    ///   ★ 两个模式（探秘 / 地牢）都一样。
    ///
    /// ── 为什么集中算 ────────────────────────────────────────
    ///   每个 MonsterAudio 都要知道自己排第几。如果各自去扫全场，
    ///   20 个怪物就是每帧 20 次全场景查找 —— 这里统一算一次，
    ///   所有怪物共享同一个结果。
    ///
    /// ── 什么算"参与排名"的怪物 ──────────────────────────────
    ///   · 有 MonsterAudio 组件（在播怪物音效）
    ///   · 物体是激活的（activeInHierarchy）
    ///   · Hide.hiding == 0（没被"删除"）
    ///
    ///   ★ 被 hiding = 1 删除的怪物不参与排名，所以它不会顶掉
    ///     别人的名额，也不会发声。
    /// </summary>
    public static class NearestMonsterAudio
    {
        /// <summary>最多同时能听到几个怪物的音效。</summary>
        public const int MaxAudible = 3;

        /// <summary>重新计算的时间间隔（秒）。0.1 秒足够，肉眼看不出跳过。</summary>
        private const float RecalcInterval = 0.1f;

        private static readonly List<MonsterAudio> s_All = new List<MonsterAudio>(16);
        private static readonly HashSet<MonsterAudio> s_Audible = new HashSet<MonsterAudio>();

        private static float m_NextRecalc;

        /// <summary>强制下一帧重算（怪物增减时调）。</summary>
        public static void Invalidate()
        {
            m_NextRecalc = 0f;
        }

        /// <summary>
        /// 这个 MonsterAudio 现在能不能发出声音。
        /// 排不进最近的 <see cref="MaxAudible"/> 个就返回 false。
        /// </summary>
        public static bool IsAudible(MonsterAudio audio)
        {
            if (audio == null) return false;

            RecalculateIfNeeded();

            return s_Audible.Contains(audio);
        }

        /// <summary>每隔 <see cref="RecalcInterval"/> 秒重算一次排名。</summary>
        private static void RecalculateIfNeeded()
        {
            float now = Time.unscaledTime;
            if (now < m_NextRecalc) return;

            m_NextRecalc = now + RecalcInterval;

            s_Audible.Clear();

            PlayerController player = FindPlayer();
            if (player == null) return;

            Vector3 playerPos = player.transform.position;

            // ---- 收集所有"参与排名"的怪物 ----
            s_All.Clear();

            MonsterAudio[] all = Object.FindObjectsOfType<MonsterAudio>();
            for (int i = 0; i < all.Length; i++)
            {
                MonsterAudio a = all[i];
                if (a == null) continue;
                if (!a.isActiveAndEnabled) continue;

                // ★ hiding = 1（被"删除"）的不参与排名
                Hide h = a.GetComponent<Hide>();
                if (h != null && h.hiding != 0) continue;

                s_All.Add(a);
            }

            if (s_All.Count == 0) return;

            // ---- 按到玩家的距离排序，取前 MaxAudible 个 ----
            //
            //   数量很小（几十个），直接插入排序即可，不用 List.Sort 分配委托。
            int take = Mathf.Min(MaxAudible, s_All.Count);

            for (int k = 0; k < take; k++)
            {
                int   best     = k;
                float bestDist = -1f;

                for (int i = k; i < s_All.Count; i++)
                {
                    float d = (s_All[i].transform.position - playerPos).sqrMagnitude;

                    if (bestDist < 0f || d < bestDist)
                    {
                        bestDist = d;
                        best     = i;
                    }
                }

                // 换到第 k 位
                if (best != k)
                {
                    MonsterAudio tmp = s_All[k];
                    s_All[k] = s_All[best];
                    s_All[best] = tmp;
                }

                s_Audible.Add(s_All[k]);
            }
        }

        private static PlayerController FindPlayer()
        {
            return Object.FindObjectOfType<PlayerController>();
        }
    }
}
