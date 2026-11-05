using UnityEngine;

namespace EchoPuzzle.Dungeon
{
    /// <summary>
    /// 【地牢模式】玩家的武器：瞄准 + 射击 + 蓝耗。
    ///
    /// ── 操作（照文档第 19 段）────────────────────────────────
    ///   · 按住右键（aimKey）进入瞄准：脚下出现圆环，环上浮起一把武器
    ///   · 瞄准时**鼠标不再转镜头**，而是绕玩家旋转武器（和匕首/投石同一套极坐标）
    ///   · 按住 / 单击左键（fireKey）发射，受 fireInterval 限制
    ///     —— 单击再快也不会超过这个射速
    ///   · 松开右键退出瞄准
    ///
    /// ── 瞄准数学（照抄 DaggerSystem.HandleAimMouse）──────────
    ///   · 按下右键那一刻：记下相机方位角 m_AimBaseYaw，虚拟光标归零
    ///   · 之后每帧：m_VirtualCursor += (Mouse X, Mouse Y) × 20
    ///   · θ  = atan2(dy, dx)            （右=0°、上=90°、左=180°、下=270°）
    ///   · 方位角 = m_AimBaseYaw + (90° - θ)
    ///   · **只取 θ，无视半径 r** —— 和匕首完全一致（投石那边才用 r 当力度）
    ///
    ///   ★ 因此武器始终在水平面上绕玩家转，俯仰恒为 0。
    ///     这和匕首"永远在水平面上"是同一个设定；怪物受击圆柱高 4 米，
    ///     水平弹道从枪口高度打过去依然能命中。
    ///
    /// ── 镜头冻结 ────────────────────────────────────────────
    ///   需要 <see cref="PlayerController"/> 在 HandleLook 里放行，
    ///   写法和它已有的 StoneThrowSystem / DaggerSystem 判断同款（3 行）。
    ///
    /// ── 和探秘模式的关系 ────────────────────────────────────
    ///   ★ 只有 <see cref="DungeonWeaponBootstrap"/> 在地牢场景里会创建它；
    ///     PlayerController 里那句判断靠 Instance 判空，探秘模式下永远为 null，
    ///     所以对探秘模式零影响。
    /// </summary>
    [AddComponentMenu("Echo Puzzle/Dungeon/Dungeon Weapon")]
    [DefaultExecutionOrder(-50)]
    public sealed class DungeonWeapon : MonoBehaviour
    {
        public static DungeonWeapon Instance { get; private set; }

        // ------------------------------------------------------------------ 输入

        [Header("输入")]
        [Tooltip("按住进入瞄准（松开退出）")]
        public KeyCode aimKey = KeyCode.Mouse1;

        [Tooltip("瞄准时按住 / 单击都会发射")]
        public KeyCode fireKey = KeyCode.Mouse0;

        // ------------------------------------------------------------------ 瞄准

        [Header("瞄准")]
        [Tooltip("鼠标位移换算成虚拟像素的比例。只影响「要拖多远才转到位」，不影响 1:1 跟随")]
        [Min(1f)] public float virtualCursorPixelsPerUnit = 20f;

        [Tooltip("鼠标位移小于多少虚拟像素时视为「还没离开原点」，武器保持朝正前方")]
        [Min(0f)] public float dontMoveEps = 2f;

        // ------------------------------------------------------------------ 射击

        [Header("射击")]
        [Tooltip("两发之间的最小间隔（秒）。按住和连点都受它限制。\n\n" +
                 "0.18 ≈ 每秒 5.5 发（偏连发枪，太快）\n" +
                 "0.25 ≈ 每秒 4 发（当前）\n" +
                 "0.32 ≈ 每秒 3 发（半自动手感）\n" +
                 "0.45 ≈ 每秒 2.2 发（重枪）\n\n" +
                 "★ 这是**基础值**，buff 的 fireRateMul 会再除它。")]
        [Min(0.02f)] public float fireInterval = 0.25f;

        // ── 蓝量经济（这几个数要一起看，单独调一个会失衡）──────────
        //
        //   怪物 12 血。当前一组默认值的账：
        //     3 发打死（damage 4）  -> 花 3 × 2 = 6 蓝
        //     击杀回 12 蓝          -> 一只怪净赚 +6
        //
        //   所以：
        //     · 满蓝 100 = 连开 50 发 = 约 16 只怪，**不需要中途回蓝**
        //     · 每只净赚 6，一场 10 只的怪潮净赚 60，蓝越打越多
        //     · 打空 3 发 = 亏掉一只怪的收益 -> 打得准仍然有意义
        //     · 另外 DungeonMana 还有 1.5/秒 的被动回复兜底（见那边注释）
        //
        //   ★ 调参口诀：想更宽松 -> 降 manaCost；想更硬核 -> 升 manaCost
        //     但 killManaRestore 要**明显大于**「杀一只所需的蓝」，
        //     否则杀得越多亏得越多（上一版 damage=1/蓝耗 5/回 15 就是这个错）。

        [Tooltip("单发蓝耗")]
        [Min(0)] public int manaCost = 2;

        [Tooltip("单发伤害。怪物血量见 PuzzleMonster.dungeonMaxHealth（默认 12）")]
        [Min(1)] public int damage = 4;

        [Tooltip("打死一只怪回复的蓝。应明显大于「杀一只所需的蓝」，否则越打越亏")]
        [Min(0)] public int killManaRestore = 12;

        // ------------------------------------------------------------------ 子弹

        [Header("子弹")]
        [Tooltip("子弹飞行速度（米/秒）")]
        [Min(1f)] public float bulletSpeed = 35f;

        [Tooltip("最大射程（米）")]
        [Min(1f)] public float bulletRange = 40f;

        [Tooltip("拖尾宽度（米）")]
        [Min(0.01f)] public float bulletWidth = 0.2f;

        [Tooltip("子弹光带颜色")]
        public Color bulletColor = new Color(0.35f, 0.62f, 1f, 1f);

        [Tooltip("拖尾长度（米）。只画波前身后这么长的一段。\n\n" +
                 "★ 太长（比如跟射程一样）时，每秒好几发会首尾相接，\n" +
                 "看起来像一根连续的光柱，会让人误以为“发射没有间隔”。")]
        [Min(0.2f)] public float bulletTrailLength = 4f;

        [Tooltip("命中后拖尾淡出所需时间（秒）")]
        [Min(0.05f)] public float bulletFadeTime = 0.5f;

        [Header("怪物受击圆柱（实体只有 0.5，这里放大到能打到）")]
        [Tooltip("水平半径（米）")]
        [Min(0.1f)] public float monsterHurtRadius = 2f;

        [Tooltip("竖直总高（米）")]
        [Min(0.1f)] public float monsterHurtHeight = 4f;

        // ------------------------------------------------------------------ 瞄准表现

        [Header("瞄准表现")]
        [Tooltip("脚下圆环半径（米）")]
        [Min(0.2f)] public float ringRadius = 1.2f;

        public Color ringColor = new Color(1f, 1f, 1f, 0.55f);

        [Tooltip("圆环离地高度（米），抬高一点避免和地面 z-fighting")]
        public float ringGroundOffset = 0.03f;

        [Tooltip("武器绕玩家旋转的半径（米）。建议略小于 ringRadius")]
        [Min(0.1f)] public float weaponOrbitRadius = 0.9f;

        [Tooltip("武器离地高度（米），也就是枪口高度")]
        public float weaponHeight = 1.15f;

        [Tooltip("枪口相对武器再往前伸出的距离（米）")]
        public float muzzleForward = 0.28f;

        public Color weaponColor = new Color(1f, 1f, 1f, 0.9f);

        [Tooltip("瞄准辅助线长度（米）。瞄准时镜头被冻结、屏幕上没有准星，靠它指示子弹方向")]
        [Min(1f)] public float guideLength = 8f;

        [Tooltip("瞄准辅助线宽度（米）")]
        [Min(0.002f)] public float guideWidth = 0.015f;

        public Color guideColor = new Color(1f, 1f, 1f, 0.35f);

        // ------------------------------------------------------------------ 运行时

        /// <summary>是否正在瞄准。</summary>
        public bool IsAiming { get; private set; }

        /// <summary>当前瞄准的世界方位角（度）。0 = +Z，90 = +X。</summary>
        public float AimYaw { get; private set; }

        /// <summary>俯仰恒为 0（和匕首一致，武器只在水平面绕玩家转）。</summary>
        public float AimPitch => 0f;

        /// <summary>最近一次成功发射的时间（Time.timeSinceLevelLoad）。</summary>
        public float LastFireTime { get; private set; } = -999f;

        /// <summary>累计发射数（调试 / buff 统计用）。</summary>
        public int ShotsFired { get; private set; }

        // ------------------------------------------------------------------ 当前有效数值（含 buff）

        // ★ 这里是「基础值 × buff 乘区」的唯一出处：
        //   开火、HUD 显示、以后做数值面板都读这几个属性，
        //   避免同一套公式在三个地方各写一遍然后慢慢写歪。

        private static float Mul(System.Func<DungeonBuffState, float> pick)
        {
            DungeonBuffState b = DungeonBuffState.Instance;
            return b != null ? pick(b) : 1f;
        }

        /// <summary>含 buff 后的单发蓝耗。</summary>
        public int EffectiveManaCost
            => Mathf.Max(0, Mathf.RoundToInt(manaCost * Mul(s => s.ManaCostMul)));

        /// <summary>含 buff 后的单发伤害。</summary>
        public int EffectiveDamage
            => Mathf.Max(1, Mathf.RoundToInt(damage * Mul(s => s.DamageMul)));

        /// <summary>含 buff 后的击杀回蓝。</summary>
        public int EffectiveKillMana
            => Mathf.Max(0, Mathf.RoundToInt(killManaRestore * Mul(s => s.KillManaMul)));

        /// <summary>含 buff 后的射击间隔（秒）。</summary>
        public float EffectiveFireInterval
            => Mathf.Max(0.02f, fireInterval / Mathf.Max(0.1f, Mul(s => s.FireRateMul)));

        /// <summary>含 buff 后的每秒射速（发/秒），给 HUD 显示用。</summary>
        public float EffectiveShotsPerSecond => 1f / Mathf.Max(0.02f, EffectiveFireInterval);

        /// <summary>含 buff 后的子弹速度。</summary>
        public float EffectiveBulletSpeed
            => bulletSpeed * Mul(s => s.BulletSpeedMul);

        /// <summary>含 buff 后的拖尾宽度。</summary>
        public float EffectiveBulletWidth
            => bulletWidth * Mul(s => s.BulletWidthMul);

        private PlayerController m_Player;

        private Transform m_RingRoot;
        private Transform m_WeaponRoot;
        private Mesh      m_RingMesh;
        private LineRenderer m_GuideLine;

        private Material m_RingMat;
        private Material m_WeaponMat;
        private Material m_BulletMat;
        private Material m_GuideMat;

        private float   m_NextFireTime;

        /// <summary>进入瞄准那一刻的相机方位角。</summary>
        private float m_AimBaseYaw;

        /// <summary>极坐标原点（虚拟光标按下的位置，通常是 0,0）。</summary>
        private Vector2 m_AimOrigin;

        /// <summary>按 Mouse X/Y 积分出来的虚拟光标位置。</summary>
        private Vector2 m_VirtualCursor;

        // ------------------------------------------------------------------ 生命周期

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            m_RingMat   = EchoRuntimeMaterials.AimOverlay(ringColor);
            m_WeaponMat = EchoRuntimeMaterials.AimOverlay(weaponColor);
            m_BulletMat = EchoRuntimeMaterials.AimOverlay(bulletColor);
            m_GuideMat  = EchoRuntimeMaterials.AimOverlay(guideColor);

            BuildRing();
            BuildWeaponModel();
            BuildGuide();

            SetAimVisuals(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            if (m_RingMesh != null) Destroy(m_RingMesh);

            if (m_RingRoot   != null) Destroy(m_RingRoot.gameObject);
            if (m_WeaponRoot != null) Destroy(m_WeaponRoot.gameObject);
            if (m_GuideLine  != null) Destroy(m_GuideLine.gameObject);

            if (m_RingMat   != null) Destroy(m_RingMat);
            if (m_WeaponMat != null) Destroy(m_WeaponMat);
            if (m_BulletMat != null) Destroy(m_BulletMat);
            if (m_GuideMat  != null) Destroy(m_GuideMat);
        }

        // ------------------------------------------------------------------ 主循环

        private void Update()
        {
            // 只让单例驱动：万一场景里混进了第二个武器组件，不会变成双倍射速
            if (Instance != this) return;

            if (!GameUI.IsDungeonMode) { LeaveAim(); return; }

            // 强化三选一界面打开时世界是冻结的（timeScale = 0），武器也不该响应
            if (DungeonBuffSelectUI.Instance != null && DungeonBuffSelectUI.Instance.IsOpen)
            {
                LeaveAim();
                return;
            }

            if (m_Player == null) m_Player = FindObjectOfType<PlayerController>();
            if (m_Player == null) { LeaveAim(); return; }

            if (!GameUI.PlayerControllable || !GameUI.CameraInputActive)
            {
                LeaveAim();
                return;
            }

            bool wantAim = Input.GetKey(aimKey);

            if (wantAim && !IsAiming) EnterAim();
            else if (!wantAim && IsAiming) LeaveAim();

            if (!IsAiming) return;

            HandleAimMouse();
            UpdateAimTransform();

            // ★ 按住 / 单击都走同一个节流，所以手速不会影响射速
            if (Input.GetKey(fireKey) && Time.timeSinceLevelLoad >= m_NextFireTime)
                Fire();
        }

        // ------------------------------------------------------------------ 瞄准状态

        private void EnterAim()
        {
            IsAiming = true;

            // 基准方位 = 此刻相机的水平朝向；之后鼠标只在这个基准上加/减角度
            m_AimBaseYaw = m_Player != null ? m_Player.Yaw : transform.eulerAngles.y;

            m_AimOrigin     = Vector2.zero;
            m_VirtualCursor = Vector2.zero;

            AimYaw = m_AimBaseYaw;

            SetAimVisuals(true);
        }

        private void LeaveAim()
        {
            if (!IsAiming)
            {
                SetAimVisuals(false);
                return;
            }

            IsAiming = false;

            // 退出时清节流：下次进瞄准立刻能打
            m_NextFireTime = 0f;

            SetAimVisuals(false);
        }

        private void SetAimVisuals(bool on)
        {
            if (m_RingRoot   != null) m_RingRoot.gameObject.SetActive(on);
            if (m_WeaponRoot != null) m_WeaponRoot.gameObject.SetActive(on);
            if (m_GuideLine  != null) m_GuideLine.enabled = on;
        }

        /// <summary>
        /// 读取鼠标、更新 <see cref="AimYaw"/>。
        /// 结构和 DaggerSystem.HandleAimMouse 一一对应：极坐标只取 θ，无视 r。
        /// </summary>
        private void HandleAimMouse()
        {
            float mx = Input.GetAxisRaw("Mouse X");
            float my = Input.GetAxisRaw("Mouse Y");

            m_VirtualCursor += new Vector2(mx, my) * Mathf.Max(1f, virtualCursorPixelsPerUnit);

            Vector2 d = m_VirtualCursor - m_AimOrigin;

            // 鼠标没离开原点：武器朝基准方向（进入瞄准时记下的相机前方）
            float eps = Mathf.Max(0f, dontMoveEps);
            if (d.sqrMagnitude < eps * eps)
            {
                AimYaw = m_AimBaseYaw;
                return;
            }

            // 右 = 0°、上 = 90°、左 = 180°、下 = 270°
            float thetaDeg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            // θ=90° -> 基准正前方；θ=0° -> 基准右方；θ=180° -> 基准左方
            AimYaw = Mathf.Repeat(m_AimBaseYaw + (90f - thetaDeg) + 180f, 360f) - 180f;
        }

        /// <summary>当前瞄准的水平单位方向。</summary>
        public Vector3 AimDirection()
        {
            float rad = AimYaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }

        /// <summary>把圆环放到脚下、武器摆到环上朝瞄准方向。</summary>
        private void UpdateAimTransform()
        {
            if (m_Player == null) return;

            Vector3 dir = AimDirection();
            Vector3 feet = m_Player.transform.position;

            // 圆环：始终水平，位置跟着玩家
            if (m_RingRoot != null)
            {
                m_RingRoot.position = feet + Vector3.up * ringGroundOffset;
                m_RingRoot.rotation = Quaternion.identity;
            }

            // 武器：在环上绕玩家转（和匕首一样，永远水平面）
            if (m_WeaponRoot != null)
            {
                Vector3 pos = feet + Vector3.up * weaponHeight + dir * Mathf.Max(0.1f, weaponOrbitRadius);

                m_WeaponRoot.position = pos;
                m_WeaponRoot.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }

            // 瞄准辅助线：从枪口往前一段，告诉玩家「子弹会往哪飞」
            // （瞄准时镜头被冻结、没有屏幕准星，这条线就是唯一的准星）
            if (m_GuideLine != null)
            {
                Vector3 muzzle = MuzzlePosition(dir);
                m_GuideLine.SetPosition(0, muzzle);
                m_GuideLine.SetPosition(1, muzzle + dir * Mathf.Max(1f, guideLength));
            }
        }

        /// <summary>
        /// 枪口位置。
        ///
        /// ★ 这里之前是个 bug：直接取「玩家 + 方向 × (环半径 + 枪管长度)」= 身前 1.18 米，
        ///   一旦玩家贴着墙站，枪口就落在**墙里面** —— 子弹一出生就撞墙消失，
        ///   表现就是"明明对着怪打，却打不中"。
        ///
        ///   现在先朝这个方向探一下：墙比枪口近，就把出生点拉回到墙前一点点
        ///   （最远不超过玩家身前 0.45 米，也就是胶囊体边缘外一点）。
        /// </summary>
        private Vector3 MuzzlePosition(Vector3 dir)
        {
            if (m_Player == null) return transform.position;

            Vector3 center = m_Player.transform.position + Vector3.up * weaponHeight;

            float want = Mathf.Max(0.1f, weaponOrbitRadius) + Mathf.Max(0f, muzzleForward);

            // 探墙：别让枪口生在墙里 / 生在目标身后
            if (Physics.SphereCast(center, 0.12f, dir, out RaycastHit hit, want,
                                   ~((1 << 2) | (1 << 4) | (1 << 5)),
                                   QueryTriggerInteraction.Ignore))
            {
                want = Mathf.Max(0.45f, hit.distance - 0.15f);
            }

            return center + dir * want;
        }

        // ------------------------------------------------------------------ 开火

        private void Fire()
        {
            // ── 0) 取含 buff 的有效数值（唯一出处见上面的 Effective* 属性）──
            int   cost     = EffectiveManaCost;
            int   dmg      = EffectiveDamage;
            int   killBack = EffectiveKillMana;

            // 1) 蓝不够就不打，也不占用节流（免得没蓝时连按把节奏卡死）
            DungeonMana mana = DungeonMana.Instance;
            if (mana != null && !mana.CanAfford(cost)) return;
            if (mana != null && cost > 0 && !mana.TrySpend(cost)) return;

            // 2) 节流
            m_NextFireTime = Time.timeSinceLevelLoad + EffectiveFireInterval;

            // 3) 出膛：方向完全由 AimYaw 决定，和相机没关系
            Vector3 dir    = AimDirection();
            Vector3 muzzle = MuzzlePosition(dir);

            float prevFire = LastFireTime;

            DungeonBullet.Spawn(muzzle, dir,
                                EffectiveBulletSpeed, bulletRange,
                                EffectiveBulletWidth, bulletFadeTime,
                                dmg, killBack,
                                monsterHurtRadius, monsterHurtHeight,
                                m_BulletMat, bulletTrailLength);

            LastFireTime = Time.timeSinceLevelLoad;
            ShotsFired++;

            // 调试：确认两发之间到底隔了多久（打不出间隔时才需要开）
            if (DungeonBullet.DebugLog)
            {
                float gap = prevFire < 0f ? -1f : LastFireTime - prevFire;
                Debug.Log($"[武器] 第 {ShotsFired} 发，距上一发 {gap:F3}s（设定最小间隔 {EffectiveFireInterval:F3}s）");
            }
        }

        // ------------------------------------------------------------------ 建模

        /// <summary>脚下圆环：一个 Ring（圆环带）网格，材质用 AimOverlay（永远画在最上层）。</summary>
        private void BuildRing()
        {
            var go = new GameObject("DungeonAimRing");

            m_RingRoot = go.transform;
            m_RingRoot.SetParent(transform, false);

            float outer = Mathf.Max(0.3f, ringRadius);
            float inner = outer * 0.93f;

            m_RingMesh = BuildRingMesh(64, inner, outer);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = m_RingMesh;

            var mr = go.AddComponent<MeshRenderer>();
            if (m_RingMat != null) mr.sharedMaterial = m_RingMat;
            mr.shadowCastingMode     = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows        = false;
            mr.lightProbeUsage       = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage  = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>悬浮武器：一个枪管 + 一个握把 + 一个准星，够看出朝向就行。</summary>
        private void BuildWeaponModel()
        {
            var root = new GameObject("DungeonWeaponModel");

            m_WeaponRoot = root.transform;
            m_WeaponRoot.SetParent(transform, false);

            AddPart(m_WeaponRoot, "Barrel", new Vector3(0f, 0f, 0.20f),   new Vector3(0.055f, 0.055f, 0.46f));
            AddPart(m_WeaponRoot, "Grip",   new Vector3(0f, -0.10f, 0.02f), new Vector3(0.05f, 0.20f, 0.07f));
            AddPart(m_WeaponRoot, "Sight",  new Vector3(0f, 0.045f, 0.12f), new Vector3(0.02f, 0.02f, 0.02f));
        }

        private void AddPart(Transform parent, string name, Vector3 localPos, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;

            // 只是好看，不需要碰撞
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localScale    = localScale;
            t.localRotation = Quaternion.identity;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (m_WeaponMat != null) mr.sharedMaterial = m_WeaponMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows    = false;
            }
        }

        /// <summary>
        /// 瞄准辅助线：一条从枪口往前的细线。
        ///
        /// 为什么要它：瞄准时镜头是冻结的（照文档），屏幕上没有准星，
        /// 玩家只能靠"枪口朝哪"去猜子弹会往哪飞。这条线把猜测变成可见。
        /// 和投石的轨迹预览是同一个思路，只是枪走直线，一段就够。
        /// </summary>
        private void BuildGuide()
        {
            var go = new GameObject("DungeonAimGuide");
            go.transform.SetParent(transform, false);

            m_GuideLine = go.AddComponent<LineRenderer>();
            m_GuideLine.useWorldSpace      = true;
            m_GuideLine.positionCount      = 2;
            m_GuideLine.loop               = false;
            m_GuideLine.textureMode        = LineTextureMode.Stretch;
            m_GuideLine.alignment          = LineAlignment.View;
            m_GuideLine.numCapVertices     = 0;
            m_GuideLine.startWidth         = Mathf.Max(0.002f, guideWidth);
            m_GuideLine.endWidth           = Mathf.Max(0.002f, guideWidth) * 0.4f;
            m_GuideLine.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_GuideLine.receiveShadows     = false;
            m_GuideLine.generateLightingData = false;

            if (m_GuideMat != null) m_GuideLine.sharedMaterial = m_GuideMat;

            m_GuideLine.SetPosition(0, Vector3.zero);
            m_GuideLine.SetPosition(1, Vector3.zero);
            m_GuideLine.enabled = false;
        }

        /// <summary>生成一个水平放置的圆环带（annulus）网格。</summary>
        private static Mesh BuildRingMesh(int segments, float inner, float outer)
        {
            segments = Mathf.Clamp(segments, 8, 256);

            var verts = new Vector3[(segments + 1) * 2];
            var tris  = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                float c = Mathf.Cos(a);
                float s = Mathf.Sin(a);

                verts[i * 2 + 0] = new Vector3(c * inner, 0f, s * inner);
                verts[i * 2 + 1] = new Vector3(c * outer, 0f, s * outer);
            }

            for (int i = 0; i < segments; i++)
            {
                int b = i * 2;
                int t = i * 6;

                tris[t + 0] = b + 0;
                tris[t + 1] = b + 1;
                tris[t + 2] = b + 2;

                tris[t + 3] = b + 2;
                tris[t + 4] = b + 1;
                tris[t + 5] = b + 3;
            }

            var mesh = new Mesh { name = "DungeonAimRing" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}