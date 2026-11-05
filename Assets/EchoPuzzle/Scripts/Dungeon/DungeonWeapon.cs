using UnityEngine;

namespace EchoPuzzle.Dungeon
{
    /// <summary>
    /// 【地牢模式】玩家的武器：瞄准 + 射击 + 蓝耗。
    ///
    /// ── 操作 ────────────────────────────────────────────────
    ///   · 按住右键（aimKey）进入瞄准：脚下出现圆环，正前方浮起一把武器
    ///   · 瞄准时按住 / 单击左键（fireKey）发射，受 fireInterval 限制
    ///     —— 单击再快也不会超过这个射速
    ///   · 松开右键退出瞄准
    ///
    /// ── 瞄准方向 ────────────────────────────────────────────
    ///   这一版用"相机准星"：屏幕中心射一条射线，打到哪就朝哪打。
    ///   也就是说瞄准时鼠标仍然转视角（和探秘模式的投石/匕首不同 ——
    ///   那两个是"按住右键拖动鼠标定角度"，更像投掷而不是射击）。
    ///
    ///   ★ 想要文档里那种"鼠标不转镜头、只移动武器角度"的手感，
    ///     只需要在 PlayerController.HandleLook 里加一个和
    ///     StoneThrowSystem.IsAiming 同款的判断（约 3 行），
    ///     本文件不用改 —— 见文末说明。
    ///
    /// ── 和探秘模式的关系 ────────────────────────────────────
    ///   ★ 只有 <see cref="DungeonWeaponBootstrap"/> 在地牢场景里会创建它。
    ///     探秘模式的 <see cref="DaggerSystem"/> / <see cref="StoneThrowSystem"/>
    ///     在地牢模式下本来就不创建，两边不会抢右键。
    /// </summary>
    [AddComponentMenu("Echo Puzzle/Dungeon/Dungeon Weapon")]
    [DefaultExecutionOrder(-50)]
    public sealed class DungeonWeapon : MonoBehaviour
    {
        public static DungeonWeapon Instance { get; private set; }

        // ------------------------------------------------------------------ 输入

        [Header("输入")]
        [Tooltip("按住进入瞄准")]
        public KeyCode aimKey = KeyCode.Mouse1;

        [Tooltip("瞄准时按住 / 单击都会发射")]
        public KeyCode fireKey = KeyCode.Mouse0;

        // ------------------------------------------------------------------ 射击

        [Header("射击")]
        [Tooltip("两发之间的最小间隔（秒）。按住和连点都受它限制")]
        [Min(0.02f)] public float fireInterval = 0.18f;

        [Tooltip("单发蓝耗")]
        [Min(0)] public int manaCost = 5;

        [Tooltip("单发伤害。怪物血量见 PuzzleMonster.dungeonMaxHealth（默认 12）")]
        [Min(1)] public int damage = 1;

        [Tooltip("打死一只怪回复的蓝")]
        [Min(0)] public int killManaRestore = 15;

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

        [Tooltip("武器悬浮点离玩家脚底的高度（米）")]
        public float weaponHeight = 1.15f;

        [Tooltip("武器悬浮点离玩家的前向距离（米）")]
        public float weaponDistance = 0.35f;

        public Color weaponColor = new Color(1f, 1f, 1f, 0.9f);

        // ------------------------------------------------------------------ 运行时

        /// <summary>是否正在瞄准。</summary>
        public bool IsAiming { get; private set; }

        /// <summary>最近一次成功发射的时间（Time.timeSinceLevelLoad）。</summary>
        public float LastFireTime { get; private set; } = -999f;

        /// <summary>累计发射数（调试 / buff 统计用）。</summary>
        public int ShotsFired { get; private set; }

        private PlayerController m_Player;
        private Camera           m_Cam;

        private Transform m_RingRoot;
        private Transform m_WeaponRoot;
        private Mesh      m_RingMesh;

        private Material m_RingMat;
        private Material m_WeaponMat;
        private Material m_BulletMat;

        private float m_NextFireTime;

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

            BuildRing();
            BuildWeaponModel();

            SetAimVisuals(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            if (m_RingMesh != null) Destroy(m_RingMesh);

            if (m_RingRoot   != null) Destroy(m_RingRoot.gameObject);
            if (m_WeaponRoot != null) Destroy(m_WeaponRoot.gameObject);

            if (m_RingMat   != null) Destroy(m_RingMat);
            if (m_WeaponMat != null) Destroy(m_WeaponMat);
            if (m_BulletMat != null) Destroy(m_BulletMat);
        }

        // ------------------------------------------------------------------ 主循环

        private void Update()
        {
            // ★ 只在真正能操作的时候响应
            if (!GameUI.IsDungeonMode) { SetAiming(false); return; }

            if (m_Player == null) m_Player = FindObjectOfType<PlayerController>();
            if (m_Player == null) { SetAiming(false); return; }

            if (!GameUI.PlayerControllable || !GameUI.CameraInputActive)
            {
                SetAiming(false);
                return;
            }

            bool wantAim = Input.GetKey(aimKey);
            SetAiming(wantAim);

            if (!IsAiming) return;

            UpdateAimTransform();

            // ★ 按住 / 单击都走同一个节流，所以手速不会影响射速
            if (Input.GetKey(fireKey) && Time.timeSinceLevelLoad >= m_NextFireTime)
                Fire();
        }

        // ------------------------------------------------------------------ 瞄准状态

        private void SetAiming(bool on)
        {
            if (on == IsAiming) return;

            IsAiming = on;
            SetAimVisuals(on);

            // 退出瞄准时把节流清掉：下次进瞄准立刻能打
            if (!on) m_NextFireTime = 0f;
        }

        private void SetAimVisuals(bool on)
        {
            if (m_RingRoot   != null) m_RingRoot.gameObject.SetActive(on);
            if (m_WeaponRoot != null) m_WeaponRoot.gameObject.SetActive(on);
        }

        /// <summary>把圆环放到脚下、武器放到准星方向。</summary>
        private void UpdateAimTransform()
        {
            if (m_Player == null) return;

            Vector3 aimDir = GetAimDirection();

            // 圆环：玩家脚下（不跟着身体旋转，始终水平）
            if (m_RingRoot != null)
            {
                m_RingRoot.position = m_Player.transform.position + Vector3.up * ringGroundOffset;
                m_RingRoot.rotation = Quaternion.identity;
            }

            // 武器：悬浮在玩家前方，指向瞄准方向
            if (m_WeaponRoot != null)
            {
                Vector3 pivot = m_Player.transform.position + Vector3.up * weaponHeight;
                m_WeaponRoot.position = pivot + aimDir * weaponDistance;
                m_WeaponRoot.rotation = Quaternion.LookRotation(aimDir, Vector3.up);
            }
        }

        /// <summary>瞄准方向：屏幕中心射线的方向（相机准星）。</summary>
        private Vector3 GetAimDirection()
        {
            Camera cam = GetCamera();

            if (cam != null)
            {
                Ray r = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

                // 打到墙就用墙上的点，否则用射程尽头
                Vector3 point = r.origin + r.direction * bulletRange;

                if (Physics.Raycast(r, out RaycastHit hit, bulletRange,
                                    ~((1 << 2) | (1 << 4) | (1 << 5)),
                                    QueryTriggerInteraction.Ignore))
                    point = hit.point;

                Vector3 dir = point - MuzzlePosition(r.direction);
                if (dir.sqrMagnitude > 1e-6f) return dir.normalized;
            }

            // 没相机就朝玩家正前方
            Vector3 fwd = m_Player != null ? m_Player.transform.forward : Vector3.forward;
            fwd.y = 0f;
            return fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
        }

        private Vector3 MuzzlePosition(Vector3 fallbackDir)
        {
            if (m_Player == null) return transform.position;

            Vector3 dir = fallbackDir.sqrMagnitude > 1e-6f ? fallbackDir.normalized : Vector3.forward;
            return m_Player.transform.position + Vector3.up * weaponHeight + dir * (weaponDistance + 0.25f);
        }

        private Camera GetCamera()
        {
            if (m_Cam != null) return m_Cam;

            m_Cam = m_Player != null && m_Player.playerCamera != null
                  ? m_Player.playerCamera
                  : Camera.main;

            return m_Cam;
        }

        // ------------------------------------------------------------------ 开火

        private void Fire()
        {
            // 1) 蓝不够就不打，也不占用节流（免得没蓝时连按把节奏卡死）
            DungeonMana mana = DungeonMana.Instance;
            if (mana != null && !mana.CanAfford(manaCost)) return;

            if (mana != null && manaCost > 0 && !mana.TrySpend(manaCost)) return;

            // 2) 节流
            m_NextFireTime = Time.timeSinceLevelLoad + Mathf.Max(0.02f, fireInterval);

            // 3) 出膛
            Vector3 dir   = GetAimDirection();
            Vector3 muzzle = MuzzlePosition(dir);

            DungeonBullet.Spawn(muzzle, dir,
                                bulletSpeed, bulletRange, bulletWidth, bulletFadeTime,
                                damage, killManaRestore,
                                monsterHurtRadius, monsterHurtHeight,
                                m_BulletMat);

            LastFireTime = Time.timeSinceLevelLoad;
            ShotsFired++;
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
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            mr.lightProbeUsage   = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>悬浮武器：一个枪管 + 一个握把（两个 cube），够看出朝向就行。</summary>
        private void BuildWeaponModel()
        {
            var root = new GameObject("DungeonWeaponModel");

            m_WeaponRoot = root.transform;
            m_WeaponRoot.SetParent(transform, false);

            AddPart(m_WeaponRoot, "Barrel", new Vector3(0f, 0f, 0.20f), new Vector3(0.055f, 0.055f, 0.46f));
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