using System.Collections.Generic;
using Starstrung.Logic;
using TMPro;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 选关 = 一团斜躺在身前的粒子银河（约 1.7 m 宽，盘面朝玩家斜 35°）：
    /// - 四条旋臂 = 四个星域，每条臂上由内往外五颗关卡星；序章在银河中心；星流沿臂一直往外流；
    /// - 进来时银河从星核里旋转着炸开、铺成型，关卡星从里往外一颗颗“点亮”（闪一下、弹一下），通关的路径连线随后画出来；
    /// - 每个星域的标签挂在旋臂末端（参考 11-24 用户图二）：极小大写眉题 + 星域名 + 一个圆圈小图标（该星域新出现的特殊星）；
    /// - 关卡星：通关的外面一圈金色手绘光环，下面三颗四芒星（拿到的大而亮的金色，没拿到的小而暗）；没解锁的缩小变暗；
    /// - 指着一颗：放大、光环转起来、关名浮现；捏下：那颗星沿弧线飞进星核，银河收回星核，然后开始这一关；
    /// - “返回”在星核旁边（回封面）。
    /// </summary>
    public class SelectView : FlowView
    {
        public GemField gems;
        public Material galaxyMat;
        public Transform core;
        public Vector3 center = new(0, 1.62f, 1.38f);
        public float radius = 1.0f, wind = 1.8f;

        static readonly float[] ArmEndDeg = { 215f, 125f, 52f, -38f };
        static readonly int[] ZoneColor = { 1, 3, 0, 4 };
        static readonly string[] ZoneName = { "晨曦", "霜川", "焰云", "深空" };
        static readonly string[] ZoneEyebrow = { "I · DAYBREAK", "II · FROSTWATER", "III · EMBERVEIL", "IV · THE DEEP" };

        Transform disk; Material gal; float galDim = 0.45f; bool holesSet;
        class Node
        {
            public Level L; public int index, zone; public Vector3 local; public int color;
            public PickTarget pick; public UIText name, num; public Renderer[] rating; public LineRenderer halo; public Renderer pop;
        }
        readonly List<Node> nodes = new();
        readonly List<(LineRenderer lr, Color c, int need)> paths = new();
        readonly List<(UIText eyebrow, UIText title, LineRenderer icon, Transform holder)> zoneLabels = new();
        UIText rifts, backLabel; PickTarget back; Vector3 backPos;
        Node launching; float launchT = -1;
        static readonly Color Gold = new(1f, 0.85f, 0.52f);
        static readonly Color Ink = new(0.82f, 0.88f, 1f, 1f);

        float ArmAngle(int z) => ArmEndDeg[z] * Mathf.Deg2Rad - Mathf.Log(0.95f / 0.03f) * wind;
        Vector3 ArmPoint(int z, float r, float lift = 0.012f)
        {
            float th = ArmAngle(z) + Mathf.Log(Mathf.Max(r, 0.03f) / 0.03f) * wind;
            return new Vector3(Mathf.Cos(th) * r * radius, lift, Mathf.Sin(th) * r * radius);
        }
        Vector3 W(Vector3 local) => disk.TransformPoint(local);
        Vector3 L(Vector3 local) => transform.InverseTransformPoint(disk.TransformPoint(local));

        void Awake()
        {
            disk = new GameObject("Galaxy").transform; disk.SetParent(transform, false);
            disk.localPosition = center; disk.localRotation = Quaternion.Euler(-55f, 0, 0);
            if (galaxyMat)
            {
                disk.gameObject.AddComponent<MeshFilter>().sharedMesh = BuildGalaxy();
                var mr = disk.gameObject.AddComponent<MeshRenderer>(); gal = new Material(galaxyMat); mr.sharedMaterial = gal;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                gal.SetFloat("_Radius", radius); gal.SetFloat("_Wind", wind); gal.SetFloat("_Intensity", 0.45f);
                holesSet = false;
            }

            var levels = Levels.LEVELS;
            nodes.Add(new Node { L = levels[0], index = 0, zone = -1, local = new Vector3(0, 0.03f, 0), color = -1 });
            int idx = 1;
            // 无尽关（11-25 用户：别和后面的关重叠，放在最后一关后面）：深空旋臂上、吞星兽（4-5）再往外一格，打完吞星兽才亮
            const float EndlessR = 1.07f;
            for (int k = 0; k < levels.Length; k++)
                if (levels[k].Endless)
                {
                    nodes.Add(new Node { L = levels[k], index = k, zone = -2, local = ArmPoint(3, EndlessR), color = 2 });
                    var seg = new List<Vector3>();
                    for (int q = 0; q <= 10; q++) seg.Add(L(ArmPoint(3, Mathf.Lerp(0.9f + 0.035f, EndlessR - 0.035f, q / 10f), 0.014f)));
                    paths.Add((UIKit.Line(transform, "PathEndless", UIKit.HandPolyline(seg, Vector3.back, 77, false, 3), 0.0032f, Gold), Gold, Progress.Index("4-5")));
                    break;
                }
            for (int z = 0; z < 4; z++)
            {
                var pts = new List<Vector3>();
                for (int j = 0; j < 5 && idx < levels.Length && !levels[idx].Endless; j++, idx++)
                {
                    float r = 0.3f + 0.15f * j;
                    nodes.Add(new Node { L = levels[idx], index = idx, zone = z, local = ArmPoint(z, r), color = ZoneColor[z] });
                }
                // 通关路径：沿旋臂从这一关画到下一关（第一段从中心的序章出发）
                for (int j = 0; j < 5; j++)
                {
                    float r0 = j == 0 ? 0.06f : 0.3f + 0.15f * (j - 1), r1 = 0.3f + 0.15f * j;
                    var seg = new List<Vector3>();
                    for (int k = 0; k <= 10; k++) seg.Add(L(ArmPoint(z, Mathf.Lerp(r0 + 0.035f, r1 - 0.035f, k / 10f), 0.014f)));
                    var col = Color.Lerp(Palette.Of(ZoneColor[z]), Gold, 0.5f);
                    paths.Add((UIKit.Line(transform, $"Path{z}_{j}", UIKit.HandPolyline(seg, Vector3.back, z * 13 + j, false, 3), 0.0032f, col), col, 1 + z * 5 + j - 1));
                }
                // 星域标签：旋臂末端外面一点
                var lp = L(ArmPoint(z, z == 3 ? 1.3f : 1.12f, 0.02f));   // 深空的标签让出位置给无尽关
                var holder = new GameObject("Zone" + z).transform; holder.SetParent(transform, false); holder.localPosition = lp;
                holder.gameObject.AddComponent<FaceHead>().yawOnly = false;
                var eb = UIText.Make(holder, ZoneEyebrow[z], UIKit.Face.Title, 0.016f, new Vector3(0, 0.07f, 0), color: new Color(0.7f, 0.78f, 1f), raw: true);
                eb.spacing = 30f; eb.Refresh();
                var tt = UIText.Make(holder, ZoneName[z], UIKit.Face.Serif, 0.062f, new Vector3(0, 0.02f, 0), color: new Color(0.92f, 0.94f, 1f));
                tt.spacing = 14f; tt.Refresh();
                var icon = UIKit.Line(holder, "Icon", UIKit.HandCircle(new Vector3(0, -0.04f, 0), Vector3.back, 0.018f, 60 + z), 0.0018f, Ink);
                zoneLabels.Add((eb, tt, icon, holder));
            }
            foreach (var n in nodes)
            {
                var nn = n;
                var lp = L(n.local);
                n.pick = Target(transform, "Lv " + n.L.Id, lp, n.index == 0 ? 0.08f : 0.06f, () => Launch(nn));
                n.num = UIText.Make(transform, "", UIKit.Face.Title, 0.018f, lp + new Vector3(0.05f, 0.03f, 0), TextAlignmentOptions.Left, new Color(0.75f, 0.8f, 1f), raw: true);
                n.num.gameObject.AddComponent<FaceHead>();
                n.name = n.index == 0
                    ? UIText.Make(transform, "", UIKit.Face.Serif, 0.038f, lp + new Vector3(-0.115f, -0.01f, -0.04f), TextAlignmentOptions.Right, new Color(0.95f, 0.96f, 1f))
                    : UIText.Make(transform, "", UIKit.Face.Serif, 0.03f, lp + new Vector3(0, 0.082f, -0.03f), color: new Color(0.95f, 0.96f, 1f));
                n.name.gameObject.AddComponent<FaceHead>();
                n.rating = new Renderer[3];
                for (int k = 0; k < 3; k++) n.rating[k] = UIKit.Glyph(transform, "Rate", lp + new Vector3((k - 1) * 0.045f, -0.078f - (k == 1 ? 0.01f : 0), -0.04f), 0.06f);
                var haloGo = new GameObject("Halo"); haloGo.transform.SetParent(transform, false); haloGo.transform.localPosition = lp;
                haloGo.AddComponent<FaceHead>().yawOnly = false;
                n.halo = UIKit.Line(haloGo.transform, "HaloLine", UIKit.HandCircle(Vector3.zero, Vector3.back, n.index == 0 ? 0.07f : 0.052f, 80 + n.index), 0.0024f, Gold);
                n.pop = UIKit.Glyph(transform, "Pop", lp + new Vector3(0, 0, -0.01f), 0.16f);
            }

            var c = core ? core.localPosition : new Vector3(0, 1.22f, 0.35f);
            backPos = c + new Vector3(-0.25f, 0.03f, -0.06f);
            back = Target(transform, "Back", backPos, 0.05f, () => flow.BackToTitle());
            backLabel = UIText.Make(transform, "返回", UIKit.Face.Serif, 0.02f, backPos + new Vector3(-0.04f, 0.03f, 0f), TextAlignmentOptions.Right, new Color(0.88f, 0.9f, 1f));
            backLabel.spacing = 24f; backLabel.Refresh(); backLabel.gameObject.AddComponent<FaceHead>();
            rifts = UIText.Make(transform, "已撕开 {0}/20 道时空裂缝", UIKit.Face.Italic, 0.019f, c + new Vector3(0.26f, 0.0f, -0.05f), TextAlignmentOptions.Left, new Color(0.72f, 0.78f, 0.98f));
            rifts.gameObject.AddComponent<FaceHead>();
        }

        Mesh BuildGalaxy()
        {
            const int arms = 4, perArm = 2200, bulge = 1300, dust = 900, haze = 260;
            int n = arms * perArm + bulge + dust + arms * haze;
            var v = new Vector3[n * 4]; var uv = new Vector2[n * 4]; var a = new Vector4[n * 4]; var b = new Vector4[n * 4]; var tri = new int[n * 6];
            var rnd = new System.Random(11);
            float R() => (float)rnd.NextDouble();
            float G() { float u = Mathf.Max(1e-4f, R()), w = R(); return Mathf.Sqrt(-2f * Mathf.Log(u)) * Mathf.Cos(2f * Mathf.PI * w); }
            int i = 0;
            void Add(Vector4 da, Vector4 db)
            {
                for (int k = 0; k < 4; k++) { v[i * 4 + k] = Vector3.zero; a[i * 4 + k] = da; b[i * 4 + k] = db; uv[i * 4 + k] = new Vector2(k == 1 || k == 2 ? 1 : 0, k >= 2 ? 1 : 0); }
                int q = i * 4, t = i * 6; tri[t] = q; tri[t + 1] = q + 2; tri[t + 2] = q + 1; tri[t + 3] = q; tri[t + 4] = q + 3; tri[t + 5] = q + 2;
                i++;
            }
            for (int z = 0; z < arms; z++)
                for (int k = 0; k < perArm; k++)
                {
                    float big = R() > 0.97f ? 2.2f : 1f;
                    Add(new Vector4(R(), ArmAngle(z), G() * 0.8f, R()), new Vector4((0.003f + 0.0045f * R()) * big, R(), 0.012f + 0.01f * R(), 0));
                }
            for (int k = 0; k < bulge; k++)
                Add(new Vector4(Mathf.Pow(R(), 1.8f) * 0.24f, R() * 6.283f, 0, R()), new Vector4(0.003f + 0.004f * R(), R() * 0.5f, 0, 1));
            for (int k = 0; k < dust; k++)
                Add(new Vector4(Mathf.Sqrt(R()) * 1.05f, R() * 6.283f, 0, R()), new Vector4(0.002f + 0.003f * R(), R(), 0, 2));
            // 柔光星云：沿旋臂的大而淡的光斑，让银河有一层雾蒙蒙的辉光（不只是一粒粒的点）
            for (int z = 0; z < arms; z++)
                for (int k = 0; k < haze; k++)
                    Add(new Vector4(0.05f + R() * 0.9f, ArmAngle(z), G() * 0.6f, R()), new Vector4(0.04f + 0.07f * R(), R(), 0.004f + 0.004f * R(), 3));
            var m = new Mesh { name = "GalaxyDisk", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = v; m.uv = uv; m.SetUVs(1, a); m.SetUVs(2, b); m.triangles = tri;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 3f);
            return m;
        }

        void Launch(Node n)
        {
            if (launching != null) return;
            if (!Progress.Unlocked(n.index)) return;
            launching = n; launchT = 0;
            AudioKit.Instance?.PlayClip(AudioKit.Instance.flyIn, 0.75f);
        }

        public override void SetShown(bool s)
        {
            base.SetShown(s);
            if (s) { Refresh(); launching = null; launchT = -1; }
        }

        void Refresh()
        {
            foreach (var n in nodes) n.name.Set(n.index == 0 ? "序章" : Loc.Name(n.L));
            int opened = 0; foreach (var id in RiftView.Cleared()) if (id != "T") opened++;
            rifts.Set("已撕开 {0}/20 道时空裂缝", opened);
        }

        void OnEnable() { Loc.Changed += Refresh; }
        void OnDisable() { Loc.Changed -= Refresh; }

        static float Ramp(float t, float a, float d) => Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - a) / d));

        protected override void Apply(float v, float dt)
        {
            if (vis <= 0f && !shown) { if (gameObject.activeSelf) gameObject.SetActive(false); return; }
            float s = shownT, t = Time.time;
            var cam = Camera.main;
            var coreLocalInDisk = disk.InverseTransformPoint(core ? core.position : transform.TransformPoint(new Vector3(0, 1.22f, 0.35f)));

            // 银河：从星核炸开；离开时收回星核
            float burst = shown ? Ramp(s, 0f, 1.7f) : vis;
            if (launchT >= 0)
            {
                launchT += dt;
                burst = 1f - Ramp(launchT, 0.35f, 0.8f);
                if (launchT > 1.25f) { var id = launching.L.Id; launching = null; launchT = -1; flow.Play(id); }
            }
            // 银河是背景：平时压暗，指着某一关时再暗一档，让关卡星和字是主角（11-24 用户：别喧宾夺主）
            float anyHover = 0; foreach (var nd in nodes) anyHover = Mathf.Max(anyHover, nd.pick.hover);
            galDim = Mathf.MoveTowards(galDim, 0.45f - 0.17f * anyHover, dt * 1.5f);
            if (gal) gal.SetFloat("_Intensity", galDim * (launchT >= 0 ? 1.6f : 1f));
            if (gal && !holesSet && nodes.Count > 0)
            {
                var holes = new Vector4[24];
                for (int k = 0; k < nodes.Count && k < 24; k++) holes[k] = nodes[k].local;
                gal.SetVectorArray("_Holes", holes); gal.SetFloat("_HoleN", Mathf.Min(24, nodes.Count)); holesSet = true;
            }
            if (gal) { gal.SetFloat("_Burst", burst); gal.SetVector("_From", coreLocalInDisk); gal.SetFloat("_Alpha", Mathf.Max(v, launchT >= 0 ? 1 : 0)); }

            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                // 点亮顺序：序章 → 由内往外（同一圈的四个星域一起）
                float order = n.index == 0 ? 0 : 1 + (n.index - 1) % 5;
                float at = 0.9f + order * 0.13f;
                float a = Ramp(s, at, 0.35f) * v;
                bool open = Progress.Unlocked(n.index), done = Progress.Cleared(n.L.Id);
                n.pick.allowed = open && launching == null;
                float hv = n.pick.hover;
                float popK = Mathf.Exp(-Mathf.Max(0, s - at) * 7f) * (s > at ? 1 : 0);
                var wp = W(n.local) + Vector3.up * Mathf.Sin(t * 0.9f + i * 1.3f) * 0.003f;
                float size = (open ? (n.index == 0 ? 0.12f : 0.095f) : 0.04f) * (1f + 0.25f * hv + 0.5f * popK) * a;
                if (launching == n)
                {
                    // 被选中的星：沿弧线飞进星核
                    float e = Mathf.SmoothStep(0, 1, Mathf.Clamp01(launchT / 0.9f));
                    var to = core ? core.position : wp;
                    wp = Vector3.Lerp(wp, to, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 0.12f;
                    size = 0.06f * (1f - e * 0.6f);
                }
                else if (launching != null) size *= 1f - Ramp(launchT, 0.2f, 0.5f);
                if (open || launching == n) gems.Extra(wp, size, n.color, Mathf.Max(hv, popK), i * 0.0731f);
                // 点亮时闪一下
                if (!open) { n.pop.transform.localScale = Vector3.one * 0.05f; UIKit.GlyphColor(n.pop, new Color(0.6f, 0.65f, 0.95f), 0.35f * a * (launching != null ? 1f - Ramp(launchT, 0f, 0.3f) : 1f)); }
                else { n.pop.transform.localScale = Vector3.one * 0.16f; UIKit.GlyphColor(n.pop, Color.Lerp(Color.white, Palette.Of(Mathf.Max(0, n.color)), 0.3f), popK * 1.6f * v + (launching == n ? 1.2f * (1 - Ramp(launchT, 0.6f, 0.4f)) : 0f)); }
                if (launching == n) n.pop.transform.position = wp;

                float la = launching != null ? 1f - Ramp(launchT, 0f, 0.3f) : 1f;
                n.num.alpha = 0f;
                n.name.alpha = a * la * (open ? 0.8f + 0.2f * hv : 0f);   // 解锁的关一直显示关名
                int st = done ? Mathf.Clamp(Progress.BestStars(n.L.Id), 1, 3) : 0;
                // 星级：关卡星下面一排三颗小金宝石（拿到的实心发亮，没拿到的暗淡小点），远看也一眼分得清；拿到的上面再闪一颗四芒星
                for (int k = 0; k < 3; k++)
                {
                    bool got = k < st;
                    float ra = Ramp(s, at + 0.25f + k * 0.08f, 0.3f) * v * la;
                    var rp = n.rating[k].transform.position;
                    if (done) gems.Extra(rp, (got ? 0.034f : 0.015f) * ra, got ? 1 : 3, got ? 0.5f : 0f, 0.9f + k * 0.1f);
                    n.rating[k].transform.localScale = Vector3.one * 0.06f;
                    float tw = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 1.7f + k * 2.1f + i * 0.7f), 4f);
                    UIKit.GlyphColor(n.rating[k], new Color(1f, 0.85f, 0.55f), got ? (0.5f + 1.2f * tw) * ra : 0f);
                }
                float ha = open ? hv * 0.85f * v * la : 0f;   // 光环只在指着时出现（不常驻，减少杂乱）
                UIKit.SetAlpha(n.halo, done ? Gold : Ink, ha);
                n.halo.transform.parent.localRotation *= Quaternion.identity;
                n.halo.transform.localRotation = Quaternion.Euler(0, 0, t * (hv > 0.1f ? 60f : 12f) + i * 40f);
            }
            for (int k = 0; k < paths.Count; k++)
            {
                var (lr, c, need) = paths[k];
                bool lit = need < Levels.LEVELS.Length && Progress.Cleared(Levels.LEVELS[need].Id);
                float pa = Ramp(s, 1.6f + (k % 5) * 0.12f, 0.5f) * v * (launching != null ? 1f - Ramp(launchT, 0f, 0.3f) : 1f);
                UIKit.SetAlpha(lr, c, pa * (lit ? 0.9f : 0.12f));
            }
            for (int z = 0; z < zoneLabels.Count; z++)
            {
                var (eb, tt, icon, holder) = zoneLabels[z];
                float za = Ramp(s, 1.5f + z * 0.12f, 0.6f) * v * (launching != null ? 1f - Ramp(launchT, 0f, 0.3f) : 1f);
                eb.alpha = za * 0.8f; tt.alpha = za; UIKit.SetAlpha(icon, Ink, za * 0.7f);
            }
            float ba = Ramp(s, 1.2f, 0.6f) * v;
            gems.Extra(transform.TransformPoint(backPos + Vector3.up * Mathf.Sin(t * 1.5f) * 0.004f), (0.03f + 0.008f * back.hover) * ba, 3, back.hover, 0.44f);
            backLabel.alpha = ba * (0.65f + 0.35f * back.hover);
            back.allowed = s > 1.3f && launching == null;
            rifts.alpha = Ramp(s, 1.8f, 0.8f) * v * 0.8f;
        }
    }
}
