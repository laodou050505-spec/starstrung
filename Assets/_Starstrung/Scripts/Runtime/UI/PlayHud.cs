using System.Collections.Generic;
using Starstrung.Logic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 关卡里的界面（尽量少）：
    /// - 开场：关名在星核上方浮现、停两秒、淡去；
    /// - 教学关：建议摘的星 / 该放的缝外面画一个慢慢转的手绘圈，旁边一句短话（按这一段教什么、手里有没有星换话）；
    ///   每段第一次消除时补一句“三颗同色相连，就会消散”；一段清空说“很好”；
    /// - 示范手（教学关）：一只半透明的光线手演示“指着 → 捏 → （拿着时）指向发亮的缝 → 松开”；近处的星手直接伸过去，远处的星从手边拉一道细线指过去；
    /// - 返回：左下一颗小四芒星，捏住不放 1.2 秒才离开（防误触），捏着时一圈手绘弧慢慢画满。
    /// </summary>
    public class PlayHud : FlowView
    {
        public GemField gems;
        public GameRunner runner;

        UIText levelName, levelId, caption, toast, leaveLabel;
        LineRenderer hintRing, leaveRing;
        Renderer leaveGlyph;
        PickTarget leave; float pauseK = 1f;
        float introT, toastT = -1, captionA, ringA;
        string toastKey;
        int stage = -1; string teach = ""; bool explainedMatch; int levelTotal;
        Vector3 hintPos; bool hintValid;
        public Transform core;
        public Material circleMat;
        // 连锁数字（爽感）：消除点浮现 “×N”，连锁越长越大越亮
        readonly UIText[] comboTxt = new UIText[4];
        readonly float[] comboT = new float[4];
        readonly Vector3[] comboAt = new Vector3[4];
        readonly int[] comboN = new int[4];
        int comboNext;
        // 进度：星核外面一圈刻度，消一颗少一格（不加文字）
        LineRenderer progRing; float progShown = 1f;
        // 紧张：串头在身后时，视野边缘一抹暖光指方向
        Renderer edgeHint;
        // 星盘：胸前两个小魔法阵（空着时淡，放了星亮，拿着星靠近时转快、变亮）
        readonly Transform[] stashPad = new Transform[2]; readonly Material[] stashMat = new Material[2];
        Vector3 leavePos = new(-0.25f, 1.24f, 0.3f);   // 星核左边（按钮都在星核附近）
        static readonly Color Ink = new(0.82f, 0.9f, 1f, 1f);

        // ───── 示范手 ─────
        Transform ghost; LineRenderer gIndex, gThumb, gPalm, gCurl, gRay;
        float ghostA, ghostT;
        static readonly Color GhostC = new(0.7f, 0.88f, 1f, 1f);

        // 手的轮廓（厘米，食指指向 +x，食指尖 = 捏合点在原点）：一只指着的手，其余三指收拢
        static readonly Vector2[] HandOutline =
        {
            new(-11f, 2.2f), new(-7f, 1.7f), new(-3.5f, 1.05f), new(-0.7f, 0.75f), new(0.05f, 0.05f), new(-0.8f, -0.65f), new(-4f, -0.8f), new(-6.1f, -0.9f),
            new(-5.3f, -1.8f), new(-5.7f, -2.7f), new(-6.7f, -2.95f), new(-6.0f, -3.7f), new(-6.4f, -4.7f), new(-7.9f, -4.95f), new(-11.4f, -4.1f),
        };
        static readonly Vector2[] ThumbOpen = { new(-8.2f, -1.3f), new(-5.0f, -2.3f), new(-2.4f, -2.7f), new(-1.6f, -2.2f) };
        static readonly Vector2[] ThumbClosed = { new(-8.2f, -1.3f), new(-4.6f, -1.7f), new(-1.6f, -1.0f), new(-0.6f, -0.55f) };

        static List<Vector3> Cm(IList<Vector2> pts) { var l = new List<Vector3>(); foreach (var p in pts) l.Add(new Vector3(p.x, p.y, 0) * 0.01f); return l; }

        void MakeGhost()
        {
            ghost = new GameObject("GhostHand").transform; ghost.SetParent(transform, false);
            // 和整套界面一样的手绘细线，像星图上画的一只手
            gPalm = UIKit.Line(ghost, "Outline", UIKit.HandPolyline(Cm(HandOutline), Vector3.back, 3, false, 4), 0.0022f, GhostC);
            gIndex = UIKit.Line(ghost, "Wrist", UIKit.HandPolyline(Cm(new[] { new Vector2(-11f, 2.2f), new Vector2(-11.4f, -4.1f) }), Vector3.back, 4, false, 4), 0.0016f, GhostC);
            gCurl = UIKit.Line(ghost, "Nail", UIKit.HandPolyline(Cm(new[] { new Vector2(-1.9f, 0.85f), new Vector2(-0.9f, 0.45f) }), Vector3.back, 5, false, 3), 0.0014f, GhostC);
            gThumb = UIKit.Line(ghost, "Thumb", Cm(ThumbOpen), 0.0022f, GhostC);
            gRay = UIKit.Line(transform, "GhostRay", new List<Vector3> { Vector3.zero, Vector3.zero }, 0.0015f, GhostC);
            gRay.useWorldSpace = true;
        }

        /// <summary>示范手这一帧：pinch 0 张开 → 1 捏住</summary>
        void PoseGhost(Vector3 tip, Vector3 pointDir, float pinch, float alpha)
        {
            var cam = Camera.main; if (!cam) return;
            var toCam = (cam.transform.position - tip).normalized;
            var x = Vector3.ProjectOnPlane(pointDir, toCam).normalized; if (x.sqrMagnitude < 1e-4f) x = cam.transform.right;
            // 本地 +z 背向相机（手画在 XY 面上、正对玩家），本地 +x = 指向；Unity 里 right = up × forward，所以 up = forward × right
            ghost.rotation = Quaternion.LookRotation(-toCam, Vector3.Cross(-toCam, x));
            ghost.position = tip;
            var th = new Vector2[ThumbOpen.Length];
            for (int i = 0; i < th.Length; i++) th[i] = Vector2.Lerp(ThumbOpen[i], ThumbClosed[i], pinch);
            var tp = Cm(th); gThumb.positionCount = tp.Count; gThumb.SetPositions(tp.ToArray());
            foreach (var lr in new[] { gIndex, gThumb, gPalm, gCurl }) UIKit.SetAlpha(lr, GhostC, alpha * 0.8f);
        }

        void UpdateGhost(Game g, bool holding, float dt)
        {
            if (!ghost) MakeGhost();
            var cam = Camera.main;
            bool show = cam && hintValid && g != null && g.Level.Tutorial && g.Status == "playing" && introT > 2.2f && teach != "play" && !(teach == "pick" && holding);
            ghostA = Mathf.MoveTowards(ghostA, show ? 1f : 0f, dt * 2f);
            if (ghostA <= 0.001f || !cam) { foreach (var lr in new[] { gIndex, gThumb, gPalm, gCurl, gRay }) if (lr) UIKit.SetAlpha(lr, GhostC, 0); return; }
            ghostT = (ghostT + dt) % 2.6f;
            var head = cam.transform.position;
            bool far = Vector3.Distance(head, hintPos) > 0.75f;
            // 手边的位置：胸前偏右下
            var handZone = head + cam.transform.forward * 0.4f + cam.transform.right * 0.24f - Vector3.up * 0.12f;   // 右前方、星核的右上（不压在星核上）
            float k = ghostT;
            float approach = Mathf.SmoothStep(0, 1, Mathf.Clamp01(k / 0.8f));
            float pinch = holding ? 1f - Mathf.SmoothStep(0, 1, Mathf.Clamp01((k - 1.0f) / 0.3f))      // 拿着：到缝那里松开
                                  : Mathf.SmoothStep(0, 1, Mathf.Clamp01((k - 0.9f) / 0.3f));            // 空手：伸过去捏住
            float fade = 1f - Mathf.Clamp01((k - 2.1f) / 0.5f);
            Vector3 tip, dir;
            if (!far)
            {
                var from = hintPos + (head - hintPos).normalized * 0.16f - Vector3.up * 0.05f;
                tip = Vector3.Lerp(from, hintPos + (head - hintPos).normalized * 0.015f, approach);
                dir = (hintPos - from).normalized;
                if (!holding && k > 1.3f) tip += (head - hintPos).normalized * Mathf.SmoothStep(0, 1, Mathf.Clamp01((k - 1.3f) / 0.6f)) * 0.08f;   // 捏住后往回拉一点
                UIKit.SetAlpha(gRay, GhostC, 0);
            }
            else
            {
                tip = handZone + (hintPos - handZone).normalized * 0.03f * approach;
                dir = (hintPos - handZone).normalized;
                gRay.SetPositions(new[] { tip + dir * 0.02f, Vector3.Lerp(tip, hintPos, Mathf.Clamp01(approach * 1.2f)) });
                UIKit.SetAlpha(gRay, GhostC, ghostA * fade * 0.35f);
            }
            PoseGhost(tip, dir, pinch, ghostA * fade * Mathf.Clamp01(k / 0.3f));
        }

        void Awake()
        {
            if (core) leavePos = core.localPosition + new Vector3(-0.25f, 0.02f, -0.05f);
            if (circleMat)
                for (int k = 0; k < 2; k++)
                {
                    var go = new GameObject("StashPad" + k); go.transform.SetParent(transform, false);
                    go.transform.localPosition = GameRunner.ToLocal(LAYOUT.Stash[k]) + Vector3.down * 0.005f;
                    go.transform.localRotation = Quaternion.Euler(90f - 30f, k == 0 ? 20f : -20f, 0);   // 平躺、朝玩家这边斜 30°
                    go.transform.localScale = Vector3.one * 0.15f;
                    go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                    var mr = go.AddComponent<MeshRenderer>(); stashMat[k] = new Material(circleMat); mr.sharedMaterial = stashMat[k];
                    stashMat[k].SetFloat("_Points", 6); stashMat[k].SetFloat("_Seed", 50 + k * 9);
                    stashMat[k].SetColor("_ColA", new Color(0.55f, 0.7f, 1f)); stashMat[k].SetColor("_ColB", new Color(0.85f, 0.92f, 1f));
                    stashPad[k] = go.transform;
                }
            levelName = UIText.Make(transform, "", UIKit.Face.Body, 0.05f, new Vector3(0, 1.62f, 0.95f), color: new Color(0.92f, 0.94f, 1f), raw: true);
            levelId = UIText.Make(transform, "", UIKit.Face.Italic, 0.022f, new Vector3(0, 1.69f, 0.95f), color: new Color(0.72f, 0.78f, 1f), raw: true);
            caption = UIText.Make(transform, "", UIKit.Face.Body, 0.024f, Vector3.zero, TMPro.TextAlignmentOptions.Left, new Color(0.9f, 0.93f, 1f));
            caption.gameObject.AddComponent<FaceHead>();
            toast = UIText.Make(transform, "", UIKit.Face.Italic, 0.03f, new Vector3(0, 1.5f, 0.6f), color: new Color(0.95f, 0.92f, 0.82f));
            hintRing = UIKit.Line(transform, "HintRing", UIKit.HandCircle(Vector3.zero, Vector3.back, 1f, 7), 0.0025f, Ink);
            hintRing.useWorldSpace = false;
            leave = Target(transform, "Leave", leavePos, 0.045f, () => flow.BackToSelect(), 1.2f);
            leaveGlyph = UIKit.Glyph(transform, "LeaveGlyph", leavePos, 0.05f);
            leaveLabel = UIText.Make(transform, "捏住不放 · 返回", UIKit.Face.Body, 0.015f, leavePos + new Vector3(0, -0.035f, 0), color: new Color(0.78f, 0.82f, 1f));
            leaveLabel.gameObject.AddComponent<FaceHead>();
            leaveRing = UIKit.Line(transform, "LeaveRing", new List<Vector3> { leavePos, leavePos }, 0.002f, Ink);
            for (int k = 0; k < comboTxt.Length; k++)
            {
                comboTxt[k] = UIText.Make(transform, "", UIKit.Face.Title, 0.05f, Vector3.zero, TMPro.TextAlignmentOptions.Center, new Color(1f, 0.92f, 0.72f), raw: true);
                comboTxt[k].spacing = 6f; comboTxt[k].Refresh();
                comboTxt[k].gameObject.AddComponent<FaceHead>();
                comboTxt[k].alpha = 0;
            }
            progRing = UIKit.Line(transform, "Progress", UIKit.HandCircle(Vector3.zero, Vector3.back, 1f, 61, 0, 360, 96), 0.0022f, new Color(0.78f, 0.86f, 1f));
            progRing.useWorldSpace = false;
            edgeHint = UIKit.Glyph(transform, "EdgeHint", Vector3.zero, 1f);
            if (runner)
            {
                runner.OnLevelStart += OnLevel;
                runner.OnEvent += OnEvent;
            }
        }

        void OnLevel()
        {
            var g = runner.Game; if (g == null) return;
            introT = 0; stage = -1; teach = ""; toastT = -1; explainedMatch = false;
            levelTotal = 0; progShown = 1f; holdLong = 0f;
            if (runner.Game != null && runner.Game.Level.Tutorial) taughtStash = false;
            for (int k = 0; k < comboT.Length; k++) comboT[k] = -1f;
            levelName.Set(Loc.Name(g.Level));
            levelId.Set(g.Level.Tutorial ? "" : g.Level.Id);
        }

        void OnEvent(GameEvent e)
        {
            // 连锁数字：从消除点浮起（连锁 ≥2 才显示，免得每次消除都跳字）
            if (e.Type == "eliminated" && e.Combo >= 2 && runner.Game != null && e.Ci >= 0 && e.Ci < runner.Game.Chains.Count)
            {
                int k = comboNext; comboNext = (comboNext + 1) % comboTxt.Length;
                comboN[k] = e.Combo; comboT[k] = 0f;
                comboAt[k] = runner.ToWorld(runner.Game.Chains[e.Ci].Track.PointAtU(System.Math.Max(0, e.S)));
                comboTxt[k].Set($"×{e.Combo}");
            }
            var g = runner.Game; if (g == null || !g.Level.Tutorial) return;
            if (e.Type == "stage") { stage = e.Stage; teach = e.Teach ?? ""; explainedMatch = false; if (teach == "play") Toast("别让星串碰到星核", 4f); }
            else if (e.Type == "eliminated" && !explainedMatch && stage == 0) { explainedMatch = true; Toast("三颗同色相连，就会消散", 3f); }
            else if (e.Type == "stageClear") Toast("很好", 1.6f);
        }

        float holdLong; bool taughtStash;

        void Toast(string key, float dur) { toastKey = key; toast.Set(key); toastT = dur; }

        // 这一刻该说的话（教学关）
        string CaptionKey(Game g, bool holding)
        {
            switch (teach)
            {
                case "pick": return holding ? "松开手，它会飞回串尾" : "指着中间那颗，捏一下";
                case "insert": return holding ? "把它放进同色之间" : "指着中间那颗，捏一下";
                case "far": return holding ? "指向发亮的缝，松手" : "远处的星：指着，捏住";
                default: return null;
            }
        }

        protected override void Apply(float v, float dt)
        {
            if (vis <= 0f && !shown) { if (gameObject.activeSelf) gameObject.SetActive(false); return; }
            var g = runner ? runner.Game : null;
            float t = Time.time;
            introT += dt;

            // 开场关名：0.8 秒浮现，停 2 秒，1 秒淡去
            float ia = Mathf.Clamp01(introT / 0.8f) * (1f - Mathf.Clamp01((introT - 2.8f) / 1.0f));
            levelName.alpha = ia * v; levelId.alpha = ia * v * 0.8f;
            levelName.transform.localPosition = new Vector3(0, 1.62f + introT * 0.006f, 0.95f);

            // 教学提示
            bool holding = false;
            if (g != null) foreach (var h in g.Hands.Values) if (h.Holding != null) holding = true;
            var hint = g?.Hint;
            hintValid = false;
            if (hint != null && !(teach == "pick" && holding))
            {
                if (hint.Type == "star" && gems && gems.Drawn.TryGetValue(hint.Id, out var sp)) { hintPos = sp; hintValid = true; }
                else if (hint.Type == "gap") { var gp = g.GapPosAt(hint.Ci, hint.Gap); if (gp != null) { hintPos = runner.ToWorld(gp.Pos); hintValid = true; } }
            }
            string ck = g != null && g.Level.Tutorial && g.Status == "playing" && introT > 2.2f ? CaptionKey(g, holding) : null;
            ringA = Mathf.MoveTowards(ringA, hintValid && introT > 2.2f ? 1f : 0f, dt * 3f);
            captionA = Mathf.MoveTowards(captionA, ck != null && (hintValid || holding) ? 1f : 0f, dt * 2.5f);
            if (ck != null && caption.key != ck) caption.Set(ck);
            if (hintValid)
            {
                float r = 0.055f * (1f + 0.08f * Mathf.Sin(t * 3f));
                hintRing.transform.position = hintPos;
                var cam = Camera.main;
                if (cam) hintRing.transform.rotation = Quaternion.LookRotation(hintPos - cam.transform.position) * Quaternion.Euler(0, 0, t * 25f);
                hintRing.transform.localScale = Vector3.one * r;
                hintRing.widthMultiplier = 0.0025f;
                caption.transform.position = hintPos + (cam ? cam.transform.right : Vector3.right) * 0.08f + Vector3.up * 0.03f;
            }
            UIKit.SetAlpha(hintRing, Ink, ringA * v * (0.7f + 0.3f * Mathf.Sin(t * 3f)));
            UpdateGhost(g, holding, dt);
            // 星盘
            for (int k = 0; k < 2; k++)
            {
                if (!stashMat[k]) continue;
                bool full = g != null && g.Stash[k] != null;
                bool target = false;
                if (g != null) foreach (var h in g.Hands.Values) if (h.Holding != null && h.Drop != null && h.Drop.Type == "stash" && h.Drop.Slot == k) target = true;
                float wantI = v * (target ? 1.6f : full ? 1.0f : 0.45f);
                stashMat[k].SetFloat("_Intensity", Mathf.Lerp(stashMat[k].GetFloat("_Intensity"), wantI, Mathf.Min(1f, dt * 6f)));
                stashMat[k].SetVector("_Spin", new Vector4(0.08f, -0.05f, 0.15f, -0.25f) * (target ? 4f : 1f));
                stashPad[k].localScale = Vector3.one * 0.15f * (target ? 1.15f : 1f) * Mathf.Max(0.001f, v);
            }
            caption.alpha = captionA * v;

            // 星盘教学：拿着星迟迟放不下 → 告诉他可以先存进胸前的星盘（只教一次）
            if (g != null && !taughtStash && g.Status == "playing")
            {
                bool hasStashFree = false;
                for (int k = 0; k < g.Stash.Length; k++) if (g.Stash[k] == null) hasStashFree = true;
                holdLong = holding && hasStashFree ? holdLong + dt : 0f;
                if (holdLong > 3f) { taughtStash = true; Toast("放不下时，先存进胸前星盘", 3.5f); }
            }

            // 小提示（很好 / 三连消散 / 守住星核）
            if (toastT > 0) toastT -= dt;
            float ta = toastT > 0 ? Mathf.Clamp01(toastT / 0.6f) : 0f;
            toast.alpha = Mathf.Min(1f, ta) * v;

            // 连锁数字：浮起 + 放大 + 淡出（0.9 秒）
            for (int k = 0; k < comboTxt.Length; k++)
            {
                if (comboT[k] < 0) { comboTxt[k].alpha = 0; continue; }
                comboT[k] += dt;
                float e = comboT[k] / 0.9f;
                if (e >= 1f) { comboT[k] = -1f; comboTxt[k].alpha = 0; continue; }
                var cam2 = Camera.main;
                comboTxt[k].transform.position = comboAt[k] + Vector3.up * (0.04f + e * 0.1f) + (cam2 ? (cam2.transform.position - comboAt[k]).normalized * 0.05f : Vector3.zero);
                float pop = 1f + 0.5f * Mathf.Exp(-e * 9f);
                comboTxt[k].height = (0.035f + 0.012f * comboN[k]) * pop;
                comboTxt[k].Refresh();
                comboTxt[k].alpha = Mathf.Min(1f, (1f - e) * 2.2f) * v;
            }

            // 进度：星核外一圈刻度，按剩余星数收短（消一颗少一格，不加文字）
            if (g != null && core)
            {
                int left = 0, total = 0;
                foreach (var c in g.Chains) { left += c.Sim.Count; total += Mathf.Max(1, c.Sim.Count); }
                if (levelTotal <= 0) levelTotal = Mathf.Max(1, left);
                float frac = Mathf.Clamp01(left / (float)levelTotal);
                progShown = Mathf.MoveTowards(progShown, frac, dt * 0.8f);
                var arc = UIKit.HandCircle(Vector3.zero, Vector3.back, 1f, 61, 95f, -350f * progShown, 96);
                progRing.positionCount = arc.Count; progRing.SetPositions(arc.ToArray());
                progRing.transform.position = core.position;
                var camP = Camera.main;
                if (camP) progRing.transform.rotation = Quaternion.LookRotation(core.position - camP.transform.position);
                progRing.transform.localScale = Vector3.one * 0.135f;
                UIKit.SetAlpha(progRing, new Color(0.78f, 0.86f, 1f), v * 0.5f);
            }
            else UIKit.SetAlpha(progRing, new Color(0.78f, 0.86f, 1f), 0f);

            // 紧张：串头在视野外时，边缘一抹暖光指方向（越近星核越亮）
            {
                var camE = Camera.main;
                float eh = 0f;
                if (g != null && camE != null)
                {
                    float danger = 0f; Vector3 headPos = Vector3.zero; bool has = false;
                    foreach (var c in g.Chains)
                    {
                        if (c.Sim.Count == 0) continue;
                        var h0 = c.Sim.Stars[0];
                        float d = (float)(h0.S / System.Math.Max(1.0, c.Track.Slots));
                        if (d > danger) { danger = d; headPos = runner.ToWorld(c.Track.PointAtU(System.Math.Max(0, h0.S))); has = true; }
                    }
                    if (has && danger > 0.55f)
                    {
                        var dir = (headPos - camE.transform.position).normalized;
                        float ahead = Vector3.Dot(dir, camE.transform.forward);
                        if (ahead < 0.35f)   // 不在正前方 → 提示
                        {
                            eh = Mathf.Clamp01((danger - 0.55f) / 0.35f) * (0.6f + 0.4f * Mathf.Sin(t * 6f));
                            var side = Vector3.ProjectOnPlane(dir, camE.transform.up).normalized;
                            float sgn = Mathf.Sign(Vector3.Dot(side, camE.transform.right));
                            if (sgn == 0) sgn = 1;
                            edgeHint.transform.position = camE.transform.position + camE.transform.forward * 0.6f + camE.transform.right * sgn * 0.42f;
                            edgeHint.transform.localScale = Vector3.one * 0.5f;
                        }
                    }
                }
                UIKit.GlyphColor(edgeHint, new Color(1f, 0.6f, 0.35f), eh * 0.9f * v);
            }

            // 返回（暂停时收起：暂停面板里有自己的“返回”）
            pauseK = Mathf.MoveTowards(pauseK, PauseView.Paused ? 0f : 1f, Time.unscaledDeltaTime * 5f);
            leave.allowed = !PauseView.Paused;
            float la = v * pauseK * (0.4f + 0.6f * leave.hover);
            leaveLabel.alpha = la;
            UIKit.GlyphColor(leaveGlyph, new Color(0.85f, 0.9f, 1f), 0.8f * la);
            float prog = leave.holdSeconds > 0 ? Mathf.Clamp01(leave.holdT / leave.holdSeconds) : 0f;
            if (prog > 0.01f)
            {
                var arc = UIKit.HandCircle(leavePos, Vector3.back, 0.03f, 3, 90f, -360f * prog, 48);
                leaveRing.positionCount = arc.Count; leaveRing.SetPositions(arc.ToArray());
                UIKit.SetAlpha(leaveRing, Ink, v);
            }
            else UIKit.SetAlpha(leaveRing, Ink, 0f);
        }
    }
}
