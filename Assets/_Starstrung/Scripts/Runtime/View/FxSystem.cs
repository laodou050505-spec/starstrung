using System.Collections.Generic;
using Starstrung.Logic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 消除和各种事件的特效（全部对象池，游戏中不新建 / 销毁物体）：
    /// - 消除：每颗星碎成几十颗同色柔光微粒 + 白色四芒小星；中心一颗大四芒星闪一下；一圈带刻度的冲击光环沿路线横截面荡开；
    ///         被消的几颗星连成一个小星座（逐段连线、停留、淡出）；连锁越多越盛大（微粒更多更远、光环更大、第二道光环）；
    /// - 摘起 / 放下：一小撮星尘；撞星核：星核处大闪光 + 光环；孤色消散：灰白微粒慢慢飘散；过关：星核处盛大的星尘绽放。
    /// </summary>
    public class FxSystem : MonoBehaviour
    {
        public GameRunner runner;
        public GemField gems;
        public Material particleMat, ringMat, flareMat;

        ParticleSystem motes, sparkles;
        struct Pooled { public Transform tr; public MeshRenderer mr; public float t, dur, size; public Color col; public bool on; }
        readonly List<Pooled> rings = new(), flashes = new();
        class Cons { public LineRenderer lr; public Vector3[] pts; public float t; public bool on; public Color col; }
        readonly List<Cons> cons = new();
        MaterialPropertyBlock mpb;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            var dot = MakeTex(false); var star = MakeTex(true);
            motes = MakePS("Motes", dot, 2000, 0.6f, 1.6f);
            sparkles = MakePS("Sparkles", star, 600, 0.5f, 1.2f);
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            for (int i = 0; i < 16; i++) rings.Add(MakeQuad($"Ring{i}", quad, ringMat));
            for (int i = 0; i < 8; i++) flashes.Add(MakeQuad($"Flash{i}", quad, flareMat));
            for (int i = 0; i < 8; i++)
            {
                var go = new GameObject($"Cons{i}"); go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                var lm = new Material(particleMat) { mainTexture = LineTex() };
                lr.sharedMaterial = lm; lr.useWorldSpace = true; lr.widthMultiplier = 0.007f; lr.textureMode = LineTextureMode.Stretch;
                lr.numCapVertices = 2; lr.enabled = false;
                cons.Add(new Cons { lr = lr });
            }
        }

        void OnEnable() { if (runner) runner.OnEvent += OnEvent; }
        void OnDisable() { if (runner) runner.OnEvent -= OnEvent; }

        // ───── 贴图 ─────
        static Texture2D MakeTex(bool star4)
        {
            const int S = 64; var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2 - 1, v = (y + 0.5f) / S * 2 - 1, r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Exp(-r * 4.5f) * 1.2f + Mathf.Exp(-r * 12f);
                    if (star4) a = Mathf.Exp(-r * 10f) * 1.5f + Mathf.Exp(-Mathf.Abs(u) * 30f) * Mathf.Pow(Mathf.Clamp01(1 - Mathf.Abs(v)), 2) + Mathf.Exp(-Mathf.Abs(v) * 30f) * Mathf.Pow(Mathf.Clamp01(1 - Mathf.Abs(u)), 2);
                    a *= Mathf.Clamp01((1 - r) * 3f);
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(a)));
                }
            t.Apply(); return t;
        }
        static Texture2D LineTex()
        {
            var t = new Texture2D(4, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++) { float v = (y + 0.5f) / 32f * 2 - 1; float a = Mathf.Exp(-v * v * 8f); for (int x = 0; x < 4; x++) t.SetPixel(x, y, new Color(1, 1, 1, a)); }
            t.Apply(); return t;
        }

        ParticleSystem MakePS(string name, Texture2D tex, int max, float lifeMin, float lifeMax)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.playOnAwake = false; main.maxParticles = max; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax); main.gravityModifier = 0.02f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.drag = 3.5f; lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = true;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(0.15f, 1f), new Keyframe(1, 0f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0, 1) });
            col.color = gr;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.05f; noise.frequency = 1.5f; noise.scrollSpeed = 0.4f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = new Material(particleMat) { mainTexture = tex, name = name };
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.minParticleSize = 0.0005f; r.maxParticleSize = 0.5f;
            ps.Play();
            return ps;
        }

        Pooled MakeQuad(string name, Mesh quad, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.enabled = false;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return new Pooled { tr = go.transform, mr = mr };
        }

        // ───── 发射 ─────
        void Burst(Vector3 p, Color c, int n, float speed, float size)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                ep.position = p + Random.insideUnitSphere * size * 0.3f;
                ep.velocity = Random.onUnitSphere * speed * Random.Range(0.3f, 1f);
                ep.startColor = Color.Lerp(c, Color.white, Random.value * 0.35f);
                ep.startSize = size * Random.Range(0.12f, 0.35f);
                ep.startLifetime = Random.Range(0.6f, 1.5f);
                motes.Emit(ep, 1);
            }
            int ns = Mathf.Max(2, n / 7);
            for (int i = 0; i < ns; i++)
            {
                ep.position = p + Random.insideUnitSphere * size * 0.4f;
                ep.velocity = Random.onUnitSphere * speed * Random.Range(0.4f, 1.2f);
                ep.startColor = new Color(1f, 0.96f, 0.88f, 1f);
                ep.startSize = size * Random.Range(0.35f, 0.7f);
                ep.startLifetime = Random.Range(0.5f, 1.1f);
                sparkles.Emit(ep, 1);
            }
        }

        /// <summary>吞星兽退散：嘴里喷出一股星云（锥形往前喷 + 一圈光环）</summary>
        public void BossVent(Vector3 p, Vector3 dir, Color c, float scale)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < 90; i++)
            {
                var d = (dir.normalized + Random.insideUnitSphere * 0.45f).normalized;
                ep.position = p + Random.insideUnitSphere * scale * 0.12f;
                ep.velocity = d * Random.Range(0.8f, 2.2f) * scale * 2.2f;
                ep.startColor = Color.Lerp(c, Color.white, Random.value * 0.5f);
                ep.startSize = scale * Random.Range(0.1f, 0.28f);
                ep.startLifetime = Random.Range(0.5f, 1.2f);
                motes.Emit(ep, 1);
            }
            Ring(p, dir, c, scale * 1.1f, 0.8f);
            AudioKit.Instance?.Play(-6, 0.3f, 0.9f);
        }

        /// <summary>吞星兽退散收尾：整头化成一片星尘慢慢散开（不闪，护眼）</summary>
        public void BossDissolve(Vector3 p, float scale)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < 260; i++)
            {
                ep.position = p + Random.insideUnitSphere * scale * 0.42f;
                ep.velocity = Random.onUnitSphere * Random.Range(0.1f, 0.5f) * scale + Vector3.up * 0.12f * scale;
                ep.startColor = Random.value < 0.4f ? new Color(1f, 0.72f, 0.95f) : Color.Lerp(new Color(0.65f, 0.55f, 1f), Color.white, Random.value * 0.6f);
                ep.startSize = scale * Random.Range(0.06f, 0.2f);
                ep.startLifetime = Random.Range(1.4f, 3f);
                motes.Emit(ep, 1);
            }
            for (int i = 0; i < 40; i++)
            {
                ep.position = p + Random.insideUnitSphere * scale * 0.4f;
                ep.velocity = Random.onUnitSphere * Random.Range(0.1f, 0.4f) * scale;
                ep.startColor = new Color(1f, 0.95f, 0.9f);
                ep.startSize = scale * Random.Range(0.14f, 0.3f);
                ep.startLifetime = Random.Range(1f, 2.2f);
                sparkles.Emit(ep, 1);
            }
            Ring(p, Vector3.up, new Color(1f, 0.8f, 1f), scale * 2.2f, 1.6f);
        }

        void Ring(Vector3 p, Vector3 tangent, Color c, float size, float dur = 0.7f)
        {
            for (int i = 0; i < rings.Count; i++)
            {
                if (rings[i].on) continue;
                var r = rings[i]; r.on = true; r.t = 0; r.dur = dur; r.size = size; r.col = c;
                var toCam = (Camera.main ? Camera.main.transform.position - p : Vector3.back).normalized;
                var nrm = (tangent.normalized + toCam * 0.8f).normalized;   // 大致垂直于路线，又偏向相机一点（不至于侧成一条线）
                r.tr.SetPositionAndRotation(p, Quaternion.LookRotation(nrm)); r.tr.localScale = Vector3.one * size; r.mr.enabled = true;
                rings[i] = r; return;
            }
        }

        void Flash(Vector3 p, Color c, float size, float dur = 0.35f)
        {
            for (int i = 0; i < flashes.Count; i++)
            {
                if (flashes[i].on) continue;
                var f = flashes[i]; f.on = true; f.t = 0; f.dur = dur; f.size = size; f.col = c;
                f.tr.position = p; f.mr.enabled = true; flashes[i] = f; return;
            }
        }

        void Constellation(List<Vector3> pts, Color c)
        {
            if (pts.Count < 2) return;
            foreach (var k in cons)
            {
                if (k.on) continue;
                k.on = true; k.t = 0; k.pts = pts.ToArray(); k.col = Color.Lerp(c, Color.white, 0.6f);
                k.lr.positionCount = 0; k.lr.enabled = true; return;
            }
        }

        Vector3 StarPos(int ci, int id, double s)
        {
            if (gems && gems.Drawn.TryGetValue(id, out var p)) return p;
            return runner.ToWorld(runner.Game.Chains[ci].Track.PointAtU(System.Math.Max(0, s)));
        }

        void OnEvent(GameEvent e)
        {
            var g = runner.Game;
            switch (e.Type)
            {
                case "eliminated":
                {
                    int combo = Mathf.Max(1, e.Combo);
                    // 爽感（11-24 用户）：连锁的那一下整串星先停住几十毫秒再合拢，“咔嗒”一下的打击感
                    if (combo >= 2) runner.HitStop(Mathf.Min(0.12f, 0.045f + 0.025f * combo));
                    var c = Palette.Of(e.Color);
                    var pts = new List<Vector3>();
                    foreach (var s in e.Stars) { var p = StarPos(e.Ci, s.Id, s.S); pts.Add(p); Burst(p, Palette.Of(s.Color), 45 + combo * 18, 0.3f + combo * 0.09f, 0.11f); }
                    var center = runner.ToWorld(g.Chains[e.Ci].Track.PointAtU(System.Math.Max(0, e.S)));
                    var tg = runner.ToWorld(g.Chains[e.Ci].Track.PointAtU(System.Math.Max(0, e.S) + 0.5)) - center;
                    float size = 0.14f + combo * 0.09f;
                    Ring(center, tg, c, size);
                    if (combo >= 2) Ring(center, tg, Color.Lerp(c, new Color(0.8f, 0.6f, 1f), 0.5f), size * 1.5f, 1.0f);
                    Flash(center, Color.Lerp(c, Color.white, 0.6f), 0.16f + combo * 0.05f, 0.45f);
                    // 小星座：顶点往外散一点，像星图上的一组星
                    for (int i = 0; i < pts.Count; i++) pts[i] += Random.insideUnitSphere * 0.035f;
                    Constellation(pts, c);
                    break;
                }
                case "grabbed": if (e.Ids != null && gems && gems.Drawn.TryGetValue(e.Ids[0], out var gp)) Burst(gp, Palette.Of(e.Color), 10, 0.08f, 0.04f); break;
                case "placed": case "inserted":
                    if (e.Ids != null && gems && gems.Drawn.TryGetValue(e.Ids[0], out var pp)) Burst(pp, Palette.Of(e.Color), 8, 0.06f, 0.04f);
                    break;
                case "coreHit":
                {
                    // 撞星核：白闪 + 两道琥珀冲击环 + 从裂缝里迸出的碎光；裂得越多越猛，第三道（输）最猛
                    var core = runner.ToWorld(LAYOUT.Core);
                    int k3 = Mathf.Clamp(e.Cracks, 1, 3);
                    Flash(core, new Color(1f, 0.85f, 0.65f), 0.22f + 0.07f * k3, 0.45f + 0.12f * k3);
                    Ring(core, Vector3.up, new Color(1f, 0.7f, 0.45f), 0.6f + 0.15f * k3, 1.0f);
                    Ring(core, Random.onUnitSphere, new Color(1f, 0.85f, 0.7f), 0.45f + 0.1f * k3, 1.3f);
                    Burst(core, new Color(1f, 0.72f, 0.4f), 50 + 40 * k3, 0.5f + 0.15f * k3, 0.07f);
                    Burst(core, new Color(0.7f, 0.8f, 1f), 40, 0.4f, 0.06f);
                    break;
                }
                case "faded": if (e.Pos.HasValue) Burst(runner.ToWorld(e.Pos.Value), new Color(0.6f, 0.6f, 0.7f), 14, 0.05f, 0.04f); break;
                case "won": case "stageClear":
                {
                    var core = runner.ToWorld(LAYOUT.Core) + Vector3.up * 0.15f;
                    for (int k = 0; k < 5; k++) Burst(core, Palette.Of(k), 40, 0.6f, 0.07f);
                    Flash(core, Color.white, 0.25f, 0.8f);
                    Ring(core, Vector3.up, new Color(0.8f, 0.85f, 1f), 0.9f, 1.4f);
                    break;
                }
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            // 到手：手边一小圈星屑 + 细光环 + 一声很轻的高音（星落进手心的“叮”）
            if (gems && runner && runner.Game != null)
                foreach (var side in gems.JustLanded)
                    if (gems.LandedPos.TryGetValue(side, out var lp))
                    {
                        var g = runner.Game; var h = g.Hands[side];
                        var col = h.Holding != null && h.Holding.Stars.Count > 0 && h.Holding.Stars[0].Color >= 0 ? (Color)Palette.Of(h.Holding.Stars[0].Color) : Color.white;
                        Burst(lp, Color.Lerp(col, Color.white, 0.3f), 8, 0.06f, 0.025f);
                        var cam = Camera.main;
                        Ring(lp, cam ? cam.transform.right : Vector3.right, Color.Lerp(col, Color.white, 0.5f), 0.07f, 0.35f);
                        AudioKit.Instance?.PlayAt(lp, 26f, 0.16f, 0.5f);
                    }
            for (int i = 0; i < rings.Count; i++)
            {
                var r = rings[i]; if (!r.on) continue;
                r.t += dt; float k = r.t / r.dur;
                if (k >= 1) { r.on = false; r.mr.enabled = false; rings[i] = r; continue; }
                mpb.Clear(); mpb.SetColor("_Color", r.col); mpb.SetFloat("_K", k); r.mr.SetPropertyBlock(mpb);
                rings[i] = r;
            }
            for (int i = 0; i < flashes.Count; i++)
            {
                var f = flashes[i]; if (!f.on) continue;
                f.t += dt; float k = f.t / f.dur;
                if (k >= 1) { f.on = false; f.mr.enabled = false; flashes[i] = f; continue; }
                float s = f.size * (0.6f + 0.6f * Mathf.Sin(Mathf.Min(1, k * 2.2f) * Mathf.PI * 0.5f));
                f.tr.localScale = Vector3.one * s;
                mpb.Clear(); mpb.SetColor("_Color", f.col); mpb.SetFloat("_Intensity", 3.2f * (1 - k) * (1 - k)); f.mr.SetPropertyBlock(mpb);
                flashes[i] = f;
            }
            foreach (var k in cons)
            {
                if (!k.on) continue;
                k.t += dt;
                // 0.1 s 后开始逐段连线，0.5 s 连完；1.4 s 后 1 s 淡出
                float draw = Mathf.Clamp01((k.t - 0.1f) / 0.4f);
                int n = Mathf.Max(1, Mathf.CeilToInt(draw * k.pts.Length));
                k.lr.positionCount = n; for (int i = 0; i < n; i++) k.lr.SetPosition(i, k.pts[i]);
                float a = k.t < 1.4f ? Mathf.Min(1, k.t * 5f) : Mathf.Max(0, 1 - (k.t - 1.4f));
                var cc = k.col; cc.a = a * 0.8f; k.lr.startColor = k.lr.endColor = cc;
                if (k.t > 2.4f) { k.on = false; k.lr.enabled = false; }
            }
        }
    }
}
