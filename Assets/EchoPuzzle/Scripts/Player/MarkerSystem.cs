using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 放置标记。**5 个固定槽位**，分别对应数字键 1~5。
    ///
    ///   · 按 2 只会刷新 2 号标记的位置，其它标记不受影响
    ///   · 每个槽位最多一个标记；槽位为空时那一格不渲染
    ///   · 所有标记外观**完全一样**，玩家分不出哪个是几号
    ///
    /// 渲染方式和声波光带一样：一个动态 Mesh、一次 DrawCall，
    /// 每个标记是一个朝向相机的广告牌，落下去时带一圈扩散脉冲。
    /// </summary>
    public sealed class MarkerSystem : MonoBehaviour
    {
        /// <summary>
        /// 槽位总数：10 个，下标 0~9 分别对应数字键 0~9。
        ///
        /// ★ 能不能用由 <see cref="DifficultySettings.IsSlotAllowed"/> 决定：
        ///     默认（提高标记上限 = 关）-> 只有槽位 1、2、3
        ///     开启                -> 槽位 0~9 全部
        ///   槽位本身始终是 10 个 —— 开关来回切时已放下的标记不会丢，
        ///   但关闭时会主动清掉 1~3 以外的（见 Update）。
        /// </summary>
        public const int SlotCount = 10;

        [Header("按键")]
        [Tooltip("十个槽位的按键。下标 0 对应数字键 0，下标 1 对应数字键 1，以此类推。")]
        public KeyCode[] slotKeys =
        {
            KeyCode.Alpha0,
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3,
            KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6,
            KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
        };

        [Header("外观")]
        public Color color = Color.white;

        [Tooltip("标记材质。留空则用 Resources/EchoRuntime/EchoMarker。\n" +
                 "**必须来自资产**，否则 shader 不会被打进包，exe 里标记不显示")]
        public Material material;

        [Tooltip("标记大小（米）")]
        public float size = 1.5f;

        [Tooltip("放置瞬间那圈脉冲的最大直径（米）。独立参数，只影响脉冲环扩散多远，不影响标记本体大小")]
        public float pulseDiameter = 2f;

        [Tooltip("标记离地高度（米）。稍微抬一点，避免被地板挡掉")]
        public float groundOffset = 0.06f;

        [Tooltip("放置瞬间那圈脉冲的时长（秒）")]
        public float pulseDuration = 0.55f;

        [Tooltip("整体发光强度")]
        public float glowIntensity = 2.0f;

        [Header("引用")]
        [Tooltip("留空会自动找场景里的 PlayerController")]
        public PlayerController player;

        // ------------------------------------------------------------------ 槽位状态

        private struct Slot
        {
            public bool    used;        // 这一格有没有标记
            public Vector3 pos;
            public float   placeTime;
        }

        private readonly Slot[] m_Slots = new Slot[SlotCount];

        /// <summary>当前已经放下的标记数量。</summary>
        public int MarkerCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < SlotCount; i++) if (m_Slots[i].used) n++;
                return n;
            }
        }

        /// <summary>某个槽位有没有标记。</summary>
        public bool HasMarker(int slot)
            => slot >= 0 && slot < SlotCount && m_Slots[slot].used;

        /// <summary>某个槽位的位置（没有标记时返回 false）。</summary>
        public bool TryGetMarker(int slot, out Vector3 pos)
        {
            if (slot >= 0 && slot < SlotCount && m_Slots[slot].used)
            {
                pos = m_Slots[slot].pos;
                return true;
            }

            pos = Vector3.zero;
            return false;
        }

        /// <summary>当前所有标记的世界坐标（跳过空槽位），只读用途。</summary>
        public List<Vector3> Positions
        {
            get
            {
                m_PosBuffer.Clear();
                for (int i = 0; i < SlotCount; i++)
                    if (m_Slots[i].used) m_PosBuffer.Add(m_Slots[i].pos);
                return m_PosBuffer;
            }
        }

        private readonly List<Vector3> m_PosBuffer = new List<Vector3>(SlotCount);

        // ------------------------------------------------------------------ 渲染资源

        private Mesh         m_Mesh;
        private Material     m_Mat;
        private bool         m_OwnsMat;   // true = 这份材质是实例，OnDestroy 时要销毁
        private MeshRenderer m_Renderer;
        private bool         m_Ready;

        private float m_LastPulseRadius = -1f;
        private float m_LastCoreScale   = -1f;

        private float PulseRadius01 => pulseDiameter / Mathf.Max(size, pulseDiameter);
        private float CoreScale01   => size / Mathf.Max(size, pulseDiameter);

        private void Awake()
        {
            if (player == null) player = FindObjectOfType<PlayerController>();

            var filter = gameObject.GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();

            m_Renderer = gameObject.GetComponent<MeshRenderer>();
            if (m_Renderer == null) m_Renderer = gameObject.AddComponent<MeshRenderer>();

            m_Mesh = new Mesh { name = "EchoMarkers" };
            m_Mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m_Mesh.MarkDynamic();
            filter.sharedMesh = m_Mesh;

            // ★ 材质必须来自**资产**（Resources 下的 EchoMarker.mat）。
            //   见 EchoRuntimeMaterials 的说明：没有资产引用这个 shader，
            //   打包时它会被整体剔除，于是 exe 里标记完全不显示。
            Material src = material != null ? material : EchoRuntimeMaterials.Marker;

            if (src == null)
            {
                Debug.LogError("[EchoPuzzle] 拿不到标记材质（Resources/EchoRuntime/EchoMarker）。\n" +
                               "请执行菜单：EchoPuzzle → 生成运行时材质资产");
                enabled = false;
                return;
            }

            // 克隆一份实例，避免改参数时污染工程里的资产
            m_Mat = new Material(src) { name = "EchoMarkerMat", hideFlags = HideFlags.DontSave };
            m_OwnsMat = true;

            m_Mat.SetFloat("_Intensity", glowIntensity);
            m_Mat.SetFloat("_PulseRadius", PulseRadius01);
            m_Mat.SetFloat("_CoreScale", CoreScale01);

            m_Renderer.sharedMaterial             = m_Mat;
            m_Renderer.shadowCastingMode          = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Renderer.receiveShadows             = false;
            m_Renderer.lightProbeUsage            = UnityEngine.Rendering.LightProbeUsage.Off;
            m_Renderer.reflectionProbeUsage       = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            m_Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            // 和光带一样：手动给包围盒，避免被视锥剔除掉
            m_Renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 600f);
            m_Renderer.allowOcclusionWhenDynamic = false;

            m_Ready = true;
        }

        private void OnDestroy()
        {
            // 只销毁自己克隆出来的实例；资产本体不能碰
            if (m_OwnsMat && m_Mat != null) Destroy(m_Mat);
            if (m_Mesh != null) Destroy(m_Mesh);
        }

        private void Update()
        {
            if (!m_Ready) return;
            if (!GameUI.GameplayInputActive) return;
            if (slotKeys == null) return;

            // ★ 有触发区正在等密码输入时，数字键归密码框用，不放置标记。
            if (GameUI.PasswordInputActive) return;

            // ★ 先按当前难度清理"不该存在"的标记。
            //
            //   场景：开着「提高标记上限」时放了 4~9 / 0 号标记，
            //   之后把开关关掉 —— 那些标记应当被删掉。
            PruneDisallowedSlots();

            // ★ 只响应"当前难度允许"的那些槽位：
            //   默认 1~3，开了「提高标记上限」是 0~9。
            for (int i = 0; i < SlotCount && i < slotKeys.Length; i++)
            {
                if (!DifficultySettings.IsSlotAllowed(i)) continue;

                if (Input.GetKeyDown(slotKeys[i])) Place(i);
            }
        }

        // ------------------------------------------------------------------ 放置

        /// <summary>
        /// 删掉"当前难度不允许"的那些标记。
        ///
        /// ★ 场景：开着「提高标记上限」时放了 4~9 / 0 号标记，
        ///   之后关掉开关 —— 回到游戏时这些标记应当消失。
        ///   每次 Update 都查一遍（开销极小），所以开关一关就立刻生效。
        /// </summary>
        private void PruneDisallowedSlots()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (!m_Slots[i].used) continue;
                if (DifficultySettings.IsSlotAllowed(i)) continue;

                // 直接清掉槽位。光点在 LateUpdate 里每帧按 m_Slots 重建，
                // 所以这里不用额外通知。
                m_Slots[i] = default;
            }
        }

        /// <summary>在玩家脚下放一个标记（默认放到 1 号槽）。</summary>
        public void Place() => Place(1);

        /// <summary>
        /// 在指定槽位放一个标记。会**刷新**这个槽位的位置，
        /// 其它槽位的标记完全不受影响。
        /// </summary>
        public void Place(int slot)
        {
            if (slot < 0 || slot >= SlotCount) return;

            m_Slots[slot] = new Slot
            {
                used      = true,
                pos       = CurrentFootPosition(),
                placeTime = Time.timeSinceLevelLoad,
            };
        }

        /// <summary>玩家脚下的放置点（找不到玩家时退回相机位置）。</summary>
        private Vector3 CurrentFootPosition()
        {
            Vector3 pos;
            if (player != null)
            {
                pos = player.FootPosition;
            }
            else
            {
                var cam = Camera.main;
                pos = cam != null ? cam.transform.position : Vector3.zero;
            }

            pos.y += groundOffset;
            return pos;
        }

        // ------------------------------------------------------------------ 存档

        /// <summary>导出槽位状态（存档用）。数组长度固定为 5。</summary>
        public void ExportSlots(List<Vector3> outPositions, List<bool> outUsed)
        {
            outPositions.Clear();
            outUsed.Clear();

            for (int i = 0; i < SlotCount; i++)
            {
                outPositions.Add(m_Slots[i].pos);
                outUsed.Add(m_Slots[i].used);
            }
        }

        /// <summary>导入槽位状态（读档用）。</summary>
        public void ImportSlots(IList<Vector3> positions, IList<bool> used)
        {
            if (positions == null || used == null) return;

            for (int i = 0; i < SlotCount; i++)
            {
                // ★ 存档里可能有"当前难度不允许"的槽位（比如存档时开着
                //   提高标记上限、现在关掉了）—— 那些直接丢弃。
                bool has = DifficultySettings.IsSlotAllowed(i)
                         && i < used.Count && used[i];

                Vector3 p = i < positions.Count ? positions[i] : Vector3.zero;

                m_Slots[i] = new Slot
                {
                    used      = has,
                    pos       = p,
                    placeTime = Time.timeSinceLevelLoad,   // 读档时不重放脉冲
                };
            }
        }

        /// <summary>清空所有槽位（重玩时用）。</summary>
        public void ClearMarkers()
        {
            for (int i = 0; i < SlotCount; i++) m_Slots[i] = default;
        }

        // ------------------------------------------------------------------ 渲染

        private void LateUpdate()
        {
            if (!m_Ready) return;

            m_Mesh.Clear(false);

            int n = MarkerCount;
            if (n == 0) return;

            Camera cam = Camera.main;
            Vector3 camPos, camRight, camUp;
            if (cam != null)
            {
                camPos   = cam.transform.position;
                camRight = cam.transform.right;
                camUp    = cam.transform.up;
            }
            else
            {
                camPos   = Vector3.zero;
                camRight = Vector3.right;
                camUp    = Vector3.up;
            }

            var verts = new Vector3[n * 4];
            var uvs   = new Vector2[n * 4];
            var cols  = new Color[n * 4];
            var misc  = new Vector2[n * 4];
            var tris  = new int[n * 6];

            // 广告牌边长取较大者，保证两个圆都能画下；但两者的物理大小由各自参数独立决定
            float billboard = Mathf.Max(size, pulseDiameter);
            float half = Mathf.Max(0.01f, billboard) * 0.5f;

            float pulseRadius = pulseDiameter / billboard;
            float coreScale   = size / billboard;
            if (m_Mat != null &&
                (Mathf.Abs(pulseRadius - m_LastPulseRadius) > 0.0001f ||
                 Mathf.Abs(coreScale - m_LastCoreScale) > 0.0001f))
            {
                m_LastPulseRadius = pulseRadius;
                m_LastCoreScale   = coreScale;
                m_Mat.SetFloat("_PulseRadius", pulseRadius);
                m_Mat.SetFloat("_CoreScale", coreScale);
            }

            int w = 0;   // 已写入的四边形数（跳过空槽位）

            for (int i = 0; i < SlotCount; i++)
            {
                if (!m_Slots[i].used) continue;

                Slot mk = m_Slots[i];

                Vector3 p = mk.pos;
                Vector3 r = camRight * half;
                Vector3 u = camUp    * half;

                int v = w * 4;
                verts[v + 0] = p - r - u;
                verts[v + 1] = p - r + u;
                verts[v + 2] = p + r + u;
                verts[v + 3] = p + r - u;

                uvs[v + 0] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(0f, 1f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(1f, 0f);

                Color c = color;
                for (int k = 0; k < 4; k++) cols[v + k] = c;

                // x = 放置时间, y = 脉冲时长（shader 靠这两个算扩散进度）
                Vector2 mi = new Vector2(mk.placeTime, Mathf.Max(0.01f, pulseDuration));
                for (int k = 0; k < 4; k++) misc[v + k] = mi;

                int t = w * 6;
                tris[t + 0] = v + 0;
                tris[t + 1] = v + 1;
                tris[t + 2] = v + 2;
                tris[t + 3] = v + 0;
                tris[t + 4] = v + 2;
                tris[t + 5] = v + 3;

                w++;
            }

            m_Mesh.SetVertices(verts);
            m_Mesh.SetUVs(0, uvs);
            m_Mesh.SetColors(cols);
            m_Mesh.SetUVs(1, misc);          // TEXCOORD1 放"放置时间 / 脉冲时长"
            m_Mesh.SetTriangles(tris, 0);
        }
    }
}
