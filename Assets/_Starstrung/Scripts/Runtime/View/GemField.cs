using System.Collections.Generic;
using Starstrung.Logic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>五色（和方案一致）：红宝石 ☆、金黄 ☾、祖母绿 ◎、蓝宝石 ◇、紫水晶 △。</summary>
    public static class Palette
    {
        public static readonly Color[] Jewel =
        {
            new(0.886f, 0.227f, 0.306f),
            new(0.953f, 0.757f, 0.169f),
            new(0.133f, 0.761f, 0.435f),
            new(0.110f, 0.420f, 1.000f),
            new(0.760f, 0.260f, 0.950f),
        };
        public static Color Of(int c) => c >= 0 && c < Jewel.Length ? Jewel[c] : Color.white;
    }

    /// <summary>
    /// 画所有的星（GPU 实例化一次画完）：串上的、手里的、星盘里的、悬浮的、飞回串尾的。
    /// - 串上的星位置做平滑（逻辑位置跳变时视觉不跳）；
    /// - 插入预览：拿着星指向某个缝，串在那里张开一个空位；
    /// - 正在被拉出的那颗往手的方向偏；远摘的星沿弧线飞到手里；随手松开的星沿弧线飞回串尾；
    /// - 孤色消散：变暗、缩小（材质 _Fade）；被指着：材质 _Highlight。
    /// </summary>
    public class GemField : MonoBehaviour
    {
        public GameRunner runner;
        public Mesh mesh;
        public Material material;

        const int Max = 1023;
        readonly Matrix4x4[] mats = new Matrix4x4[Max];
        readonly Vector4[] cols = new Vector4[Max];
        readonly float[] sym = new float[Max], seed = new float[Max], hi = new float[Max], fade = new float[Max], rainbow = new float[Max], ice = new float[Max];
        readonly Dictionary<int, double> vis = new();
        MaterialPropertyBlock mpb;
        int n;
        /// <summary>每颗星这一帧画在哪（世界坐标），给特效 / 准星用</summary>
        public readonly Dictionary<int, Vector3> Drawn = new();
        /// <summary>每颗星这一帧画出来的直径（米），给准星用</summary>
        public readonly Dictionary<int, float> DrawnSize = new();
        // 被指着的平滑程度（0..1，按星 id）：指中时星放大一圈、整颗提亮，移开时慢慢回落（不闪跳）
        readonly Dictionary<int, float> hoverA = new();
        readonly List<int> hoverKeys = new();
        // 到手（11-25 爽感）：星落进手里的那一刻弹一下、亮一下（左右手各一个，0..1 倒计时）
        readonly Dictionary<string, float> landPulse = new() { ["left"] = 0f, ["right"] = 0f };
        readonly Dictionary<string, bool> landed = new() { ["left"] = false, ["right"] = false };
        /// <summary>某只手刚刚“到手”（给特效 / 声音用）：这一帧为真</summary>
        public readonly HashSet<string> JustLanded = new();
        public readonly Dictionary<string, Vector3> LandedPos = new();
        /// <summary>到手弹跳的剩余量（0..1，调试 / 自动测试读）</summary>
        public float LandPulse(string side) => landPulse.TryGetValue(side, out var v) ? v : 0f;
        /// <summary>关卡里的星显隐（0..1）：进出关卡时整串星缩小淡出，不凭空消失</summary>
        [System.NonSerialized] public float presence = 1f;

        // 界面用的宝石星（选关星、按钮星、标题星链）：界面脚本在 Update 里调 Extra()，这一帧和关卡里的星一起画
        struct ExtraGem { public Vector3 p; public float size, hi, seed; public int color; }
        readonly List<ExtraGem> extras = new();
        public void Extra(Vector3 p, float size, int color, float highlight = 0f, float seed = 0.3f)
        { if (size > 0.0005f) extras.Add(new ExtraGem { p = p, size = size, color = color, hi = highlight, seed = seed }); }

        void OnEnable()
        {
            mpb = new MaterialPropertyBlock();
            if (!GetComponent<PlayReticle>()) gameObject.AddComponent<PlayReticle>().field = this;
            if (runner) runner.OnLevelStart += () => vis.Clear();
        }

        void LateUpdate()
        {
            n = 0; Drawn.Clear(); DrawnSize.Clear();
            if (!mesh || !material) { extras.Clear(); return; }
            var g = runner ? runner.Game : null;
            if (g != null && presence > 0.001f) DrawGame(g);
            foreach (var e in extras)
            {
                if (n >= Max) break;
                mats[n] = Matrix4x4.TRS(e.p, Quaternion.identity, Vector3.one * e.size);
                cols[n] = e.color >= 0 ? (Vector4)Palette.Of(e.color) : new Vector4(1, 1, 1, 1);
                sym[n] = e.color; seed[n] = e.seed; hi[n] = e.hi; fade[n] = 0; rainbow[n] = e.color < 0 ? 1f : 0f; ice[n] = 0; n++;
            }
            extras.Clear();
            if (n == 0) return;
            mpb.Clear();
            mpb.SetVectorArray("_Color", cols); mpb.SetFloatArray("_Symbol", sym); mpb.SetFloatArray("_Seed", seed);
            mpb.SetFloatArray("_Highlight", hi); mpb.SetFloatArray("_Fade", fade); mpb.SetFloatArray("_Rainbow", rainbow); mpb.SetFloatArray("_Ice", ice);
            var rp = new RenderParams(material) { matProps = mpb, worldBounds = new Bounds(Vector3.zero, Vector3.one * 40f), layer = gameObject.layer };
            Graphics.RenderMeshInstanced(rp, mesh, 0, mats, n);
        }

        void DrawGame(Game g)
        {
            float dt = Time.deltaTime;

            // 插入预览：哪条串的哪个缝要张开
            var opens = new List<DropTarget>();
            foreach (var h in g.Hands.Values) if (h.Holding != null && h.Drop != null && h.Drop.Type == "gap") opens.Add(h.Drop);
            // 被指着（空手 hover 的星）
            var hovered = new HashSet<int>();
            foreach (var h in g.Hands.Values) if (h.Holding == null && h.Hover != null && h.Hover.Type == "star") hovered.Add(h.Hover.Id);
            foreach (var id in hovered) if (!hoverA.ContainsKey(id)) hoverA[id] = 0f;
            hoverKeys.Clear(); hoverKeys.AddRange(hoverA.Keys);
            foreach (var id in hoverKeys)
            {
                float a = Mathf.MoveTowards(hoverA[id], hovered.Contains(id) ? 1f : 0f, dt * (hovered.Contains(id) ? 12f : 5f));
                if (a <= 0f) hoverA.Remove(id); else hoverA[id] = a;
            }

            foreach (var c in g.Chains)
            {
                var st = c.Sim.Stars;
                for (int i = 0; i < st.Count; i++)
                {
                    var s = st[i];
                    double v = vis.TryGetValue(s.Id, out var vv) ? vv : s.S;
                    v += (s.S - v) * Mathf.Min(1f, dt * 16f);
                    vis[s.Id] = v;
                    double u = v;
                    foreach (var o in opens)
                    {
                        if (o.Ci != c.Ci) continue;
                        int k = i < o.Gap ? o.Gap - i : i - o.Gap + 1;
                        if (k <= 4) u += (i < o.Gap ? 1 : -1) * 0.45 * (1 - (k - 1) / 4.0);
                    }
                    if (u < -0.5) continue;
                    float size = (float)(c.Track.SizeAtU(System.Math.Max(0, u)) * System.Math.Min(1, u + 0.5));
                    // 进关时路线从入口流出来：星只在已展开的那段上出现（随展开前沿一颗颗长出来），退出时跟着收回
                    if (AppFlow.Grow < 999f)
                    {
                        float m = (float)c.Track.UToM(System.Math.Max(0, u));
                        size *= Mathf.SmoothStep(0, 1, Mathf.Clamp01((AppFlow.Grow - m) / 0.6f));
                        if (size < 0.001f) continue;
                    }
                    var p = runner.ToWorld(c.Track.PointAtU(u));
                    foreach (var h in g.Hands.Values)
                        if (h.Pull != null && h.Pull.Id == s.Id && h.Tracked) p = Vector3.Lerp(p, HandPoint(h), 0.45f);
                    float ha = hoverA.TryGetValue(s.Id, out var hv) ? hv : 0f;
                    Put(s, p, size * (1f + 0.25f * ha * (2f - ha)), ha);
                }
            }
            const float near = 0.065f;
            JustLanded.Clear();
            foreach (var h in g.Hands.Values)
            {
                bool inHand = h.Holding != null && h.Holding.FlyT <= 0;
                if (inHand && !landed[h.Side]) { landPulse[h.Side] = 1f; JustLanded.Add(h.Side); }
                landed[h.Side] = inHand;
                landPulse[h.Side] = Mathf.MoveTowards(landPulse[h.Side], 0f, dt * 3.5f);
                if (h.Holding == null) continue;
                float lt = 1f - landPulse[h.Side];
                float pop = landPulse[h.Side] > 0f ? 1f + 0.32f * Mathf.Sin(lt * Mathf.PI) * (1f - lt) : 1f;
                var hp = h.Tracked ? HandPoint(h) : runner.ToWorld(LAYOUT.Core) + new Vector3(h.Side == "right" ? 0.18f : -0.18f, 0.05f, 0.1f);
                if (JustLanded.Contains(h.Side)) LandedPos[h.Side] = hp;
                for (int k = 0; k < h.Holding.Stars.Count; k++)
                {
                    var p = hp + runner.rig.right * (k * near);
                    if (h.Holding.FlyT > 0)
                    {
                        float t = 1f - (float)(h.Holding.FlyT / PICK.FlyIn);
                        float e = t * t * (3 - 2 * t);
                        p = Vector3.Lerp(runner.ToWorld(h.Holding.From), p, e) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.12f);
                    }
                    Put(h.Holding.Stars[k], p, near * pop, landPulse[h.Side]);
                }
            }
            for (int k = 0; k < g.Stash.Length; k++)
            {
                var st = g.Stash[k]; if (st == null) continue;
                for (int j = 0; j < st.Stars.Count; j++) Put(st.Stars[j], runner.ToWorld(LAYOUT.Stash[k]) + Vector3.up * 0.03f + runner.rig.right * (j * near), near, 0);
            }
            foreach (var f in g.Floating)
                for (int j = 0; j < f.Stars.Count; j++) Put(f.Stars[j], runner.ToWorld(f.Pos) + Vector3.up * (Mathf.Sin(Time.time * 2f) * 0.01f) + runner.rig.right * (j * near), near, 0);
            foreach (var r in g.Returning)
            {
                var c = g.Chains[r.Ci]; var tail = c.Sim.Count > 0 ? c.Sim.Stars[c.Sim.Count - 1] : null;
                var to = runner.ToWorld(c.Track.PointAtU(tail != null ? System.Math.Max(0, tail.S - 1) : 0));
                float t = Mathf.Min(1f, (float)(r.T / PICK.ReturnTime));
                foreach (var s in r.Stars) Put(s, Vector3.Lerp(runner.ToWorld(r.From), to, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.4f), near, 0);
            }

        }

        Vector3 HandPoint(Hand h)
        {
            var inp = runner.LastInput;
            if (inp != null) foreach (var x in inp.Hands) if (x.Side == h.Side && x.Tracked) return runner.ToWorld(x.Point);
            return runner.ToWorld(LAYOUT.Core);
        }

        void Put(Star s, Vector3 p, float size, float highlight)
        {
            if (n >= Max) return;
            double f = runner.Game.FadeOf(s.Id);
            float fd = f >= 0 ? (float)(f / 1.5) : 0f;
            float sz = size * (1f - fd * 0.6f) * presence;
            mats[n] = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * sz);
            cols[n] = s.Color >= 0 ? (Vector4)Palette.Of(s.Color) : new Vector4(1, 1, 1, 1);
            sym[n] = s.Color >= 0 ? s.Color : -1;
            seed[n] = (float)((s.Id * 0.6180339) % 1.0);
            hi[n] = highlight;
            fade[n] = fd > 0 ? 0.4f + 0.4f * Mathf.Abs(Mathf.Sin((float)f * 14f)) : 0f;
            rainbow[n] = s.Color < 0 ? 1f : 0f;
            ice[n] = s.Kind == StarKind.Ice ? 1f : 0f;
            Drawn[s.Id] = p; DrawnSize[s.Id] = sz;
            n++;
        }
    }
}
