// 走法枚举（纯逻辑）：教学提示和机器人共用。对应 JS moves.js。
// 只做静态估计（不推演连锁）。

using System.Collections.Generic;

namespace Starstrung.Logic
{
    public static class Moves
    {
        // ──────────────────── pickMatches ────────────────────

        /// 摘第 i 颗后两边合拢能直接消掉的走法：[(i, value)]
        public static List<(int i, int value)> PickMatches(ChainSim sim)
        {
            var st  = sim.Stars;
            var out_ = new List<(int, int)>();
            for (int i = 1; i + 1 < st.Count; i++)
            {
                if (!sim.CanPick(i) || sim.TwinPartnerIndex(i) >= 0) continue;
                if (!sim.Touching(i - 1) || !sim.Touching(i)) continue;
                var a = st[i - 1]; var b = st[i + 1];
                if (a.Color < 0 || a.Color != b.Color) continue;
                int run = sim.RunLeft(i - 1) + sim.RunRight(i + 1);
                if (run >= sim.Cfg.MinMatch) out_.Add((i, run));
            }
            return out_;
        }

        // ──────────────────── insertMatches ────────────────────

        /// 把 count 颗 color 色星塞进各缝隙能凑成的同色段长度：[(gap, value)]（只返回 ≥ minMatch）
        public static List<(int gap, int value)> InsertMatches(ChainSim sim, int color, int count = 1)
        {
            var st   = sim.Stars;
            int n    = st.Count;
            var out_ = new List<(int, int)>();
            for (int gap = 0; gap <= n; gap++)
            {
                // 跳过相邻两颗 s<0 的缝隙（星还没进轨道）
                if (gap < n && st[gap].S < 0 && (gap == 0 || st[gap - 1].S < 0)) continue;
                int run = count;
                if (color < 0)
                {
                    // 彩星：解析为两边较长段的颜色
                    int rc = sim.ResolveRainbow(gap);
                    int l  = (gap > 0 && st[gap - 1].Color == rc) ? sim.RunLeft(gap - 1) : 0;
                    int r  = (gap < n && st[gap].Color == rc && (gap == 0 || sim.Touching(gap - 1))) ? sim.RunRight(gap) : 0;
                    if (rc >= 0 && count + l + r >= sim.Cfg.MinMatch)
                        out_.Add((gap, count + l + r));
                    continue;
                }
                if (gap > 0 && st[gap - 1].Color == color) run += sim.RunLeft(gap - 1);
                // 断开缺口时，新星碰不到后段
                if (gap < n && st[gap].Color == color && (gap == 0 || sim.Touching(gap - 1)))
                    run += sim.RunRight(gap);
                if (run >= sim.Cfg.MinMatch) out_.Add((gap, run));
            }
            return out_;
        }

        // ──────────────────── loneStars ────────────────────

        /// 某颜色是"孤星"（两边都不是同色）的可摘下标列表
        public static List<int> LoneStars(ChainSim sim, int color)
        {
            var st   = sim.Stars;
            var out_ = new List<int>();
            for (int i = 0; i < st.Count; i++)
            {
                if (st[i].Color != color || !sim.CanPick(i)) continue;
                bool l = i > 0     && sim.Touching(i - 1) && st[i - 1].Color == color;
                bool r = i + 1 < st.Count && sim.Touching(i)     && st[i + 1].Color == color;
                if (!l && !r) out_.Add(i);
            }
            return out_;
        }
    }
}
