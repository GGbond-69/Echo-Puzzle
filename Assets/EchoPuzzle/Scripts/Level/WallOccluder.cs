using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>遮挡墙体的形状。</summary>
    public enum OccluderShape
    {
        Box    = 0,   // 方墙（用尺寸直接定长宽高）
        Circle = 1,   // 圆弧墙
        Dome   = 2,   // 空心半球
    }

    /// <summary>
    /// **完全不透光**的墙体。即使打开了"墙体完全透明 + 关闭光照"，
    /// 它依然会挡住：
    ///   玩家物体、声波光带、标记、放置标记时的脉冲
    /// 但**不会**挡住：
    ///   HintZone 文字、所有 UI（它们是 IMGUI 画的，不经过深度缓冲）
    ///
    /// 实现要点：材质用 <c>EchoPuzzle/Occluder</c>，画成纯黑并写深度；
    /// 且 Cull Off（两面都画）—— 否则玩家站进墙体内部时会从里面看到透明。
    ///
    /// 形状复用现有生成器：
    ///   Circle -> CircleWall.BuildMesh
    ///   Dome   -> HemisphereDome.BuildMesh
    ///   Box    -> 自己生成一个立方体网格
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class WallOccluder : MonoBehaviour
    {
        [Header("形状")]
        public OccluderShape shape = OccluderShape.Box;

        [Header("方墙（shape = Box）")]
        public Vector3 size = new Vector3(1f, 1f, 1f);
        public Vector3 center = Vector3.zero;

        [Header("圆弧墙（shape = Circle）")]
        [Min(0.1f)] public float radius = 8f;
        [Min(0f)]   public float thickness = 1f;
        [Range(1f, 360f)] public float angle = 360f;
        public float startAngle = 0f;
        [Min(0.1f)] public float height = 4f;
        [Range(2, 128)] public int segmentsPerQuarter = 16;

        [Header("半球（shape = Dome）")]
        [Range(4, 128)] public int longitudeSegments = 48;
        [Range(2, 48)]  public int latitudeSegments  = 16;

        [Header("位置")]
        [Tooltip("底面高度。0 = 贴地")]
        public float bottom = 0f;

        [Header("渲染")]
        [Tooltip("留空则回落到共用的遮挡材质（纯黑、写深度）")]
        public Material material;

        private MeshFilter   m_Filter;
        private MeshRenderer m_Renderer;
        private MeshCollider m_Collider;

        private static Material s_SharedMat;

        private static Material SharedMat
        {
            get
            {
                if (s_SharedMat == null) s_SharedMat = EchoMaterials.CreateOccluder();
                return s_SharedMat;
            }
        }

        private void Reset()
        {
            if (GetComponent<MeshCollider>() == null) gameObject.AddComponent<MeshCollider>();
        }

        private void Awake()    => Generate();
        private void OnEnable() => Generate();

#if UNITY_EDITOR
        private void OnValidate() => ScheduleGenerate();

        private static bool s_GenScheduled;

        /// <summary>
        /// 安排一次延迟重建（面板改参数后下一帧生效）。
        ///
        /// ★ 必须先去重再注册，且要能安全退出。
        ///
        ///   旧写法每次 OnValidate 都 `delayCall +=`，永不移除：
        ///     · Inspector 里拖一次滑条就可能堆几十个待处理回调
        ///     · 这些回调是编辑器级的，退出播放 / 关闭工程时引擎正在销毁对象，
        ///       它们仍会依次执行 Generate()（重建网格），访问已释放的 MeshFilter
        ///     · 表现就是关闭 Unity 时卡在 Application.Shutdown.CleanupEngine 无响应
        ///
        ///   现在用静态标志保证同一时刻只挂一个回调，并且回调开头就检查
        ///   playing / 对象有效性，销毁阶段直接放弃。
        /// </summary>
        private void ScheduleGenerate()
        {
            if (s_GenScheduled) return;
            s_GenScheduled = true;

            UnityEditor.EditorApplication.delayCall += () =>
            {
                s_GenScheduled = false;

                // 正在切换播放模式（进 / 出 Play）或工程正在关闭时不要碰网格
                if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode
                    && !UnityEditor.EditorApplication.isPlaying)
                {
                    return;
                }

                // 对象已被销毁就放弃
                if (this == null || !gameObject.activeInHierarchy) return;

                Generate();
            };
        }
#endif

        [ContextMenu("重新生成遮挡墙")]
        public void Generate()
        {
            m_Filter   = GetComponent<MeshFilter>();
            m_Renderer = GetComponent<MeshRenderer>();

            Mesh mesh = BuildMesh(shape, size, center, radius, thickness, angle, startAngle,
                                  height, bottom, segmentsPerQuarter,
                                  longitudeSegments, latitudeSegments);
            mesh.name = "WallOccluder";
            m_Filter.sharedMesh = mesh;

            m_Renderer.sharedMaterial = material != null ? material : SharedMat;

            // 它不参与光照，也不该投影 —— 免得在别处留下影子
            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Renderer.receiveShadows    = false;

            // 声波射线要打在它上面才会被挡住
            m_Collider = GetComponent<MeshCollider>();
            if (m_Collider == null) m_Collider = gameObject.AddComponent<MeshCollider>();

            m_Collider.sharedMesh = null;      // 先清掉，强制 PhysX 重建加速结构
            m_Collider.sharedMesh = mesh;
            m_Collider.convex     = false;
        }

        // ------------------------------------------------------------------ 编辑期边框

#if UNITY_EDITOR

        /// <summary>
        /// 边框颜色 —— **全局静态**，改一处所有 WallOccluder 一起生效。
        ///
        /// ★ 为什么是静态的：
        ///   场景里有几百个遮挡墙，逐个在 Inspector 里调颜色不现实。
        ///   静态字段没有序列化，所以：
        ///     · 改这里的默认值 -> 所有物体立刻用新颜色
        ///     · Inspector 里不会出现这个字段（不占地方，也不会被序列化值覆盖）
        ///
        /// ★ 改不透明度就改第 4 个参数（alpha，0~1）：
        ///     1.0  完全不透明
        ///     0.35 半透明（当前）
        ///     0.1  很淡
        /// </summary>
        public static Color OutlineColor = new Color(1f, 1f, 1f, 0.1f);

        [Header("编辑期边框")]
        [Tooltip("在 Scene 视图里用直线画出这个遮挡墙的轮廓。\n\n" +
                 "★ 只在编辑状态下显示，进 Play 后就没了。\n" +
                 "  因为墙体在游戏里是「完全黑、看不见」的，\n" +
                 "  摆地图时如果没有边框就完全找不到它们在哪。\n\n" +
                 "边框颜色是全局的，见 WallOccluder.OutlineColor。")]
        public bool showEditorOutline = true;

        /// <summary>
        /// 在 Scene 视图里画边框。
        ///
        /// 三种形状分别画：
        ///   · Box    -> 12 条棱（立方体线框）
        ///   · Circle -> 上下两圈圆弧 + 若干根竖直连线
        ///   · Dome   -> 若干条经线 + 几条纬线
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!showEditorOutline) return;
            if (!isActiveAndEnabled) return;

            Gizmos.color = OutlineColor;

            switch (shape)
            {
                case OccluderShape.Circle:
                    DrawCircleOutline();
                    break;

                case OccluderShape.Dome:
                    DrawDomeOutline();
                    break;

                default:
                    DrawBoxOutline();
                    break;
            }
        }

        /// <summary>方墙：12 条棱。</summary>
        private void DrawBoxOutline()
        {
            Vector3 h = new Vector3(Mathf.Max(0.001f, size.x),
                                    Mathf.Max(0.001f, size.y),
                                    Mathf.Max(0.001f, size.z)) * 0.5f;

            Vector3 c = new Vector3(center.x, center.y + bottom, center.z);

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

            Gizmos.DrawLine(p[0], p[1]); Gizmos.DrawLine(p[1], p[2]);
            Gizmos.DrawLine(p[2], p[3]); Gizmos.DrawLine(p[3], p[0]);

            Gizmos.DrawLine(p[4], p[5]); Gizmos.DrawLine(p[5], p[6]);
            Gizmos.DrawLine(p[6], p[7]); Gizmos.DrawLine(p[7], p[4]);

            Gizmos.DrawLine(p[0], p[4]); Gizmos.DrawLine(p[1], p[5]);
            Gizmos.DrawLine(p[2], p[6]); Gizmos.DrawLine(p[3], p[7]);
        }

        /// <summary>圆弧墙：上下两圈 + 竖直连接线。</summary>
        private void DrawCircleOutline()
        {
            float rad = Mathf.Max(0.1f, radius);
            float ang = Mathf.Clamp(angle, 1f, 360f);
            float y0  = bottom;
            float y1  = bottom + Mathf.Max(0.1f, height);

            Transform tr = transform;

            int seg = Mathf.Clamp(Mathf.CeilToInt(ang / 90f * segmentsPerQuarter), 4, 256);

            Vector3[] lo = new Vector3[seg + 1];
            Vector3[] hi = new Vector3[seg + 1];

            for (int i = 0; i <= seg; i++)
            {
                float a = startAngle + ang * i / seg;
                float r = a * Mathf.Deg2Rad;

                float x = Mathf.Cos(r) * rad;
                float z = Mathf.Sin(r) * rad;

                lo[i] = tr.TransformPoint(new Vector3(x, y0, z));
                hi[i] = tr.TransformPoint(new Vector3(x, y1, z));
            }

            for (int i = 0; i < seg; i++)
            {
                Gizmos.DrawLine(lo[i], lo[i + 1]);
                Gizmos.DrawLine(hi[i], hi[i + 1]);
            }

            // 竖直连接：最多 16 根，太多会糊成一片
            int step = Mathf.Max(1, seg / 16);

            for (int i = 0; i <= seg; i += step)
            {
                Gizmos.DrawLine(lo[i], hi[i]);
            }

            // 起点 / 终点补一根，让切口看得清
            if (ang < 359.9f)
            {
                Gizmos.DrawLine(lo[0], hi[0]);
                Gizmos.DrawLine(lo[seg], hi[seg]);
            }
        }

        /// <summary>半球：若干经线 + 几条纬线。</summary>
        private void DrawDomeOutline()
        {
            float rad = Mathf.Max(0.1f, radius);
            float y0  = bottom;

            Transform tr = transform;

            const int meridians = 12;    // 经线数量
            const int latitudes = 5;     // 纬线数量（不含底边）

            // ---- 经线 ----
            for (int m = 0; m < meridians; m++)
            {
                float a  = 360f * m / meridians * Mathf.Deg2Rad;
                float cx = Mathf.Cos(a);
                float cz = Mathf.Sin(a);

                Vector3 prev = tr.TransformPoint(new Vector3(cx * rad, y0, cz * rad));

                const int segs = 12;

                for (int i = 1; i <= segs; i++)
                {
                    // 0 度 = 地平线，90 度 = 顶点
                    float phi = 90f * i / segs * Mathf.Deg2Rad;

                    float r = Mathf.Cos(phi) * rad;
                    float y = Mathf.Sin(phi) * rad;

                    Vector3 cur = tr.TransformPoint(new Vector3(cx * r, y0 + y, cz * r));

                    Gizmos.DrawLine(prev, cur);
                    prev = cur;
                }
            }

            // ---- 纬线 ----
            for (int l = 1; l <= latitudes; l++)
            {
                float phi = 90f * l / (latitudes + 1) * Mathf.Deg2Rad;

                float r = Mathf.Cos(phi) * rad;
                float y = Mathf.Sin(phi) * rad;

                const int segs = 48;
                Vector3 prev = Vector3.zero;

                for (int i = 0; i <= segs; i++)
                {
                    float a = 360f * i / segs * Mathf.Deg2Rad;

                    Vector3 cur = tr.TransformPoint(
                        new Vector3(Mathf.Cos(a) * r, y0 + y, Mathf.Sin(a) * r));

                    if (i > 0) Gizmos.DrawLine(prev, cur);
                    prev = cur;
                }
            }
        }

#endif

        // ------------------------------------------------------------------ 网格

        /// <summary>按形状生成网格。复用圆墙 / 半球的生成器，方墙自己生成。</summary>
        public static Mesh BuildMesh(OccluderShape shape,
                                     Vector3 size, Vector3 center,
                                     float radius, float thickness, float angle, float startAngle,
                                     float height, float bottom, int segmentsPerQuarter,
                                     int longitudeSegments, int latitudeSegments)
        {
            switch (shape)
            {
                case OccluderShape.Circle:
                    return CircleWall.BuildMesh(radius, thickness, angle, startAngle,
                                                height, bottom, segmentsPerQuarter);

                case OccluderShape.Dome:
                    return HemisphereDome.BuildMesh(radius, thickness, bottom,
                                                    longitudeSegments, latitudeSegments);

                default:
                    return BuildBoxMesh(size, center, bottom);
            }
        }

        /// <summary>一个封闭的立方体网格（法线朝外）。</summary>
        private static Mesh BuildBoxMesh(Vector3 size, Vector3 center, float bottom)
        {
            Vector3 h = new Vector3(Mathf.Max(0.001f, size.x),
                                    Mathf.Max(0.001f, size.y),
                                    Mathf.Max(0.001f, size.z)) * 0.5f;

            Vector3 c = new Vector3(center.x, center.y + bottom, center.z);

            var verts   = new List<Vector3>(24);
            var normals = new List<Vector3>(24);
            var tris    = new List<int>(36);

            // 每个面直接用"法线 + 两个切向 + 对应半长"描述，避免在循环里判断轴向出错。
            // quad 顶点顺序：( -t1 -t2 ), ( +t1 -t2 ), ( +t1 +t2 ), ( -t1 +t2 )
            // 绕序由"算出来的法线是否和 n 同向"自动决定，不依赖左右手约定。
            AddFace(verts, normals, tris, c, Vector3.right,
                    Vector3.up, Vector3.forward, h.y, h.z, h.x);

            AddFace(verts, normals, tris, c, Vector3.left,
                    Vector3.forward, Vector3.up, h.z, h.y, h.x);

            AddFace(verts, normals, tris, c, Vector3.up,
                    Vector3.forward, Vector3.right, h.z, h.x, h.y);

            AddFace(verts, normals, tris, c, Vector3.down,
                    Vector3.right, Vector3.forward, h.x, h.z, h.y);

            AddFace(verts, normals, tris, c, Vector3.forward,
                    Vector3.right, Vector3.up, h.x, h.y, h.z);

            AddFace(verts, normals, tris, c, Vector3.back,
                    Vector3.up, Vector3.right, h.y, h.x, h.z);

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 加一个面。面中心在 c + n * halfN，两条切向张成这个面。
        /// 绕序按"算出来的法线要和 n 同向"自动选，所以不用管左右手约定。
        /// </summary>
        private static void AddFace(List<Vector3> v, List<Vector3> normals, List<int> t,
                                    Vector3 c, Vector3 n,
                                    Vector3 t1, Vector3 t2,
                                    float half1, float half2, float halfN)
        {
            int b = v.Count;

            Vector3 nc = c + n * halfN;

            v.Add(nc - t1 * half1 - t2 * half2);
            v.Add(nc + t1 * half1 - t2 * half2);
            v.Add(nc + t1 * half1 + t2 * half2);
            v.Add(nc - t1 * half1 + t2 * half2);

            for (int i = 0; i < 4; i++) normals.Add(n);

            Vector3 got = Vector3.Cross(v[b + 1] - v[b + 0], v[b + 2] - v[b + 0]);

            if (Vector3.Dot(got, n) >= 0f)
            {
                t.Add(b + 0); t.Add(b + 1); t.Add(b + 2);
                t.Add(b + 0); t.Add(b + 2); t.Add(b + 3);
            }
            else
            {
                t.Add(b + 0); t.Add(b + 2); t.Add(b + 1);
                t.Add(b + 0); t.Add(b + 3); t.Add(b + 2);
            }
        }
    }
}
