using System.Collections.Generic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 设置（标题和选关都在）：右下方三个手绘小图标，悬在手边，指着捏切换。
    /// - 地球（无字）+ “切换语言 / Language”：中英切换；
    /// - 音符 + “音乐”、声波 + “音效”：四档音量（关 / 低 / 中 / 高）循环，图标下面三个小点表示档位。
    /// 图标都是手绘细线（微弯、两头收尖），不规整地错落摆着。
    /// </summary>
    public class SettingsView : FlowView
    {
        class Item { public PickTarget pick; public List<LineRenderer> lines = new(); public UIText label; public Renderer[] dots; public Vector3 pos; }
        readonly List<Item> items = new();
        static readonly Color Ink = new(0.82f, 0.88f, 1f, 1f);
        static readonly Color DotC = new(0.95f, 0.9f, 0.78f);
        /// <summary>设置按钮的位置（三个图标在它左上方展开）</summary>
        [System.NonSerialized] public Vector3 anchor = new(-0.25f, 1.24f, 0.3f);

        void Awake()
        {
            // 位置在 LateStart 里按设置按钮摆（错落，不排成一条直线）
            var globe = Make(Vector3.zero, "切换语言 / Language", () => Loc.Toggle(), false);
            Globe(globe);
            var music = Make(Vector3.zero, "音乐", () => AudioKit.MusicLevel = (AudioKit.MusicLevel + 3) % 4, true);
            Note(music);
            var sfx = Make(Vector3.zero, "音效", () => { AudioKit.SfxLevel = (AudioKit.SfxLevel + 3) % 4; AudioKit.Tick(); }, true);
            Waves(sfx);
            var rc = Make(Vector3.zero, "重新对准", () => { var r = Object.FindAnyObjectByType<GameRunner>(); if (r) r.Recenter(); AudioKit.Tick(); }, false);
            Aim(rc);
            fadeIn = 0.35f; fadeOut = 0.3f;
        }

        Item Make(Vector3 pos, string key, System.Action act, bool hasDots)
        {
            var it = new Item { pos = pos };
            var holder = new GameObject("Setting " + key).transform; holder.SetParent(transform, false); holder.localPosition = pos;
            holder.gameObject.AddComponent<FaceHead>().yawOnly = false;
            it.pick = Target(transform, key, pos, 0.035f, act);
            it.label = UIText.Make(holder, key, UIKit.Face.Body, 0.0135f, new Vector3(0, -0.038f, 0), color: new Color(0.8f, 0.85f, 1f));
            if (key.Contains("Language")) it.label.raw = true;
            if (hasDots)
            {
                it.dots = new Renderer[3];
                for (int k = 0; k < 3; k++) it.dots[k] = UIKit.Glyph(holder, "Dot", new Vector3((k - 1) * 0.009f, -0.025f, 0), 0.012f);
            }
            items.Add(it);
            it.pick.transform.SetParent(holder, true);
            return it;
        }

        LineRenderer Stroke(Item it, List<Vector3> pts, float w = 0.0016f)
        {
            var lr = UIKit.Line(it.label.transform.parent, "Stroke", pts, w, Ink);
            it.lines.Add(lr);
            return lr;
        }

        // 图标在 holder 的局部平面（x 右 y 上，法线 -z 朝玩家）里画，尺寸约 3 cm
        void Globe(Item it)
        {
            var n = Vector3.back;
            Stroke(it, UIKit.HandCircle(Vector3.zero, n, 0.014f, 11));
            Stroke(it, UIKit.HandPolyline(Ellipse(0.0065f, 0.014f, 90f, 270f), n, 12, false, 3));
            Stroke(it, UIKit.HandPolyline(Ellipse(0.0065f, 0.014f, -90f, 90f), n, 13, false, 3));
            Stroke(it, UIKit.HandPolyline(new[] { new Vector3(-0.014f, 0, 0), new Vector3(0.014f, 0, 0) }, n, 14));
            Stroke(it, UIKit.HandPolyline(new[] { new Vector3(-0.0115f, 0.0075f, 0), new Vector3(0.0115f, 0.0075f, 0) }, n, 15));
            Stroke(it, UIKit.HandPolyline(new[] { new Vector3(-0.0115f, -0.0075f, 0), new Vector3(0.0115f, -0.0075f, 0) }, n, 16));
        }
        static List<Vector3> Ellipse(float rx, float ry, float a0, float a1)
        {
            var l = new List<Vector3>();
            for (int k = 0; k <= 16; k++) { float a = Mathf.Lerp(a0, a1, k / 16f) * Mathf.Deg2Rad; l.Add(new Vector3(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry, 0)); }
            return l;
        }
        void Note(Item it)
        {
            var n = Vector3.back;
            Stroke(it, UIKit.HandCircle(new Vector3(-0.005f, -0.009f, 0), n, 0.0045f, 21));
            Stroke(it, UIKit.HandPolyline(new[] { new Vector3(-0.0006f, -0.008f, 0), new Vector3(-0.0004f, 0.013f, 0), new Vector3(0.009f, 0.008f, 0), new Vector3(0.0095f, 0.002f, 0) }, n, 22, false, 6));
        }
        void Waves(Item it)
        {
            var n = Vector3.back;
            Stroke(it, UIKit.HandCircle(new Vector3(-0.009f, 0, 0), n, 0.0022f, 31));
            for (int k = 0; k < 3; k++) Stroke(it, UIKit.HandCircle(new Vector3(-0.009f, 0, 0), n, 0.006f + k * 0.0045f, 32 + k, -48f, 96f, 18));
        }

        // 重新对准：一圈不闭合的手绘圆 + 四道朝里的短刻度 + 中心一点（“对准中心”），和准星同一种语言
        void Aim(Item it)
        {
            var n = Vector3.back;
            Stroke(it, UIKit.HandCircle(Vector3.zero, n, 0.012f, 41, 20f, 320f, 48));
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI * 0.5f; var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                Stroke(it, UIKit.HandPolyline(new[] { d * 0.017f, d * 0.0085f }, n, 42 + k));
            }
            Stroke(it, UIKit.HandCircle(Vector3.zero, n, 0.0018f, 47));
        }

        // 顺序：地球、音乐、音效、重新对准。“切换语言 / Language”这行字最宽 → 地球放在弧线最左端（外侧是空的，字往空处伸，不压别的）；
        // 音乐、音效在上方，重新对准在左下；彼此约 15 cm（11-25：语言那行字曾压到“退出”、又压到“音乐”）
        static readonly Vector3[] Fan = { new(-0.42f, 0.02f, -0.03f), new(-0.12f, 0.19f, -0.06f), new(-0.27f, 0.16f, -0.05f), new(-0.33f, -0.15f, -0.02f) };

        protected override void Apply(float v, float dt)
        {
            if (vis <= 0f && !shown) { if (gameObject.activeSelf) gameObject.SetActive(false); return; }
            // 从设置按钮里“长”出来：展开时沿弧线滑到各自的位置
            for (int i = 0; i < items.Count; i++)
            {
                var holder = items[i].label.transform.parent;
                holder.localPosition = anchor + Fan[i] * Mathf.SmoothStep(0, 1, Mathf.Clamp01(vis * 1.3f - i * 0.1f));
            }
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                float a = Mathf.Clamp01((shownT - i * 0.08f) / 0.3f) * v;
                float hv = it.pick.hover;
                foreach (var lr in it.lines) UIKit.SetAlpha(lr, Ink, a * (0.6f + 0.4f * hv));
                it.label.alpha = a * (0.55f + 0.45f * hv);
                it.label.transform.parent.localScale = Vector3.one * (1f + 0.12f * hv);
                if (it.dots != null)
                {
                    int lv = i == 1 ? AudioKit.MusicLevel : AudioKit.SfxLevel;
                    for (int k = 0; k < 3; k++) UIKit.GlyphColor(it.dots[k], DotC, (k < lv ? 0.9f : 0.12f) * a);
                }
            }
        }
    }
}
