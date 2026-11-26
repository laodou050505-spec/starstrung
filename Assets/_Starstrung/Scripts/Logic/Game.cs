// 一局游戏（纯逻辑）：对应 JS game.js。
// 编排星串 × 轨道、两只手、星盘、星守恒、共享星核、教学分段、输赢判断。

using System;
using System.Collections.Generic;

namespace Starstrung.Logic
{
    // ──────────────────── 辅助数据类型 ────────────────────

    public class ChainInfo
    {
        public int      Ci;
        public ITrack   Track;
        public ChainSim Sim;
    }

    public class StashSlot
    {
        public List<Star> Stars;
        public int        Ci;
    }

    public class FloatGroup
    {
        public List<Star> Stars;
        public int        Ci;
        public Vec3       Pos;
    }

    public class ReturnGroup
    {
        public List<Star> Stars;
        public int        Ci;
        public double     T;
        public Vec3       From;
    }

    public class HoldGroup
    {
        public List<Star> Stars;
        public int        Ci;
        public double     FlyT;
        public Vec3       From;   // 星被摘时的世界坐标（fly-in 动画用）
    }

    public class PullState
    {
        public int  Ci;
        public int  Id;
        public Vec3 Start;   // 捏合时指尖位置（拉到 5 cm 外才真正摘）
    }

    public class HoverResult
    {
        public string Type;  // "stash" / "float" / "fork" / "star" / "gap"（hint 用）
        public int    Ci;
        public int    Id;
        public int    Slot;
        public int    Idx;
        public int    Gap;
        public Vec3   Pos;
        public bool   Near;
    }

    public class DropTarget
    {
        public string Type;  // "stash" / "gap" / "mouth"
        public int    Ci;
        public int    Gap;
        public int    Slot;
        public Vec3   Pos;
        public bool   Near;
    }

    public class ForkState
    {
        public int    Next;
        public double Timer;
        public bool   Warn;
        public double Cooldown;
        public bool   Committed;
    }

    public class GameResult
    {
        public double Time;
        public int    MaxCombo;
        public int    Score;
        public int    Cracks;
        public int    Stars;
    }

    public class StarLocation
    {
        public Star        Star;
        public string      Where;    // "chain" / "hand" / "stash" / "float" / "return"
        public int         Ci;
        public Hand        HandRef;  // Where == "hand"
        public int         Slot;     // Where == "stash"
        public FloatGroup  F;        // Where == "float"
        public ReturnGroup R;        // Where == "return"
    }

    public class GapPos
    {
        public double U;
        public Vec3   Pos;
    }

    public class Hand
    {
        public string      Side;
        public bool        Tracked;
        public double      LostT;
        public bool        Pinching;
        public Vec3?       Point;
        public Ray3?       Ray;
        public HoldGroup   Holding;
        public PullState   Pull;
        public HoverResult Hover;
        public DropTarget  Drop;
        public bool        RaiseHint;
        public Picker      Picker    = new Picker();
        public Picker      GapPicker = new Picker();
    }

    // ──────────────────── Game ────────────────────

    public class Game
    {
        public Level              Level;
        public List<ITrack>       Tracks;
        public double             Time;
        public int                Cracks;
        public string             Status;    // "playing" / "won" / "lost"
        public List<GameEvent>    Events;
        public Dictionary<string, Hand> Hands;
        public StashSlot[]        Stash;
        public List<FloatGroup>   Floating;
        public List<ReturnGroup>  Returning;
        public Ray3?              Gaze;
        public int                Stage;
        public double             StageClearT;
        public List<ChainInfo>    Chains;
        public ForkState          Fork;
        /// <summary>无尽模式：第几波（从 1 起）、累计分数。每打退一只吞星兽就下一波，越来越快、越来越硬。</summary>
        public int                Wave = 1;
        public int                EndlessScore;
        public Boss               Boss;
        public GameResult         Result;

        // 孤色消散：Map 顺序用 List，查询用 Dictionary
        /// 某颗星孤色消散进行了多久（秒）；没在消散返回 -1。表现层用来画变暗闪烁
        public double FadeOf(int id) => _fadingMap.TryGetValue(id, out var t) ? t : -1;
        private List<int>              _fadingOrder = new List<int>();
        private Dictionary<int, double> _fadingMap  = new Dictionary<int, double>();
        private double                  _orphanTimer;

        // ──────────────────── 构造 ────────────────────

        public Game(Level level, bool dummy = false)
        {
            Level      = level;
            Tracks     = BuildTracks(level);
            Time       = 0;
            Cracks     = 0;
            Status     = "playing";
            Events     = new List<GameEvent>();
            Stash      = new StashSlot[2];
            Floating   = new List<FloatGroup>();
            Returning  = new List<ReturnGroup>();
            Gaze       = null;
            Stage      = 0;
            StageClearT= -1;

            Hands = new Dictionary<string, Hand>
            {
                ["left"]  = new Hand { Side = "left"  },
                ["right"] = new Hand { Side = "right" },
            };

            Chains = new List<ChainInfo>();
            for (int ci = 0; ci < Tracks.Count; ci++)
            {
                var tr = Tracks[ci];
                var sim = MakeSim(tr, ci);
                Chains.Add(new ChainInfo { Ci = ci, Track = tr, Sim = sim });
            }

            if (level.Tutorial)
            {
                LoadStage(0);
            }
            else
            {
                for (int ci = 0; ci < Chains.Count; ci++)
                {
                    int seed = (level.Seed != 0 ? level.Seed : 1) + ci * 7919;
                    var opts = new GenChainOpts
                    {
                        Colors   = level.Colors,
                        Length   = level.Length,
                        Specials = level.Specials ?? new SpecialsOpts(),
                    };
                    // JS：{ colors, length, specials, ...(level.gen || {}) } —— gen 里只有 pairRate / sandwich，
                    // 不能拿 gen 的默认（空）特殊星覆盖关卡自己的特殊星（11-23 比对轨迹时查出：曾导致所有关卡都没有特殊星）
                    if (level.Gen != null)
                    {
                        opts.PairRate = level.Gen.PairRate;
                        opts.Sandwich = level.Gen.Sandwich;
                    }
                    var list = Levels.ToLoadList(Levels.GenChain(seed, opts));
                    Chains[ci].Sim.Load(list);
                }
            }

            // 岔路：信标初始值
            Fork = Tracks[0] is ForkTrack
                ? new ForkState { Next = 0, Timer = 7, Warn = false, Cooldown = 0, Committed = false }
                : null;

            // Boss
            Boss = level.Boss != null
                ? new Boss(this, 0, new BossCfg { Hp = level.Boss.Hp })
                : null;
        }

        // ──────────────────── BuildTracks（静态，Bot/测试也用） ────────────────────

        public static List<ITrack> BuildTracks(Level level)
        {
            var t = level.Track ?? new TrackSpec();
            if (t.Type == "fork") return new List<ITrack> { new ForkTrack(t) };
            var basePts = Track.GenTrackPts(t);
            if (level.Chains == 2)
            {
                var pair = Track.HelixPair(basePts);
                return new List<ITrack> { new Track(pair[0]), new Track(pair[1]) };
            }
            return new List<ITrack> { new Track(basePts) };
        }

        // ──────────────────── MakeSim ────────────────────

        ChainSim MakeSim(ITrack tr, int ci)
        {
            var L  = Level;
            double slots = (tr is ForkTrack ft) ? ft.Paths[0].Slots : tr.Slots; // 岔路按短路算（最危险的情况）
            double introHeadS = Math.Min(slots * 0.3, (L.Length != 0 ? L.Length : 30) * 0.7);
            double baseSpeed  = L.Pace != 0
                ? (slots - introHeadS) / L.Pace
                : 0.5; // JS: L.speed ?? 0.5（关卡根上都没写 speed）

            var reverseZones = new List<double[]>();
            if (L.Reverse != null)
                foreach (var z in L.Reverse)
                    reverseZones.Add(new double[] { z[0] * tr.Slots, z[1] * tr.Slots });

            var sim = new ChainSim(new ChainCfg
            {
                TrackLength    = tr.Slots,
                BaseSpeed      = baseSpeed,
                SpeedRamp      = L.Ramp != 0 ? L.Ramp : 0.004,
                MaxSpeed       = L.MaxSpeed ?? 2.0,
                IntroHeadS     = introHeadS,
                CoreKnockback  = tr.SlotsBackFromEnd(GAME.KnockbackMeters),
                MaxCracks      = (int)1e9,  // 星核裂纹由 Game 统一管理
                ReverseZones   = reverseZones,
            });
            sim.NextId = 1 + ci * 100000;
            return sim;
        }

        // ──────────────────── 教学分段 ────────────────────

        void LoadStage(int k)
        {
            Stage      = k;
            StageClearT= -1;
            var st = Level.Stages[k];
            var c  = Chains[0];
            var tr = c.Track;

            if (st.Place == "intro")
            {
                var list = Levels.ToLoadList(Levels.GenChain(
                    st.Seed != 0 ? st.Seed : 1,
                    new GenChainOpts
                    {
                        Colors = Level.Colors,
                        Length = st.Length != 0 ? st.Length : 20,
                    }
                ));
                c.Sim.Load(list);
                c.Sim.SpeedOverride = null;
                c.Sim.Cfg.BaseSpeed = st.Speed;
                c.Sim.Cfg.IntroHeadS = tr.Slots * 0.25;
            }
            else
            {
                double headM = st.Place == "near"
                    ? tr.Length - 0.55
                    : tr.Length * 0.12 + 0.6;
                var list = new List<object>();
                foreach (int color in st.Chain) list.Add(new StarSource { Color = color });
                c.Sim.Load(list, tr.MToU(headM));
                c.Sim.SpeedOverride = st.Speed;
            }
            Emit(new GameEvent { Type = "stage", Stage = k, Teach = st.Teach });
        }

        /// 教学提示：当前应摘哪颗 / 应放哪缝（视图层画光圈）
        public HoverResult Hint
        {
            get
            {
                if (!Level.Tutorial || Status != "playing") return null;
                var st = Level.Stages[Stage];
                if (st.Teach == "play") return null;
                var sim = Chains[0].Sim;
                var held = HeldStars();
                if (held.Count > 0)
                {
                    var m = Moves.InsertMatches(sim, held[0].Color);
                    return m.Count > 0
                        ? new HoverResult { Type = "gap", Ci = 0, Gap = m[0].gap }
                        : null;
                }
                var p = Moves.PickMatches(sim);
                if (p.Count > 0) return new HoverResult { Type = "star", Ci = 0, Id = sim.Stars[p[0].i].Id };
                for (int color = 0; color < 5; color++)
                {
                    var lone = Moves.LoneStars(sim, color);
                    var filtered = lone.FindAll(i =>
                        Moves.InsertMatches(sim, color).Exists(g => g.gap != i && g.gap != i + 1));
                    if (filtered.Count > 0)
                        return new HoverResult { Type = "star", Ci = 0, Id = sim.Stars[filtered[0]].Id };
                }
                return null;
            }
        }

        // ──────────────────── 查询 ────────────────────

        public Vec3 StarPos(int ci, Star star)  => Tracks[ci].PointAtU(star.S);
        public double StarSize(int ci, Star star) => Tracks[ci].SizeAtU(Math.Max(0, star.S));

        /// 所有离开星串的星（手、星盘、悬浮、飞回中）
        public List<Star> HeldStars()
        {
            var out_ = new List<Star>();
            foreach (var h in Hands.Values) if (h.Holding != null) out_.AddRange(h.Holding.Stars);
            foreach (var s in Stash) if (s != null) out_.AddRange(s.Stars);
            foreach (var f in Floating) out_.AddRange(f.Stars);
            foreach (var r in Returning) out_.AddRange(r.Stars);
            return out_;
        }

        /// 缝隙中点。gap=0 是串头前，gap=n 是串尾后。找不到返回 null。
        public GapPos GapPosAt(int ci, int gap)
        {
            var c   = Chains[ci];
            var sim = c.Sim;
            var tr  = c.Track;
            var st  = sim.Stars;
            int n   = st.Count;
            if (n == 0) return null;
            if (gap > 0 && gap < n)
            {
                var a = st[gap - 1]; var b = st[gap];
                if (b.S < 0) return null;
                double u = sim.Touching(gap - 1) ? (a.S + b.S) / 2 : b.S + 0.5;
                return new GapPos { U = u, Pos = tr.PointAtU(u) };
            }
            if (gap == 0)
            {
                double u = st[0].S + 0.6;
                return st[0].S < 0 || u > tr.Slots ? null : new GapPos { U = u, Pos = tr.PointAtU(u) };
            }
            {
                double u = st[n - 1].S - 0.6;
                return u < 0 ? null : new GapPos { U = u, Pos = tr.PointAtU(u) };
            }
        }

        // ──────────────────── 主循环 ────────────────────

        public void Update(double dt, FrameInput input = null)
        {
            if (Status != "playing") { Time += dt; return; }
            Time += dt;
            if (Fork != null) UpdateFork(dt);
            if (Boss  != null) Boss.Update(dt);

            var heldColors = new List<int>();
            foreach (var s in HeldStars()) heldColors.Add(s.Color);

            foreach (var c in Chains)
            {
                var other = new List<int>();
                foreach (var o in Chains)
                    if (o != c)
                        for (int idx = 0; idx < o.Sim.Stars.Count; idx++)
                            if (o.Sim.CanPick(idx)) other.Add(o.Sim.Stars[idx].Color);

                if (other.Count > 0)
                {
                    var combined = new List<int>(heldColors);
                    combined.AddRange(other);
                    c.Sim.HeldColors = combined;
                }
                else
                {
                    c.Sim.HeldColors = new List<int>(heldColors);
                }

                c.Sim.Update(dt);
                foreach (var e in c.Sim.DrainEvents()) OnSimEvent(c.Ci, e);
                if (Status != "playing") return;
            }

            if (input != null)
            {
                Gaze = input.Gaze;
                foreach (var side in new[] { "left", "right" })
                {
                    var inp = input.Hands?.Find(h => h.Side == side);
                    UpdateHand(Hands[side], inp, dt);
                }
            }
            else
            {
                foreach (var h in Hands.Values)
                    if (h.Holding != null && h.Holding.FlyT > 0)
                        h.Holding.FlyT -= dt;
            }

            // 飞回串尾
            for (int i = Returning.Count - 1; i >= 0; i--)
            {
                var r = Returning[i];
                r.T += dt;
                if (r.T >= PICK.ReturnTime)
                {
                    Returning.RemoveAt(i);
                    var sim = Chains[r.Ci].Sim;
                    foreach (var s in r.Stars) sim.AppendTail(s);
                    foreach (var e in sim.DrainEvents()) OnSimEvent(r.Ci, e);
                }
            }

            UpdateOrphans(dt);
            CheckClear(dt);
        }

        // ──────────────────── 岔路 ────────────────────

        void UpdateFork(double dt)
        {
            var F  = Fork;
            var ft = (ForkTrack)Tracks[0];
            var c  = Chains[0];
            var sim= c.Sim;

            F.Cooldown = Math.Max(0, F.Cooldown - dt);
            F.Timer   -= dt;

            if (!F.Warn && F.Timer <= 1.5)
            {
                F.Warn = true;
                Emit(new GameEvent { Type = "forkWarn", Next = 1 - F.Next });
            }
            if (F.Timer <= 0)
            {
                F.Next  = 1 - F.Next;
                F.Timer = 6 + ((Time * 7.31) % 3);
                F.Warn  = false;
                Emit(new GameEvent { Type = "forkSwitch", Next = F.Next, Auto = true });
            }

            var head = sim.Stars.Count > 0 ? sim.Stars[0] : null;
            if (!F.Committed && head != null && head.S >= ft.ForkU)
            {
                F.Committed = true;
                ft.Active   = F.Next;
                Emit(new GameEvent { Type = "forkTaken", Branch = F.Next });
            }
            if (F.Committed && (head == null || head.S < ft.ForkU - 1))
                F.Committed = false;
            if (!F.Committed) ft.Active = F.Next;
            sim.Cfg.TrackLength = ft.Slots;
        }

        /// 玩家捏信标：手动换路（有冷却）
        public bool FlipFork()
        {
            if (Fork == null || Fork.Cooldown > 0) return false;
            Fork.Next     = 1 - Fork.Next;
            Fork.Cooldown = 2;
            Fork.Timer    = Math.Max(Fork.Timer, 5);
            Fork.Warn     = false;
            Emit(new GameEvent { Type = "forkSwitch", Next = Fork.Next, Auto = false });
            return true;
        }

        // ──────────────────── allStars ────────────────────

        public List<StarLocation> AllStars()
        {
            var out_ = new List<StarLocation>();
            foreach (var c in Chains)
                foreach (var s in c.Sim.Stars)
                    out_.Add(new StarLocation { Star = s, Where = "chain", Ci = c.Ci });

            foreach (var h in Hands.Values)
                if (h.Holding != null)
                    foreach (var s in h.Holding.Stars)
                        out_.Add(new StarLocation { Star = s, Where = "hand", HandRef = h });

            for (int k = 0; k < 2; k++)
                if (Stash[k] != null)
                    foreach (var s in Stash[k].Stars)
                        out_.Add(new StarLocation { Star = s, Where = "stash", Slot = k });

            foreach (var f in Floating)
                foreach (var s in f.Stars)
                    out_.Add(new StarLocation { Star = s, Where = "float", F = f });

            foreach (var r in Returning)
                foreach (var s in r.Stars)
                    out_.Add(new StarLocation { Star = s, Where = "return", R = r });

            return out_;
        }

        // ──────────────────── 孤色消散 ────────────────────

        void UpdateOrphans(double dt)
        {
            const double FADE_TIME = 1.5;
            _orphanTimer -= dt;
            if (_orphanTimer <= 0)
            {
                _orphanTimer = 0.25;
                var all = AllStars();
                var count = new Dictionary<int, int>();
                int rainbow = 0;
                foreach (var x in all)
                {
                    if (x.Star.Color < 0) rainbow++;
                    else
                    {
                        if (!count.ContainsKey(x.Star.Color)) count[x.Star.Color] = 0;
                        count[x.Star.Color]++;
                    }
                }
                bool Doomed(Star star)
                {
                    if (star.Color < 0) return count.Count == 0 && rainbow < 3;
                    return (count.TryGetValue(star.Color, out int cnt) ? cnt : 0) + rainbow < 3;
                }
                foreach (var x in all)
                {
                    if (!Doomed(x.Star) || _fadingMap.ContainsKey(x.Star.Id)) continue;
                    _fadingMap[x.Star.Id] = 0;
                    _fadingOrder.Add(x.Star.Id);
                    Emit(new GameEvent { Type = "fadeWarn", Id = x.Star.Id, Color = x.Star.Color });
                }
            }

            if (_fadingMap.Count == 0) return;
            var all2 = AllStars();
            var snap = new List<int>(_fadingOrder);
            foreach (int id in snap)
            {
                if (!_fadingMap.TryGetValue(id, out double t)) continue;
                double nt = t + dt;
                var hit = all2.Find(x => x.Star.Id == id);
                if (hit == null)
                {
                    _fadingMap.Remove(id); _fadingOrder.Remove(id);
                    continue;
                }
                if (nt < FADE_TIME) { _fadingMap[id] = nt; continue; }
                _fadingMap.Remove(id); _fadingOrder.Remove(id);
                RemoveStar(hit);
            }
        }

        void RemoveStar(StarLocation x)
        {
            int id = x.Star.Id;
            if (x.Where == "chain")
            {
                var sim = Chains[x.Ci].Sim;
                sim.RemoveById(id);
                foreach (var e in sim.DrainEvents()) OnSimEvent(x.Ci, e);
                return;
            }
            Vec3? pos = null;
            if (x.Where == "hand" && x.HandRef?.Holding != null)
            {
                pos = x.HandRef.Point;
                x.HandRef.Holding.Stars.RemoveAll(s => s.Id == id);
                if (x.HandRef.Holding.Stars.Count == 0) x.HandRef.Holding = null;
            }
            if (x.Where == "stash" && Stash[x.Slot] != null)
            {
                pos = LAYOUT.Stash[x.Slot];
                Stash[x.Slot].Stars.RemoveAll(s => s.Id == id);
                if (Stash[x.Slot].Stars.Count == 0) Stash[x.Slot] = null;
            }
            if (x.Where == "float" && x.F != null)
            {
                pos = x.F.Pos;
                x.F.Stars.RemoveAll(s => s.Id == id);
                Floating.RemoveAll(f => f.Stars.Count == 0);
            }
            if (x.Where == "return" && x.R != null)
            {
                x.R.Stars.RemoveAll(s => s.Id == id);
                Returning.RemoveAll(r => r.Stars.Count == 0);
            }
            Emit(new GameEvent { Type = "faded", Id = id, Color = x.Star.Color, Pos = pos });
        }

        void OnSimEvent(int ci, GameEvent e)
        {
            e.Ci = ci;
            if (e.Type == "coreHit")
            {
                Cracks++;
                Emit(new GameEvent { Type = "coreHit", Ci = ci, Cracks = Cracks });
                if (Cracks >= GAME.MaxCracks) { Status = "lost"; Emit(new GameEvent { Type = "lost" }); }
                return;
            }
            Emit(e);
        }

        /// <summary>无尽模式的下一波：串更长、速度更快，吞星兽血量随波次 / 分数上涨（每 2 波 +1，上限 6）。</summary>
        void NextWave()
        {
            double scoreK = 1 + Math.Min(0.5, EndlessScore / 60000.0);          // 分数越高越快（最多再快五成）
            double waveK = 1 + (Wave - 1) * 0.07;                                // 每波再快 7%
            foreach (var c in Chains)
            {
                var list = Levels.ToLoadList(Levels.GenEndlessChain(Level, Wave));
                c.Sim.Load(list);            // 和开局一样从入口冲进来
                c.Sim.SpeedOverride = null;
                c.Sim.SpeedScale = waveK * scoreK;
            }
            int hp = Math.Min(6, 2 + Wave / 2);
            Boss = new Boss(this, 0, new BossCfg { Hp = hp, ChewEvery = Math.Max(4.5, 8 - Wave * 0.4), OpenEvery = Math.Max(6.5, 10 - Wave * 0.3) });
            Emit(new GameEvent { Type = "wave", Stage = Wave });
        }

        void CheckClear(double dt)
        {
            bool empty = Chains.TrueForAll(c => c.Sim.Count == 0) && HeldStars().Count == 0;
            if (!empty) { StageClearT = -1; return; }

            if (Level.Tutorial && Stage < Level.Stages.Length - 1)
            {
                if (StageClearT < 0) { StageClearT = 0; Emit(new GameEvent { Type = "stageClear", Stage = Stage }); }
                StageClearT += dt;
                if (StageClearT >= 1.2) LoadStage(Stage + 1);
                return;
            }

            // 无尽模式（11-24 用户）：串清空 = 打退一波，不结束。下一波更快、兽更硬，直到星核裂满
            if (Level.Endless)
            {
                if (StageClearT < 0) { StageClearT = 0; Emit(new GameEvent { Type = "stageClear", Stage = Wave }); }
                StageClearT += dt;
                if (StageClearT < 1.4) return;
                foreach (var c in Chains) EndlessScore += c.Sim.Score;
                Wave++;
                StageClearT = -1;
                NextWave();
                return;
            }

            Status = "won";
            int maxCombo = 0;
            foreach (var c in Chains) maxCombo = Math.Max(maxCombo, c.Sim.MaxCombo);
            int totalScore = 0;
            foreach (var c in Chains) totalScore += c.Sim.Score;
            // 三星（11-24 用户：现在太容易满星）：过关 1 星；用时压得很紧 +1；连锁 ≥4 且没被撞 +1
            double par   = (Level.Length != 0 ? Level.Length : 20) * (Level.Chains != 0 ? Level.Chains : 1) * 1.85;
            int stars    = 1 + (Time <= par ? 1 : 0) + (maxCombo >= 4 && Cracks == 0 ? 1 : 0);
            Result = new GameResult { Time = Time, MaxCombo = maxCombo, Score = totalScore, Cracks = Cracks, Stars = stars };
            Emit(new GameEvent
            {
                Type        = "won",
                GameTime    = Time,
                MaxCombo    = maxCombo,
                TotalScore  = totalScore,
                TotalCracks = Cracks,
                Stars3      = stars,
            });
        }

        // ──────────────────── 手的状态机 ────────────────────

        static double Dist(Vec3 a, Vec3 b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public void UpdateHand(Hand h, HandInput inp, double dt)
        {
            if (inp == null || !inp.Tracked)
            {
                h.LostT  += dt;
                h.Tracked = false;
                if (h.LostT > HAND.FreezeAfter)
                {
                    if (h.Holding != null)
                    {
                        Vec3 p = h.Point ?? LAYOUT.Core;
                        Floating.Add(new FloatGroup { Stars = h.Holding.Stars, Ci = h.Holding.Ci, Pos = p });
                        Emit(new GameEvent { Type = "floated", Side = h.Side });
                        h.Holding = null;
                    }
                    h.Pull = null; h.Pinching = false; h.Hover = null; h.Drop = null;
                }
                h.RaiseHint = h.LostT > HAND.RaiseHint;
                return;
            }

            h.LostT    = 0;
            h.RaiseHint= false;
            h.Tracked  = true;
            h.Point    = inp.Point;   // Vec3 is a struct, copies automatically
            h.Ray      = inp.Ray;
            bool was   = h.Pinching;
            h.Pinching = inp.Pinching;
            bool start = h.Pinching && !was;
            bool end   = !h.Pinching && was;

            if (h.Holding != null)
            {
                if (h.Holding.FlyT > 0) h.Holding.FlyT -= dt;
                h.Drop = FindDrop(h);
                if (end) Release(h);
                return;
            }
            if (h.Pull != null)
            {
                var sim = Chains[h.Pull.Ci].Sim;
                int i   = sim.IndexOf(h.Pull.Id);
                if (end || i < 0 || !sim.CanPick(i))
                {
                    h.Pull = null;
                    Emit(new GameEvent { Type = "pullCancel", Side = h.Side });
                    return;
                }
                if (Dist(h.Point.Value, h.Pull.Start) >= PICK.PullOut)
                    PickAt(h, h.Pull.Ci, i, false);
                return;
            }
            h.Hover = FindHover(h);
            if (start) Grab(h);
        }

        HoverResult FindHover(Hand h)
        {
            if (!h.Point.HasValue) return null;
            var p = h.Point.Value;

            // 星盘 / 悬浮星
            for (int k = 0; k < 2; k++)
                if (Stash[k] != null && Dist(p, LAYOUT.Stash[k]) <= PICK.StashNear)
                    return new HoverResult { Type = "stash", Slot = k, Pos = LAYOUT.Stash[k] };
            for (int k = 0; k < Floating.Count; k++)
                if (Dist(p, Floating[k].Pos) <= PICK.StashNear)
                    return new HoverResult { Type = "float", Idx = k, Pos = Floating[k].Pos };

            // 岔路信标
            if (Fork != null)
            {
                var fp   = ((ForkTrack)Tracks[0]).ForkPos;
                bool nearB = Dist(p, fp) <= 0.12;
                bool rayB  = false;
                if (h.Ray.HasValue)
                {
                    var ray = h.Ray.Value;
                    double vx = fp.X - ray.O.X, vy = fp.Y - ray.O.Y, vz = fp.Z - ray.O.Z;
                    double l  = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    if (l > 0) rayB = (vx * ray.D.X + vy * ray.D.Y + vz * ray.D.Z) / l
                                      > Math.Cos(4 * Math.PI / 180);
                }
                if (nearB || rayB) return new HoverResult { Type = "fork", Pos = fp, Near = nearB };
            }

            // 近星 + 远摘候选
            HoverResult near = null;
            double nd = PICK.NearRadius;
            var cands = new List<PickCandidate>();
            var other = OtherHand(h);

            foreach (var c in Chains)
            {
                var st = c.Sim.Stars;
                for (int i = 0; i < st.Count; i++)
                {
                    if (!c.Sim.CanPick(i)) continue;
                    if (other.Pull != null && other.Pull.Id == st[i].Id) continue;
                    var pos = c.Track.PointAtU(st[i].S);
                    double d = Dist(p, pos);
                    if (d <= nd)
                    {
                        nd   = d;
                        near = new HoverResult { Type = "star", Ci = c.Ci, Id = st[i].Id, Pos = pos, Near = true };
                    }
                    cands.Add(new PickCandidate
                    {
                        Key = $"{c.Ci}:{st[i].Id}",
                        Pos = pos,
                        R   = c.Track.SizeAtU(st[i].S) / 2,
                    });
                }
            }

            var far = h.Picker.Update(Time, h.Ray, cands, Gaze);
            if (near != null) return near;
            if (far != null)
            {
                var parts = far.Key.Split(':');
                int fci = int.Parse(parts[0]), fid = int.Parse(parts[1]);
                return new HoverResult { Type = "star", Ci = fci, Id = fid, Pos = far.Pos, Near = false };
            }
            return null;
        }

        Hand OtherHand(Hand h) => Hands[h.Side == "left" ? "right" : "left"];

        void Grab(Hand h)
        {
            var hv = h.Hover;
            if (hv != null && hv.Type == "stash" && Stash[hv.Slot] != null)
            {
                h.Holding = new HoldGroup { Stars = Stash[hv.Slot].Stars, Ci = Stash[hv.Slot].Ci, FlyT = 0 };
                Stash[hv.Slot] = null;
                Emit(new GameEvent { Type = "unstash", Side = h.Side, Slot = hv.Slot });
                return;
            }
            if (hv != null && hv.Type == "float")
            {
                var f = Floating[hv.Idx];
                Floating.RemoveAt(hv.Idx);
                h.Holding = new HoldGroup { Stars = f.Stars, Ci = f.Ci, FlyT = 0 };
                Emit(new GameEvent { Type = "regrab", Side = h.Side });
                return;
            }
            if (hv != null && hv.Type == "fork") { FlipFork(); return; }
            if (hv != null && hv.Type == "star" && hv.Near)
            {
                h.Pull = new PullState { Ci = hv.Ci, Id = hv.Id, Start = h.Point.Value };
                Emit(new GameEvent { Type = "pullStart", Side = h.Side, Ci = hv.Ci, Id = hv.Id });
                return;
            }
            // 远摘
            string key = h.Picker.LockedKey(Time);
            if (key == null) { Emit(new GameEvent { Type = "pinchEmpty", Side = h.Side }); return; }
            var kparts = key.Split(':');
            int ci = int.Parse(kparts[0]), sid = int.Parse(kparts[1]);
            var csim = ci < Chains.Count ? Chains[ci].Sim : null;
            int idx  = csim != null ? csim.IndexOf(sid) : -1;
            if (idx < 0) return;
            PickAt(h, ci, idx, true);
        }

        /// 摘下 ci 串第 i 颗放进手里（机器人也走这里）
        public bool PickAt(Hand h, int ci, int i, bool far)
        {
            var c    = Chains[ci];
            var from = c.Track.PointAtU(c.Sim.Stars[i].S);
            var got  = c.Sim.Pick(i);
            h.Pull = null;
            if (got == null) return false;
            foreach (var e in c.Sim.DrainEvents()) OnSimEvent(ci, e);
            h.Holding = new HoldGroup { Stars = got, Ci = ci, FlyT = far ? PICK.FlyIn : 0, From = from };
            h.Hover   = null;
            h.Picker.Reset();
            Emit(new GameEvent
            {
                Type  = "grabbed",
                Side  = h.Side,
                Ci    = ci,
                Far   = far,
                Color = got[0].Color,
                Ids   = got.ConvertAll(s => s.Id),
            });
            return true;
        }

        HoverResult FindHoverDrop(Hand h)
        {
            // 仅供内部使用：复用 HoverResult 类型临时表示 DropTarget 形状
            return null;
        }

        DropTarget FindDrop(Hand h)
        {
            if (!h.Point.HasValue) return null;
            var p = h.Point.Value;

            // 星盘
            for (int k = 0; k < 2; k++)
                if (Stash[k] == null && Dist(p, LAYOUT.Stash[k]) <= PICK.StashNear)
                    return new DropTarget { Type = "stash", Slot = k, Pos = LAYOUT.Stash[k] };

            // 近缝 + 远放候选
            DropTarget best = null;
            double bd = PICK.GapNearRadius;
            var cands = new List<PickCandidate>();

            foreach (var c in Chains)
            {
                for (int g = 0; g <= c.Sim.Count; g++)
                {
                    var gp = GapPosAt(c.Ci, g);
                    if (gp == null) continue;
                    double d = Dist(p, gp.Pos);
                    if (d <= bd)
                    {
                        bd   = d;
                        best = new DropTarget { Type = "gap", Ci = c.Ci, Gap = g, Pos = gp.Pos, Near = true };
                    }
                    cands.Add(new PickCandidate
                    {
                        Key = $"{c.Ci}:{g}",
                        Pos = gp.Pos,
                        R   = c.Track.SizeAtU(gp.U) / 2,
                    });
                }
            }

            // Boss 张嘴
            if (Boss != null && Boss.MouthOpen)
            {
                var bc  = Chains[Boss.Ci];
                var mp  = bc.Track.PointAtU(Math.Min(bc.Track.Slots, Boss.U));
                if (Dist(p, mp) <= 0.12)
                    return new DropTarget { Type = "mouth", Pos = mp, Near = true };
                cands.Add(new PickCandidate { Key = "mouth", Pos = mp, R = bc.Track.SizeAtU(Boss.U) * 0.8 });
            }

            var far = h.GapPicker.Update(Time, h.Ray, cands, Gaze);
            if (best != null) return best;
            if (far != null && far.Key == "mouth")
                return new DropTarget { Type = "mouth", Pos = far.Pos, Near = false };
            if (far != null)
            {
                var parts = far.Key.Split(':');
                return new DropTarget { Type = "gap", Ci = int.Parse(parts[0]), Gap = int.Parse(parts[1]), Pos = far.Pos, Near = false };
            }
            return null;
        }

        void Release(Hand h)
        {
            var d = h.Drop;
            if (d != null && d.Type == "gap" && !d.Near)
            {
                string key = h.GapPicker.LockedKey(Time);
                if (key == "mouth" && Boss != null && Boss.MouthOpen)
                    d = new DropTarget { Type = "mouth" };
                else if (key != null && key != "mouth")
                {
                    var parts = key.Split(':');
                    d = new DropTarget { Type = "gap", Pos = d.Pos, Near = d.Near, Ci = int.Parse(parts[0]), Gap = int.Parse(parts[1]) };
                }
            }
            DropTo(h, d);
        }

        /// 把手里的星放到 d（null = 飞回串尾）。机器人也走这里。
        public void DropTo(Hand h, DropTarget d)
        {
            var hold = h.Holding;
            if (hold == null) return;
            h.Holding = null; h.Drop = null;
            h.GapPicker.Reset();

            if (d != null && d.Type == "mouth" && Boss != null)
            {
                if (Boss.Feed(hold.Stars)) return;
                // 喂错：落到下面的"飞回串尾"
            }
            else if (d != null && d.Type == "stash" && Stash[d.Slot] == null)
            {
                Stash[d.Slot] = new StashSlot { Stars = hold.Stars, Ci = hold.Ci };
                Emit(new GameEvent { Type = "stashed", Side = h.Side, Slot = d.Slot });
                return;
            }

            if (d != null && d.Type == "gap" && d.Ci < Chains.Count && d.Gap <= Chains[d.Ci].Sim.Count)
            {
                var c = Chains[d.Ci];
                c.Sim.Insert(d.Gap, hold.Stars);
                Emit(new GameEvent { Type = "placed", Side = h.Side, Ci = d.Ci, Gap = d.Gap, Color = hold.Stars[0].Color });
                foreach (var e in c.Sim.DrainEvents()) OnSimEvent(d.Ci, e);
                return;
            }

            // 星守恒：飞回原串尾
            var from = h.Point ?? LAYOUT.Core;
            Returning.Add(new ReturnGroup { Stars = hold.Stars, Ci = hold.Ci, T = 0, From = from });
            Emit(new GameEvent { Type = "returning", Side = h.Side, Ci = hold.Ci });
        }

        // ──────────────────── 事件 ────────────────────

        public void Emit(GameEvent e) { e.T = Time; Events.Add(e); }
        public List<GameEvent> DrainEvents() { var e = Events; Events = new List<GameEvent>(); return e; }
    }
}
