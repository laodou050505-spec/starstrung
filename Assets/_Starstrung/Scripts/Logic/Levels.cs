// 关卡数据 + 星串生成（纯逻辑）。对应 JS levels.js。
// 教学关 + 4 个星域 × 5 关。

using System;
using System.Collections.Generic;

namespace Starstrung.Logic
{
    // ──────────────────── 关卡数据结构 ────────────────────

    public class LevelName
    {
        public string Zh; // 中文名
        public string En; // 英文名
    }

    public class SpecialsOpts
    {
        public double? Ice;
        public double? Twin;
        public double? Bomb;
        public double? Timestop;
        public double? Rainbow;
    }

    public class BossOpts
    {
        public int Hp = 3;
    }

    public class GenChainOpts
    {
        public int         Colors;
        public int         Length;
        public SpecialsOpts Specials = new SpecialsOpts();
        public double      PairRate = 0.3;
        public double      Sandwich = 0.2;
    }

    public class TutorialStageOpts
    {
        public string Teach;
        public string Place;
        public double Speed;
        public int[]  Chain;    // 显式颜色数组；null 时用 Length + Seed 生成
        public int    Length;
        public int    Seed;
    }

    public class Level
    {
        public string         Id;
        public int            Zone;
        public LevelName      Name;
        public bool           Tutorial;
        public int            Colors;
        public int            Length;    // 星串长度
        public double         Pace;      // 整段串跑完多少秒（决定基础速度）
        public double         Ramp;      // 速度增量
        public double?        MaxSpeed;  // 覆盖默认最大速度
        public int            Seed;
        public GenChainOpts   Gen;       // pairRate + sandwich（由 gen(i) 生成）
        public SpecialsOpts   Specials;
        public TrackSpec      Track;
        public int            Chains = 1;      // 双串关 = 2
        public bool           Fork;
        public double[][]     Reverse;         // [[u0,u1],...] 单位：轨道 slots 的分数
        public BossOpts       Boss;
        public TutorialStageOpts[] Stages;     // 教学关子段
        /// <summary>无尽模式（11-24 用户：这一关挺好，后面可以有无尽模式，按玩家分数加难度）：
        /// 吞星兽一波波来，每打退一只，速度和兽的血量都往上走，直到星核三道裂纹裂满为止。</summary>
        public bool           Endless;
    }

    // ──────────────────── 生成函数 ────────────────────

    public static class Levels
    {
        /// gen(i)：i=0..19，生成 pairRate/sandwich 参数
        static GenChainOpts GenOpts(int i) => new GenChainOpts
        {
            PairRate = Math.Round(0.42 - 0.011 * i, 3),
            Sandwich = Math.Round(0.36 - 0.015 * i, 3),
        };

        // ─── TrackSpec 快捷构造 ───────────────────

        static TrackSpec Sweep(TrackSpec o)  { o.Type = "sweep";  return o; }
        static TrackSpec Petal(TrackSpec o)  { o.Type = "petal";  return o; }
        static TrackSpec Spiral(TrackSpec o) { o.Type = "spiral"; return o; }
        static TrackSpec Vortex(TrackSpec o) { o.Type = "vortex"; return o; }

        // ──────────────────── LEVELS 数组 ────────────────────

        public static readonly Level[] LEVELS = new Level[]
        {
            // ── 教学关 T ──────────────────────────────────────────────────────
            new Level
            {
                Id = "T", Zone = 0,
                Name = new LevelName { Zh = "序章 · 手可摘星辰", En = "Prologue · Reach Out" },
                Tutorial = true, Colors = 3,
                Track = Sweep(new TrackSpec { RStart=2.6, Sweeps=3, Span=120, YOuter=1.65 }),
                Stages = new[]
                {
                    new TutorialStageOpts { Teach="pick",   Place="near",  Speed=0,    Chain=new[]{2,2,0,2,0,0} },
                    new TutorialStageOpts { Teach="insert", Place="near",  Speed=0,    Chain=new[]{1,1,2,2,1,2} },
                    new TutorialStageOpts { Teach="far",    Place="far",   Speed=0.12, Chain=new[]{0,0,2,0,1,2,2,1,1} },
                    new TutorialStageOpts { Teach="play",   Place="intro", Speed=0.32, Length=21, Seed=7 },
                },
            },

            // ── 星域一：初光 ──────────────────────────────────────────────────
            new Level
            {
                Id="1-1", Zone=1, Name=new LevelName{Zh="初光",En="First Light"},
                Colors=3, Length=40, Pace=103, Ramp=0.0046, Seed=101,
                Gen=GenOpts(0),
                Track=Sweep(new TrackSpec{RStart=3.2,Sweeps=3,Span=140,YOuter=1.75}),
            },
            new Level
            {
                Id="1-2", Zone=1, Name=new LevelName{Zh="远岸",En="Far Shore"},
                Colors=3, Length=50, Pace=99, Ramp=0.0046, Seed=102,
                Gen=GenOpts(1),
                Track=Sweep(new TrackSpec{RStart=3.8,Sweeps=4,Span=160,StartSide=1,Wave=5,WaveN=2}),
            },
            new Level
            {
                Id="1-3", Zone=1, Name=new LevelName{Zh="第四色",En="The Fourth Hue"},
                Colors=4, Length=55, Pace=94, Ramp=0.0057, Seed=103,
                Gen=GenOpts(2),
                Track=Petal(new TrackSpec{RStart=3.6,Sweeps=4,Span=180}),
            },
            new Level
            {
                Id="1-4", Zone=1, Name=new LevelName{Zh="停摆",En="Stillpoint"},
                Colors=4, Length=60, Pace=89, Ramp=0.0057, Seed=104,
                Gen=GenOpts(3),
                Specials=new SpecialsOpts{Timestop=0.05},
                Track=Sweep(new TrackSpec{RStart=3.8,Sweeps=4,Span=190,Center=10,YOuter=2.25}),
            },
            new Level
            {
                Id="1-5", Zone=1, Name=new LevelName{Zh="潮汐",En="Tides"},
                Colors=4, Length=70, Pace=86, Ramp=0.0057, Seed=105,
                Gen=GenOpts(4),
                Track=Sweep(new TrackSpec{RStart=4.0,Sweeps=5,Span=170,Wave=7,WaveN=3}),
            },

            // ── 星域二：冰河 ──────────────────────────────────────────────────
            new Level
            {
                Id="2-1", Zone=2, Name=new LevelName{Zh="冰河",En="Ice River"},
                Colors=4, Length=62, Pace=80, Ramp=0.006, Seed=201,
                Gen=GenOpts(5),
                Specials=new SpecialsOpts{Ice=0.08},
                Track=Sweep(new TrackSpec{RStart=3.8,Sweeps=4,Span=210,YOuter=1.35,YInner=1.22}),
            },
            new Level
            {
                Id="2-2", Zone=2, Name=new LevelName{Zh="双子",En="Gemini"},
                Colors=4, Length=62, Pace=78, Ramp=0.006, Seed=202,
                Gen=GenOpts(6),
                Specials=new SpecialsOpts{Twin=0.06},
                Track=Petal(new TrackSpec{RStart=3.8,Sweeps=4,Span=200,StartSide=1}),
            },
            new Level
            {
                Id="2-3", Zone=2, Name=new LevelName{Zh="冻结的双星",En="Frozen Binary"},
                Colors=4, Length=66, Pace=66, Ramp=0.006, Seed=203,
                Gen=GenOpts(7),
                Specials=new SpecialsOpts{Ice=0.06,Twin=0.05},
                Track=Sweep(new TrackSpec{RStart=4.0,Sweeps=4,Span=220,Center=-10,Wave=8,WaveN=2}),
            },
            new Level
            {
                Id="2-4", Zone=2, Name=new LevelName{Zh="逆流",En="Backflow"},
                Colors=4, Length=70, Pace=61, Ramp=0.0072, Seed=204,
                Gen=GenOpts(8),
                Reverse=new double[][]{new[]{0.55,0.72}},
                Track=Sweep(new TrackSpec{RStart=3.8,Sweeps=4,Span=200,StartSide=1,YOuter=2.0}),
            },
            new Level
            {
                Id="2-5", Zone=2, Name=new LevelName{Zh="螺旋之链",En="Double Helix"},
                Colors=4, Length=34, Chains=2, Pace=174, Ramp=0.006, Seed=205,
                Gen=GenOpts(9),
                Track=Spiral(new TrackSpec{RStart=3.8,Turns=0.58,A0=-150,YOuter=1.9}),
            },

            // ── 星域三：星爆 ──────────────────────────────────────────────────
            new Level
            {
                Id="3-1", Zone=3, Name=new LevelName{Zh="星爆",En="Starburst"},
                Colors=4, Length=70, Pace=49, Ramp=0.0075, Seed=301,
                Gen=GenOpts(10),
                Specials=new SpecialsOpts{Bomb=0.06},
                Track=Vortex(new TrackSpec{RStart=3.8,Turns=0.6,A0=-160,YOuter=2.4}),
            },
            new Level
            {
                Id="3-2", Zone=3, Name=new LevelName{Zh="五色",En="Five Hues"},
                Colors=5, Length=66, Pace=53, Ramp=0.0063, Seed=302,
                Gen=GenOpts(11),
                Track=Petal(new TrackSpec{RStart=4.0,Sweeps=4,Span=220,Center=8}),
            },
            new Level
            {
                Id="3-3", Zone=3, Name=new LevelName{Zh="缠绕之火",En="Tangled Fire"},
                Colors=5, Length=40, Chains=2, Pace=128, Ramp=0.0063, Seed=303,
                Gen=GenOpts(12),
                Specials=new SpecialsOpts{Bomb=0.06},
                Track=Spiral(new TrackSpec{RStart=4.0,Turns=0.58,A0=150,Dir=-1,YOuter=2.1,Wave=5,WaveN=2}),
            },
            new Level
            {
                Id="3-4", Zone=3, Name=new LevelName{Zh="岔路",En="Fork"},
                Colors=5, Length=72, Pace=60, Ramp=0.0075, Seed=304,
                Gen=GenOpts(13),
                Fork=true,
                Track=new TrackSpec{Type="fork",RStart=3.8,Span=200,ForkR=2.2},
            },
            new Level
            {
                Id="3-5", Zone=3, Name=new LevelName{Zh="冰岔",En="Frozen Fork"},
                Colors=5, Length=74, Pace=65, Ramp=0.0075, Seed=305,
                Gen=GenOpts(14),
                Fork=true,
                Specials=new SpecialsOpts{Ice=0.06},
                Track=new TrackSpec{Type="fork",RStart=4.0,Span=230,StartSide=1,YOuter=2.2,ForkR=2.4,LongSweeps=4},
            },

            // ── 星域四：深空 ──────────────────────────────────────────────────
            new Level
            {
                Id="4-1", Zone=4, Name=new LevelName{Zh="背后的星",En="Behind You"},
                Colors=5, Length=74, Pace=64, Ramp=0.0078, Seed=401,
                Gen=GenOpts(15),
                Track=Spiral(new TrackSpec{RStart=3.8,Turns=1.4,A0=-90}),
            },
            new Level
            {
                Id="4-2", Zone=4, Name=new LevelName{Zh="虹",En="Prism"},
                Colors=5, Length=76, Pace=76, Ramp=0.0078, Seed=402,
                Gen=GenOpts(16),
                Specials=new SpecialsOpts{Rainbow=0.05},
                Track=Vortex(new TrackSpec{RStart=4.0,Turns=1.3,A0=90,Dir=-1}),
            },
            new Level
            {
                Id="4-3", Zone=4, Name=new LevelName{Zh="万象",En="All Things"},
                Colors=5, Length=80, Pace=67, Ramp=0.0078, Seed=403,
                Gen=GenOpts(17),
                Specials=new SpecialsOpts{Ice=0.04,Twin=0.03,Bomb=0.03,Timestop=0.03},
                Track=Spiral(new TrackSpec{RStart=4.0,Turns=1.25,A0=-60,Wave=8,WaveN=4}),
            },
            new Level
            {
                Id="4-4", Zone=4, Name=new LevelName{Zh="疾星",En="Swift Stars"},
                Colors=5, Length=76, Pace=46, Ramp=0.0091, MaxSpeed=2.4, Seed=404,
                Gen=GenOpts(18),
                Track=Sweep(new TrackSpec{RStart=4.2,Sweeps=5,Span=220,StartSide=1}),
            },
            new Level
            {
                Id="4-5", Zone=4, Name=new LevelName{Zh="吞星兽",En="Star Eater"},
                Colors=5, Length=84, Pace=69, Ramp=0.0078, Seed=405,
                Gen=GenOpts(19),
                Boss=new BossOpts{Hp=3},
                Track=Vortex(new TrackSpec{RStart=4.2,Turns=1.2,A0=30,Dir=-1}),
            },
            // 无尽：通关全部 20 关后解锁。一波波吞星兽，分数越高越快越硬，星核裂满为止
            new Level
            {
                Id="E", Zone=5, Name=new LevelName{Zh="无尽 · 星渊",En="Endless · The Maw"},
                Colors=5, Length=90, Pace=62, Ramp=0.009, MaxSpeed=2.8, Seed=999,
                Gen=GenOpts(19),
                Boss=new BossOpts{Hp=2},
                Endless=true,
                Track=Vortex(new TrackSpec{RStart=4.2,Turns=1.25,A0=40,Dir=-1}),   // 11-26：原 1.3 圈 / 20° 收尾落在身侧 83°（自检 G02 不过）；扫参数选出既不同于 4-5、又从正面收进星核的一条
            },
        };

        // ──────────────────── 查询 ────────────────────

        public static Level LevelById(string id)
        {
            foreach (var l in LEVELS) if (l.Id == id) return l;
            return null;
        }

        // ──────────────────── 星串生成 ────────────────────

        /// 生成一条星串。对应 JS genChain(seed, {colors, length, specials, pairRate, sandwich})。
        /// 返回 List<StarSource>，可直接传给 ChainSim.Load()（需先 ConvertAll((s)=>(object)s)）。
        /// <summary>无尽模式第 wave 波的星串：比基准长 8%/波（上限 +60%），种子随波次变（每波不同）</summary>
        public static List<StarSource> GenEndlessChain(Level level, int wave)
        {
            var opts = new GenChainOpts
            {
                Colors = level.Gen.Colors,
                Length = (int)System.Math.Round(level.Length * System.Math.Min(1.6, 1 + 0.08 * (wave - 1))),
                PairRate = System.Math.Max(0.12, level.Gen.PairRate - 0.02 * (wave - 1)),
                Sandwich = System.Math.Max(0.05, level.Gen.Sandwich - 0.015 * (wave - 1)),
                Specials = level.Gen.Specials,
            };
            return GenChain(level.Seed + wave * 31, opts);
        }

        /// <summary>把生成结果包成 Load 要的 list</summary>
        public static List<object> EndlessList(List<StarSource> chain) => ToLoadList(chain);

        public static List<StarSource> GenChain(int seed, GenChainOpts opts)
        {
            int    colors   = opts.Colors;
            int    length   = opts.Length;
            var    sp       = opts.Specials ?? new SpecialsOpts();
            double pairRate = opts.PairRate;
            double sandwich = opts.Sandwich;

            var rng = new Rng(seed);
            // 临时 raw 数组
            var rawColor = new int[length];
            var rawKind  = new StarKind[length];
            var rawTwin  = new int[length]; // -1 = 无
            for (int k = 0; k < length; k++) { rawKind[k] = StarKind.Normal; rawTwin[k] = -1; }

            // 1. 生成颜色序列
            int prev = -1, prev2 = -1;
            int count = 0;
            while (count < length)
            {
                int c = rng.Range(0, colors);
                if (c == prev)
                    c = (c + 1 + rng.Range(0, colors - 1)) % colors;
                // 夹心控制
                if (c == prev2 && rng.Value() > sandwich)
                {
                    for (int k = 1; k < colors; k++) { int d = (c + k) % colors; if (d != prev) { c = d; break; } }
                }
                int run = rng.Value() < pairRate ? 2 : 1;
                for (int k = 0; k < run && count < length; k++)
                {
                    rawColor[count++] = c;
                }
                prev2 = prev; prev = c;
            }

            // 2. 补齐颜色（每种颜色至少 3 颗）
            for (int c = 0; c < colors; c++)
            {
                int guard = 0;
                while (CountColor(rawColor, length, c) < 3 && guard++ < 200)
                {
                    int idx = rng.Range(1, length - 1);
                    if (rawColor[idx] == c) continue;
                    int left  = rawColor[idx - 1] == c ? (idx >= 2 && rawColor[idx - 2] == c ? 2 : 1) : 0;
                    int right = idx + 1 < length && rawColor[idx + 1] == c ? (idx + 2 < length && rawColor[idx + 2] == c ? 2 : 1) : 0;
                    if (left + right + 1 >= 3) continue;
                    rawColor[idx] = c;
                }
            }

            // 3. 特殊星（used 集合保证间距 ≥ 3）
            var used = new HashSet<int>();
            bool Free(int i) {
                if (i < 2 || i >= length - 1) return false;
                foreach (int u in used) if (Math.Abs(u - i) < 3) return false;
                return true;
            }

            void Place(StarKind kind, double? ratio)
            {
                if (ratio == null || ratio.Value == 0) return;
                int n = (int)Math.Floor(length * ratio.Value + 0.5); // JS Math.round：.5 向上取（C# Math.Round 默认银行家舍入）
                int guard = 0;
                for (int k = 0; k < n && guard++ < 500;)
                {
                    int i = rng.Range(2, length - 2);
                    if (!Free(i)) continue;
                    if (kind == StarKind.Twin)
                    {
                        if (!Free(i + 1)) continue;
                        rawKind[i] = StarKind.Twin; rawKind[i + 1] = StarKind.Twin;
                        rawTwin[i] = i + 1; rawTwin[i + 1] = i;
                        used.Add(i); used.Add(i + 1);
                    }
                    else
                    {
                        rawKind[i] = kind;
                        if (kind == StarKind.Rainbow) rawColor[i] = -1;
                        used.Add(i);
                    }
                    k++;
                }
            }

            Place(StarKind.Ice,      sp.Ice);
            Place(StarKind.Twin,     sp.Twin);
            Place(StarKind.Bomb,     sp.Bomb);
            Place(StarKind.TimeStop, sp.Timestop);
            Place(StarKind.Rainbow,  sp.Rainbow);

            // 4. 组装 StarSource 列表
            var out_ = new List<StarSource>(length);
            for (int i = 0; i < length; i++)
            {
                out_.Add(new StarSource
                {
                    OrigId     = (object)i,
                    Color      = rawColor[i],
                    Kind       = rawKind[i],
                    TwinOrigId = rawTwin[i] >= 0 ? (object)rawTwin[i] : null,
                });
            }
            return out_;
        }

        static int CountColor(int[] arr, int len, int c)
        {
            int k = 0;
            for (int i = 0; i < len; i++) if (arr[i] == c) k++;
            return k;
        }

        /// 把 List<StarSource> 转成 Load() 所需的 List<object>
        public static List<object> ToLoadList(List<StarSource> src) =>
            src.ConvertAll(s => (object)s);
    }
}
