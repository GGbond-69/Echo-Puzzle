using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 脚印系统。
    ///
    /// **每次释放声波**时在玩家脚下打印一个鞋印，左右交替：
    /// 第一次左脚、第二次右脚、第三次左脚……走路 / 跑步 / 潜行时
    /// 因为它们各自的发射频率不同，所以印子的疏密也跟着变。
    ///
    /// 每个脚印的不透明度随时间渐降，降到阈值以下就淘汰。
    ///
    /// 渲染方式和光带 / 标记完全一致：一个动态 Mesh、一次 DrawCall，
    /// ZTest LEqual（所以会被不透光的遮挡墙体挡住）。
    /// </summary>
    public sealed class FootprintSystem : MonoBehaviour
    {
        [Header("脚印")]
        [Tooltip("脚印长度（米）")]
        public float length = 0.6f;

        [Tooltip("脚印宽度（米）")]
        public float width = 0.4f;

        [Tooltip("左右脚中心离玩家中轴线的距离（米）")]
        public float lateralOffset = 0.30f;

        [Tooltip("脚印离地高度（米）")]
        public float groundOffset = 0.02f;

        [Tooltip("脚印朝向相对玩家前方的偏转角（度）。0 = 完全朝前，稍大一点像外八")]
        public float splayDegrees = 6f;

        [Header("外观")]
        public Color color = Color.white;

        [Tooltip("脚印材质。留空则用 Resources/EchoRuntime/EchoFootprint。\n" +
                 "**必须来自资产**，否则 shader 不会被打进包，exe 里脚印不显示")]
        public Material material;

        [Tooltip("整体发光强度")]
        public float glowIntensity = 1.6f;

        [Tooltip("初始不透明度")]
        [Range(0f, 1f)] public float initialAlpha = 0.55f;

        [Header("生命")]
        [Tooltip("从放置到完全消失的总时长（秒）")]
        [Min(0.1f)] public float lifetime = 24f;

        [Header("上限")]
        [Tooltip("同时最多保留多少个脚印。超出时最早放置的被丢掉")]
        public int maxFootprints = 400;

        [Header("引用")]
        [Tooltip("留空会自动找场景里的 PlayerController")]
        public PlayerController player;

        // ------------------------------------------------------------------ 内部

        private struct Footprint
        {
            public Vector3 pos;
            public Vector3 right;    // 脚掌的横向（已经含左右偏移和偏转）
            public Vector3 forward;  // 脚掌的长轴方向
            public float   birth;
        }

        private readonly List<Footprint> m_Prints = new List<Footprint>(512);

        private Mesh         m_Mesh;
        private Material     m_Mat;
        private bool         m_OwnsMat;   // true = 这份材质是实例，OnDestroy 时要销毁
        private MeshRenderer m_Renderer;
        private bool         m_Ready;

        // 顶点缓冲。每次重建都复用，避免每帧分配
        private Vector3[] m_Verts;
        private Vector2[] m_Uvs;
        private Color[]   m_Cols;
        private int[]     m_Indices;
        private int       m_Capacity;

        private bool m_NextIsLeft = true;   // 一左一右交替

        /// <summary>当前脚印数量。</summary>
        public int Count => m_Prints.Count;

        private void Awake()
        {
            if (player == null) player = FindObjectOfType<PlayerController>();

            var filter = gameObject.GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();

            m_Renderer = gameObject.GetComponent<MeshRenderer>();
            if (m_Renderer == null) m_Renderer = gameObject.AddComponent<MeshRenderer>();

            m_Mesh = new Mesh { name = "EchoFootprints" };
            m_Mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m_Mesh.MarkDynamic();
            filter.sharedMesh = m_Mesh;

            // ★ 材质必须来自**资产**（Resources 下的 EchoFootprint.mat）。
            //   见 EchoRuntimeMaterials 的说明：没有资产引用这个 shader，
            //   打包时它会被整体剔除，于是 exe 里脚印完全不显示。
            Material src = material != null ? material : EchoRuntimeMaterials.Footprint;

            if (src == null)
            {
                Debug.LogError("[EchoPuzzle] 拿不到脚印材质（Resources/EchoRuntime/EchoFootprint）。\n" +
                               "请执行菜单：EchoPuzzle → 生成运行时材质资产");
                enabled = false;
                return;
            }

            // 克隆一份实例，避免改参数时污染工程里的资产
            m_Mat = new Material(src) { name = "EchoFootprintMat", hideFlags = HideFlags.DontSave };
            m_OwnsMat = true;

            m_Mat.SetFloat("_Intensity", glowIntensity);

            m_Renderer.sharedMaterial             = m_Mat;
            m_Renderer.shadowCastingMode          = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Renderer.receiveShadows             = false;
            m_Renderer.lightProbeUsage            = UnityEngine.Rendering.LightProbeUsage.Off;
            m_Renderer.reflectionProbeUsage       = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            m_Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            // 和光带一样：手动给包围盒，避免被视锥剔除掉
            m_Renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 600f);
            m_Renderer.allowOcclusionWhenDynamic = false;

            EnsureCapacity(256);
            m_Ready = true;
        }

        private void OnDestroy()
        {
            // 只销毁自己克隆出来的实例；资产本体不能碰
            if (m_OwnsMat && m_Mat != null) Destroy(m_Mat);
            if (m_Mesh != null) Destroy(m_Mesh);
        }

        private void OnValidate()
        {
            if (m_Mat != null) m_Mat.SetFloat("_Intensity", glowIntensity);
        }

        // ------------------------------------------------------------------ 打印脚印

        /// <summary>
        /// 在玩家脚下打印一个脚印（左右自动交替）。
        /// 每次释放声波调用一次。
        /// </summary>
        public void Print()
        {
            if (!m_Ready) return;
            if (player == null) return;

            // ★「降低难度 → 显示脚印」没开就不留脚印（默认关）
            if (!DifficultySettings.ShowFootprints) return;

            Vector3 origin = player.FootPosition;

            // 脚掌朝向：用玩家正前方（水平化），再按左右做一点外八偏转
            Vector3 fwd = player.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) return;
            fwd.Normalize();

            Vector3 baseRight = Vector3.Cross(Vector3.up, fwd).normalized;

            // 一左一右
            float side = m_NextIsLeft ? -1f : 1f;
            m_NextIsLeft = !m_NextIsLeft;

            float splay = splayDegrees * side;
            Quaternion rot = Quaternion.AngleAxis(splay, Vector3.up);

            Vector3 f = rot * fwd;
            Vector3 r = rot * baseRight;

            Vector3 pos = origin + baseRight * (lateralOffset * side);
            pos.y += groundOffset;

            // 超过上限就丢掉最早的
            int cap = Mathf.Max(1, maxFootprints);
            while (m_Prints.Count >= cap) m_Prints.RemoveAt(0);

            m_Prints.Add(new Footprint
            {
                pos     = pos,
                right   = r,
                forward = f,
                birth   = Time.timeSinceLevelLoad,
            });
        }

        /// <summary>清空所有脚印（重玩时用）。</summary>
        public void ClearFootprints()
        {
            m_Prints.Clear();
            m_NextIsLeft = true;
        }

        // ------------------------------------------------------------------ 渲染

        private void EnsureCapacity(int quads)
        {
            if (quads <= m_Capacity) return;

            // 一次多留一些，避免频繁扩容
            int cap = Mathf.Max(quads, m_Capacity * 2, 256);

            m_Verts   = new Vector3[cap * 4];
            m_Uvs     = new Vector2[cap * 4];
            m_Cols    = new Color  [cap * 4];
            m_Indices = new int    [cap * 6];

            for (int q = 0; q < cap; q++)
            {
                int v = q * 4, t = q * 6;
                m_Indices[t + 0] = v + 0; m_Indices[t + 1] = v + 1; m_Indices[t + 2] = v + 2;
                m_Indices[t + 3] = v + 0; m_Indices[t + 4] = v + 2; m_Indices[t + 5] = v + 3;
            }

            m_Capacity = cap;
        }

        private void LateUpdate()
        {
            if (!m_Ready) return;

            float now = Time.timeSinceLevelLoad;
            float life = Mathf.Max(0.1f, lifetime);

            // ---- 淘汰过期脚印（倒序删除） ----
            for (int i = m_Prints.Count - 1; i >= 0; i--)
            {
                if (now - m_Prints[i].birth >= life) m_Prints.RemoveAt(i);
            }

            m_Mesh.Clear(false);

            // ★「降低难度 → 显示脚印」没开就不画（已留下的也会一起被隐藏）
            if (!DifficultySettings.ShowFootprints) return;

            int n = m_Prints.Count;
            if (n == 0) return;

            EnsureCapacity(n);

            float hl = Mathf.Max(0.01f, length) * 0.5f;
            float hw = Mathf.Max(0.01f, width)  * 0.5f;

            int w = 0;

            for (int i = 0; i < n; i++)
            {
                Footprint fp = m_Prints[i];

                // 不透明度线性降到 0，最后一段用平方让尾巴收得柔一点
                float t     = Mathf.Clamp01((now - fp.birth) / life);
                float alpha = initialAlpha * (1f - t) * (1f - t);

                if (alpha <= 0.004f) continue;

                // 四边形：长轴沿 forward，短轴沿 right
                Vector3 f = fp.forward * hl;
                Vector3 r = fp.right   * hw;
                Vector3 p = fp.pos;

                int v = w * 4;
                m_Verts[v + 0] = p - f - r;   // 脚跟左
                m_Verts[v + 1] = p - f + r;   // 脚跟右
                m_Verts[v + 2] = p + f + r;   // 脚尖右
                m_Verts[v + 3] = p + f - r;   // 脚尖左

                // UV：x 是脚跟->脚尖的长轴，y 是左右
                m_Uvs[v + 0] = new Vector2(0f, 0f);
                m_Uvs[v + 1] = new Vector2(0f, 1f);
                m_Uvs[v + 2] = new Vector2(1f, 1f);
                m_Uvs[v + 3] = new Vector2(1f, 0f);

                Color c = color;
                c.a = alpha;
                for (int k = 0; k < 4; k++) m_Cols[v + k] = c;

                w++;
            }

            if (w == 0) return;

            int vcount = w * 4;
            m_Mesh.SetVertices(m_Verts, 0, vcount);
            m_Mesh.SetUVs(0, m_Uvs, 0, vcount, UnityEngine.Rendering.MeshUpdateFlags.Default);
            m_Mesh.SetColors(m_Cols, 0, vcount);
            m_Mesh.SetIndices(m_Indices, 0, w * 6, MeshTopology.Triangles, 0, true);
        }
    }
}
