// 星串核心纯逻辑：推进、摘、放、合拢、消除、连锁、撞星核、无解保护。
// 对应 JS chain.js，逐位一致。
// 单位：星位（相邻两颗星中心距 = 1）。stars[0] 最靠近星核（s 最大）。

using System;
using System.Collections.Generic;

namespace Starstrung.Logic
{
    // ──────────────────── 枚举 & 常量 ────────────────────

    /// 对应 JS Kind 对象，值名与 JS 字符串的首字母大写一致
    public enum StarKind { Normal, Ice, Twin, Rainbow, Bomb, TimeStop }

    /// 对应 JS DEFAULT_CHAIN
    public class ChainCfg
    {
        public double TrackLength      = 120;
        public double BaseSpeed        = 0.55;
        public double SpeedRamp        = 0.004;
        public double MaxSpeed         = 1.6;
        public double IntroHeadS       = 30;
        public double IntroSpeed       = 10;
        public double AttractTime      = 0.25;
        public double AttractMinSpeed  = 2.5;
        public int    MinMatch         = 3;
        public double CoreKnockback    = 18;
        public double KnockbackSpeed   = 30;
        public int    MaxCracks        = 3;
        public double ComboRetreat     = 2.5;
        public double RetreatSpeed     = 12;
        public double TimeStopSeconds  = 3;
        public double NoMoveRainbowSeconds = 6;
        public int    ScorePerStar     = 10;
        public List<double[]> ReverseZones = new List<double[]>(); // [[u0,u1],...]
        public double ReverseSpeed     = 1.2;
        public double MaxStep          = 1.0 / 120.0;
    }

    // ──────────────────── 星 ────────────────────

    /// 单颗星，对应 JS {id,color,kind,twin,s}。
    /// 注意：JS 里 {...x} 是浅拷贝，C# 里对应 Clone()。
    public class Star
    {
        public int     Id;
        public int     Color;   // 颜色索引；-1 = 彩星未确定颜色
        public StarKind Kind;
        public int     Twin;    // 双子搭档的 id；-1 = 无
        public double  S;       // 星位（串头方向为正）

        public Star Clone() => new Star { Id = Id, Color = Color, Kind = Kind, Twin = Twin, S = S };
    }

    // ──────────────────── 事件 ────────────────────

    /// 对应 JS 里各种 {type:'...', ...} 事件对象。
    /// 字段名注释标注了对应的 JS 字段名。
    public class GameEvent
    {
        public string   Type;          // JS: type
        public double   T;             // JS: t（Game.emit 加上）
        public int      Ci;            // JS: ci（Game.onSimEvent 加上）
        public int      Combo;         // JS: combo（eliminated）
        public int      Color;         // JS: color
        public double   S;             // JS: s（星位）
        public int      Score;         // JS: score
        public bool     Bomb;          // JS: bomb
        public bool     TimeStop;      // JS: timeStop
        public List<int>  Ids;         // JS: ids
        public List<Star> Stars;       // JS: stars（快照副本）
        public int      Matched;       // JS: matched
        public int      Cracks;        // JS: cracks
        public int      Stage;         // JS: stage
        public string   Teach;         // JS: teach
        public double   GameTime;      // JS: time（won）
        public int      MaxCombo;      // JS: maxCombo（won）
        public int      TotalScore;    // JS: score（won）
        public int      TotalCracks;   // JS: cracks（won）
        public int      Stars3;        // JS: stars（1-3 星评级，won）
        public int      Next;          // JS: next（forkWarn/forkSwitch）
        public bool     Auto;          // JS: auto（forkSwitch）
        public int      Branch;        // JS: branch（forkTaken）
        public int      Hp;            // JS: hp（bossHurt）
        public double   U;             // JS: u（bossDefeated）
        public int      Id;            // JS: id（fadeWarn）
        public string   Side;          // JS: side（grabbed/stashed 等）
        public int      Slot;          // JS: slot（stash）
        public int      Gap;           // JS: gap（placed）
        public bool     Far;           // JS: far（grabbed）
        public Vec3?    Pos;           // JS: pos（faded）
    }

    // ──────────────────── ChainSim ────────────────────

    public class ChainSim
    {
        private const double EPS = 1e-4;

        public ChainCfg      Cfg;
        public List<Star>    Stars    = new List<Star>();
        public List<GameEvent> Events  = new List<GameEvent>();
        public int           Cracks   = 0;
        public int           Score    = 0;
        public int           MaxCombo = 0;
        public double        Time     = 0;
        public bool          Lost     = false;
        public bool          Paused   = false;
        public double        SpeedScale    = 1;
        public double?       SpeedOverride = null; // 非 null 时固定速度（教学用）
        public double        HeadOffset    = 0;    // Boss 兽头占多少星位
        public double        TimeStop      = 0;
        public List<int>     HeldColors    = new List<int>();
        // attract: key=后段段首星 id, value=连锁等级；Dictionary 插入顺序不影响结果（见 cleanAttract 分析）
        public Dictionary<int,int> Attract = new Dictionary<int,int>();
        public double        Retreat  = 0;
        public double        Knockback= 0;
        private double       noMoveTimer    = 0;
        private double       moveCheckTimer = 0;
        public int           NextId   = 1;

        public ChainSim(ChainCfg cfg = null)
        {
            Cfg = cfg ?? new ChainCfg();
        }

        // ───────────── 搭建 ─────────────

        /// 放入初始星串（串头→串尾）。headS 缺省时排在轨道入口之前，开局冲刺入场。
        public void Load(List<object> list, double headS = -1)
        {
            Stars.Clear();
            Attract.Clear();
            var idMap = new Dictionary<object, int>();

            for (int i = 0; i < list.Count; i++)
            {
                var src = list[i] as StarSource;
                if (src == null) throw new ArgumentException("list 元素必须是 StarSource");
                var s = new Star
                {
                    Id    = NextId++,
                    Color = src.Color,
                    Kind  = src.Kind,
                    Twin  = -1,
                    S     = headS - i,
                };
                // 保留原始 id 或用下标
                idMap[src.OrigId ?? (object)i] = s.Id;
                Stars.Add(s);
            }
            for (int i = 0; i < list.Count; i++)
            {
                var src = list[i] as StarSource;
                if (src != null && src.TwinOrigId != null)
                {
                    int mapped;
                    Stars[i].Twin = idMap.TryGetValue(src.TwinOrigId, out mapped) ? mapped : -1;
                }
            }
        }

        public Star NewStar(int color, StarKind kind = StarKind.Normal)
        {
            return new Star { Id = NextId++, Color = color, Kind = kind, Twin = -1, S = 0 };
        }

        public int Count => Stars.Count;

        public double CurrentSpeed
        {
            get
            {
                if (Stars.Count == 0) return 0;
                if (SpeedOverride.HasValue) return SpeedOverride.Value;
                if (Stars[0].S < Cfg.IntroHeadS) return Cfg.IntroSpeed;
                double v = Math.Min(Cfg.MaxSpeed, Cfg.BaseSpeed * (1 + Cfg.SpeedRamp * Time));
                return v * SpeedScale;
            }
        }

        // ───────────── 查询 ─────────────

        public int IndexOf(int id)
        {
            for (int i = 0; i < Stars.Count; i++) if (Stars[i].Id == id) return i;
            return -1;
        }

        /// 第 i 颗和第 i+1 颗是否贴在一起
        public bool Touching(int i)
        {
            if (i < 0 || i + 1 >= Stars.Count) return false;
            return Stars[i].S - Stars[i + 1].S <= 1 + EPS;
        }

        public int TwinPartnerIndex(int i)
        {
            if (i < 0 || i >= Stars.Count) return -1;
            var s = Stars[i];
            if (s.Twin < 0) return -1;
            if (i > 0 && Stars[i - 1].Id == s.Twin) return i - 1;
            if (i + 1 < Stars.Count && Stars[i + 1].Id == s.Twin) return i + 1;
            return -1;
        }

        public bool CanPick(int i)
        {
            if (i < 0 || i >= Stars.Count || Lost) return false;
            var s = Stars[i];
            if (s.S < 0) return false;
            if (s.Kind == StarKind.Ice) return false;
            if (s.Twin >= 0 && TwinPartnerIndex(i) < 0) return false;
            return true;
        }

        // ───────────── 玩家动作 ─────────────

        /// 摘下第 i 颗（双子星连同另一半）。返回摘下的星（串头→串尾顺序），失败返回 null。
        public List<Star> Pick(int i)
        {
            if (!CanPick(i)) return null;
            int lo = i, hi = i;
            int p = TwinPartnerIndex(i);
            if (p >= 0) { lo = Math.Min(i, p); hi = Math.Max(i, p); }
            bool touchFront = lo > 0 && Touching(lo - 1);
            bool touchBack  = hi + 1 < Stars.Count && Touching(hi);
            int inherited = 0;
            bool hasInherited = lo < Stars.Count && Attract.TryGetValue(Stars[lo].Id, out inherited);

            var picked = Stars.GetRange(lo, hi - lo + 1);
            foreach (var st in picked) Attract.Remove(st.Id);
            Stars.RemoveRange(lo, hi - lo + 1);

            if (lo > 0 && lo < Stars.Count)
            {
                if (touchFront && touchBack)
                    Attract[Stars[lo].Id] = 0;
                else if (hasInherited && touchBack)
                    Attract[Stars[lo].Id] = inherited;
            }
            Emit(new GameEvent
            {
                Type  = "picked",
                S     = picked[0].S,
                Color = picked[0].Color,
                Ids   = picked.ConvertAll(x => x.Id),
            });
            noMoveTimer = 0;
            return picked.ConvertAll(x => x.Clone());   // JS: picked.map(x => ({...x}))
        }

        /// 抽走一颗（不管能不能摘，冰星也行）：孤色消散用。
        public Star RemoveById(int id, string type = "faded")
        {
            int i = IndexOf(id);
            if (i < 0) return null;
            bool touchFront = i > 0 && Touching(i - 1);
            bool touchBack  = i + 1 < Stars.Count && Touching(i);
            int inherited = 0;
            bool hasInherited = Attract.TryGetValue(id, out inherited);

            var gone = Stars[i];
            Stars.RemoveAt(i);
            Attract.Remove(id);
            foreach (var st in Stars) if (st.Twin == id) st.Twin = -1;

            if (i > 0 && i < Stars.Count)
            {
                if (touchFront && touchBack)
                    Attract[Stars[i].Id] = 0;
                else if (hasInherited && touchBack)
                    Attract[Stars[i].Id] = inherited;
            }
            Emit(new GameEvent { Type = type, S = gone.S, Color = gone.Color, Ids = new List<int> { gone.Id } });
            return gone;
        }

        /// 把星（串头→串尾顺序）塞到第 gap 颗星前面。返回是否立即消除。
        public bool Insert(int gap, List<Star> list)
        {
            if (list == null || list.Count == 0 || Lost) return false;
            gap = Math.Max(0, Math.Min(Stars.Count, gap));
            var add = list.ConvertAll(x => x.Clone());   // JS: list.map(x => ({...x}))
            foreach (var c in add)
                if (c.Kind == StarKind.Rainbow && c.Color < 0) c.Color = ResolveRainbow(gap);

            if (Stars.Count == 0)
            {
                for (int k = 0; k < add.Count; k++)
                    add[k].S = Math.Max(0, Cfg.IntroHeadS) - k;
            }
            else if (gap == 0)
            {
                double head = Stars[0].S;
                for (int k = 0; k < add.Count; k++)
                    add[k].S = head + add.Count - k;
            }
            else
            {
                double prev = Stars[gap - 1].S;
                for (int k = 0; k < add.Count; k++)
                    add[k].S = prev - 1 - k;
            }
            Stars.InsertRange(gap, add);
            // 后面被挤到的星往后推
            for (int j = gap + add.Count; j < Stars.Count; j++)
            {
                if (Stars[j - 1].S - Stars[j].S < 1 - EPS)
                    Stars[j].S = Stars[j - 1].S - 1;
                else break;
            }
            CleanAttract();
            Emit(new GameEvent
            {
                Type  = "inserted",
                S     = add[0].S,
                Color = add[0].Color,
                Ids   = add.ConvertAll(x => x.Id),
            });
            noMoveTimer = 0;
            return TryMatchAt(gap, 1);
        }

        /// 彩星：变成两边相邻同色段较长的那一种（一样长取串头一侧）
        public int ResolveRainbow(int gap)
        {
            int lc = -1, rc = -1;
            int ll = 0, rl = 0;
            if (gap > 0 && Stars[gap - 1].Color >= 0) { lc = Stars[gap - 1].Color; ll = RunLeft(gap - 1); }
            if (gap < Stars.Count && Stars[gap].Color >= 0) { rc = Stars[gap].Color; rl = RunRight(gap); }
            if (lc < 0 && rc < 0) return 0;
            if (rc < 0) return lc;
            if (lc < 0) return rc;
            return rl > ll ? rc : lc;
        }

        // ───────────── 推进 ─────────────

        public void Update(double dt)
        {
            if (Lost || Paused || dt <= 0) return;
            while (dt > 1e-9)
            {
                double h = Math.Min(dt, Cfg.MaxStep);
                Step(h);
                dt -= h;
                if (Lost) return;
            }
        }

        private void Step(double dt)
        {
            var c   = Cfg;
            var st  = Stars;
            Time += dt;
            if (TimeStop > 0) TimeStop = Math.Max(0, TimeStop - dt);
            if (st.Count == 0) return;

            // 1) 分段
            var segStart = new List<int> { 0 };
            for (int i = 0; i + 1 < st.Count; i++)
                if (!Touching(i)) segStart.Add(i + 1);

            double global = 0;
            if (Knockback > 0)
            {
                double d = Math.Min(Knockback, c.KnockbackSpeed * dt);
                Knockback -= d; global -= d;
            }
            if (Retreat > 0)
            {
                double d = Math.Min(Retreat, c.RetreatSpeed * dt);
                Retreat -= d; global -= d;
            }
            bool frozen  = TimeStop > 0 || Knockback > 0;
            double baseV = frozen ? 0 : CurrentSpeed;

            // 2) 每段移动；记下本步开始时的段首
            var segHeads = new HashSet<int>();
            for (int g = segStart.Count - 1; g >= 0; g--)
            {
                int a = segStart[g];
                int b = g + 1 < segStart.Count ? segStart[g + 1] - 1 : st.Count - 1;
                double v = (g == segStart.Count - 1) ? baseV : 0;
                if (g < segStart.Count - 1 && !frozen && InReverse(st[a].S))
                    v = -c.ReverseSpeed;
                if (g > 0 && Attract.ContainsKey(st[a].Id))
                {
                    double gapLen = st[a - 1].S - st[a].S - 1;
                    v = Math.Max(v, gapLen / c.AttractTime + c.AttractMinSpeed);
                }
                double move = v * dt + global;
                for (int k = a; k <= b; k++) st[k].S += move;
                if (g > 0) segHeads.Add(st[a].Id);
            }

            // 3) 重叠修正：后面的星不能越过前面的星（被吸过头 / 推挤），整段一起贴齐
            for (int i = 0; i + 1 < st.Count; i++)
            {
                double over = st[i + 1].S - (st[i].S - 1);
                if (over > EPS)
                {
                    for (int j = i + 1; j < st.Count; j++)
                    {
                        bool wasGap = j + 1 < st.Count && st[j].S - st[j + 1].S > 1 + EPS;
                        st[j].S -= over;
                        if (wasGap) break;
                    }
                }
            }

            // 4) 接触检查：本步之前是段首、现在贴上了 → 接缝两边同色就检查消除
            for (int i = 0; i + 1 < Stars.Count; i++)
            {
                var back = Stars[i + 1];
                if (!segHeads.Contains(back.Id) || !Touching(i)) continue;
                segHeads.Remove(back.Id);
                int level;
                if (!Attract.TryGetValue(back.Id, out level)) level = 0;
                Attract.Remove(back.Id);
                if (SameColor(Stars[i], back) && TryMatchAt(i, level + 1)) i = -1;
            }
            CleanAttract();

            if (Stars.Count == 0) return;

            // 5) 撞星核
            if (Knockback <= 0 && Stars.Count > 0 && Stars[0].S + HeadOffset >= c.TrackLength)
            {
                Cracks++;
                Emit(new GameEvent { Type = "coreHit", S = Stars[0].S, Cracks = Cracks });
                if (Cracks >= c.MaxCracks) { Lost = true; Emit(new GameEvent { Type = "lost" }); return; }
                Knockback = c.CoreKnockback;
            }

            // 6) 无解保护：连续 N 秒没有任何可消走法，彩星混进串尾
            moveCheckTimer -= dt;
            if (moveCheckTimer <= 0)
            {
                moveCheckTimer = 0.25;
                bool intro = Stars[0].S < c.IntroHeadS && !SpeedOverride.HasValue;
                bool busy  = Attract.Count > 0 || intro;
                if (!busy && !HasAnyMove()) noMoveTimer += 0.25; else noMoveTimer = 0;
                if (noMoveTimer >= c.NoMoveRainbowSeconds) { noMoveTimer = 0; JoinRainbow(); }
            }
        }

        private bool InReverse(double u)
        {
            foreach (var zone in Cfg.ReverseZones)
                if (u >= zone[0] && u <= zone[1]) return true;
            return false;
        }

        private void JoinRainbow()
        {
            var tail = Stars.Count > 0 ? Stars[Stars.Count - 1] : null;
            var s = new Star
            {
                Id    = NextId++,
                Color = -1,
                Kind  = StarKind.Rainbow,
                Twin  = -1,
                S     = tail != null ? tail.S - 1 : 0,
            };
            Stars.Add(s);
            Emit(new GameEvent { Type = "rainbowJoined", S = s.S, Ids = new List<int> { s.Id } });
        }

        /// 把一颗星接回串尾（星守恒：随手松开的星飞回这里）
        public void AppendTail(Star star)
        {
            var tail = Stars.Count > 0 ? Stars[Stars.Count - 1] : null;
            var cpy  = star.Clone();
            cpy.S = tail != null ? tail.S - 1 : 0;
            Stars.Add(cpy);
            Emit(new GameEvent { Type = "returned", S = cpy.S, Ids = new List<int> { cpy.Id }, Color = cpy.Color });
            // 接到串尾也可能凑满 3 颗
            TryMatchAt(Stars.Count - 1, 1);
        }

        // ───────────── 消除 ─────────────

        public bool TryMatchAt(int i, int combo)
        {
            if (i < 0 || i >= Stars.Count) return false;
            int color = Stars[i].Color;
            if (color < 0) return false;
            int lo = i, hi = i;
            while (lo > 0 && Touching(lo - 1) && Stars[lo - 1].Color == color) lo--;
            while (hi + 1 < Stars.Count && Touching(hi) && Stars[hi + 1].Color == color) hi++;
            if (hi - lo + 1 < Cfg.MinMatch) return false;
            Eliminate(lo, hi, combo, color);
            return true;
        }

        private void Eliminate(int lo, int hi, int combo, int color)
        {
            bool bomb = false, stop = false;
            for (int k = lo; k <= hi; k++)
            {
                if (Stars[k].Kind == StarKind.Bomb)     bomb = true;
                if (Stars[k].Kind == StarKind.TimeStop) stop = true;
            }
            int coreLo = lo, coreHi = hi;
            if (bomb) { lo = Math.Max(0, lo - 2); hi = Math.Min(Stars.Count - 1, hi + 2); }
            var gone = Stars.GetRange(lo, hi - lo + 1);
            double atS = gone[gone.Count >> 1].S;
            foreach (var g in gone) Attract.Remove(g.Id);
            Stars.RemoveRange(lo, hi - lo + 1);

            var goneIds = new HashSet<int>();
            foreach (var g in gone) goneIds.Add(g.Id);
            foreach (var s in Stars) if (s.Twin >= 0 && goneIds.Contains(s.Twin)) s.Twin = -1;

            int score = gone.Count * Cfg.ScorePerStar * combo;
            Score    += score;
            MaxCombo  = Math.Max(MaxCombo, combo);
            if (combo >= 2) Retreat += Cfg.ComboRetreat * (combo - 1);
            if (stop) { TimeStop = Cfg.TimeStopSeconds; Emit(new GameEvent { Type = "timeStop", S = atS }); }

            Emit(new GameEvent
            {
                Type     = "eliminated",
                Combo    = combo,
                Color    = color,
                S        = atS,
                Score    = score,
                Bomb     = bomb,
                TimeStop = stop,
                Ids      = gone.ConvertAll(g => g.Id),
                Stars    = gone.ConvertAll(g => g.Clone()),
                Matched  = coreHi - coreLo + 1,
            });

            // 新缺口两端同色：后段被吸过来，形成连锁
            if (lo > 0 && lo < Stars.Count && SameColor(Stars[lo - 1], Stars[lo]))
                Attract[Stars[lo].Id] = combo;

            noMoveTimer = 0;
        }

        private void CleanAttract()
        {
            // 迭代副本，避免在遍历中修改字典
            var keys = new List<int>(Attract.Keys);
            foreach (int id in keys)
            {
                int i = IndexOf(id);
                if (i <= 0 || Touching(i - 1)) Attract.Remove(id);
            }
        }

        /// 是否还在合拢 / 连锁 / 后退中
        public bool Settling => Attract.Count > 0 || Retreat > 0 || Knockback > 0;

        // ───────────── 走法检查 ─────────────

        /// 有没有一步能直接引发消除（供无解保护检查）
        public bool HasAnyMove()
        {
            var st = Stars;
            int n = st.Count;
            int m = Cfg.MinMatch;
            if (n == 0) return HeldColors.Count > 0 ? false : true;
            // 摘一颗让两边合拢
            for (int i = 1; i + 1 < n; i++)
            {
                if (!CanPick(i) || TwinPartnerIndex(i) >= 0) continue;
                if (!Touching(i - 1) || !Touching(i)) continue;
                if (st[i - 1].Color < 0 || st[i - 1].Color != st[i + 1].Color) continue;
                if (RunLeft(i - 1) + RunRight(i + 1) >= m) return true;
            }
            var avail = new Dictionary<int, int>();
            bool rainbow = false;
            for (int i = 0; i < n; i++)
            {
                if (!CanPick(i)) continue;
                if (st[i].Color < 0) { rainbow = true; continue; }
                int cnt;
                avail[st[i].Color] = avail.TryGetValue(st[i].Color, out cnt) ? cnt + 1 : 1;
            }
            foreach (int c in HeldColors)
            {
                if (c < 0) rainbow = true;
                else { int cnt; avail[c] = avail.TryGetValue(c, out cnt) ? cnt + 100 : 100; }
            }
            for (int gap = 0; gap <= n; gap++)
            {
                int lc = gap > 0 ? st[gap - 1].Color : -1;
                int rc = gap < n ? st[gap].Color : -1;
                int ll = lc >= 0 ? RunLeft(gap - 1) : 0;
                int rl = rc >= 0 ? RunRight(gap) : 0;
                bool joined = gap > 0 && gap < n && Touching(gap - 1);
                if (rainbow && (ll >= 2 || rl >= 2)) return true;
                if (lc >= 0 && avail.ContainsKey(lc))
                {
                    bool both  = joined && rc == lc;
                    int total  = ll + 1 + (both ? rl : 0);
                    int own    = PickableIn(gap - ll, gap - 1) + (both ? PickableIn(gap, gap + rl - 1) : 0);
                    if (total >= m && avail[lc] - own > 0) return true;
                }
                if (rc >= 0 && rc != lc && avail.ContainsKey(rc))
                {
                    if (rl + 1 >= m && avail[rc] - PickableIn(gap, gap + rl - 1) > 0) return true;
                }
            }
            return false;
        }

        public int PickableIn(int a, int b)
        {
            int k = 0;
            for (int i = Math.Max(0, a); i <= b && i < Stars.Count; i++)
                if (CanPick(i)) k++;
            return k;
        }

        public int RunLeft(int i)
        {
            int color = Stars[i].Color;
            int k = 1;
            for (int j = i - 1; j >= 0 && Touching(j) && Stars[j].Color == color; j--) k++;
            return k;
        }

        public int RunRight(int i)
        {
            int color = Stars[i].Color;
            int k = 1;
            for (int j = i + 1; j < Stars.Count && Touching(j - 1) && Stars[j].Color == color; j++) k++;
            return k;
        }

        // ───────────── 工具 ─────────────

        private void Emit(GameEvent e) { Events.Add(e); }

        public List<GameEvent> DrainEvents()
        {
            var e = Events;
            Events = new List<GameEvent>();
            return e;
        }

        /// 深拷贝（机器人试走法用），不拷贝事件
        public ChainSim Clone()
        {
            var c = new ChainSim(Cfg);
            c.HeadOffset     = HeadOffset;
            c.Cracks         = Cracks;
            c.Score          = Score;
            c.MaxCombo       = MaxCombo;
            c.Time           = Time;
            c.Lost           = Lost;
            c.Paused         = Paused;
            c.SpeedScale     = SpeedScale;
            c.SpeedOverride  = SpeedOverride;
            c.TimeStop       = TimeStop;
            c.Retreat        = Retreat;
            c.Knockback      = Knockback;
            c.noMoveTimer    = noMoveTimer;
            c.moveCheckTimer = moveCheckTimer;
            c.NextId         = NextId;
            c.Stars          = Stars.ConvertAll(s => s.Clone());
            c.Attract        = new Dictionary<int, int>(Attract);
            c.HeldColors     = new List<int>(HeldColors);
            return c;
        }

        /// 调试输出：颜色序列，缺口用 | 表示
        public string Dump()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Stars.Count; i++)
            {
                var s = Stars[i];
                string t = s.Kind == StarKind.Normal
                    ? s.Color.ToString()
                    : s.Kind.ToString()[0].ToString().ToUpper() + s.Color;
                sb.Append(t);
                if (i + 1 < Stars.Count) sb.Append(Touching(i) ? " " : " | ");
            }
            return sb.ToString();
        }

        private static bool SameColor(Star a, Star b) => a.Color >= 0 && a.Color == b.Color;
    }

    // ──────────────────── 辅助：Load 用的输入类型 ────────────────────

    /// Load() 的输入元素，对应 JS 里传给 sim.load() 的 {id, color, kind, twin} 普通对象
    public class StarSource
    {
        public object   OrigId;        // 原始 id（用于 twin 匹配）；null 则用列表下标
        public int      Color;
        public StarKind Kind    = StarKind.Normal;
        public object   TwinOrigId;    // 双子搭档的原始 id；null 表示无
    }
}
