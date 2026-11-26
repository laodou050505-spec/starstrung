using System;
using System.Collections.Generic;
using Starstrung.Logic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>存档：通关的关卡（时空裂缝用同一份）+ 每关最好星级。</summary>
    public static class Progress
    {
        public static bool Cleared(string id) => RiftView.Cleared().Contains(id);
        public static int BestStars(string id) => PlayerPrefs.GetInt("starstrung.stars." + id, 0);
        public static void Record(string id, int stars)
        {
            RiftView.MarkCleared(id);
            if (stars > BestStars(id)) { PlayerPrefs.SetInt("starstrung.stars." + id, stars); PlayerPrefs.Save(); }
        }
        public static int Index(string id) { for (int i = 0; i < Levels.LEVELS.Length; i++) if (Levels.LEVELS[i].Id == id) return i; return -1; }
        /// <summary>第 i 关能不能玩：序章永远能；之后要前一关通过</summary>
        public static bool Unlocked(int i)
        {
            if (AppFlow.UnlockAll) return true;
            if (i <= 0) return true;
            if (Levels.LEVELS[i].Endless) return Cleared("4-5");       // 无尽：打完吞星兽才解锁
            return Cleared(Levels.LEVELS[i - 1].Id);
        }
        public static Level Next(string id) { int i = Index(id); var n = i >= 0 && i + 1 < Levels.LEVELS.Length ? Levels.LEVELS[i + 1] : null; return n != null && n.Endless ? null : n; }   // 无尽关只从选关进
    }

    /// <summary>
    /// 整个游戏的流程：标题 → 选关 → 关卡 → 结算 →（重来 / 下一关 / 返回）。
    /// 每个界面都是场景里的实物（星座、宝石星、手绘细线、小字），切换时有淡入淡出的过程。
    /// 第一次玩：标题点“开始”直接进序章（教学关），之后进选关。
    /// </summary>
    public class AppFlow : MonoBehaviour
    {
        public enum State { Title, Select, Play, Result, Ending }
        public GameRunner runner;
        public GemField gems;
        public MenuPicker picker;
        public TitleView title;
        public SelectView select;
        public ResultView result;
        public PlayHud hud;
        public SettingsView settings;
        public EndingView ending;
        public PauseView pause;
        public SkyView sky;
        [Tooltip("测试用：所有关卡都能选")] public bool unlockAll;
        public static bool UnlockAll;

        public State Current { get; private set; } = State.Title;
        public event Action<State> StateChanged;
        float resultIn = -1f, endingIn = -1f;
        float presence;          // 关卡里星串的显隐（进出关卡有过程）

        // 路线展开：进关时从入口魔法阵往星核“流”出来（米 / 秒），退出时收回入口
        public static float Grow = 1000f;
        const float GrowSpeed = 9f;

        void Awake() { UnlockAll = unlockAll; SetGrow(1000f); }
        public static void SetGrow(float g) { Grow = g; Shader.SetGlobalFloat("_SS_Grow", g); Shader.SetGlobalFloat("_SS_GrowK", Mathf.Clamp01(g / 12f)); }

        void Start()
        {
            if (runner) runner.OnEvent += OnGameEvent;
            Go(State.Title);
        }

        public void Go(State s)
        {
            Current = s;
            if (title) title.SetShown(s == State.Title);
            if (select) select.SetShown(s == State.Select);
            if (settings && s != State.Title) settings.SetShown(false);   // 设置只在封面里点“设置”展开
            if (result && s != State.Result) result.SetShown(false);
            if (hud) hud.SetShown(s == State.Play);
            if (ending && s != State.Ending) ending.SetShown(false);
            if (pause && s != State.Play) pause.SetPaused(false);
            if (picker) picker.active = true;   // 关卡里也要（“捏住不放 · 返回”）
            if (sky) sky.veil = s == State.Play ? 0.32f : 0.42f;   // 封面 / 选关时房间再暗一点，突出星图
            StateChanged?.Invoke(s);
        }

        // ───── 入口 ─────
        public void Begin()
        {
            AudioKit.Instance?.PlayClip(AudioKit.Instance.begin, 0.8f);
            if (!Progress.Cleared("T")) Play("T");
            else Go(State.Select);
        }

        public void Play(string id)
        {
            resultIn = -1f;
            presence = 0f;
            SetGrow(0f);
            AudioKit.Instance?.PlayClip(AudioKit.Instance.levelStart, 0.75f);
            // 进关前轻校准：玩家已经偏开太多（转身 / 挪了位置）就先把星界平滑转过来
            if (runner.InXR && runner.Drifted()) runner.Recenter();
            runner.StartLevel(id);
            Go(State.Play);
        }

        public void Retry() => Play(runner.levelId);
        public void Next()
        {
            var n = Progress.Next(runner.levelId);
            if (n != null) Play(n.Id); else BackToSelect();
        }
        public void BackToSelect()
        {
            StartCoroutine(LeaveLevel(State.Select));
        }
        public void BackToTitle()
        {
            if (runner && runner.Game != null) { StartCoroutine(LeaveLevel(State.Title)); return; }
            Go(State.Title);
        }

        System.Collections.IEnumerator LeaveLevel(State to)
        {
            if (result) result.SetShown(false);
            if (hud) hud.SetShown(false);
            // 星串先缩小淡出，再撤掉路线
            float t = 0; float p0 = presence; float g0 = Mathf.Min(Grow, 30f);
            while (t < 0.8f) { t += Time.deltaTime; float k = Mathf.Clamp01(t / 0.8f); presence = Mathf.Lerp(p0, 0f, k); SetGrow(Mathf.Lerp(g0, 0f, k * k)); yield return null; }
            runner.Unload();
            Go(to);
        }

        void Update()
        {
            // 星串显隐
            bool inLevel = runner && runner.Game != null && (Current == State.Play || Current == State.Result || Current == State.Ending);
            if (inLevel) presence = Mathf.MoveTowards(presence, Current == State.Play ? 1f : 0f, Time.deltaTime / 0.8f);   // 结算时星串淡出，不和结算的字挤在一起
            if (gems) gems.presence = Mathf.SmoothStep(0, 1, presence);
            if (inLevel && Current != State.Ending && Grow < 1000f)
            {
                float g = Grow + Time.deltaTime * GrowSpeed * (Grow < 1.5f ? 0.5f : 1f);   // 第一米半慢一点（入口魔法阵张开）
                SetGrow(g > 60f ? 1000f : g);
            }

            if (endingIn > 0 && (endingIn -= Time.deltaTime) <= 0) PlayEnding();
            if (resultIn > 0 && (resultIn -= Time.deltaTime) <= 0)
            {
                Current = State.Result;
                if (hud) hud.SetShown(false);
                if (picker) picker.active = true;
                if (result) result.Show(runner.Game);
                StateChanged?.Invoke(State.Result);
            }
#if UNITY_EDITOR
            // 编辑器快捷键：Esc 回选关
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && Current == State.Play) BackToSelect();
#endif
        }

        void OnGameEvent(GameEvent e)
        {
            if (Current != State.Play) return;
            if (e.Type == "won")
            {
                var g = runner.Game;
                Progress.Record(g.Level.Id, g.Result != null ? g.Result.Stars : 1);
                if (g.Level.Id == "4-5" && ending) { endingIn = 1.6f; return; }   // 打完吞星兽：进结局，不出结算
                resultIn = 1.4f;
            }
            else if (e.Type == "lost") resultIn = 1.6f;
        }

        public void PlayEnding()
        {
            Current = State.Ending;
            if (hud) hud.SetShown(false);
            if (picker) picker.active = true;
            StateChanged?.Invoke(State.Ending);
            ending.Play();
        }

        /// <summary>测试用：直接以某状态打开（cs.py 截图用）</summary>
        public void DebugShowResult(bool won, int stars, int score)
        {
            if (result) result.ShowDebug(won, stars, score);
            Current = State.Result; if (picker) picker.active = true;
        }
    }

    /// <summary>界面视图的共同部分：显隐有过程（vis 0..1 平滑过渡），子类在 Apply(vis) 里按 vis 设透明度 / 大小。</summary>
    public abstract class FlowView : MonoBehaviour
    {
        public AppFlow flow;
        public float fadeIn = 0.8f, fadeOut = 0.5f;
        protected float vis;
        protected bool shown;
        protected float shownT;   // 显示后过了多久（做依次出现的动画）

        public virtual void SetShown(bool s)
        {
            if (s && !shown) shownT = 0;
            shown = s;
            if (s) gameObject.SetActive(true);
        }

        protected virtual void Update()
        {
            float dt = Time.deltaTime;
            vis = Mathf.MoveTowards(vis, shown ? 1f : 0f, dt / (shown ? fadeIn : fadeOut));
            if (shown) shownT += dt;
            Apply(Mathf.SmoothStep(0, 1, vis), dt);
            SetTargets(shown && vis > 0.6f);
        }

        protected abstract void Apply(float v, float dt);

        protected readonly List<PickTarget> targets = new();
        void SetTargets(bool on) { foreach (var t in targets) if (t) t.interactable = on && t.allowed; }

        protected PickTarget Target(Transform parent, string name, Vector3 localPos, float radius, Action onPick, float hold = 0)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = localPos;
            var t = go.AddComponent<PickTarget>(); t.radius = radius; t.onPick = onPick; t.holdSeconds = hold; t.interactable = false;
            targets.Add(t);
            return t;
        }
    }
}
