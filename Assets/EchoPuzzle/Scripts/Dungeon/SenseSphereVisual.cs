using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle.Dungeon
{
    /// <summary>
    /// 【地牢模式】变色球的红线可视化。
    ///
    /// ══════════════════════════════════════════════════════════════
    /// 效果
    ///   · 怪物周围有一个**球形范围**（半径 = PuzzleMonster.colorRadius）
    ///   · **光带波前**扫过这个球下方时，球内会出现一根根**红色竖线**
    ///     —— 每条竖线的 (x, z) = 波前被打中那一刻的 (x, z)
    ///   · 波前划过一截 -> 留下**一排竖线**，拼成一个面
    ///   · 波前横穿球下方 -> 拼成完整**横切面**，像球被切开
    ///
    /// ── 三个关键行为 ────────────────────────────────────────
    ///   1) **只有波前**能产生红线。
    ///      已经扫过去的轨迹不再产生 —— 所以红光总是跟着波前走。
    ///   2) 红线**生成后就固定在原地**，不跟着怪物动。
    ///      怪物移动时，旧红线留在生成时的位置慢慢淡出。
    ///   3) 每条红线**初始 alpha = 1**，在 <see cref="lineLifetime"/> 秒内
    ///      线性淡出到 0，然后移除。
    ///
    /// ── 为什么是"竖线" ─────────────────────────────────────
    ///   判定只看 xz：波前某个点 (x, z) 落进球内，
    ///   这一列在**球体内部**就是从球底贯到球顶的一条**竖直线段**
    ///   （和球面相交的那条弦），上下端正好抵在球面上。
    ///
    /// ══════════════════════════════════════════════════════════════
    /// 实现
    ///   · 一个持久列表 <see cref="m_Lines"/>，每项记录 { xz 位置, 生成时间 }
    ///   · 每帧从光带里取"波前位置"，落进球内的就**追加**一条新红线
    ///   · 每帧按出生时间算 alpha，过期的移除
    ///   · 全部竖线塞进同一个动态 Mesh（一根 = 一个朝向相机的细长 quad）
    /// ══════════════════════════════════════════════════════════════
    /// 注意：探秘模式仍然是"光带变色"（PuzzleMonster.overrideColor），
    ///       地牢模式由 PuzzleMonster 把这个开关关掉，改由这里画红线。
    /// ══════════════════════════════════════════════════════════════
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Echo Puzzle/Dungeon/Sense Sphere Visual")]
    public sealed class SenseSphereVisual : MonoBehaviour
    {
        // ---------------------------------------------------------------- 一条红线

        private struct Line
        {
            public Vector2 xz;          // 生成时的世界坐标 xz —— **固定不动**
            public float   birthTime;   // 生成时刻
        }

        // ---------------------------------------------------------------- 外观

        [Header("范围")]
        [Tooltip("球的半径。留 <= 0 表示跟随 PuzzleMonster.colorRadius")]
        public float radiusOverride = -1f;

        [Header("外观")]
        [Tooltip("红线的颜色（默认纯红）")]
        public Color lineColor = new Color(1f, 0f, 0f, 1f);

        [Tooltip("红线的粗细（米）")]
        [Min(0.001f)] public float lineWidth = 0.04f;

        [Tooltip("红线从生成到完全消失的时长（秒）。初始 alpha 固定为 1，线性淡到 0")]
        [Min(0.01f)] public float lineLifetime = 0.5f;

        [Tooltip("每条红线最多存多少根（性能上限）")]
        [Range(8, 1024)] public int maxLines = 512;

        [Tooltip("相邻两根红线的最小间距（米）。小于它就不重复生成")]
        [Min(0.001f)] public float minLineSpacing = 0.05f;

        // ---------------------------------------------------------------- 运行时

        private PuzzleMonster m_Monster;
        private TriggerVolume  m_Trigger;
        private SonarSystem    m_Sonar;

        /// <summary>线条外观：粗细 / 颜色（优先用 TriggerVolume 那一套，没有就用自身参数）。</summary>
        private float CurrentLineWidth
        {
            get
            {
                if (m_Trigger != null && m_Trigger.overrideWidth)
                    return Mathf.Max(0.001f, m_Trigger.waveWidth);

                return Mathf.Max(0.001f, lineWidth);
            }
        }

        private Color CurrentLineColor
        {
            get
            {
                if (m_Trigger != null && m_Trigger.overrideColor)
                    return m_Trigger.waveColor;

                return lineColor;
            }
        }

        /// <summary>不透明度系数：TriggerVolume 勾了 overrideAlpha 就用它的值。</summary>
        private float CurrentAlphaScale
        {
            get
            {
                if (m_Trigger != null && m_Trigger.overrideAlpha)
                    return Mathf.Clamp01(m_Trigger.waveAlpha);

                return 1f;
            }
        }

        private Transform    m_Root;
        private MeshFilter   m_Filter;
        private MeshRenderer m_Renderer;
        private Material     m_Mat;
        private Mesh         m_Mesh;

        private Vector3[] m_Verts;
        private Color[]   m_Colors;
        private Vector2[] m_Uvs;
        private int[]     m_Tris;

        /// <summary>所有还活着的红线（固定在世界坐标上）。</summary>
        private readonly List<Line> m_Lines = new List<Line>(512);

        /// <summary>本帧光带波前落在球内的位置。</summary>
        private readonly List<Vector2> m_Wavefronts = new List<Vector2>(64);

        private Camera m_Cam;

        // ---------------------------------------------------------------- 生命周期

        private void Awake()
        {
            try
            {
                m_Monster = GetComponent<PuzzleMonster>();
                if (m_Monster == null) m_Monster = GetComponentInParent<PuzzleMonster>();

                // ★ 也可以挂在 TriggerVolume 上（地牢模式让光带进入触发区时产生竖线）
                if (m_Monster == null)
                {
                    m_Trigger = GetComponent<TriggerVolume>();
                    if (m_Trigger == null) m_Trigger = GetComponentInParent<TriggerVolume>();
                }

                BuildMesh();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[地牢] SenseSphereVisual.Awake 出错：" + e);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (m_Mat != null) Destroy(m_Mat);
            if (m_Mesh != null) Destroy(m_Mesh);

            // 网格物体在场景根下，必须显式销毁
            if (m_Root != null) Destroy(m_Root.gameObject);
        }

        private void Update()
        {
            try
            {
                UpdateInternal();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[地牢] SenseSphereVisual.Update 出错：" + e);
                enabled = false;
            }
        }

        private void UpdateInternal()
        {
            if (m_Filter == null) return;
            if (m_Mat == null || m_Renderer == null) return;

            // 既不是怪物也不是触发区 -> 什么都不做
            if (m_Monster == null && m_Trigger == null) return;

            float r   = ResolveRadius();
            float now = Time.timeSinceLevelLoad;

            // ★ 根节点保持恒等变换（顶点按世界坐标写入）
            if (m_Root.position     != Vector3.zero)      m_Root.position   = Vector3.zero;
            if (m_Root.rotation     != Quaternion.identity) m_Root.rotation = Quaternion.identity;
            if (m_Root.localScale   != Vector3.one)       m_Root.localScale = Vector3.one;

            // ---- 1) 本帧的波前位置 ----
            m_Wavefronts.Clear();
            CollectWavefronts(r);

            // ---- 2) 每一条新波前 -> 生成一根**新的、固定在原地**的红线 ----
            SpawnLines(now);

            // ---- 3) 淘汰过期的红线 ----
            float life = Mathf.Max(0.01f, lineLifetime);

            for (int i = m_Lines.Count - 1; i >= 0; i--)
            {
                if (now - m_Lines[i].birthTime >= life)
                    m_Lines.RemoveAt(i);
            }

            if (m_Lines.Count == 0)
            {
                m_Renderer.enabled = false;
                return;
            }

            m_Renderer.enabled = true;

            // ---- 4) 重建几何 ----
            RebuildLines(r, now);
        }

        // ---------------------------------------------------------------- 数据收集

        /// <summary>区域半径：触发区用它的立方体半对角线（xz），怪物用 colorRadius。</summary>
        private float ResolveRadius()
        {
            if (radiusOverride > 0f) return radiusOverride;

            if (m_Monster != null) return m_Monster.colorRadius;

            if (m_Trigger != null)
            {
                // 触发区是立方体：取它在 xz 平面上的包围半径
                Bounds b = m_Trigger.WorldBounds;

                return Mathf.Max(0.01f, Mathf.Max(b.extents.x, b.extents.z));
            }

            return 2f;
        }

        private void CollectWavefronts(float radius)
        {
            if (m_Sonar == null) m_Sonar = FindObjectOfType<SonarSystem>();
            if (m_Sonar == null) return;

            var trailRenderer = m_Sonar.GetComponentInChildren<SonarTrailRenderer>(true);
            if (trailRenderer == null) return;

            // 被"删除"的对象不亮
            Hide h = GetComponent<Hide>();
            if (h == null) h = GetComponentInParent<Hide>();
            if (h != null && h.hiding != 0) return;

            // ★ 谁来过滤"只看潜行"：
            //     · 怪物的变色球  -> sneakOnly = true （只有潜行声波才有红线）
            //     · 触发区        -> sneakOnly = false（任何声波进入都产生竖线）
            bool sneakOnly = m_Monster != null;

            // ★ 判定区域形状：
            //     · 怪物的变色球  -> **圆**（球形）
            //     · 触发区        -> **方块**（TriggerVolume 是方形空间）
            bool useBox = m_Trigger != null;

            trailRenderer.CollectWavefrontsXZ(transform.position, radius,
                                              m_Wavefronts, sneakOnly, useBox);
        }

        /// <summary>
        /// 把本帧的波前位置变成**新红线**（固定在生成时的位置）。
        /// </summary>
        private void SpawnLines(float now)
        {
            float spacing2 = Mathf.Max(0.001f, minLineSpacing);
            spacing2 *= spacing2;

            for (int i = 0; i < m_Wavefronts.Count; i++)
            {
                if (m_Lines.Count >= maxLines) break;

                Vector2 p = m_Wavefronts[i];

                // 离已有的"刚生成的"红线太近就跳过，避免同一位置疯狂叠加
                bool tooClose = false;

                for (int k = m_Lines.Count - 1; k >= 0; k--)
                {
                    Line old = m_Lines[k];

                    // 只和"这一帧附近生成的"比较，老的不影响
                    if (now - old.birthTime > 0.15f) break;

                    if ((old.xz - p).sqrMagnitude < spacing2)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (tooClose) continue;

                m_Lines.Add(new Line { xz = p, birthTime = now });

                // 达到上限时淘汰**最老**的一条，保证新波前始终能留下痕迹
                if (m_Lines.Count >= maxLines)
                    m_Lines.RemoveAt(0);
            }
        }

        // ---------------------------------------------------------------- 竖线几何

        /// <summary>
        /// 按当前红线列表重建 Mesh。
        ///
        /// 一根红线 = 一个**朝向相机的细长 quad**，上下端抵在球面上：
        ///   在 (x, z) 处，球的竖直半弦长 h = sqrt(r² - d²)。
        ///
        /// ★ 位置用**红线自己记的 xz**（生成时固化的），不是怪物当前位置 ——
        ///   所以怪物移动时旧红线留在原地。
        /// ★ 球心用**怪物当前位置**：红线会被当前球体裁掉一部分，
        ///   看起来就是"球里的一根线"。
        /// </summary>
        private void RebuildLines(float radius, float now)
        {
            Vector3 sphereCenter = transform.position;
            Vector2 c = new Vector2(sphereCenter.x, sphereCenter.z);

            // 朝向相机的宽度方向
            if (m_Cam == null) m_Cam = Camera.main;

            Vector3 camPos = m_Cam != null ? m_Cam.transform.position : sphereCenter + Vector3.back;

            Vector3 flatToCam = camPos - sphereCenter;
            flatToCam.y = 0f;
            if (flatToCam.sqrMagnitude < 1e-6f) flatToCam = Vector3.back;
            flatToCam.Normalize();

            Vector3 widthDir = Vector3.Cross(Vector3.up, flatToCam).normalized;

            // ★ 线条外观：挂在 TriggerVolume 上时，
            //   粗细 / 颜色 / 不透明度**和该触发区修改光带的数值一致**。
            float halfW = CurrentLineWidth * 0.5f;
            float life  = Mathf.Max(0.01f, lineLifetime);
            float r2    = radius * radius;

            Color baseColor = CurrentLineColor;
            float alphaScale = CurrentAlphaScale;

            // 触发区的高度范围（怪物不用）
            float triggerHalfHeight = 1f;
            float triggerCenterY    = sphereCenter.y;

            if (m_Trigger != null)
            {
                Bounds b = m_Trigger.WorldBounds;

                triggerCenterY    = b.center.y;
                triggerHalfHeight = Mathf.Max(0.01f, b.extents.y);
            }

            int written = 0;
            int cap     = m_Verts.Length / 4;

            for (int i = 0; i < m_Lines.Count && written < cap; i++)
            {
                Line ln = m_Lines[i];

                // ---- 这条线还在不在区域内 ----
                //
                //   ★ 怪物变色球：**圆**，竖线高度用球的竖直弦长
                //   ★ 触发区：**方块**，竖线高度铺满触发区的 y 范围
                bool  useBox = m_Trigger != null;
                float halfH;

                if (useBox)
                {
                    Vector2 d = ln.xz - c;

                    if (Mathf.Abs(d.x) > radius || Mathf.Abs(d.y) > radius)
                        continue;                // 已经在方块外了 —— 不画

                    // 触发区是柱体：竖线铺满整个高度
                    halfH = triggerHalfHeight;
                }
                else
                {
                    float d2 = (ln.xz - c).sqrMagnitude;

                    if (d2 >= r2) continue;      // 已经在球外了 —— 不画

                    halfH = Mathf.Sqrt(r2 - d2);
                }

                // ---- alpha：初始 1，线性淡出到 0 ----
                float t01 = (now - ln.birthTime) / life;
                if (t01 >= 1f) continue;

                float a = Mathf.Clamp01(1f - t01) * alphaScale;

                Color col = baseColor;
                col.a *= a;

                Vector3 basePos = new Vector3(ln.xz.x, triggerCenterY, ln.xz.y);

                Vector3 bottom = basePos - Vector3.up * halfH;
                Vector3 top    = basePos + Vector3.up * halfH;

                int v = written * 4;

                m_Verts[v + 0] = bottom - widthDir * halfW;
                m_Verts[v + 1] = bottom + widthDir * halfW;
                m_Verts[v + 2] = top    + widthDir * halfW;
                m_Verts[v + 3] = top    - widthDir * halfW;

                m_Uvs[v + 0] = new Vector2(0f, 0f);
                m_Uvs[v + 1] = new Vector2(1f, 0f);
                m_Uvs[v + 2] = new Vector2(1f, 1f);
                m_Uvs[v + 3] = new Vector2(0f, 1f);

                m_Colors[v + 0] = col;
                m_Colors[v + 1] = col;
                m_Colors[v + 2] = col;
                m_Colors[v + 3] = col;

                int t = written * 6;

                m_Tris[t + 0] = v + 0; m_Tris[t + 1] = v + 2; m_Tris[t + 2] = v + 1;
                m_Tris[t + 3] = v + 0; m_Tris[t + 4] = v + 3; m_Tris[t + 5] = v + 2;

                written++;
            }

            m_Mesh.Clear();

            if (written <= 0) return;

            m_Mesh.SetVertices(m_Verts, 0, written * 4);
            m_Mesh.SetUVs(0, m_Uvs, 0, written * 4);
            m_Mesh.SetColors(m_Colors, 0, written * 4);
            m_Mesh.SetTriangles(m_Tris, 0, written * 6, 0);
            m_Mesh.RecalculateBounds();
        }

        // ---------------------------------------------------------------- 网格 / 材质

        private void BuildMesh()
        {
            int cap = Mathf.Clamp(maxLines, 8, 1024);

            m_Verts  = new Vector3[cap * 4];
            m_Colors = new Color[cap * 4];
            m_Uvs    = new Vector2[cap * 4];
            m_Tris   = new int[cap * 6];

            m_Mesh = new Mesh { name = "SenseSphereLines" };
            m_Mesh.MarkDynamic();

            // ★★ 网格物体不做任何物体的子物体 ★★
            //
            //   顶点按**世界坐标**写入，所以这个 Transform 必须一直是恒等的
            //   （position = 0、rotation = 单位、scale = 1）。
            //   挂在怪物下面时，父级的变换会把世界坐标再乘一遍，
            //   表现为"竖线飞到离怪物很远的地方，而且越跑越远"。
            var go = new GameObject("[变色球红线]");

            // 纯视觉的运行时物体，不要被 WorldSnapshot 记录 / 还原
            go.hideFlags = HideFlags.DontSave;

            go.transform.SetParent(null, false);
            go.transform.position   = Vector3.zero;
            go.transform.rotation   = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            m_Root = go.transform;

            m_Filter = go.AddComponent<MeshFilter>();
            m_Filter.sharedMesh = m_Mesh;

            m_Renderer = go.AddComponent<MeshRenderer>();
            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Renderer.receiveShadows    = false;

            m_Mat = MakeMaterial();

            if (m_Mat == null)
            {
                m_Renderer.enabled = false;
                return;
            }

            m_Renderer.sharedMaterial = m_Mat;
            m_Renderer.enabled = false;
        }

        /// <summary>支持顶点色的透明材质（URP）。</summary>
        private static Material MakeMaterial()
        {
            // ★ 必须用**URP 兼容**且**支持顶点色**的 shader。
            //   内置管线的 Sprites/Default 等在 URP 下 Shader.Find 返回 null，
            //   new Material(null) 会抛异常。
            Shader sh = Shader.Find("EchoPuzzle/VertexColor");

            if (sh == null)
            {
                Debug.LogError("[地牢] 找不到 shader EchoPuzzle/VertexColor，" +
                               "变色球红线无法显示。请确认 " +
                               "Assets/EchoPuzzle/Shaders/EchoVertexColor.shader 存在。");
                return null;
            }

            var mat = new Material(sh)
            {
                name      = "SenseSphereLineColor",
                hideFlags = HideFlags.DontSave,
            };

            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            return mat;
        }
    }
}
