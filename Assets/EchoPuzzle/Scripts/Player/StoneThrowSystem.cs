using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 投掷石子系统。
    ///
    /// ── 瞄准方式 ────────────────────────────────────────────────
    ///   按住 <see cref="aimKey"/>（右键）进入瞄准：
    ///     · **鼠标不再转镜头**（玩家自己会把 HandleLook 关掉）
    ///     · 以**按下右键那一刻的鼠标位置**为原点，建立一个极坐标系
    ///     · 鼠标相对原点的**角度** = 投掷水平角
    ///     · 鼠标相对原点的**距离** = 投掷力度（映射到 3m ~ 18m 的落点距离）
    ///
    ///   瞄准时每点一次左键投出一颗石子，俯仰角固定为水平（纯水平抛出）。
    ///
    /// ── 可视化 ─────────────────────────────────────────────────
    ///   · 准星圈画在一个**与水平面平行**的面上，高度 = 石子出发点高度
    ///     （用世界空间的圆环网格，所以它会跟着相机透视，看起来是躺在地面上的一圈）
    ///   · 轨迹预览线在**下落到玩家脚底平面**时终止，并在终点画一个水平圆
    ///
    /// ── 石子行为 ────────────────────────────────────────────────
    ///   · 石子本身沿着真实的抛物线飞行，石子光带紧跟它的轨迹
    ///   · 撞墙 -> 波前消失，光带残留淡出，并在命中点产生一次声波
    ///   · 石子**穿过**怪物的感知圆柱和实体，不吸引它们；只有撞墙后的声波才会
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class StoneThrowSystem : MonoBehaviour
    {
        public static StoneThrowSystem Instance { get; private set; }

        // ---------------------------------------------------------------- 瞄准

        [Header("瞄准")]
        [Tooltip("按住这个键进入瞄准模式")]
        public KeyCode aimKey = KeyCode.Mouse1;

        [Tooltip("瞄准时用这个键投出石子")]
        public KeyCode throwKey = KeyCode.Mouse0;

        [Tooltip("鼠标拖动多少像素算『力度拉满』。越小越灵敏")]
        public float dragFullDistance = 260f;

        [Tooltip("灵敏度：鼠标拖动同样的距离，落点距离放大这个倍数。\n\n" +
                 "★ 只影响**初速度 / 落点距离**，不影响水平角度。\n" +
                 "   水平角始终 1:1 跟随鼠标，这样瞄准才跟手。")]
        [Min(0.1f)] public float aimSensitivity = 3f;

        [Tooltip("力度对应的落点距离（米）。鼠标离原点最近 -> 第一个值，最远 -> 第二个值。\n\n" +
                 "第一个值取 0 时，鼠标没动就等于原地轻放（落点在玩家脚下）。")]
        public Vector2 landingRange = new Vector2(0f, 18f);

        // ---------------------------------------------------------------- 石子物理

        [Header("石子物理")]
        [Tooltip("石子半径（米）")]
        public float stoneRadius = 0.08f;

        [Tooltip("重力加速度（负值 = 向下）")]
        public float gravity = -9.81f;

        [Tooltip("发射点相对**玩家胶囊中心**的高度偏移（米）。\n" +
                 "0 = 正好在胶囊体中心；负值往下、正值往上")]
        public float launchHeightOffset = 0f;

        [Tooltip("石子最长存活时间（秒）")]
        public float maxLife = 10f;

        [Tooltip("石子撞到的层")]
        public LayerMask hitMask = ~((1 << 2) | (1 << 4) | (1 << 5));

        [Tooltip("石子命中检测的分子步数，越大越不容易穿墙")]
        [Range(1, 8)] public int substeps = 4;

        // ---------------------------------------------------------------- 石子光带

        [Header("石子光带")]
        [Tooltip("光带粗细（米）")]
        public float trailWidth = 0.1f;

        [Tooltip("光带颜色")]
        public Color trailColor = new Color(0.6f, 0.85f, 1f, 1f);

        [Tooltip("初始不透明度")]
        [Range(0f, 1f)] public float trailInitialAlpha = 1f;

        [Tooltip("光带本体的淡出速度（1/秒）")]
        public float trailBodyFade = 0f;

        [Tooltip("光带波前的淡出速度（1/秒）。应当大于本体速度")]
        public float trailHeadFade = 0.5f;

        // ---------------------------------------------------------------- 撞墙声波

        [Header("撞墙产生的声波")]
        [Tooltip("石子撞墙后，是否在该处产生一次声波光带")]
        public bool emitSonarOnImpact = true;

        [Tooltip("这一次声波的数值。默认与走路声波相同")]
        public EchoPreset impactEcho = new EchoPreset
        {
            enabled = true, color = Color.white, initialAlpha = 1f,
            rayCount90 = 16, rayCount30 = 16, rayCount60 = 16,
            range = 30f, speed = 8f, trailSpacing = 0.30f,
            maxBounces = 3, bounceFalloff = 0.65f,
            lifetimeCap = 2f, tailLinger = 20f, bounceLifeLoss = 0.35f,
            diffuseCount15 = 0, diffuseCount45 = 0, diffuseCount75 = 0,
            hemisphereCount = 0, hemisphereDiffuse = false,
        };

        // ---------------------------------------------------------------- 瞄准可视化

        [Header("瞄准可视化")]

        [Tooltip("准星圆环的半径（米）")]
        public float aimRingRadius = 0.5f;

        [Tooltip("准星颜色")]
        public Color aimColor = new Color(1f, 1f, 1f, 1f);

        [Tooltip("准星圆环的粗细（0..1，环带厚度占半径的比例）。0.015 = 很细的一圈")]
        [Range(0.005f, 0.9f)] public float aimRingThickness = 0.1f;

        [Header("落点圆环")]
        [Tooltip("落点圆环的【直径】（米）")]
        [Min(0.05f)] public float landingRingDiameter = 1.5f;

        [Tooltip("落点圆环的【粗细】（米，环带的实际物理宽度）")]
        [Min(0.005f)] public float landingRingThicknessMeters = 0.1f;

        [Tooltip("落点圆环的【颜色】。Alpha 通道就是不透明度")]
        public Color landingRingColor = new Color(1f, 1f, 1f, 1f);

        [Tooltip("落点圆环离开地面的高度（米）。稍微抬一点避免和地板闪烁")]
        public float landingRingLift = 0.02f;

        /// <summary>环带厚度 ÷ 半径。网格是单位半径的，乘上去就是实际粗细。</summary>
        private float LandingRingThicknessRatio
        {
            get
            {
                float radius = Mathf.Max(0.025f, landingRingDiameter * 0.5f);
                return Mathf.Clamp(landingRingThicknessMeters / radius, 0.02f, 0.9f);
            }
        }

        [Header("瞄准镜头偏移")]
        [Tooltip("落点距离超过这个值（米）时，镜头开始朝落点方向偏移")]
        public float cameraOffsetStart = 6f;

        [Tooltip("落点距离达到这个值（米）时，偏移量拉满")]
        public float cameraOffsetFull = 18f;

        [Tooltip("镜头朝落点方向偏移的最大量（米）")]
        public float cameraOffsetMax = 12f;

        [Tooltip("轨迹预览颜色")]
        public Color trajectoryColor = new Color(1f, 1f, 1f, 0.5f);

        [Tooltip("轨迹预览最多多少个点")]
        [Range(8, 200)] public int trajectoryPoints = 90;

        [Tooltip("预估轨迹最长模拟时间（秒）。到点还没落地就按这个截断")]
        public float trajectoryMaxTime = 6f;

        // ---------------------------------------------------------------- 引用

        [Header("引用")]
        public PlayerController player;

        [Tooltip("石子光带渲染器。留空会自动创建")]
        public MonsterTrailRenderer trailRenderer;

        // ---------------------------------------------------------------- 状态

        private sealed class Stone
        {
            public Vector3 pos;
            public Vector3 vel;
            public float   birth;
            public Vector3 origin;        // 整条轨迹的发射点
            public int     trailIndex;    // 当前正在生长的光带句柄（-1 = 没有）
            public Vector3 trailAnchor;   // 当前这一段的起点
            public float   trailTravelled;// 当前这一段已经铺了多长
        }

        private readonly List<Stone> m_Stones = new List<Stone>(16);

        /// <summary>是否正在瞄准。</summary>
        public bool IsAiming { get; private set; }

        /// <summary>瞄准时镜头应朝落点方向偏移的世界向量（非瞄准时为 0）。</summary>
        public Vector3 CameraOffset { get; private set; }

        /// <summary>当前投掷水平角（世界坐标，0 = +Z）。</summary>
        public float AimYaw { get; private set; }

        /// <summary>当前力度 0..1（鼠标离原点的距离映射）。</summary>
        public float AimPower { get; private set; }

        /// <summary>当前预估落点距离（米）。</summary>
        public float AimLandingDistance { get; private set; }

        private Vector2 m_AimOrigin;      // 按下右键时的鼠标屏幕位置

        /// <summary>
        /// 虚拟光标位置 —— 光标保持锁定（不显示指针），
        /// 靠 Mouse X/Y 的增量自己积分出一个"位置"来算极坐标。
        /// </summary>
        private Vector2 m_VirtualCursor;

        /// <summary>把 GetAxisRaw 的量纲换算成"虚拟像素"。</summary>
        private const float VirtualCursorPixelsPerUnit = 20f;
        private float   m_CamForwardYaw;  // 按下右键时镜头朝向的水平方位角

        private LineRenderer m_TrajectoryLine;
        private MeshRenderer m_AimRingRenderer;
        private MeshRenderer m_LandingRingRenderer;
        private Mesh         m_RingMesh;

        // ---------------------------------------------------------------- 生命周期

        private void Awake()
        {
            Instance = this;

            if (player == null) player = FindObjectOfType<PlayerController>();

            // ★★ 地牢模式：没有石子，整套准星都别建 ★★
            //
            //   EnsureAimVisuals 会建出 AimRing / LandingRing 两个圆环，
            //   它们用的是 AimOverlay 材质（ZTest Always + Overlay 队列），
            //   一旦被启用就会穿透一切显示成一个漂浮的圆环。
            //   Update 里虽然已经 return，但 Awake 已经把物体建出来了。
            if (GameUI.IsDungeonMode) return;

            EnsureTrailRenderer();
            EnsureAimVisuals();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            if (m_RingMesh != null) Destroy(m_RingMesh);
        }

        private void EnsureTrailRenderer()
        {
            if (trailRenderer == null)
                trailRenderer = GetComponentInChildren<MonsterTrailRenderer>(true);

            if (trailRenderer == null)
            {
                var go = new GameObject("StoneTrailRenderer");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.zero;

                trailRenderer = go.AddComponent<MonsterTrailRenderer>();
            }

            trailRenderer.trailSpacing  = 0.15f;
            trailRenderer.bodyFadeSpeed = trailBodyFade;
            trailRenderer.headFadeSpeed = trailHeadFade;
            trailRenderer.maxLength     = 60f;
        }

        /// <summary>建轨迹线 + 两个圆环（准星圈 / 落点圈）。</summary>
        private void EnsureAimVisuals()
        {
            // ---- 轨迹线 ----
            var lineGo = new GameObject("StoneTrajectoryPreview");
            lineGo.transform.SetParent(transform, false);

            m_TrajectoryLine = lineGo.AddComponent<LineRenderer>();
            m_TrajectoryLine.useWorldSpace     = true;
            m_TrajectoryLine.positionCount     = 0;
            m_TrajectoryLine.loop              = false;
            m_TrajectoryLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_TrajectoryLine.receiveShadows    = false;

            // ★ 瞄准指示线必须**穿透一切**（墙体、WallOccluder、怪物实体）。
            //
            //   关键点：本项目是 URP。内置管线的 "Sprites/Default" 在 URP 下
            //   并不会真正遵守 _ZTest 这类属性，所以之前会被遮挡。
            //
            //   改用 URP 的 Unlit shader，并：
            //     · ZTest  = Always  -> 不做深度比较，任何物体都挡不住
            //     · ZWrite = Off     -> 自己不写深度，不影响别人
            //     · 渲染队列 = Overlay(4000) -> 排在**所有**透明物体之后，
            //       保证不被后画的水面/光带等叠上去盖住
            Material lineMat = CreateOverlayMaterial(trajectoryColor);
            m_TrajectoryLine.material = lineMat;

            m_TrajectoryLine.enabled = false;

            // ---- 圆环网格 ----
            //   准星圈和落点圈各用一份网格：落点圈要能单独调粗细，
            //   所以不能共用同一份（网格里的内外半径是烘死在顶点里的）。
            m_RingMesh = BuildRingMesh(48, aimRingThickness);

            var landingMesh = BuildRingMesh(48, LandingRingThicknessRatio);

            m_AimRingRenderer     = CreateRingRenderer("AimRing", m_RingMesh, aimColor);
            m_LandingRingRenderer = CreateRingRenderer("LandingRing", landingMesh, landingRingColor);
        }

        private MeshRenderer CreateRingRenderer(string name, Mesh mesh, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var rend = go.AddComponent<MeshRenderer>();
            rend.shadowCastingMode          = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows             = false;
            rend.lightProbeUsage            = UnityEngine.Rendering.LightProbeUsage.Off;
            rend.reflectionProbeUsage       = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            rend.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            Material mat = CreateOverlayMaterial(color);
            rend.material = mat;
            rend.enabled  = false;

            return rend;
        }

        /// <summary>
        /// 创建一个"永远画在最上层、不被任何物体遮挡"的材质。
        ///
        /// 实现见 <see cref="EchoRuntimeMaterials.AimOverlay"/>：
        /// 用专用的 EchoPuzzle/AimOverlay shader，把
        /// ZTest Always / ZWrite Off / Queue=Overlay 硬编码在 Pass 里。
        /// </summary>
        private static Material CreateOverlayMaterial(Color color)
            => EchoRuntimeMaterials.AimOverlay(color);

        /// <summary>生成一个躺在 XZ 平面上的圆环（细环带，不是实心圆）。</summary>
        private static Mesh BuildRingMesh(int segments, float thickness)
        {
            var verts = new List<Vector3>();
            var tris  = new List<int>();

            thickness = Mathf.Clamp(thickness, 0.1f, 1f);

            // 单位半径的细环：外径 1，内径 = 1 - thickness
            float outer = 3.0f;
            float inner = 3f - thickness;

            for (int i = 0; i <= segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);

                verts.Add(new Vector3(c * inner, 0f, s * inner));
                verts.Add(new Vector3(c * outer, 0f, s * outer));
            }

            for (int i = 0; i < segments; i++)
            {
                int b = i * 2;
                tris.Add(b);     tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b + 2); tris.Add(b + 1); tris.Add(b + 3);
            }

            var mesh = new Mesh { name = "AimRing" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------------------------------------------------------- 主循环

        private void Update()
        {
            // ★★ 地牢模式（DungeonMode）禁用投石 ★★
            //
            //   地牢模式里玩家**不再能扔石头**（武器是枪）。
            //   这里整个跳过：不响应瞄准、不生成石子。
            //   已经在飞的石子仍会在 UpdateStones 里飞完 ——
            //   但地牢模式下从没生成过，所以实际上不会有。
            //
            //   ★ 交接说明：地牢模式的枪械请新建
            //     Scripts/Dungeon/DungeonGun.cs，不要改这个脚本。
            if (GameUI.IsDungeonMode) return;

            bool canAct = GameUI.Current == GameState.Playing;

            UpdateAiming(canAct);
            UpdateStones();
            UpdateCameraOffset();

            if (canAct && IsAiming && Input.GetKeyDown(throwKey)) Throw();
        }

        /// <summary>
        /// 瞄准时计算镜头朝落点方向的偏移量：
        /// 落点距离在 [cameraOffsetStart, cameraOffsetFull] 之间线性映射到 [0, cameraOffsetMax]。
        /// 实际应用在 PlayerController.LateUpdate（相机位置定稿之后）。
        /// </summary>
        private void UpdateCameraOffset()
        {
            if (!IsAiming || player == null)
            {
                CameraOffset = Vector3.zero;
                return;
            }

            float t   = Mathf.InverseLerp(cameraOffsetStart, cameraOffsetFull, AimLandingDistance);
            float off = cameraOffsetMax * Mathf.Clamp01(t);
            CameraOffset = AimDirection * off;
        }

        /// <summary>
        /// 瞄准：以按下右键时的鼠标位置为原点做极坐标。
        ///
        ///   角度定义（相对**镜头朝向前方**）：
        ///     0°   = 屏幕右方（+X）
        ///     90°  = 屏幕前方（-Y，鼠标往上）
        ///     180° = 屏幕左方（-X）
        ///     270° = 屏幕后方（+Y，鼠标往下）
        ///
        ///   也就是说：
        ///     · 鼠标往**现实正前方**（屏幕上方）移动 -> 投掷角固定 90°（正前方），初速随距离变大
        ///     · 鼠标绕原点旋转 -> 投掷角变化，而**半径不变则初速不变**
        ///
        ///   所以"角度"和"力度"分别只由鼠标位移的**方向**和**长度**决定，互不干扰。
        /// </summary>
        private void UpdateAiming(bool canAct)
        {
            if (!canAct)
            {
                IsAiming = false;
                UpdateAimVisuals();
                return;
            }

            // ★ 互斥：按住右键时强制退出匕首瞄准。
            //   两套瞄准不能同时存在（都占左键、都改光标）。
            if (Input.GetKey(aimKey))
            {
                var dagger = DaggerSystem.Instance;
                if (dagger != null && dagger.IsAiming) dagger.CancelAim();
            }

            // ★ 受惊死亡倒计时期间**不能瞄准**
            bool wantAim = Input.GetKey(aimKey) && !GameUI.FrightenedDeathActive;

            if (wantAim && !IsAiming)
            {
                // ★ 不解锁光标 —— 屏幕上不出现指针。
                //   改用"虚拟光标"：把 Mouse X/Y 的增量积分成一个位置，
                //   极坐标就用它算，效果和真实光标完全一致。

                // 进入瞄准：记录鼠标原点
                IsAiming    = true;
                m_VirtualCursor = Vector2.zero;
                m_AimOrigin     = Vector2.zero;

                // 记录此刻**镜头前方**的世界方位角 —— 鼠标"朝上"对应的世界方向
                m_CamForwardYaw = CameraYaw();

                // 刚按下的瞬间鼠标还没动：力度 0 -> 落点在脚下。
                // 角度此时无所谓（距离为 0），但先给个相机前方，免得显示异常。
                AimYaw   = m_CamForwardYaw;
                AimPower = 0f;
                AimLandingDistance = landingRange.x;
            }
            else if (!wantAim)
            {
                IsAiming = false;
                UpdateAimVisuals();

                // 光标本来就没动过，这里不需要恢复什么。
                return;
            }

            // ---- 累加虚拟光标 ----
            float mx = Input.GetAxisRaw("Mouse X");
            float my = Input.GetAxisRaw("Mouse Y");
            m_VirtualCursor += new Vector2(mx, my) * VirtualCursorPixelsPerUnit;

            // ---- 当前虚拟光标相对原点 ----
            Vector2 d = m_VirtualCursor - m_AimOrigin;

            // 鼠标没动（或只差一两个像素）：力度为 0，落点就在脚下。
            // 角度在这时没有实际意义（atan2(0,0)、距离 0），保持当前值即可。
            if (d.sqrMagnitude < DontMoveEps * DontMoveEps)
            {
                AimPower = 0f;
                AimLandingDistance = landingRange.x;
                UpdateAimVisuals();
                return;
            }

            // ---- 角度：**绝对式**，不乘任何灵敏度 ----
            //
            //     · 鼠标朝上移动一点     -> 落点立刻跳到**相机朝向的正前方**
            //     · 鼠标朝下移动一点     -> 落点立刻跳到**相机朝向的正后方**
            //     · 鼠标往哪转，落点就转到哪，且**转速和鼠标完全一致**
            //
            //   （鼠标完全没动时力度为 0，落点在脚下，方向无意义 —— 见上面的提前返回）
            //
            //   ★ 左右要取反。
            //
            //     screenAngleDeg = atan2(d.y, d.x) 是数学上的逆时针为正：
            //       右 = 0°、上 = 90°、左 = 180°（逆时针增大）
            //     AimYaw 是世界方位角（atan2(x, z)）：
            //       右 = +90°、前 = 0°、左 = -90°（顺时针增大）
            //     两者转向相反，所以用 (90° - screenAngleDeg) 而不是 (screenAngleDeg - 90°)。
            //
            //   验证（screenAngle -> 世界偏移 -> 结果）：
            //     上  90°  ->  90-90 = 0    -> 相机正前方  ✅
            //     右   0°  ->  90-0  = +90  -> 相机正右方  ✅
            //     下 270°  ->  90-270 = -180-> 相机正后方  ✅
            //     左 180°  ->  90-180 = -90 -> 相机正左方  ✅
            float screenAngleDeg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            AimYaw = Mathf.Repeat(m_CamForwardYaw + (90f - screenAngleDeg) + 180f, 360f) - 180f;

            // ---- 力度 ----
            //   只由鼠标离原点的**距离**决定。绕着原点转圈时初速不变。
            //
            //   ★ aimSensitivity 只作用在这里（只影响初速度 / 落点距离），
            //     不影响水平角度 —— 角度保持 1:1 跟随鼠标，手感才准。
            AimPower = Mathf.Clamp01(d.magnitude / Mathf.Max(1f, dragFullDistance) * aimSensitivity);

            AimLandingDistance = Mathf.Lerp(landingRange.x, landingRange.y, AimPower);

            UpdateAimVisuals();
        }

        /// <summary>鼠标位移小于多少像素时视为"还没动"。</summary>
        private const float DontMoveEps = 2f;

        /// <summary>当前镜头朝向的水平方位角（0 = +Z，90 = +X）。</summary>
        private float CameraYaw()
        {
            Camera cam = Camera.main;
            if (cam == null && player != null) cam = player.playerCamera;

            Vector3 fwd;

            if (cam != null)
            {
                fwd = cam.transform.forward;
            }
            else if (player != null)
            {
                fwd = player.transform.forward;
            }
            else
            {
                return 0f;
            }

            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();

            return Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 算出落点距离所需的初速度（纯水平抛出、速度不衰减）。
        ///
        /// 石子从**胶囊中心**出发，落到玩家脚底平面所需高度 = 出发点高度 - 脚底高度。
        ///   h = ½·g·t²  ->  t = sqrt(2h / |g|)
        ///   d = v·t     ->  v = d / t
        /// </summary>
        public float SpeedForLandingDistance(float distance)
        {
            // ★ 距离为 0 时初速就是 0 —— 石子直接落到脚下。
            //   不要在这里套 Mathf.Max(下限)，否则"力度为 0"会被硬抬成
            //   一个最小速度，落点就离开脚下了。
            if (distance <= 1e-4f) return 0f;

            float h = Mathf.Max(0.05f, LaunchDropHeight());
            float g = Mathf.Max(0.1f, Mathf.Abs(gravity));

            float t = Mathf.Sqrt(2f * h / g);
            return distance / t;
        }

        /// <summary>石子从出发点到玩家脚底平面的垂直落差（米）。</summary>
        private float LaunchDropHeight()
        {
            if (player == null) return 1f;

            float launchY = player.transform.position.y;

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) launchY += cc.center.y;      // 胶囊中心

            launchY += launchHeightOffset;

            return launchY - player.FootPosition.y;
        }

        /// <summary>当前瞄准的水平方向。</summary>
        public Vector3 AimDirection
        {
            get
            {
                float r = AimYaw * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
            }
        }

        /// <summary>
        /// 石子发射点：**玩家胶囊体的中心**。
        ///
        /// 胶囊中心 = transform.position + controller.center
        /// （本项目里 center.y = 0.9、height = 1.8，所以正好是胶囊的几何中心：
        ///   水平面上是圆心的位置，竖直方向是腰的位置）。
        ///
        /// 再加一个很小的前向偏移，避免一出手就撞到玩家自己的胶囊体。
        /// </summary>
        private Vector3 LaunchOrigin()
        {
            return LaunchOriginFor(SpeedForLandingDistance(AimLandingDistance));
        }

        /// <summary>
        /// 给定初速度时的发射点。
        ///
        /// ★ 前向偏移只在**真的会飞出去**时才加。
        ///
        ///   偏移的目的仅是"别一出手就撞到玩家自己的胶囊"（玩家半径 0.34m）。
        ///   但速度接近 0 时石子是垂直落下的，根本不会横向移动，
        ///   这时如果还挪 0.23m，落点就会偏离脚下 —— 看起来像"落在镜头前方一点点"。
        ///
        ///   所以：偏移量与速度挂钩，速度为 0 时偏移也是 0，
        ///   落点严格等于玩家的水平中心。
        /// </summary>
        private Vector3 LaunchOriginFor(float speed)
        {
            if (player == null) return transform.position;

            Vector3 center = player.transform.position;

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) center += cc.center;      // <- 胶囊中心

            center += Vector3.up * launchHeightOffset;

            // 速度越大越需要让开自己；速度 ~0 时完全不偏
            float fullOffset = stoneRadius + 0.15f;                    // 0.23m
            float t          = Mathf.Clamp01(speed / SafeLaunchSpeed);  // 0..1
            float offset     = fullOffset * t;

            if (offset <= 1e-4f) return center;

            return center + AimDirection * offset;
        }

        /// <summary>达到这个初速度时前向偏移取满（米/秒）。</summary>
        private const float SafeLaunchSpeed = 3f;

        /// <summary>
        /// 瞄准圈的中心：玩家胶囊体的中心（+ 高度偏移），**不带**前向偏移。
        ///
        /// 瞄准圈表达的是"从玩家身上哪个点扔出去"，所以圈心就是玩家自身，
        /// 而不是发射点 LaunchOrigin（那个为了不撞自己往投掷方向挪了 0.23m）。
        /// </summary>
        private Vector3 AimRingCenter()
        {
            if (player == null) return transform.position;

            Vector3 center = player.transform.position;

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) center += cc.center;      // <- 胶囊中心

            center += Vector3.up * launchHeightOffset;
            return center;
        }

        // ---------------------------------------------------------------- 可视化

        private void UpdateAimVisuals()
        {
            if (m_TrajectoryLine == null) return;

            if (!IsAiming || player == null)
            {
                m_TrajectoryLine.enabled = false;
                if (m_AimRingRenderer     != null) m_AimRingRenderer.enabled     = false;
                if (m_LandingRingRenderer != null) m_LandingRingRenderer.enabled = false;
                return;
            }

            float speed  = SpeedForLandingDistance(AimLandingDistance);
            Vector3 dir  = AimDirection;
            Vector3 orig = LaunchOriginFor(speed);     // 用同一个 speed，和 Throw 一致
            Vector3 vel  = dir * speed;

            // 玩家脚底的 Y：轨迹线落到这个高度就终止
            float floorY = player.FootPosition.y;

            // ---- 准星圈：以玩家为圆心，躺在出发点高度的水平面上 ----
            if (m_AimRingRenderer != null)
            {
                m_AimRingRenderer.enabled = true;
                m_AimRingRenderer.transform.position = AimRingCenter();
                m_AimRingRenderer.transform.rotation = Quaternion.identity;
                m_AimRingRenderer.transform.localScale =
                    new Vector3(aimRingRadius, 1f, aimRingRadius);
            }

            // ---- 轨迹线 ----
            int   n  = Mathf.Clamp(trajectoryPoints, 8, 200);
            float dt = Mathf.Max(0.005f, trajectoryMaxTime / (n - 1));

            // 先粗略算出落到 floorY 大约要多久，用它来分配采样点
            float tFall = TimeToFallTo(orig.y, vel.y, floorY);
            if (tFall < dt) tFall = dt;
            dt = Mathf.Max(0.005f, tFall / (n - 1));

            List<Vector3> pts = m_PointBuffer;
            pts.Clear();

            Vector3 last = orig;

            for (int i = 0; i < n; i++)
            {
                float t = i * dt;
                Vector3 p = orig + vel * t + Vector3.up * (0.5f * gravity * t * t);

                // ★ 落到玩家脚底平面就终止
                if (p.y <= floorY && i > 0)
                {
                    // 在 last 与 p 之间线性插值出精确的落点
                    float a = last.y, b = p.y;
                    float k = Mathf.Approximately(a - b, 0f) ? 1f : (a - floorY) / (a - b);
                    last = Vector3.Lerp(last, p, Mathf.Clamp01(k));
                    pts.Add(last);
                    break;
                }

                pts.Add(p);
                last = p;
            }

            m_TrajectoryLine.enabled = true;
            m_TrajectoryLine.positionCount = pts.Count;

            for (int i = 0; i < pts.Count; i++)
            {
                m_TrajectoryLine.SetPosition(i, pts[i]);
            }

            m_TrajectoryLine.startColor = trajectoryColor;
            m_TrajectoryLine.endColor   = new Color(trajectoryColor.r, trajectoryColor.g,
                                                    trajectoryColor.b, trajectoryColor.a * 0.25f);
            m_TrajectoryLine.startWidth = 0.035f;
            m_TrajectoryLine.endWidth   = 0.02f;

            // ---- 落点圈：躺在落点的水平面上 ----
            if (m_LandingRingRenderer != null && pts.Count > 0)
            {
                Vector3 land = pts[pts.Count - 1];
                m_LandingRingRenderer.enabled = true;
                m_LandingRingRenderer.transform.position = land + Vector3.up * landingRingLift;
                m_LandingRingRenderer.transform.rotation = Quaternion.identity;

                // 直径 -> 缩放（网格是单位半径 = 直径 2，所以缩放 = 直径 / 2）
                float r = Mathf.Max(0.025f, landingRingDiameter * 0.5f);
                m_LandingRingRenderer.transform.localScale = new Vector3(r, 1f, r);
            }
        }

        private readonly List<Vector3> m_PointBuffer = new List<Vector3>(200);

        /// <summary>从高度 y0、竖直速度 vy 出发，落到 targetY 需要多久（纯重力）。</summary>
        private float TimeToFallTo(float y0, float vy, float targetY)
        {
            float g = Mathf.Max(0.1f, Mathf.Abs(gravity));
            float dy = y0 - targetY;

            // y(t) = y0 + vy·t - ½·g·t²  求 y(t) = targetY
            // ½·g·t² - vy·t - dy = 0
            float a = 0.5f * g;
            float b = -vy;
            float c = -dy;

            float disc = b * b - 4f * a * c;
            if (disc < 0f) return trajectoryMaxTime;

            float t1 = (-b + Mathf.Sqrt(disc)) / (2f * a);
            float t2 = (-b - Mathf.Sqrt(disc)) / (2f * a);

            float t = Mathf.Max(t1, t2);
            if (t < 0f) return trajectoryMaxTime;

            return Mathf.Min(t, trajectoryMaxTime);
        }

        // ---------------------------------------------------------------- 投掷

        /// <summary>朝当前瞄准方向投出一颗石子。</summary>
        public void Throw()
        {
            if (player == null) return;

            // ★ 消耗一个石头。
            //   Consume() 在 Gravel 区域内**不扣**（返回 true 但数量不变）；
            //   石头为 0 且不在 Gravel 内时返回 false -> 投不出去。
            if (StoneSystem.Instance != null && !StoneSystem.Instance.Consume())
                return;

            Vector3 dir   = AimDirection;
            float   speed = SpeedForLandingDistance(AimLandingDistance);

            // ★ 发射点必须用**同一帧的同一个速度**来算，
            //   这样它和轨迹预览、落点圈完全一致。
            Vector3 orig  = LaunchOriginFor(speed);

            // 俯仰角固定为水平：dir.y = 0
            Vector3 vel   = dir * speed;

            m_Stones.Add(new Stone
            {
                pos            = orig,
                vel            = vel,
                birth          = Time.timeSinceLevelLoad,
                origin         = orig,
                trailIndex     = -1,        // 第一段在第一帧 UpdateStoneTrail 里开
                trailAnchor    = orig,
                trailTravelled = 0f,
            });
        }

        // ---------------------------------------------------------------- 石子推进

        private void UpdateStones()
        {
            if (m_Stones.Count == 0) return;

            float dt = Time.deltaTime;

            for (int i = m_Stones.Count - 1; i >= 0; i--)
            {
                Stone s = m_Stones[i];

                if (Time.timeSinceLevelLoad - s.birth > maxLife)
                {
                    StopStoneTrail(s);
                    m_Stones.RemoveAt(i);
                    continue;
                }

                int   steps  = Mathf.Clamp(substeps, 1, 8);
                float stepDt = dt / steps;

                bool    hit      = false;
                Vector3 hitPoint = default;

                for (int k = 0; k < steps; k++)
                {
                    Vector3 prev = s.pos;

                    // 半隐式欧拉：先更新速度，再更新位置
                    s.vel += Vector3.up * (gravity * stepDt);
                    s.pos += s.vel * stepDt;

                    Vector3 delta = s.pos - prev;
                    float   len   = delta.magnitude;
                    if (len <= 1e-5f) continue;

                    if (Physics.SphereCast(prev, stoneRadius, delta / len, out RaycastHit hitInfo,
                                           len, hitMask, QueryTriggerInteraction.Ignore))
                    {
                        hit      = true;
                        hitPoint = prev + delta / len * hitInfo.distance;
                        break;
                    }
                }

                // ---- 石子光带：波前跟着石子走 ----
                //
                // ★ 这里有个容易写错的地方：光带渲染器是按"起点 + 方向 + 长度"
                //   画一条**直线**的，而石子走的是**抛物线**。
                //   如果只 SetLength，光带会是一条从发射点指向石子的直线，
                //   完全看不出弧线。
                //
                //   所以这里把轨迹**分段**：每飞一小段就停住那一段（StopHead），
                //   再以石子当前位置为新起点开一段新的。于是整条轨迹由若干段
                //   短直线拼成，视觉上就是连续的抛物线。
                UpdateStoneTrail(s, hit, hitPoint);

                if (hit)
                {
                    if (emitSonarOnImpact) EmitImpactSonar(hitPoint);
                    m_Stones.RemoveAt(i);
                }
                else if (s.pos.y <= (player != null ? player.FootPosition.y - 0.5f : -100f))
                {
                    // 落到地下（没有碰撞体的情况）-> 收尾
                    m_Stones.RemoveAt(i);
                }
            }
        }

        /// <summary>把石子这一帧的位置推给折线光带。</summary>
        [Header("石子光带分段")]
        [Tooltip("石子光带每推进多远加一个路径点。越小弧越顺滑，顶点越多")]
        public float trailPointStep = 0.15f;

        /// <summary>
        /// 石子光带 = 一条**折线**光带，沿石子真实抛物线追加路径点。
        ///
        /// ★ 为什么不用"多条直线拼接"：
        ///   那样每段都是独立的光带，段与段之间有缝隙、亮度和宽度也不连续，
        ///   看起来是一节一节的直线，不是弧。
        ///
        ///   现在给 MonsterTrailRenderer 加了 path 支持：一条光带内部存一串路径点，
        ///   渲染时沿路径逐段铺四边形、每段用自己的切向算基向量，
        ///   所以整条是**连贯的圆弧**。
        /// </summary>
        private void UpdateStoneTrail(Stone s, bool hit, Vector3 hitPoint)
        {
            if (trailRenderer == null) return;

            if (hit)
            {
                if (s.trailIndex >= 0) trailRenderer.StopPath(s.trailIndex, hitPoint);
                s.trailIndex = -1;
                return;
            }

            // 还没有光带 -> 开一条折线光带
            if (s.trailIndex < 0)
            {
                s.trailIndex = trailRenderer.AddPathTrail(
                    s.trailAnchor, trailWidth, trailColor, trailInitialAlpha);
            }

            // 沿抛物线追加点
            if (s.trailIndex >= 0)
            {
                trailRenderer.PushPathPoint(s.trailIndex, s.pos, trailPointStep);
            }
        }

        private void StopStoneTrail(Stone s)
        {
            if (s.trailIndex < 0 || trailRenderer == null) return;

            trailRenderer.StopPath(s.trailIndex, s.pos);
            s.trailIndex = -1;
        }

        /// <summary>在石子撞墙处放一次声波（数值 = 走路声波）。</summary>
        private void EmitImpactSonar(Vector3 position)
        {
            var sonar = FindObjectOfType<SonarSystem>();
            if (sonar == null) return;

            EmitParams p = impactEcho.ToEmitParams();
            p.referenceDir = Vector3.forward;

            sonar.Emit(position, p);

            AudioSystem.Play(AudioSystem.Cue.Walk);
        }

        // ---------------------------------------------------------------- 清空

        public void ClearStones()
        {
            m_Stones.Clear();
            IsAiming = false;
            if (trailRenderer != null) trailRenderer.ClearTrails();
        }

        /// <summary>
        /// 只退出瞄准，不动已经飞出去的石子。
        /// 匕首进入瞄准时会调它，避免两套瞄准同时占着鼠标左键。
        /// </summary>
        public void CancelAim()
        {
            IsAiming = false;
        }
    }
}
