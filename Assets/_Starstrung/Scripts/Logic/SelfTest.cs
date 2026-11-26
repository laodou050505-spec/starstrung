// 自测（无需 NUnit）：对应 test/chain.test.js（T01-T19）和 test/game.test.js（G01-G06）。
// RunAll() 返回 "PASS n / FAIL n / TOTAL n\n..." 格式字符串。

using System;
using System.Collections.Generic;
using System.Text;

namespace Starstrung.Logic
{
    public static class SelfTest
    {
        // ──────────────────── 颜色常量 ────────────────────
        const int R = 0, G = 1, B = 2, Y = 3;

        static int _pass, _fail;
        static StringBuilder _sb;

        // ──────────────────── 入口 ────────────────────

        public static string RunAll()
        {
            _pass = 0; _fail = 0; _sb = new StringBuilder();

            // chain.test.js
            T01(); T02(); T03(); T04(); T05(); T06(); T07(); T08(); T09(); T10();
            T11(); T12(); T13(); T14(); T15(); T16(); T17(); T18(); T19();

            // game.test.js
            G01(); G02(); G03(); G04(); G05(); G06();

            _sb.AppendLine($"PASS {_pass} / FAIL {_fail} / TOTAL {_pass + _fail}");
            return _sb.ToString();
        }

        // ──────────────────── 断言工具 ────────────────────

        static void Ok(bool cond, string name, string msg = null)
        {
            if (cond)
            {
                _pass++;
                _sb.AppendLine("PASS " + name);
            }
            else
            {
                _fail++;
                _sb.AppendLine("FAIL " + name + (msg != null ? ": " + msg : ""));
            }
        }

        static void Seq(List<int> got, List<int> want, string name)
        {
            if (got == null) { Ok(false, name, "got=null"); return; }
            if (got.Count != want.Count)
            {
                Ok(false, name, $"len {got.Count}!={want.Count}  got=[{string.Join(",", got)}]");
                return;
            }
            for (int i = 0; i < got.Count; i++)
                if (got[i] != want[i]) { Ok(false, name, $"[{i}]{got[i]}!={want[i]}"); return; }
            Ok(true, name);
        }

        // ──────────────────── 辅助函数（对应 JS 测试帮手） ────────────────────

        // SS(color, kind?, id?, twin?) → StarSource，对应 JS { color, kind, id, twin }
        static StarSource SS(int color, StarKind kind = StarKind.Normal, object id = null, object twin = null)
            => new StarSource { Color = color, Kind = kind, OrigId = id, TwinOrigId = twin };

        // St(id, color, kind?) → Star，用于 Insert / AppendTail
        static Star St(int id, int color, StarKind kind = StarKind.Normal)
            => new Star { Id = id, Color = color, Kind = kind, Twin = -1 };

        // make()：trackLength=200, noMoveRainbowSeconds=999, speedOverride=0
        static ChainSim Make(StarSource[] srcs,
            double noMoveRainbow = 999,
            List<double[]> reverseZones = null,
            double reverseSpeed = 1.2)
        {
            var cfg = new ChainCfg
            {
                TrackLength          = 200,
                NoMoveRainbowSeconds = noMoveRainbow,
                ReverseSpeed         = reverseSpeed,
            };
            if (reverseZones != null) cfg.ReverseZones = reverseZones;

            var sim  = new ChainSim(cfg);
            var list = new List<object>();
            foreach (var s in srcs) list.Add(s);
            sim.Load(list, 50);
            sim.SpeedOverride = 0;
            return sim;
        }

        // colors(sim)
        static List<int> Colors(ChainSim sim) => sim.Stars.ConvertAll(s => s.Color);

        // settle(sim, t=3) → t*60 次 update(1/60)
        static void Settle(ChainSim sim, double t = 3)
        {
            int n = (int)(t * 60);
            for (int i = 0; i < n; i++) sim.Update(1.0 / 60);
        }

        // elim(sim)
        static List<GameEvent> Elim(ChainSim sim)
            => sim.DrainEvents().FindAll(e => e.Type == "eliminated");

        // 在 ChainSim 里找到第一颗可摘、非双子、颜色≥0 的星的下标（-1=没有）
        static int FindPickable(ChainSim sim)
        {
            for (int k = 0; k < sim.Stars.Count; k++)
                if (sim.CanPick(k) && sim.Stars[k].Twin < 0 && sim.Stars[k].Color >= 0) return k;
            return -1;
        }

        // 把 TrackSpec 序列化为唯一字符串（对应 JS JSON.stringify(L.track)）
        static string TrackKey(TrackSpec t)
        {
            if (t == null) return "null";
            return $"{t.Type},{t.RStart},{t.REnd},{t.Pow},{t.YOuter},{t.YInner}," +
                   $"{t.Wave},{t.WaveN},{t.Steps},{t.Bulge},{t.BulgeN}," +
                   $"{t.Sweeps},{t.Span},{t.Center},{t.StartSide},{t.Narrow},{t.TurnDrop}," +
                   $"{t.Turns},{t.A0},{t.Dir},{t.ForkR},{t.ShortSweeps},{t.LongSweeps},{t.Lateral}";
        }

        // ══════════════════════════════════════════════════════════════════════
        //  chain.test.js 的 19 个测试
        // ══════════════════════════════════════════════════════════════════════

        // T01：摘走中间一颗，两边合拢成 3 同色就消除
        static void T01()
        {
            const string N = "T01摘中间合拢消除";
            var sim = Make(new[] { SS(G), SS(R), SS(R), SS(B), SS(R), SS(G) });
            var got = sim.Pick(3);
            Seq(got?.ConvertAll(s => s.Color) ?? new List<int>(), new List<int> { B }, N + "_pick");
            Settle(sim);
            Seq(Colors(sim), new List<int> { G, G }, N + "_colors");
            var e = Elim(sim);
            Ok(e.Count == 1, N + "_elimCount");
            Ok(e.Count > 0 && e[0].Combo == 1, N + "_combo");
        }

        // T02：合拢大约 0.25 秒内完成（测 0.3 s）
        static void T02()
        {
            const string N = "T02合拢0.3s内";
            var sim = Make(new[] { SS(G), SS(R), SS(B), SS(Y), SS(G) });
            sim.Pick(2);
            for (int i = 0; i < 18; i++) sim.Update(1.0 / 60);
            Ok(sim.Touching(1), N, "0.3s后应已合拢: " + sim.Dump());
        }

        // T03：连锁：消除后两端同色继续吸，连锁等级递增
        static void T03()
        {
            const string N = "T03连锁";
            // G Y R R [B] R Y Y G → 摘B: RRR消(1) → Y|YY吸过去: YYY消(2) → G|G剩
            var sim = Make(new[] { SS(G), SS(Y), SS(R), SS(R), SS(B), SS(R), SS(Y), SS(Y), SS(G) });
            sim.Pick(4);
            Settle(sim);
            Seq(Colors(sim), new List<int> { G, G }, N + "_colors");
            var e = Elim(sim);
            Seq(e.ConvertAll(x => x.Combo), new List<int> { 1, 2 }, N + "_comboSeq");
            Ok(sim.MaxCombo == 2, N + "_maxCombo");
        }

        // T04：消除后两端不同色：不吸，前段停住等后段推上来
        static void T04()
        {
            const string N = "T04不同色不吸";
            var sim = Make(new[] { SS(G), SS(R), SS(R), SS(B), SS(R), SS(Y) });
            // opts.speed:0 与 Make 默认 speedOverride=0 相同
            sim.Pick(3);
            Settle(sim, 1);
            Seq(Colors(sim), new List<int> { G, Y }, N + "_colors");
            Ok(!sim.Touching(0), N + "_gap", "两端不同色应保持缺口");
            sim.SpeedOverride = 2;
            Settle(sim, 3);
            Ok(sim.Touching(0), N + "_touch", "尾段推上来后贴合");
            Ok(Elim(sim).Count == 1, N + "_elim");
        }

        // T05：放：塞进去凑满 3 颗就消除
        static void T05()
        {
            const string N = "T05放凑满消除";
            var sim = Make(new[] { SS(G), SS(B), SS(B), SS(Y) });
            bool done = sim.Insert(1, new List<Star> { St(999, B) });
            Ok(done, N + "_ret");
            Seq(Colors(sim), new List<int> { G, Y }, N + "_colors");
        }

        // T06：放：没凑满就留在串里，后面的星往后挤
        static void T06()
        {
            const string N = "T06放不消留串";
            var sim = Make(new[] { SS(G), SS(B), SS(Y) });
            double tailS = sim.Stars[2].S;
            sim.Insert(1, new List<Star> { St(999, R) });
            Seq(Colors(sim), new List<int> { G, R, B, Y }, N + "_colors");
            Ok(Math.Abs(sim.Stars[3].S - (tailS - 1)) < 1e-9, N + "_tailS");
            bool all = true;
            for (int i = 0; i < 3; i++) if (!sim.Touching(i)) { all = false; break; }
            Ok(all, N + "_touching");
        }

        // T07：冰星摘不动，但能在两边凑同色一起消
        static void T07()
        {
            const string N = "T07冰星";
            var sim = Make(new[] { SS(G), SS(R), SS(R, StarKind.Ice), SS(B), SS(R), SS(Y) });
            Ok(sim.Pick(2) == null, N + "_cantPick");
            sim.Pick(3); // 摘 B
            Settle(sim);
            Seq(Colors(sim), new List<int> { G, Y }, N + "_colors");
        }

        // T08：双子星只能一起摘
        static void T08()
        {
            const string N = "T08双子星";
            var sim = Make(new[] {
                SS(G),
                SS(R, StarKind.Normal, "a", "b"),
                SS(B, StarKind.Normal, "b", "a"),
                SS(Y),
            });
            var got = sim.Pick(2);
            Seq(got?.ConvertAll(s => s.Color) ?? new List<int>(), new List<int> { R, B }, N + "_pick");
            Seq(Colors(sim), new List<int> { G, Y }, N + "_colors");
        }

        // T09：彩星塞进去变成两边较长同色段的颜色
        static void T09()
        {
            const string N = "T09彩星";
            var sim = Make(new[] { SS(G), SS(R), SS(R), SS(B) });
            sim.Insert(3, new List<Star> { St(999, -1, StarKind.Rainbow) });
            Seq(Colors(sim), new List<int> { G, B }, N);
        }

        // T10：炸星被消除时前后各炸 2 颗
        static void T10()
        {
            const string N = "T10炸星";
            var sim = Make(new[] {
                SS(Y), SS(G), SS(B), SS(R),
                SS(R, StarKind.Bomb),
                SS(R), SS(G), SS(B), SS(Y),
            });
            sim.Insert(4, new List<Star>());                       // 空操作
            sim.Insert(3, new List<Star> { St(999, R) });
            Seq(Colors(sim), new List<int> { Y, Y }, N + "_colors");
            var e = Elim(sim);
            Ok(e.Count > 0 && e[0].Bomb, N + "_bomb");
        }

        // T11：时停星被消除时整串停 3 秒
        static void T11()
        {
            const string N = "T11时停";
            var sim = new ChainSim(new ChainCfg { TrackLength = 200, NoMoveRainbowSeconds = 999 });
            var list = new List<object> { SS(G), SS(R), SS(R, StarKind.TimeStop), SS(B) };
            sim.Load(list, 50);
            sim.SpeedOverride = 1;                                 // 有速度，能检测停没停

            sim.Insert(3, new List<Star> { St(999, R) });         // 触发 RRR 消 + 时停
            double s0 = sim.Stars.Count > 0 ? sim.Stars[sim.Stars.Count - 1].S : 0;

            Settle(sim, 2);                                        // 2 s：时停 3→1 s，仍冻结
            double s2 = sim.Stars.Count > 0 ? sim.Stars[sim.Stars.Count - 1].S : s0;
            Ok(Math.Abs(s2 - s0) < 1e-6, N + "_frozen", "时停期间尾段不动");

            Settle(sim, 2);                                        // 再 2 s：时停 1→0 s，然后推进
            double s4 = sim.Stars.Count > 0 ? sim.Stars[sim.Stars.Count - 1].S : s0;
            Ok(s4 > s0, N + "_unfrozen", "时停结束后继续推进");
        }

        // T12：串头撞星核：裂纹递增；第 3 道裂纹即输
        static void T12()
        {
            const string N = "T12撞星核";
            var sim = new ChainSim(new ChainCfg
            {
                TrackLength          = 20,
                CoreKnockback        = 5,
                IntroHeadS           = -100,  // 跳过入场动画
                BaseSpeed            = 4,
                MaxSpeed             = 4,
                SpeedRamp            = 0,
                NoMoveRainbowSeconds = 999,
            });
            var list = new List<object> { SS(R), SS(G), SS(B) };
            sim.Load(list, 15);
            Settle(sim, 2);
            Ok(sim.Cracks >= 1, N + "_cracks1");
            Settle(sim, 20);
            Ok(sim.Cracks == 3, N + "_cracks3");
            Ok(sim.Lost,        N + "_lost");
        }

        // T13：无解保护：没有可消走法时，彩星混进串尾
        static void T13()
        {
            const string N = "T13无解保护";
            var sim = new ChainSim(new ChainCfg { TrackLength = 200, NoMoveRainbowSeconds = 2 });
            var list = new List<object> { SS(R), SS(G), SS(B), SS(Y) };
            sim.Load(list, 50);
            sim.SpeedOverride = 0;
            Ok(!sim.HasAnyMove(), N + "_noMove");
            Settle(sim, 3);
            var last = sim.Stars.Count > 0 ? sim.Stars[sim.Stars.Count - 1] : null;
            Ok(last != null && last.Kind == StarKind.Rainbow, N + "_rainbow");
        }

        // T14：走法检查
        static void T14()
        {
            const string N = "T14走法检查";
            Ok( Make(new[] { SS(R), SS(R), SS(G), SS(B), SS(R) }).HasAnyMove(), N + "_has");
            Ok(!Make(new[] { SS(R), SS(G), SS(R), SS(B) }).HasAnyMove(),        N + "_no");
            var sim = Make(new[] { SS(R), SS(R), SS(G), SS(B) });
            Ok(!sim.HasAnyMove(), N + "_no2");
            sim.HeldColors = new List<int> { R };
            Ok(sim.HasAnyMove(),  N + "_held");
        }

        // T15：开局冲刺入场后减速到关卡速度
        static void T15()
        {
            const string N = "T15开局冲刺";
            // 直接构造 ChainSim，不用 Make（需要正常速度而非 speedOverride=0）
            var sim = new ChainSim(new ChainCfg { IntroHeadS = 10, IntroSpeed = 10, BaseSpeed = 0.5 });
            var list = new List<object>();
            for (int i = 0; i < 20; i++) list.Add(SS(i % 3));
            sim.Load(list);                                        // headS 默认 -1（入场前）
            Settle(sim, 2);
            Ok(sim.Stars.Count > 0 && sim.Stars[0].S >= 10, N + "_entry");
            Ok(Math.Abs(sim.CurrentSpeed - 0.5) < 0.1,      N + "_speed");
        }

        // T16：接回串尾（星守恒）
        static void T16()
        {
            const string N = "T16appendTail";
            var sim = Make(new[] { SS(G), SS(B), SS(R), SS(R) });
            sim.AppendTail(St(999, R));
            Seq(Colors(sim), new List<int> { G, B }, N);
        }

        // T17：倒流区：断在前面的段落在区内就往回漂，碰上后段再贴合
        static void T17()
        {
            const string N = "T17倒流区";
            var sim = Make(
                new[] { SS(Y), SS(R), SS(R), SS(G), SS(B) },
                reverseZones: new List<double[]> { new double[] { 40, 60 } },
                reverseSpeed: 2
            );
            // 插入 R 凑成 RRR 消掉，剩 [Y, G, B]；Y 在倒流区，往回漂
            sim.Insert(3, new List<Star> { St(999, R) });
            Seq(Colors(sim), new List<int> { Y, G, B }, N + "_colors");
            double before = sim.Stars.Count > 0 ? sim.Stars[0].S : 0;
            Settle(sim, 0.5);
            Ok(sim.Stars.Count > 0 && sim.Stars[0].S < before, N + "_reverse", "前段应该往回漂");
            Settle(sim, 3);
            Ok(sim.Touching(0), N + "_touch", "漂回来贴上后段");
        }

        // T18：按 id 抽走一颗：缺口照常合拢
        static void T18()
        {
            const string N = "T18removeById";
            var sim = Make(new[] { SS(G), SS(R), SS(R), SS(B), SS(R), SS(G) });
            sim.RemoveById(sim.Stars[3].Id);
            Settle(sim);
            Seq(Colors(sim), new List<int> { G, G }, N);
        }

        // T19：一步之内串被清空不崩溃
        static void T19()
        {
            const string N = "T19清空不崩溃";
            var sim = new ChainSim(new ChainCfg { TrackLength = 200, NoMoveRainbowSeconds = 999 });
            var list = new List<object> { SS(R), SS(R), SS(B), SS(R) };
            sim.Load(list, 50);
            sim.SpeedOverride = 1;
            sim.Pick(2);               // 摘 B → [R,R,R] 消 → 空串
            Settle(sim, 2);
            Ok(sim.Count == 0, N);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  game.test.js 的 6 个测试
        // ══════════════════════════════════════════════════════════════════════

        // G01：每关路线都不一样
        static void G01()
        {
            const string N = "G01每关路线唯一";
            var seen = new Dictionary<string, string>();
            bool ok = true; string msg = null;
            foreach (var L in Levels.LEVELS)
            {
                string key = TrackKey(L.Track);
                if (seen.ContainsKey(key))
                {
                    ok = false;
                    msg = $"{L.Id} 和 {seen[key]} 路线完全一样";
                    break;
                }
                seen[key] = L.Id;
            }
            Ok(ok, N, msg);
        }

        // G02：每关路线不穿模（自身、双串之间、星核、星盘、玩家）
        static void G02()
        {
            foreach (var L in Levels.LEVELS)
            {
                string N = "G02路线不穿模_" + L.Id;
                var tracks = Game.BuildTracks(L);

                // 岔路：分别检查两条支路；普通关：检查整组轨道
                List<List<ITrack>> sets;
                if (tracks[0] is ForkTrack ft)
                {
                    sets = new List<List<ITrack>>();
                    foreach (var p in ft.Paths)
                        sets.Add(new List<ITrack> { p });
                }
                else
                {
                    sets = new List<List<ITrack>> { tracks };
                }

                foreach (var set in sets)
                {
                    var probs = TrackCheck.Clearance(set);
                    Ok(probs.Count == 0, N, probs.Count > 0 ? probs[0] : null);
                }
            }
        }

        // G03：难度曲线：现成对子和夹心比例逐关递减
        static void G03()
        {
            const string N = "G03难度曲线";
            var ls    = new List<Level>(Levels.LEVELS).FindAll(l => !l.Tutorial);
            bool ok   = true; string msg = null;
            for (int i = 1; i < ls.Count; i++)
            {
                if (ls[i].Gen == null || ls[i - 1].Gen == null) continue;
                if (ls[i].Gen.PairRate > ls[i - 1].Gen.PairRate)
                {
                    ok = false;
                    msg = $"{ls[i].Id}.pairRate > {ls[i-1].Id}.pairRate";
                    break;
                }
                if (ls[i].Gen.Sandwich > ls[i - 1].Gen.Sandwich)
                {
                    ok = false;
                    msg = $"{ls[i].Id}.sandwich > {ls[i-1].Id}.sandwich";
                    break;
                }
            }
            Ok(ok, N, msg);
        }

        // G04：岔路：串头过岔口时整串走定信标指的那条；捏信标可以换（有冷却）
        static void G04()
        {
            const string N = "G04岔路";
            var g  = new Game(Levels.LevelById("3-4"));
            var tr = (ForkTrack)g.Tracks[0];

            Ok( g.FlipFork(), N + "_flip1");
            Ok(!g.FlipFork(), N + "_cooldown");

            int? taken = null;
            for (int i = 0; i < 30 * 200 && taken == null; i++)
            {
                g.Update(1.0 / 30, null);
                foreach (var e in g.DrainEvents())
                    if (e.Type == "forkTaken") { taken = e.Branch; break; }
            }

            Ok(taken != null, N + "_reached", "串头应该走到岔口");
            if (taken != null)
            {
                Ok(tr.Active == taken.Value, N + "_active");
                Ok(Math.Abs(g.Chains[0].Sim.Cfg.TrackLength - tr.Paths[taken.Value].Slots) < 1e-9,
                   N + "_trackLen", "轨道长度跟着支路走");
                Ok(tr.Paths[1].Slots > tr.Paths[0].Slots + 30, N + "_longPath", "长路明显更长");
            }
        }

        // G05：Boss：张嘴时喂同色星→掉血整串后退；喂错→吐回串尾；三次打死
        static void G05()
        {
            const string N = "G05Boss";
            var g  = new Game(Levels.LevelById("4-5"));
            var b  = g.Boss;
            var h  = g.Hands["right"];

            // 快进 8 秒，等串头进入轨道
            for (int i = 0; i < 30 * 8; i++) g.Update(1.0 / 30, null);
            g.DrainEvents(); // 清空初始事件

            var bs = g.Chains[0].Sim;
            Ok(bs.HeadOffset > 0, N + "_headOffset", "兽头占位");

            // 喂错一次
            int idx = FindPickable(bs);
            if (idx < 0) { Ok(false, N + "_cantFind", "找不到可摘星"); return; }
            b.State      = "open"; b.OpenT = 3;
            b.MouthColor = (bs.Stars[idx].Color + 1) % 5;    // 故意用错色
            g.PickAt(h, 0, idx, true);
            int bef = g.Returning.Count;
            g.DropTo(h, new DropTarget { Type = "mouth" });
            Ok(g.Returning.Count > bef, N + "_reject", "喂错吐回串尾");
            Ok(b.Hp == 3,              N + "_hp3");

            // 喂对三次：消灭 Boss
            for (int feed = 0; feed < 3; feed++)
            {
                idx = FindPickable(bs);
                if (idx < 0) { Ok(false, N + "_noStar" + feed, "找不到可摘星"); return; }
                b.State      = "open"; b.OpenT = 3;
                b.MouthColor = bs.Stars[idx].Color;
                g.PickAt(h, 0, idx, true);
                int bef2 = g.Returning.Count;
                g.DropTo(h, new DropTarget { Type = "mouth" });
                if (feed == 0)
                {
                    Ok(g.Returning.Count <= bef2, N + "_accept", "喂对不吐回串尾");
                    Ok(b.Hp == 2,                 N + "_hp2");
                    Ok(bs.Retreat > 0,            N + "_retreat", "整串后退");
                }
            }

            Ok(!b.Alive,          N + "_dead");
            Ok(bs.HeadOffset == 0, N + "_headOff0");
            Ok(g.DrainEvents().Exists(e => e.Type == "bossDefeated"), N + "_event");
        }

        // G06：星守恒：随手松开的星飞回串尾，总数不变
        static void G06()
        {
            const string N = "G06星守恒";
            var g = new Game(Levels.LevelById("1-1"));
            for (int i = 0; i < 30 * 6; i++) g.Update(1.0 / 30, null);

            var bs = g.Chains[0].Sim;
            int n0 = bs.Count + g.HeldStars().Count;

            // 找一颗中间星（两侧颜色不同，松开后不会触发串头消除）
            int idx = -1;
            for (int k = 1; k < bs.Count - 1; k++)
                if (bs.CanPick(k) && bs.Stars[k - 1].Color != bs.Stars[k + 1].Color)
                { idx = k; break; }

            if (idx < 0) { Ok(false, N, "找不到合适的星"); return; }

            g.PickAt(g.Hands["right"], 0, idx, true);
            Ok(bs.Count + g.HeldStars().Count == n0, N + "_afterPick");

            g.DropTo(g.Hands["right"], null);                   // 飞回串尾
            for (int k = 0; k < 30 * 2; k++) g.Update(1.0 / 30, null);
            Ok(bs.Count + g.HeldStars().Count == n0, N + "_afterReturn");
        }
    }
}
