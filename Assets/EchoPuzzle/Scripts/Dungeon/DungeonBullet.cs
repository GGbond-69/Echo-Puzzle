using UnityEngine;

namespace EchoPuzzle.Dungeon
{
    /// <summary>
    /// 【地牢模式】一颗子弹 = 一条"子弹光带"。
    ///
    /// ── 结构（和文档里的四段式对应）─────────────────────────
    ///   · 波前：<see cref="m_Travel"/> 这个位置，负责碰撞和伤害
    ///   · 可见拖尾：身后的那一段线，命中后**留在原地淡出**（缩宽度到 0）
    ///   · 不可见拖尾 / 几何轨迹：这一版用一条 LineRenderer 近似，
    ///     等手感定下来再换成 SonarTrailRenderer 的四边形光带（见文件末尾说明）
    ///
    /// ── 命中规则 ────────────────────────────────────────────
    ///   · 怪物：按"圆柱"判定，半径 / 高度由武器传进来（默认 2 / 4），
    ///     因为怪物实体只有 0.5 半径，玩家根本打不中
    ///   · 墙壁：和声波同一套层掩码（排除 Ignore Raycast / Water / UI）
    ///   · 命中后波前停住，拖尾在 fadeTime 内缩到 0 再销毁自己
    ///
    /// ── 和探秘模式的关系 ────────────────────────────────────
    ///   ★ 只有 <see cref="DungeonWeapon"/> 会创建它，探秘模式不产生子弹。
    /// </summary>
    public sealed class DungeonBullet : MonoBehaviour
    {
        /// <summary>和声波一致的"墙"层掩码：排除 Ignore Raycast(2) / Water(4) / UI(5)。</summary>
        private static readonly LayerMask WallMask = ~((1 << 2) | (1 << 4) | (1 << 5));

        // 怪物列表每帧全场景扫太贵，0.5 秒刷新一次就够（怪物移动没那么快）
        private static PuzzleMonster[] s_Monsters;
        private static float           s_MonstersNextRefresh;

        // ------------------------------------------------------------------ 创建

        /// <summary>
        /// 发射一颗子弹。
        /// </summary>
        /// <param name="material">拖尾材质（由武器持有并销毁，子弹不负责）</param>
        public static DungeonBullet Spawn(Vector3 origin, Vector3 dir,
                                          float speed, float range, float width,
                                          float fadeTime, int damage, int manaRestore,
                                          float hurtRadius, float hurtHeight,
                                          Material material)
        {
            var go = new GameObject("DungeonBullet");
            var b  = go.AddComponent<DungeonBullet>();
            b.Init(origin, dir, speed, range, width, fadeTime, damage, manaRestore,
                   hurtRadius, hurtHeight, material);
            return b;
        }

        // ------------------------------------------------------------------ 状态

        private Vector3 m_Origin;
        private Vector3 m_Dir;

        private float m_Speed;
        private float m_Range;
        private float m_Width;
        private float m_FadeTime;

        private int m_Damage;
        private int m_ManaRestore;

        private float m_HurtRadius;
        private float m_HurtHeight;

        private float m_Travel;      // 波前已经飞了多远
        private bool  m_Stopped;     // 命中 / 到射程，波前停住
        private float m_FadeTimer;

        private LineRenderer m_Line;

        private void Init(Vector3 origin, Vector3 dir,
                          float speed, float range, float width,
                          float fadeTime, int damage, int manaRestore,
                          float hurtRadius, float hurtHeight,
                          Material material)
        {
            m_Origin      = origin;
            m_Dir         = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            m_Speed       = Mathf.Max(0.1f, speed);
            m_Range       = Mathf.Max(0.1f, range);
            m_Width       = Mathf.Max(0.01f, width);
            m_FadeTime    = Mathf.Max(0.01f, fadeTime);
            m_Damage      = Mathf.Max(1, damage);
            m_ManaRestore = Mathf.Max(0, manaRestore);
            m_HurtRadius  = Mathf.Max(0.01f, hurtRadius);
            m_HurtHeight  = Mathf.Max(0.01f, hurtHeight);

            m_Line = gameObject.AddComponent<LineRenderer>();
            m_Line.useWorldSpace       = true;
            m_Line.positionCount       = 2;
            m_Line.loop                = false;
            m_Line.textureMode         = LineTextureMode.Stretch;
            m_Line.alignment           = LineAlignment.View;
            m_Line.numCapVertices      = 2;
            m_Line.shadowCastingMode   = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Line.receiveShadows      = false;
            m_Line.generateLightingData = false;

            if (material != null) m_Line.sharedMaterial = material;

            m_Line.SetPosition(0, m_Origin);
            m_Line.SetPosition(1, m_Origin);

            ApplyWidth(m_Width);
        }

        // ------------------------------------------------------------------ 每帧

        private void Update()
        {
            float dt = Time.deltaTime;

            if (!m_Stopped)
            {
                Step(dt);
                return;
            }

            // 命中之后：只做淡出
            m_FadeTimer += dt;

            float k = Mathf.Clamp01(1f - m_FadeTimer / m_FadeTime);
            ApplyWidth(m_Width * k);

            if (k <= 0f) Destroy(gameObject);
        }

        /// <summary>推进一步波前，顺带做碰撞。</summary>
        private void Step(float dt)
        {
            Vector3 a = PointAt(m_Travel);

            float next = Mathf.Min(m_Travel + m_Speed * dt, m_Range);
            Vector3 b = PointAt(next);

            float segLen = (b - a).magnitude;

            // ---- 1) 先查怪物：贴脸时墙的判定可能先吃掉，所以怪物优先 ----
            if (TryHitMonster(a, b, out float hitT))
            {
                StopAt(Mathf.Lerp(m_Travel, next, hitT));
                return;
            }

            // ---- 2) 再查墙 ----
            if (segLen > 1e-5f)
            {
                if (Physics.SphereCast(a, m_Width * 0.5f, (b - a) / segLen,
                                       out RaycastHit hit, segLen,
                                       WallMask, QueryTriggerInteraction.Ignore))
                {
                    StopAt(m_Travel + Mathf.Max(0f, hit.distance));
                    return;
                }
            }

            m_Travel = next;
            m_Line.SetPosition(1, b);

            // 打空到射程尽头：停住、开始淡出
            if (m_Travel >= m_Range - 1e-4f) StopAt(m_Range);
        }

        /// <summary>波前停在这里，之后只淡出。</summary>
        private void StopAt(float distance)
        {
            m_Stopped = true;
            m_Travel  = Mathf.Clamp(distance, 0f, m_Range);
            m_Line.SetPosition(1, PointAt(m_Travel));
        }

        private void ApplyWidth(float w)
        {
            w = Mathf.Max(0f, w);
            m_Line.startWidth = w;
            m_Line.endWidth   = w;
        }

        private Vector3 PointAt(float distance) => m_Origin + m_Dir * distance;

        // ------------------------------------------------------------------ 怪物判定

        /// <summary>
        /// 线段 a→b 有没有穿过某只怪物的"受击圆柱"。
        /// 命中就扣血，打死就回蓝。
        /// </summary>
        private bool TryHitMonster(Vector3 a, Vector3 b, out float hitT)
        {
            hitT = 0f;

            RefreshMonsters();
            if (s_Monsters == null) return false;

            float best = float.MaxValue;
            PuzzleMonster bestMon = null;

            for (int i = 0; i < s_Monsters.Length; i++)
            {
                PuzzleMonster mon = s_Monsters[i];
                if (mon == null || !mon.isActiveAndEnabled) continue;
                if (mon.DungeonIsDead) continue;

                // 圆柱：以怪物球心为中轴，水平半径 hurtRadius，竖直总高 hurtHeight
                if (!SegmentHitsCylinder(a, b, mon.transform.position,
                                         m_HurtRadius, m_HurtHeight * 0.5f, out float t))
                    continue;

                if (t < best)
                {
                    best     = t;
                    bestMon  = mon;
                }
            }

            if (bestMon == null) return false;

            hitT = best;

            // ★ 伤害入口：PuzzleMonster 已经写好了地牢模式的血量 / 死亡 / 事件，
            //   探秘模式下这个调用会被它自己挡掉（不是地牢模式直接 return）。
            bool wasDead = bestMon.DungeonIsDead;
            bestMon.DungeonTakeDamage(m_Damage);

            // 这一发把它打死了 -> 回蓝
            if (!wasDead && bestMon.DungeonIsDead && m_ManaRestore > 0)
            {
                if (DungeonMana.Instance != null) DungeonMana.Instance.Restore(m_ManaRestore);
            }

            return true;
        }

        private static void RefreshMonsters()
        {
            float now = Time.timeSinceLevelLoad;
            if (s_Monsters != null && now < s_MonstersNextRefresh) return;

            s_MonstersNextRefresh = now + 0.5f;
            s_Monsters = FindObjectsOfType<PuzzleMonster>(true);
        }

        /// <summary>
        /// 线段 [a,b] 与"竖直圆柱"（中轴过 center、水平半径 radius、半高 halfHeight）
        /// 的解析求交。返回第一次进入圆柱的 t（0~1）。
        /// </summary>
        private static bool SegmentHitsCylinder(Vector3 a, Vector3 b, Vector3 center,
                                                float radius, float halfHeight, out float t)
        {
            t = 0f;

            Vector3 p = a - center;
            Vector3 d = b - a;

            float tLo = 0f;
            float tHi = 1f;

            // ---- 水平方向：|p.xz + d.xz·t| <= radius ----
            float A = d.x * d.x + d.z * d.z;
            float B = 2f * (p.x * d.x + p.z * d.z);
            float C = p.x * p.x + p.z * p.z - radius * radius;

            if (A < 1e-8f)
            {
                if (C > 0f) return false;
            }
            else
            {
                float disc = B * B - 4f * A * C;
                if (disc < 0f) return false;

                float sq = Mathf.Sqrt(disc);
                float t0 = (-B - sq) / (2f * A);
                float t1 = (-B + sq) / (2f * A);

                tLo = Mathf.Max(tLo, Mathf.Min(t0, t1));
                tHi = Mathf.Min(tHi, Mathf.Max(t0, t1));

                if (tLo > tHi) return false;
            }

            // ---- 竖直方向：|p.y + d.y·t| <= halfHeight ----
            if (Mathf.Abs(d.y) < 1e-8f)
            {
                if (Mathf.Abs(p.y) > halfHeight) return false;
            }
            else
            {
                float y0 = (-halfHeight - p.y) / d.y;
                float y1 = ( halfHeight - p.y) / d.y;

                if (y0 > y1) { float tmp = y0; y0 = y1; y1 = tmp; }

                tLo = Mathf.Max(tLo, y0);
                tHi = Mathf.Min(tHi, y1);

                if (tLo > tHi) return false;
            }

            t = Mathf.Clamp01(tLo);
            return true;
        }
    }
}