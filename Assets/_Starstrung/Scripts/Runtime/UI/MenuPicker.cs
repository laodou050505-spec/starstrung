using System;
using System.Collections.Generic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 界面上能“摘”的东西（选关星、按钮星、设置图标）。位置 = 本物体世界坐标，radius = 判定半径（米）。
    /// hover 0..1 是平滑后的“被指着”程度，界面脚本拿来放大 / 提亮。
    /// </summary>
    public class PickTarget : MonoBehaviour
    {
        public float radius = 0.05f;
        public Action onPick;
        public bool interactable = true;
        /// <summary>界面逻辑允许不允许摘（比如没解锁的关）；interactable 由视图显隐控制，两个都为真才能摘</summary>
        public bool allowed = true;
        [NonSerialized] public float hover;
        [NonSerialized] public bool hovered;
        /// <summary>捏住不放才触发（秒，0 = 一捏就触发）。用在“返回”这类不想误触的地方。</summary>
        public float holdSeconds;
        [NonSerialized] public float holdT;

        static readonly List<PickTarget> all = new();
        public static IReadOnlyList<PickTarget> All => all;
        void OnEnable() => all.Add(this);
        void OnDisable() { all.Remove(this); hovered = false; hover = 0; holdT = 0; }
    }

    /// <summary>
    /// 界面里的“指着捏”，和玩法里是同一个手势：
    /// - 近：捏合点离目标 ≤ 半径 + 4 cm → 直接捏；
    /// - 远：手掌射线和目标方向夹角 ≤ 判定锥（目标越大锥越宽），已指着的目标多给 1.5° 滞回，不来回跳；
    /// - 先亮后捏：被指着的目标先放大提亮，捏下去那一刻取 0.12 秒前指着的目标（捏的动作会让射线偏）。
    /// </summary>
    public class MenuPicker : MonoBehaviour
    {
        public GameRunner runner;
        public float coneDeg = 7.5f, hysteresisDeg = 2.5f, lookback = 0.20f;
        public bool active = true;
        public event Action<PickTarget> Picked;

        class HandTrack { public bool wasPinch; public PickTarget cur; public float curSince; public PickTarget recent; public float recentT; public PickTarget holding; public Transform reticle; public LineRenderer[] lines; public float a; }
        readonly HandTrack[] hands = { new(), new() };
        static readonly Color Ink = new(0.86f, 0.92f, 1f, 1f);

        /// <summary>当前指定手正瞄准或按住的界面目标（供手部星光指引线吸附端点使用）。</summary>
        public PickTarget CurrentTarget(int side)
        {
            if (side < 0 || side > 1) return null;
            var h = hands[side];
            return h.holding ? h.holding : h.cur;
        }

        void Awake()
        {
            // 自动挂载手部星光指引（指尖微光点 + 极细星光射线），无需重建场景即可在头显上清晰反馈手的位置与指向
            if (!GetComponent<HandPointerView>())
            {
                var pv = gameObject.AddComponent<HandPointerView>();
                pv.runner = runner;
                pv.menuPicker = this;
            }
        }

        // 星盘准星：被指着的东西外面一个手绘细圆 + 四道斜刻度（方案 7.1），慢慢转；两只手各一个
        void MakeReticle(HandTrack h, int side)
        {
            h.reticle = new GameObject("Reticle" + side).transform; h.reticle.SetParent(transform, false);
            var lines = new System.Collections.Generic.List<LineRenderer> { UIKit.Line(h.reticle, "Ring", UIKit.HandCircle(Vector3.zero, Vector3.back, 1f, 90 + side, 0, 360, 72), 0.0016f, Ink) };
            for (int k = 0; k < 4; k++)
            {
                float a = (45f + k * 90f) * Mathf.Deg2Rad; var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                lines.Add(UIKit.Line(h.reticle, "Tick", new System.Collections.Generic.List<Vector3> { d * 1.12f, d * 1.38f }, 0.0018f, Ink));
            }
            h.lines = lines.ToArray();
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            for (int i = 0; i < 2; i++)
            {
                var h = hands[i];
                if (!h.reticle) MakeReticle(h, i);
                var t = h.holding ? h.holding : h.cur;
                bool show = active && t && t.isActiveAndEnabled && t.interactable;
                h.a = Mathf.MoveTowards(h.a, show ? 1f : 0f, Time.deltaTime * 6f);
                if (show)
                {
                    h.reticle.position = t.transform.position;
                    if (cam) h.reticle.rotation = Quaternion.LookRotation(t.transform.position - cam.transform.position) * Quaternion.Euler(0, 0, Time.time * 30f);
                    float pulse = 1f + 0.06f * Mathf.Sin(Time.time * 4f);
                    h.reticle.localScale = Vector3.one * t.radius * 0.95f * pulse * Mathf.Lerp(1.3f, 1f, h.a);
                }
                foreach (var lr in h.lines) { UIKit.SetAlpha(lr, Ink, h.a * 0.85f); lr.widthMultiplier = 0.0016f; }
            }
        }

        void Update()
        {
            float dt = Time.deltaTime, now = Time.time;
            foreach (var t in PickTarget.All) t.hovered = false;
            if (!active || !runner) { Decay(dt); return; }
            runner.ReadHands(out var l, out var r);
            Track(0, l, now, dt); Track(1, r, now, dt);
            Decay(dt);
        }

        void Decay(float dt)
        {
            for (int i = 0; i < PickTarget.All.Count; i++)
            {
                var t = PickTarget.All[i];
                t.hover = Mathf.MoveTowards(t.hover, t.hovered && t.interactable ? 1f : 0f, dt * 6f);
            }
        }

        void Track(int side, HandState h, float now, float dt)
        {
            var ht = hands[side];
            if (!h.tracked) { ht.cur = null; ht.wasPinch = false; ht.holding = null; return; }
            // 捏着不放的时候，保持锁定在捏住的那个目标上（用于“捏住不放 · 返回”）
            if (ht.holding)
            {
                if (h.pinch && ht.holding.isActiveAndEnabled)
                {
                    ht.holding.hovered = true;
                    ht.holding.holdT += dt;
                    if (ht.holding.holdT >= ht.holding.holdSeconds) { var t0 = ht.holding; ht.holding = null; t0.holdT = 0; Fire(t0); }
                }
                else { ht.holding.holdT = 0; ht.holding = null; }
                ht.wasPinch = h.pinch;
                return;
            }

            // 1) 近距离直接用手摸（最高优先级）
            PickTarget best = null; float bestScore = float.MaxValue;
            for (int i = 0; i < PickTarget.All.Count; i++)
            {
                var t = PickTarget.All[i];
                if (!t.interactable) continue;
                float near = Vector3.Distance(h.pinchPoint, t.transform.position);
                if (near <= t.radius + 0.075f && near < bestScore) { bestScore = near; best = t; }
            }

            // 2) 眼动优先锁定（看哪个按钮/选关星就套上准星，手轻捏即点）
            bool hasGaze = best == null && runner && runner.InXR && runner.pico && runner.pico.EyeTracked;
            if (hasGaze)
            {
                Ray gaze = runner.pico.Gaze;
                bestScore = float.MaxValue;
                for (int i = 0; i < PickTarget.All.Count; i++)
                {
                    var t = PickTarget.All[i];
                    if (!t.interactable) continue;
                    var to = t.transform.position - gaze.origin; float dist = to.magnitude; if (dist < 0.05f) continue;
                    float ang = Vector3.Angle(gaze.direction, to);
                    float angRad = Mathf.Atan2(t.radius, dist) * Mathf.Rad2Deg;
                    float gOff = Mathf.Max(0f, ang - angRad);
                    float limit = 5.5f + (t == ht.cur ? 1.2f : 0f);
                    float score = gOff - (t == ht.cur ? 0.9f : 0f);
                    if (gOff <= limit && score < bestScore) { bestScore = score; best = t; }
                }
            }

            // 3) 眼动未注视任何按钮时，回退到零视差手部射线
            if (best == null && h.hasRay)
            {
                bestScore = float.MaxValue;
                for (int i = 0; i < PickTarget.All.Count; i++)
                {
                    var t = PickTarget.All[i];
                    if (!t.interactable) continue;
                    var to = t.transform.position - h.ray.origin; float dist = to.magnitude; if (dist < 0.05f) continue;
                    float ang = Vector3.Angle(h.ray.direction, to);
                    float angRad = Mathf.Atan2(t.radius, dist) * Mathf.Rad2Deg;
                    float cone = Mathf.Max(coneDeg, angRad * 1.4f) + (t == ht.cur ? hysteresisDeg : 0f);
                    float score = Mathf.Max(0f, ang - angRad) - (t == ht.cur ? hysteresisDeg * 0.6f : 0f);
                    if (ang <= cone && score < bestScore) { bestScore = score; best = t; }
                }
            }
            if (best != ht.cur)
            {
                // 如果刚刚还稳稳锁着旧目标，先记入 recent 供捏合瞬间防抖回溯
                if (ht.cur && ht.cur.interactable) { ht.recent = ht.cur; ht.recentT = now; }
                ht.cur = best;
                ht.curSince = now;
            }
            if (best) { best.hovered = true; if (now - ht.curSince >= 0.08f) { ht.recent = best; ht.recentT = now; } }

            bool down = h.pinch && !ht.wasPinch;
            ht.wasPinch = h.pinch;
            if (!down) return;
            // 若在捏合前 < 80 ms 才刚切到旁边新按钮（大概率是捏合动作带偏），优先回溯前一个稳稳瞄住的 recent 目标
            PickTarget target = best;
            if ((!best || now - ht.curSince < 0.08f) && ht.recent && ht.recent.isActiveAndEnabled && ht.recent.interactable && now - ht.recentT <= lookback)
                target = ht.recent;
            if (!target) return;
            if (target.holdSeconds > 0f) { ht.holding = target; target.holdT = 0; return; }
            Fire(target);
        }

        void Fire(PickTarget t)
        {
            Picked?.Invoke(t);
            t.onPick?.Invoke();
        }
    }
}
