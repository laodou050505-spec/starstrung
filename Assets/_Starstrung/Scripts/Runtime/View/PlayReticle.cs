using System.Collections.Generic;
using Starstrung.Logic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 关卡里的星盘准星（11-25：用户“用鼠标都不好识别”——以前只有星边缘一点点亮，看不出会捏到哪颗）。
    /// - 空手指着星：那颗星外面一个手绘细圆 + 四道斜刻度，颜色随星色、慢慢转；捏下去那一刻收紧一下；
    /// - 拿着星指缝隙：准星挪到要插进去的缝上（香槟金，略小），和串张开的空位一起提示“会插在这”；
    /// - 线宽按离眼睛的距离放大，3～4 米外的星上也是清楚的一道细线，不会细到看不见。
    /// 和菜单里的准星（MenuPicker）同一种画法，两只手各一个。
    /// </summary>
    public class PlayReticle : MonoBehaviour
    {
        public GemField field;
        static readonly Color Ink = new(0.86f, 0.92f, 1f, 1f);
        static readonly Color Gold = new(1f, 0.86f, 0.58f, 1f);

        class R { public Transform t; public LineRenderer[] lines; public float a; public Vector3 pos; public float rad; public Color col; public bool wasHold; public float squeeze; }
        readonly R[] rs = { new(), new() };

        void Make(R r, int side)
        {
            r.t = new GameObject("PlayReticle" + side).transform; r.t.SetParent(transform, false);
            var lines = new List<LineRenderer> { UIKit.Line(r.t, "Ring", UIKit.HandCircle(Vector3.zero, Vector3.back, 1f, 140 + side, 0, 360, 72), 0.002f, Ink) };
            for (int k = 0; k < 4; k++)
            {
                float a = (45f + k * 90f) * Mathf.Deg2Rad; var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                lines.Add(UIKit.Line(r.t, "Tick", new List<Vector3> { d * 1.14f, d * 1.46f }, 0.002f, Ink));
            }
            r.lines = lines.ToArray();
        }

        void LateUpdate()
        {
            var runner = field ? field.runner : null;
            var g = runner ? runner.Game : null;
            var cam = Camera.main;
            float dt = Time.deltaTime;
            for (int i = 0; i < 2; i++)
            {
                var r = rs[i];
                if (!r.t) Make(r, i);
                bool show = false;
                Hand h = null;
                if (g != null && g.Status == "playing" && field.presence > 0.5f && g.Hands.TryGetValue(i == 0 ? "left" : "right", out h) && h.Tracked)
                {
                    if (h.Holding == null && h.Hover != null && h.Hover.Type == "star" && field.Drawn.TryGetValue(h.Hover.Id, out var p))
                    {
                        show = true; r.pos = p; r.rad = field.DrawnSize.TryGetValue(h.Hover.Id, out var sz) ? sz * 0.5f : 0.06f;
                        var st = FindStar(g, h.Hover.Id);
                        r.col = st != null && st.Color >= 0 ? Color.Lerp(Palette.Of(st.Color), Color.white, 0.45f) : Ink;
                    }
                    else if (h.Holding != null && h.Drop != null && (h.Drop.Type == "gap" || h.Drop.Type == "mouth"))
                    {
                        show = true; r.pos = runner.ToWorld(h.Drop.Pos); r.rad = h.Drop.Type == "mouth" ? 0.16f : 0.055f; r.col = Gold;
                    }
                }
                // 捏下去 / 放下去那一刻：准星收紧一下（“锁定了”的手感）
                bool holding = h != null && h.Holding != null;
                if (holding != r.wasHold) r.squeeze = 1f;
                r.wasHold = holding;
                r.squeeze = Mathf.MoveTowards(r.squeeze, 0f, dt * 5f);

                r.a = Mathf.MoveTowards(r.a, show ? 1f : 0f, dt * (show ? 10f : 6f));
                if (r.a > 0f)
                {
                    r.t.position = r.pos;
                    float dist = cam ? Vector3.Distance(cam.transform.position, r.pos) : 1f;
                    if (cam) r.t.rotation = Quaternion.LookRotation(r.pos - cam.transform.position) * Quaternion.Euler(0, 0, Time.time * 25f + i * 45f);
                    float pulse = 1f + 0.05f * Mathf.Sin(Time.time * 5f);
                    r.t.localScale = Vector3.one * r.rad * 1.18f * pulse * Mathf.Lerp(1.35f, 1f, r.a) * (1f - 0.18f * r.squeeze);
                    // 线宽：约 0.16° 视角（近处 1.8 mm，4 米外 11 mm）
                    float w = Mathf.Max(0.0018f, dist * 0.0028f);
                    foreach (var lr in r.lines) { lr.widthMultiplier = w; UIKit.SetAlpha(lr, r.col, r.a * 0.95f); }
                }
                else foreach (var lr in r.lines) UIKit.SetAlpha(lr, r.col, 0f);
            }
        }

        static Star FindStar(Game g, int id)
        {
            foreach (var c in g.Chains) foreach (var s in c.Sim.Stars) if (s.Id == id) return s;
            return null;
        }
    }
}
