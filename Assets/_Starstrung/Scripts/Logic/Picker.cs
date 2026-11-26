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
        /// 真机眼手协同策略：眼睛精准看着某候选时优先锁定所看目标（彻底消除双眼视差导致“看 A 却显示 B”）；
        /// 眼睛未看向任何候选（或无眼动）时，无缝回退到零视差手部射线选星。
        public PickCandidate Update(double t, Ray3? ray, List<PickCandidate> candidates, Ray3? gaze = null)
        {
            PickCandidate best = null;
            double bestScore = double.PositiveInfinity;
            double bestDist  = double.PositiveInfinity;
            double curScore  = double.PositiveInfinity; // 当前候选的分数（Infinity = 不在锥内）

            // 1) 眼动优先锁定：头显眼动有效且直接注视到某个候选（夹角扣减星角半径 <= _gazeDeg）
            if (gaze.HasValue && candidates != null && candidates.Count > 0)
            {
                var go = gaze.Value.O;
                var gd = gaze.Value.D;
                PickCandidate gazeBest = null;
                double gazeBestScore = double.PositiveInfinity;
                double gazeBestDist = double.PositiveInfinity;
                double gazeCurScore = double.PositiveInfinity;

                foreach (var cand in candidates)
                {
                    var g = PickMath.AngleTo(go, gd, cand.Pos);
                    double ang = Math.Atan2(cand.R, g.Dist) * (180.0 / Math.PI);
                    double gOff = Math.Max(0, g.Deg - ang);
                    if (gOff > _gazeDeg) continue;

                    // 眼动误差为主分；当两颗星视线几乎重叠时，微弱参考手部射线距离做决胜
                    double score = gOff;
                    if (ray.HasValue)
                    {
                        var r = PickMath.AngleTo(ray.Value.O, ray.Value.D, cand.Pos);
                        double rOff = Math.Max(0, r.Deg - ang);
                        score += Math.Min(rOff, 12.0) * 0.04;
                    }

                    if (cand.Key == Current) gazeCurScore = score;
                    if (gazeBest == null
                        || score < gazeBestScore - 0.35
                        || (Math.Abs(score - gazeBestScore) <= 0.35 && g.Dist < gazeBestDist))
                    {
                        gazeBest = cand;
                        gazeBestScore = score;
                        gazeBestDist = g.Dist;
                    }
                }

                if (gazeBest != null)
                {
                    best = gazeBest;
                    bestScore = gazeBestScore;
                    curScore = gazeCurScore;
                }
            }

            // 2) 眼睛未注视任何候选（或无眼动）时，回退到零视差手部射线
            if (best == null && ray.HasValue)
            {
                var ro = ray.Value.O;
                var rd = ray.Value.D;
                foreach (var cand in candidates)
                {
                    var a   = PickMath.AngleTo(ro, rd, cand.Pos);
                    // 星的角半径（度）
                    double ang   = Math.Atan2(cand.R, a.Dist) * (180.0 / Math.PI);
                    double score = Math.Max(0, a.Deg - ang);
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
            // 防跳：当前候选仍在有效锥内，且新候选没明显更准 → 保持当前（眼动切星用更灵敏的滞回，手部射线用原滞回）
            double hyst = gaze.HasValue && best != null ? Math.Min(_hysteresisDeg, 1.0) : _hysteresisDeg;
            if (Current != null
                && curScore < double.PositiveInfinity
                && key != Current
                && bestScore > curScore - hyst)
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
