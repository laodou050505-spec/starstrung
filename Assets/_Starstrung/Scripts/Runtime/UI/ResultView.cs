using System.Collections.Generic;
using Starstrung.Logic;
using TMPro;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 结算 = 写在星核周围的“星图批注”（11-24 用户：居中一摞字有 AI 味）。不居中堆字、不对称、围着星核展开：
    /// - 左边：竖排细宋体“星核守住了 / 星核裂开了”，旁边一列竖排小字写关名（英文时改成右对齐横排）；
    /// - 星核上方：拿到几颗星，就有几颗金宝石从星核里飞出来停在上方，手绘细线把它们连成一个小星座（这一关的“战绩星座”）；没拿到的位置只留一个手绘空圈；
    /// - 右上：一条手绘引线从星核边上拉出去，末端写细衬线大数字（分数从 0 数上去）；最大连锁、用时作为引线上的小刻度注记；
    /// - 按钮在星核的手绘轨道线上：下一关（右前、最大）/ 重来（左前）/ 返回（左后）；输了没有“下一关”。
    /// 出现有过程：竖排字一个个写出 → 宝石飞出、星座连线 → 引线画出、分数数上去 → 注记 → 按钮。
    /// </summary>
    public class ResultView : FlowView
    {
        public GemField gems;
        public Transform anchor;   // 星核

        Vector3 C;   // 星核（本地）
        UIText titleV, titleH, nameV, scoreTxt, comboTxt, timeTxt;
        LineRenderer leader, constel, orbit; readonly LineRenderer[] emptyRing = new LineRenderer[3];
        readonly Vector3[] starAt = new Vector3[3];
        readonly Renderer[] starFlash = new Renderer[3];
        readonly PickTarget[] btn = new PickTarget[3];
        readonly UIText[] btnLabel = new UIText[3];
        readonly Vector3[] btnPos = new Vector3[3];
        static readonly string[] BtnKey = { "重来", "下一关", "返回" };
        static readonly int[] BtnColor = { 2, 1, 3 };
        List<Vector3> leaderPts, constelPts;
        bool won, nextOk; int nStars, scoreN, combo; float time; string levelName = "";
        int chimed;
        static readonly Color Ink = new(0.84f, 0.9f, 1f, 1f);
        static readonly Color Gold = new(1f, 0.85f, 0.55f);

        void Awake()
        {
            C = anchor ? anchor.localPosition : new Vector3(0, 1.22f, 0.35f);
            // 左边竖排标题 + 竖排关名
            titleV = UIText.Make(transform, "", UIKit.Face.Serif, 0.05f, C + new Vector3(-0.33f, 0.39f, 0.06f), TextAlignmentOptions.Center, new Color(0.95f, 0.96f, 1f), raw: true);
            titleV.tmp.lineSpacing = -22f;
            nameV = UIText.Make(transform, "", UIKit.Face.Serif, 0.018f, C + new Vector3(-0.275f, 0.44f, 0.06f), TextAlignmentOptions.Center, new Color(0.75f, 0.8f, 1f), raw: true);
            nameV.tmp.lineSpacing = -10f;
            titleH = UIText.Make(transform, "", UIKit.Face.Serif, 0.042f, C + new Vector3(-0.2f, 0.42f, 0.06f), TextAlignmentOptions.Right, new Color(0.95f, 0.96f, 1f), raw: true);
            foreach (var t in new[] { titleV, nameV, titleH }) t.gameObject.AddComponent<FaceHead>().yawOnly = false;   // 完全正对玩家（低头看时竖排字也不歪）

            // 星级星座：星核上方三个位置（不在一条线上）
            starAt[0] = C + new Vector3(-0.11f, 0.37f, 0.03f); starAt[1] = C + new Vector3(0.0f, 0.43f, 0.05f); starAt[2] = C + new Vector3(0.1f, 0.36f, 0.02f);
            for (int k = 0; k < 3; k++)
            {
                starFlash[k] = UIKit.Glyph(transform, "Flash" + k, starAt[k], 0.22f);
                var rg = new GameObject("Empty" + k); rg.transform.SetParent(transform, false); rg.transform.localPosition = starAt[k];
                rg.AddComponent<FaceHead>().yawOnly = false;
                emptyRing[k] = UIKit.Line(rg.transform, "Ring", UIKit.HandCircle(Vector3.zero, Vector3.back, 0.018f, 20 + k), 0.0014f, Ink);
            }
            constelPts = UIKit.HandPolyline(new[] { starAt[0], starAt[1], starAt[2] }, Vector3.back, 31, false, 10);
            constel = UIKit.Line(transform, "Constellation", new List<Vector3> { C, C }, 0.0016f, Gold);

            // 右上的手绘引线 + 分数 + 注记
            leaderPts = UIKit.HandPolyline(new[] { C + new Vector3(0.14f, 0.2f, 0f), C + new Vector3(0.21f, 0.42f, 0.04f), C + new Vector3(0.43f, 0.45f, 0.08f) }, Vector3.back, 41, false, 12);
            leader = UIKit.Line(transform, "Leader", new List<Vector3> { C, C }, 0.0014f, Ink);
            scoreTxt = UIText.Make(transform, "0", UIKit.Face.Title, 0.07f, C + new Vector3(0.25f, 0.495f, 0.08f), TextAlignmentOptions.Left, new Color(0.97f, 0.94f, 0.86f), raw: true);
            scoreTxt.spacing = 6f; scoreTxt.Refresh();
            comboTxt = UIText.Make(transform, "", UIKit.Face.Serif, 0.016f, C + new Vector3(0.26f, 0.42f, 0.06f), TextAlignmentOptions.Left, new Color(0.8f, 0.85f, 1f), raw: true);
            timeTxt = UIText.Make(transform, "", UIKit.Face.Serif, 0.016f, C + new Vector3(0.19f, 0.3f, 0.03f), TextAlignmentOptions.Left, new Color(0.8f, 0.85f, 1f), raw: true);
            foreach (var t in new[] { scoreTxt, comboTxt, timeTxt }) t.gameObject.AddComponent<FaceHead>();

            // 星核轨道上的按钮
            var oc = C + new Vector3(0, 0.02f, 0); float orR = 0.27f;
            Vector3 OnOrbit(float deg) { float a = deg * Mathf.Deg2Rad; return oc + Quaternion.Euler(0, 0, -9f) * new Vector3(Mathf.Cos(a) * orR, 0, Mathf.Sin(a) * orR * 0.55f); }
            var ell = new List<Vector3>();
            for (int k = 0; k <= 96; k++) { float a = (k / 96f) * 374f * Mathf.Deg2Rad; ell.Add(oc + Quaternion.Euler(0, 0, -9f) * new Vector3(Mathf.Cos(a) * orR, 0, Mathf.Sin(a) * orR * 0.55f) + Vector3.up * (k / 96f) * 0.006f); }
            orbit = UIKit.Line(transform, "Orbit", ell, 0.0017f, Ink);
            btnPos[1] = OnOrbit(-32f); btnPos[0] = OnOrbit(-148f); btnPos[2] = OnOrbit(152f);
            for (int k = 0; k < 3; k++)
            {
                int kk = k;
                btn[k] = Target(transform, "Btn" + k, btnPos[k], k == 1 ? 0.06f : 0.05f, () => OnButton(kk));
                bool right = btnPos[k].x > C.x;
                btnLabel[k] = UIText.Make(transform, BtnKey[k], UIKit.Face.Serif, k == 1 ? 0.024f : 0.02f, btnPos[k] + new Vector3(right ? 0.05f : -0.045f, 0.03f, -0.02f),
                    right ? TextAlignmentOptions.Left : TextAlignmentOptions.Right, new Color(0.9f, 0.92f, 1f));
                btnLabel[k].spacing = 26f; btnLabel[k].Refresh();
                btnLabel[k].gameObject.AddComponent<FaceHead>();
            }
            fadeIn = 0.6f;
        }

        void OnButton(int k)
        {
            if (k == 0) flow.Retry();
            else if (k == 1) flow.Next();
            else flow.BackToSelect();
        }

        public void Show(Game g)
        {
            won = g.Status == "won";
            var r = g.Result;
            nStars = won && r != null ? r.Stars : 0;
            scoreN = 0; combo = 0;
            if (r != null) { scoreN = r.Score; combo = r.MaxCombo; }
            else foreach (var c in g.Chains) { scoreN += c.Sim.Score; combo = Mathf.Max(combo, c.Sim.MaxCombo); }
            time = (float)g.Time;
            levelName = g.Level.Tutorial ? Loc.T("序章") : (g.Level.Id + "  " + Loc.Name(g.Level));
            Fill();
            SetShown(true);
        }

        public void ShowDebug(bool w, int st, int sc) { won = w; nStars = st; scoreN = sc; combo = 5; time = 93; levelName = "1-2  " + (Loc.English ? "Far Shore" : "远岸"); Fill(); SetShown(true); }

        public override void SetShown(bool s) { if (s && !shown) chimed = 0; base.SetShown(s); }

        static string Vertical(string s) { var sb = new System.Text.StringBuilder(); foreach (var ch in s) { if (ch == ' ') continue; if (sb.Length > 0) sb.Append('\n'); sb.Append(ch); } return sb.ToString(); }

        void Fill()
        {
            nextOk = won && flow && flow.runner && Progress.Next(flow.runner.levelId) != null;
            string title = Loc.T(won ? "星核守住了" : "星核裂开了");
            titleV.Set(Loc.English ? "" : Vertical(title));
            titleH.Set(Loc.English ? title : "");
            nameV.Set(Loc.English ? "" : Vertical(levelName));
            comboTxt.Set($"{Loc.T("最大连锁")} ×{combo}");
            timeTxt.Set(Loc.F("{0} 秒", Mathf.RoundToInt(time)));
        }

        void OnEnable() => Loc.Changed += Fill;
        void OnDisable() => Loc.Changed -= Fill;

        static float Ramp(float t, float a, float d) => Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - a) / d));

        void Partial(LineRenderer lr, List<Vector3> pts, float k)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(pts.Count * Mathf.Clamp01(k)));
            lr.positionCount = n;
            for (int i = 0; i < n; i++) lr.SetPosition(i, pts[Mathf.Min(i, pts.Count - 1)]);
        }

        protected override void Apply(float v, float dt)
        {
            if (vis <= 0f && !shown) { if (gameObject.activeSelf) gameObject.SetActive(false); return; }
            float t = Time.time, s = shownT;
            var coreW = anchor ? anchor.position : transform.TransformPoint(C);

            // 竖排标题：一个个写出来（用逐字显示）
            int chars = titleV.tmp.textInfo != null ? titleV.tmp.textInfo.characterCount : 0;
            titleV.tmp.maxVisibleCharacters = Mathf.FloorToInt(Mathf.Clamp01(s / 0.9f) * (chars + 1));
            titleV.alpha = v; titleH.alpha = Ramp(s, 0f, 0.8f) * v;
            nameV.alpha = Ramp(s, 0.6f, 0.6f) * v * 0.8f;

            // 星级：金宝石从星核飞出、停在上方；连成小星座
            int landed = 0;
            for (int k = 0; k < 3; k++)
            {
                float at = 0.7f + k * 0.3f;
                bool lit = k < nStars;
                var target = transform.TransformPoint(starAt[k]);
                float e = Mathf.Clamp01((s - at) / 0.55f); float ee = 1f - Mathf.Pow(1f - e, 3f);
                var p = Vector3.Lerp(coreW, target, ee) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 0.05f;
                if (lit && s > at) gems.Extra(p + Vector3.up * Mathf.Sin(t * 1.3f + k) * 0.003f * ee, 0.042f * Mathf.Lerp(0.4f, 1f, ee) * v, 1, 0.5f, 0.2f + k * 0.17f);
                if (lit && e >= 1f) landed++;
                float flash = lit && e >= 1f ? Mathf.Exp(-(s - at - 0.55f) * 5f) : 0f;
                float tw = lit ? 0.3f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 1.6f + k * 2f), 3f) : 0f;
                UIKit.GlyphColor(starFlash[k], Gold, (flash * 2f + tw) * v);
                UIKit.SetAlpha(emptyRing[k], Ink, lit ? 0f : Ramp(s, at, 0.4f) * v * 0.5f);
                if (lit && chimed <= k && e >= 1f) { chimed = k + 1; AudioKit.Instance?.Play(new[] { 12f, 16f, 19f }[k], 0.45f, 2.2f); }
            }
            // 星座连线：所有拿到的星落定后画出来（只连拿到的）
            float cl = Ramp(s, 0.7f + nStars * 0.3f + 0.4f, 0.6f);
            if (nStars >= 2) { var sub = constelPts.GetRange(0, Mathf.Min(constelPts.Count, nStars == 2 ? constelPts.Count / 2 + 1 : constelPts.Count)); Partial(constel, sub, cl); UIKit.SetAlpha(constel, Gold, cl * v * 0.7f); }
            else UIKit.SetAlpha(constel, Gold, 0);

            // 引线 + 分数
            float la = Ramp(s, 1.8f, 0.6f);
            Partial(leader, leaderPts, la); UIKit.SetAlpha(leader, Ink, v * 0.55f);
            float cnt = Mathf.Clamp01((s - 2.1f) / 1.1f);
            scoreTxt.Set(Mathf.RoundToInt(scoreN * (1f - Mathf.Pow(1f - cnt, 3f))).ToString());
            scoreTxt.alpha = Ramp(s, 2.0f, 0.4f) * v;
            comboTxt.alpha = Ramp(s, 2.7f, 0.5f) * v * 0.85f; timeTxt.alpha = Ramp(s, 2.85f, 0.5f) * v * 0.85f;

            // 按钮
            UIKit.SetAlpha(orbit, Ink, Ramp(s, 2.9f, 0.6f) * v * 0.5f);
            for (int k = 0; k < 3; k++)
            {
                bool ok = k != 1 || nextOk;
                bool big = k == 1 || (k == 0 && !nextOk);
                btn[k].allowed = ok && s > 3.2f;
                float a = Ramp(s, 3.0f + k * 0.12f, 0.5f) * v * (ok ? 1f : 0f);
                float hv = btn[k].hover;
                float size = (big ? 0.046f : k == 2 ? 0.026f : 0.032f) * (1f + 0.25f * hv);
                gems.Extra(transform.TransformPoint(btnPos[k] + Vector3.up * Mathf.Sin(t * 1.5f + k) * 0.004f), size * a, BtnColor[k], hv, 0.3f + k * 0.21f);
                btnLabel[k].alpha = a * (0.7f + 0.3f * hv);
            }
        }
    }
}
