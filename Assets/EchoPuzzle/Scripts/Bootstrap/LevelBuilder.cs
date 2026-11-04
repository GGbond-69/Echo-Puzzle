using System.Collections.Generic;
using UnityEngine;

namespace EchoPuzzle
{
    /// <summary>
    /// 关卡：按 AutoCAD 图纸的实测尺寸搭建。
    ///
    /// 坐标方向（Unity 标准 —— **X 右 / Y 上 / Z 前**）：
    ///   X = 东（图纸向右）
    ///   Z = 北（图纸向上）
    ///   Y = 高度（地面 Y=0）
    /// （之前我把高度放在 Z 上，导致地图"立起来"了，现已改正。）
    ///
    /// 尺寸来源：
    ///   走廊净宽 4m，墙厚 1m（**向走廊外侧长**，不占走廊宽度）
    ///   走廊净高 4m
    ///   圆形大房间：内半径 8m，环形墙到 Y=4，上面接半径 8m 的半球穹顶
    ///   机关门中心线：距圆心 2√15 + 8 ≈ 15.746m
    ///     （圆房间出口在 X=8，所以出口→门 = 7.746m ≈ 图上第一个 8；门→斜坡 = 3m）
    ///   斜坡：3-4-5 直角三角形放大，水平 8m、高 6m、坡长 10m，从西往东是上坡
    /// </summary>
    public static class LevelBuilder
    {
        // ------------------------------------------------------------------ 尺寸常量

        public const float CorridorWidth = 4f;
        public const float WallThick     = 1f;
        public const float Ceiling       = 4f;
        public const float RoomRadius    = 8f;

        /// <summary>机关门中心线到圆心的距离：2√15 + 8 ≈ 15.7459</summary>
        public const float GateCenterX = 15.7459667f;
        public const float GateThick   = 1f;

        public const float SlopeStartX = 19.5f;                   // 机关门之后 3m
        public const float SlopeRun    = 8f;
        public const float SlopeRise   = 6f;
        public const float SlopeEndX   = SlopeStartX + SlopeRun;  // 27.5

        public const float UpperY = SlopeRise;                    // 上层地面高度 = 6

        // 上层回字形的关键坐标
        private const float UpperMainZ0 = -2f;    // 上层主干南墙内侧
        private const float UpperMainZ1 = 2f;
        private const float UpperEastX  = 45f;    // 主干东端（之后接竖直走廊）
        private const float UpperVertX1 = 49f;    // 竖直走廊东墙内侧
        private const float LowerZ0     = 10f;    // 下层走廊南墙内侧
        private const float LowerZ1     = 14f;
        private const float LowerWestX  = 43f;    // 下层走廊西端
        private const float LowerEastX  = 59f;

        // 机关空间 b 的凹室（挖在下层走廊南侧）
        private const float MechBX0 = 37f;
        private const float MechBX1 = 41f;
        private const float MechBZ0 = 6f;

        // 圆房间的三个门洞（水平面上的极角，0° = +X 东，90° = +Z 北）
        private const float DoorEastAngle  = 0f;
        private const float DoorSouthAngle = 270f;
        private const float DoorEastHalf   = 15f;
        private const float DoorSouthHalf  = 16f;

        // 斜向走廊（45°）
        private const float DiagLength    = 6f;
        private const float DiagDoorAngle = 45f;
        private const float DiagDoorHalf  = 16f;

        private const int RingSegments = 128;

        // ------------------------------------------------------------------ 入口

        public static Vector3 Build(Transform root, Material mat, Material floorMat,
                                    out Vector3 goalPosition, out Transform gateOut)
        {
            // 测试期：地板/天花板跟随墙体材质（都不透明）。
            // 等路径确认无误，再把 EchoBootstrap 里的地板换成 CreateInvisible()。
            Material flat = floorMat != null ? floorMat : mat;

            // ============ 1. 圆形大房间：环形墙 + 半球穹顶 + 圆形地板 ============
            BuildCircularRoom(root, mat);

            // ============ 2. 出生点走廊（房间正南方，门洞在 270°） ============
            // 净空 X∈[-2,2]，Z∈[-23,-8]
            Corridor(root, mat, flat, -2f, 2f, -23f, -8f);
            Cap(root, mat, 0f, -23f - WallThick * 0.5f,
                CorridorWidth + WallThick * 2f, WallThick, 0f);

            // ============ 3. 水平主干：房间出口(0°) → 机关门 ============
            // 北墙留空：45° 方向那条斜向走廊的入口就在这一段上
            Corridor(root, mat, flat, 2f, GateCenterX, -2f, 2f, wallN: false);

            // 机关门：一道厚 1m 的墙，触发机关 a 之后消失
            var gateGo = Slab(root, mat, "Gate",
                              new Vector3(GateCenterX, Ceiling * 0.5f, 0f),
                              new Vector3(GateThick, Ceiling, CorridorWidth), true);
            gateOut = gateGo.transform;

            // 机关门 → 斜坡起点（3m）。东西两侧的墙留空，避免和机关门 / 斜坡侧墙打架
            Corridor(root, mat, flat, GateCenterX, SlopeStartX, -2f, 2f,
                     wallW: false, wallE: false, wallN: false);

            // ============ 4. 斜向走廊（45°，通往机关空间 a） ============
            BuildDiagonalCorridor(root, mat, flat);

            // ============ 5. 斜坡 ============
            BuildSlope(root, mat, flat);

            // ============ 6. 上层主干（Y=6 平面，X 从 27.5 到 45） ============
            CorridorUpper(root, mat, flat, SlopeEndX, UpperEastX, UpperMainZ0, UpperMainZ1,
                          wallW: false);
            Cap(root, mat, UpperEastX + WallThick * 0.5f, 0f,
                WallThick, CorridorWidth + WallThick * 2f, UpperY);

            // ============ 7. 东北角竖直走廊（把上层主干接回下层走廊） ============
            // 净空 X∈[45,49]，Z∈[2,14]，地面 Y=6。南北墙都留空 —— 两端分别接主干和下层走廊
            CorridorUpper(root, mat, flat, UpperEastX, UpperVertX1, UpperMainZ1, LowerZ1,
                          wallS: false, wallN: false);

            // ============ 8. 下层走廊（Y=6 平面，通往终点） ============
            // 凹室（机关空间 b）挖在下层走廊南侧，所以走廊在凹室那一带向南鼓出去：
            //   凹室净空 X∈[37,41]，Z∈[6,10]
            //   东段净空 X∈[41,59]，Z∈[10,14]
            CorridorUpper(root, mat, flat, MechBX0, MechBX1, MechBZ0, LowerZ0,
                          wallW: false, wallE: false, wallN: false);   // 凹室本体
            CorridorUpper(root, mat, flat, MechBX1, LowerEastX, LowerZ0, LowerZ1,
                          wallS: false);                                // 东段

            Cap(root, mat, LowerEastX + WallThick * 0.5f, LowerZ0 + CorridorWidth * 0.5f,
                WallThick, CorridorWidth, UpperY);

            // 凹室的南墙 / 东墙
            Slab(root, mat, "MechBWallS",
                 new Vector3((MechBX0 + MechBX1) * 0.5f, UpperY + Ceiling * 0.5f,
                             MechBZ0 - WallThick * 0.5f),
                 new Vector3(CorridorWidth + WallThick * 2f, Ceiling, WallThick), true);
            Slab(root, mat, "MechBWallE",
                 new Vector3(MechBX1 + WallThick * 0.5f, UpperY + Ceiling * 0.5f,
                             (MechBZ0 + LowerZ0) * 0.5f),
                 new Vector3(WallThick, Ceiling, LowerZ0 - MechBZ0), true);

            // 西侧封墙：X=37 那道墙从凹室南墙一直封到走廊北墙，
            // 否则玩家会在 X=41 附近从南边漏出去
            Slab(root, mat, "MechBWallW",
                 new Vector3(MechBX0 - WallThick * 0.5f, UpperY + Ceiling * 0.5f,
                             (MechBZ0 + LowerZ1) * 0.5f),
                 new Vector3(WallThick, Ceiling, LowerZ1 - MechBZ0), true);

            // ============ 9. 特殊空间 ============
            // 出生点走廊里的提示空间（占满走廊整个高度）
            Vector3 hintA     = new Vector3(0f, Ceiling * 0.5f, -13f);
            Vector3 hintASize = new Vector3(CorridorWidth, Ceiling, 4f);

            // 终点走廊南侧的提示空间
            Vector3 hintB     = new Vector3(47f, UpperY + Ceiling * 0.5f, 12f);
            Vector3 hintBSize = new Vector3(4f, Ceiling, 4f);

            // 机关空间 a：圆房间 45° 斜向走廊里（中心线上，距圆心 12m 处）
            float diagMid = 12f * Mathf.Sqrt(0.5f);   // 12 * cos45° ≈ 8.485
            Vector3 mechA     = new Vector3(diagMid, Ceiling * 0.5f, diagMid);
            Vector3 mechASize = new Vector3(4f, Ceiling, 4f);

            // 机关空间 b：下层走廊南侧的凹室内部
            Vector3 mechB     = new Vector3(39f, UpperY + Ceiling * 0.5f, 8f);
            Vector3 mechBSize = new Vector3(3f, Ceiling, 3f);

            // 陷阱空间（红）：只贴地 1m，挡住下层走廊南侧那半
            Vector3 trapC     = new Vector3(43f, UpperY + 0.5f, 12f);
            Vector3 trapCSize = new Vector3(4f, 1f, 4f);

            // 终点空间（绿）：占满走廊整个高度
            goalPosition = new Vector3(51f, UpperY + Ceiling * 0.5f, 12f);

            BuildZones(root, hintA, hintASize, hintB, hintBSize,
                       mechA, mechASize, mechB, mechBSize,
                       trapC, trapCSize, goalPosition,
                       out GameObject mechAGo, out GameObject mechBGo, out GameObject trapGo);

            // 机关触发后要消失的东西
            var za = mechAGo.GetComponent<SpecialZone>();
            var zb = mechBGo.GetComponent<SpecialZone>();
            if (za != null) za.disableOnTrigger = new[] { gateGo };
            if (zb != null) zb.disableOnTrigger = new[] { trapGo };

            // 出生点：图上的 × 位于提示空间下方那段走廊里。
            // X、Z 都必须取走廊中心线 0 —— 走廊净空 X∈[-2,2]，两侧墙占 X∈[1.5,2.5]，
            // 之前写成 X=2 会把玩家卡进墙里。
            return new Vector3(0f, 0.1f, -18f);
        }

        // ------------------------------------------------------------------ 圆形房间

        private static void BuildCircularRoom(Transform root, Material mat)
        {
            var verts = new List<Vector3>();
            var tris  = new List<int>();

            // ---- 环形墙：内表面 r=8，Y 从 0 到 4 ----
            for (int i = 0; i < RingSegments; i++)
            {
                float a0 = i * (360f / RingSegments);
                float a1 = a0 + (360f / RingSegments);

                if (NearAngle(a0, DoorEastAngle,  DoorEastHalf))  continue;
                if (NearAngle(a0, DoorSouthAngle, DoorSouthHalf)) continue;
                if (NearAngle(a0, DiagDoorAngle,  DiagDoorHalf))  continue;

                Vector3 p0 = Polar(RoomRadius, a0);
                Vector3 p1 = Polar(RoomRadius, a1);

                int b = verts.Count;
                verts.Add(new Vector3(p0.x, 0f,      p0.z));
                verts.Add(new Vector3(p1.x, 0f,      p1.z));
                verts.Add(new Vector3(p1.x, Ceiling, p1.z));
                verts.Add(new Vector3(p0.x, Ceiling, p0.z));

                tris.Add(b + 0); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 3);
            }

            // ---- 半球穹顶：半径 8，只保留 Y >= Ceiling 的部分 ----
            int lat = 24;
            int lon = RingSegments;

            var domeVerts = new List<Vector3>();
            var domeTris  = new List<int>();
            var lookup    = new Dictionary<long, int>();

            int DomeVert(int iLat, int iLon)
            {
                long key = (long)iLat * 100000 + (iLon % lon);
                if (lookup.TryGetValue(key, out int found)) return found;

                float phi = (90f * iLat / lat) * Mathf.Deg2Rad;   // 0 = 顶点
                float th  = (iLon % lon) * (360f / lon) * Mathf.Deg2Rad;

                float rr = Mathf.Cos(phi) * RoomRadius;
                var v = new Vector3(Mathf.Cos(th) * rr, Mathf.Sin(phi) * RoomRadius,
                                    Mathf.Sin(th) * rr);

                // 低于环墙顶的部分不生成（那一段已经被墙占了）
                if (v.y < Ceiling - 0.001f && iLat > 0) { lookup[key] = -1; return -1; }

                domeVerts.Add(v);
                int idx = domeVerts.Count - 1;
                lookup[key] = idx;
                return idx;
            }

            for (int i = 0; i < lat; i++)
            {
                for (int j = 0; j < lon; j++)
                {
                    int v00 = DomeVert(i,     j);
                    int v01 = DomeVert(i,     j + 1);
                    int v10 = DomeVert(i + 1, j);
                    int v11 = DomeVert(i + 1, j + 1);

                    if (v00 < 0 || v01 < 0 || v10 < 0 || v11 < 0) continue;

                    // 内表面朝向球心，所以绕序反过来
                    domeTris.Add(v00); domeTris.Add(v10); domeTris.Add(v11);
                    domeTris.Add(v00); domeTris.Add(v11); domeTris.Add(v01);
                }
            }

            var allVerts = new List<Vector3>(verts);
            var allTris  = new List<int>(tris);
            int offset = allVerts.Count;
            allVerts.AddRange(domeVerts);
            for (int i = 0; i < domeTris.Count; i++) allTris.Add(domeTris[i] + offset);

            var mesh = new Mesh { name = "CircleRoom" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(allVerts);
            mesh.SetTriangles(allTris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("CircleRoomWall");
            go.transform.SetParent(root, false);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial    = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows    = true;

            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex     = false;

            // ---- 圆形地板：圆盘，Y 从 -1 到 0 ----
            BuildDisc(root, mat, RoomRadius + WallThick, -WallThick, 0f);
        }

        private static Vector3 Polar(float r, float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }

        private static bool NearAngle(float a, float target, float tol)
            => Mathf.Abs(Mathf.DeltaAngle(a, target)) <= tol;

        // ------------------------------------------------------------------ 走廊

        /// <summary>地面层的走廊：净空 X∈[x0,x1]，Z∈[z0,z1]，Y 从 0 到 4。</summary>
        private static void Corridor(Transform root, Material mat, Material flat,
                                     float x0, float x1, float z0, float z1,
                                     bool wallS = true, bool wallN = true,
                                     bool wallW = true, bool wallE = true)
            => CorridorAt(root, mat, flat, x0, x1, z0, z1, 0f, Ceiling,
                          wallS, wallN, wallW, wallE);

        /// <summary>上层（Y=6）的走廊。</summary>
        private static void CorridorUpper(Transform root, Material mat, Material flat,
                                          float x0, float x1, float z0, float z1,
                                          bool wallS = true, bool wallN = true,
                                          bool wallW = true, bool wallE = true)
            => CorridorAt(root, mat, flat, x0, x1, z0, z1, UpperY, UpperY + Ceiling,
                          wallS, wallN, wallW, wallE);

        /// <summary>
        /// 一段走廊：南北两道墙 + 东西两道墙 + 天花板 + 地板。
        /// 墙和天花板都向净空**外侧**长 WallThick，所以不占走廊宽度。
        /// 段与段对接的地方可以把某一侧的墙关掉，免得在接缝处多出一堵挡路的墙。
        /// </summary>
        private static void CorridorAt(Transform root, Material mat, Material flat,
                                       float x0, float x1, float z0, float z1,
                                       float yFloor, float yCeil,
                                       bool wallS, bool wallN, bool wallW, bool wallE)
        {
            float w  = x1 - x0;
            float d  = z1 - z0;
            float cx = (x0 + x1) * 0.5f;
            float cz = (z0 + z1) * 0.5f;
            float h  = yCeil - yFloor;
            float my = yFloor + h * 0.5f;

            // 南北墙：沿 X 展开
            if (wallS)
                Slab(root, mat, "WallS", new Vector3(cx, my, z0 - WallThick * 0.5f),
                     new Vector3(w + WallThick * 2f, h, WallThick), true);

            if (wallN)
                Slab(root, mat, "WallN", new Vector3(cx, my, z1 + WallThick * 0.5f),
                     new Vector3(w + WallThick * 2f, h, WallThick), true);

            // 东西墙：沿 Z 展开
            if (wallW)
                Slab(root, mat, "WallW", new Vector3(x0 - WallThick * 0.5f, my, cz),
                     new Vector3(WallThick, h, d), true);

            if (wallE)
                Slab(root, mat, "WallE", new Vector3(x1 + WallThick * 0.5f, my, cz),
                     new Vector3(WallThick, h, d), true);

            // 天花板与地板
            Slab(root, flat, "Ceil", new Vector3(cx, yCeil + WallThick * 0.5f, cz),
                 new Vector3(w + WallThick * 2f, WallThick, d + WallThick * 2f), false);
            Slab(root, flat, "Floor", new Vector3(cx, yFloor - WallThick * 0.5f, cz),
                 new Vector3(w + WallThick * 2f, WallThick, d + WallThick * 2f), false);
        }

        /// <summary>走廊端头封口。cx / cz 是封口中心，sizeX / sizeZ 是它的横截面尺寸。</summary>
        private static void Cap(Transform root, Material mat, float cx, float cz,
                                float sizeX, float sizeZ, float yBase)
        {
            Slab(root, mat, "Cap",
                 new Vector3(cx, yBase + Ceiling * 0.5f, cz),
                 new Vector3(sizeX, Ceiling, sizeZ), true);
        }

        // ------------------------------------------------------------------ 斜向走廊

        /// <summary>
        /// 45° 方向的斜向走廊：从圆房间的斜向门洞往外伸 DiagLength 米，宽 4m。
        /// 侧墙用绕 Y 轴旋转 45° 的方块，所以接缝是严丝合缝的。
        /// 机关空间 a 就放在这条走廊里。
        /// </summary>
        private static void BuildDiagonalCorridor(Transform root, Material mat, Material flat)
        {
            float a = DiagDoorAngle * Mathf.Deg2Rad;
            Vector3 dir  = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));   // 中心线方向
            Vector3 perp = new Vector3(-dir.z, 0f, dir.x);                // 水平垂直方向

            float u0 = RoomRadius - 1.5f;                 // 起点稍微伸进房间，保证接缝不漏
            float u1 = RoomRadius + DiagLength;
            float v0 = -CorridorWidth * 0.5f, v1 = CorridorWidth * 0.5f;

            Vector3 Center(float u, float v) => dir * u + perp * v;
            float yaw = -DiagDoorAngle;                   // 让方块的长边对齐 dir

            float mu = (u0 + u1) * 0.5f;
            float ml = u1 - u0;

            // 两侧墙
            SlabRot(root, mat, "DiagWallA",
                    Center(mu, v0 - WallThick * 0.5f) + Vector3.up * (Ceiling * 0.5f),
                    new Vector3(ml + WallThick, Ceiling, WallThick), yaw, true);
            SlabRot(root, mat, "DiagWallB",
                    Center(mu, v1 + WallThick * 0.5f) + Vector3.up * (Ceiling * 0.5f),
                    new Vector3(ml + WallThick, Ceiling, WallThick), yaw, true);

            // 尽头封口
            SlabRot(root, mat, "DiagEnd",
                    Center(u1 + WallThick * 0.5f, 0f) + Vector3.up * (Ceiling * 0.5f),
                    new Vector3(WallThick, Ceiling, CorridorWidth + WallThick * 2f), yaw, true);

            // 地板 / 天花板
            float flatLen = ml + WallThick * 2f;
            float flatWid = CorridorWidth + WallThick * 2f;

            SlabRot(root, flat, "DiagFloor", Center(mu, 0f) + Vector3.down * (WallThick * 0.5f),
                    new Vector3(flatLen, WallThick, flatWid), yaw, false);
            SlabRot(root, flat, "DiagCeil",
                    Center(mu, 0f) + Vector3.up * (Ceiling + WallThick * 0.5f),
                    new Vector3(flatLen, WallThick, flatWid), yaw, false);
        }

        /// <summary>带 Y 轴旋转的方块。注意 size 是旋转**之前**的尺寸。</summary>
        private static GameObject SlabRot(Transform root, Material mat, string name,
                                          Vector3 center, Vector3 size, float yawDeg, bool castsShadow)
        {
            var go = Slab(root, mat, name, center, size, castsShadow);
            go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            return go;
        }

        // ------------------------------------------------------------------ 斜坡

        /// <summary>
        /// 三角形斜坡：截面是直角三角形（水平 8、高 6、坡长 10），沿 Z 宽 4m。
        /// 从西往东是上坡。
        /// </summary>
        private static void BuildSlope(Transform root, Material mat, Material flat)
        {
            float x0 = SlopeStartX, x1 = SlopeEndX;
            float z0 = -CorridorWidth * 0.5f, z1 = CorridorWidth * 0.5f;
            float h  = SlopeRise;

            // 截面三点（XY 平面）：左下、右下、右上
            var sec = new List<Vector2>
            {
                new Vector2(x0, 0f),
                new Vector2(x1, 0f),
                new Vector2(x1, h),
            };

            int n = sec.Count;
            var verts = new List<Vector3>();
            var tris  = new List<int>();

            // 前后两个端面
            for (int i = 0; i < n; i++) verts.Add(new Vector3(sec[i].x, sec[i].y, z0));
            for (int i = 0; i < n; i++) verts.Add(new Vector3(sec[i].x, sec[i].y, z1));

            tris.Add(0); tris.Add(2); tris.Add(1);          // 南端面
            tris.Add(3); tris.Add(4); tris.Add(5);          // 北端面

            // 三个侧面
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int a = i, b = j, c = n + j, d = n + i;
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }

            var mesh = new Mesh { name = "Slope" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Slope");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial    = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex     = false;

            // 斜坡两侧的墙（从地面一直到上层天花板）
            float total = h + Ceiling;
            Slab(root, mat, "SlopeWallS",
                 new Vector3((x0 + x1) * 0.5f, total * 0.5f, z0 - WallThick * 0.5f),
                 new Vector3(x1 - x0 + WallThick * 2f, total, WallThick), true);
            Slab(root, mat, "SlopeWallN",
                 new Vector3((x0 + x1) * 0.5f, total * 0.5f, z1 + WallThick * 0.5f),
                 new Vector3(x1 - x0 + WallThick * 2f, total, WallThick), true);

            // 斜坡段的天花板
            Slab(root, flat, "SlopeCeil",
                 new Vector3((x0 + x1) * 0.5f, UpperY + Ceiling + WallThick * 0.5f, 0f),
                 new Vector3(x1 - x0 + WallThick * 2f, WallThick,
                             CorridorWidth + WallThick * 2f), false);
        }

        // ------------------------------------------------------------------ 构件

        private static GameObject Slab(Transform root, Material mat, string name,
                                       Vector3 center, Vector3 size, bool castsShadow)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position   = center;
            go.transform.localScale = size;

            var mr = go.GetComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = castsShadow
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;

            return go;
        }

        private static void BuildDisc(Transform root, Material mat, float radius,
                                      float yBottom, float yTop)
        {
            int seg = RingSegments;
            var verts = new List<Vector3>();
            var tris  = new List<int>();

            float cy = (yBottom + yTop) * 0.5f;
            float hy = (yTop - yBottom) * 0.5f;

            for (int i = 0; i < seg; i++)
            {
                float a0 = i * (360f / seg);
                float a1 = (i + 1) * (360f / seg);

                Vector3 p0 = Polar(radius, a0);
                Vector3 p1 = Polar(radius, a1);

                int b = verts.Count;
                verts.Add(new Vector3(p0.x, cy - hy, p0.z));
                verts.Add(new Vector3(p1.x, cy - hy, p1.z));
                verts.Add(new Vector3(p1.x, cy + hy, p1.z));
                verts.Add(new Vector3(p0.x, cy + hy, p0.z));

                tris.Add(b + 0); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 3);   // 上表面
                tris.Add(b + 2); tris.Add(b + 1); tris.Add(b + 0);
                tris.Add(b + 3); tris.Add(b + 2); tris.Add(b + 0);   // 下表面
            }

            var mesh = new Mesh { name = "RoomFloor" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("RoomFloor");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex     = false;
        }

        // ------------------------------------------------------------------ 特殊空间

        private static void BuildZones(Transform root,
                                       Vector3 hintA, Vector3 hintASize,
                                       Vector3 hintB, Vector3 hintBSize,
                                       Vector3 mechA, Vector3 mechASize,
                                       Vector3 mechB, Vector3 mechBSize,
                                       Vector3 trapC, Vector3 trapCSize,
                                       Vector3 goal,
                                       out GameObject mechAGo, out GameObject mechBGo,
                                       out GameObject trapGo)
        {
            MakeZone(root, "Zone_HintA", ZoneKind.Hint, hintA, hintASize, Color.white, 0f, 0f);
            MakeZone(root, "Zone_HintB", ZoneKind.Hint, hintB, hintBSize, Color.white, 0f, 0f);

            mechAGo = MakeZone(root, "Zone_MechA", ZoneKind.Mechanism, mechA, mechASize,
                               new Color(1f, 0.85f, 0.1f), 0f, 0f);
            mechBGo = MakeZone(root, "Zone_MechB", ZoneKind.Mechanism, mechB, mechBSize,
                               new Color(1f, 0.85f, 0.1f), 0f, 0f);

            trapGo = MakeZone(root, "Zone_Trap", ZoneKind.Trap, trapC, trapCSize,
                              new Color(1f, 0.12f, 0.12f), 0f, 0f);

            MakeZone(root, "Zone_Goal", ZoneKind.Goal, goal,
                     new Vector3(4f, Ceiling, 4f), Color.white, 0.22f, 1f);
        }

        private static GameObject MakeZone(Transform root, string name, ZoneKind kind,
                                           Vector3 center, Vector3 size, Color waveColor,
                                           float goalWidth, float goalAlpha)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);

            var z = go.AddComponent<SpecialZone>();
            z.kind      = kind;
            z.center    = center;
            z.size      = size;
            z.waveColor = waveColor;
            z.goalWidth = goalWidth;
            z.goalAlpha = goalAlpha;

            return go;
        }

        // ------------------------------------------------------------------ 终点触发区

        public static LevelGoal AddGoal(Transform root, Vector3 position)
        {
            var go = new GameObject("LevelGoal");
            go.transform.SetParent(root, false);
            go.transform.position = position;

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center    = Vector3.zero;
            col.size      = new Vector3(4f, Ceiling, 4f);

            return go.AddComponent<LevelGoal>();
        }
    }
}
