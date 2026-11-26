using System.Collections.Generic;
using Starstrung.Logic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 星图化的路线（替代临时细线）：
    /// - 刻度带：沿路线的带状网格 + StarChartRibbon 着色器（边线、刻度、光晕）；顶点颜色每帧更新：串经过的地方亮、越危险越暖、倒流区冷蓝；
    /// - 暗星点：沿带散落，错频闪烁（StarSpark）；
    /// - 星尘：沿路线慢慢流向星核；
    /// - 入口：两圈刻度环慢慢反向转。
    /// 岔路：第一条画全程，第二条只画分岔之后；没被选中的那条调暗。
    /// </summary>
    public class StarChartTrack : MonoBehaviour
    {
        public GameRunner runner;
        public Material ribbonMat, sparkMat, galaxyMat, nebulaMat;
        [Tooltip("入口魔法阵模板（场景里隐藏的一份，开局复制）")] public MagicCircleStack portalStack;
        [Tooltip("银河星流：每米多少颗粒子")] public float particlesPerMeter = 900f;
        [Tooltip("路线默认亮度（没有星经过时）")] public float baseLight = 0.22f;
        [Tooltip("串经过时额外的亮度")] public float litLight = 0.85f;

        static readonly Color Gold = new(0.91f, 0.83f, 0.64f), Cool = new(0.45f, 0.72f, 1.1f);

        class PathView
        {
            public int ci, pi; public bool fork;
            public ITrack tr; public double fromM;
            public Mesh ribbon; public Color[] cols; public double[] vu; public bool[] rev;
            public Mesh dust; public Vector3[] dustV; public DustP[] dustP;
            public Transform portal; public Vector3 portalScale;
            public Texture2D trackTex; public double[] texU; public Color[] texRow; public Material galaxyInst;
            public Mesh nebula; public Color[] nebCols; public double[] nebU;
            public GameObject root;
            public float dim = 1f;
        }
        struct DustP { public double m; public float off, up, v; }
        readonly List<PathView> views = new();

        void OnEnable() { if (runner) runner.OnLevelStart += Rebuild; }
        void OnDisable() { if (runner) runner.OnLevelStart -= Rebuild; }
        void Start() => Rebuild();

        [Tooltip("显示金色刻度线（11-24 用户：太硬，默认关）")] public bool showRails = false;
        [Tooltip("星河中心偏离路线的幅度（带宽倍数）")] public float meander = 0.85f;

        // ───── 平滑的一维噪声（星河的摆动、呼吸、聚散都用它）─────
        static float Hash(int n) { n = (n << 13) ^ n; return 1f - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824f; }
        static float Noise1(double x, int seed)
        {
            int i = (int)System.Math.Floor(x); float f = (float)(x - i); f = f * f * (3 - 2 * f);
            return Mathf.Lerp(Hash(i * 57 + seed * 131), Hash((i + 1) * 57 + seed * 131), f);   // -1..1
        }
        /// <summary>星河在 m 米处：相对路线的侧向 / 上向偏移（带宽倍数）和宽度倍数</summary>
        (float dx, float dy, float wmul) Meander(double m, int seed)
        {
            float dx = (Noise1(m * 0.35, seed) * 0.7f + Noise1(m * 0.9, seed + 3) * 0.3f) * meander;
            float dy = (Noise1(m * 0.3, seed + 7) * 0.7f + Noise1(m * 0.8, seed + 11) * 0.3f) * meander * 0.6f;
            float wmul = 0.85f + 0.45f * Noise1(m * 0.45, seed + 19) + 0.15f * Noise1(m * 1.3, seed + 23);
            return (dx, dy, Mathf.Max(0.45f, wmul));
        }

        // ───── 路线上某处的局部坐标系（Unity 世界坐标）─────
        (Vector3 p, Vector3 t, Vector3 side, Vector3 up, float w, double u) Frame(ITrack tr, double m)
        {
            double u = tr.MToU(m);
            var p = runner.ToWorld(tr.PointAtU(u));
            var p2 = runner.ToWorld(tr.PointAtU(System.Math.Min(tr.Slots, u + 0.05)));
            var p0 = runner.ToWorld(tr.PointAtU(System.Math.Max(0, u - 0.05)));
            var t = (p2 - p0).normalized; if (t == Vector3.zero) t = Vector3.forward;
            var side = Vector3.Cross(Vector3.up, t); if (side.sqrMagnitude < 1e-6f) side = Vector3.right; side.Normalize();
            var up = Vector3.Cross(t, side).normalized;
            float w = (float)tr.SizeAtU(u) * 0.62f;
            return (p, t, side, up, w, u);
        }

        void Rebuild()
        {
            foreach (var v in views) if (v.root) Destroy(v.root);
            views.Clear();
            var g = runner ? runner.Game : null;
            if (g == null) return;
            for (int ci = 0; ci < g.Tracks.Count; ci++)
            {
                var tr = g.Tracks[ci];
                var paths = tr is ForkTrack ft ? ft.Paths : new ITrack[] { tr };
                var zones = g.Chains[ci].Sim.Cfg.ReverseZones;
                for (int pi = 0; pi < paths.Length; pi++)
                {
                    double fromM = tr is ForkTrack ft2 && pi > 0 ? paths[pi].UToM(ft2.ForkU) : 0;
                    views.Add(Build(ci, pi, tr is ForkTrack, paths[pi], fromM, zones));
                }
            }
        }

        PathView Build(int ci, int pi, bool fork, ITrack tr, double fromM, List<double[]> zones)
        {
            var v = new PathView { ci = ci, pi = pi, fork = fork, tr = tr, fromM = fromM };
            v.root = new GameObject($"StarChart_{ci}_{pi}");
            v.root.transform.SetParent(transform, false);
            double L = tr.Length;
            var rnd = new System.Random(ci * 31 + pi * 7 + 1);

            // 1) 刻度带：每 2 cm 一对顶点；带宽比边线略宽（边线在 ±0.87，外面留光晕）
            int N = Mathf.Max(2, Mathf.CeilToInt((float)((L - fromM) / 0.02)));
            var verts = new Vector3[(N + 1) * 2]; var uvs = new Vector2[verts.Length]; v.cols = new Color[verts.Length];
            v.vu = new double[N + 1]; v.rev = new bool[N + 1];
            var tris = new int[N * 6];
            for (int i = 0; i <= N; i++)
            {
                double m = fromM + (L - fromM) * i / N;
                var f = Frame(tr, m);
                float hw = f.w / 0.87f;
                verts[i * 2] = transform.InverseTransformPoint(f.p - f.side * hw);
                verts[i * 2 + 1] = transform.InverseTransformPoint(f.p + f.side * hw);
                uvs[i * 2] = new Vector2((float)m, -1); uvs[i * 2 + 1] = new Vector2((float)m, 1);
                v.vu[i] = f.u;
                foreach (var z in zones) if (f.u >= z[0] && f.u <= z[1]) v.rev[i] = true;
                if (i < N) { int a = i * 2; tris[i * 6] = a; tris[i * 6 + 1] = a + 2; tris[i * 6 + 2] = a + 1; tris[i * 6 + 3] = a + 1; tris[i * 6 + 4] = a + 2; tris[i * 6 + 5] = a + 3; }
            }
            v.ribbon = new Mesh { name = "Ribbon", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            v.ribbon.vertices = verts; v.ribbon.uv = uvs; v.ribbon.colors = v.cols; v.ribbon.triangles = tris;
            v.ribbon.bounds = new Bounds(Vector3.zero, Vector3.one * 30);
            v.ribbon.MarkDynamic();
            if (showRails) Child(v.root, "Ribbon", v.ribbon, ribbonMat);
            int seedT = ci * 17 + pi * 5 + 3;

            // 2) 暗星点：沿带散落
            var dots = new List<(Vector3 c, float ph, float fr, float mag, float size)>();
            for (double m = fromM; m < L; m += 0.09)
            {
                if (rnd.NextDouble() < 0.45) continue;
                var f = Frame(tr, m);
                var c = f.p + f.side * (float)((rnd.NextDouble() * 2 - 1) * f.w * 2.4) + f.up * (float)((rnd.NextDouble() * 2 - 1) * f.w * 1.2);
                float mag = 0.35f + (float)(rnd.NextDouble() * rnd.NextDouble()) * 0.9f;
                dots.Add((c, (float)rnd.NextDouble() * 6.28f, 0.6f + (float)rnd.NextDouble() * 2.2f, mag, 0.007f + mag * 0.006f));
            }
            Child(v.root, "Dots", SparkMesh(dots, new Color(1f, 0.93f, 0.8f)), sparkMat);

            // 3) 星尘：沿路线流向星核（每帧更新顶点）
            int dn = Mathf.Min(500, Mathf.CeilToInt((float)(L - fromM) * 14));
            v.dustP = new DustP[dn];
            var dl = new List<(Vector3, float, float, float, float)>();
            for (int i = 0; i < dn; i++)
            {
                v.dustP[i] = new DustP { m = fromM + rnd.NextDouble() * (L - fromM), off = (float)(rnd.NextDouble() * 2 - 1), up = (float)(rnd.NextDouble() * 2 - 1), v = 0.05f + (float)rnd.NextDouble() * 0.1f };
                float mag = 0.25f + (float)rnd.NextDouble() * 0.5f;
                dl.Add((Vector3.zero, (float)rnd.NextDouble() * 6.28f, 1.5f + (float)rnd.NextDouble() * 4f, mag, 0.004f + mag * 0.004f));
            }
            v.dust = SparkMesh(dl, new Color(1f, 0.92f, 0.78f));
            v.dust.MarkDynamic();
            v.dustV = v.dust.vertices;
            Child(v.root, "Dust", v.dust, sparkMat);

            // 5) 银河星流：路线烘成贴图，粒子在显卡里流动
            if (galaxyMat)
            {
                int S = Mathf.Clamp(Mathf.CeilToInt((float)(L - fromM) / 0.02f), 64, 2048);
                v.trackTex = new Texture2D(S, 4, TextureFormat.RGBAFloat, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "TrackTex" };
                v.texU = new double[S]; v.texRow = new Color[S];
                for (int i = 0; i < S; i++)
                {
                    double m = fromM + (L - fromM) * i / (S - 1);
                    var f = Frame(tr, m);
                    var mz = Meander(m, seedT);
                    var lp = transform.InverseTransformPoint(f.p + f.side * (mz.dx * f.w) + f.up * (mz.dy * f.w));
                    var ls = transform.InverseTransformDirection(f.side); var lu = transform.InverseTransformDirection(f.up);
                    v.trackTex.SetPixel(i, 0, new Color(lp.x, lp.y, lp.z, f.w * mz.wmul));
                    v.trackTex.SetPixel(i, 1, new Color(ls.x, ls.y, ls.z, 0));
                    v.trackTex.SetPixel(i, 2, new Color(lu.x, lu.y, lu.z, 0));
                    v.trackTex.SetPixel(i, 3, Color.clear);
                    v.texU[i] = f.u;
                }
                v.trackTex.Apply(false);
                int count = Mathf.Min(60000, Mathf.CeilToInt((float)(L - fromM) * particlesPerMeter));
                var gv = new Vector3[count * 4]; var guv = new Vector2[count * 4]; var gp2 = new List<Vector4>(count * 4); var gt = new int[count * 6];
                Vector2[] corner = { new(-1, -1), new(1, -1), new(1, 1), new(-1, 1) };
                float[] strandAng = { 0f, 2.1f, 4.2f };
                for (int i = 0; i < count; i++)
                {
                    // 沿线有聚有散：按噪声做拒绝采样（星团 / 稀疏处）
                    double m0d;
                    int guard = 0;
                    do { m0d = fromM + rnd.NextDouble() * (L - fromM); }
                    while (rnd.NextDouble() > 0.35 + 0.65 * (0.5 + 0.5 * Noise1(m0d * 0.6, seedT + 31)) && guard++ < 8);
                    float m0 = (float)m0d;
                    float ang, rad;
                    double kind = rnd.NextDouble();
                    if (kind < 0.3)
                    {
                        // 三股旋臂星丝：角度固定在三条螺旋上（着色器里随米数扭转），半径集中在 0.5～0.9
                        ang = strandAng[rnd.Next(3)] + (float)(rnd.NextDouble() - 0.5) * 0.35f;
                        rad = 0.5f + (float)rnd.NextDouble() * 0.4f;
                    }
                    else
                    {
                        ang = (float)(rnd.NextDouble() * Mathf.PI * 2);
                        float g = (float)((rnd.NextDouble() + rnd.NextDouble() + rnd.NextDouble()) / 3.0 * 2 - 1);
                        rad = Mathf.Abs(g) * 1.05f;
                        if (kind > 0.93) rad += 0.6f + (float)rnd.NextDouble() * 1.4f;   // 往外飘散的星屑
                    }
                    float speed = 0.04f + (float)rnd.NextDouble() * 0.12f;
                    double rr = rnd.NextDouble();
                    float size = 0.0026f + (float)(rr * rr * rr * rr * rr * rr) * 0.022f;   // 绝大多数是细星尘，极少数大散景
                    var a = new Vector3(m0, ang, rad);
                    var p2 = new Vector4(speed, size, (float)rnd.NextDouble() * 100f, (float)rnd.NextDouble() * 6.28f);
                    for (int k = 0; k < 4; k++) { gv[i * 4 + k] = a; guv[i * 4 + k] = corner[k]; gp2.Add(p2); }
                    gt[i * 6] = i * 4; gt[i * 6 + 1] = i * 4 + 1; gt[i * 6 + 2] = i * 4 + 2; gt[i * 6 + 3] = i * 4; gt[i * 6 + 4] = i * 4 + 2; gt[i * 6 + 5] = i * 4 + 3;
                }
                var gm = new Mesh { name = "Galaxy", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                gm.vertices = gv; gm.uv = guv; gm.SetUVs(1, gp2); gm.triangles = gt;
                gm.bounds = new Bounds(Vector3.zero, Vector3.one * 30);
                v.galaxyInst = new Material(galaxyMat) { name = "GalaxyStream(实例)" };
                v.galaxyInst.SetTexture("_TrackTex", v.trackTex);
                v.galaxyInst.SetFloat("_FromM", (float)fromM); v.galaxyInst.SetFloat("_Length", (float)L);
                Child(v.root, "Galaxy", gm, v.galaxyInst);
            }

            // 6) 星云气：一横一竖两条交叉带
            if (nebulaMat)
            {
                int NN = Mathf.Max(2, Mathf.CeilToInt((float)((L - fromM) / 0.03)));
                var nv = new List<Vector3>(); var nuv = new List<Vector2>(); var nn = new List<Vector3>(); var nt = new List<int>();
                v.nebU = new double[NN + 1];
                for (int band = 0; band < 2; band++)
                {
                    int b0 = nv.Count;
                    for (int i = 0; i <= NN; i++)
                    {
                        double m = fromM + (L - fromM) * i / NN;
                        var f = Frame(tr, m);
                        var dir = band == 0 ? f.side : f.up;
                        var nrm = band == 0 ? f.up : f.side;
                        var mz = Meander(m, seedT);
                        var c0 = f.p + f.side * (mz.dx * f.w) + f.up * (mz.dy * f.w);
                        float hw = f.w * mz.wmul * (band == 0 ? 3.4f : 2.3f);
                        nv.Add(transform.InverseTransformPoint(c0 - dir * hw)); nv.Add(transform.InverseTransformPoint(c0 + dir * hw));
                        nuv.Add(new Vector2((float)m, -1)); nuv.Add(new Vector2((float)m, 1));
                        var ln = transform.InverseTransformDirection(nrm); nn.Add(ln); nn.Add(ln);
                        if (band == 0) v.nebU[i] = f.u;
                        if (i < NN) { int a = b0 + i * 2; nt.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
                    }
                }
                v.nebula = new Mesh { name = "Nebula", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                v.nebula.SetVertices(nv); v.nebula.SetUVs(0, nuv); v.nebula.SetNormals(nn); v.nebula.SetTriangles(nt, 0);
                v.nebCols = new Color[nv.Count]; v.nebula.colors = v.nebCols;
                v.nebula.bounds = new Bounds(Vector3.zero, Vector3.one * 30);
                v.nebula.MarkDynamic();
                var nebInst = new Material(nebulaMat) { name = "NebulaRibbon(实例)" };
                nebInst.SetFloat("_FromM", (float)fromM); nebInst.SetFloat("_Length", (float)L);
                Child(v.root, "Nebula", v.nebula, nebInst);
            }

            // 4) 入口：多层立体魔法阵，法线沿路线方向（星河从阵心穿过几层涌出来）
            if (fromM == 0 && portalStack)
            {
                var f0 = Frame(tr, 0.05);
                var st = Instantiate(portalStack, v.root.transform);
                st.seed = 11f + ci * 5f + pi * 2f;   // 每个入口的手绘笔迹不同（模板是隐藏的，复制后激活时才 Awake）
                st.gameObject.SetActive(true);
                var head = runner.rig ? runner.rig.TransformPoint(new Vector3(0, 1.6f, 0)) : new Vector3(0, 1.6f, 0);
                var toHead = (head - f0.p).normalized;
                var nrm = Vector3.Slerp(f0.t, toHead, 0.5f).normalized;   // 一半沿路线、一半朝玩家：星河仍从阵心涌出，玩家也看得到阵面
                st.transform.SetPositionAndRotation(f0.p - f0.t * 0.06f, Quaternion.FromToRotation(Vector3.up, nrm));
                st.transform.localScale = Vector3.one * (f0.w * 3.6f / Mathf.Max(0.01f, portalStack.radius));
                v.portal = st.transform; v.portalScale = st.transform.localScale;
            }
            return v;
        }

        GameObject Child(GameObject parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            // 网格顶点都在本组件的局部坐标里：子物体就放在原点（入口环例外，它自己有位置）
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return go;
        }

        Mesh SparkMesh(List<(Vector3 c, float ph, float fr, float mag, float size)> list, Color col)
        {
            int n = list.Count;
            var v = new Vector3[n * 4]; var uv = new Vector2[n * 4]; var p2 = new List<Vector4>(n * 4); var cs = new Color[n * 4]; var t = new int[n * 6];
            Vector2[] corner = { new(-1, -1), new(1, -1), new(1, 1), new(-1, 1) };
            for (int i = 0; i < n; i++)
            {
                var (c, ph, fr, mag, size) = list[i];
                var lc = transform.InverseTransformPoint(c);
                for (int k = 0; k < 4; k++) { v[i * 4 + k] = lc; uv[i * 4 + k] = corner[k]; p2.Add(new Vector4(ph, fr, mag, size)); cs[i * 4 + k] = col; }
                t[i * 6] = i * 4; t[i * 6 + 1] = i * 4 + 1; t[i * 6 + 2] = i * 4 + 2; t[i * 6 + 3] = i * 4; t[i * 6 + 4] = i * 4 + 2; t[i * 6 + 5] = i * 4 + 3;
            }
            var m = new Mesh { name = "Sparks", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = v; m.uv = uv; m.SetUVs(1, p2); m.colors = cs; m.triangles = t;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 30);
            return m;
        }

        /// <summary>圆环带（半径 r、半宽 hw），uv.x 用弧长（米，按单位圆算），uv.y 横向 -1..1</summary>
        static Mesh RingMesh(float r, float hw, int seg)
        {
            var v = new Vector3[(seg + 1) * 2]; var uv = new Vector2[v.Length]; var c = new Color[v.Length]; var t = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                v[i * 2] = d * (r - hw); v[i * 2 + 1] = d * (r + hw);
                uv[i * 2] = new Vector2(a * r * 0.6f, -1); uv[i * 2 + 1] = new Vector2(a * r * 0.6f, 1);
                c[i * 2] = c[i * 2 + 1] = Gold * 0.5f;
                if (i < seg) { int b = i * 2; t[i * 6] = b; t[i * 6 + 1] = b + 2; t[i * 6 + 2] = b + 1; t[i * 6 + 3] = b + 1; t[i * 6 + 4] = b + 2; t[i * 6 + 5] = b + 3; }
            }
            var m = new Mesh { name = "Ring" }; m.vertices = v; m.uv = uv; m.colors = c; m.triangles = t; m.RecalculateBounds();
            return m;
        }

        void LateUpdate()
        {
            // 入口魔法阵：进关时先从一个点张开（路线展开的第一米半），退出时合上
            float open = Mathf.SmoothStep(0, 1, Mathf.Clamp01(AppFlow.Grow / 1.5f));
            foreach (var pv in views) if (pv.portal) pv.portal.localScale = pv.portalScale * Mathf.Max(0.001f, open);
            var g = runner ? runner.Game : null;
            if (g == null) return;
            float dt = Time.deltaTime, time = Time.time;
            foreach (var v in views)
            {
                var c = g.Chains[v.ci];
                bool active = !v.fork || ((ForkTrack)g.Tracks[v.ci]).Active == v.pi;
                if (v.fork && g.Fork != null) v.dim = g.Fork.Committed ? (active ? 1f : 0.25f) : (g.Fork.Next == v.pi ? 1f : 0.45f);
                // 串上星的位置（升序）
                var us = new List<double>();
                if (active) foreach (var s in c.Sim.Stars) us.Add(s.S);
                us.Sort();
                var head = c.Sim.Count > 0 ? c.Sim.Stars[0] : null;
                float heat = head != null ? Mathf.Clamp01(((float)(head.S / c.Track.Slots) - 0.6f) / 0.4f) : 0f;
                int j = 0;
                for (int i = 0; i < v.vu.Length; i++)
                {
                    double u = v.vu[i];
                    while (j + 1 < us.Count && us[j + 1] < u) j++;
                    double d = 1e9;
                    if (us.Count > 0) { d = System.Math.Abs(us[j] - u); if (j + 1 < us.Count) d = System.Math.Min(d, System.Math.Abs(us[j + 1] - u)); }
                    float lit = Mathf.Max(0f, 1f - (float)d / 2.5f);
                    float k = (baseLight + lit * litLight) * v.dim;
                    Color col = v.rev[i] ? Cool * k : new Color(Gold.r * k * (1 + heat * 0.2f), Gold.g * k * (1 - heat * 0.15f), Gold.b * k * (1 - heat * 0.35f));
                    v.cols[i * 2] = v.cols[i * 2 + 1] = col;
                }
                v.ribbon.colors = v.cols;
                // 银河 / 星云气：同样按“离最近一颗星多远”算亮度
                float Lit(double u)
                {
                    int lo = 0, hi = us.Count - 1;
                    if (hi < 0) return 0f;
                    while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (us[mid] < u) lo = mid; else hi = mid; }
                    double d = System.Math.Min(System.Math.Abs(us[lo] - u), System.Math.Abs(us[hi] - u));
                    return Mathf.Max(0f, 1f - (float)d / 3f);
                }
                if (v.trackTex)
                {
                    for (int i = 0; i < v.texU.Length; i++) v.texRow[i] = new Color(Lit(v.texU[i]) * v.dim, heat, 0, 0);
                    v.trackTex.SetPixels(0, 3, v.texU.Length, 1, v.texRow);
                    v.trackTex.Apply(false);
                    v.galaxyInst.SetFloat("_Intensity", 1.6f * (0.35f + 0.65f * v.dim));
                }
                if (v.nebula)
                {
                    int half = v.nebU.Length;
                    for (int i = 0; i < half; i++)
                    {
                        var cc = new Color(Lit(v.nebU[i]) * v.dim + (v.dim - 1f) * 0.4f, heat, 0, 1);
                        v.nebCols[i * 2] = v.nebCols[i * 2 + 1] = cc;
                        v.nebCols[half * 2 + i * 2] = v.nebCols[half * 2 + i * 2 + 1] = cc;
                    }
                    v.nebula.colors = v.nebCols;
                }
                // 星尘流动
                double L = v.tr.Length;
                for (int i = 0; i < v.dustP.Length; i++)
                {
                    var p = v.dustP[i];
                    p.m += p.v * dt; if (p.m > L) p.m = v.fromM + (p.m - L);
                    v.dustP[i] = p;
                    var f = Frame(v.tr, p.m);
                    float wob = Mathf.Sin(time * 0.7f + i) * 0.15f;
                    var w = f.p + f.side * (f.w * (p.off * 1.6f + wob)) + f.up * (f.w * p.up * 0.9f);
                    var lp = transform.InverseTransformPoint(w);
                    for (int k = 0; k < 4; k++) v.dustV[i * 4 + k] = lp;
                }
                v.dust.vertices = v.dustV;
            }
        }
    }
}
