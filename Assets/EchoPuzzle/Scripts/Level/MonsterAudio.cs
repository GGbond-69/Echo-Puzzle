using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 怪物的 3D 音效：Resting（静止时）与 Searching（搜索时）。
    ///
    /// ── 音量随距离 ──────────────────────────────────────────
    ///   距玩家 25m  ->  100%
    ///   距玩家  5m  ->  150%（最大）
    ///   距玩家 65m 或更远 -> 0%
    ///
    ///   5m 以内保持 150%（不再继续放大），25m 以外线性衰减到 0。
    ///
    /// ── Resting 规则 ────────────────────────────────────────
    ///   1) 击杀范围为 KillRadiusIdle 时，若玩家在 25m 内：
    ///        等 1~2 秒 -> 再检测一次 -> 仍满足就播一条 Resting
    ///   2) 播放后**暂停**检测；播放结束 -> 等 2~3 秒 -> 再检测
    ///        仍满足就再播一条
    ///
    /// ── Searching 规则 ─────────────────────────────────────
    ///   3) 击杀范围在 Idle / Moving 之间切换 -> **立即**播一条 Searching
    ///      （与玩家距离无关）
    ///   4) 播放结束 -> 接下来 2~3 秒里如果怪物**一直在移动**（按坐标判定）
    ///        -> 再播一条 Searching
    ///
    /// ★ 多个怪物各自持有独立的 AudioSource 与状态机，互不影响。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class MonsterAudio : MonoBehaviour
    {
        // ---------------------------------------------------------------- 距离 / 音量

        [Header("通用")]
        [Tooltip("★ 玩家在这个距离内才会**触发新的** Resting 音效（米）。\n\n" +
                 "这是**故意设计**：离得够远就不该再听到怪物在休息时哼声。\n" +
                 "   · 超出这个距离 -> 不再触发新的 Resting，当前这条播完就静音\n" +
                 "   · 但只要还在这个距离内，音量仍然按 Resting 那一套曲线走\n" +
                 "     （由 restingRefDistance / restingSilentDistance 决定）\n\n" +
                 "★ 注意：这只影响 **Resting**。Searching 音效不受距离限制。")]
        [Min(0f)] public float restingDetectRadius = 65f;

        // ---- Resting 音效的距离 / 音量（只影响 Resting） ----
        [Header("Resting 距离与音量")]
        [Tooltip("离玩家这个距离时音量 = Resting Ref Volume（米）")]
        [Min(0.01f)] public float restingRefDistance = 35f;

        [Tooltip("离玩家这个距离时音量 = Resting Max Volume（米）")]
        [Min(0.01f)] public float restingMaxVolumeDistance = 5f;

        [Tooltip("Resting Ref Distance 处的音量（0~1）")]
        [Range(0f, 1f)] public float restingRefVolume = 1.05f;

        [Tooltip("Resting Max Volume Distance 处的音量（0~1）")]
        [Range(0f, 1f)] public float restingMaxVolume = 1.5f;

        [Tooltip("Resting 超过这个距离音量归零（米）")]
        [Min(0.01f)] public float restingSilentDistance = 105f;

        // ---- Searching 音效的距离 / 音量（只影响 Searching） ----
        [Header("Searching 距离与音量")]
        [Tooltip("离玩家这个距离时音量 = Searching Ref Volume（米）")]
        [Min(0.01f)] public float searchingRefDistance = 35f;

        [Tooltip("离玩家这个距离时音量 = Searching Max Volume（米）")]
        [Min(0.01f)] public float searchingMaxVolumeDistance = 5f;

        [Tooltip("Searching Ref Distance 处的音量（0~1）")]
        [Range(0f, 1f)] public float searchingRefVolume = 0.84f;

        [Tooltip("Searching Max Volume Distance 处的音量（0~1）")]
        [Range(0f, 1f)] public float searchingMaxVolume = 1.2f;

        [Tooltip("Searching 超过这个距离音量归零（米）")]
        [Min(0.01f)] public float searchingSilentDistance = 105f;

        // ---------------------------------------------------------------- 检测节奏

        [Header("Resting 节奏")]
        [Tooltip("第一次检测前的随机等待（秒）")]
        public Vector2 restingFirstDelay = new Vector2(1f, 2f);

        [Tooltip("播放结束后的随机等待（秒）")]
        public Vector2 restingRepeatDelay = new Vector2(2f, 3f);

        [Header("Searching 节奏")]
        [Tooltip("播放结束后观察怪物是否持续移动的时长（秒）")]
        public Vector2 searchingObserveDelay = new Vector2(2f, 3f);

        /// <summary>持续移动判定所需的最小位移（米）。低于它视为没动。</summary>
        [Tooltip("观察期内累计位移低于这个值视为\"没在移动\"（米）")]
        [Min(0f)] public float searchingMoveThreshold = 0.25f;

        // ---------------------------------------------------------------- 运行时

        private AudioSource m_Source;

        private AudioClip[] m_RestingClips;
        private AudioClip[] m_SearchingClips;

        private PuzzleMonster m_Monster;
        private CharacterController m_Controller;

        /// <summary>上一次记录的击杀范围（用来检测 Idle / Moving 切换）。</summary>
        private bool m_WasMovingRange;

        /// <summary>上一次记录的位置（判断"有没有在动"）。</summary>
        private Vector3 m_LastPos;
        private float   m_MovedAccum;

        // ---- Resting 状态机 ----
        private enum RestingState
        {
            Idle,       // 等第一次检测
            Checking,   // 等 1~2 秒后的复检
            Playing,    // 正在播，暂停检测
            Cooldown,   // 播完等 2~3 秒
        }

        private RestingState m_RestState = RestingState.Idle;
        private float        m_RestTimer;

        // ---- Searching 状态机 ----
        private enum SearchingState
        {
            Idle,
            Playing,
            Observing,   // 播完后观察 2~3 秒是否一直在动
        }

        private SearchingState m_SearchState = SearchingState.Idle;
        private float          m_SearchTimer;

        /// <summary>上一帧怪物是否处于受惊状态（用来检测"进入/退出"的那一帧）。</summary>
        private bool m_WasFrightened;

        // ---------------------------------------------------------------- 生命周期

        private void Awake()
        {
            m_Monster    = GetComponent<PuzzleMonster>();
            m_Controller = GetComponent<CharacterController>();

            m_Source = GetComponent<AudioSource>();
            ConfigureSource();

            LoadClips();

            m_LastPos    = transform.position;
            m_WasMovingRange = false;
        }

        /// <summary>
        /// 把 AudioSource 配成 3D 点音源。
        ///
        /// ★★ maxDistance 必须**远大于** silentDistance ★★
        ///
        ///   Unity 的 3D 音源会把"距离衰减系数"**再乘一遍**到 volume 上：
        ///       实际听到的音量 = m_Source.volume × 距离衰减
        ///
        ///   Linear rolloff 的衰减系数是
        ///       1 - (d - minDistance) / (maxDistance - minDistance)
        ///
        ///   以前 maxDistance = 200，于是：
        ///       · 35m（= refDistance）时衰减系数只有 0.83
        ///       · 105m（= silentDistance）时只剩 0.48
        ///   结果真实的音量曲线**不等于** VolumeForDistance 算出的那条 ——
        ///   会在 refDistance 附近就明显变小、甚至提前听不见。
        ///
        ///   本脚本的远近完全由 VolumeForDistance 决定（那是唯一的音量来源），
        ///   所以这里把 3D 衰减压到几乎不影响：
        ///   取 minDistance = 1、maxDistance = 10000，
        ///   在我们关心的 0~200m 范围内衰减系数 ≈ 1。
        ///
        ///   保留 spatialBlend = 1（左右的方位感仍然需要）。
        /// </summary>
        private void ConfigureSource()
        {
            m_Source.playOnAwake     = false;
            m_Source.loop            = false;
            m_Source.spatialBlend    = 1f;          // 全 3D（保留方位感）
            m_Source.rolloffMode     = AudioRolloffMode.Linear;
            m_Source.minDistance     = 1f;
            m_Source.maxDistance     = 10000f;      // ★ 不再抢 VolumeForDistance 的活
            m_Source.dopplerLevel    = 0f;          // 怪物移动慢，关掉多普勒避免音高抖动
            m_Source.volume          = 0f;
        }

        private void LoadClips()
        {
            m_RestingClips   = LoadClipsSafe("AudioPack/Monster/Resting");
            m_SearchingClips = LoadClipsSafe("AudioPack/Monster/Searching");

            if (m_RestingClips.Length == 0)
            {
                Debug.LogWarning($"[EchoPuzzle] 「{name}」找不到 Resting 音效。" +
                    "请在 Assets/Resources/AudioPack/Monster/Resting 下放音频文件。");
            }

            if (m_SearchingClips.Length == 0)
            {
                Debug.LogWarning($"[EchoPuzzle] 「{name}」找不到 Searching 音效。" +
                    "请在 Assets/Resources/AudioPack/Monster/Searching 下放音频文件。");
            }
        }

        private static AudioClip[] LoadClipsSafe(string path)
        {
            AudioClip[] clips = Resources.LoadAll<AudioClip>(path);
            return clips != null ? clips : new AudioClip[0];
        }

        private bool IsRestingPlaying   => m_Source != null && m_Source.isPlaying && m_RestPlayingClip != null;
        private bool IsSearchingPlaying => m_Source != null && m_Source.isPlaying && m_SearchPlayingClip != null;

        private AudioClip m_RestPlayingClip;
        private AudioClip m_SearchPlayingClip;

        // ---------------------------------------------------------------- 主循环

        private void Update()
        {
            if (m_Monster == null || m_Source == null) return;

            float dt  = Time.deltaTime;
            float now = Time.timeSinceLevelLoad;

            // ★★ 不在"最近三个"里 -> 完全静音并停掉播放 ★★
            //
            //   只把音量设成 0 是不够的：AudioSource 还在"播放中"，
            //   计时器也还在走，切回前三名时会突然接上之前的进度。
            //   这里直接 Stop，并让状态机回到 Idle —— 等它重新进入前三名
            //   再从头开始播。
            if (!NearestMonsterAudio.IsAudible(this))
            {
                if (m_Source.isPlaying) m_Source.Stop();
                m_Source.volume = 0f;

                // 状态机复位（不算"播完了"，所以不进入观察期）
                if (m_RestState == RestingState.Playing)
                {
                    m_RestState = RestingState.Idle;
                    m_RestPlayingClip = null;
                    m_RestTimer = Random.Range(restingFirstDelay.x, restingFirstDelay.y);
                }

                if (m_SearchState == SearchingState.Playing)
                {
                    m_SearchState = SearchingState.Idle;
                    m_SearchPlayingClip = null;
                }

                return;
            }

            UpdateVolume();

            UpdateMoveAccumulator(dt);

            // ★ 刚被 TriggerVolume 显现出来的那一帧：
            //   · **不播 Searching**（"出现"不等于"从移动中停下"）
            //   · 同步击杀范围基线，避免下一帧被当成"切换"
            if (m_Monster != null && m_Monster.ConsumeJustAppeared())
            {
                m_WasMovingRange = m_Monster.IsKillRangeMoving;

                // 状态全部归位，等下一帧正常开始计时
                m_RestState   = RestingState.Idle;
                m_RestTimer   = Random.Range(restingFirstDelay.x, restingFirstDelay.y);
                m_SearchState = SearchingState.Idle;
                m_SearchTimer = 0f;

                return;
            }

            // ★ 结算界面（赢了 / 死了）：
            //   · **已经触发的音效继续播完**（不 Stop）
            //   · **不再触发新的** Resting / Searching
            if (!CanTriggerNewSounds()) return;

            // ★ 受惊状态的进入 / 退出 —— 优先于其它触发方式处理
            if (UpdateFrightenedTransition())
            {
                // 受惊期间：除了上面这些，不再跑任何音效触发逻辑
                return;
            }

            UpdateSearchingSwitch();

            UpdateRestingStateMachine(dt, now);

            UpdateSearchingStateMachine(dt, now);
        }

        // ---------------------------------------------------------------- 音量

        /// <summary>
        /// 按到玩家的距离算音量。
        ///
        ///   maxVolumeDistance(5m) 以内 -> maxVolume       (100%)
        ///   refDistance(25m)            -> refVolume       (20%)
        ///   silentDistance(30m) 及更远  -> 0
        /// </summary>
        private void UpdateVolume()
        {
            PlayerController player = m_Monster != null ? m_Monster.Player : null;

            float vol;

            if (player == null)
            {
                // 没有玩家引用时按 Ref Distance 处的音量播（避免完全静音听不到）
                vol = IsSearchingPlaying ? searchingRefVolume : restingRefVolume;
            }
            else
            {
                // ★★ 只听最近的 3 个怪物 ★★
                //
                //   需求：玩家只会接收到离他最近的三个 hiding = 0 的怪物的音效
                //   （两个模式都一样）。
                //
                //   判据由 NearestMonsterAudio 统一算好（每帧一次，避免
                //   每个怪物各扫一遍全场）。排不进前三的直接静音。
                if (!NearestMonsterAudio.IsAudible(this))
                {
                    m_Source.volume = 0f;
                    return;
                }

                float d = Vector3.Distance(transform.position, player.transform.position);
                vol = VolumeForDistance(d);
            }

            // ★ 不要 Clamp01 到 1 以上 —— restingRefVolume / restingMaxVolume
            //   的默认值分别是 1.05 / 1.5，本来就允许超过 1
            //   （AudioSource.volume 的上限就是 1，Unity 内部自己会夹，
            //     但这里夹成 1 会让 5m~35m 之间**完全没有音量变化**）。
            //
            //   下限仍然夹到 0（负音量没有意义）。
            m_Source.volume = Mathf.Max(0f, vol);
        }

        /// <summary>
        /// 给定距离返回音量（0~1）。
        ///
        /// ★ 公式和以前完全一样，只是参数分成 Resting / Searching 两套：
        ///   · 当前正在播 Searching -> 用 Searching* 那一组
        ///   · 其它（Resting / 刚起步） -> 用 Resting* 那一组
        ///
        ///   分段线性：
        ///     near 以内   -> maxVolume
        ///     near ~ ref  -> maxVolume 线性降到 refVolume
        ///     ref ~ silent-> refVolume 线性降到 0
        ///     ≥ silent    -> 0
        /// </summary>
        public float VolumeForDistance(float d)
        {
            bool searching = IsSearchingPlaying;

            float refDist = searching ? searchingRefDistance        : restingRefDistance;
            float maxDist = searching ? searchingMaxVolumeDistance  : restingMaxVolumeDistance;
            float refVol  = searching ? searchingRefVolume          : restingRefVolume;
            float maxVol  = searching ? searchingMaxVolume          : restingMaxVolume;
            float silent  = searching ? searchingSilentDistance     : restingSilentDistance;

            float near = Mathf.Min(maxDist, refDist);
            float far  = Mathf.Max(refDist, silent);

            // 近距离：最大音量
            if (d <= near) return maxVol;

            // 中间：从最大音量线性降到 refVol
            if (d <= refDist)
            {
                float t = Mathf.InverseLerp(near, refDist, d);
                return Mathf.Lerp(maxVol, refVol, t);
            }

            // 远距离：从 refVol 线性降到 0
            if (d < far)
            {
                float t = Mathf.InverseLerp(refDist, far, d);
                return Mathf.Lerp(refVol, 0f, t);
            }

            return 0f;
        }

        // ---------------------------------------------------------------- 移动检测

        /// <summary>累计这段时间里走了多远（完全按坐标判定）。</summary>
        private void UpdateMoveAccumulator(float dt)
        {
            Vector3 p = transform.position;
            Vector3 d = p - m_LastPos;
            d.y = 0f;

            m_MovedAccum += d.magnitude;

            m_LastPos = p;
        }

        /// <summary>
        /// 现在是否允许**触发新的**音效。
        ///
        /// ★ 需求：进入"你赢了 / 你死了"界面后，
        ///   已经触发的音效要继续播完，但不要再触发新的。
        ///
        ///   所以这个函数只用来挡住"启动新音效"的入口，
        ///   已经 Play 出去的 AudioSource 不受影响 —— 它会自然播完。
        /// </summary>
        private static bool CanTriggerNewSounds()
        {
            return GameUI.Current == GameState.Playing;
        }

        // ---------------------------------------------------------------- 受惊

        /// <summary>
        /// 处理受惊状态的进入与退出。
        ///
        /// ★ 需求：
        ///   · **进入**受惊 -> 立即播一次 Searching
        ///   · **受惊期间** -> 不再按其它方式触发 Searching（Resting 也停）
        ///   · **退出**受惊 -> 恢复正常的 Searching 触发方式
        ///
        /// 返回 true 表示当前处于受惊状态，调用方应跳过本帧的其它音效逻辑。
        /// </summary>
        private bool UpdateFrightenedTransition()
        {
            bool nowFrightened = m_Monster != null && m_Monster.IsFrightened;

            if (nowFrightened == m_WasFrightened)
                return nowFrightened;      // 状态没变

            m_WasFrightened = nowFrightened;

            if (nowFrightened)
            {
                // ---- 进入受惊：立刻播 Searching ----
                //   先把手上的 Resting / 观察期清掉，避免声音叠在一起
                m_RestState         = RestingState.Idle;
                m_RestPlayingClip   = null;
                m_RestTimer         = Random.Range(restingFirstDelay.x, restingFirstDelay.y);

                m_SearchState = SearchingState.Idle;
                m_SearchPlayingClip = null;

                PlaySearching();
            }
            else
            {
                // ---- 退出受惊：恢复正常的触发节奏 ----
                //   重置 Searching 状态机，让 Idle/Moving 切换检测重新开始工作
                m_SearchState = SearchingState.Idle;
                m_SearchPlayingClip = null;
                m_SearchTimer = 0f;

                //   记录当前击杀范围，避免退出瞬间因为"状态不一致"又误触发一次
                m_WasMovingRange = m_Monster != null && m_Monster.IsKillRangeMoving;

                //   Resting 重新开始计时
                m_RestState = RestingState.Idle;
                m_RestPlayingClip = null;
                m_RestTimer = Random.Range(restingFirstDelay.x, restingFirstDelay.y);
            }

            return nowFrightened;
        }

        // ---------------------------------------------------------------- Searching

        /// <summary>检测击杀范围 Idle <-> Moving 的切换。</summary>
        private void UpdateSearchingSwitch()
        {
            bool nowMovingRange = m_Monster != null && m_Monster.IsKillRangeMoving;

            if (nowMovingRange != m_WasMovingRange)
            {
                m_WasMovingRange = nowMovingRange;

                // ★ 切换即刻播放，与玩家距离无关
                PlaySearching();
            }
        }

        private void PlaySearching()
        {
            if (m_SearchingClips.Length == 0) return;
            if (m_SearchState == SearchingState.Playing) return;   // 正在播就不打断

            AudioClip clip = m_SearchingClips[Random.Range(0, m_SearchingClips.Length)];

            PlayClip(clip);
            m_SearchPlayingClip = clip;

            m_SearchState = SearchingState.Playing;
        }

        private void UpdateSearchingStateMachine(float dt, float now)
        {
            switch (m_SearchState)
            {
                case SearchingState.Playing:
                    // 播完了 -> 进入观察期
                    if (!m_Source.isPlaying || m_SearchPlayingClip == null)
                    {
                        m_SearchPlayingClip = null;
                        m_MovedAccum  = 0f;
                        m_SearchState = SearchingState.Observing;
                        m_SearchTimer = Random.Range(searchingObserveDelay.x, searchingObserveDelay.y);
                    }
                    break;

                case SearchingState.Observing:
                    m_SearchTimer -= dt;

                    if (m_SearchTimer <= 0f)
                    {
                        // ★ 这段时间里是否"一直在移动"（按坐标累计位移判定）
                        bool movedEnough = m_MovedAccum >= searchingMoveThreshold;

                        m_SearchState = SearchingState.Idle;

                        if (movedEnough)
                        {
                            // 满足 -> 再播一条 Searching
                            PlaySearching();
                        }
                    }
                    break;
            }
        }

        // ---------------------------------------------------------------- Resting

        private void UpdateRestingStateMachine(float dt, float now)
        {
            // 正在播 Searching 时不插手 Resting（声音别打架）
            if (m_SearchState == SearchingState.Playing) return;

            switch (m_RestState)
            {
                case RestingState.Idle:
                case RestingState.Cooldown:
                    // 这两种都只是在等计时
                    m_RestTimer -= dt;

                    if (m_RestTimer <= 0f)
                    {
                        // 到点：进入"1~2 秒后再检测一次"的 Checking
                        m_RestState = RestingState.Checking;
                        m_RestTimer = Random.Range(restingFirstDelay.x, restingFirstDelay.y);
                    }
                    break;

                case RestingState.Checking:
                    m_RestTimer -= dt;

                    if (m_RestTimer <= 0f)
                    {
                        // ★ 到这次才真正判定
                        if (CanPlayResting())
                        {
                            PlayResting();
                        }
                        else
                        {
                            // 不满足 -> 回到 Idle 重新等
                            m_RestState = RestingState.Idle;
                            m_RestTimer = Random.Range(restingFirstDelay.x, restingFirstDelay.y);
                        }
                    }
                    break;

                case RestingState.Playing:
                    if (!m_Source.isPlaying || m_RestPlayingClip == null)
                    {
                        m_RestPlayingClip = null;
                        m_RestState = RestingState.Cooldown;
                        m_RestTimer = Random.Range(restingRepeatDelay.x, restingRepeatDelay.y);
                    }
                    break;
            }
        }

        /// <summary>当前是否满足播 Resting 的条件。</summary>
        private bool CanPlayResting()
        {
            if (m_RestingClips.Length == 0) return false;

            // 击杀范围必须是 KillRadiusIdle
            if (m_Monster == null || m_Monster.IsKillRangeMoving) return false;

            // ★ 玩家必须在 restingDetectRadius 内才会触发新的 Resting 音效。
            //
            //   这是**故意设计**：离得够远就不该再听到怪物休息时的哼声。
            //   注意它和音量曲线是两件独立的事：
            //     · 本函数控制"要不要**触发**新音效"
            //     · VolumeForDistance 控制"已经触发的那条播多大音"
            //   所以超出这个距离时，怪物手上那条 clip 播完就静音了。
            PlayerController player = m_Monster.Player;
            if (player == null) return false;

            float d = Vector3.Distance(transform.position, player.transform.position);
            return d <= restingDetectRadius;
        }

        private void PlayResting()
        {
            AudioClip clip = m_RestingClips[Random.Range(0, m_RestingClips.Length)];

            PlayClip(clip);
            m_RestPlayingClip = clip;

            m_RestState = RestingState.Playing;
        }

        // ---------------------------------------------------------------- 播放

        private void PlayClip(AudioClip clip)
        {
            if (clip == null || m_Source == null) return;

            m_Source.Stop();
            m_Source.clip = clip;
            m_Source.time = 0f;
            m_Source.volume = 0f;      // UpdateVolume 下一帧会给正确值
            m_Source.Play();
        }

        /// <summary>重玩时清空所有状态（由 PuzzleMonster.ResetMonster 调用）。</summary>
        public void ResetAudioState()
        {
            if (m_Source != null)
            {
                m_Source.Stop();
                m_Source.clip   = null;
                m_Source.volume = 0f;
            }

            // ★ 清掉"播放中"的那一帧残留。
            //   停止播放后 isPlaying 会变 false，状态机下一帧就会认为
            //   "Searching 播完了" -> 进入观察期 -> 可能又播一条，
            //   表现为"被杀后重开又响一次"。所以这里把状态直接归位。
            m_RestState   = RestingState.Idle;
            m_SearchState = SearchingState.Idle;

            m_RestPlayingClip   = null;
            m_SearchPlayingClip = null;

            m_RestState   = RestingState.Idle;
            m_RestTimer   = Random.Range(restingFirstDelay.x, restingFirstDelay.y);

            m_SearchState = SearchingState.Idle;
            m_SearchTimer = 0f;

            m_MovedAccum = 0f;
            m_LastPos    = transform.position;

            m_WasMovingRange = m_Monster != null && m_Monster.IsKillRangeMoving;
            m_WasFrightened  = m_Monster != null && m_Monster.IsFrightened;
        }
    }
}
