using System.Collections.Generic;
using Starstrung.Logic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 结局（通关 4-5 后）。11-25 按用户认可的分析重做：玩家亲手摘下最后一颗星，满天星是自己这一路攒下的。
    /// 文字只有一句“手可摘星辰”、署名、两个字的提示“捏住它”和每个星座旁一行很小的关名。
    /// 时间线（s = 结局开始后的秒数；u = 星在手里炸开后的秒数）：
    ///  s 0～1.5   星串、路线收回；
    ///  s 1.5～5   星核蓄力：裂纹爬满、星环越转越快、合唱升起（封面曲同时淡出）；
    ///  s 5～6     完全寂静：所有声音停住，星核静静发着暗光（之后的爆开才有冲击力）；
    ///  s 6        碎裂：水晶碎片飞散（闪光克制，护眼），一颗金色的星从核心飘到玩家手边；
    ///  等玩家     捏住它（远指、近捏都行）→ 星跟着手 → 松开（或捏住 2.5 秒）→ 在手里炸开；20 秒没捏就自己炸开（不卡住玩家）；
    ///  u 0～6     几千颗星从玩家手里螺旋飞出铺满房间；结局曲起；
    ///  u 0.5～8   时空裂缝张满整个空间、边缘消失；夜蓝罩加浓、关掉视频透视：房间完全变成裂缝后面的星空天空盒（一直保持到返回）；
    ///  u 6.5～16  天上逐个连出这一路的 20 个星座（每关一个，旁边一行小字关名；三星通关的是金色）；全三星再多一圈金色“星冠”；
    ///  u 16 / 18 / 19.5  “手可摘星辰” → 署名 → 返回宝石；
    /// </summary>
    public class EndingView : FlowView
    {
        public GemField gems;
        public CoreView core;
        public RiftView rifts;
        public SkyView sky;
        public Material starMat, shardMat, starTextMat, logoMat;
        public Mesh shardMesh;
        public int starCount = 5200;

        Transform skyRoot; Material starInst;
        readonly List<(Transform t, Vector3 v, Vector3 spin)> shards = new();
        Renderer bigFlash, offerGlow;
        UIText backLabel, hint; PickTarget back, offer;
        // 结尾字标（11-25 用户：做得更精致、署名要有图标）
        Transform lockup; UIText zhai, xing, chen, shouKe, script, caps, creditText, mainEn;
        Renderer glint, logo; LineRenderer pluckLine, dialArc, creditLine; List<Vector3> pluckPts, arcPts;
        readonly Dictionary<TMPro.TextMeshPro, Material> starMats = new();
        readonly List<(LineRenderer lr, List<Vector3> pts, float at, Color col, UIText label)> constellations = new();
        LineRenderer crown; List<Vector3> crownPts;
        Vector3 C, backPos;
        bool shattered;
        static readonly Color Ink = new(0.86f, 0.9f, 1f, 1f);
        static readonly Color Gold = new(1f, 0.84f, 0.5f, 1f);

        // 最后一颗星
        enum Ph { Charge, Offer, Held, Burst }
        Ph ph;
        Vector3 offerW, starW;           // 停在手边的位置 / 星这一帧的位置（世界）
        float offerT, heldT, burstAt = -1f;
        int heldSide = -1;
        bool silenced, passOff, allThree;
        GameRunner runner; MRRig mr;

        void Awake()
        {
            C = core ? core.transform.localPosition : new Vector3(0, 1.22f, 0.35f);
            skyRoot = new GameObject("EndingSky").transform; skyRoot.SetParent(transform, false);
            bigFlash = UIKit.Glyph(transform, "ShatterFlash", C, 1.4f);
            offerGlow = UIKit.Glyph(transform, "OfferGlow", C, 0.2f);
            BuildLockup();
            hint = UIText.Make(transform, "捏住它", UIKit.Face.Serif, 0.016f, C, color: new Color(0.95f, 0.9f, 0.78f));
            hint.spacing = 30f; hint.Refresh(); hint.gameObject.AddComponent<FaceHead>();
            backPos = C + new Vector3(0, 0.02f, 0);
            back = Target(transform, "EndBack", backPos, 0.06f, () => flow.BackToTitle());
            backLabel = UIText.Make(transform, "返回", UIKit.Face.Serif, 0.02f, backPos + new Vector3(0, -0.06f, 0), color: new Color(0.88f, 0.9f, 1f));
            backLabel.spacing = 26f; backLabel.Refresh(); backLabel.gameObject.AddComponent<FaceHead>();
            offer = Target(transform, "LastStar", C, 0.07f, OnPluck);
            fadeIn = 0.01f; fadeOut = 1.2f;
        }

        // ───────── 结尾字标 ─────────
        // 和封面同一套字体语言（星空填充的细宋体），但更安静：1.6 m 外、略高于视线，“摘 · 星 · 辰”高低大小错落，
        // “手可”竖排小字在左上起笔；星的右上一颗呼吸的四芒小星，一道手绘细弧从它落到“摘”（摘下的那一下）；
        // 背后极淡的花体英文，下面一行小号大写、一段刻度弧；右下 TRIPO 标志（图标 + 字）+ “Tripo XR 出品”。
        static readonly Color Silver = new(0.9f, 0.93f, 1f);
        void BuildLockup()
        {
            lockup = new GameObject("EndLockup").transform; lockup.SetParent(transform, false);
            lockup.localPosition = new Vector3(0f, 1.74f, 1.6f);
            lockup.localScale = Vector3.one * 1.6f;   // 头显 90° 视场里要有分量（字号参数不是字形实高，1.6 倍后“星”约 10°）
            zhai = UIText.Make(lockup, "摘", UIKit.Face.Title, 0.19f, new Vector3(-0.2f, -0.03f, 0f), color: Silver, raw: true);
            xing = UIText.Make(lockup, "星", UIKit.Face.Title, 0.22f, new Vector3(0.03f, 0.05f, -0.02f), color: Silver, raw: true);
            chen = UIText.Make(lockup, "辰", UIKit.Face.Title, 0.13f, new Vector3(0.235f, -0.06f, 0.01f), color: Silver, raw: true);
            shouKe = UIText.Make(lockup, "手\n可", UIKit.Face.Serif, 0.075f, new Vector3(-0.4f, 0.075f, -0.03f), color: new Color(0.86f, 0.9f, 1f), raw: true);
            shouKe.tmp.lineSpacing = -10f;   // 11-25 用户：“手可”别小到看不见；1.6 m 外最小约 2.2 cm 才看得清，这里 7.5 cm
            script = UIText.Make(lockup, "Reach out, pluck a star", UIKit.Face.Script, 0.17f, new Vector3(0.06f, -0.14f, 0.45f), color: new Color(0.62f, 0.68f, 0.92f), raw: true);
            script.transform.localRotation = Quaternion.Euler(0, 0, 5f);
            caps = UIText.Make(lockup, "REACH OUT · PLUCK A STAR", UIKit.Face.Title, 0.024f, new Vector3(0.01f, -0.2f, -0.02f), color: new Color(0.75f, 0.8f, 0.98f), raw: true);
            caps.spacing = 26f; caps.Refresh();
            mainEn = UIText.Make(lockup, "Reach out, pluck a star", UIKit.Face.Script, 0.13f, new Vector3(0f, 0f, 0f), color: Silver, raw: true);
            // 四芒小星 + 摘星的细弧
            glint = UIKit.Glyph(lockup, "Glint", new Vector3(0.15f, 0.165f, -0.05f), 0.09f);
            pluckPts = UIKit.HandPolyline(new[] { new Vector3(0.14f, 0.15f, -0.05f), new Vector3(-0.02f, 0.2f, -0.05f), new Vector3(-0.17f, 0.12f, -0.05f) }, Vector3.back, 501, false, 18);
            pluckLine = UIKit.Line(lockup, "PluckArc", new List<Vector3> { pluckPts[0], pluckPts[0] }, 0.0012f, Silver);
            // 刻度弧（下方一段，像星盘的边）
            arcPts = UIKit.HandCircle(new Vector3(0.01f, 0.38f, 0.04f), Vector3.back, 0.6f, 502, 246f, 48f, 90);
            dialArc = UIKit.Line(lockup, "DialArc", new List<Vector3> { arcPts[0], arcPts[0] }, 0.0012f, Silver);
            // 署名：右下，标志在上（图标 + TRIPO），“Tripo XR 出品”另起一行；前面一道细线引过来
            var credit = new GameObject("EndCredit").transform; credit.SetParent(lockup, false); credit.localPosition = new Vector3(0.4f, -0.3f, -0.02f);
            if (logoMat)
            {
                var tex = logoMat.mainTexture; float aspect = tex ? tex.width / (float)tex.height : 4f; float lh = 0.07f;
                var lg = new GameObject("TripoLogo"); lg.transform.SetParent(credit, false);
                lg.transform.localPosition = new Vector3(-lh * aspect * 0.5f, 0.022f, 0); lg.transform.localScale = new Vector3(lh * aspect, lh, 1);
                lg.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                logo = lg.AddComponent<MeshRenderer>(); logo.sharedMaterial = new Material(logoMat); logo.sharedMaterial.SetColor("_Color", Color.white);
                creditLine = UIKit.Line(credit, "CreditLine", UIKit.HandPolyline(new[] { new Vector3(-lh * aspect - 0.14f, 0.022f, 0), new Vector3(-lh * aspect - 0.014f, 0.022f, 0) }, Vector3.back, 503), 0.001f, Silver);
            }
            creditText = UIText.Make(credit, "Tripo XR 出品", UIKit.Face.Brand, 0.022f, new Vector3(-0.01f, -0.018f, 0), TMPro.TextAlignmentOptions.Right, new Color(0.85f, 0.87f, 0.95f));
            creditText.spacing = 12f; creditText.Refresh();
        }

        void StarMat(UIText t, float alpha)
        {
            var tmp = t.tmp;
            if (!starTextMat) { t.alpha = alpha; return; }
            if (!starMats.TryGetValue(tmp, out var m) || m.mainTexture != tmp.font.atlasTexture)
            {
                m = new Material(starTextMat) { mainTexture = tmp.font.atlasTexture };
                m.SetColor("_ColA", new Color(1f, 0.95f, 0.86f)); m.SetColor("_ColB", new Color(0.82f, 0.8f, 0.96f)); m.SetColor("_ColC", new Color(1f, 0.9f, 0.7f));
                m.SetFloat("_Reveal", -10f); m.SetFloat("_Glow", 0f); m.SetFloat("_Intensity", 1.0f);
                tmp.fontSharedMaterial = m; starMats[tmp] = m;
            }
            m.SetFloat("_Alpha", alpha);
            t.alpha = alpha > 0.003f ? 1f : 0f;
        }

        static void Draw(LineRenderer lr, List<Vector3> pts, float e, Color c, float a)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(pts.Count * e));
            lr.positionCount = n; for (int i = 0; i < n; i++) lr.SetPosition(i, pts[Mathf.Min(i, pts.Count - 1)]);
            UIKit.SetAlpha(lr, c, a);
        }

        void ApplyLockup(float u, float v, float t)
        {
            bool en = Loc.English;
            float R(float a, float d) => u >= 0f ? Ramp(u, a, d) : 0f;
            // 四芒小星先亮（轻，护眼），之后慢慢呼吸
            float ga = R(15.4f, 1.0f) * v;
            glint.transform.localScale = Vector3.one * 0.09f * (0.85f + 0.15f * Mathf.Sin(t * 1.7f));
            UIKit.GlyphColor(glint, new Color(1f, 0.95f, 0.85f), 0.75f * ga);
            // 三个大字依次浮现（高低错落、各自微微上浮停稳）
            float a1 = R(16.0f, 1.3f) * v, a2 = R(16.35f, 1.3f) * v, a3 = R(16.7f, 1.3f) * v;
            StarMat(zhai, en ? 0 : a1); StarMat(xing, en ? 0 : a2); StarMat(chen, en ? 0 : a3);
            zhai.transform.localPosition = new Vector3(-0.2f, -0.03f - (1 - R(16.0f, 1.6f)) * 0.015f, 0f);
            xing.transform.localPosition = new Vector3(0.03f, 0.05f - (1 - R(16.35f, 1.6f)) * 0.015f, -0.02f);
            chen.transform.localPosition = new Vector3(0.235f, -0.06f - (1 - R(16.7f, 1.6f)) * 0.015f, 0.01f);
            mainEn.alpha = en ? R(16.0f, 1.6f) * v * 0.9f : 0f;
            shouKe.alpha = en ? 0f : R(17.2f, 1.0f) * v;
            Draw(pluckLine, pluckPts, R(17.0f, 1.4f), Silver, R(17.0f, 0.4f) * v * 0.45f);
            script.alpha = en ? 0f : R(17.4f, 1.6f) * v * 0.06f;   // 只是底纹
            caps.alpha = R(17.9f, 1.0f) * v * 0.75f;
            Draw(dialArc, arcPts, R(17.6f, 2.0f), Silver, R(17.6f, 0.4f) * v * 0.22f);
            // 署名
            float cr = R(18.8f, 1.2f) * v;
            creditText.alpha = cr * 0.8f;
            if (logo) { logo.sharedMaterial.SetFloat("_Alpha", cr * 0.9f); logo.enabled = cr > 0.003f; }
            if (creditLine) UIKit.SetAlpha(creditLine, Silver, cr * 0.35f);
        }

        // ───────── 满天星（origin = 从哪里飞出：玩家的手）─────────
        void BuildSky(Vector3 originW)
        {
            foreach (Transform c in skyRoot) Destroy(c.gameObject);
            constellations.Clear(); crown = null;
            var rnd = new System.Random(2026);
            float R() => (float)rnd.NextDouble();
            int n = starCount;
            var v = new Vector3[n * 4]; var uv = new Vector2[n * 4]; var d = new Vector4[n * 4]; var to = new Vector4[n * 4]; var col = new Color[n * 4]; var tri = new int[n * 6];
            Color[] jewel = { Palette.Of(0), Palette.Of(1), Palette.Of(2), Palette.Of(3), Palette.Of(4) };
            var bright = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                bool cross = R() < 0.12f;
                // 终点：玩家周围的上半空间（仰角 -10°～85°，距离 1.4～5.5 m），一半集中在前方 ±80°，一半铺满四周
                float az = R() < 0.5f ? (R() - 0.5f) * 160f : R() * 360f, el = Mathf.Lerp(-10f, 85f, Mathf.Pow(R(), 0.9f)), dist = Mathf.Lerp(1.4f, 5.5f, Mathf.Sqrt(R()));
                var target = transform.TransformPoint(new Vector3(0, 1.6f, 0) + Quaternion.Euler(-el, az, 0) * Vector3.forward * dist);
                var start = originW + Random.insideUnitSphere * 0.03f;
                float size = cross ? Mathf.Lerp(0.02f, 0.055f, R()) * (dist / 3f) : Mathf.Lerp(0.008f, 0.018f, R()) * (dist / 3f);
                var data = new Vector4(size, R() * 6.28f, Mathf.Lerp(1.6f, 6.5f, R()), cross ? 1 : 0);
                float delay = Mathf.Pow(R(), 1.6f) * 0.45f;
                var c = R() < 0.55f ? Color.Lerp(new Color(0.8f, 0.9f, 1f), new Color(1f, 0.95f, 0.85f), R()) : Color.Lerp(jewel[rnd.Next(5)], Color.white, 0.3f);
                if (!cross) c *= Mathf.Lerp(0.6f, 1f, R());
                for (int k = 0; k < 4; k++)
                {
                    v[i * 4 + k] = start; d[i * 4 + k] = data; to[i * 4 + k] = new Vector4(target.x, target.y, target.z, delay); col[i * 4 + k] = c;
                    uv[i * 4 + k] = new Vector2(k == 1 || k == 2 ? 1 : 0, k >= 2 ? 1 : 0);
                }
                int b = i * 4, t = i * 6;
                tri[t] = b; tri[t + 1] = b + 2; tri[t + 2] = b + 1; tri[t + 3] = b; tri[t + 4] = b + 3; tri[t + 5] = b + 2;
                if (cross && dist > 2.2f && el > 5f && el < 70f) bright.Add(target);
            }
            var m = new Mesh { name = "EndingStars", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = v; m.uv = uv; m.SetUVs(1, d); m.SetUVs(2, to); m.colors = col; m.triangles = tri;
            m.bounds = new Bounds(transform.TransformPoint(new Vector3(0, 1.6f, 0)), Vector3.one * 14f);
            skyRoot.localPosition = Vector3.zero; skyRoot.localRotation = Quaternion.identity;
            var go = new GameObject("Stars"); go.transform.SetParent(skyRoot, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var rend = go.AddComponent<MeshRenderer>(); starInst = new Material(starMat); rend.sharedMaterial = starInst;
            starInst.SetVector("_Center", originW); starInst.SetFloat("_K", 0);
            BuildConstellations(bright);
        }

        // 这一路的 20 个星座：每关一个，沿玩家前方一道宽弧从左到右排开（关卡顺序 = 走过的路）；
        // 三星通关的画成金色；形状由关卡 id 决定（每关固定、各不相同）
        void BuildConstellations(List<Vector3> bright)
        {
            var eye = transform.TransformPoint(new Vector3(0, 1.6f, 0));
            var used = new HashSet<int>();
            var levels = new List<Level>();
            foreach (var L in Levels.LEVELS) if (!L.Tutorial && !L.Endless) levels.Add(L);
            allThree = levels.Count > 0;
            for (int i = 0; i < levels.Count; i++)
            {
                var L = levels[i];
                int stars = Progress.BestStars(L.Id);
                if (stars < 3) allThree = false;
                var rnd = new System.Random(L.Id.GetHashCode() & 0x7fffffff);
                // 位置：前方 -110°～110°，高低交错
                float az = Mathf.Lerp(-110f, 110f, (i + 0.5f) / levels.Count) + (float)(rnd.NextDouble() - 0.5) * 6f;
                float el = 18f + (i % 2) * 16f + (float)rnd.NextDouble() * 10f;
                var dir = transform.TransformDirection(Quaternion.Euler(-el, az, 0) * Vector3.forward);
                // 起点：离这个方向最近的一颗亮星
                int s0 = -1; float best = 999f;
                for (int j = 0; j < bright.Count; j++) { if (used.Contains(j)) continue; float a = Vector3.Angle(bright[j] - eye, dir); if (a < best) { best = a; s0 = j; } }
                if (s0 < 0 || best > 12f) continue;
                var chain = new List<Vector3> { bright[s0] }; used.Add(s0);
                int want = 3 + stars + rnd.Next(2);   // 星级越高，星座越大
                for (int k = 1; k < want; k++)
                {
                    int nb = -1; float bd = 1.2f;
                    for (int j = 0; j < bright.Count; j++)
                    {
                        if (used.Contains(j)) continue;
                        float dd = Vector3.Distance(bright[j], chain[^1]);
                        // 不往回折、角度离本关方向不能太远（星座之间不打架）
                        if (dd < bd && dd > 0.3f && Vector3.Angle(bright[j] - eye, dir) < 14f) { bd = dd; nb = j; }
                    }
                    if (nb < 0) break; used.Add(nb); chain.Add(bright[nb]);
                }
                if (chain.Count < 3) continue;
                var pts = UIKit.HandPolyline(chain, (chain[0] - eye).normalized, 300 + i, false, 10);
                for (int q = 0; q < pts.Count; q++) pts[q] = skyRoot.InverseTransformPoint(pts[q]);
                var c = stars >= 3 ? Gold : Ink;
                var lr = UIKit.Line(skyRoot, "Constellation " + L.Id, new List<Vector3> { pts[0], pts[0] }, 0.007f, c);
                // 关名：星座第一颗星下方一行很小的字
                var lab = UIText.Make(skyRoot, "", UIKit.Face.Serif, 0.06f * Vector3.Distance(eye, chain[0]) / 3f, skyRoot.InverseTransformPoint(chain[0] + Vector3.down * 0.12f), color: c, raw: true);
                lab.Set(Loc.Name(L)); lab.spacing = 12f; lab.Refresh(); lab.gameObject.AddComponent<FaceHead>(); lab.alpha = 0f;
                constellations.Add((lr, pts, 6.5f + i * 0.45f, c, lab));
            }
            // 全三星：头顶一圈金色“星冠”（手绘圆，慢慢画出来）
            if (allThree)
            {
                crownPts = UIKit.HandCircle(new Vector3(0, 3.3f, 0), Vector3.up, 2.1f, 777, 0, 360, 160);
                crown = UIKit.Line(skyRoot, "Crown", new List<Vector3> { crownPts[0], crownPts[0] }, 0.012f, Gold);
            }
        }

        /// <summary>调试 / 自动测试：星在手里炸开后过了多久（秒，没炸开 = -1）；最后一颗星能不能捏了</summary>
        public float BurstAge => burstAt >= 0f ? shownT - burstAt : -1f;
        public bool Offering => ph == Ph.Offer && offerT > 1.5f;

        public void Play()
        {
            runner = FindAnyObjectByType<GameRunner>(); mr = FindAnyObjectByType<MRRig>();
            ph = Ph.Charge; burstAt = -1f; heldSide = -1; offerT = heldT = 0f;
            silenced = passOff = false;
            shattered = false;
            foreach (Transform c in skyRoot) Destroy(c.gameObject);
            constellations.Clear(); crown = null; starInst = null;
            SetShown(true);
            AudioKit.Instance?.PlayClip(AudioKit.Instance.choirSwell, 0.6f, 1.6f);
            AudioKit.Instance?.PlayClip(AudioKit.Instance.choirSwell, 0.5f, 3.0f, 0.75f);
        }


        void Shatter()
        {
            shattered = true;
            if (core) core.gameObject.SetActive(false);
            var coreW = transform.TransformPoint(C);
            if (shardMesh && shardMat)
                for (int k = 0; k < 26; k++)
                {
                    var go = new GameObject("Shard"); go.transform.SetParent(transform, false); go.transform.position = coreW + Random.insideUnitSphere * 0.05f;
                    go.AddComponent<MeshFilter>().sharedMesh = shardMesh;
                    var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = shardMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    go.transform.localScale = new Vector3(0.02f, 0.05f, 0.02f) * Random.Range(0.6f, 1.4f);
                    go.transform.rotation = Random.rotation;
                    var dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 0.6f + 0.1f;
                    shards.Add((go.transform, dir.normalized * Random.Range(0.5f, 1.3f), Random.insideUnitSphere * 300f));
                }
            AudioKit.Instance?.PlayClip(AudioKit.Instance.coreHit, 0.9f);
            // 最后一颗星停在玩家手边：胸前、略低、离身体 38 cm（伸手就够得着，也可以远指捏）
            var cam = Camera.main;
            if (cam)
            {
                var f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up); if (f.sqrMagnitude < 1e-4f) f = transform.forward;
                offerW = cam.transform.position + f.normalized * 0.38f + Vector3.down * 0.3f;
            }
            else offerW = transform.TransformPoint(new Vector3(0, 1.25f, 0.38f));
            starW = coreW;
            ph = Ph.Offer; offerT = 0f;
        }

        void OnPluck()
        {
            if (ph != Ph.Offer || offerT < 1.5f) return;
            ph = Ph.Held; heldT = 0f; heldSide = -1;
            if (runner)
            {
                runner.ReadHands(out var l, out var r);
                float dl = l.tracked && l.pinch ? Vector3.Distance(l.pinchPoint, starW) : 99f, dr = r.tracked && r.pinch ? Vector3.Distance(r.pinchPoint, starW) : 99f;
                heldSide = dl < dr ? 0 : dr < 99f ? 1 : -1;
            }
            AudioKit.Instance?.PlayAt(starW, 26f, 0.3f, 0.8f);
        }

        void Burst()
        {
            ph = Ph.Burst; burstAt = shownT;
            BuildSky(starW);
            AudioKit.Instance?.StartEnding();
            AudioKit.Instance?.PlayClipAt(starW, AudioKit.Instance.win, 0.8f);
        }

        static float Ramp(float t, float a, float d) => Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - a) / d));

        protected override void Apply(float v, float dt)
        {
            if (vis <= 0f && !shown) { if (gameObject.activeSelf) gameObject.SetActive(false); return; }
            float s = shownT, t = Time.time;
            // 路线收回入口（1.5 秒）
            if (s < 2f) AppFlow.SetGrow(Mathf.Lerp(40f, 0f, Ramp(s, 0f, 1.5f)));
            // 蓄力 → 寂静一秒 → 碎裂
            if (core && !shattered)
            {
                core.endingCharge = Ramp(s, 1.5f, 3.3f);
                if (s >= 5f && !silenced) { silenced = true; AudioKit.Instance?.Silence(true); }
                if (s >= 6f) { AudioKit.Instance?.Silence(false); Shatter(); }
            }
            // 碎裂闪光（护眼：峰值压低、亮一下就收；寂静的那一秒星核只剩暗暗的光）
            float since = s - 6f;
            float fl = shattered ? Mathf.Exp(-since * 4.5f) : core ? core.endingCharge * 0.1f * (s >= 5f ? 0.35f : 0.5f + 0.5f * Mathf.Sin(t * 8f)) : 0;
            bigFlash.transform.localScale = Vector3.one * (shattered ? 0.75f + since * 0.2f : 0.45f);
            UIKit.GlyphColor(bigFlash, new Color(0.95f, 0.93f, 1f), fl * 0.6f * v);
            // 碎片飞散、慢慢变小
            for (int k = 0; k < shards.Count; k++)
            {
                var (tr, vel, spin) = shards[k]; if (!tr) continue;
                tr.position += vel * dt * Mathf.Exp(-since * 0.6f);
                tr.Rotate(spin * dt, Space.Self);
                tr.localScale *= 1f - dt * 0.28f;
                if (tr.localScale.x < 0.002f) Destroy(tr.gameObject);
            }

            // ── 最后一颗星 ──
            var coreW = transform.TransformPoint(C);
            float starSize = 0f, hov = offer ? offer.hover : 0f;
            if (ph == Ph.Offer)
            {
                offerT += dt;
                float e = Ramp(offerT, 0f, 2.5f);
                // 弧线飘过来：先往上一点再落到手边
                starW = Vector3.Lerp(coreW, offerW, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 0.15f + Vector3.up * Mathf.Sin(t * 1.6f) * 0.006f * e;
                starSize = Mathf.Lerp(0.03f, 0.075f, e) * (1f + 0.15f * hov) * (1f + 0.05f * Mathf.Sin(t * 3f));
                if (offerT > 20f) { heldSide = -1; Burst(); }   // 不卡住玩家：20 秒没捏就自己炸开
            }
            else if (ph == Ph.Held)
            {
                heldT += dt;
                bool still = false;
                if (runner && heldSide >= 0)
                {
                    runner.ReadHands(out var l, out var r);
                    var h = heldSide == 0 ? l : r;
                    if (h.tracked && h.pinch) { still = true; starW = Vector3.Lerp(starW, h.pinchPoint, Mathf.Min(1f, dt * 14f)); }
                }
                starSize = 0.075f * (1f + 0.25f * Mathf.Clamp01(heldT / 1.5f)) * (1f + 0.08f * Mathf.Sin(t * (6f + heldT * 6f)));
                // 松开 = 放飞；一直捏着 2.5 秒也放飞（捏着就会越来越亮、越抖越快）
                if (!still && heldT > 0.15f || heldT > 2.5f) Burst();
            }
            offer.transform.position = starW;
            offer.allowed = ph == Ph.Offer && offerT > 1.5f;
            if (starSize > 0.001f) gems.Extra(starW, starSize * v, 1, ph == Ph.Held ? 1f : hov, 0.37f);
            float glowA = ph == Ph.Offer ? Ramp(offerT, 0.5f, 1.5f) * (0.3f + 0.1f * Mathf.Sin(t * 2.2f)) : ph == Ph.Held ? 0.4f + 0.15f * Mathf.Clamp01(heldT / 2f) : 0f;   // 离眼睛只有半米，光晕要克制
            offerGlow.transform.position = starW; offerGlow.transform.localScale = Vector3.one * (0.16f + 0.1f * glowA);
            UIKit.GlyphColor(offerGlow, Gold, glowA * v);
            // “捏住它”：星停稳 3 秒后还没捏才浮出来（大多数人看到星飘到手边自己就会去捏）
            hint.transform.position = starW + Vector3.down * 0.075f;
            hint.alpha = ph == Ph.Offer ? Ramp(offerT, 5.5f, 1.2f) * 0.85f * v : Mathf.MoveTowards(hint.alpha, 0f, dt * 3f);

            // ── 炸开之后 ──
            float u = burstAt >= 0f ? s - burstAt : -1f;
            if (starInst)
            {
                starInst.SetFloat("_K", u >= 0f ? Mathf.Clamp01(u / 6f) : 0f);
                starInst.SetFloat("_Intensity", 1.25f * v * (u >= 0f ? Mathf.Lerp(0.65f, 1f, Ramp(u, 0.5f, 3.0f)) : 0f));   // 11-25 用户：炸开别太闪 → 3 秒多慢慢亮起来
            }
            skyRoot.localPosition = Vector3.zero;
            skyRoot.localRotation = Quaternion.Euler(0, Mathf.Max(0, u - 8f) * 0.5f, 0);   // 整片天空绕玩家竖轴慢慢转
            if (u >= 0f)
            {
                // 裂缝张满整个空间、边缘消失（11-25 用户：最后没有裂缝了，就是裂缝后面的星空天空盒）
                if (rifts) { rifts.quiet = true; rifts.SetFinal(Ramp(u, 0.5f, 7.5f)); }
                if (sky) sky.veil = Mathf.Lerp(0.42f, 1f, Ramp(u, 4f, 4f));
                if (!passOff && u > 9f) { passOff = true; if (mr) mr.SetPassthrough(false); }
            }
            else if (sky) sky.veil = Mathf.Lerp(0.42f, 0.6f, Ramp(s, 3f, 3f));
            // 星座逐段连线 + 关名
            foreach (var (lr, pts, at, col, label) in constellations)
            {
                float e = u >= 0f ? Ramp(u, at, 1.6f) : 0f;
                int nPts = Mathf.Max(2, Mathf.CeilToInt(pts.Count * e));
                lr.positionCount = nPts;
                for (int i = 0; i < nPts; i++) lr.SetPosition(i, pts[Mathf.Min(i, pts.Count - 1)]);
                UIKit.SetAlpha(lr, col, e * 0.85f * v);
                if (label) label.alpha = (u >= 0f ? Ramp(u, at + 1.2f, 1.2f) : 0f) * 0.55f * v;
            }
            if (crown)
            {
                float e = u >= 0f ? Ramp(u, 16.5f, 4f) : 0f;
                int nPts = Mathf.Max(2, Mathf.CeilToInt(crownPts.Count * e));
                crown.positionCount = nPts;
                for (int i = 0; i < nPts; i++) crown.SetPosition(i, crownPts[Mathf.Min(i, crownPts.Count - 1)]);
                UIKit.SetAlpha(crown, Gold, e * (0.7f + 0.3f * Mathf.Sin(t * 1.3f)) * v);
            }
            // 字和返回
            ApplyLockup(u, v, t);
            float ba = (u >= 0f ? Ramp(u, 19.5f, 1f) : 0f) * v;
            gems.Extra(transform.TransformPoint(backPos + Vector3.up * Mathf.Sin(t * 1.4f) * 0.005f), (0.05f + 0.012f * back.hover) * ba, -1, back.hover, 0.71f);
            backLabel.alpha = ba * (0.7f + 0.3f * back.hover);
            back.allowed = u > 20f;
        }


        public override void SetShown(bool s)
        {
            if (s) { if (rifts) rifts.quiet = false; }
            if (!s)
            {
                if (core) { core.gameObject.SetActive(true); core.endingCharge = 0; }
                if (mr) mr.SetPassthrough(true);   // 返回（含中途返回）：透视开回来、裂缝回到通关进度对应的样子
                if (rifts) rifts.SetFinal(0f);
                AudioKit.Instance?.Silence(false);
                foreach (var sh in shards) if (sh.t) Destroy(sh.t.gameObject);
                shards.Clear();
            }
            base.SetShown(s);
        }
    }
}
