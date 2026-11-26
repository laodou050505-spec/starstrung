// Boss 吞星兽（纯逻辑）：对应 JS boss.js。
// 内置 xorshift32 随机（rs=12345），与 Rng 类无关。

using System;
using System.Collections.Generic;

namespace Starstrung.Logic
{
    // ──────────────────── 常量 ────────────────────

    public class BossCfg
    {
        public int    Hp           = 3;
        public double Gap          = 1.5;    // 兽头中心在串头前面几个星位
        public double Length_      = 2.5;    // 撞星核时计入兽头前沿
        public double SpeedScale   = 1.2;
        public double ChewEvery    = 8;
        public double ChewWarn     = 1.5;
        public int    ChewCount    = 2;
        public double OpenEvery    = 10;
        public double OpenTime     = 3.5;
        public double OpenWarn     = 1.0;
        public double FeedKnockback= 12;
    }

    // ──────────────────── Boss ────────────────────

    public class Boss
    {
        public  BossCfg Cfg;
        private Game    _game;
        public  int     Ci;
        public  int     Hp;
        public  bool    Alive    = true;
        public  string  State    = "idle"; // idle / chewWarn / openWarn / open / hurt
        public  double  T        = 0;
        public  double  ChewT;
        public  double  OpenT;
        public  int     MouthColor = -1;
        private uint    _rs = 12345u; // 内置 xorshift32

        public Boss(Game game, int ci, BossCfg cfg = null)
        {
            Cfg   = cfg ?? new BossCfg();
            _game = game;
            Ci    = ci;
            Hp    = Cfg.Hp;
            ChewT = Cfg.ChewEvery;
            OpenT = Cfg.OpenEvery * 0.6;
        }

        ChainInfo SimInfo => _game.Chains[Ci];
        ChainSim  Sim     => SimInfo.Sim;

        // ──────────────────── 内置随机 ────────────────────

        /// xorshift32，对应 JS: this.rs ^= ...; this.rs >>>= 0; return this.rs % n
        int Rand(int n)
        {
            unchecked
            {
                _rs ^= _rs << 13;
                _rs ^= _rs >> 17;
                _rs ^= _rs << 5;
            }
            return (int)(_rs % (uint)n);
        }

        // ──────────────────── 属性 ────────────────────

        /// 兽头所在的星位（串头星位 + gap）
        public double U
        {
            get
            {
                var stars = Sim.Stars;
                var h = stars.Count > 0 ? stars[0] : null;
                return h != null ? h.S + Cfg.Gap : 0;
            }
        }

        public bool MouthOpen => Alive && State == "open";

        // ──────────────────── 更新 ────────────────────

        public void Update(double dt)
        {
            var sim = Sim;
            var c   = Cfg;
            if (!Alive) return;
            if (sim.Count == 0) { Alive = false; sim.HeadOffset = 0; return; }
            sim.HeadOffset  = c.Length_;
            sim.SpeedScale  = c.SpeedScale;
            if (sim.Stars[0].S < 0) return; // 还没进轨道

            T += dt;

            // ── 嚼星 ──
            ChewT -= dt;
            if (State == "idle" && ChewT <= c.ChewWarn)
            {
                State = "chewWarn";
                _game.Emit(new GameEvent { Type = "bossChewWarn", Ci = Ci });
            }
            if (State == "chewWarn" && ChewT <= 0)
            {
                State = "idle"; ChewT = c.ChewEvery;
                int colors = _game.Level.Colors;
                var ids = new List<int>();
                for (int k = 0; k < c.ChewCount && k < sim.Count; k++)
                {
                    var st = sim.Stars[k];
                    if (st.Color < 0) continue;
                    st.Color = (st.Color + 1 + Rand(colors - 1)) % colors;
                    ids.Add(st.Id);
                }
                _game.Emit(new GameEvent { Type = "bossChew", Ci = Ci, Ids = ids });
                // 吐出来正好凑成 3 连的，照常消
                sim.TryMatchAt(0, 1);
            }

            // ── 张嘴 ──
            if (State == "idle")
            {
                OpenT -= dt;
                if (OpenT <= c.OpenWarn)
                {
                    int mc = PickColor();
                    if (mc >= 0)
                    {
                        MouthColor = mc;
                        State = "openWarn";
                        _game.Emit(new GameEvent { Type = "bossOpenWarn", Ci = Ci, Color = MouthColor });
                    }
                    else OpenT = c.OpenEvery;
                }
            }
            else if (State == "openWarn")
            {
                OpenT -= dt;
                if (OpenT <= 0)
                {
                    State = "open"; OpenT = c.OpenTime;
                    _game.Emit(new GameEvent { Type = "bossOpen", Ci = Ci, Color = MouthColor });
                }
            }
            else if (State == "open")
            {
                OpenT -= dt;
                if (OpenT <= 0) Close();
            }
        }

        void Close()
        {
            State = "idle"; OpenT = Cfg.OpenEvery;
            _game.Emit(new GameEvent { Type = "bossClose", Ci = Ci });
        }

        /// 张嘴要的颜色：玩家手里有的优先，否则串里能摘到的
        int PickColor()
        {
            var held = _game.HeldStars();
            var heldColors = new List<int>();
            foreach (var s in held) if (s.Color >= 0) heldColors.Add(s.Color);
            if (heldColors.Count > 0) return heldColors[Rand(heldColors.Count)];
            var sim = Sim;
            var cand = new List<int>();
            for (int i = 0; i < sim.Count; i++)
                if (sim.CanPick(i) && sim.Stars[i].Color >= 0) cand.Add(sim.Stars[i].Color);
            return cand.Count > 0 ? cand[Rand(cand.Count)] : -1;
        }

        /// 喂星：成功返回 true；失败由 Game 把星送回串尾
        public bool Feed(List<Star> stars)
        {
            if (!MouthOpen || stars.Count != 1 || stars[0].Color != MouthColor)
            {
                _game.Emit(new GameEvent { Type = "bossReject", Ci = Ci });
                return false;
            }
            Hp--;
            Sim.Retreat += Cfg.FeedKnockback;
            _game.Emit(new GameEvent { Type = "bossHurt", Ci = Ci, Hp = Hp, Color = MouthColor });
            Close();
            if (Hp <= 0)
            {
                Alive = false;
                Sim.HeadOffset = 0;
                Sim.SpeedScale = 1;
                _game.Emit(new GameEvent { Type = "bossDefeated", Ci = Ci, U = U });
            }
            return true;
        }
    }
}
