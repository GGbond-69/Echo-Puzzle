using UnityEngine;

namespace EchoPuzzle.Dungeon
{
    /// <summary>
    /// 【地牢模式】玩家的生命值。
    ///
    /// ── 和探秘模式的关系 ────────────────────────────────────
    ///   ★ 这是一个**完全独立**的组件，只在 DungeonMode 场景里使用。
    ///     探秘模式（PuzzleMode）不挂它，也不受影响。
    ///
    /// ── 规则 ────────────────────────────────────────────────
    ///   · 默认生命值 6
    ///   · 子弹 / 任何伤害调 <see cref="TakeDamage"/>
    ///   · 降到 0 时：
    ///       - 在玩家脚下释放一次**探秘模式同样的死亡光带**
    ///         （SonarSystem.DefaultTrap 那一套）
    ///       - 播放**探秘模式同样的死亡音效**（AudioSystem.Cue.Death）
    ///       - 调用 GameUI.NotifyPlayerDied() 进入死亡界面
    ///
    /// ── 适用范围 ────────────────────────────────────────────
    ///   ★ 只处理"生命值 / 受伤 / 死亡表现"。
    ///     枪械、子弹、怪物 AI 由别的脚本负责（见下方 TODO）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Echo Puzzle/Dungeon/Player Health")]
    public sealed class PlayerHealth : MonoBehaviour
    {
        // ------------------------------------------------------------------ 配置

        [Header("生效范围")]
        [Tooltip("只在**地牢模式（DungeonMode）**里生效。\n\n" +
                 "★ 这个脚本可以安全地挂在 Player.prefab 上 ——\n" +
                 "  在探秘模式（PuzzleMode）里它会自动变成空壳：\n" +
                 "    · 不响应任何伤害\n" +
                 "    · 不触发死亡\n" +
                 "    · 不影响探秘模式的任何玩法")]
        public bool dungeonModeOnly = true;

        [Header("生命值")]
        [Tooltip("最大生命值")]
        [Min(1)] public int maxHealth = 6;

        [Tooltip("开局生命值。留 -1 表示用 Max Health")]
        public int startingHealth = -1;

        [Tooltip("受伤后多久内免疫再次伤害（秒）。0 = 不免疫")]
        [Min(0f)] public float invincibleDuration = 0.5f;

        [Header("死亡表现")]
        [Tooltip("死亡时在脚下释放光带。取消勾选则只播音效 + 进死亡界面。")]
        public bool emitDeathSonar = true;

        [Tooltip("死亡光带的参数。\n\n" +
                 "默认用 SonarSystem.DefaultTrap —— 和探秘模式里\n" +
                 "「踩陷阱 / 被怪物击杀」是**同一套**视觉。")]
        public SonarEmitSettings deathSonar = SonarEmitSettings.DefaultTrap;

        // ------------------------------------------------------------------ 运行时

        /// <summary>当前生命值。</summary>
        public int Current { get; private set; }

        /// <summary>已经死了吗。</summary>
        public bool IsDead => Current <= 0;

        /// <summary>
        /// 这个组件当前**是否生效**。
        ///
        /// ★ 挂在 Player.prefab 上的同一个实例，在探秘模式里返回 false，
        ///   地牢模式里返回 true。所有公开接口都先过这一关。
        /// </summary>
        public bool IsEnabledHere => !dungeonModeOnly || GameUI.IsDungeonMode;

        /// <summary>生命值变化时触发（当前值、最大值）。给 HUD 用。</summary>
        public event System.Action<int, int> HealthChanged;

        private PlayerController m_Player;
        private float           m_InvincibleUntil;
        private bool            m_DeathHandled;

        // ------------------------------------------------------------------ 生命周期

        private void Awake()
        {
            m_Player = GetComponent<PlayerController>();
            if (m_Player == null) m_Player = GetComponentInParent<PlayerController>();
            if (m_Player == null) m_Player = FindObjectOfType<PlayerController>();

            Current = startingHealth >= 0
                    ? Mathf.Clamp(startingHealth, 0, maxHealth)
                    : maxHealth;

            HealthChanged?.Invoke(Current, maxHealth);
        }

        private void Start()
        {
            // ★ 探秘模式下这个组件是空壳，不用推事件
            if (!IsEnabledHere) return;

            // Awake 时 HUD 可能还没建好，这里再推一次
            HealthChanged?.Invoke(Current, maxHealth);
        }

        // ------------------------------------------------------------------ 对外接口

        /// <summary>
        /// 扣血。
        /// </summary>
        /// <param name="amount">扣多少（正数）。</param>
        /// <returns>这次伤害有没有真的扣到血（无敌期间 / 已死会返回 false）。</returns>
        public bool TakeDamage(int amount)
        {
            // ★ 探秘模式下这个组件不生效
            if (!IsEnabledHere) return false;

            if (IsDeadOrDone()) return false;
            if (amount <= 0) return false;

            // 无敌期间不掉血
            if (Time.timeSinceLevelLoad < m_InvincibleUntil) return false;

            m_InvincibleUntil = Time.timeSinceLevelLoad + Mathf.Max(0f, invincibleDuration);

            Current = Mathf.Max(0, Current - amount);

            HealthChanged?.Invoke(Current, maxHealth);

            if (Current <= 0) Die();

            return true;
        }

        /// <summary>回血（不会超过最大值）。</summary>
        public void Heal(int amount)
        {
            // ★ 探秘模式下这个组件不生效
            if (!IsEnabledHere) return;

            if (IsDeadOrDone() || amount <= 0) return;

            Current = Mathf.Min(maxHealth, Current + amount);

            HealthChanged?.Invoke(Current, maxHealth);
        }

        /// <summary>直接设成某个值（读档 / 重开时用）。</summary>
        public void SetHealth(int value)
        {
            Current = Mathf.Clamp(value, 0, maxHealth);

            HealthChanged?.Invoke(Current, maxHealth);
        }

        /// <summary>满血并清掉死亡标记（重开时用）。</summary>
        public void ResetHealth()
        {
            m_DeathHandled    = false;
            m_InvincibleUntil = 0f;

            Current = startingHealth >= 0
                    ? Mathf.Clamp(startingHealth, 0, maxHealth)
                    : maxHealth;

            HealthChanged?.Invoke(Current, maxHealth);
        }

        /// <summary>已经死过（死亡表现已触发）或者当前血量为 0。</summary>
        private bool IsDeadOrDone() => m_DeathHandled || Current <= 0;

        // ------------------------------------------------------------------ 死亡

        /// <summary>
        /// 死亡：光带 + 音效 + 死亡界面。
        ///
        /// ★ 表现和探秘模式**完全一致** ——
        ///   用的就是 TriggerVolume 里 Lose 效果那一套：
        ///     · 光带：SonarSystem.DefaultTrap（红色细光柱）
        ///     · 音效：AudioSystem.Cue.Death
        ///     · 界面：GameUI.NotifyPlayerDied()
        /// </summary>
        private void Die()
        {
            // ★ 保险：探秘模式下永远不会走到这里
            if (!IsEnabledHere) return;

            if (m_DeathHandled) return;
            m_DeathHandled = true;

            Debug.Log("[地牢] 玩家生命值归零，触发死亡。");

            // ---- 1) 光带 ----
            if (emitDeathSonar)
            {
                EmitDeathSonar();
            }

            // ---- 2) 音效 ----
            AudioSystem.Play(AudioSystem.Cue.Death);

            // ---- 3) 死亡界面 ----
            GameUI.NotifyPlayerDied();
        }

        /// <summary>在玩家脚下放一次死亡光带（和探秘模式踩陷阱一样）。</summary>
        private void EmitDeathSonar()
        {
            SonarSystem sonar = FindObjectOfType<SonarSystem>();
            if (sonar == null) return;

            if (m_Player == null) return;

            // 方位角起点：玩家正前方的水平分量（和常规声波一致）
            Vector3 facing = m_Player.transform.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
            facing.Normalize();

            sonar.Emit(m_Player.FootPosition, deathSonar.ToEmitParams(facing));
        }

        // ------------------------------------------------------------------ TODO（交给别人）

        // ★★ 以下部分**不属于**这个脚本，是地牢模式需要新写的： ★★
        //
        //   1) 枪械系统
        //      · 取代 DaggerSystem（匕首）/ StoneThrowSystem（投石）
        //      · 按住左键射击、子弹飞行、命中判定
        //      · 建议新建：Scripts/Dungeon/DungeonGun.cs
        //
        //   2) 子弹
        //      · 撞墙 / 超时 / 命中玩家后消失
        //      · 命中玩家时调用 PlayerHealth.TakeDamage(1)
        //      · 建议新建：Scripts/Dungeon/DungeonBullet.cs
        //
        //   3) 怪物 AI（地牢版）
        //      · 不再近身追击、不再秒杀
        //      · 在地图上随机移动 + 朝玩家/随机方向开火
        //      · 建议新建：Scripts/Dungeon/DungeonEnemy.cs
        //      · ★ 不要复用 PuzzleMonster.cs（那是探秘模式的）
        //
        //   4) 生命值 HUD
        //      · 左上角去掉电池（BatterySystem）和石头（StoneSystem）
        //      · 换成生命值显示
        //      · 建议新建：Scripts/Dungeon/DungeonHUD.cs
        //      · ★ 需要在下面这几处让旧 HUD 在地牢模式不画：
        //          BatterySystem.OnGUI()
        //          StoneSystem.OnGUI()
    }
}
