using System.Collections.Generic;
using Starstrung.Logic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 岔路信标 + Boss 吞星兽（和星核同一套视觉语言：星云、黄铜、水晶、魔法阵、手绘细线）。
    ///
    /// 岔路信标（3-4、3-5）：悬在岔口上方 16 cm——一颗慢慢自转的发光水晶 + 外面一圈小魔法阵（朝着玩家），
    /// 旁边一道手绘弧形箭头指向“下一条路”；快自动换路时水晶和箭头闪烁预警；被指着时放大；捏它 = 手动换路（逻辑层判定）。
    ///
    /// 吞星兽（4-5）：Tripo P2 模型（概念图 → 白模审计 → 最高档贴图，Docs/概念图/吞星兽_1124），整头程序动画、不用骨骼——
    /// - 深紫漆面甲片 + 黄铜星盘纹饰 + 水晶角，眼睛周围一圈星盘刻度；嘴本来就是张开的，嘴里是星云（StarEater 着色器分区上材质感）；
    /// - 平时嘴里星云暗；预警时一闪一闪；张嘴可喂时嘴里亮成它要的颜色、嘴前张开一圈小魔法阵、星尘往嘴里吸，头先往后仰再前探；
    /// - 嚼星前抖；受击闪白 + 后撤；头顶三颗金宝石 = 血量；出现 / 消失用溶解（逐片消散带亮边）；
    /// - 嘴朝向一半沿路线、一半朝玩家（看得见嘴里的颜色）。
    /// </summary>
    public class ForkBossView : MonoBehaviour
    {
        public GameRunner runner;
        public GemField gems;
        public Mesh crystalMesh, hoopMesh;
        public Material moteMat;
        public FxSystem fx;
        public Material crystalMat, brassMat, shellMat, mawMat, circleMat;
        public GameObject headPrefab;   // Tripo P2 吞星兽头（模型本地：头顶 +Y，鼻尖朝偏航 224°）
        public Material headMat;

        // 信标
        Transform beacon, beaconGem, beaconCircle; LineRenderer arrow; Material beaconCircleMat;
        // Boss
        Transform boss, headT, mouthCircle; Renderer[] headR; Renderer mouthGlow; Material mouthCircleMat; MaterialPropertyBlock headMpb;
        ParticleSystem suck;
        float jaw, chewShake, hurtFlash, appear, recoil;
        float defeat = -1f; Vector3 defeatPos; Quaternion defeatRot; float defeatSize;
        static readonly Vector3 MouthLocal = new(0f, -0.18f, 0.32f);   // 嘴在头本地（转正后）的位置，头半径约 0.5
        static readonly Color Ink = new(0.85f, 0.9f, 1f, 1f);

        void Start()
        {
            BuildBeacon(); BuildBoss();
            if (runner) runner.OnEvent += e =>
            {
                if (e.Type == "bossHurt") hurtFlash = 1f;
                // 退散（11-24 用户：不要一下就没了）：原地定住，仰头、嘴里的星云一股股往外喷、裂光爬满、整头溶解飘散
                if (e.Type == "bossDefeated" && boss) { defeat = 0f; defeatPos = boss.position; defeatRot = boss.rotation; defeatSize = boss.localScale.x; }
                if (e.Type == "stage" || e.Type == "won" || e.Type == "lost") { }
            };
        }

        void BuildBeacon()
        {
            beacon = new GameObject("ForkBeacon").transform; beacon.SetParent(transform, false);
            beaconGem = Part(beacon, "Gem", crystalMesh, crystalMat).transform; beaconGem.localScale = new Vector3(0.065f, 0.12f, 0.065f);
            var cg = new GameObject("Circle"); cg.transform.SetParent(beacon, false); beaconCircle = cg.transform;
            cg.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var mr = cg.AddComponent<MeshRenderer>(); beaconCircleMat = new Material(circleMat); mr.sharedMaterial = beaconCircleMat;
            beaconCircleMat.SetFloat("_Points", 5); beaconCircleMat.SetFloat("_Seed", 77); beaconCircleMat.SetFloat("_Intensity", 1.1f);
            beaconCircleMat.SetColor("_ColA", new Color(1f, 0.75f, 0.4f)); beaconCircleMat.SetColor("_ColB", new Color(1f, 0.92f, 0.75f));
            cg.transform.localScale = Vector3.one * 0.27f;
            arrow = UIKit.Line(transform, "BeaconArrow", new List<Vector3> { Vector3.zero, Vector3.zero }, 0.003f, Ink);
            arrow.useWorldSpace = true;
            beacon.gameObject.SetActive(false);
        }

        static Mesh Dome(bool top, int seg = 40, int rings = 12)
        {
            // 半球 + 平口（实心，张嘴时看得到口面）；top = 上半，否则下半
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            float s = top ? 1 : -1;
            for (int r = 0; r <= rings; r++)
            {
                float phi = r / (float)rings * Mathf.PI * 0.5f;
                for (int i = 0; i <= seg; i++)
                {
                    float th = i / (float)seg * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(th) * Mathf.Cos(phi), s * Mathf.Sin(phi), Mathf.Sin(th) * Mathf.Cos(phi));
                    v.Add(d); n.Add(d);
                    if (r < rings && i < seg) { int a = r * (seg + 1) + i, b = a + seg + 1; if (top) t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 }); else t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b }); }
                }
            }
            int c0 = v.Count; v.Add(Vector3.zero); n.Add(new Vector3(0, -s, 0));
            for (int i = 0; i <= seg; i++) { float th = i / (float)seg * Mathf.PI * 2f; v.Add(new Vector3(Mathf.Cos(th), 0, Mathf.Sin(th))); n.Add(new Vector3(0, -s, 0)); }
            for (int i = 0; i < seg; i++) { if (top) t.AddRange(new[] { c0, c0 + 1 + i, c0 + 2 + i }); else t.AddRange(new[] { c0, c0 + 2 + i, c0 + 1 + i }); }
            var m = new Mesh { name = top ? "DomeTop" : "DomeBottom" }; m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        GameObject Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        void BuildBoss()
        {
            boss = new GameObject("StarEater").transform; boss.SetParent(transform, false);
            headMpb = new MaterialPropertyBlock();
            if (headPrefab)
            {
                headT = Instantiate(headPrefab, boss).transform;
                headT.name = "Head";
                headT.localRotation = Quaternion.Euler(0, 136f, 0);   // 模型鼻尖在偏航 224° → 转到 +Z
                headR = headT.GetComponentsInChildren<Renderer>();
                foreach (var r in headR) { r.sharedMaterial = headMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            }
            // 嘴里亮的颜色（四芒光点），嘴前一圈小魔法阵
            mouthGlow = UIKit.Glyph(boss, "MouthGlow", MouthLocal + new Vector3(0, 0, 0.1f), 0.9f);
            var cg = new GameObject("MouthCircle"); cg.transform.SetParent(boss, false); mouthCircle = cg.transform;
            cg.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var mr = cg.AddComponent<MeshRenderer>(); mouthCircleMat = new Material(circleMat); mr.sharedMaterial = mouthCircleMat;
            mouthCircleMat.SetFloat("_Points", 7); mouthCircleMat.SetFloat("_Seed", 91);
            cg.transform.localPosition = MouthLocal + new Vector3(0, 0, 0.42f);
            // 星尘往嘴里吸（粒子在嘴前一圈生成，速度指向嘴）
            var pg = new GameObject("Suck"); pg.transform.SetParent(boss, false); pg.transform.localPosition = MouthLocal + new Vector3(0, 0, 0.1f);
            suck = pg.AddComponent<ParticleSystem>(); suck.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = suck.main; main.loop = true; main.startLifetime = 0.7f; main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.014f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local; main.maxParticles = 150; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var sh = suck.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.9f; sh.radiusThickness = 0.15f;
            var vel = suck.velocityOverLifetime; vel.enabled = true; vel.radial = new ParticleSystem.MinMaxCurve(-1.3f);
            var em = suck.emission; em.rateOverTime = 0f;
            var col = suck.colorOverLifetime; col.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.3f), new GradientAlphaKey(0, 1) }); col.color = gr;
            if (moteMat) pg.GetComponent<ParticleSystemRenderer>().sharedMaterial = moteMat;
            suck.Play();
            boss.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            var g = runner ? runner.Game : null;
            float t = Time.time, dt = Time.deltaTime;
            var cam = Camera.main;

            // ── 信标 ──
            bool hasFork = g != null && g.Fork != null && g.Tracks.Count > 0 && g.Tracks[0] is ForkTrack;
            if (beacon) beacon.gameObject.SetActive(hasFork);
            if (hasFork)
            {
                var ft = (ForkTrack)g.Tracks[0]; var F = g.Fork;
                var pos = runner.ToWorld(ft.ForkPos) + Vector3.up * 0.16f;
                beacon.position = pos;
                bool hovered = false; foreach (var h in g.Hands.Values) if (h.Hover != null && h.Hover.Type == "fork") hovered = true;
                float blink = F.Warn ? (Mathf.Sin(t * 20f) > 0 ? 1f : 0.25f) : 1f;
                beacon.localScale = Vector3.one * (hovered ? 1.3f : 1f) * Mathf.Clamp01(AppFlow.Grow / 6f + (AppFlow.Grow > 999 ? 1 : 0));
                beaconGem.localRotation = Quaternion.Euler(0, t * 60f, 0);
                if (cam) beaconCircle.rotation = Quaternion.LookRotation(pos - cam.transform.position) * Quaternion.Euler(0, 0, t * 20f);
                beaconCircleMat.SetFloat("_Intensity", 1.1f * blink);
                // 箭头：从信标指向下一条路往前一段（手绘弧线 + 箭头的两撇）
                var p = ft.Paths[F.Next];
                var a = runner.ToWorld(p.PointAtU(ft.ForkU + 1.5)); var b = runner.ToWorld(p.PointAtU(ft.ForkU + 5));
                var mid = (pos + b) * 0.5f + Vector3.up * 0.08f;
                var pts = new List<Vector3>();
                for (int k = 0; k <= 16; k++) { float u = k / 16f; pts.Add(Vector3.Lerp(Vector3.Lerp(pos, mid, u), Vector3.Lerp(mid, b, u), u)); }
                var dir = (pts[16] - pts[14]).normalized; var side = Vector3.Cross(dir, cam ? (cam.transform.position - b).normalized : Vector3.up).normalized;
                pts.Add(b - dir * 0.05f + side * 0.03f); pts.Add(b); pts.Add(b - dir * 0.05f - side * 0.03f);
                arrow.positionCount = pts.Count; arrow.SetPositions(pts.ToArray());
                UIKit.SetAlpha(arrow, new Color(1f, 0.9f, 0.7f), 0.75f * blink * Mathf.Clamp01(AppFlow.Grow / 8f + (AppFlow.Grow > 999 ? 1 : 0)));
            }
            else if (arrow) UIKit.SetAlpha(arrow, Ink, 0);

            // ── 吞星兽 ──
            // 退散过程（被打败后 2.6 秒）：此时不再跟着星串，原地演完
            if (defeat >= 0f)
            {
                defeat += dt;
                float k = Mathf.Clamp01(defeat / 2.6f);
                boss.gameObject.SetActive(true);
                boss.position = defeatPos + Vector3.up * (k * k * 0.12f);                       // 慢慢往上浮一点
                boss.rotation = defeatRot * Quaternion.Euler(-k * 34f, Mathf.Sin(defeat * 7f) * 4f * (1 - k), 0);   // 仰头，先抖后静
                boss.localScale = Vector3.one * defeatSize * (1f + 0.06f * Mathf.Sin(defeat * 22f) * (1 - k));
                jaw = Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0, 1, k));                          // 嘴慢慢张到最大
                var mcD = new Color(1f, 0.6f, 0.95f);
                headMpb.SetFloat("_Mouth", 1f); headMpb.SetColor("_MouthColor", Color.Lerp(mcD, Color.white, k * 0.6f));
                headMpb.SetFloat("_Flash", Mathf.Max(0f, 0.55f - k) * 0.8f);
                headMpb.SetFloat("_Alpha", 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.3f) / 0.7f)));   // 后 70% 溶解
                if (headR != null) foreach (var r in headR) r.SetPropertyBlock(headMpb);
                UIKit.GlyphColor(mouthGlow, Color.Lerp(mcD, Color.white, k), (1.4f - k) * 1.2f);
                var emD = suck.emission; emD.rateOverTime = 0f;
                // 嘴里的星云一股股往外喷（分三波），再在头的位置散出一片星尘
                if (fx)
                {
                    if (defeat < 1.5f && Mathf.FloorToInt(defeat / 0.45f) != Mathf.FloorToInt((defeat - dt) / 0.45f))
                        fx.BossVent(boss.position + boss.forward * defeatSize * 0.45f, boss.forward, mcD, defeatSize);
                    if (defeat - dt < 1.6f && defeat >= 1.6f) fx.BossDissolve(boss.position, defeatSize);
                }
                if (defeat >= 2.6f) { defeat = -1f; appear = 0f; boss.gameObject.SetActive(false); }
                return;
            }

            var B = g?.Boss;
            bool want = B != null && B.Alive && g.Chains[B.Ci].Sim.Count > 0 && g.Chains[B.Ci].Sim.Stars[0].S > -1 && (gems ? gems.presence > 0.05f : true);
            appear = Mathf.MoveTowards(appear, want ? 1f : 0f, dt / (want ? 1.2f : 0.8f));   // 出现 / 消失都是溶解
            if (boss) boss.gameObject.SetActive(appear > 0.001f && B != null);
            if (appear <= 0.001f || B == null) return;
            var tr = g.Tracks[B.Ci];
            double U = System.Math.Min(tr.Slots, B.U);
            var bp = runner.ToWorld(tr.PointAtU(U));
            var tg = runner.rig ? runner.rig.TransformDirection(GameRunner.ToLocal(tr.TangentAtU(U))) : GameRunner.ToLocal(tr.TangentAtU(U));
            float size = (float)tr.SizeAtU(U) * 2.6f;
            chewShake = B.State == "chewWarn" ? 1f : Mathf.Max(0, chewShake - dt * 3f);
            hurtFlash = Mathf.Max(0, hurtFlash - dt * 1.8f);
            recoil = Mathf.Max(0, recoil - dt * 2.5f); if (hurtFlash > 0.95f) recoil = 1f;
            float wantJaw = B.State == "open" ? 1f : B.State == "openWarn" ? 0.25f + 0.25f * Mathf.Abs(Mathf.Sin(t * 10f)) : 0f;
            jaw += (wantJaw - jaw) * Mathf.Min(1f, dt * 5f);
            // 朝向：嘴朝星串前进的方向（沿路线切线，水平为主），只略微偏向玩家，让玩家看得到嘴里的颜色
            var tgf = tg.normalized;
            var toCam = cam ? (cam.transform.position - bp).normalized : -tgf;
            var fwd = Vector3.Slerp(tgf, toCam, 0.28f);
            fwd = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized * 0.9f + Vector3.up * fwd.y * 0.1f;
            fwd.Normalize();
            // 头半径约 0.5×size；第一颗星在兽头中心后面 Gap 个星位 → 往前挪，后脑勺离开第一颗星（不穿模）
            float starD = (float)tr.SizeAtU(U);
            // 头贴着第一颗星（11-24 用户：不要离那么远）：只留 3% 星径的缝，不穿模就行
            bp += tgf * Mathf.Max(0f, 0.5f * size - ((float)B.Cfg.Gap - 0.5f) * starD - 0.03f * starD);
            // 头的动作：呼吸起伏；张嘴可喂时先往后仰再往前探（像在吸）；嚼星抖；受击往后撤
            float rear = jaw * (12f * Mathf.Sin(Mathf.Clamp01(jaw) * Mathf.PI) - 6f);
            boss.position = bp + Vector3.up * Mathf.Sin(t * 1.3f) * 0.012f - fwd * recoil * 0.06f + Random.insideUnitSphere * 0.006f * chewShake;
            boss.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(-rear - recoil * 14f + Mathf.Sin(t * 0.9f) * 4f, Mathf.Sin(t * 0.6f) * 6f, Mathf.Sin(t * 0.7f) * 3f + Mathf.Sin(t * 37f) * 3f * chewShake);
            boss.localScale = Vector3.one * size;
            // 材质参数
            var mc = B.MouthColor >= 0 ? Palette.Of(B.MouthColor) : new Color(0.9f, 0.35f, 0.85f);
            headMpb.SetFloat("_Mouth", 0.12f + 0.88f * jaw); headMpb.SetColor("_MouthColor", mc);
            headMpb.SetFloat("_Flash", hurtFlash); headMpb.SetFloat("_Alpha", Mathf.SmoothStep(0, 1, appear));
            if (headR != null) foreach (var r in headR) r.SetPropertyBlock(headMpb);
            UIKit.GlyphColor(mouthGlow, mc, jaw * (1.2f + 0.5f * Mathf.Sin(t * 6f)) * appear + hurtFlash * 1.5f);
            // 嘴前的小魔法阵：张嘴时张开、面朝嘴的方向
            mouthCircle.localScale = Vector3.one * Mathf.Max(0.001f, 0.9f * Mathf.SmoothStep(0, 1, jaw));
            mouthCircle.localRotation = Quaternion.Euler(0, 0, t * 40f);
            mouthCircleMat.SetColor("_ColA", mc); mouthCircleMat.SetColor("_ColB", Color.Lerp(mc, Color.white, 0.6f)); mouthCircleMat.SetFloat("_Intensity", 1.3f * jaw);
            var em = suck.emission; em.rateOverTime = 120f * jaw * appear;
            var main = suck.main; main.startColor = new ParticleSystem.MinMaxGradient(mc, Color.Lerp(mc, Color.white, 0.5f));
            // 血量：头顶三颗金宝石，慢慢绕
            for (int k = 0; k < B.Hp; k++)
            {
                float a = t * 0.8f + k * Mathf.PI * 2f / 3f;
                var hp = boss.position + Vector3.up * size * 0.75f + (boss.right * Mathf.Cos(a) + boss.forward * Mathf.Sin(a) * 0.6f) * size * 0.55f;
                gems.Extra(hp, size * 0.13f * appear, 1, 0.4f, 0.3f + k * 0.2f);
            }
        }
    }
}
