using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 三角斜坡（直角三角柱）。三个面：
    ///   · 底面  —— 与水平面平行（贴在 <see cref="bottom"/> 高度上）
    ///   · 背面  —— 与水平面垂直（在 X = <see cref="length"/> 那一侧）
    ///   · 坡面  —— 斜面，从原点斜着升到背面顶端
    ///
    /// 朝向：沿 **+X 方向上坡**，宽度沿 **Z 方向**。
    ///       物体自身的位置就是这个斜坡的**原点**（左下角、贴地那一点）。
    ///
    /// 尺寸：<see cref="length"/> 水平长度、<see cref="height"/> 高度、
    ///       <see cref="width"/> 坡面宽度（Z 方向）。
    ///       坡面长度 = √(length² + height²)，坡度 = atan(height / length)。
    ///
    /// 顶点是手工按"法线必须朝外"算的，所以不调 RecalculateNormals，
    /// 光照不会因为绕序问题变脏。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SlopeRamp : MonoBehaviour
    {
        [Header("尺寸（米）")]
        [Tooltip("水平长度（X 方向），也就是坡的水平投影")]
        [Min(0.01f)] public float length = 8f;

        [Tooltip("高度（Y 方向），坡顶相对坡底的高度")]
        [Min(0.01f)] public float height = 6f;

        [Tooltip("坡面宽度（Z 方向）")]
        [Min(0.01f)] public float width = 4f;

        [Header("位置")]
        [Tooltip("整个坡的基准高度。0 = 坡底贴地")]
        public float bottom = 0f;

        [Header("渲染")]
        [Tooltip("留空则回落到共用的灰色墙体材质")]
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

        /// <summary>坡度角（度）。</summary>
        public float SlopeAngleDeg => Mathf.Atan2(height, length) * Mathf.Rad2Deg;

        /// <summary>坡面实际长度。</summary>
        public float SlopeLength => Mathf.Sqrt(length * length + height * height);

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

        [ContextMenu("重新生成斜坡")]
        public void Generate()
        {
            m_Filter   = GetComponent<MeshFilter>();
            m_Renderer = GetComponent<MeshRenderer>();

            Mesh mesh = BuildMesh(length, height, width, bottom);
            mesh.name = "SlopeRamp";
            m_Filter.sharedMesh = mesh;

            m_Renderer.sharedMaterial    = material != null ? material : SharedMat;
            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            m_Renderer.receiveShadows    = true;

            // 声波射线要打在这个坡面上才能生成光带
            m_Collider = GetComponent<MeshCollider>();
            if (m_Collider == null) m_Collider = gameObject.AddComponent<MeshCollider>();

            m_Collider.sharedMesh = null;      // 先清掉，强制 PhysX 重建加速结构
            m_Collider.sharedMesh = mesh;
            m_Collider.convex     = false;
        }

        // ------------------------------------------------------------------ 网格生成

        /// <summary>生成三角斜坡网格。静态方法，编辑器工具可直接调用来预烘焙。</summary>
        public static Mesh BuildMesh(float length, float height, float width, float bottom)
        {
            length = Mathf.Max(0.01f, length);
            height = Mathf.Max(0.01f, height);
            width  = Mathf.Max(0.01f, width);

            float hw = width * 0.5f;
            float y0 = bottom;
            float y1 = bottom + height;

            // 截面三个角（XY 平面）：
            //   A = (0,  y0)  左下（坡底）
            //   B = (L,  y0)  右下（背面脚下）
            //   C = (L,  y1)  右上（坡顶）
            //
            // 两个端面分别在 Z = -hw 和 Z = +hw
            var A0 = new Vector3(0f, y0, -hw);
            var B0 = new Vector3(length, y0, -hw);
            var C0 = new Vector3(length, y1, -hw);
            var A1 = new Vector3(0f, y0, +hw);
            var B1 = new Vector3(length, y0, +hw);
            var C1 = new Vector3(length, y1, +hw);

            var verts   = new List<Vector3>(18);
            var normals = new List<Vector3>(18);
            var tris    = new List<int>(24);

            // 每个面都用"先算出期望法线，再按它定绕序"的方式铺 ——
            // 这样不会因为坐标系的左右手约定把某个面铺反。
            //
            // 底面：与水平面平行，法线朝下
            AddQuad(verts, normals, tris, A0, B0, B1, A1, Vector3.down);

            // 背面：与水平面垂直（在 X = length 一侧），法线朝 +X
            AddQuad(verts, normals, tris, B0, C0, C1, B1, Vector3.right);

            // 坡面：从坡底 A 斜升到坡顶 C，法线朝斜上方、背离斜坡体
            Vector3 slopeN = new Vector3(-height, length, 0f).normalized;
            AddQuad(verts, normals, tris, A0, C0, C1, A1, slopeN);

            // 两个端面（直角三角形的竖截面）
            AddTriangle(verts, normals, tris, A0, B0, C0, Vector3.back);
            AddTriangle(verts, normals, tris, A1, B1, C1, Vector3.forward);

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>铺一个四边形，法线固定为 wantNormal（自动选绕序保证正面朝那里）。</summary>
        private static void AddQuad(List<Vector3> v, List<Vector3> n, List<int> t,
                                    Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                                    Vector3 wantNormal)
        {
            int b = v.Count;

            v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
            for (int i = 0; i < 4; i++) n.Add(wantNormal);

            // 先按一个方向铺，算出来的法线和期望相反就翻过来
            Vector3 got = Vector3.Cross(p1 - p0, p2 - p0);

            if (Vector3.Dot(got, wantNormal) >= 0f)
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

        /// <summary>铺一个三角形，法线固定为 wantNormal。</summary>
        private static void AddTriangle(List<Vector3> v, List<Vector3> n, List<int> t,
                                        Vector3 p0, Vector3 p1, Vector3 p2,
                                        Vector3 wantNormal)
        {
            int b = v.Count;

            v.Add(p0); v.Add(p1); v.Add(p2);
            for (int i = 0; i < 3; i++) n.Add(wantNormal);

            Vector3 got = Vector3.Cross(p1 - p0, p2 - p0);

            if (Vector3.Dot(got, wantNormal) >= 0f)
            {
                t.Add(b + 0); t.Add(b + 1); t.Add(b + 2);
            }
            else
            {
                t.Add(b + 0); t.Add(b + 2); t.Add(b + 1);
            }
        }
    }
}
