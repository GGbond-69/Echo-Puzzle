using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 一段圆弧形墙壁。可以直接拖进场景，在 Inspector 里调参数。
    ///
    /// 弧长算法：0° 在 **+X 方向**，角度沿 **+Z（北）** 方向增加。
    ///   输入 45  -> 生成 1/8 个圆
    ///   输入 180 -> 生成半圆
    ///   输入 360 -> 生成完整的圆环
    ///
    /// 厚度向**外侧**长：内表面半径 = <see cref="radius"/>，外表面半径 = radius + thickness。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class CircleWall : MonoBehaviour
    {
        [Header("形状")]
        [Tooltip("内表面半径（米）。墙的实体在这个半径**之外**")]
        [Min(0.1f)] public float radius = 8f;

        [Tooltip("墙厚（米）。0 = 单面墙（没有厚度）")]
        [Min(0f)] public float thickness = 1f;

        [Tooltip("圆弧张角（度）。45 = 八分之一圆，180 = 半圆，360 = 整圈")]
        [Range(1f, 360f)] public float angle = 360f;

        [Tooltip("起始角度（度）。0 = +X 方向，90 = +Z（北）方向")]
        public float startAngle = 0f;

        [Header("尺寸")]
        [Tooltip("墙高（米）")]
        [Min(0.1f)] public float height = 4f;

        [Tooltip("底面高度（米）。0 = 贴地")]
        public float bottom = 0f;

        [Header("细分")]
        [Tooltip("每 90° 分成多少段。越大越圆滑，也越费性能")]
        [Range(2, 128)] public int segmentsPerQuarter = 16;

        [Header("渲染")]
        [Tooltip("留空则用共用的灰色墙体材质")]
        public Material material;

        private MeshFilter   m_Filter;
        private MeshRenderer m_Renderer;
        private MeshCollider m_Collider;

        // 多个墙体共用一份材质，避免每次重建都漏一个 Material
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

        private void Awake()   => Generate();
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

        /// <summary>重新生成网格。改了参数想立刻生效时可以手动调用。</summary>
        [ContextMenu("重新生成墙体")]
        public void Generate()
        {
            m_Filter   = GetComponent<MeshFilter>();
            m_Renderer = GetComponent<MeshRenderer>();

            Mesh mesh = BuildMesh(radius, thickness, angle, startAngle, height, bottom,
                                  segmentsPerQuarter);
            mesh.name = "CircleWall";
            m_Filter.sharedMesh = mesh;

            m_Renderer.sharedMaterial    = material != null ? material : SharedMat;
            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            m_Renderer.receiveShadows    = true;

            // 碰撞体：声波射线要打在这面墙上才能生成光带。
            // 少了这个，光带会直接穿墙而过（原来就是这个毛病）。
            m_Collider = GetComponent<MeshCollider>();
            if (m_Collider == null) m_Collider = gameObject.AddComponent<MeshCollider>();

            m_Collider.sharedMesh = null;      // 先清掉，强制 PhysX 重建加速结构
            m_Collider.sharedMesh = mesh;
            m_Collider.convex     = false;
        }

        // ------------------------------------------------------------------ 网格生成

        /// <summary>
        /// 生成一段圆弧墙的网格。静态方法，编辑器工具也可以直接调用来预烘焙网格。
        /// </summary>
        public static Mesh BuildMesh(float radius, float thickness, float angle,
                                     float startAngle, float height, float bottom,
                                     int segmentsPerQuarter)
        {
            angle = Mathf.Clamp(angle, 1f, 360f);
            radius = Mathf.Max(0.05f, radius);
            thickness = Mathf.Max(0f, thickness);

            int segs = Mathf.Max(2,
                Mathf.CeilToInt(segmentsPerQuarter * (angle / 90f)));

            float outer = radius + thickness;
            float y0 = bottom;
            float y1 = bottom + Mathf.Max(0.05f, height);

            var verts = new List<Vector3>();
            var tris  = new List<int>();

            // ---- 内外两个圆柱面 ----
            // 内表面：法线指向圆心
            AddCylinderBand(verts, tris, radius, y0, y1, startAngle, angle, segs, true);
            // 外表面：法线朝外
            if (thickness > 0.0001f)
                AddCylinderBand(verts, tris, outer, y0, y1, startAngle, angle, segs, false);

            bool fullCircle = angle >= 359.9f;

            if (thickness > 0.0001f)
            {
                // ---- 顶面 / 底面：环形带 ----
                AddRingBand(verts, tris, radius, outer, y1, startAngle, angle, segs, true);
                AddRingBand(verts, tris, radius, outer, y0, startAngle, angle, segs, false);
            }

            if (!fullCircle)
            {
                // ---- 两端封口 ----
                AddEndCap(verts, tris, radius, outer, y0, y1, startAngle);
                AddEndCap(verts, tris, radius, outer, y0, y1, startAngle + angle);
            }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddCylinderBand(List<Vector3> v, List<int> t,
                                            float r, float y0, float y1,
                                            float startDeg, float sweepDeg, int segs,
                                            bool inward)
        {
            int start = v.Count;

            for (int i = 0; i <= segs; i++)
            {
                float a = (startDeg + sweepDeg * i / segs) * Mathf.Deg2Rad;
                float cx = Mathf.Cos(a) * r;
                float cz = Mathf.Sin(a) * r;

                v.Add(new Vector3(cx, y0, cz));
                v.Add(new Vector3(cx, y1, cz));
            }

            for (int i = 0; i < segs; i++)
            {
                int b = start + i * 2;
                if (inward)
                {
                    t.Add(b + 0); t.Add(b + 2); t.Add(b + 3);
                    t.Add(b + 0); t.Add(b + 3); t.Add(b + 1);
                }
                else
                {
                    t.Add(b + 0); t.Add(b + 1); t.Add(b + 3);
                    t.Add(b + 0); t.Add(b + 3); t.Add(b + 2);
                }
            }
        }

        private static void AddRingBand(List<Vector3> v, List<int> t,
                                        float rInner, float rOuter, float y,
                                        float startDeg, float sweepDeg, int segs, bool up)
        {
            int start = v.Count;

            for (int i = 0; i <= segs; i++)
            {
                float a = (startDeg + sweepDeg * i / segs) * Mathf.Deg2Rad;
                float cx = Mathf.Cos(a);
                float cz = Mathf.Sin(a);

                v.Add(new Vector3(cx * rInner, y, cz * rInner));
                v.Add(new Vector3(cx * rOuter, y, cz * rOuter));
            }

            for (int i = 0; i < segs; i++)
            {
                int b = start + i * 2;
                if (up)
                {
                    t.Add(b + 0); t.Add(b + 2); t.Add(b + 3);
                    t.Add(b + 0); t.Add(b + 3); t.Add(b + 1);
                }
                else
                {
                    t.Add(b + 0); t.Add(b + 3); t.Add(b + 2);
                    t.Add(b + 0); t.Add(b + 1); t.Add(b + 3);
                }
            }
        }

        private static void AddEndCap(List<Vector3> v, List<int> t,
                                      float rInner, float rOuter,
                                      float y0, float y1, float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            float dx = Mathf.Cos(a), dz = Mathf.Sin(a);

            int b = v.Count;
            v.Add(new Vector3(dx * rInner, y0, dz * rInner));
            v.Add(new Vector3(dx * rOuter, y0, dz * rOuter));
            v.Add(new Vector3(dx * rOuter, y1, dz * rOuter));
            v.Add(new Vector3(dx * rInner, y1, dz * rInner));

            t.Add(b + 0); t.Add(b + 1); t.Add(b + 2);
            t.Add(b + 0); t.Add(b + 2); t.Add(b + 3);
            t.Add(b + 0); t.Add(b + 2); t.Add(b + 1);
            t.Add(b + 0); t.Add(b + 3); t.Add(b + 2);
        }
    }
}
