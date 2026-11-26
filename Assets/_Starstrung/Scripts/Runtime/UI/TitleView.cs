using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 封面（11-24 用户选 B 稿：碎玻璃放射）。MR 游戏的封面，不是一张大海报：所有东西按真实尺寸悬在房间里，前后错落，
    /// 透视画面从缝隙里透出来。
    /// - 一米半外：细宋体“摘”“星”（每字约 17 cm）一高一低，两字之间一颗星芒 + 一道横向光芒；
    /// - 五片有厚度的玻璃碎片从星芒放射出去（前后错开、微微倾斜、慢慢漂），碎片里是有纵深的星空，边缘一圈细亮光；
    /// - 更远处很淡的花体 Starstrung；字标左下竖排小字“手可摘星辰”，旁边一行竖着的小号大写英文；
    /// - 胸前是游戏里的星核：脚下两圈透视刻度环，周围几颗四芒小星闪烁，金色碎光慢慢飘落；
    /// - “开始”= 星核旁手绘轨道线上的一颗虹彩宝石星，指着捏。
    /// 出现有过程：星芒点亮 → 碎片炸开停稳 → 字浮现 → 花体、副标题 → 星核周围的刻度环、闪光 → 开始星 → 署名。
    /// </summary>
    public class TitleView : FlowView
    {
        public GemField gems;
        public Material starTextMat, logoMat, shardMat, circleMat, moteMat;
        public Transform core;
        public Vector3 burst = new(0.0f, 1.74f, 1.3f);   // 星芒（字标中心）

        // 字
        UIText zhai, xing, wordEn, script, subZh, subCaps, subEn, beginLabel, creditText;
        readonly Dictionary<TextMeshPro, (TMP_FontAsset font, Material mat)> starMats = new();
        // 星芒
        Renderer flare, streak;
        // 碎片
        class Shard { public Transform t; public Material m; public Vector3 rest, dir; public Quaternion rot; public float delay, len; }
        readonly List<Shard> shards = new();
        // 星核周围
        readonly List<(Transform t, Material m, float r)> dials = new();
        readonly List<(Renderer r, Color c, float ph)> sparks = new();
        ParticleSystem flecks;
        LineRenderer orbit; Vector3 orbitC; float orbitR = 0.27f;
        PickTarget begin, setBtn, exitBtn; Vector3 beginPos, setPos, exitPos;
        UIText setLabel, exitLabel; LineRenderer exitRing; bool settingsOpen; float quitT = -1;
        Transform credit; Renderer logo; LineRenderer creditLine;

        static readonly Color Silver = new(0.9f, 0.93f, 1f);
        static readonly Color Ink = new(0.82f, 0.88f, 1f, 1f);

        void Awake()
        {
            var B = burst;
            // ── 字标 ──
            zhai = UIText.Make(transform, "摘", UIKit.Face.Title, 0.26f, B + new Vector3(-0.17f, -0.025f, 0f), color: Silver, raw: true);
            xing = UIText.Make(transform, "星", UIKit.Face.Title, 0.26f, B + new Vector3(0.18f, 0.045f, -0.02f), color: Silver, raw: true);
            wordEn = UIText.Make(transform, "STARSTRUNG", UIKit.Face.Title, 0.12f, B + new Vector3(0, 0, -0.01f), color: Silver, raw: true);
            wordEn.spacing = 22f; wordEn.Refresh();
            script = UIText.Make(transform, "Starstrung", UIKit.Face.Script, 0.5f, B + new Vector3(0.03f, -0.12f, 0.6f), color: new Color(0.62f, 0.68f, 0.9f), raw: true);
            script.transform.localRotation = Quaternion.Euler(0, 0, 4f);
            subZh = UIText.Make(transform, "手\n可\n摘\n星\n辰", UIKit.Face.Serif, 0.042f, B + new Vector3(-0.36f, -0.46f, -0.08f), color: new Color(0.86f, 0.9f, 1f), raw: true);
            subZh.tmp.lineSpacing = -18f;
            subCaps = UIText.Make(transform, "REACH OUT · PLUCK A STAR", UIKit.Face.Title, 0.018f, B + new Vector3(-0.305f, -0.465f, -0.08f), color: new Color(0.75f, 0.8f, 0.98f), raw: true);
            subCaps.spacing = 26f; subCaps.Refresh();
            subCaps.transform.localRotation = Quaternion.Euler(0, 0, -90f);
            subEn = UIText.Make(transform, "手可摘星辰", UIKit.Face.Italic, 0.04f, B + new Vector3(0, -0.22f, -0.04f), color: new Color(0.84f, 0.88f, 1f));

            // ── 星芒 + 横向光芒 ──
            flare = UIKit.Glyph(transform, "Flare", B + new Vector3(0.005f, 0.01f, -0.04f), 0.32f);
            streak = UIKit.Glyph(transform, "Streak", B + new Vector3(0.005f, 0.01f, -0.045f), 1f);
            streak.transform.localScale = new Vector3(2.2f, 0.07f, 1f);
            streak.sharedMaterial = new Material(UIKit.GlyphMat); streak.sharedMaterial.SetFloat("_Mode", 4); streak.sharedMaterial.SetFloat("_Billboard", 0);
            streak.gameObject.AddComponent<FaceHead>().yawOnly = false;

            // ── 碎片 ──（方向按 B 稿：左上、上偏右、右、左下、下偏左；长短、前后、倾斜都不同）
            (float ang, float len, float wid, float z, float tilt)[] plan =
            {
                (128f, 0.44f, 0.11f, -0.1f, 18f),
                (72f, 0.5f, 0.12f, 0.13f, -14f),
                (18f, 0.58f, 0.1f, 0.05f, 10f),
                (208f, 0.62f, 0.125f, -0.15f, -20f),
                (262f, 0.36f, 0.09f, 0.09f, 12f),
            };
            for (int k = 0; k < plan.Length; k++)
            {
                var pl = plan[k];
                var go = new GameObject("Shard" + k); go.transform.SetParent(transform, false);
                var mesh = ShardMesh(pl.len, pl.wid, k, out var corners);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); var m = new Material(shardMat); mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                m.SetVector("_P01", new Vector4(corners[0].x, corners[0].y, corners[1].x, corners[1].y));
                m.SetVector("_P23", new Vector4(corners[2].x, corners[2].y, corners[3].x, corners[3].y));
                m.SetFloat("_Seed", k * 1.7f + 0.3f);
                var dir = new Vector3(Mathf.Cos(pl.ang * Mathf.Deg2Rad), Mathf.Sin(pl.ang * Mathf.Deg2Rad), 0);
                var rot = Quaternion.Euler(pl.tilt * 0.6f, pl.tilt, pl.ang);
                shards.Add(new Shard { t = go.transform, m = m, dir = dir, rest = B + dir * 0.1f + Vector3.forward * pl.z, rot = rot, delay = k * 0.05f, len = pl.len });
            }

            // ── 星核周围：两圈透视刻度环（只画外环和刻度，手绘笔迹）──
            var c = core ? core.localPosition : new Vector3(0, 1.22f, 0.35f);
            for (int k = 0; k < 2; k++)
            {
                var go = new GameObject("Dial" + k); go.transform.SetParent(transform, false);
                go.transform.localPosition = c + new Vector3(0, -0.26f - k * 0.05f, 0);
                go.transform.localRotation = Quaternion.Euler(90, 0, 0);
                float r = k == 0 ? 0.36f : 0.5f;
                go.transform.localScale = Vector3.one * r * 2f;
                go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                var mr = go.AddComponent<MeshRenderer>(); var m = new Material(circleMat); mr.sharedMaterial = m;
                m.SetVector("_Mask", new Vector4(1, 0, 0, 0)); m.SetFloat("_Seed", 40 + k * 7); m.SetFloat("_Intensity", 0.8f);
                m.SetColor("_ColB", new Color(0.78f, 0.84f, 1f)); m.SetVector("_Spin", new Vector4(k == 0 ? 0.03f : -0.02f, 0, 0, 0));
                dials.Add((go.transform, m, r));
            }
            // 四芒小星：星核周围错落几颗，青 / 紫 / 白
            var rnd = new System.Random(4);
            Color[] sc = { new(0.55f, 0.85f, 1f), new(0.75f, 0.55f, 1f), new(0.95f, 0.95f, 1f) };
            for (int k = 0; k < 11; k++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, rr = 0.24f + (float)rnd.NextDouble() * 0.3f;
                var p = c + new Vector3(Mathf.Cos(a) * rr, -0.18f + (float)rnd.NextDouble() * 0.34f, Mathf.Sin(a) * rr * 0.6f);
                var g = UIKit.Glyph(transform, "Spark", p, 0.025f + (float)rnd.NextDouble() * 0.03f);
                sparks.Add((g, sc[k % 3], (float)rnd.NextDouble() * 6f));
            }
            // 金色碎光：慢慢飘落
            if (moteMat)
            {
                var go = new GameObject("GoldFlecks"); go.transform.SetParent(transform, false);
                go.transform.localPosition = c + new Vector3(0, 0.55f, 0.15f);
                flecks = go.AddComponent<ParticleSystem>();
                flecks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = flecks.main; main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
                main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.009f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.82f, 0.45f), new Color(1f, 0.92f, 0.7f));
                main.gravityModifier = 0.004f; main.maxParticles = 80; main.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = flecks.emission; em.rateOverTime = 7f;
                var sh = flecks.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.9f, 0.5f, 0.6f);
                var vel = flecks.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
                vel.x = new ParticleSystem.MinMaxCurve(-0.01f, 0.01f); vel.y = new ParticleSystem.MinMaxCurve(-0.03f, -0.012f); vel.z = new ParticleSystem.MinMaxCurve(-0.01f, 0.01f);
                var col = flecks.colorOverLifetime; col.enabled = true;
                var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0.7f, 0.7f), new GradientAlphaKey(0, 1) });
                col.color = gr;
                var noise = flecks.noise; noise.enabled = true; noise.strength = 0.01f; noise.frequency = 0.6f;
                go.GetComponent<ParticleSystemRenderer>().sharedMaterial = moteMat;
            }
            // 手绘轨道线（略倾斜的椭圆）+ 开始宝石星
            orbitC = c + new Vector3(0, 0.02f, 0);
            var ell = new List<Vector3>();
            for (int k = 0; k <= 96; k++)
            {
                float a = (k / 96f) * 374f * Mathf.Deg2Rad;   // 多画一点，首尾错开
                ell.Add(orbitC + Quaternion.Euler(0, 0, -9f) * new Vector3(Mathf.Cos(a) * orbitR, 0, Mathf.Sin(a) * orbitR * 0.55f) + Vector3.up * (k / 96f) * 0.006f);
            }
            orbit = UIKit.Line(transform, "Orbit", ell, 0.0017f, Ink);
            // 三颗宝石按钮都在星核的轨道线上：开始（右前，最大）、设置（左前）、退出（左后，捏住不放 1.5 秒）
            Vector3 OnOrbit(float deg) { float a = deg * Mathf.Deg2Rad; return orbitC + Quaternion.Euler(0, 0, -9f) * new Vector3(Mathf.Cos(a) * orbitR, 0, Mathf.Sin(a) * orbitR * 0.55f); }
            beginPos = OnOrbit(-32f);
            begin = Target(transform, "Begin", beginPos, 0.06f, () => flow.Begin());
            beginLabel = UIText.Make(transform, "开始", UIKit.Face.Serif, 0.024f, beginPos + new Vector3(0.05f, 0.03f, -0.02f), TextAlignmentOptions.Left, new Color(0.92f, 0.94f, 1f));
            beginLabel.spacing = 30f; beginLabel.Refresh(); beginLabel.gameObject.AddComponent<FaceHead>();
            setPos = OnOrbit(-148f);
            setBtn = Target(transform, "Settings", setPos, 0.05f, () => { settingsOpen = !settingsOpen; if (flow.settings) flow.settings.SetShown(settingsOpen); });
            setLabel = UIText.Make(transform, "设置", UIKit.Face.Serif, 0.019f, setPos + new Vector3(-0.045f, 0.028f, -0.02f), TextAlignmentOptions.Right, new Color(0.85f, 0.88f, 1f));
            setLabel.spacing = 24f; setLabel.Refresh(); setLabel.gameObject.AddComponent<FaceHead>();
            exitPos = OnOrbit(152f);
            exitBtn = Target(transform, "Exit", exitPos, 0.045f, Quit, 1.5f);
            exitLabel = UIText.Make(transform, "退出", UIKit.Face.Serif, 0.017f, exitPos + new Vector3(-0.04f, 0.03f, 0f), TextAlignmentOptions.Right, new Color(0.8f, 0.84f, 1f));
            exitLabel.spacing = 24f; exitLabel.Refresh(); exitLabel.gameObject.AddComponent<FaceHead>();
            exitRing = UIKit.Line(transform, "ExitRing", new List<Vector3> { exitPos, exitPos }, 0.0018f, Ink);
            if (flow && flow.settings) flow.settings.anchor = setPos;

            // ── 公司署名：字标右下方（标志在前，文字另起一行），醒目但不抢字标 ──
            credit = new GameObject("Credit").transform; credit.SetParent(transform, false);
            credit.localPosition = B + new Vector3(0.3f, -0.43f, -0.08f);
            if (logoMat)
            {
                var lg = new GameObject("TripoLogo"); lg.transform.SetParent(credit, false);
                var tex = logoMat.mainTexture; float aspect = tex ? tex.width / (float)tex.height : 4f;
                float lh = 0.062f;   // 标志图片四周有留白，实际字形约占一半高
                lg.transform.localPosition = new Vector3(-lh * aspect * 0.5f, 0.02f, 0);   // 右对齐到 credit 点
                lg.transform.localScale = new Vector3(lh * aspect, lh, 1);
                lg.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                logo = lg.AddComponent<MeshRenderer>(); logo.sharedMaterial = new Material(logoMat);
                logo.sharedMaterial.SetColor("_Color", new Color(1f, 1f, 1f));
                creditLine = UIKit.Line(credit, "CreditLine", UIKit.HandPolyline(new[] { new Vector3(-lh * aspect - 0.14f, 0.02f, 0), new Vector3(-lh * aspect - 0.015f, 0.02f, 0) }, Vector3.back, 91), 0.0012f, Ink);
            }
            creditText = UIText.Make(credit, "Tripo XR 出品", UIKit.Face.Brand, 0.017f, new Vector3(-0.01f, -0.014f, 0), TextAlignmentOptions.Right, new Color(0.85f, 0.87f, 0.95f));
            creditText.spacing = 12f; creditText.Refresh();
        }

        /// <summary>斜长的玻璃碎片：一头窄一头宽、边不直，带 4 mm 厚度。物体空间从原点沿 +x 伸出去。</summary>
        static Mesh ShardMesh(float len, float wid, int seed, out Vector2[] c)
        {
            var r = new System.Random(seed * 7 + 3);
            float J() => (float)r.NextDouble() - 0.5f;
            c = new[]
            {
                new Vector2(0f, wid * (0.15f + 0.1f * J())),
                new Vector2(len * (0.95f + 0.08f * J()), wid * (0.55f + 0.2f * J())),
                new Vector2(len * (0.8f + 0.1f * J()), -wid * (0.45f + 0.2f * J())),
                new Vector2(len * 0.04f, -wid * (0.12f + 0.08f * J())),
            };
            const float th = 0.004f;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector3 nn) { int i = v.Count; v.AddRange(new[] { a, b, cc, d }); n.AddRange(new[] { nn, nn, nn, nn }); tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); }
            Vector3 F(Vector2 p) => new(p.x, p.y, -th / 2);
            Vector3 Bk(Vector2 p) => new(p.x, p.y, th / 2);
            Quad(F(c[0]), F(c[1]), F(c[2]), F(c[3]), Vector3.back);    // 正面（朝玩家）
            Quad(Bk(c[3]), Bk(c[2]), Bk(c[1]), Bk(c[0]), Vector3.forward);
            for (int k = 0; k < 4; k++)
            {
                var a = c[k]; var b = c[(k + 1) % 4];
                var e = (b - a).normalized; var nn = new Vector3(e.y, -e.x, 0);
                Quad(F(a), Bk(a), Bk(b), F(b), nn);
            }
            var m = new Mesh { name = "Shard" + seed };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }

        void Quit()
        {
            quitT = 0f;   // 先淡出，再退出（有过程）
        }

        public override void SetShown(bool s)
        {
            if (!s && settingsOpen) { settingsOpen = false; if (flow && flow.settings) flow.settings.SetShown(false); }
            base.SetShown(s);
            if (flecks) { if (s) flecks.Play(); else flecks.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
        }

        static float Ramp(float t, float a, float dur) => Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - a) / dur));
        static float OutBack(float x) { const float c1 = 1.4f, c3 = c1 + 1; return 1 + c3 * Mathf.Pow(x - 1, 3) + c1 * Mathf.Pow(x - 1, 2); }

        void StarMat(UIText t, float alpha)
        {
            var tmp = t.tmp;
            if (!starMats.TryGetValue(tmp, out var e) || e.font != tmp.font)
            {
                var m = new Material(starTextMat) { mainTexture = tmp.font.atlasTexture };
                // 香槟白 → 淡银紫；亮度压在 0.9 左右（不触发大片泛光，字边才清楚）
                m.SetColor("_ColA", new Color(1f, 0.95f, 0.86f)); m.SetColor("_ColB", new Color(0.82f, 0.8f, 0.96f)); m.SetColor("_ColC", new Color(1f, 0.9f, 0.7f));
                m.SetFloat("_Reveal", -10f); m.SetFloat("_Glow", 0f); m.SetFloat("_Intensity", 1.05f);
                tmp.fontSharedMaterial = m; e = (tmp.font, m); starMats[tmp] = e;
            }
            e.mat.SetFloat("_Alpha", alpha);
            t.alpha = alpha > 0.003f ? 1f : 0f;
        }

        protected override void Apply(float v, float dt)
        {
            if (vis <= 0f && !shown) { if (gameObject.activeSelf) gameObject.SetActive(false); return; }
            float s = shownT, t = Time.time;
            bool en = Loc.English;

            // 星芒：先点亮，之后轻轻呼吸
            float fa = Ramp(s, 0f, 0.5f) * v;
            float tw = 0.85f + 0.15f * Mathf.Sin(t * 2.3f) + 0.25f * Mathf.Exp(-Mathf.Max(0, s - 0.3f) * 3f);
            flare.transform.localScale = Vector3.one * 0.32f * (0.6f + 0.4f * fa) * tw;
            UIKit.GlyphColor(flare, new Color(0.95f, 0.97f, 1f), 1.4f * fa);
            streak.transform.localScale = new Vector3(2.2f * (0.3f + 0.7f * Ramp(s, 0.1f, 0.7f)), 0.07f, 1f);
            var sm = streak.sharedMaterial; sm.SetColor("_Color", new Color(0.85f, 0.92f, 1f)); sm.SetColor("_Color2", new Color(0.45f, 0.5f, 1f)); sm.SetFloat("_Intensity", 0.9f * fa * tw);
            streak.enabled = fa > 0.003f;

            // 碎片：从星芒炸开，停稳后慢慢漂
            var B = burst;
            for (int k = 0; k < shards.Count; k++)
            {
                var sh = shards[k];
                float e = Mathf.Clamp01((s - 0.3f - sh.delay) / 0.8f);
                float pos = e > 0 ? OutBack(e) : 0f;
                var drift = new Vector3(Mathf.Sin(t * 0.31f + k), Mathf.Sin(t * 0.27f + k * 2f), Mathf.Sin(t * 0.23f + k * 3f)) * 0.006f;
                sh.t.localPosition = Vector3.Lerp(B, sh.rest, pos) + drift * pos;
                sh.t.localRotation = sh.rot * Quaternion.Euler(Mathf.Sin(t * 0.4f + k) * 2.5f, Mathf.Sin(t * 0.33f + k * 1.7f) * 3f, 0);
                sh.t.localScale = Vector3.one * Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(e * 1.5f));
                sh.m.SetFloat("_Alpha", Mathf.Clamp01(e * 3f) * v);
                sh.t.gameObject.SetActive(e > 0 && v > 0.003f);
            }

            // 字标
            float wa = Ramp(s, 0.6f, 1.0f) * v;
            StarMat(zhai, en ? 0 : wa); StarMat(xing, en ? 0 : wa); StarMat(wordEn, en ? wa : 0);
            float rise = (1 - Ramp(s, 0.6f, 1.2f)) * -0.015f;
            zhai.transform.localPosition = B + new Vector3(-0.17f, -0.025f + rise, 0f);
            xing.transform.localPosition = B + new Vector3(0.18f, 0.045f + rise, -0.02f);
            script.alpha = Ramp(s, 1.0f, 1.2f) * v * 0.11f;
            subZh.alpha = en ? 0 : Ramp(s, 1.4f, 0.8f) * v * 0.9f;
            subCaps.alpha = en ? 0 : Ramp(s, 1.6f, 0.8f) * v * 0.75f;
            subEn.alpha = en ? Ramp(s, 1.4f, 0.8f) * v * 0.85f : 0;

            // 星核周围
            float ca = Ramp(s, 1.8f, 0.9f) * v;
            for (int k = 0; k < dials.Count; k++) dials[k].m.SetFloat("_Intensity", 0.75f * ca * (k == 0 ? 1f : 0.6f));
            foreach (var (r, c, ph) in sparks) UIKit.GlyphColor(r, c, ca * (0.35f + 0.65f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 1.3f + ph * 3f), 3f)));
            UIKit.SetAlpha(orbit, Ink, Ramp(s, 2.0f, 0.8f) * v * 0.55f);
            float ba = Ramp(s, 2.2f, 0.8f) * v;
            void Gem(PickTarget p, Vector3 pos, float size, int color, float seed, float delay)
            {
                float a = Ramp(s, 2.2f + delay, 0.8f) * v, h = p.hover;
                gems.Extra(transform.TransformPoint(pos + Vector3.up * Mathf.Sin(t * 1.6f + seed * 9f) * 0.005f), (size + size * 0.25f * h) * a, color, h, seed);
                p.allowed = s > 2.5f + delay;
            }
            Gem(begin, beginPos, 0.046f, -1, 0.71f, 0f);
            Gem(setBtn, setPos, 0.032f, 3, 0.37f, 0.15f);
            Gem(exitBtn, exitPos, 0.026f, 4, 0.53f, 0.3f);
            beginLabel.alpha = ba * (0.75f + 0.25f * begin.hover);
            setLabel.alpha = Ramp(s, 2.35f, 0.8f) * v * (settingsOpen ? 1f : 0.6f + 0.4f * setBtn.hover);
            bool exitHot = exitBtn.hover > 0.5f || exitBtn.holdT > 0;
            exitLabel.Set(exitHot ? "捏住不放 · 退出" : "退出");
            exitLabel.alpha = Ramp(s, 2.5f, 0.8f) * v * (0.55f + 0.45f * exitBtn.hover);
            float prog = Mathf.Clamp01(exitBtn.holdT / exitBtn.holdSeconds);
            if (prog > 0.01f)
            {
                var arc = UIKit.HandCircle(exitPos, Vector3.back, 0.03f, 5, 90f, -360f * prog, 48);
                exitRing.positionCount = arc.Count; exitRing.SetPositions(arc.ToArray());
                UIKit.SetAlpha(exitRing, Ink, v);
            }
            else UIKit.SetAlpha(exitRing, Ink, 0f);
            if (quitT >= 0)
            {
                quitT += dt;
                if (flow && flow.sky) flow.sky.veil = 1f;
                if (quitT > 1.2f)
                {
                    quitT = -1;
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                }
            }

            float cr = Ramp(s, 1.6f, 1.0f) * v;
            // 署名像一行落款：不抢字标，亮度比字标低一截，随星芒轻轻明暗
            float glow = 0.55f + 0.08f * Mathf.Sin(t * 0.9f);
            creditText.alpha = cr * 0.55f;
            if (logo) { logo.sharedMaterial.SetFloat("_Alpha", cr * glow); logo.enabled = cr > 0.003f; }
            if (creditLine) UIKit.SetAlpha(creditLine, Ink, cr * 0.35f);
        }
    }
}
