using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 音乐触发区：和 <see cref="TriggerVolume"/> / <see cref="Gravel"/> 一样**没有实体**。
    ///
    /// ── 它不做什么 ──────────────────────────────────────────
    ///   ★ 不改变光带外观（这点和 Gravel 不同）。
    ///
    /// ── 它做什么 ────────────────────────────────────────────
    ///   玩家进入后：
    ///     1) 先播放「先行音频」（若有），播完**立刻**接着播「主音频」，中间没有淡入淡出
    ///     2) 主音频可以循环播放
    ///     3) 播放开始时带**淡入**（默认 1 秒）
    ///     4) 同时**淡出**指定的其它 TriggerMusic 的音频（默认 1 秒）
    ///
    /// ── 淡入 / 淡出怎么算 ────────────────────────────────────
    ///   每首曲子有自己的 <see cref="MusicChannel"/>（独立的 AudioSource），
    ///   所以互不干扰 —— 不会被 AudioSystem 的声部池抢占。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class TriggerMusic : MonoBehaviour
    {
        // ---------------------------------------------------------------- 编辑期边框

        /// <summary>
        /// 编辑期边框颜色（全局，淡紫色）。
        ///
        /// ★ 静态字段：所有 TriggerMusic 共用同一个颜色，
        ///   和 WallOccluder / Gravel 的做法一致。
        /// </summary>
        public static Color OutlineColor = new Color(0.75f, 0.55f, 1f, 0.25f);

        [Header("编辑期边框")]
        [Tooltip("在 Scene 视图里用直线画出这个音乐触发区的轮廓。\n\n" +
                 "★ 只在编辑状态下显示，进 Play 后就没了。\n" +
                 "边框颜色是全局的，见 TriggerMusic.OutlineColor。")]
        public bool showEditorOutline = true;

        // ---------------------------------------------------------------- 主音频

        [Header("主音频")]
        [Tooltip("进入后要播放的音频。留空则不播。")]
        public AudioClip mainClip;

        [Tooltip("主音频是否循环播放")]
        public bool loop = true;

        [Range(0f, 1f)]
        [Tooltip("主音频的目标音量（淡入结束后达到的音量）")]
        public float mainVolume = 1f;

        // ---------------------------------------------------------------- 先行音频

        [Header("先行音频")]
        [Tooltip("播放主音频**之前**先播的音频。\n\n" +
                 "★ 播完立刻开始主音频，两者之间没有淡入淡出。\n" +
                 "★ 留空则直接播主音频。")]
        public AudioClip preClip;

        [Range(0f, 1f)]
        [Tooltip("先行音频的音量")]
        public float preVolume = 1f;

        // ---------------------------------------------------------------- 淡入 / 淡出

        [Header("淡入 / 淡出")]
        [Tooltip("开始播放时的淡入时长（秒）。0 = 不做淡入")]
        [Min(0f)] public float fadeInDuration = 1f;

        [Tooltip("停止其它音乐时的淡出时长（秒）。0 = 立刻停")]
        [Min(0f)] public float fadeOutDuration = 1f;

        // ---------------------------------------------------------------- 要停止的其它音乐

        [Header("同时停止的其它音乐")]
        [Tooltip("本区域触发时，要让这些 TriggerMusic 淡出停止。\n\n" +
                 "★ 可以填任意多个（包括自己 —— 一般不需要）。")]
        public List<TriggerMusic> stopOthers = new List<TriggerMusic>();

        // ---------------------------------------------------------------- 运行时

        [Tooltip("触发一次后就不再触发")]
        public bool triggerOnce = true;

        /// <summary>本区域是否已经触发过。</summary>
        public bool Consumed { get; private set; }

        private BoxCollider  m_Box;
        private MusicChannel m_Channel;
        private PlayerController m_Player;
        private bool m_PlayerInside;

        private void Awake()
        {
            m_Box = GetComponent<BoxCollider>();
            m_Box.isTrigger = true;

            m_Channel = new MusicChannel(gameObject);
        }

        private void OnDestroy()
        {
            m_Channel?.Dispose();
        }

        private void Update()
        {
            if (GameUI.Current != GameState.Playing) return;

            if (m_Player == null) m_Player = FindObjectOfType<PlayerController>();
            if (m_Player == null) return;

            bool inside = Contains(m_Player.FootPosition);

            if (inside && !m_PlayerInside)
            {
                m_PlayerInside = true;

                if (!(triggerOnce && Consumed))
                {
                    Consumed = true;
                    Fire();
                }
            }
            else if (!inside && m_PlayerInside)
            {
                m_PlayerInside = false;
            }

            // 推进淡入淡出
            m_Channel?.Tick(Time.unscaledDeltaTime);
        }

        /// <summary>触发：淡出别人 + 播放自己。</summary>
        public void Fire()
        {
            // ① 先让指定的其它音乐淡出
            if (stopOthers != null)
            {
                for (int i = 0; i < stopOthers.Count; i++)
                {
                    TriggerMusic other = stopOthers[i];
                    if (other == null || other == this) continue;

                    other.m_Channel?.FadeOut(fadeOutDuration);
                }
            }

            // ② 自己开始播放（带淡入）
            m_Channel?.PlayWithPre(preClip, preVolume, mainClip, mainVolume,
                                   fadeInDuration, loop);
        }

        /// <summary>重玩时复位：停掉音乐、清掉已触发标记。</summary>
        public void ResetMusic()
        {
            Consumed      = false;
            m_PlayerInside = false;

            m_Channel?.StopImmediate();
        }

        /// <summary>重玩时对所有 TriggerMusic 调一次。</summary>
        public static void ResetAllMusic()
        {
            TriggerMusic[] all = Resources.FindObjectsOfTypeAll<TriggerMusic>();

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            for (int i = 0; i < all.Length; i++)
            {
                TriggerMusic m = all[i];
                if (m == null) continue;

                GameObject go = m.gameObject;
                if (go.scene != scene) continue;
                if ((go.hideFlags & HideFlags.DontSave) != 0) continue;

                m.ResetMusic();
            }
        }

        /// <summary>某个世界坐标是否在本区域内。</summary>
        public bool Contains(Vector3 worldPos)
        {
            if (m_Box == null) m_Box = GetComponent<BoxCollider>();
            if (m_Box == null) return false;

            Vector3 local = transform.InverseTransformPoint(worldPos);
            Vector3 c     = m_Box.center;
            Vector3 h     = m_Box.size * 0.5f;

            return Mathf.Abs(local.x - c.x) <= h.x
                && Mathf.Abs(local.y - c.y) <= h.y
                && Mathf.Abs(local.z - c.z) <= h.z;
        }

        // ---------------------------------------------------------------- 编辑期边框

        private void OnDrawGizmos()
        {
            if (!showEditorOutline) return;

            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null) return;

            Gizmos.color = OutlineColor;

            Vector3 c = box.center;
            Vector3 h = box.size * 0.5f;

            Vector3[] p = new Vector3[8]
            {
                c + new Vector3(-h.x, -h.y, -h.z),
                c + new Vector3( h.x, -h.y, -h.z),
                c + new Vector3( h.x, -h.y,  h.z),
                c + new Vector3(-h.x, -h.y,  h.z),
                c + new Vector3(-h.x,  h.y, -h.z),
                c + new Vector3( h.x,  h.y, -h.z),
                c + new Vector3( h.x,  h.y,  h.z),
                c + new Vector3(-h.x,  h.y,  h.z),
            };

            Transform tr = transform;
            for (int i = 0; i < 8; i++) p[i] = tr.TransformPoint(p[i]);

            // 底面
            Gizmos.DrawLine(p[0], p[1]); Gizmos.DrawLine(p[1], p[2]);
            Gizmos.DrawLine(p[2], p[3]); Gizmos.DrawLine(p[3], p[0]);

            // 顶面
            Gizmos.DrawLine(p[4], p[5]); Gizmos.DrawLine(p[5], p[6]);
            Gizmos.DrawLine(p[6], p[7]); Gizmos.DrawLine(p[7], p[4]);

            // 竖棱
            Gizmos.DrawLine(p[0], p[4]); Gizmos.DrawLine(p[1], p[5]);
            Gizmos.DrawLine(p[2], p[6]); Gizmos.DrawLine(p[3], p[7]);
        }

        // ---------------------------------------------------------------- 播放通道

        /// <summary>
        /// 一首曲子的独立播放通道。
        /// 自己带一个 AudioSource，所以不会被 AudioSystem 的声部池抢占。
        /// </summary>
        private sealed class MusicChannel
        {
            private readonly AudioSource m_Source;
            private readonly GameObject  m_Owner;

            // 淡入淡出
            private float m_Alpha;        // 当前增益 0..1
            private float m_TargetAlpha;  // 目标增益
            private float m_FadeSpeed;    // alpha / 秒；0 = 瞬间

            // "播完先行音频接着播主音频"
            private AudioClip m_PendingMain;
            private float     m_PendingVolume;
            private bool      m_PendingLoop;
            private bool      m_HasPending;
            private float     m_PendingFadeIn;

            private bool m_StopWhenSilent;   // 淡出到 0 后要不要停

            public MusicChannel(GameObject owner)
            {
                m_Owner = owner;

                m_Source = owner.AddComponent<AudioSource>();
                m_Source.playOnAwake   = false;
                m_Source.loop          = false;
                m_Source.volume        = 0f;
                m_Source.spatialBlend  = 0f;      // 纯 2D
            }

            public void Dispose()
            {
                if (m_Source != null) m_Source.Stop();
            }

            /// <summary>
            /// 播放：有先行音频就先播它，播完立刻接主音频（中间无淡入淡出）。
            /// </summary>
            public void PlayWithPre(AudioClip pre, float preVol,
                                    AudioClip main, float mainVol,
                                    float fadeIn, bool loopMain)
            {
                m_StopWhenSilent = false;
                m_HasPending     = false;

                if (pre == null)
                {
                    // 没有先行音频：直接播主音频
                    StartClip(main, mainVol, loopMain, fadeIn);
                    return;
                }

                // 有先行音频：先播它，把主音频挂起
                m_PendingMain    = main;
                m_PendingVolume  = mainVol;
                m_PendingLoop    = loopMain;
                m_PendingFadeIn  = fadeIn;
                m_HasPending     = (main != null);

                // 先行音频自己不做淡入淡出（按需求：两者之间没有淡入淡出）
                StartClipImmediate(pre, preVol, loop: false);
            }

            private void StartClip(AudioClip clip, float volume, bool loop, float fadeIn)
            {
                if (clip == null)
                {
                    StopImmediate();
                    return;
                }

                m_Source.clip   = clip;
                m_Source.loop   = loop;
                m_Source.volume = 0f;
                m_Source.time   = 0f;
                m_Source.Play();

                if (fadeIn <= 0.0001f)
                {
                    m_Alpha       = 1f;
                    m_TargetAlpha = 1f;
                    m_FadeSpeed   = 0f;
                    m_Source.volume = volume;
                    m_MaxVolume   = volume;
                }
                else
                {
                    m_Alpha       = 0f;
                    m_TargetAlpha = 1f;
                    m_FadeSpeed   = 1f / fadeIn;
                    m_MaxVolume   = volume;
                }
            }

            private float m_MaxVolume = 1f;

            /// <summary>立刻播放，不做淡入（先行音频用）。</summary>
            private void StartClipImmediate(AudioClip clip, float volume, bool loop)
            {
                m_Source.clip   = clip;
                m_Source.loop   = loop;
                m_Source.volume = Mathf.Clamp01(volume);
                m_Source.time   = 0f;
                m_Source.Play();

                m_Alpha       = 1f;
                m_TargetAlpha = 1f;
                m_FadeSpeed   = 0f;
                m_MaxVolume   = volume;
                m_InPre       = true;
            }

            private bool m_InPre;

            /// <summary>淡出到静音后停止。</summary>
            public void FadeOut(float duration)
            {
                if (!m_Source.isPlaying) return;

                m_TargetAlpha = 0f;
                m_StopWhenSilent = true;

                m_FadeSpeed = duration <= 0.0001f ? 0f : m_Alpha / duration;

                if (m_FadeSpeed <= 0f)
                {
                    StopImmediate();
                }
            }

            /// <summary>立刻停止。</summary>
            public void StopImmediate()
            {
                m_Source.Stop();
                m_Source.clip   = null;
                m_Source.volume = 0f;

                m_Alpha          = 0f;
                m_TargetAlpha    = 0f;
                m_FadeSpeed      = 0f;
                m_HasPending     = false;
                m_StopWhenSilent = false;
                m_InPre          = false;
            }

            /// <summary>每帧推进淡入淡出 / 检查"先行音频播完了没"。</summary>
            public void Tick(float dt)
            {
                // ---- 先行音频播完 -> 立刻接主音频 ----
                if (m_InPre)
                {
                    if (!m_Source.isPlaying)
                    {
                        m_InPre = false;

                        if (m_HasPending)
                        {
                            m_HasPending = false;
                            StartClip(m_PendingMain, m_PendingVolume, m_PendingLoop, m_PendingFadeIn);
                        }
                    }

                    return;   // 先行音频期间不处理淡入淡出
                }

                if (!m_Source.isPlaying) return;

                // ---- 淡入淡出 ----
                if (m_FadeSpeed > 0f)
                {
                    m_Alpha = Mathf.MoveTowards(m_Alpha, m_TargetAlpha, m_FadeSpeed * dt);

                    if (m_TargetAlpha <= 0f && m_Alpha <= 0f && m_StopWhenSilent)
                    {
                        StopImmediate();
                        return;
                    }
                }

                m_Source.volume = Mathf.Clamp01(m_Alpha * m_MaxVolume);
            }
        }
    }
}
