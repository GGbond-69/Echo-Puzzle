using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>玩家做出的一类可被检测的操作。<see cref="None"/> 表示"还没有过操作"。</summary>
    public enum PlayerAction
    {
        None   = 0,
        Space  = 1,   // 按下空格（喊叫）
        Look   = 2,   // 转动视角
        Zoom   = 3,   // 滚轮缩放
        Move   = 4,   // 移动角色
        Marker = 5,   // 按 E 放标记
    }

    public enum MoveMode { Sneak, Walk, Run }

    /// <summary>
    /// M1：第三人称角色控制 + 相机环绕 + 移动发声。
    ///
    /// 四种发声方式（走 / 冲 / 潜 / 空格踏步）的全部参数都在 Inspector 上：
    /// 颜色、初始不透明度、衰减速度、射程、点密度…… 改完直接生效，不用改代码。
    ///
    /// 关于不透明度：衰减速度（fadeSpeed）**天然是全局统一的**，
    /// 因为它是声波的物理属性。所以「这串点能亮多久」完全由初始不透明度决定：
    ///     存活时长 ≈ -ln(0.02) / fadeSpeed × (初始不透明度 / 1.0)
    /// 想让某种步伐看得更久，调它的 initialAlpha，而不是调 fadeSpeed。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("引用")]
        public Camera      playerCamera;
        public SonarSystem echo;

        [Tooltip("脚印系统。留空则自动创建（每次释放声波打印一个鞋印）")]
        public FootprintSystem footprints;

        [Header("自动补齐（手工摆 Player 时用）")]
        [Tooltip("场景里没有相机时，自动创建一个（清屏纯黑 + Bloom）")]
        public bool autoCreateCamera = true;

        [Tooltip("场景里没有声波系统时，自动创建一个")]
        public bool autoCreateSonar = true;

        private GameObject m_AutoCamera;
        private GameObject m_AutoSonar;

        // ------------------------------------------------------------------ 移动速度

        [Header("移动速度（米/秒）")]
        [Tooltip("潜行速度")]
        public float sneakSpeed = 1.35f;
        [Tooltip("正常走路速度")]
        public float walkSpeed  = 3.4f;
        [Tooltip("冲刺速度")]
        public float runSpeed   = 6.2f;
        [Tooltip("加速度，越大起步越干脆")]
        public float accel      = 16f;

        [Header("击杀后潜行加速")]
        [Tooltip("成功移除一只怪物后，潜行速度立刻变成 N 倍")]
        [Min(1f)] public float sneakBoostMultiplier = 4f;

        [Tooltip("加速后回落到正常速度所需时间（秒）")]
        [Min(0.01f)] public float sneakBoostFadeDuration = 3f;

        /// <summary>潜行加速的剩余时间（秒）。</summary>
        private float m_SneakBoostTimer;

        /// <summary>
        /// 通知：玩家刚刚成功移除了一只怪物。
        /// 潜行速度立刻 ×sneakBoostMultiplier，然后在 sneakBoostFadeDuration 内线性回落。
        /// </summary>
        public void NotifyMonsterRemoved()
        {
            m_SneakBoostTimer = Mathf.Max(0.01f, sneakBoostFadeDuration);
        }

        // ------------------------------------------------------------------ 脚步节奏

        [Header("脚步节奏（走过多少米踏一步）")]
        [Tooltip("潜行一步的位移距离")]
        public float sneakStride = 1.0f;
        [Tooltip("走路一步的位移距离。调小 = 声波更连贯，但生成更频繁")]
        public float walkStride  = 1.9f;
        [Tooltip("冲刺一步的位移距离")]
        public float runStride   = 1.5f;
        [Tooltip("两次发声的最小间隔（秒），防止慢帧时爆炸")]
        public float minEmitInterval = 0.16f;

        [Tooltip("【起步第一步】玩家从**静止**开始移动时，只走过多远就立刻发出第一声（米）。\n\n" +
                 "让起步就有反馈，不必等走满一整个 stride。走 / 跑 / 潜行共用这个值。\n" +
                 "第一声之后恢复按各自 stride 的常规节奏。")]
        [Min(0.05f)] public float firstStepDistance = 0.5f;

        // ------------------------------------------------------------------ 四种声波

        // 四种声波**速度完全一样**（都跟随 SonarTrailRenderer.referenceSpeed）。
        // 它们的区别在：生成频率（stride）、三个仰角层的射线数量（rayCount90/30/60）、
        // 初始不透明度（initialAlpha）、波前存在时长（lifetimeCap）、
        // 尾巴残留时长（tailLinger）、每次反射扣的时长（bounceLifeLoss）。
        //
        // 关于那几个时间：
        //   lifetimeCap    —— 波前推进多久后**停止推进**，形状钉住
        //   tailLinger     —— 钉住之后光带还能**残留多久**才消失
        //   bounceLifeLoss —— 每反射一次，从剩余波前时长里再扣掉这么多秒
        //   三者独立。tailLinger = 0 就是旧行为（波前一停，光带跟着立刻没）。
        //
        // 射线数量是性能大头：一次发声 = 三层之和，每条再弹 maxBounces 次，
        // 总射线检测次数 ≈ 三层之和 × 2^maxBounces，别随手调大。

        [Header("① 走路声波")]
        public EchoPreset walkEcho = new EchoPreset
        {
            enabled = true, color = Color.white, initialAlpha = 0.4f,
            rayCount90 = 16, rayCount30 = 16, rayCount60 = 16, range = 30f, speed = 8f, trailSpacing = 0.30f,
            maxBounces = 3, bounceFalloff = 0.65f,
            lifetimeCap = 2f, tailLinger = 20f, bounceLifeLoss = 0.35f,
            diffuseCount15 = 8, diffuseCount45 = 8, diffuseCount75 = 8,
            hemisphereCount = 0, hemisphereDiffuse = false,
        };

        [Header("② 冲刺声波")]
        public EchoPreset runEcho = new EchoPreset
        {
            enabled = true, color = Color.white, initialAlpha = 0.6f,
            rayCount90 = 16, rayCount30 = 16, rayCount60 = 16, range = 50f, speed = 8f, trailSpacing = 0.30f,
            maxBounces = 3, bounceFalloff = 0.65f,
            lifetimeCap = 4f, tailLinger = 20f, bounceLifeLoss = 0.35f,
            diffuseCount15 = 8, diffuseCount45 = 8, diffuseCount75 = 8,
            hemisphereCount = 0, hemisphereDiffuse = false,
        };

        [Header("③ 潜行声波")]
        public EchoPreset sneakEcho = new EchoPreset
        {
            enabled = true, color = Color.white, initialAlpha = 0.22f,
            rayCount90 = 8, rayCount30 = 8, rayCount60 = 8, range = 2f, speed = 4f, trailSpacing = 0.25f,
            maxBounces = 2, bounceFalloff = 0.5f,
            lifetimeCap = 0.5f, tailLinger = 20f, bounceLifeLoss = 0.15f,
            diffuseCount15 = 4, diffuseCount45 = 4, diffuseCount75 = 4,
            hemisphereCount = 0, hemisphereDiffuse = false,
        };

        [Header("④ 空格踏步声波")]
        public KeyCode    manualPingKey = KeyCode.Space;
        public EchoPreset shoutEcho = new EchoPreset
        {
            enabled = true, color = Color.white, initialAlpha = 1.0f,
            rayCount90 = 80, rayCount30 = 80, rayCount60 = 80, range = 80f, speed = 8f, trailSpacing = 0.30f,
            maxBounces = 4, bounceFalloff = 0.65f,
            lifetimeCap = 10f, tailLinger = 20f, bounceLifeLoss = 0.35f,
            diffuseCount15 = 40, diffuseCount45 = 40, diffuseCount75 = 40,
            hemisphereCount = 0, hemisphereDiffuse = false,
        };

        // ------------------------------------------------------------------ 地牢模式的声波覆盖

        // ★★ 地牢模式（DungeonMode）专用的一套声波参数 ★★
        //
        //   探秘模式用上面那四个（walkEcho / runEcho / sneakEcho / shoutEcho），
        //   地牢模式**完全独立**用下面这几个，互不影响。
        //
        //   ★ 勾上 useDungeonEchoes 才会生效。
        //   ★ 不勾时，地牢模式**和探秘模式一样**用上面那四个。
        //
        //   所以：
        //     · 想让两个模式声波一致 -> 不勾
        //     · 想单独调地牢模式的声波数量 -> 勾上，然后改下面这四个

        [Header("⑤ 地牢模式声波（独立）")]
        [Tooltip("勾上 = 地牢模式使用下面这套独立参数。\n\n" +
                 "★ 不勾 = 地牢模式和探秘模式用同一套（上面那四个）。\n" +
                 "★ 探秘模式**永远**用上面那四个，跟这个开关无关。")]
        public bool useDungeonEchoes = false;

        [Tooltip("地牢 · 走路声波")]
        public EchoPreset dungeonWalkEcho = new EchoPreset
        {
            enabled = true, color = Color.white, initialAlpha = 0.4f,
            rayCount90 = 16, rayCount30 = 16, rayCount60 = 16, range = 30f, speed = 8f, trailSpacing = 0.30f,
            maxBounces = 3, bounceFalloff = 0.65f,
            lifetimeCap = 2f, tailLinger = 20f, bounceLifeLoss = 0.35f,
            diffuseCount15 = 8, diffuseCount45 = 8, diffuseCount75 = 8,
            hemisphereCount = 0, hemisphereDiffuse = false,
        };

        [Tooltip("地牢 · 冲刺声波")]
        public EchoPreset dungeonRunEcho = new EchoPreset
        {
            enabled = true, color = Color.white, initialAlpha = 0.6f,
            rayCount90 = 16, rayCount30 = 16, rayCount60 = 16, range = 50f, speed = 8f, trailSpacing = 0.30f,
            maxBounces = 3, bounceFalloff = 0.65f,
            lifetimeCap = 4f, tailLinger = 20f, bounceLifeLoss = 0.35f,
            diffuseCount15 = 8, diffuseCount45 = 8, diffuseCount75 = 8,
            hemisphereCount = 0, hemisphereDiffuse = false,
        };

        [Tooltip("地牢 · 潜行声波")]
        public EchoPreset dungeonSneakEcho = new EchoPreset
        {
            enabled = true, color = Color.white, initialAlpha = 0.22f,
            rayCount90 = 8, rayCount30 = 8, rayCount60 = 8, range = 2f, speed = 4f, trailSpacing = 0.25f,
            maxBounces = 2, bounceFalloff = 0.5f,
            lifetimeCap = 0.5f, tailLinger = 20f, bounceLifeLoss = 0.15f,
            diffuseCount15 = 4, diffuseCount45 = 4, diffuseCount75 = 4,
            hemisphereCount = 0, hemisphereDiffuse = false,
        };

        [Tooltip("地牢 · 空格踏步声波")]
        public EchoPreset dungeonShoutEcho = new EchoPreset
        {
            enabled = true, color = Color.white, initialAlpha = 1.0f,
            rayCount90 = 80, rayCount30 = 80, rayCount60 = 80, range = 80f, speed = 8f, trailSpacing = 0.30f,
            maxBounces = 4, bounceFalloff = 0.65f,
            lifetimeCap = 10f, tailLinger = 20f, bounceLifeLoss = 0.35f,
            diffuseCount15 = 40, diffuseCount45 = 40, diffuseCount75 = 40,
            hemisphereCount = 0, hemisphereDiffuse = false,
        };

        /// <summary>
        /// 是否正在使用地牢模式的那套声波参数。
        ///
        /// ★ 只看 useDungeonEchoes 这一个开关 —— **不按场景强制**。
        ///
        ///   之前这里写的是 useDungeonEchoes || GameUI.IsDungeonMode，
        ///   结果是"不勾也会用"，开关等于失效。
        ///   现在：勾上 = 用地牢那套；不勾 = 用探秘那套（两个模式都一样）。
        /// </summary>
        private bool UseDungeonEchoSet => useDungeonEchoes;

        // ------------------------------------------------------------------ 相机

        [Header("相机")]
        public float lookSensitivity = 2.6f;
        [Tooltip("俯视角度上限（负值 = 向上看）")]
        public float pitchMin = -42f;
        [Tooltip("仰视角度上限（正值 = 向下看）")]
        public float pitchMax = 89.9f;

        [Tooltip("每次开始 / 重玩时，相机距离重置回这个值（玩家用滚轮缩放过的距离会被还原）")]
        public float defaultCameraDistance = 9.0f;

        [Tooltip("当前相机距离（米），会被滚轮修改")]
        public float cameraDistance = 9.0f;
        [Tooltip("滚轮能拉近到多近")]
        public float zoomMin = 4f;
        [Tooltip("滚轮能拉到多远")]
        public float zoomMax = 14f;
        [Tooltip("滚轮灵敏度：每一格改变多少米")]
        public float zoomStep = 1f;

        public float cameraHeight   = 1.55f;
        public float cameraLag      = 14f;

        [Header("拉远时抬高相机")]
        [Tooltip("拉到最远时，相机额外抬高多少米。\n" +
                 "0 = 关闭这个效果。\n\n" +
                 "为什么需要：房间较高时，平视会让顶部超出画面、而地板以下空一大片。\n" +
                 "拉远时把相机抬高，画面中心会更接近房间中段。\n\n" +
                 "注意：这是字段初始值，只对**新建**的组件生效。\n" +
                 "场景里已有的实例请在检查器里改（Unity 会把实例值序列化进场景）。")]
        public float maxZoomExtraHeight = 3f;

        [Tooltip("滚轮距离到这个值时开始抬高（通常在默认距离附近）。\n" +
                 "比它近的距离完全不抬高 —— 近距离看角色时视角保持低位。")]
        public float zoomHeightStart = 8f;

        [Tooltip("抬高的曲线。1 = 线性；>1 = 越远抬得越猛（前段几乎不抬）；<1 = 一开始就抬得快")]
        [Range(0.2f, 4f)] public float zoomHeightCurve = 1.6f;

        [Tooltip("进入 Play 时锁定鼠标。关掉的话需要按住右键才能转视角")]
        public bool lockCursorOnStart = true;

        [Tooltip("相机防穿墙。正式玩法必须保持关闭：相机能穿过墙体，玩家才没法靠相机位置反推墙在哪。")]
        public bool cameraCollision = false;
        [Tooltip("仅在 cameraCollision 打开时生效")]
        public LayerMask cameraCollisionMask = ~((1 << 2) | (1 << 4) | (1 << 5));

        // ------------------------------------------------------------------ 运行状态

        public float CurrentNoiseLoudness { get; private set; }
        public MoveMode Mode { get; private set; } = MoveMode.Walk;
        public bool IsMoving { get; private set; }

        // ------------------------------------------------------------------ 操作检测
        // 供 HintZone 之类的组件读取：玩家最后做的是哪类操作、什么时候做的。
        // 只在"操作开始的那一帧"更新，持续按住不会每帧刷新 ——
        // 这样消费方可以用"时间戳是否变新"来判断又发生了一次操作。

        /// <summary>玩家最近一次操作的类型。</summary>
        public PlayerAction LastAction { get; private set; } = PlayerAction.None;

        /// <summary>最近一次操作发生的时间（Time.timeSinceLevelLoad）。</summary>
        public float LastActionTime { get; private set; } = -999f;

        private bool m_WasMoving;
        private bool m_WasLooking;

        private void NoteAction(PlayerAction action)
        {
            LastAction     = action;
            LastActionTime = Time.timeSinceLevelLoad;
        }

        private CharacterController m_Controller;
        private Transform m_CamPivot;

        private float m_Yaw;
        private float m_Pitch = 12f;
        private float m_VerticalVelocity;
        private Vector3 m_HorizontalVelocity;

        private float m_StrideAccum;
        private float m_LastEmitTime;

        /// <summary>
        /// 是否已经迈出过"起步第一步"。
        /// 静止时置回 false，于是下一次起步会先用 firstStepDistance 触发一声。
        /// </summary>
        private bool m_HasTakenFirstStep;

        private void Awake()
        {
            m_Controller = GetComponent<CharacterController>();

            // 这几个是"手工把 Player 拖进场景时也必须存在"的配套系统。
            // 以前由 EchoBootstrap 统一创建，现在关卡是手搭的，所以在这里补齐。
            EnsureCamera();
            EnsureSonar();
            EnsureMarkerSystem();
            EnsureFootprintSystem();

            // ★★ 地牢模式：没有石子 / 匕首，这两个系统整个不建 ★★
            //
            //   StoneThrowSystem 的 Awake 会建出 AimRing / LandingRing 两个圆环，
            //   DaggerSystem 会建出 DaggerAimRing —— 它们用 AimOverlay 材质
            //   （ZTest Always + Overlay 队列），一旦被启用就会穿透一切，
            //   在地图上显示成一个漂浮的圆环。
            //   地牢模式的武器是枪，这两套都不该存在。
            if (!GameUI.IsDungeonMode)
            {
                EnsureStoneThrowSystem();
                EnsureDaggerSystem();
            }

            m_Yaw = transform.eulerAngles.y;

            if (m_CamPivot == null)
            {
                var pivot = new GameObject("EchoCameraPivot");
                m_CamPivot = pivot.transform;
                m_CamPivot.SetParent(null);
            }
            m_CamPivot.position = transform.position + Vector3.up * CurrentCameraHeight();
            m_CamPivot.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);

            // 开局就把相机摆到默认俯仰 / 缩放距离，避免第一帧闪一下
            if (playerCamera != null)
            {
                Transform ct = playerCamera.transform;
                ct.position = m_CamPivot.position - m_CamPivot.forward * cameraDistance;
                ct.rotation = m_CamPivot.rotation;
            }

            // 光标由 GameUI 统一管理（首页 / 暂停要解锁）。这里不主动锁定。
        }

        /// <summary>场景里没有相机就建一个（纯黑清屏 + Bloom）。</summary>
        private void EnsureCamera()
        {
            playerCamera = Camera.main;
            if (playerCamera != null) return;
            if (!autoCreateCamera) return;

            m_AutoCamera = new GameObject("EchoCamera");

            var cam = m_AutoCamera.AddComponent<Camera>();
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView     = 68f;
            cam.nearClipPlane   = 0.05f;
            cam.farClipPlane    = 400f;
            m_AutoCamera.tag    = "MainCamera";

            var data = m_AutoCamera.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.renderShadows        = true;
            data.antialiasing         = UnityEngine.Rendering.Universal.AntialiasingMode.FastApproximateAntialiasing;

            // Bloom 是声波发光的关键
            var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            profile.name      = "EchoVolumeProfile";
            profile.hideFlags = HideFlags.DontSave;

            var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
            bloom.intensity.overrideState = true;
            bloom.intensity.value         = 1.1f;
            bloom.threshold.overrideState = true;
            bloom.threshold.value         = 0.85f;
            bloom.scatter.overrideState   = true;
            bloom.scatter.value           = 0.72f;

            var vignette = profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
            vignette.intensity.overrideState  = true;
            vignette.intensity.value          = 0.30f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value         = 0.6f;

            var volumeGo = new GameObject("EchoVolume");
            volumeGo.transform.SetParent(m_AutoCamera.transform, false);

            var volume = volumeGo.AddComponent<UnityEngine.Rendering.Volume>();
            volume.isGlobal      = true;
            volume.priority      = 10f;
            volume.sharedProfile = profile;

            playerCamera = cam;

            // ★ 相机必须带一个 AudioListener，否则整个场景一点声音都听不见，
            //   而且每个 AudioSource 播放时都会刷一条
            //   "There are no audio listeners in the scene" 警告。
            EnsureAudioListener(m_AutoCamera);

            Debug.Log("[EchoPuzzle] 场景里没有相机，已自动创建一个（EchoCamera）。");
        }

        /// <summary>
        /// 保证场上恰有一个 AudioListener。
        ///
        /// 场景里完全没有相机时（本项目的常见情况），这里会补在自动创建的相机上。
        /// 已经有的话不动它 —— 多个 AudioListener 反而会让 Unity 报警告。
        /// </summary>
        private static void EnsureAudioListener(GameObject host)
        {
            if (Object.FindObjectOfType<AudioListener>() != null) return;

            host.AddComponent<AudioListener>();
            Debug.Log("[EchoPuzzle] 场景里没有 AudioListener，已挂到自动创建的相机上。");
        }

        /// <summary>场景里没有声波系统就建一个。</summary>
        private void EnsureSonar()
        {
            echo = FindObjectOfType<SonarSystem>();
            if (echo != null) return;
            if (!autoCreateSonar) return;

            m_AutoSonar = new GameObject("SonarSystem");

            var rendererGo = new GameObject("SonarTrailRenderer");
            rendererGo.transform.SetParent(m_AutoSonar.transform, false);
            var trailRenderer = rendererGo.AddComponent<SonarTrailRenderer>();

            echo = m_AutoSonar.AddComponent<SonarSystem>();
            echo.trailRenderer = trailRenderer;

            Debug.Log("[EchoPuzzle] 场景里没有声波系统，已自动创建一个（SonarSystem）。");
        }

        /// <summary>场景里没有标记系统就建一个（按数字键 1~5 放标记）。</summary>
        private void EnsureMarkerSystem()
        {
            if (FindObjectOfType<MarkerSystem>() != null) return;

            var go = new GameObject("MarkerSystem");
            var ms = go.AddComponent<MarkerSystem>();
            ms.player = this;
        }

        /// <summary>场景里没有脚印系统就建一个。</summary>
        private void EnsureFootprintSystem()
        {
            footprints = FindObjectOfType<FootprintSystem>();
            if (footprints != null) return;

            var go = new GameObject("FootprintSystem");
            footprints = go.AddComponent<FootprintSystem>();
            footprints.player = this;
        }

        /// <summary>投掷石子系统。场景里没有就建一个。</summary>
        public StoneThrowSystem stones;

        private void EnsureStoneThrowSystem()
        {
            // ★ 连**未激活**的也一起找 —— 否则某人把它 SetActive(false) 之后，
            //   这里会找不到、再建一个新的（旧的圆环还在，又多了新的）。
            stones = FindObjectOfType<StoneThrowSystem>(true);
            if (stones != null) return;

            var go = new GameObject("StoneThrowSystem");
            stones = go.AddComponent<StoneThrowSystem>();
            stones.player = this;
        }

        /// <summary>匕首刺出技能。场景里没有就建一个。</summary>
        public DaggerSystem dagger;

        private void EnsureDaggerSystem()
        {
            // ★ 同上：连未激活的也一起找，避免重复创建
            dagger = FindObjectOfType<DaggerSystem>(true);
            if (dagger != null) return;

            var go = new GameObject("DaggerSystem");
            dagger = go.AddComponent<DaggerSystem>();
            dagger.player = this;
        }

        /// <summary>把玩家与相机瞬移回指定位置，并清掉动量。重玩时用。</summary>
        public void Teleport(Vector3 position, float yaw, float pitch)
        {
            if (m_Controller != null) m_Controller.enabled = false;

            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            m_Yaw   = yaw;
            m_Pitch = pitch;
            m_VerticalVelocity   = 0f;
            m_HorizontalVelocity = Vector3.zero;

            // 瞬移后等同于"从静止重新起步"：累计器和第一步标志都要清
            m_StrideAccum        = 0f;
            m_HasTakenFirstStep  = false;

            // 每次开局把相机距离还原成默认值，玩家滚轮缩放过的距离不跨局残留
            cameraDistance = defaultCameraDistance;

            if (m_CamPivot != null)
            {
                m_CamPivot.position = position + Vector3.up * CurrentCameraHeight();
                m_CamPivot.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            if (playerCamera != null)
            {
                Transform ct = playerCamera.transform;
                ct.position = m_CamPivot != null
                    ? m_CamPivot.position - m_CamPivot.forward * cameraDistance
                    : position;
                ct.rotation = m_CamPivot != null ? m_CamPivot.rotation : Quaternion.identity;
            }

            if (m_Controller != null) m_Controller.enabled = true;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // 只在"镜头还需要接受输入"时重新锁光标（进行中 / 胜负界面淡出期）。
            // 首页 / 暂停 / 淡出结束后的胜负界面都保持解锁 —— 那些界面要用鼠标点按钮。
            // ★ 任何瞄准期间都保持解锁（投石 / 匕首都要用鼠标位置做极坐标），
            //   否则切窗口回来时这里会把瞄准用的光标重新锁死。
            // 只有投石需要解锁光标；匕首用虚拟光标，光标保持锁定。
            bool aiming = stones != null && stones.IsAiming;

            if (hasFocus && lockCursorOnStart && GameUI.CameraInputActive && !aiming)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible   = false;
            }
        }

        /// <summary>脚下的地面点 —— 脚步的声源就放在这里。</summary>
        public Vector3 FootPosition
        {
            get
            {
                Vector3 p = transform.position;
                p.y += m_Controller.skinWidth + 0.01f;
                return p;
            }
        }

        // ------------------------------------------------------------------ 主循环

        private void Update()
        {
            // 镜头是否接受输入（转视角 / 滚轮）。
            // 首页 / 暂停时为 false —— 否则会跟 UI 按钮抢鼠标。
            // 但**胜负界面淡出那几秒**是 true：那几秒里世界继续跑，还能环顾四周。
            if (!GameUI.CameraInputActive)
            {
                m_HorizontalVelocity = Vector3.zero;
                IsMoving = false;
                return;
            }

            HandleLook();
            HandleZoom();

            // 角色本身是否可操作。
            // 胜负界面淡出期间这里是 false —— 世界继续跑、光带继续动、镜头能动，
            // 但人不能再走、不能再发声、不能再放标记。
            if (!GameUI.PlayerControllable)
            {
                // 已经站住了就别再走，但也不要强行归零速度 ——
                // 让惯性自然停下，看起来更顺（角色会滑一小段然后停住）。
                m_HorizontalVelocity = Vector3.MoveTowards(m_HorizontalVelocity, Vector3.zero,
                                                           accel * 2f * Time.deltaTime);
                IsMoving = false;
                m_WasMoving = false;

                // 停下 -> 累计器和第一步标志都清，下次起步重新从 0.5m 开始
                m_StrideAccum       = 0f;
                m_HasTakenFirstStep = false;

                // 重力照常，免得站在半空时僵住
                if (m_Controller != null)
                {
                    if (m_Controller.isGrounded && m_VerticalVelocity < 0f) m_VerticalVelocity = -2f;
                    m_VerticalVelocity += Physics.gravity.y * Time.deltaTime;
                    m_Controller.Move(Vector3.up * m_VerticalVelocity * Time.deltaTime);
                }

                return;
            }

            HandleMove();
            HandleStrideNoise();

            if (Input.GetKeyDown(manualPingKey))
            {
                NoteAction(PlayerAction.Space);

                // ★ 空格（跺脚）是独立声源：**不受潜行影响**
                //   地牢模式用自己那一套 dungeonShoutEcho
                EchoPreset shoutPreset = UseDungeonEchoSet ? dungeonShoutEcho : shoutEcho;

                if (echo != null && shoutPreset.enabled)
                    Emit(shoutPreset, affectedBySneak: false);

                // 空格（跺脚）的音效。放在 Emit 外面：
                // Emit 里挂的是"脚步类"音效（按 Mode 分走/跑/潜行），
                // 而空格是单独一种动作，不该跟着脚步声走。
                AudioSystem.Play(AudioSystem.Cue.Stomp);
            }

            // 按 R：把相机拉回玩家正后方（朝向玩家正前方），
            // 俯仰角和缩放距离用下面的可调参数
            if (Input.GetKeyDown(cameraResetKey)) ResetCamera();
        }

        // ------------------------------------------------------------------ 相机复位

        [Header("按 R 复位相机")]
        [Tooltip("触发相机复位的按键")]
        public KeyCode cameraResetKey = KeyCode.R;

        [Tooltip("复位后的俯仰角（度）。正数 = 从上往下看")]
        public float resetPitch = 12f;

        [Tooltip("复位后的缩放距离（米）")]
        public float resetDistance = 9f;

        [Tooltip("复位时相机是否瞬移过去。取消勾选则平滑过渡")]
        public bool resetCameraInstant = true;

        /// <summary>
        /// 把相机复位到玩家**正后方**（也就是朝向玩家正前方）。
        ///
        /// 做法：取玩家当前正前方的水平方向，把相机枢轴的 yaw / pitch 设成与它一致，
        /// 距离用 <see cref="resetDistance"/>。相机在 LastUpdate 里会被摆到
        /// 「枢轴位置 - 枢轴前方 × 距离」，所以正好落在玩家背后、和角色朝同一个方向。
        ///
        /// 玩家自身朝向不变，只是视角回到背后。
        /// </summary>
        public void ResetCamera()
        {
            // 玩家正前方（投影到水平面，避免前后倾斜影响 yaw）
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();

            m_Yaw          = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            m_Pitch        = Mathf.Clamp(resetPitch, pitchMin, pitchMax);
            cameraDistance = Mathf.Clamp(resetDistance, zoomMin, zoomMax);

            if (resetCameraInstant) SnapCameraToPivot();
        }

        /// <summary>立刻把相机摆到枢轴后面的正确位置（不做插值）。</summary>
        private void SnapCameraToPivot()
        {
            if (m_CamPivot == null) return;

            m_CamPivot.position = transform.position + Vector3.up * CurrentCameraHeight();
            m_CamPivot.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);

            if (playerCamera != null)
            {
                Transform ct = playerCamera.transform;
                ct.position = m_CamPivot.position - m_CamPivot.forward * cameraDistance;
                ct.rotation = m_CamPivot.rotation;
            }
        }

        // ------------------------------------------------------------------ 对外访问（存档用）

        /// <summary>当前水平朝向（度）。</summary>
        public float Yaw => m_Yaw;

        /// <summary>当前俯仰角（度）。</summary>
        public float Pitch => m_Pitch;

        /// <summary>当前缩放距离（米）。可读可写。</summary>
        public float Distance
        {
            get => cameraDistance;
            set => cameraDistance = Mathf.Clamp(value, zoomMin, zoomMax);
        }

        /// <summary>读档时用：直接把朝向 / 俯仰 / 距离设成指定值并立刻摆好相机。</summary>
        public void ResetCameraImmediate(float pitch, float yaw, float distance)
        {
            m_Pitch        = Mathf.Clamp(pitch, pitchMin, pitchMax);
            m_Yaw          = yaw;
            cameraDistance = Mathf.Clamp(distance, zoomMin, zoomMax);

            SnapCameraToPivot();
        }

        private void HandleLook()
        {
            // 光标没锁定时不转视角（由 GameUI 负责在进入游戏时锁定）
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                m_WasLooking = false;
                return;
            }

            // ★ 投掷瞄准时**不转视角**。
            //   瞄准是用鼠标拖动的：以按下右键的位置为原点算角度和力度，
            //   如果这里同时还在转镜头，鼠标一动视角就飘了，根本没法瞄。
            var thrower = StoneThrowSystem.Instance;
            if (thrower != null && thrower.IsAiming)
            {
                m_WasLooking = false;
                return;
            }

            // ★ 匕首瞄准时同样不转视角 —— 鼠标改为控制匕首的水平角。
            //
            //   例外：受惊死亡倒计时期间匕首被「冻结」（圆环 + 匕首还在淡出，
            //   但鼠标不再控制它们），这时要让鼠标**重新接管镜头**。
            var dagger = DaggerSystem.Instance;
            if (dagger != null && dagger.IsAiming && !dagger.IsAimFrozen)
            {
                m_WasLooking = false;
                return;
            }

            float dx = Input.GetAxisRaw("Mouse X");
            float dy = Input.GetAxisRaw("Mouse Y");

            m_Yaw   += dx * lookSensitivity;
            m_Pitch -= dy * lookSensitivity;
            m_Pitch  = Mathf.Clamp(m_Pitch, pitchMin, pitchMax);

            // 只在"从没转 -> 开始转"的那一帧记一次，持续转不会每帧刷新时间戳
            bool looking = Mathf.Abs(dx) > 0.01f || Mathf.Abs(dy) > 0.01f;
            if (looking && !m_WasLooking) NoteAction(PlayerAction.Look);
            m_WasLooking = looking;
        }

        private void HandleZoom()
        {
            float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) < 0.0001f) return;

            cameraDistance = Mathf.Clamp(cameraDistance - scroll * zoomStep * 10f,
                                         zoomMin, zoomMax);

            NoteAction(PlayerAction.Zoom);
        }

        private void HandleMove()
        {
            bool wantRun   = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool wantSneak = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                          || Input.GetKey(KeyCode.C);

            Mode = wantSneak ? MoveMode.Sneak : (wantRun ? MoveMode.Run : MoveMode.Walk);

            float sneakNow = sneakSpeed;

            // ★ 击杀怪物后的潜行加速：
            //   刚击杀时 = sneakSpeed × sneakBoostMultiplier（默认 4 倍），
            //   然后在 sneakBoostFadeDuration（默认 1 秒）内**线性**回落到 sneakSpeed。
            if (m_SneakBoostTimer > 0f)
            {
                m_SneakBoostTimer -= Time.deltaTime;

                float dur = Mathf.Max(0.01f, sneakBoostFadeDuration);
                float k   = Mathf.Clamp01(m_SneakBoostTimer / dur);   // 1 -> 0

                sneakNow = Mathf.Lerp(sneakSpeed, sneakSpeed * Mathf.Max(1f, sneakBoostMultiplier), k);
            }

            float speed = Mode == MoveMode.Sneak ? sneakNow
                        : Mode == MoveMode.Run   ? runSpeed
                        : walkSpeed;

            // ★ 受惊死亡倒计时期间：**只能以很慢的速度移动**（默认 0.4 m/s）
            float cap = GameUI.FrightenedMoveSpeed;
            if (speed > cap) speed = cap;

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            // 左右方向与直觉相反，所以把水平轴取反。前后（v）不动。
            h = -h;

            if (Mathf.Abs(h) > 0.01f || Mathf.Abs(v) > 0.01f)
            {
                // 用相机朝向决定"前"
                Vector3 fwd = m_CamPivot != null ? m_CamPivot.forward : transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                fwd.Normalize();

                Vector3 right = Vector3.Cross(fwd, Vector3.up).normalized;

                Vector3 world = (fwd * v + right * h).normalized * speed;
                m_HorizontalVelocity = Vector3.MoveTowards(m_HorizontalVelocity, world, accel * Time.deltaTime);
                IsMoving = true;

                // 只在"从静止 -> 开始走"的那一帧记一次
                if (!m_WasMoving) NoteAction(PlayerAction.Move);
            }
            else
            {
                m_HorizontalVelocity = Vector3.MoveTowards(m_HorizontalVelocity, Vector3.zero,
                                                           accel * 2f * Time.deltaTime);
                IsMoving = false;
            }

            m_WasMoving = IsMoving;

            // 重力
            if (m_Controller.isGrounded && m_VerticalVelocity < 0f) m_VerticalVelocity = -2f;
            m_VerticalVelocity += Physics.gravity.y * Time.deltaTime;

            m_Controller.Move((m_HorizontalVelocity + Vector3.up * m_VerticalVelocity) * Time.deltaTime);

            // 角色面朝移动方向
            Vector3 flat = m_HorizontalVelocity;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.05f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(flat.normalized, Vector3.up), 12f * Time.deltaTime);
        }

        private void HandleStrideNoise()
        {
            if (echo == null) return;

            EchoPreset preset = Mode == MoveMode.Sneak ? sneakEcho
                              : Mode == MoveMode.Run   ? runEcho
                              : walkEcho;

            if (!preset.enabled) return;

            float stride = Mode == MoveMode.Sneak ? sneakStride
                         : Mode == MoveMode.Run   ? runStride
                         : walkStride;

            bool isMoving = m_HorizontalVelocity.sqrMagnitude > MovingThreshold;

            // ---------------------------------------------------------------
            // ★ 玩家停下 -> 重置一切，下一次起步重新开始
            //
            //   这是之前最核心的 bug：旧代码在 moved <= 0 时直接 return，
            //   累计器 m_StrideAccum **永远不会被清零**。
            //   于是"走 3 米停下再走 1 米"这种情况，累计器还留着上次的余量，
            //   新的一步要靠旧余量凑够 stride 才响 —— 节奏完全不对。
            // ---------------------------------------------------------------
            if (!isMoving)
            {
                m_StrideAccum       = 0f;
                m_HasTakenFirstStep = false;
                return;
            }

            float moved = m_HorizontalVelocity.magnitude * Time.deltaTime;
            m_StrideAccum += moved;

            // ---------------------------------------------------------------
            // 阈值：
            //   · 还没迈出第一步 -> firstStepDistance（默认 0.5 米）
            //   · 之后           -> 各自的 stride（走 / 跑 / 潜行）
            // ---------------------------------------------------------------
            float threshold = !m_HasTakenFirstStep
                            ? Mathf.Max(0.05f, firstStepDistance)
                            : stride;

            if (m_StrideAccum < threshold) return;

            // 最小间隔防抖：还没到间隔就**不消耗**累计器，下一帧继续累
            // （旧代码在这里 return 之前已经把 accum 加上去了，但不会清零，
            //   所以这里不清零是对的 —— 只是别提前把 accum 归零）
            if (Time.timeSinceLevelLoad - m_LastEmitTime < minEmitInterval) return;

            // ---------------------------------------------------------------
            // 发一声。
            //
            // ★ 扣除而不是清零：
            //   如果这帧刚好跨过阈值，多走的那一点应该计入下一步。
            //   例如 firstStepDistance = 0.5、这帧到 0.53，那么剩下的 0.03
            //   属于下一步，不该被丢掉。这样长期看节奏是准确的。
            // ---------------------------------------------------------------
            m_StrideAccum  -= threshold;
            if (m_StrideAccum < 0f) m_StrideAccum = 0f;

            m_LastEmitTime      = Time.timeSinceLevelLoad;
            m_HasTakenFirstStep = true;      // 之后按 stride 走

            // ★ 脚步：受潜行影响（潜行 -> isSneak，怪物捕捉不到）
            //   地牢模式整组换成自己那套（dungeonWalkEcho 等）
            if (UseDungeonEchoSet)
            {
                preset = Mode == MoveMode.Sneak ? dungeonSneakEcho
                       : Mode == MoveMode.Run   ? dungeonRunEcho
                       :                          dungeonWalkEcho;
            }

            Emit(preset, affectedBySneak: true);

            CurrentNoiseLoudness = Mode == MoveMode.Sneak ? 0.25f
                                 : Mode == MoveMode.Run   ? 1.6f
                                 : 1.0f;
        }

        /// <summary>
        /// 判定"玩家是否在移动"的速度阈值（平方值）。
        ///
        /// 取一个很小的值，只要脚下一动就算移动。
        /// 用速度而不是位移来判断，是因为停下那一帧的位移也可能非零。
        /// </summary>
        private const float MovingThreshold = 0.0025f;   // 0.05 m/s 的平方

        /// <summary>
        /// 把一个 EchoPreset 参数集发出去。
        ///
        /// **所有四种声波（走 / 跑 / 潜行 / 空格）都走这里**，
        /// 所以脚印也挂在这一处 —— 每次释放声波就在脚下打一个，
        /// 左右自动交替。步行 / 冲刺 / 潜行的发射频率不同，
        /// 脚印的疏密自然也跟着不同。
        ///
        /// 发射方向统一由 SonarSystem 处理：先检测脚下地面法线，
        /// 再按 0° / 30° / 60° 三个固定仰角层撒射线。
        /// </summary>
        /// <param name="affectedBySneak">
        /// 这次发声是否受"潜行"影响。
        ///
        /// ★ 只有**脚步**（走路 / 跑步）才受潜行影响：
        ///   潜行时不发声，怪物也就捕捉不到。
        ///
        ///   空格（跺脚）是**独立**的声源，不该因为按着 Ctrl 就变成潜行声波 ——
        ///   否则玩家可以"潜行 + 跺脚"来放声波却不让怪物听到，破坏玩法。
        /// </param>
        private void Emit(EchoPreset preset, bool affectedBySneak)
        {
            EmitParams p = preset.ToEmitParams();

            // ★ 方位角起点 = 玩家正前方。
            //   这样"相对正前方偏转半格"才有意义；否则方位角是相对世界轴算的，
            //   玩家一转身，图案就跟着世界轴而不是跟着人。
            //   只取水平分量 —— 抬头低头不该改变方位角的起点。
            Vector3 facing = transform.forward;
            facing.y = 0f;
            p.referenceDir = facing.sqrMagnitude > 1e-6f ? facing.normalized : Vector3.forward;

            // ★ 潜行产生的**脚步**打上标记：怪物不会捕捉它，玩家位置因此不会暴露。
            //   其它声源（空格跺脚）不受影响 —— 见 affectedBySneak 的说明。
            //
            //   ★ 例外：在 Gravel（碎石区）里潜行时，脚步会暴露 ——
            //     碎石地上藏不住脚步，光带**照常能被怪物捕捉**。
            bool sneakSilent = affectedBySneak
                            && Mode == MoveMode.Sneak
                            && !Gravel.AnyPlayerInside();

            p.isSneak = sneakSilent;

            echo.Emit(FootPosition, p);

            if (footprints != null) footprints.Print();

            // 脚步声：走路 / 跑步各有各的音效，潜行不响。
            // 挂在这里是因为它和声波是同一个节奏 ——
            // 一步一声波，也就是一步一声响，不会对不上。
            switch (Mode)
            {
                case MoveMode.Walk: AudioSystem.Play(AudioSystem.Cue.Walk); break;
                case MoveMode.Run:  AudioSystem.Play(AudioSystem.Cue.Run);  break;

                case MoveMode.Sneak:
                    // ★ 例外：在 Gravel（碎石区）里潜行时会踩出声音 ——
                    //   碎石地本来就藏不住脚步声。用走路音效。
                    if (Gravel.AnyPlayerInside())
                        AudioSystem.Play(AudioSystem.Cue.Walk);
                    break;
            }
        }

        /// <summary>
        /// 当前应该用的相机枢轴高度。
        ///
        /// 基础值是 <see cref="cameraHeight"/>；当滚轮距离超过
        /// <see cref="zoomHeightStart"/> 之后，按比例往上一路加到
        /// <see cref="maxZoomExtraHeight"/>。拉近时（距离不大于起点）不加高。
        /// </summary>
        public float CurrentCameraHeight()
        {
            if (maxZoomExtraHeight <= 0f) return cameraHeight;

            float range = zoomMax - zoomHeightStart;
            if (range <= 0.01f) return cameraHeight;

            float extra = Mathf.Clamp01((cameraDistance - zoomHeightStart) / range);

            // 曲线：1 = 线性；>1 = 前段几乎不抬、后段抬得猛
            extra = Mathf.Pow(extra, Mathf.Max(0.01f, zoomHeightCurve));

            return cameraHeight + maxZoomExtraHeight * extra;
        }

        private void LateUpdate()
        {
            if (m_CamPivot == null) return;

            // 拉远时把镜头抬高：房间高的话平视会让顶部出画、地板下方空一大片。
            // 拉近时（距离 <= zoomHeightStart）完全不抬，保持看角色的低视角。
            float wantHeight = CurrentCameraHeight();
            Vector3 target = transform.position + Vector3.up * wantHeight;

            float t = 1f - Mathf.Exp(-cameraLag * Time.deltaTime);
            m_CamPivot.position = Vector3.Lerp(m_CamPivot.position, target, t);

            // ★ 高度**瞬时**到位，不参与上面的平滑。
            //
            //   为什么：滚轮改的是 cameraDistance，而相机位置是
            //   「枢轴 - 前方向 * 距离」，距离是瞬时的、枢轴高度如果还走 lerp，
            //   两者就会不同步 —— 看起来像玩家物体在下落。
            //   高度直接吸附到目标值，滚多少抬多少，和拉远是同一个瞬间完成的。
            Vector3 p = m_CamPivot.position;
            p.y = target.y;
            m_CamPivot.position = p;

            m_CamPivot.rotation = Quaternion.Slerp(m_CamPivot.rotation,
                Quaternion.Euler(m_Pitch, m_Yaw, 0f), t);

            if (playerCamera != null)
            {
                Transform ct = playerCamera.transform;

                // 相机允许穿墙 —— 否则玩家能靠相机被挡住的位置反推墙体轮廓
                float dist = cameraDistance;
                Vector3 back = -m_CamPivot.forward;

                if (cameraCollision &&
                    Physics.SphereCast(m_CamPivot.position, 0.28f, back,
                        out RaycastHit hit, cameraDistance, cameraCollisionMask,
                        QueryTriggerInteraction.Ignore))
                {
                    dist = Mathf.Max(0.6f, hit.distance - 0.05f);
                }

                ct.position = m_CamPivot.position + back * dist;
                ct.rotation = m_CamPivot.rotation;

                // ★ 投掷瞄准时镜头朝落点方向偏移（偏移量由 StoneThrowSystem 每帧算好）。
                //   放在相机位置定稿之后，保证本帧内不被其它逻辑覆盖。
                var thrower = StoneThrowSystem.Instance;
                if (thrower != null && thrower.IsAiming)
                {
                    Vector3 off = thrower.CameraOffset;
                    if (off.sqrMagnitude > 0.0001f) ct.position += off;
                }
            }
        }

        private void OnDestroy()
        {
            if (m_CamPivot  != null) Destroy(m_CamPivot.gameObject);
            if (m_AutoCamera != null) Destroy(m_AutoCamera);
            if (m_AutoSonar  != null) Destroy(m_AutoSonar);
        }
    }

    /// <summary>
    /// 一种发声方式的完整参数，全部可在 Inspector 里调。
    /// </summary>
    [System.Serializable]
    public struct EchoPreset
    {
        public bool enabled;

        [Tooltip("声波颜色。你想要纯白就保持白色")]
        public Color color;

        [Tooltip("初始不透明度 0..1。这是唯一区分四种步伐的刻度：\n" +
                 "衰减速度全局统一，所以初始值越低 = 越早暗到看不见 = 能持续的时间越短")]
        [Range(0f, 1f)] public float initialAlpha;

        [Tooltip("镜面反射类：与地面法线夹角 90°（即水平面）的射线数量。这类射线反射时走严格镜面")]
        public int rayCount90;

        [Tooltip("镜面反射类：与地面法线夹角 30°（即相对水平面仰起 60°）的射线数量")]
        public int rayCount30;

        [Tooltip("镜面反射类：与地面法线夹角 60°（即相对水平面仰起 30°）的射线数量")]
        public int rayCount60;

        [Tooltip("单根射线的最大传播距离（米）")]
        public float range;

        [Tooltip("声速（米/秒）。波前扩散的快慢")]
        public float speed;

        [Tooltip("光带采样间距（米）。越小越顺滑，几何量越大")]
        public float trailSpacing;

        [Tooltip("最大反射次数。注意开销是 2 的幂次增长，慎调大")]
        public int maxBounces;

        [Tooltip("每次反射后的不透明度衰减系数")]
        public float bounceFalloff;

        [Tooltip("【每次反射扣掉的波前时长（秒）】每碰一次墙，这一小段的波前存在时长就减这么多。\n" +
                 "所以绕得越远的声波，能继续推进的时间越短 —— 比单纯限制反射次数自然得多。\n\n" +
                 "扣到 0 以下时这条子射线**根本不会生成**，等于声波绕不动了。\n" +
                 "填 0 = 不扣（各次反射的时长都一样）。\n\n" +
                 "和 lifetimeCap 配合：例如 lifetimeCap = 1.5、本值 = 0.4，\n" +
                 "则第 1 次反射后只剩 1.1 秒、第 2 次 0.7 秒、第 4 次就没了。")]
        public float bounceLifeLoss;

        [Tooltip("【波前存在时长（秒）】从声波诞生起算。时间一到，波前**停止推进**，\n" +
                 "已经长出来的光带形状会钉在原地不再生长。\n\n" +
                 "四种声波各有一份，互不影响 —— 这是控制『每种声波能扫多远』的开关。\n" +
                 "填 0 或负数 = 不限时，一直推进到射程尽头。")]
        public float lifetimeCap;

        [Tooltip("【尾巴残留时长（秒）】波前停住之后，光带还能继续存在多久。\n" +
                 "这段时间里它会留在原地继续变暗，直到彻底看不见才消失 ——\n" +
                 "所以『波前没了』和『光带没了』是两个独立的时刻。\n\n" +
                 "填 0 = 波前一停就立刻一起消失，不留尾巴。\n" +
                 "填大 = 波前扫过的轨迹像余像一样挂很久。")]
        public float tailLinger;

        [Tooltip("漫反射类：与地面法线夹角 15°（接近朝天，几乎垂直向上）的射线数量。这类射线反射时走漫反射")]
        public int diffuseCount15;

        [Tooltip("漫反射类：与地面法线夹角 45°（即相对水平面仰起 45°）的射线数量")]
        public int diffuseCount45;

        [Tooltip("漫反射类：与地面法线夹角 75°（接近贴地，几乎水平）的射线数量")]
        public int diffuseCount75;

        [Tooltip("★ 全上半球采样的射线数量。0 = 不启用。\n\n" +
                 "与六层俯仰角采样**并行**：这一部分射线在整个上半球面上分布\n" +
                 "（余弦加权 Fibonacci，正对法线的方向更密），不受六层角度限制。\n" +
                 "方向同样以脚下地面法线为轴 —— 斜坡上会跟着坡面倾斜，和六层一致。")]
        public int hemisphereCount;

        [Tooltip("半球采样出来的射线，反射时是否走漫反射（朗伯）。\n" +
                 "勾选 = 漫反射（在命中面上半球均匀散射）；\n" +
                 "取消 = 严格镜面（和 90°/30°/60° 那三层一样）。")]
        public bool hemisphereDiffuse;

        public EmitParams ToEmitParams()
        {
            return new EmitParams
            {
                rayCount90     = Mathf.Max(0, rayCount90),
                rayCount30     = Mathf.Max(0, rayCount30),
                rayCount60     = Mathf.Max(0, rayCount60),
                range          = range,
                speed          = speed,
                trailSpacing     = trailSpacing,
                intensity      = initialAlpha,
                lifetime       = lifetimeCap,
                tailLinger     = tailLinger,
                bounceLifeLoss = bounceLifeLoss,
                maxBounces     = maxBounces,
                bounceFalloff  = bounceFalloff,
                emitMode       = EmitMode.ElevationLayers,
                emitAxis       = Vector3.zero,          // 零向量 = 由 SonarSystem 检测地面法线
                coneAngle      = 90f,
                diffuseCount15 = Mathf.Max(0, diffuseCount15),
                diffuseCount45 = Mathf.Max(0, diffuseCount45),
                diffuseCount75 = Mathf.Max(0, diffuseCount75),
                hemisphereCount   = Mathf.Max(0, hemisphereCount),
                hemisphereDiffuse = hemisphereDiffuse,
                color          = color,
            };
        }
    }
}
