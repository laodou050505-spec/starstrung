using System.Collections.Generic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 暂停（11-24 用户：游戏途中需要一个暂停；11-25：要一个暂停按钮）。
    /// - 手动：星核右边一个暂停按钮（手绘细圆 + 两道竖线，和左边的“捏住不放 · 返回”对称），指着捏一下就暂停；
    ///   以前的“指着星核捏一下暂停”去掉了：串头逼近星核时玩家正对着那里捏星，容易误停；
    /// - 自动：摘下头显、按系统键切到后台、应用失焦 → 立刻暂停（回来时不会发现星核已经被撞裂）；
    /// - 暂停时：星串和一切逻辑停住（GameRunner 不推进）、音乐压低、画面罩暗一档，星核上方浮出“已暂停”和两颗宝石：继续 / 返回；
    /// - 恢复：捏“继续”（不自动恢复，免得玩家还没准备好星串就动了）。
    /// 11-25 修：这个视图以前没暂停时会把自己整个关掉，挂在它上面的检测（自动暂停）也跟着停了，等于从没生效 → 现在常驻，只藏画面。
    /// </summary>
    public class PauseView : FlowView
    {
        public GameRunner runner;
        public GemField gems;
        public Transform core;
        public SkyView sky;

        public static bool Paused { get; private set; }

        UIText head, resumeLabel, backLabel, btnLabel;
        PickTarget resume, back, pauseBtn;
        Transform btn; readonly List<LineRenderer> btnLines = new();
        Vector3 C, resumePos, backPos, btnPos;
        float btnA;
        static readonly Color Ink = new(0.86f, 0.9f, 1f, 1f);

        void Awake()
        {
            C = core ? core.localPosition : new Vector3(0, 1.22f, 0.35f);
            head = UIText.Make(transform, "已暂停", UIKit.Face.Serif, 0.042f, C + new Vector3(0, 0.3f, 0.1f), color: new Color(0.94f, 0.95f, 1f));
            head.spacing = 26f; head.Refresh(); head.gameObject.AddComponent<FaceHead>();
            resumePos = C + new Vector3(0.16f, 0.1f, 0f); backPos = C + new Vector3(-0.16f, 0.1f, 0f);
            resume = Target(transform, "Resume", resumePos, 0.055f, () => SetPaused(false));
            resumeLabel = UIText.Make(transform, "继续", UIKit.Face.Serif, 0.022f, resumePos + new Vector3(0, -0.06f, 0), color: new Color(0.9f, 0.92f, 1f));
            resumeLabel.spacing = 24f; resumeLabel.Refresh(); resumeLabel.gameObject.AddComponent<FaceHead>();
            back = Target(transform, "PauseBack", backPos, 0.05f, () => { SetPaused(false); flow.BackToSelect(); });
            backLabel = UIText.Make(transform, "返回", UIKit.Face.Serif, 0.02f, backPos + new Vector3(0, -0.055f, 0), color: new Color(0.86f, 0.88f, 1f));
            backLabel.spacing = 24f; backLabel.Refresh(); backLabel.gameObject.AddComponent<FaceHead>();

            // 暂停按钮：星核右边，和 PlayHud 里左边的“返回”对称（离右侧星盘 16 cm，不会捏错）
            btnPos = C + new Vector3(0.25f, 0.02f, -0.05f);
            btn = new GameObject("PauseButton").transform; btn.SetParent(transform, false); btn.localPosition = btnPos;
            btn.gameObject.AddComponent<FaceHead>().yawOnly = false;
            var n = Vector3.back;
            btnLines.Add(UIKit.Line(btn, "Ring", UIKit.HandCircle(Vector3.zero, n, 0.022f, 91, 15f, 330f, 48), 0.0018f, Ink));
            btnLines.Add(UIKit.Line(btn, "BarL", UIKit.HandPolyline(new[] { new Vector3(-0.0055f, 0.0095f, 0), new Vector3(-0.0055f, -0.0095f, 0) }, n, 92), 0.0022f, Ink));
            btnLines.Add(UIKit.Line(btn, "BarR", UIKit.HandPolyline(new[] { new Vector3(0.0055f, 0.0095f, 0), new Vector3(0.0055f, -0.0095f, 0) }, n, 93), 0.0022f, Ink));
            btnLabel = UIText.Make(btn, "暂停", UIKit.Face.Body, 0.015f, new Vector3(0, 0.036f, 0), color: new Color(0.78f, 0.82f, 1f));
            // 不放进 targets：它的可摘状态单独管（只在关卡里、没暂停时可摘）
            var go = new GameObject("PauseHit"); go.transform.SetParent(transform, false); go.transform.localPosition = btnPos;
            pauseBtn = go.AddComponent<PickTarget>(); pauseBtn.radius = 0.045f; pauseBtn.onPick = () => SetPaused(true); pauseBtn.interactable = false;
            fadeIn = 0.25f; fadeOut = 0.2f;
        }

        public void SetPaused(bool on)
        {
            if (Paused == on) return;
            Paused = on;
            SetShown(on);
            if (runner) runner.paused = on;
            AudioKit.Instance?.SetPaused(on);
            if (on) AudioKit.Instance?.Play(5, 0.3f, 0.6f);
        }

        void OnApplicationPause(bool p) { if (p) AutoPause(); }
        void OnApplicationFocus(bool f) { if (!f) AutoPause(); }
        void AutoPause()
        {
            // 编辑器里不做：编辑器窗口切到后台（比如在别的窗口改代码、跑脚本）也算失焦，会一直被暂停；头显上才需要
            if (Application.isEditor) return;
            // 摘下头显 / 切后台：只在关卡里才需要暂停（封面、选关本来就没有在跑的星串）
            if (flow && flow.Current == AppFlow.State.Play && runner && runner.Game != null && runner.Game.Status == "playing") SetPaused(true);
        }

        protected override void Update()
        {
            base.Update();
            bool inPlay = flow && flow.Current == AppFlow.State.Play && runner && runner.Game != null && runner.Game.Status == "playing";
            if (Paused && !(flow && flow.Current == AppFlow.State.Play)) SetPaused(false);
            // 头显：摘下来时 XR 会报“用户不在”，也当作暂停
            if (inPlay && !Paused && runner.InXR && !UnityEngine.XR.XRSettings.isDeviceActive) SetPaused(true);
            if (sky && Paused) sky.veil = 0.62f;

            // 暂停按钮：关卡进行中常亮（淡），指着时亮起；暂停 / 结算 / 进出关卡时收起
            bool showBtn = inPlay && !Paused && AppFlow.Grow > 3f;
            pauseBtn.interactable = showBtn;
            btnA = Mathf.MoveTowards(btnA, showBtn ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            float hv = pauseBtn.hover;
            foreach (var lr in btnLines) UIKit.SetAlpha(lr, Ink, btnA * (0.45f + 0.55f * hv));
            btnLabel.alpha = btnA * (0.4f + 0.6f * hv);
            btn.localScale = Vector3.one * (1f + 0.15f * hv);
        }

        protected override void Apply(float v, float dt)
        {
            // 不再 SetActive(false)：按钮和自动暂停的检测要一直在
            float t = Time.unscaledTime;
            head.alpha = v;
            resumeLabel.alpha = backLabel.alpha = 0f;
            if (v <= 0.001f) return;
            foreach (var (p, lab, size, color, seed) in new[] { (resumePos, resumeLabel, 0.05f, 2, 0.3f), (backPos, backLabel, 0.036f, 3, 0.6f) })
            {
                var pick = p == resumePos ? resume : back;
                float hv = pick.hover;
                gems.Extra(transform.TransformPoint(p + Vector3.up * Mathf.Sin(t * 1.5f + seed * 9f) * 0.004f), (size + size * 0.25f * hv) * v, color, hv, seed);
                lab.alpha = v * (0.75f + 0.25f * hv);
            }
        }
    }
}
