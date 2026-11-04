using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 空心半球体（穹顶）。拖进场景后在 Inspector 里调半径和厚度即可。
    ///
    /// 形状：一个内外两层球面的壳，赤道在 <see cref="bottom"/> 高度，顶点在它上方 radius 处。
    /// 底边（赤道那一圈）会封口，所以看起来是一个有厚度的碗倒扣过来。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class HemisphereDome : MonoBehaviour
    {
        [Header("形状")]
        [Tooltip("内表面半径（米）。壳的实体在这个半径**之外**")]
        [Min(0.1f)] public float radius = 8f;

        [Tooltip("壳体厚度（米）")]
        [Min(0.01f)] public float thickness = 0.5f;

        [Header("位置")]
        [Tooltip("赤道所在高度（米）。0 = 贴地；想做成 墙+穹顶 就把这里设成墙的高度")]
        public float bottom = 4f;

        [Header("细分")]
        [Tooltip("经度方向分段数")]
        [Range(4, 128)] public int longitudeSegments = 48;

        [Tooltip("纬度方向分段数")]
        [Range(2, 48)] public int latitudeSegments = 16;

        [Header("渲染")]
        [Tooltip("勾上则内外两面都渲染（背面剔除关掉）。\n" +
                 "绕序不确定时勾上最保险，代价是背面光照不参与计算")]
        public bool doubleSided = true;

        [Tooltip("留空则用共用的灰色墙体材质")]
        public Material material;

        private MeshFilter   m_Filter;
        private MeshRenderer m_Renderer;
        private MeshCollider m_Collider;

        private static Material s_SharedMat;

        private static Material SharedMat
        {
            get
            {
                if (s_SharedMat == null) s_SharedMat = EchoMaterials.CreateWall(true);
                return s_SharedMat;
            }
        }

        /// <summary>组件第一次挂上去时补上碰撞体。</summary>
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

        [ContextMenu("重新生成穹顶")]
        public void Generate()
        {
            m_Filter   = GetComponent<MeshFilter>();
            m_Renderer = GetComponent<MeshRenderer>();

            Mesh mesh = BuildMesh(radius, thickness, bottom,
                                  longitudeSegments, latitudeSegments);
            mesh.name = "HemisphereDome";
            m_Filter.sharedMesh = mesh;

            m_Renderer.sharedMaterial    = material != null ? material : SharedMat;
            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            m_Renderer.receiveShadows    = true;

            // 碰撞体：声波射线要打在穹顶内表面上才能生成光带。
            m_Collider = GetComponent<MeshCollider>();
            if (m_Collider == null) m_Collider = gameObject.AddComponent<MeshCollider>();

            m_Collider.sharedMesh = null;      // 先清掉，强制 PhysX 重建加速结构
            m_Collider.sharedMesh = mesh;
            m_Collider.convex     = false;

            ApplyCullMode();
        }

        /// <summary>
        /// 双面渲染：用 MaterialPropertyBlock 设 _Cull，不改共用材质，
        /// 所以不会影响场景里别的墙体。
        /// </summary>
        private void ApplyCullMode()
        {
            if (m_Renderer == null) m_Renderer = GetComponent<MeshRenderer>();

            var block = new MaterialPropertyBlock();
            m_Renderer.GetPropertyBlock(block);
            block.SetFloat("_Cull", doubleSided ? 2f : 0f);   // 2 = Off, 0 = Back
            m_Renderer.SetPropertyBlock(block);
        }

        // ------------------------------------------------------------------ 网格生成

        /// <summary>生成空心半球的网格。静态方法，编辑器工具可以直接调用来预烘焙。</summary>
        public static Mesh BuildMesh(float radius, float thickness, float bottom,
                                     int longitudeSegments, int latitudeSegments)
        {
            radius    = Mathf.Max(0.05f, radius);
            thickness = Mathf.Max(0.001f, thickness);

            int lon = Mathf.Max(4, longitudeSegments);
            int lat = Mathf.Max(1, latitudeSegments);

            float rOuter = radius + thickness;

            var verts   = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris    = new List<int>();

            // ★ 朝向是**实测**出来的，不是我推导出来的 ——
            //   我按"球面法线应该是这个方向"推了四次，四次都推反了。
            //   最后靠诊断工具的对照实验（hitBackfaces 开 / 关）量清楚：
            //
            //     hitBackfaces=false 只报命中面朝射线来向的三角形。
            //     实测：绕序 (a,d,c)/(a,b,d) 在 hitBackfaces=false 下
            //           从球心朝外打能命中 -> 它才是"朝向房间"的那一套。
            //
            //   两个球壳都用这一套：内球壳负责挡住房间里的声波（真正反弹的那层），
            //   外球壳负责挡住夹层，免得有射线漏进去在两层之间来回弹。
            AddShell(verts, normals, tris, radius, bottom, lon, lat);   // 内球壳
            AddShell(verts, normals, tris, rOuter, bottom, lon, lat);   // 外球壳

            // 赤道那一圈的封口环
            AddEquatorRing(verts, normals, tris, radius, rOuter, bottom, lon);

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);          // 用我们算好的，不调 RecalculateNormals
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 一层球面。phi 从 0（顶点）到 90°（赤道）。
        ///
        /// 三角形绕序是**实测**定下来的（见 BuildMesh 里的说明），
        /// 顶点法线单独手工算，所以不调 RecalculateNormals。
        /// </summary>
        private static void AddShell(List<Vector3> v, List<Vector3> n, List<int> t,
                                     float r, float bottom, int lon, int lat)
        {
            int start = v.Count;

            for (int i = 0; i <= lat; i++)
            {
                float phi = (90f * i / lat) * Mathf.Deg2Rad;
                float rr  = Mathf.Cos(phi) * r;
                float yy  = bottom + Mathf.Sin(phi) * r;

                for (int j = 0; j <= lon; j++)
                {
                    float th = (360f * j / lon) * Mathf.Deg2Rad;
                    float cx = Mathf.Cos(th);
                    float cz = Mathf.Sin(th);

                    v.Add(new Vector3(cx * rr, yy, cz * rr));

                    // 球面法线就是径向：(cx*cosφ, sinφ, cz*cosφ)
                    // 朝**房间里面**（也就是朝球心），声波打上来时朝向观察者。
                    n.Add(new Vector3(-cx * Mathf.Cos(phi),
                                      -Mathf.Sin(phi),
                                      -cz * Mathf.Cos(phi)).normalized);
                }
            }

            int stride = lon + 1;

            // 单面：只铺一遍。
            //
            // ★ 不能铺正反两遍（我曾试过用双向铺法回避"绕序该朝哪边"的问题）。
            //   两遍会在完全相同的位置生成两组反向共面的三角形，PhysX 面对
            //   同一位置上两个朝向相反的三角形时选择是不确定的 ——
            //   实测表现正是"有的射线反弹了、有的穿到外壳和内壳之间来回弹"。
            for (int i = 0; i < lat; i++)
            {
                for (int j = 0; j < lon; j++)
                {
                    int a = start + i * stride + j;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;

                    t.Add(a); t.Add(d); t.Add(c);
                    t.Add(a); t.Add(b); t.Add(d);
                }
            }
        }

        /// <summary>赤道处的环形封口（把内外两层连起来）。法线朝下。</summary>
        private static void AddEquatorRing(List<Vector3> v, List<Vector3> n, List<int> t,
                                           float rInner, float rOuter, float y, int lon)
        {
            int start = v.Count;

            for (int j = 0; j <= lon; j++)
            {
                float th = (360f * j / lon) * Mathf.Deg2Rad;
                float cx = Mathf.Cos(th);
                float cz = Mathf.Sin(th);

                v.Add(new Vector3(cx * rInner, y, cz * rInner));
                v.Add(new Vector3(cx * rOuter, y, cz * rOuter));

                n.Add(Vector3.down);
                n.Add(Vector3.down);
            }

            for (int j = 0; j < lon; j++)
            {
                int b = start + j * 2;

                // 单面，朝下（和球壳同样的理由：不能铺两遍）
                t.Add(b + 0); t.Add(b + 3); t.Add(b + 2);
                t.Add(b + 0); t.Add(b + 1); t.Add(b + 3);
            }
        }
    }
}
