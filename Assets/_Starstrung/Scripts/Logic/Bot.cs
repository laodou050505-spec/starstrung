// 自动测试机器人（纯逻辑）：对应 JS bot.js。
// 用相同的 xorshift32 随机数（uint _rs），与 JS 逐位一致。
// 只用一只手（右手），react 秒做一个动作。

using System;
using System.Collections.Generic;

namespace Starstrung.Logic
{
    public class BotOpts
    {
        public double React  = 0.6;
        public string Side   = "right";
        public double Vision = 1;
        public int    Seed   = 1;
    }

    // 布局计划：摘下 (SrcCi, SrcI) 或从 stash (SrcStash)，放到 (Ci, Gap)
    class BotPlan
    {
        public int Ci;
        public int Gap;
        public int SrcStash = -1;  // ≥0 = 来自星盘
        public int SrcCi    = -1;  // ≥0 = 来自串
        public int SrcI;
    }

    // Rehearse 返回值
    struct RehResult
    {
        public int      Gained;
        public int      Combo;
        public ChainSim Sim;
    }

    public class Bot
    {
        readonly Game   _game;
        readonly Hand   _hand;
        readonly double _react;
        readonly double _vision;
        double          _timer;
        uint            _rs;        // xorshift32
        BotPlan         _plan;
        public int      Actions { get; private set; }

        public Bot(Game game, BotOpts opts = null)
        {
            opts    = opts ?? new BotOpts();
            _game   = game;
            _react  = opts.React;
            _vision = opts.Vision;
            _timer  = opts.React;
            _rs     = (uint)opts.Seed != 0u ? (uint)opts.Seed : 1u;
            _hand   = game.Hands[opts.Side];
            _hand.Tracked = true;
        }

        // ──────────────────── 主循环 ────────────────────

        public void Update(double dt)
        {
            _timer -= dt;
            if (_timer > 0 || _game.Status != "playing") return;
            _timer = _react;
            if (Special()) return;
            if (_hand.Holding != null) Place(); else Choose();
        }

        // ──────────────────── 随机数（xorshift32） ────────────────────

        // rand01()：与 JS 逐位一致（uint 右移天然无符号）
        double Rand01()
        {
            unchecked
            {
                _rs ^= _rs << 13;
                _rs ^= _rs >> 17;
                _rs ^= _rs << 5;
            }
            return _rs / 4294967296.0;
        }

        // sees()：只有 vision < 1 时才推进 RNG
        bool Sees()
        {
            if (_vision >= 1) return true;
            unchecked
            {
                _rs ^= _rs << 13;
                _rs ^= _rs >> 17;
                _rs ^= _rs << 5;
            }
            return _rs / 4294967296.0 < _vision;
        }

        double Danger(int ci, double s) => s / _game.Chains[ci].Track.Slots;

        // ──────────────────── 推演 ────────────────────

        RehResult Rehearse(ChainSim sim, Action<ChainSim> fn)
        {
            var c      = sim.Clone();
            int before = c.Count;
            fn(c);
            for (int k = 0; k < 75; k++) c.Update(1.0 / 30);
            return new RehResult { Gained = before - c.Count, Combo = c.MaxCombo, Sim = c };
        }

        // ──────────────────── Special ────────────────────

        bool Special()
        {
            var g = _game;
            // 岔路：信标指着短路且串头快到岔口 → 换到长路
            if (g.Fork != null && g.Fork.Next == 0 && !g.Fork.Committed && g.Fork.Cooldown <= 0)
            {
                var head = g.Chains[0].Sim.Stars.Count > 0 ? g.Chains[0].Sim.Stars[0] : null;
                if (head != null && head.S > ((ForkTrack)g.Tracks[0]).ForkU - 30 && Sees())
                {
                    Actions++; return g.FlipFork();
                }
            }
            var b = g.Boss;
            if (b != null && b.MouthOpen)
            {
                var held = _hand.Holding;
                if (held != null && held.Stars.Count == 1 && held.Stars[0].Color == b.MouthColor)
                {
                    Actions++;
                    g.DropTo(_hand, new DropTarget { Type = "mouth" });
                    return true;
                }
                if (held == null)
                {
                    var sim  = g.Chains[b.Ci].Sim;
                    var cand = Moves.LoneStars(sim, b.MouthColor);
                    int idx  = cand.Count > 0 ? cand[cand.Count - 1] : -1;
                    if (idx < 0)
                        for (int k = sim.Count - 1; k >= 0; k--)
                            if (sim.CanPick(k) && sim.Stars[k].Color == b.MouthColor && sim.Stars[k].Twin < 0)
                            { idx = k; break; }
                    if (idx >= 0) { Actions++; g.PickAt(_hand, b.Ci, idx, true); return true; }
                }
            }
            return false;
        }

        // ──────────────────── Place ────────────────────

        void Place()
        {
            var g    = _game;
            var held = _hand.Holding.Stars;
            DropTarget best = null;
            double bv = 0;

            foreach (var c in g.Chains)
            {
                List<int> cand;
                if (held.Count == 1)
                    cand = Moves.InsertMatches(c.Sim, held[0].Color).ConvertAll(m => m.gap);
                else
                {
                    cand = new List<int>();
                    for (int g2 = 0; g2 <= c.Sim.Count; g2++) cand.Add(g2);
                }

                foreach (int gap in cand)
                {
                    var gp = g.GapPosAt(c.Ci, gap);
                    if (gp == null) continue;
                    var capturedHeld = held;
                    var capturedGap  = gap;
                    var r = Rehearse(c.Sim, s => { s.Insert(capturedGap, capturedHeld); });
                    if (r.Gained <= 0) continue;
                    double v = r.Gained + r.Combo * 2 + Danger(c.Ci, gp.U) * 3;
                    if (v > bv) { bv = v; best = new DropTarget { Type = "gap", Ci = c.Ci, Gap = gap }; }
                }
            }

            if (best == null && _plan != null)
            {
                int color = held[0].Color;
                var c     = g.Chains[_plan.Ci];
                DropTarget gbest = null;
                double gd = 1e9;
                for (int gap = 0; gap <= c.Sim.Count; gap++)
                {
                    int l = gap > 0            ? c.Sim.Stars[gap - 1].Color : -9;
                    int r = gap < c.Sim.Count  ? c.Sim.Stars[gap].Color     : -9;
                    if (color >= 0 && l != color && r != color) continue;
                    double d = Math.Abs(gap - _plan.Gap);
                    if (d < gd && g.GapPosAt(c.Ci, gap) != null)
                    { gd = d; gbest = new DropTarget { Type = "gap", Ci = c.Ci, Gap = gap }; }
                }
                best = gbest;
            }

            _plan = null;
            if (best == null)
            {
                int k = Array.FindIndex(g.Stash, s => s == null);
                if (k >= 0) best = new DropTarget { Type = "stash", Slot = k };
            }
            Actions++;
            g.DropTo(_hand, best); // null = 飞回串尾
        }

        // ──────────────────── Choose ────────────────────

        void Choose()
        {
            var g = _game;

            // ① 直接消（摘一颗两边合拢）
            int p1ci = -1, p1i = -1; double bv1 = 0;
            foreach (var c in g.Chains)
            {
                foreach (var m in Moves.PickMatches(c.Sim))
                {
                    if (!Sees()) continue;
                    var capturedI = m.i;
                    var r = Rehearse(c.Sim, s => { s.Pick(capturedI); });
                    double v = r.Gained + r.Combo * 2 + Danger(c.Ci, c.Sim.Stars[m.i].S) * 4;
                    if (v > bv1) { bv1 = v; p1ci = c.Ci; p1i = m.i; }
                }
            }
            if (p1ci >= 0) { Actions++; g.PickAt(_hand, p1ci, p1i, true); return; }

            // ② 星盘里能用的
            for (int k = 0; k < 2; k++)
            {
                var st = g.Stash[k];
                if (st == null) continue;
                bool useful = g.Chains.Exists(c =>
                    Moves.InsertMatches(c.Sim, st.Stars[0].Color, st.Stars.Count).Count > 0);
                if (useful)
                {
                    _hand.Holding = new HoldGroup { Stars = st.Stars, Ci = st.Ci, FlyT = 0 };
                    g.Stash[k] = null;
                    Actions++;
                    return;
                }
            }

            // ③ 摘任意一颗去别处凑 3（推演摘+放两步）
            int p3ci = -1, p3i = -1; double pv = 0;
            foreach (var c in g.Chains)
            {
                var st = c.Sim.Stars;
                for (int i = 0; i < st.Count; i++)
                {
                    if (!c.Sim.CanPick(i) || !Sees()) continue;
                    var after = c.Sim.Clone();
                    var got   = after.Pick(i);
                    if (got == null) continue;
                    int capturedCCi = c.Ci;
                    var capturedAfter = after;

                    foreach (var t in g.Chains)
                    {
                        var ts = t.Ci == c.Ci ? after : t.Sim;
                        List<int> gaps;
                        if (got.Count == 1)
                            gaps = Moves.InsertMatches(ts, got[0].Color).ConvertAll(m => m.gap);
                        else
                            gaps = new List<int>();

                        foreach (int gap in gaps)
                        {
                            var capturedGot = got;
                            var capturedGap = gap;
                            var r = Rehearse(ts, s => { s.Insert(capturedGap, capturedGot); });
                            if (r.Gained <= 0) continue;
                            int gpIdx = ts.Count > 0 ? Math.Min(gap, ts.Count - 1) : 0;
                            Star gp   = ts.Stars.Count > 0 ? ts.Stars[gpIdx] : null;
                            double dangerSrc = Danger(c.Ci, st[i].S);
                            double v = r.Gained + r.Combo * 2
                                + (gp != null ? Danger(t.Ci, gp.S) * 3 : 0)
                                + (1 - dangerSrc) * 0.5;
                            if (v > pv) { pv = v; p3ci = c.Ci; p3i = i; }
                        }
                    }
                }
            }
            if (p3ci >= 0) { Actions++; g.PickAt(_hand, p3ci, p3i, true); return; }

            // ④ 布局
            Setup();
        }

        // ──────────────────── Setup ────────────────────

        void Setup()
        {
            var g = _game;
            BotPlan best = null;
            double  bv   = 0;

            // 局部函数：捕获 bv / best（by ref）
            void TryPlace(int srcColor, Func<List<Star>> apply, Func<int, ChainSim> makeSrc)
            {
                foreach (var t in g.Chains)
                {
                    var baseS = makeSrc(t.Ci);
                    if (baseS == null) continue;
                    for (int gap = 0; gap <= baseS.Count; gap++)
                    {
                        int l = gap > 0           ? baseS.Stars[gap - 1].Color : -9;
                        int r = gap < baseS.Count ? baseS.Stars[gap].Color     : -9;
                        if (srcColor >= 0 && l != srcColor && r != srcColor) continue;
                        var after = baseS.Clone();
                        after.Insert(gap, apply());
                        after.HeldColors = new List<int>();
                        double v = (after.HasAnyMove() ? 3 : 1) + Rand01() * 0.1;
                        if (v > bv) { bv = v; best = new BotPlan { Ci = t.Ci, Gap = gap }; }
                    }
                }
            }

            // 星盘里的星
            for (int k = 0; k < 2; k++)
            {
                var st = g.Stash[k];
                if (st == null || st.Stars.Count != 1) continue;
                double before = bv;
                int kLocal = k;
                TryPlace(st.Stars[0].Color, () => st.Stars, ci => g.Chains[ci].Sim);
                if (bv > before && best != null) best.SrcStash = kLocal;
            }

            // 串里的星
            foreach (var c in g.Chains)
            {
                for (int i = 0; i < c.Sim.Count; i++)
                {
                    if (!c.Sim.CanPick(i) || c.Sim.Stars[i].Twin >= 0) continue;
                    var cAfter = c.Sim.Clone();
                    var cGot   = cAfter.Pick(i);
                    if (cGot == null) continue;
                    double before    = bv;
                    int capturedCCi  = c.Ci;
                    int capturedI    = i;
                    var capturedAfterLocal = cAfter;
                    var capturedGotLocal   = cGot;
                    TryPlace(cGot[0].Color,
                        ()   => capturedGotLocal,
                        ci2  => ci2 == capturedCCi ? capturedAfterLocal : g.Chains[ci2].Sim);
                    if (bv > before && best != null) { best.SrcCi = capturedCCi; best.SrcI = capturedI; }
                }
            }

            if (best == null || bv < 3) return;
            Actions++;
            if (best.SrcStash >= 0)
            {
                var st = g.Stash[best.SrcStash];
                _hand.Holding = new HoldGroup { Stars = st.Stars, Ci = st.Ci, FlyT = 0 };
                g.Stash[best.SrcStash] = null;
            }
            else
            {
                g.PickAt(_hand, best.SrcCi, best.SrcI, true);
            }
            _plan = best;
        }
    }
}
