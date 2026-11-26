// 选星（纯逻辑）：对应 JS picker.js。
// 评分 = 射线与星中心夹角 − 星角半径；防跳（hysteresis）+ 捏合回溯（lookback）。

using System;
using System.Collections.Generic;

namespace Starstrung.Logic
{
    // ──────────────────── 候选项 ────────────────────

    public class PickCandidate
    {
        public string Key; // 复合 key，格式 "ci:id" / "ci:gap" / "mouth"
        public Vec3   Pos; // 世界坐标
        public double R;   // 半径（米）
    }

    // ──────────────────── angleTo ────────────────────

    public struct AngleResult
    {
        public double Deg;  // 夹角（度）
        public double Dist; // 到目标的距离（米）
    }

    public static class PickMath
    {
        private const double DEG = 180.0 / Math.PI; // 弧度→度

        /// 对应 JS angleTo(o, d, p)：射线 (o, d) 与点 p 的夹角（度）+ 距离
        public static AngleResult AngleTo(Vec3 o, Vec3 d, Vec3 p)
        {
            double vx = p.X - o.X, vy = p.Y - o.Y, vz = p.Z - o.Z;
            double l  = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (l == 0) l = 1;
            double c = (vx * d.X + vy * d.Y + vz * d.Z) / l;
            c = Math.Max(-1.0, Math.Min(1.0, c));
            return new AngleResult { Deg = Math.Acos(c) * DEG, Dist = l };
        }
    }

    // ──────────────────── Picker ────────────────────

    public class Picker
    {
        private readonly double _coneDeg;
        private readonly double _hysteresisDeg;
        private readonly double _nearTieDeg;
        private readonly double _lookback;
        private readonly double _gazeDeg;
        private readonly double _gazeBonusDeg;

        public string Current  { get; private set; } // 当前高亮的 key（null = 无）
        private readonly List<(double t, string key)> _history = new List<(double, string)>();

        public Picker()
        {
            _coneDeg       = PICK.ConeDeg;
            _hysteresisDeg = PICK.HysteresisDeg;
            _nearTieDeg    = PICK.NearTieDeg;
            _lookback      = PICK.Lookback;
            _gazeDeg       = PICK.GazeDeg;
            _gazeBonusDeg  = PICK.GazeBonusDeg;
        }

        public void Reset()
        {
            Current = null;
            _history.Clear();
        }

        /// 每帧更新：返回当前高亮候选（null = 无）
        /// ray = null 表示无射线；gaze = null 表示无眼动
        public PickCandidate Update(double t, Ray3? ray, List<PickCandidate> candidates, Ray3? gaze = null)
        {
            PickCandidate best = null;
            double bestScore = double.PositiveInfinity;
            double bestDist  = double.PositiveInfinity;
            double curScore  = double.PositiveInfinity; // 当前候选的分数（Infinity = 不在锥内）

            if (ray.HasValue)
            {
                var ro = ray.Value.O;
                var rd = ray.Value.D;
                foreach (var cand in candidates)
                {
                    var a   = PickMath.AngleTo(ro, rd, cand.Pos);
                    // 星的角半径（度）
                    double ang   = Math.Atan2(cand.R, a.Dist) * (180.0 / Math.PI);
                    double score = Math.Max(0, a.Deg - ang);
                    if (gaze.HasValue)
                    {
                        var g = PickMath.AngleTo(gaze.Value.O, gaze.Value.D, cand.Pos);
                        if (g.Deg - ang <= _gazeDeg) score -= _gazeBonusDeg;
                    }
                    // 锥角过滤
                    if (a.Deg - ang > _coneDeg) continue;
                    if (cand.Key == Current) curScore = score;
                    // 选分最低；分差 ≤ nearTieDeg 时选近的
                    if (best == null
                        || score < bestScore - _nearTieDeg
                        || (Math.Abs(score - bestScore) <= _nearTieDeg && a.Dist < bestDist))
                    {
                        best = cand; bestScore = score; bestDist = a.Dist;
                    }
                }
            }

            string key = best?.Key;
            // 防跳：当前候选仍在锥内，且新候选没明显更准 → 保持当前
            if (Current != null
                && curScore < double.PositiveInfinity
                && key != Current
                && bestScore > curScore - _hysteresisDeg)
                key = Current;

            Current = key;
            _history.Add((t, key));
            // 只保留 1 秒内 & 至少 2 条
            while (_history.Count > 2 && _history[0].t < t - 1)
                _history.RemoveAt(0);

            if (key == null) return null;
            return candidates.Find(x => x.Key == key);
        }

        /// 捏合瞬间：取 lookback 秒之前高亮的候选 key
        public string LockedKey(double t)
        {
            double target = t - _lookback;
            string k = Current;
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (_history[i].t <= target)
                {
                    k = _history[i].key ?? Current;
                    break;
                }
            }
            return k;
        }
    }
}
