// 轨道（纯数学，不依赖 three）：Catmull-Rom 采样 → 弧长表；星位 ↔ 米 映射。
// 对应 JS track.js，逐位一致（hypot 用 Sqrt(a²+b²+c²) 代替，可接受微小差异）。

using System;
using System.Collections.Generic;

namespace Starstrung.Logic
{
    // ──────────────────── 轨道接口 ────────────────────

    /// Track 和 ForkTrack 均实现此接口，Game 里统一用 ITrack
    public interface ITrack
    {
        double   Slots    { get; }
        double   Length   { get; }
        List<Vec3> Pts    { get; }
        double[] U        { get; }
        Vec3     PointAtU(double u);
        Vec3     TangentAtU(double u);
        double   SizeAtU(double u);
        double   UToM(double u);
        double   MToU(double m);
        double   SlotsBackFromEnd(double meters);
        Vec3     Head     { get; }
    }

    // ──────────────────── TrackSpec ────────────────────

    /// 关卡数据里的轨道规格（所有字段可空，不设则在生成函数里使用 JS 默认值）
    public class TrackSpec
    {
        public string  Type      = "sweep"; // sweep / spiral / vortex / petal / fork
        // param 默认值
        public double? RStart;      // 3.6
        public double? REnd;        // 0.62
        public double? Pow;         // 1.15
        public double? YOuter;      // 1.9
        public double? YInner;      // 1.3
        public double? Wave;        // 0
        public double? WaveN;       // 2
        public int?    Steps;       // 260
        public double? Bulge;       // 0
        public double? BulgeN;      // 0
        // sweepTrack 默认值
        public int?    Sweeps;      // 4
        public double? Span;        // 160
        public double? Center;      // 0
        public int?    StartSide;   // -1
        public double? Narrow;      // 0.5
        public double? TurnDrop;    // 0.7
        // spiralTrack 默认值
        public double? Turns;       // 1.5
        public double? A0;          // -90
        public int?    Dir;         // 1
        // ForkTrack 专属
        public double? ForkR;       // 2.2
        public int?    ShortSweeps; // 1
        public int?    LongSweeps;  // 3
        // approach 参数
        public double? Lateral;     // 0

        /// 对象展开合并：base 的字段被 over 覆盖（over 字段不为 null 才覆盖）
        /// 对应 JS { ...base, ...over } 语义
        public static TrackSpec Merge(TrackSpec @base, TrackSpec over)
        {
            var r = new TrackSpec();
            r.Type       = over.Type ?? @base.Type;
            r.RStart     = over.RStart     ?? @base.RStart;
            r.REnd       = over.REnd       ?? @base.REnd;
            r.Pow        = over.Pow        ?? @base.Pow;
            r.YOuter     = over.YOuter     ?? @base.YOuter;
            r.YInner     = over.YInner     ?? @base.YInner;
            r.Wave       = over.Wave       ?? @base.Wave;
            r.WaveN      = over.WaveN      ?? @base.WaveN;
            r.Steps      = over.Steps      ?? @base.Steps;
            r.Bulge      = over.Bulge      ?? @base.Bulge;
            r.BulgeN     = over.BulgeN     ?? @base.BulgeN;
            r.Sweeps     = over.Sweeps     ?? @base.Sweeps;
            r.Span       = over.Span       ?? @base.Span;
            r.Center     = over.Center     ?? @base.Center;
            r.StartSide  = over.StartSide  ?? @base.StartSide;
            r.Narrow     = over.Narrow     ?? @base.Narrow;
            r.TurnDrop   = over.TurnDrop   ?? @base.TurnDrop;
            r.Turns      = over.Turns      ?? @base.Turns;
            r.A0         = over.A0         ?? @base.A0;
            r.Dir        = over.Dir        ?? @base.Dir;
            r.ForkR      = over.ForkR      ?? @base.ForkR;
            r.ShortSweeps= over.ShortSweeps?? @base.ShortSweeps;
            r.LongSweeps = over.LongSweeps ?? @base.LongSweeps;
            r.Lateral    = over.Lateral    ?? @base.Lateral;
            return r;
        }

        /// 用于 SelfTest 的序列化，检查每关路线不一样
        public string Sig()
        {
            return $"{Type},{RStart},{REnd},{Pow},{YOuter},{YInner},{Wave},{WaveN},{Steps},{Bulge},{BulgeN}," +
                   $"{Sweeps},{Span},{Center},{StartSide},{Narrow},{TurnDrop},{Turns},{A0},{Dir}," +
                   $"{ForkR},{ShortSweeps},{LongSweeps},{Lateral}";
        }
    }

    // ──────────────────── 数学工具 ────────────────────

    static class TrackMath
    {
        public const double DEG = Math.PI / 180.0;

        public static Vec3 V3(double x, double y, double z) => new Vec3(x, y, z);
        public static Vec3 Sub(Vec3 a, Vec3 b) => V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        // 对应 JS Math.hypot(a.x,a.y,a.z)，用 sqrt(a²+b²+c²) 代替
        public static double Len(Vec3 a) => Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);
        public static Vec3 Lerp3(Vec3 a, Vec3 b, double t) =>
            V3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;
        // 极坐标 → 世界坐标（θ=0 对应 -Z 方向，向右为正）
        public static Vec3 Polar(double r, double th, double y) =>
            V3(r * Math.Sin(th), y, -r * Math.Cos(th));
    }

    // ──────────────────── Track ────────────────────

    public class Track : ITrack
    {
        public List<Vec3> Pts { get; private set; }
        public double[]   M   { get; private set; } // 弧长（米），累计
        public double[]   U_  { get; private set; } // 星位，累计
        public double     Length  { get; private set; } // 米
        public double     Slots   { get; private set; } // 星位
        public Vec3       Head    { get; private set; }

        double[] ITrack.U => U_;

        static readonly double ELEV_up   = 22;
        static readonly double ELEV_down = 38;
        static readonly double ELEV_yMin = 0.8;
        static readonly double ELEV_yMax = 2.6;

        public Track(List<Vec3> ctrl, Vec3? head = null, double step = 0.02)
        {
            var h = head ?? LAYOUT.Head;
            Head = h;
            var pts = new List<Vec3>();
            int cn = ctrl.Count;
            for (int i = 0; i < cn - 1; i++)
            {
                var p0 = ctrl[Math.Max(0, i - 1)];
                var p1 = ctrl[i];
                var p2 = ctrl[i + 1];
                var p3 = ctrl[Math.Min(cn - 1, i + 2)];
                double segLen = TrackMath.Len(TrackMath.Sub(p2, p1));
                int n = Math.Max(2, (int)Math.Ceiling(segLen / step));
                for (int k = 0; k < n; k++) pts.Add(Catmull(p0, p1, p2, p3, (double)k / n));
            }
            pts.Add(ctrl[cn - 1]);
            Pts = pts;

            int pn = pts.Count;
            var m = new double[pn];
            var u = new double[pn];
            m[0] = 0; u[0] = 0;
            for (int i = 1; i < pn; i++)
            {
                double dm  = TrackMath.Len(TrackMath.Sub(pts[i], pts[i - 1]));
                var    mid = TrackMath.Lerp3(pts[i], pts[i - 1], 0.5);
                m[i] = m[i - 1] + dm;
                u[i] = u[i - 1] + dm / StarSize(TrackMath.Len(TrackMath.Sub(mid, h)));
            }
            M      = m;
            U_     = u;
            Length = m[pn - 1];
            Slots  = u[pn - 1];
        }

        /// 星的直径（米），d = 离玩家头部的距离
        public static double StarSize(double d) =>
            Math.Min(LAYOUT.Star.Max, Math.Max(LAYOUT.Star.Min, LAYOUT.Star.Base + LAYOUT.Star.PerMeter * d));

        static Vec3 Catmull(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, double t)
        {
            double t2 = t * t, t3 = t2 * t;
            double F(double a, double b, double c, double d) =>
                0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
            return TrackMath.V3(F(p0.X, p1.X, p2.X, p3.X), F(p0.Y, p1.Y, p2.Y, p3.Y), F(p0.Z, p1.Z, p2.Z, p3.Z));
        }

        /// 二分查找：返回 (i, t) 使得 arr[i] ≤ v < arr[i+1]，t 为线性插值系数
        public static (int idx, double t) Seek(double[] arr, double v)
        {
            if (v <= arr[0]) return (0, 0.0);
            int n = arr.Length - 1;
            if (v >= arr[n]) return (n - 1, 1.0);
            int lo = 0, hi = n;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (arr[mid] <= v) lo = mid; else hi = mid; }
            double denom = arr[lo + 1] - arr[lo];
            return (lo, denom == 0 ? 0.0 : (v - arr[lo]) / denom);
        }

        public Vec3 PointAtU(double u)
        {
            if (u < 0)
            {
                var d = TrackMath.Sub(Pts[1], Pts[0]);
                double k = u * StarSize(TrackMath.Len(TrackMath.Sub(Pts[0], Head))) / (TrackMath.Len(d) != 0 ? TrackMath.Len(d) : 1);
                return TrackMath.V3(Pts[0].X + d.X * k, Pts[0].Y + d.Y * k, Pts[0].Z + d.Z * k);
            }
            var (i, t) = Seek(U_, u);
            return TrackMath.Lerp3(Pts[i], Pts[i + 1], t);
        }

        public Vec3 TangentAtU(double u)
        {
            var (i, _) = Seek(U_, Math.Max(0, u));
            var d = TrackMath.Sub(Pts[i + 1], Pts[i]);
            double l = TrackMath.Len(d);
            if (l == 0) return TrackMath.V3(0, 0, 1);
            return TrackMath.V3(d.X / l, d.Y / l, d.Z / l);
        }

        public double UToM(double u)
        {
            var (i, t) = Seek(U_, u);
            return M[i] + (M[i + 1] - M[i]) * t;
        }

        public double MToU(double mm)
        {
            var (i, t) = Seek(M, mm);
            return U_[i] + (U_[i + 1] - U_[i]) * t;
        }

        public double SizeAtU(double u)
        {
            return StarSize(TrackMath.Len(TrackMath.Sub(PointAtU(u), Head)));
        }

        public double SlotsBackFromEnd(double meters)
        {
            return Slots - MToU(Math.Max(0, Length - meters));
        }

        // ──────────────────── 轨道形状生成器 ────────────────────

        static double ClampY(double r, double y)
        {
            double h    = LAYOUT.Head.Y;
            double tanU = Math.Tan(ELEV_up   * TrackMath.DEG);
            double tanD = Math.Tan(ELEV_down * TrackMath.DEG);
            return Math.Max(ELEV_yMin, Math.Min(ELEV_yMax,
                Math.Max(h - r * tanD, Math.Min(h + r * tanU, y))));
        }

        /// 收尾：沿轨道结束时所在的方向直接进星核（lateral = 双串时上下错开）
        internal static void Approach(List<Vec3> pts, double lateral)
        {
            var core = LAYOUT.Core;
            var last = pts[pts.Count - 1];
            double dx = last.X - core.X, dz = last.Z - core.Z;
            double l  = Math.Sqrt(dx * dx + dz * dz);
            if (l == 0) l = 1;
            dx /= l; dz /= l;
            double R = LAYOUT.CoreRadius + 0.025;
            pts.Add(TrackMath.V3(core.X + dx * 0.2, core.Y + (last.Y - core.Y) * 0.3 + lateral, core.Z + dz * 0.2));
            pts.Add(TrackMath.V3(core.X + dx * R,   core.Y + lateral * 0.5,                      core.Z + dz * R));
        }

        /// 通用参数化轨道：theta(t) 给角度，rt(t) 给半径缩放系数
        internal static List<Vec3> Param(TrackSpec o, Func<double, double> theta, Func<double, double> rt = null)
        {
            if (rt == null) rt = t => t;
            double rStart = o.RStart ?? 3.6;
            double rEnd   = o.REnd   ?? 0.62;
            double pow    = o.Pow    ?? 1.15;
            double yOuter = o.YOuter ?? 1.9;
            double yInner = o.YInner ?? 1.3;
            double wave   = o.Wave   ?? 0;
            double waveN  = o.WaveN  ?? 2;
            int    steps  = o.Steps  ?? 260;
            double bulge  = o.Bulge  ?? 0;
            double bulgeN = o.BulgeN ?? 0;

            var pts = new List<Vec3>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                double t  = (double)i / steps;
                double r  = TrackMath.Lerp(rStart, rEnd, 1 - Math.Pow(1 - rt(t), pow));
                double th = theta(t);
                if (bulge != 0) r *= 1 + bulge * Math.Cos(bulgeN * th) * (1 - t * 0.8);
                double y  = TrackMath.Lerp(yOuter, yInner, t * t * (3 - 2 * t));
                if (wave != 0) y += r * Math.Tan(wave * TrackMath.DEG * Math.Sin(waveN * 2 * Math.PI * t)) * (1 - t * 0.7);
                pts.Add(TrackMath.Polar(r, th, ClampY(r, y)));
            }
            return pts;
        }

        /// 往返扫轨道
        public static List<Vec3> SweepTrack(TrackSpec o)
        {
            int    sweeps    = o.Sweeps    ?? 4;
            double span      = o.Span      ?? 160.0;
            double center    = o.Center    ?? 0.0;
            int    startSide = o.StartSide ?? -1;
            double narrow    = o.Narrow    ?? 0.5;
            double turnDrop  = o.TurnDrop  ?? 0.7;
            double c    = center * TrackMath.DEG;
            double half = (span / 2) * TrackMath.DEG;

            Func<double, double> rt = t =>
                Math.Min(1, Math.Max(0, t + turnDrop * Math.Sin(2 * Math.PI * sweeps * t) / (2 * Math.PI * sweeps)));
            var pts = Param(o,
                t => c + startSide * half * TrackMath.Lerp(1, narrow, t) * Math.Cos(Math.PI * sweeps * t),
                rt);
            Approach(pts, o.Lateral ?? 0.0);
            return pts;
        }

        /// 螺旋轨道
        public static List<Vec3> SpiralTrack(TrackSpec o)
        {
            double turns = o.Turns ?? 1.5;
            double a0    = o.A0    ?? -90.0;
            int    dir   = o.Dir   ?? 1;
            var pts = Param(o, t => a0 * TrackMath.DEG + dir * turns * 2 * Math.PI * t);
            Approach(pts, o.Lateral ?? 0.0);
            return pts;
        }

        /// 漩涡轨道（螺旋 + 从高处盘旋而下）
        /// 对应 JS spiralTrack({ yOuter: 2.45, yInner: 1.28, pow: 0.9, ...o })
        public static List<Vec3> VortexTrack(TrackSpec o)
        {
            var merged = TrackSpec.Merge(
                new TrackSpec { YOuter = 2.45, YInner = 1.28, Pow = 0.9 },
                o);
            return SpiralTrack(merged);
        }

        /// 花瓣轨道（往返扫 + 半径一鼓一收）
        /// 对应 JS sweepTrack({ bulge: 0.18, bulgeN: 5, ...o })
        public static List<Vec3> PetalTrack(TrackSpec o)
        {
            var merged = TrackSpec.Merge(
                new TrackSpec { Bulge = 0.18, BulgeN = 5 },
                o);
            return SweepTrack(merged);
        }

        /// 根据 TrackSpec.Type 分发生成函数
        public static List<Vec3> GenTrackPts(TrackSpec spec)
        {
            switch (spec.Type)
            {
                case "sweep":  return SweepTrack(spec);
                case "spiral": return SpiralTrack(spec);
                case "vortex": return VortexTrack(spec);
                case "petal":  return PetalTrack(spec);
                default:       return SweepTrack(spec);
            }
        }

        /// 双螺旋（DNA）：在基准轨道两侧上下交缠出两条
        public static List<Vec3>[] HelixPair(List<Vec3> basePts, double radius = 0.13, double twists = 9)
        {
            var baseTrack = new Track(basePts);
            int N = 300;
            var a = new List<Vec3>(N + 1);
            var b = new List<Vec3>(N + 1);
            for (int i = 0; i <= N; i++)
            {
                double u   = baseTrack.Slots * i / N;
                var    p   = baseTrack.PointAtU(u);
                var    tg  = baseTrack.TangentAtU(u);
                double sx  = -tg.Z, sz = tg.X;
                double sl  = Math.Sqrt(sx * sx + sz * sz);
                if (sl == 0) sl = 1;
                sx /= sl; sz /= sl;
                double fade = Math.Min(1.0, (double)(N - i) / 40.0);
                double ph   = ((double)i / N) * twists * Math.PI * 2;
                double cv   = Math.Cos(ph) * radius * fade;
                double sv   = Math.Sin(ph) * radius * fade + (1 - fade) * 0.07;
                a.Add(TrackMath.V3(p.X + sx * cv, p.Y + sv,  p.Z + sz * cv));
                b.Add(TrackMath.V3(p.X - sx * cv, p.Y - sv,  p.Z - sz * cv));
            }
            return new[] { a, b };
        }
    }

    // ──────────────────── ForkTrack ────────────────────

    public class ForkTrack : ITrack
    {
        public Track[]  Paths   { get; private set; }  // [0] 短路（危险），[1] 长路（安全）
        public double   ForkU   { get; private set; }  // 岔口在基准轨道上的星位
        public Vec3     ForkPos { get; private set; }  // 岔口世界坐标
        public int      Active  { get; set; } = 0;     // 当前激活的路：0 或 1
        public Vec3     Head    { get; private set; }

        public ForkTrack(TrackSpec spec)
        {
            double center    = spec.Center    ?? 0.0;
            int    startSide = spec.StartSide ?? -1;
            double span      = spec.Span      ?? 170.0;

            double c = center * TrackMath.DEG;

            // 主干：往返扫 1.5 趟，对应 JS param(trunkSpec, theta)（无 approach）
            var trunkSpec = TrackSpec.Merge(spec,
                new TrackSpec { REnd = spec.ForkR ?? 2.2, Pow = 1.0, Steps = 160 });
            var trunk = Track.Param(trunkSpec,
                t => c + startSide * (span / 2) * TrackMath.DEG * Math.Cos(Math.PI * 1.5 * t));

            var f   = trunk[trunk.Count - 1];
            double fr = Math.Sqrt(f.X * f.X + f.Z * f.Z);

            // 支路生成器（闭包捕获 c, startSide, fr, f, spec）
            // 对应 JS: approach(param({...}, theta, rt).slice(1))
            List<Vec3> Branch(int sweeps, double spanB, double yInner)
            {
                var branchSpec = TrackSpec.Merge(spec,
                    new TrackSpec { RStart = fr, REnd = 0.62, YOuter = f.Y, YInner = yInner,
                                    Pow = 1.0, Steps = 220, Wave = 0.0 });
                var bPts = Track.Param(branchSpec,
                    t => c + startSide * (spanB / 2) * TrackMath.DEG * TrackMath.Lerp(1, 0.55, t) * Math.Cos(Math.PI * sweeps * t - Math.PI / 2),
                    t => Math.Min(1, Math.Max(0, t - 0.7 * Math.Sin(2 * Math.PI * sweeps * t) / (2 * Math.PI * sweeps))));
                // .slice(1) — 去掉首点（和主干末尾重叠），然后 approach() 进星核
                var sliced = bPts.GetRange(1, bPts.Count - 1);
                Track.Approach(sliced, 0.0);
                return sliced;
            }

            int   shortSweeps = spec.ShortSweeps ?? 1;
            int   longSweeps  = spec.LongSweeps  ?? 3;
            var shortBranch = Branch(shortSweeps, span * 0.8, 1.28);
            var longBranch  = Branch(longSweeps,  span * 1.1, 1.32);

            var ptsShort = new List<Vec3>(trunk);
            ptsShort.AddRange(shortBranch);
            var ptsLong = new List<Vec3>(trunk);
            ptsLong.AddRange(longBranch);

            Paths = new[] { new Track(ptsShort), new Track(ptsLong) };
            Head    = Paths[0].Head;
            // JS: const t0 = new Track(trunk); this.forkU = t0.slots - 1; this.forkPos = t0.pointAtU(this.forkU);
            // （11-23 比对轨迹时查出：翻译时这里被写成了别的公式，岔口位置差了 35 个星位）
            var t0 = new Track(new List<Vec3>(trunk));
            ForkU   = t0.Slots - 1;
            ForkPos = t0.PointAtU(ForkU);
        }

        private Track Cur => Paths[Active];

        public double   Slots  => Cur.Slots;
        public double   Length => Cur.Length;
        public List<Vec3> Pts  => Cur.Pts;
        public double[] U      => Cur.U_;

        public Vec3   PointAtU(double u)          => Cur.PointAtU(u);
        public Vec3   TangentAtU(double u)         => Cur.TangentAtU(u);
        public double SizeAtU(double u)            => Cur.SizeAtU(u);
        public double UToM(double u)               => Cur.UToM(u);
        public double MToU(double m)               => Cur.MToU(m);
        public double SlotsBackFromEnd(double m)   => Cur.SlotsBackFromEnd(m);
    }

    // ──────────────────── 防穿模检查 ────────────────────

    /// 对应 JS clearance(tracks)，返回问题列表（空 = 通过）
    public static class TrackCheck
    {
        public static List<string> Clearance(List<ITrack> tracks)
        {
            var probs = new List<string>();
            double DEG = Math.PI / 180;

            // 对每条轨道采样
            var samples = new List<List<Sample>>();
            foreach (var tr in tracks)
            {
                var list = new List<Sample>();
                for (double m = 0; m <= tr.Length; m += 0.04)
                {
                    double u = tr.MToU(m);
                    var    p = tr.PointAtU(u);
                    double r = tr.SizeAtU(u) / 2;
                    list.Add(new Sample { M = m, U = u, P = p, R = r, End = tr.Length - m });
                }
                samples.Add(list);
            }

            double D(Vec3 a, Vec3 b)
            {
                double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
                return Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }

            // 收尾角度检查
            for (int ti = 0; ti < tracks.Count; ti++)
            {
                var tr = tracks[ti];
                double endU = Math.Max(0, tr.Slots - tr.MToU(tr.Length) + tr.MToU(Math.Max(0, tr.Length - 0.5)));
                var    e   = tr.PointAtU(endU);
                double ang = Math.Abs(Math.Atan2(e.X, -e.Z)) / DEG;
                if (ang > 75) probs.Add($"轨道{ti} 收尾在身侧 / 身后（{ang:F0}°）");
            }

            for (int ti = 0; ti < tracks.Count; ti++)
            {
                var S = samples[ti];
                foreach (var s in S)
                {
                    if (s.End > 0.45 && D(s.P, LAYOUT.Core) < LAYOUT.CoreRadius + s.R + 0.02)
                        probs.Add($"轨道{ti} 第 {s.M:F2} m 太贴星核");
                    for (int sk = 0; sk < LAYOUT.Stash.Length; sk++)
                        if (D(s.P, LAYOUT.Stash[sk]) < s.R + LAYOUT.StashRadius * 0.6)
                            probs.Add($"轨道{ti} 第 {s.M:F2} m 压到星盘");
                    if (D(s.P, LAYOUT.Head) < 0.35)
                        probs.Add($"轨道{ti} 第 {s.M:F2} m 离头太近");
                    double hd = Math.Sqrt(s.P.X * s.P.X + s.P.Z * s.P.Z);
                    if (hd < 0.3 && s.P.Y < LAYOUT.Head.Y)
                        probs.Add($"轨道{ti} 第 {s.M:F2} m 穿过身体");
                }
                // 自身和其它轨道之间的间距检查
                for (int ui = ti; ui < samples.Count; ui++)
                {
                    var T = samples[ui];
                    for (int i = 0; i < S.Count; i += 2)
                    {
                        for (int j = (ui == ti ? i + 1 : 0); j < T.Count; j += 2)
                        {
                            var a = S[i]; var b = T[j];
                            if (ui == ti && Math.Abs(a.M - b.M) < (a.R + b.R) * 4) continue;
                            if (a.End < 0.4 && b.End < 0.4) continue;
                            if (D(a.P, b.P) < (a.R + b.R) * 1.1)
                            {
                                probs.Add($"轨道{ti}@{a.M:F2}m 与 轨道{ui}@{b.M:F2}m 太近（{D(a.P, b.P):F3} m）");
                                i += 10; break;
                            }
                        }
                    }
                }
            }
            return probs;
        }

        class Sample
        {
            public double M, U, R, End;
            public Vec3 P;
        }
    }
}
