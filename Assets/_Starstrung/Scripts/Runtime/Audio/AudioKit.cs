using System.Collections.Generic;
using Starstrung.Logic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 声音（全部程序合成，没有版权问题；来源见 Audio/CREDITS.md）。基调：宏大的星空感（11-24 用户）。
    /// - 背景音乐两首：封面 / 选关用 title_theme（合唱垫音 + 弦乐 + 钢片琴主旋律 + 竖琴刮奏），关卡里用 play_theme（竖琴 / 钢片琴分解和弦），切换时交叉淡入淡出；
    /// - 消除：运行时合成的钟琴（基频 + 近谐波泛音指数衰减），按 G 五声音阶一路往上走，连锁越长音越高，连锁 ≥2 叠五度、≥3 叠八度并加一声合唱；
    /// - 事件音效：界面指着 / 捏下、开始、进关、撞星核、过关、失败、时空裂缝打开、星星飞进星核（Tools/make_sfx.py 合成，自带长混响）；
    /// - 钟琴和短音效过一道音乐厅混响（AudioReverbFilter），音乐在单独的物体上不过混响；
    /// - 音量：音乐 / 音效各四档（关 / 低 / 中 / 高），存 PlayerPrefs。
    /// </summary>
    public class AudioKit : MonoBehaviour
    {
        public GameRunner runner;
        public AppFlow flow;
        public AudioClip titleMusic, playMusic;
        /// <summary>星域变奏（11-25）：霜川 / 焰云 / 深空（序章、晨曦用 playMusic；无尽关用深空）</summary>
        public AudioClip[] zoneMusic;
        /// <summary>危险层（11-25 紧张感）：串头逼近星核时推大的低音脉动</summary>
        public AudioClip dangerLayer;
        /// <summary>结局专属音乐（11-25）：从星在玩家手里炸开那一刻起放，不循环</summary>
        public AudioClip endingMusic;
        AudioSource musE;
        bool silent;
        /// <summary>寂静（结局碎裂前的一秒）：所有声音立刻停，音乐瞬间归零；关掉后再按各自的规则淡回来</summary>
        public void Silence(bool on)
        {
            silent = on;
            if (!on) return;
            foreach (var s in pool) s.Stop();
            foreach (var s in pool3D) s.Stop();
            musA.volume = musB.volume = 0f; if (musD) musD.volume = 0f; dangerV = 0f;
        }
        /// <summary>结局曲从头放</summary>
        public void StartEnding() { if (musE && endingMusic) { musE.clip = endingMusic; musE.volume = Vol(MusicLevel) * 0.75f; musE.Play(); endingStarted = true; } }
        bool endingStarted;
        public AudioClip hover, pick, begin, levelStart, coreHit, win, lose, riftOpen, choirSwell, flyIn;
        const int SR = 44100;
        static readonly int[] Penta = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24, 26, 28, 31 };
        const float Base = 392f;   // G4

        public static int MusicLevel { get => PlayerPrefs.GetInt("starstrung.music", 2); set { PlayerPrefs.SetInt("starstrung.music", value); PlayerPrefs.Save(); } }
        public static int SfxLevel { get => PlayerPrefs.GetInt("starstrung.sfx", 3); set { PlayerPrefs.SetInt("starstrung.sfx", value); PlayerPrefs.Save(); } }
        static float Vol(int lv) => lv <= 0 ? 0f : lv == 1 ? 0.3f : lv == 2 ? 0.6f : 1f;

        readonly Dictionary<int, AudioClip> bells = new();
        readonly List<AudioSource> pool = new();
        readonly List<AudioSource> pool3D = new();   // 带方位的（消除、摘放、撞核、Boss：声音从事发的方向传来）
        int next, next3D;
        AudioSource musA, musB; bool aIsTitle = true; float musicMix = 0f;   // 0 = 封面曲，1 = 关卡曲
        AudioSource musD; float dangerV;   // 危险层
        AudioClip wantPlay;                 // 这一关该放的关卡曲（换星域时先淡出旧的再换）
        int step; float lastElim = -10f, lastHover = -10f;
        bool paused;
        float heartT;   // 紧张感（11-24 用户）：串头逼近星核时心跳越来越快
        /// <summary>暂停：音乐压低到三成、音效不再响</summary>
        public void SetPaused(bool on) => paused = on;
        public static AudioKit Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            var sfxGo = new GameObject("Sfx"); sfxGo.transform.SetParent(transform, false);
            for (int i = 0; i < 14; i++)
            {
                var s = sfxGo.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = 0f; s.dopplerLevel = 0;
                pool.Add(s);
            }
            // 空间音效（11-24 用户：声音要考虑方向位置）：spatialBlend 0.85 留一点直达声保证清楚，线性衰减到 12 m
            var sfx3D = new GameObject("Sfx3D"); sfx3D.transform.SetParent(transform, false);
            for (int i = 0; i < 12; i++)
            {
                var s3 = sfx3D.AddComponent<AudioSource>();
                s3.playOnAwake = false; s3.spatialBlend = 0.85f; s3.dopplerLevel = 0f; s3.spread = 35f;
                s3.rolloffMode = AudioRolloffMode.Linear; s3.minDistance = 0.6f; s3.maxDistance = 12f;
                pool3D.Add(s3);
            }
            var rv3 = sfx3D.AddComponent<AudioReverbFilter>();
            rv3.reverbPreset = AudioReverbPreset.User;
            rv3.dryLevel = 0; rv3.room = -1200; rv3.roomHF = -900; rv3.decayTime = 3.2f; rv3.decayHFRatio = 0.6f;
            rv3.reflectionsLevel = -1500; rv3.reverbLevel = -600; rv3.reverbDelay = 0.03f; rv3.diffusion = 100; rv3.density = 100;

            // 音乐厅混响：让钟琴有星空里回荡的尾音（合成的音效已自带混响，这里只加一点）
            var rv = sfxGo.AddComponent<AudioReverbFilter>();
            rv.reverbPreset = AudioReverbPreset.User;
            rv.dryLevel = 0; rv.room = -1100; rv.roomHF = -900; rv.decayTime = 3.6f; rv.decayHFRatio = 0.6f;
            rv.reflectionsLevel = -1400; rv.reverbLevel = -500; rv.reverbDelay = 0.03f; rv.diffusion = 100; rv.density = 100;

            var musGo = new GameObject("Music"); musGo.transform.SetParent(transform, false);
            musA = musGo.AddComponent<AudioSource>(); musB = musGo.AddComponent<AudioSource>();
            foreach (var m in new[] { musA, musB }) { m.loop = true; m.playOnAwake = false; m.spatialBlend = 0; m.volume = 0; }
            musA.clip = titleMusic; musB.clip = playMusic;
            musE = musGo.AddComponent<AudioSource>(); musE.loop = false; musE.playOnAwake = false; musE.spatialBlend = 0; musE.volume = 0;
            musD = musGo.AddComponent<AudioSource>(); musD.loop = true; musD.playOnAwake = false; musD.spatialBlend = 0; musD.volume = 0; musD.clip = dangerLayer;
        }

        void Start()
        {
            if (titleMusic) musA.Play();
            if (playMusic) musB.Play();
            if (dangerLayer) musD.Play();
            if (runner) runner.OnLevelStart += PickZoneMusic;
            if (runner) runner.OnEvent += OnEvent;
            var picker = runner ? runner.GetComponent<MenuPicker>() : null;
            if (picker) picker.Picked += _ => PlayClip(pick, 0.7f);
        }

        void Update()
        {
            // 关卡里切到关卡曲，其它时候封面曲；交叉淡入淡出
            bool inPlay = flow && (flow.Current == AppFlow.State.Play);
            musicMix = Mathf.MoveTowards(musicMix, inPlay ? 1f : 0f, Time.deltaTime / 2.5f);
            float mv = Vol(MusicLevel) * 0.6f * (paused ? 0.3f : 1f);
            // 结局：整段结局里封面曲 / 关卡曲都让开（蓄力时淡出），结局曲放完后封面曲再慢慢回来；寂静时一切归零
            bool inEnding = flow && flow.Current == AppFlow.State.Ending;
            if (!inEnding) endingStarted = false;
            bool endingDone = endingStarted && musE && !musE.isPlaying;
            if (silent || (inEnding && !endingDone)) mv = 0f;
            if (musE && !silent && endingStarted && inEnding) musE.volume = Mathf.MoveTowards(musE.volume, Vol(MusicLevel) * 0.75f, Time.deltaTime * 0.5f);
            else if (musE) musE.volume = Mathf.MoveTowards(musE.volume, 0f, Time.deltaTime * (silent ? 10f : 0.25f));
            musA.volume = Mathf.MoveTowards(musA.volume, mv * Mathf.Cos(musicMix * Mathf.PI * 0.5f), Time.deltaTime * (inEnding && !endingDone ? 0.25f : 0.3f));
            // 换星域：旧曲先淡到底，再换新曲从头放
            bool swapping = wantPlay && musB.clip != wantPlay;
            if (swapping && musB.volume < 0.01f) { musB.clip = wantPlay; musB.Play(); swapping = false; }
            float playV = swapping ? 0f : mv * Mathf.Sin(musicMix * Mathf.PI * 0.5f) * (1f - 0.3f * dangerV);
            musB.volume = Mathf.MoveTowards(musB.volume, playV, Time.deltaTime * (swapping ? 0.6f : 0.3f));
            // 危险层：串头过路线 55% 开始渐响，到星核前最响（暂停 / 不在关卡里收掉）
            float dWant = 0f;
            // 心跳：串头走到路线 60% 之后开始，越近越快（0.9 秒 → 0.32 秒一拍），两声一拍
            var g = runner ? runner.Game : null;
            if (g != null && !paused && g.Status == "playing")
            {
                float danger = 0f;
                foreach (var c in g.Chains)
                {
                    if (c.Sim.Count == 0) continue;
                    float d = (float)(c.Sim.Stars[0].S / System.Math.Max(1.0, c.Track.Slots));
                    if (d > danger) danger = d;
                }
                dWant = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.95f, danger));
                if (danger > 0.6f)
                {
                    float k = Mathf.Clamp01((danger - 0.6f) / 0.4f);
                    float period = Mathf.Lerp(0.9f, 0.32f, k);
                    heartT += Time.deltaTime;
                    if (heartT >= period)
                    {
                        heartT = 0f;
                        float gain = 0.12f + 0.3f * k;
                        Play(-24, gain, 0.45f);
                        Play(-29, gain * 0.7f, 0.5f, 0.1f);
                    }
                }
                else heartT = 0f;
            }

            dangerV = Mathf.MoveTowards(dangerV, inPlay && !paused ? dWant : 0f, Time.deltaTime * (dWant > dangerV ? 0.5f : 0.25f));
            if (musD) musD.volume = Vol(MusicLevel) * 0.55f * dangerV;

            // 界面：指着一个新东西时轻轻一声
            foreach (var t in PickTarget.All)
                if (t.hovered && t.hover < 0.12f && Time.time - lastHover > 0.12f) { PlayClip(hover, 0.35f); lastHover = Time.time; break; }
        }

        void PickZoneMusic()
        {
            var g = runner ? runner.Game : null;
            if (g == null) return;
            int z = g.Level.Endless ? 4 : g.Level.Zone;
            wantPlay = z >= 2 && zoneMusic != null && zoneMusic.Length >= z - 1 && zoneMusic[z - 2] ? zoneMusic[z - 2] : playMusic;
        }

        // ───── 播放 ─────
        AudioSource Next() { var s = pool[next]; next = (next + 1) % pool.Count; return s; }

        AudioSource Next3D(Vector3 at) { var s = pool3D[next3D]; next3D = (next3D + 1) % pool3D.Count; s.transform.position = at; return s; }

        /// <summary>带方位的钟琴：玩家听得出串头在身后哪一侧</summary>
        public void PlayAt(Vector3 at, float semis, float gain, float decay = 1.6f, float delay = 0f)
        {
            float v = Vol(SfxLevel); if (v <= 0 || paused || silent || pool3D.Count == 0) return;
            var s = Next3D(at); s.clip = Bell(semis, decay); s.volume = gain * v; s.pitch = 1f;
            if (delay > 0) s.PlayDelayed(delay); else s.Play();
        }

        /// <summary>带方位的音效片段</summary>
        public void PlayClipAt(Vector3 at, AudioClip c, float gain, float delay = 0f, float pitch = 1f)
        {
            float v = Vol(SfxLevel); if (v <= 0 || !c || paused || silent || pool3D.Count == 0) return;
            var s = Next3D(at); s.clip = c; s.volume = gain * v; s.pitch = pitch;
            if (delay > 0) s.PlayDelayed(delay); else s.Play();
        }

        public void PlayClip(AudioClip c, float gain, float delay = 0f, float pitch = 1f)
        {
            float v = Vol(SfxLevel); if (v <= 0 || !c || paused || silent) return;
            var s = Next(); s.clip = c; s.volume = gain * v; s.pitch = pitch;
            if (delay > 0) s.PlayDelayed(delay); else s.Play();
        }

        AudioClip Bell(float semis, float decay)
        {
            int key = Mathf.RoundToInt(semis * 10) * 100 + Mathf.RoundToInt(decay * 10);
            if (bells.TryGetValue(key, out var c)) return c;
            float f = Base * Mathf.Pow(2f, semis / 12f);
            int n = (int)(SR * (decay + 0.1f));
            var data = new float[n];
            // 钟琴：近谐波泛音（明亮、像水晶），高泛音衰减得更快
            (float mul, float g, float dk)[] parts = { (1f, 1f, 1f), (2.01f, 0.45f, 0.6f), (3f, 0.18f, 0.4f), (4.17f, 0.12f, 0.3f), (5.43f, 0.06f, 0.2f) };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR, s = 0;
                foreach (var (mul, g, dk) in parts) s += g * Mathf.Sin(2f * Mathf.PI * f * mul * t + mul) * Mathf.Exp(-t / (decay * dk / 5f));
                data[i] = s * Mathf.Min(1f, t / 0.003f) * 0.45f;
            }
            c = AudioClip.Create($"bell_{key}", n, 1, SR, false);
            c.SetData(data, 0);
            bells[key] = c;
            return c;
        }

        public void Play(float semis, float gain, float decay = 1.6f, float delay = 0f)
        {
            float v = Vol(SfxLevel); if (v <= 0 || silent) return;
            var s = Next();
            s.clip = Bell(semis, decay); s.volume = gain * v; s.pitch = 1f;
            if (delay > 0) s.PlayDelayed(delay); else s.Play();
        }

        public static void Tick(float semis = 17f, float gain = 0.35f) { if (Instance) Instance.Play(semis, gain, 0.4f); }

        /// <summary>消除：五声音阶一路往上走</summary>
        public void Eliminate(int combo, int count, Vector3? at = null)
        {
            float now = Time.time;
            if (combo <= 1 && now - lastElim > 2.5f) step = 0;
            lastElim = now;
            int idx = Mathf.Min(Penta.Length - 1, step + (combo - 1) * 2);
            step = Mathf.Min(Penta.Length - 3, step + 1);
            float semi = Penta[idx];
            void Bell1(float s, float g, float d, float dl = 0f) { if (at.HasValue) PlayAt(at.Value, s, g, d, dl); else Play(s, g, d, dl); }
            Bell1(semi, 0.55f + Mathf.Min(0.3f, count * 0.04f), 2.0f);
            if (combo >= 2) Bell1(semi + 7, 0.3f, 1.8f, 0.07f);
            if (combo >= 3) { Bell1(semi + 12, 0.25f, 1.8f, 0.14f); PlayClip(choirSwell, 0.35f + Mathf.Min(0.3f, (combo - 3) * 0.1f), 0.05f); }
        }

        /// <summary>事件发生的世界位置（取不到就返回 null，退回无方位播放）</summary>
        Vector3? At(GameEvent e)
        {
            if (!runner || runner.Game == null) return null;
            if (e.Pos.HasValue) return runner.ToWorld(e.Pos.Value);
            if (e.Stars != null && e.Stars.Count > 0 && e.Ci >= 0 && e.Ci < runner.Game.Chains.Count)
                return runner.ToWorld(runner.Game.Chains[e.Ci].Track.PointAtU(System.Math.Max(0, e.Stars[0].S)));
            return null;
        }
        void PlayAtOr(Vector3? at, float semis, float gain, float decay)
        { if (at.HasValue) PlayAt(at.Value, semis, gain, decay); else Play(semis, gain, decay); }
        Vector3 BossPos()
        {
            var g = runner ? runner.Game : null;
            if (g?.Boss == null) return runner ? runner.ToWorld(Logic.LAYOUT.Core) : Vector3.zero;
            var tr = g.Tracks[g.Boss.Ci];
            return runner.ToWorld(tr.PointAtU(System.Math.Min(tr.Slots, g.Boss.U)));
        }

        void OnEvent(GameEvent e)
        {
            switch (e.Type)
            {
                case "eliminated": Eliminate(Mathf.Max(1, e.Combo), e.Stars != null ? e.Stars.Count : 3, At(e)); break;
                case "grabbed": PlayAtOr(At(e), Penta[7] + 2, 0.2f, 0.5f); break;
                case "placed": case "stashed": case "unstash": PlayAtOr(At(e), Penta[5] + 2, 0.2f, 0.5f); break;
                case "coreHit": PlayClipAt(runner.ToWorld(Logic.LAYOUT.Core), coreHit, 0.85f); break;
                case "rainbowJoined": Play(Penta[10], 0.18f, 0.6f); break;
                case "forkSwitch": Play(e.Auto ? Penta[3] : Penta[6], 0.22f, 0.6f); break;
                case "forkTaken": Play(Penta[2], 0.28f, 0.8f); break;
                case "bossChew": PlayClipAt(BossPos(), coreHit, 0.35f, 0, 1.5f); break;
                case "bossOpen": PlayAt(BossPos(), Penta[5], 0.35f, 1.0f); break;
                case "bossReject": PlayAt(BossPos(), -14, 0.45f, 0.6f); break;
                case "bossHurt": Eliminate(3, 6, BossPos()); break;
                case "bossDefeated": PlayClip(win, 0.8f); break;
                case "stage": if (e.Stage > 0) PlayClip(choirSwell, 0.5f); break;
                case "won": PlayClip(win, 0.85f); break;
                case "lost": PlayClip(lose, 0.75f); break;
            }
        }
    }
}
