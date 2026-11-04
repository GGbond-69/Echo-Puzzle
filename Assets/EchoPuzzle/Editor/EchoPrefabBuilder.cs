using System.Collections.Generic;
using System.IO;
using EchoPuzzle;
using UnityEditor;
using UnityEngine;

namespace EchoPuzzle.EditorTools
{
    /// <summary>
    /// 一键生成可拖拽的 Prefab。
    ///
    /// 菜单：EchoPuzzle / 生成可拖拽的 Prefab
    /// 产出在 Assets/EchoPuzzle/Prefabs/ 下，直接从「项目」面板拖进场景即可。
    /// </summary>
    public static class EchoPrefabBuilder
    {
        private const string PrefabFolder = "Assets/EchoPuzzle/Prefabs";

        [MenuItem("EchoPuzzle/生成可拖拽的 Prefab", false, 0)]
        public static void BuildPrefabs()
        {
            EnsureFolder();

            MakeCircleWall();
            MakeDome();
            MakeSlopeRamp();
            MakeWallOccluder();
            MakeTriggerBox();
            MakeHintZone();
            MakePlayer();
            MakeSonarSystem();
            MakePuzzleMonster();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("EchoPuzzle",
                "Prefab 已生成到：\nAssets/EchoPuzzle/Prefabs/\n\n" +
                "里面有九个：\n" +
                "  CircleWall     圆形墙壁（可设半径/厚度/角度）\n" +
                "  HemisphereDome 空心半球\n" +
                "  SlopeRamp      三角斜坡（可设长度/高度/坡面宽度）\n" +
                "  WallOccluder   完全不透光的遮挡墙（透明模式下也挡视线）\n" +
                "  TriggerVolume  不可见触发立方体（出现/消失/通关/失败）\n" +
                "  HintZone       提示文字区域（有淡入淡出、可响应玩家操作）\n" +
                "  Player         玩家 + 相机 + 标记 + 脚印\n" +
                "  SonarSystem    声波系统（光带渲染器 + 调试开关）\n" +
                "  PuzzleMonster  怪物（不可见实体 + 感知圆柱 + 变色球 + 怪物光带）\n\n" +
                "★ SonarSystem 请**拖进场景**（放在世界原点即可），不要挂到 Player 下面 ——\n" +
                "  光带网格用的是世界坐标，跟着玩家旋转会让整条光带错位。\n\n" +
                "直接从「项目」面板把它们拖进 Scene 即可。",
                "好");

            var player = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/Player.prefab");
            if (player != null) EditorGUIUtility.PingObject(player);
        }

        // ------------------------------------------------------------------ 各个 Prefab

        private static void MakeCircleWall()
        {
            var go = new GameObject("CircleWall");
            var wall = go.AddComponent<CircleWall>();

            // 默认参数：半径 8、厚 1、整圈、高 4
            wall.radius              = 8f;
            wall.thickness           = 1f;
            wall.angle               = 360f;
            wall.startAngle          = 0f;
            wall.height              = 4f;
            wall.bottom              = 0f;
            wall.segmentsPerQuarter  = 16;

            // 声波射线必须打在这面墙上才能生成光带
            go.AddComponent<MeshCollider>();

            Save(go, "CircleWall");
        }

        private static void MakeSlopeRamp()
        {
            var go = new GameObject("SlopeRamp");

            // RequireComponent 会自动补上 MeshFilter / MeshRenderer
            var ramp = go.AddComponent<SlopeRamp>();

            ramp.length = 8f;
            ramp.height = 6f;
            ramp.width  = 4f;
            ramp.bottom = 0f;

            // 声波射线要打在坡面上才能生成光带
            go.AddComponent<MeshCollider>();

            Save(go, "SlopeRamp");
        }

        private static void MakeDome()
        {
            var go = new GameObject("HemisphereDome");
            var dome = go.AddComponent<HemisphereDome>();

            dome.radius            = 8f;
            dome.thickness         = 0.5f;
            dome.bottom            = 4f;
            dome.longitudeSegments = 48;
            dome.latitudeSegments  = 16;
            dome.doubleSided       = true;   // 内外两面都渲染，绕序问题就不会再让某一面消失

            // 声波射线必须打在穹顶内表面上才能生成光带
            go.AddComponent<MeshCollider>();

            Save(go, "HemisphereDome");
        }

        private static void MakeWallOccluder()
        {
            var go = new GameObject("WallOccluder");

            var occ = go.AddComponent<WallOccluder>();
            occ.shape  = OccluderShape.Box;
            occ.size   = new Vector3(4f, 4f, 1f);
            occ.center = Vector3.zero;
            occ.bottom = 0f;

            // 声波射线要打在它上面才会被挡住
            go.AddComponent<MeshCollider>();

            Save(go, "WallOccluder");
        }

        private static void MakeTriggerBox()        {
            var go = new GameObject("TriggerVolume");

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size      = Vector3.one;

            var vol = go.AddComponent<TriggerVolume>();
            vol.effect         = TriggerEffect.None;
            vol.overrideColor  = true;
            vol.waveColor      = new Color(0.35f, 0.85f, 1f);
            vol.overrideAlpha  = true;
            vol.waveAlpha      = 1f;
            vol.useBoundsCheck = true;
            vol.destroySelfAfterTrigger = true;

            Save(go, "TriggerVolume");
        }

        private static void MakeHintZone()
        {
            var go = new GameObject("HintZone");

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size      = new Vector3(4f, 4f, 4f);

            var hint = go.AddComponent<HintZone>();
            hint.triggerOnce     = false;
            hint.useBoundsCheck  = true;

            // 默认给一重"进入区域就显示"的文本
            hint.entries = new System.Collections.Generic.List<HintEntry>
            {
                new HintEntry
                {
                    text    = "在这里写提示文字",
                    trigger = HintTriggerKind.EnterZone,
                    delay   = 0f,
                },
            };

            Save(go, "HintZone");
        }

        private static void MakePlayer()
        {
            var go = new GameObject("Player");

            // ---- 胶囊碰撞体 ----
            var cc = go.AddComponent<CharacterController>();
            cc.height          = 1.8f;
            cc.radius          = 0.34f;
            cc.center          = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit      = 55f;
            cc.stepOffset      = 0.4f;
            cc.skinWidth       = 0.03f;
            cc.minMoveDistance = 0f;

            // ---- 身体：只显示发光轮廓 ----
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale    = new Vector3(0.68f, 0.9f, 0.68f);
            Object.DestroyImmediate(body.GetComponent<Collider>());

            Material outline = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/EchoPuzzle/Materials/EchoOutline.mat");
            if (outline == null)
            {
                outline = EchoMaterials.CreateOutline();
                if (outline != null)
                {
                    AssetDatabase.CreateAsset(outline, "Assets/EchoPuzzle/Materials/EchoOutline.mat");
                    AssetDatabase.SaveAssets();
                    outline = AssetDatabase.LoadAssetAtPath<Material>(
                        "Assets/EchoPuzzle/Materials/EchoOutline.mat");
                }
            }
            if (outline != null) body.GetComponent<MeshRenderer>().sharedMaterial = outline;

            // ---- 玩家控制 ----
            go.AddComponent<PlayerController>();

            Save(go, "Player");
        }

        // ------------------------------------------------------------------ 声波系统

        /// <summary>
        /// 声波系统 Prefab：一个 SonarSystem 根物体 + 一个 SonarTrailRenderer 子物体。
        ///
        /// ★ 必须放在**世界原点**、而且是**独立根物体**，不能挂到 Player 下面。
        ///
        ///   原因：SonarTrailRenderer 是直接把世界坐标写进网格顶点的
        ///   （`m_Verts[v] = origin + dir * t`，origin 来自 hit.point）。
        ///   网格顶点会被渲染器的 transform 再变换一次，所以渲染器的 transform
        ///   必须是**单位变换**，否则光带会整体偏移/旋转。
        ///   而 Player 会随移动方向旋转（HandleMove 里的 Slerp），
        ///   挂在它下面的话，玩家一转，所有光带就跟着转 —— 位置全错。
        ///
        /// 放到场景里的好处：
        ///   · 调试开关（角度检测 / 碰撞检测）位置固定，不用每次 Play 去层级里找
        ///   · 光带渲染器参数能被保存下来
        ///   · 不会再每次开新局都新建一个
        /// </summary>
        private static void MakeSonarSystem()
        {
            var go = new GameObject("SonarSystem");
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var rendererGo = new GameObject("SonarTrailRenderer");
            rendererGo.transform.SetParent(go.transform, false);
            rendererGo.transform.localPosition = Vector3.zero;
            rendererGo.transform.localRotation = Quaternion.identity;
            rendererGo.transform.localScale = Vector3.one;

            var trail = rendererGo.AddComponent<SonarTrailRenderer>();

            // 引用材质**资产**，不要用 Shader.Find ——
            // 没有资产引用这个 shader 的话打包时会被剔除，exe 里光带就不显示了。
            var trailMat = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/EchoPuzzle/Resources/EchoRuntime/EchoSonarTrail.mat");
            if (trailMat != null) trail.material = trailMat;
            else Debug.LogWarning("[EchoPuzzle] 找不到 EchoSonarTrail.mat，" +
                                  "请先执行菜单：EchoPuzzle → 生成运行时材质资产");

            var sonar = go.AddComponent<SonarSystem>();
            sonar.trailRenderer = trail;

            Save(go, "SonarSystem");
        }

        // ------------------------------------------------------------------ 怪物

        /// <summary>
        /// 怪物 Prefab：一个不可见的球 + CharacterController（被墙阻挡）+ 怪物光带渲染器。
        ///
        /// 实体的可见性：要求是"不可见"。所以这里**不挂 MeshRenderer**，
        /// 只留 CharacterController —— 玩家看不见它，只能靠它甩出来的光带判断位置。
        /// </summary>
        private static void MakePuzzleMonster()
        {
            var go = new GameObject("PuzzleMonster");
            go.transform.position = Vector3.zero;

            // 实体：CharacterController 负责被墙体阻挡
            var cc = go.AddComponent<CharacterController>();
            cc.radius     = 0.5f;
            cc.height     = 1f;
            cc.center     = Vector3.zero;
            cc.slopeLimit = 45f;
            cc.stepOffset = 0f;

            // 光带渲染器作为子物体（单位变换，和声波系统同样的要求）
            var trailGo = new GameObject("MonsterTrailRenderer");
            trailGo.transform.SetParent(go.transform, false);
            trailGo.transform.localPosition = Vector3.zero;
            trailGo.transform.localRotation = Quaternion.identity;
            trailGo.transform.localScale    = Vector3.one;

            var trail = trailGo.AddComponent<MonsterTrailRenderer>();

            var mat = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/EchoPuzzle/Resources/EchoRuntime/EchoSonarTrail.mat");
            if (mat != null) trail.material = mat;
            else Debug.LogWarning("[EchoPuzzle] 找不到 EchoSonarTrail.mat，" +
                                  "请先执行菜单：EchoPuzzle → 生成运行时材质资产");

            var monster = go.AddComponent<PuzzleMonster>();
            monster.trailRenderer = trail;

            Save(go, "PuzzleMonster");
        }

        // ------------------------------------------------------------------ 工具

        /// <summary>
        /// 保存一个新 Prefab，但**绝不覆盖已存在的**。
        ///
        /// ★ 这一点非常关键。
        ///
        ///   原来这里是 `PrefabUtility.SaveAsPrefabAsset(temp, path)` —— 直接覆盖。
        ///   而 temp 是"用代码默认值新建的空物体"，于是每跑一次这个菜单，
        ///   你就丢掉了一次在 Inspector 里调过的所有数值
        ///   （光带数量、相机缩放范围、各种速度……）。
        ///
        ///   现在改成：目标已存在就**跳过**，只在 Console 提示一句。
        ///   想重建某个 Prefab，请先手动删掉它，再跑菜单。
        ///
        /// 返回值：true = 新建了；false = 已存在，跳过。
        /// </summary>
        private static bool Save(GameObject temp, string name)
        {
            string path = $"{PrefabFolder}/{name}.prefab";

            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                Object.DestroyImmediate(temp);

                Debug.Log($"[EchoPuzzle] 已跳过 {path}：文件已存在，保留你原来的数值。\n" +
                          "  （想按代码默认值重建，请先手动删掉这个 .prefab，再跑菜单）");
                return false;
            }

            PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);

            Debug.Log($"[EchoPuzzle] 已生成 {path}");
            return true;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/EchoPuzzle"))
                AssetDatabase.CreateFolder("Assets", "EchoPuzzle");

            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder("Assets/EchoPuzzle", "Prefabs");
        }
    }
}
