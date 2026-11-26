// 批量模拟（纯逻辑）：对应 JS tools/sim.mjs --summary 模式。
// 对每一关运行 seeds 次，输出难度汇总表。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Starstrung.Logic
{
    public static class Sim
    {
        public static string Summary(double react = 0.6, double vision = 1, int seeds = 1)
        {
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;

            sb.AppendLine($"反应间隔 {react.ToString(ci)}s，视野 {vision.ToString(ci)}，每关 {seeds} 个种子");

            // 聚合数据（保持 LEVELS 插入顺序）
            var aggOrder = new List<string>();
            var aggMap   = new Dictionary<string, AggData>();

            foreach (var L in Levels.LEVELS)
            {
                if (L == null) continue;

                for (int k = 0; k < seeds; k++)
                {
                    // 临时覆盖种子（单线程安全）
                    int savedSeed = L.Seed;
                    L.Seed = (L.Seed != 0 ? L.Seed : 1) + k * 1000;
                    var game = new Game(L);
                    L.Seed = savedSeed;

                    var bot = new Bot(game, new BotOpts { React = react, Vision = vision, Seed = 7 + k });
                    const double dt = 1.0 / 30;
                    double closest = 1;

                    while (game.Status == "playing" && game.Time < 900)
                    {
                        bot.Update(dt);
                        game.Update(dt, null);
                        foreach (var c in game.Chains)
                        {
                            var h = c.Sim.Stars.Count > 0 ? c.Sim.Stars[0] : null;
                            if (h != null && h.S > 0)
                                closest = Math.Min(closest,
                                    Math.Max(0, (c.Sim.Cfg.TrackLength - h.S - c.Sim.HeadOffset)
                                                 / c.Sim.Cfg.TrackLength));
                        }
                        game.DrainEvents(); // 清空（彩星在 Trace 里计，Summary 不需要）
                    }

                    int maxCombo = 0;
                    foreach (var c in game.Chains) maxCombo = Math.Max(maxCombo, c.Sim.MaxCombo);

                    if (!aggMap.TryGetValue(L.Id, out var a))
                    {
                        a = new AggData { Name = L.Name?.Zh ?? L.Id };
                        aggMap[L.Id] = a;
                        aggOrder.Add(L.Id);
                    }
                    a.N++;
                    if (game.Status == "won") a.Win++;
                    a.Cracks += game.Cracks;
                    a.Time   += game.Time;
                    a.Combo  += maxCombo;
                    a.Close  += closest;
                }
            }

            // 汇总表头
            sb.AppendLine("关    胜率   平均裂纹  平均用时  平均连锁  最险时离星核  难度分");

            foreach (var id in aggOrder)
            {
                var a     = aggMap[id];
                double close = a.Close / a.N;
                double diff  = (1 - (double)a.Win / a.N) * 3
                             + (double)a.Cracks / a.N
                             + Math.Max(0, 1 - close * 2);

                string row =
                    PadEnd(id, 6)
                    + PadEnd($"{a.Win}/{a.N}", 7)
                    + PadEnd(((double)a.Cracks / a.N).ToString("F1", ci), 10)
                    + PadEnd(((double)a.Time   / a.N).ToString("F0", ci) + "s", 10)
                    + PadEnd(((double)a.Combo  / a.N).ToString("F1", ci), 10)
                    + PadEnd(((close * 100).ToString("F0", ci) + "%"), 12)
                    + diff.ToString("F2", ci) + "  " + new string('█', (int)Math.Round(diff * 3));

                sb.AppendLine(row);
            }

            return sb.ToString();
        }

        static string PadEnd(string s, int n) => s.PadRight(n);

        class AggData
        {
            public string Name;
            public int    N, Win, Cracks, Combo;
            public double Time, Close;
        }
    }
}
