using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 让所有怪物**彼此不碰撞**（可以完全重叠），但仍然与墙 / 玩家碰撞。
    ///
    /// ── 为什么需要这个 ──────────────────────────────────────
    ///   怪物用 CharacterController 移动，它会和**同层**的 Collider 推挤。
    ///   两只怪物互相撞 = 走不过去、挤成一团，与需求相反。
    ///
    ///   最干净的做法是把怪物放到一个**独立物理层**，然后在
    ///   Physics 的碰撞矩阵里，把"怪物层 vs 怪物层"勾掉。
    ///   但碰撞矩阵只能手动在 Project Settings 里改，
    ///   运行时改不了，所以这里改用 <see cref="Physics.IgnoreCollision"/>。
    ///
    /// ── 做法 ────────────────────────────────────────────────
    ///   每只怪物在 Awake 里注册。注册表更新时，
    ///   把**所有怪物对**两两设为互相忽略。
    ///
    ///   新怪物是运行中创建的也没问题 —— 每次注册都会重新算一遍全部配对，
    ///   已有的配对重复设置是幂等的。
    /// </summary>
    public static class MonsterCollisionGroup
    {
        private static readonly System.Collections.Generic.List<PuzzleMonster> s_Monsters
            = new System.Collections.Generic.List<PuzzleMonster>();

        /// <summary>注册一只怪物，并把它的所有碰撞体与其它怪物互相忽略。</summary>
        public static void Register(PuzzleMonster monster)
        {
            if (monster == null) return;

            // 先清掉已销毁的
            s_Monsters.RemoveAll(m => m == null);

            if (!s_Monsters.Contains(monster)) s_Monsters.Add(monster);

            Apply();
        }

        /// <summary>注销（怪物被销毁时）。</summary>
        public static void Unregister(PuzzleMonster monster)
        {
            s_Monsters.Remove(monster);
        }

        /// <summary>把所有怪物之间的碰撞设成"互相忽略"。</summary>
        private static void Apply()
        {
            for (int i = 0; i < s_Monsters.Count; i++)
            {
                Collider[] a = s_Monsters[i].GetComponentsInChildren<Collider>(true);
                if (a.Length == 0) continue;

                for (int j = i + 1; j < s_Monsters.Count; j++)
                {
                    Collider[] b = s_Monsters[j].GetComponentsInChildren<Collider>(true);
                    if (b.Length == 0) continue;

                    for (int x = 0; x < a.Length; x++)
                    {
                        if (a[x] == null) continue;

                        for (int y = 0; y < b.Length; y++)
                        {
                            if (b[y] == null) continue;
                            Physics.IgnoreCollision(a[x], b[y], true);
                        }
                    }
                }
            }
        }

        /// <summary>重玩时清空注册表（怪物物体被复用，但引用可能失效）。</summary>
        public static void ResetRegistry()
        {
            s_Monsters.Clear();
        }
    }
}
