// 对照轨迹（纯逻辑）：对应 JS tools/trace.mjs。
// 格式：每秒一行 "秒 | count/s/score,... | 裂纹 | 手/盘/飞回 | 状态"
// 末行："END 状态 时间.3f actions=动作数"
// 与 node tools/trace.mjs <level> <secs> <react> <vision> <seed> 逐行比对。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Starstrung.Logic
{
    public static class Trace
    {
        public static string Run(
            string levelId = "1-1",
            double secs    = 60,
            double react   = 1.5,
            double vision  = 0.6,
            int    seed    = 7)
        {
            var level = Levels.LevelById(levelId)
                ?? throw new ArgumentException($"关卡 {levelId} 不存在");
            var game  = new Game(level);
            var bot   = new Bot(game, new BotOpts { React = react, Vision = vision, Seed = seed });

            const double dt = 1.0 / 30;
            double t   = 0;
            int    next = 1;
            var    sb  = new StringBuilder();

            while (t < secs - 1e-9 && game.Status == "playing")
            {
                bot.Update(dt);
                game.Update(dt, null);
                game.DrainEvents();
                t += dt;

                if (t >= next - 1e-9)
                {
                    // 每条串：count/headS.3f/score
                    var chParts = new List<string>();
                    foreach (var c in game.Chains)
                    {
                        double headS = c.Sim.Stars.Count > 0 ? c.Sim.Stars[0].S : 0.0;
                        chParts.Add($"{c.Sim.Count}/{headS.ToString("F3", CultureInfo.InvariantCulture)}/{c.Sim.Score}");
                    }
                    string ch = string.Join(",", chParts);

                    // 手里/星盘/飞回
                    int heldCount     = game.HeldStars().Count;
                    int stashCount    = 0;
                    foreach (var s in game.Stash) if (s != null) stashCount++;
                    int returningCount = game.Returning.Count;

                    sb.AppendLine($"{next} | {ch} | {game.Cracks} | {heldCount}/{stashCount}/{returningCount} | {game.Status}");
                    next++;
                }
            }

            // 末行
            sb.Append($"END {game.Status} {game.Time.ToString("F3", CultureInfo.InvariantCulture)} actions={bot.Actions}");
            return sb.ToString();
        }
    }
}
