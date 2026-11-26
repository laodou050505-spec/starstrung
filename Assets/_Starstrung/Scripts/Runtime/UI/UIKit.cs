using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 界面工具：字体、3D 文字、手绘细线、四芒星光点。
    /// 界面风格（用户 11-24 定的基调）：星座 + 手绘细线 + 小字 + 四芒星，像星图上随手标注的，不要面板、不要规整的框。
    /// 资源都放在 Resources（字体：Resources/Fonts；材质：Resources/UI）。
    /// </summary>
    public static class UIKit
    {
        public enum Face { Body, Italic, Title, Brand, Serif, Script }

        static readonly Dictionary<string, TMP_FontAsset> fonts = new();
        public static TMP_FontAsset Font(Face f, bool english)
        {
            string n = f switch
            {
                Face.Title => english ? "TitleEn" : "TitleZh",
                Face.Brand => english ? "Brand" : "BrandZh",
                Face.Italic => english ? "UIEnItalic" : "UIZh",
                Face.Serif => english ? "UIEn" : "SerifZh",
                Face.Script => "Script",
                _ => english ? "UIEn" : "UIZh",
            };
            if (!fonts.TryGetValue(n, out var fa) || !fa) fonts[n] = fa = Resources.Load<TMP_FontAsset>("Fonts/" + n);
            return fa;
        }

        static Material lineMat, glyphMat;
        public static Material LineMat => lineMat ? lineMat : lineMat = Resources.Load<Material>("UI/HandLine");
        public static Material GlyphMat => glyphMat ? glyphMat : glyphMat = Resources.Load<Material>("UI/StarGlyph");

        /// <summary>
        /// 手绘折线：把几个关键点连成一笔，每段微微弯（一笔划过去的弧度）+ 很小的抖动，段与段之间稍微出头。
        /// size = 整体尺度（米），决定弯曲 / 抖动幅度；不歪歪扭扭（弯曲约段长 2%，抖动约 0.4%）。
        /// </summary>
        public static List<Vector3> HandPolyline(IList<Vector3> pts, Vector3 normal, int seed, bool closed = false, int perSeg = 14)
        {
            var outPts = new List<Vector3>();
            var rnd = new System.Random(seed);
            int segs = closed ? pts.Count : pts.Count - 1;
            for (int i = 0; i < segs; i++)
            {
                Vector3 a = pts[i], b = pts[(i + 1) % pts.Count];
                var d = b - a; float L = d.magnitude; if (L < 1e-5f) continue;
                var side = Vector3.Cross(normal, d / L).normalized;
                float bow = ((float)rnd.NextDouble() - 0.5f) * 0.045f * L;
                float ph = (float)rnd.NextDouble() * 6.28f;
                int n = Mathf.Max(4, perSeg);
                for (int k = 0; k <= n; k++)
                {
                    if (i > 0 && k == 0) continue;
                    float u = k / (float)n;
                    float off = bow * Mathf.Sin(u * Mathf.PI) + L * 0.004f * Mathf.Sin(u * 17f + ph) + L * 0.0025f * Mathf.Sin(u * 41f + ph * 2f);
                    outPts.Add(a + d * u + side * off);
                }
            }
            return outPts;
        }

        /// <summary>手绘圆：半径略有起伏，首尾稍微错开不完全闭合（像一笔画的圈）</summary>
        public static List<Vector3> HandCircle(Vector3 c, Vector3 normal, float r, int seed, float from = 0f, float sweep = 360f, int n = 64)
        {
            var rnd = new System.Random(seed);
            float s1 = (float)rnd.NextDouble() * 6.28f, s2 = (float)rnd.NextDouble() * 6.28f;
            var u = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var v = Vector3.Cross(normal, u);
            var list = new List<Vector3>();
            float over = sweep >= 359f ? 14f : 0f;   // 闭合圈多画一点、错开
            for (int k = 0; k <= n; k++)
            {
                float a = (from + (sweep + over) * k / n) * Mathf.Deg2Rad;
                float rr = r * (1f + 0.012f * Mathf.Sin(a * 3f + s1) + 0.006f * Mathf.Sin(a * 7f + s2)) + r * 0.03f * (k / (float)n) * (over > 0 ? 1 : 0);
                list.Add(c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * rr);
            }
            return list;
        }

        public static LineRenderer Line(Transform parent, string name, List<Vector3> pts, float width, Color col, bool local = true)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = !local; lr.sharedMaterial = LineMat; lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch; lr.numCapVertices = 0; lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.widthMultiplier = width; lr.positionCount = pts.Count; lr.SetPositions(pts.ToArray());
            lr.startColor = lr.endColor = col;
            return lr;
        }

        public static void SetAlpha(LineRenderer lr, Color baseCol, float a)
        {
            var c = baseCol; c.a = baseCol.a * a; lr.startColor = lr.endColor = c;
        }

        /// <summary>四芒星光点（面朝相机的方片，FxGlow 模式 0）</summary>
        public static Renderer Glyph(Transform parent, string name, Vector3 localPos, float size)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos; go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = GlyphMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mr;
        }

        static MaterialPropertyBlock mpb;
        public static void GlyphColor(Renderer r, Color c, float intensity)
        {
            mpb ??= new MaterialPropertyBlock();
            mpb.Clear(); mpb.SetColor("_Color", c); mpb.SetColor("_Color2", Color.Lerp(c, new Color(0.55f, 0.5f, 1f), 0.5f)); mpb.SetFloat("_Intensity", intensity);
            r.SetPropertyBlock(mpb);
            r.enabled = intensity > 0.002f;
        }
    }

    /// <summary>
    /// 3D 文字（TextMeshPro）：key 是中文原文，语言切换时自动换文字和字体。
    /// height = 一行字的高度（米）。alpha 用来做出现 / 消失的过程。
    /// </summary>
    public class UIText : MonoBehaviour
    {
        public string key;
        public object[] args;
        public UIKit.Face face;
        public float height = 0.03f;
        public Color color = new(0.86f, 0.9f, 1f, 1f);
        public bool raw;          // true = key 原样显示（数字等），不查表
        public float? spacing;    // 字距（为空用默认）
        [Range(0, 1)] public float alpha = 1f;
        public TextMeshPro tmp;
        float shownAlpha = -1;

        // TextMeshPro 3D：fontSize 10 ≈ 一行高 1 米 × 0.1（这里统一用 fontSize = 高度 × 系数）
        const float SizePerMeter = 10f / 1.0f;

        public static UIText Make(Transform parent, string key, UIKit.Face face, float height, Vector3 localPos,
            TextAlignmentOptions align = TextAlignmentOptions.Center, Color? color = null, bool raw = false)
        {
            var go = new GameObject("Text " + key); go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var t = go.AddComponent<UIText>();
            t.key = key; t.face = face; t.height = height; t.raw = raw;
            if (color.HasValue) t.color = color.Value;
            t.tmp = go.AddComponent<TextMeshPro>();
            t.tmp.alignment = align;
            t.tmp.textWrappingMode = TextWrappingModes.NoWrap;
            t.tmp.overflowMode = TextOverflowModes.Overflow;
            t.tmp.rectTransform.sizeDelta = new Vector2(2f, 0.5f);
            // 文字框的锚点跟着对齐方式走：左对齐的字从这个点开始往右写，右对齐的字写到这个点为止
            bool left = align == TextAlignmentOptions.Left || align == TextAlignmentOptions.TopLeft || align == TextAlignmentOptions.BottomLeft;
            bool right = align == TextAlignmentOptions.Right || align == TextAlignmentOptions.TopRight || align == TextAlignmentOptions.BottomRight;
            t.tmp.rectTransform.pivot = new Vector2(left ? 0f : right ? 1f : 0.5f, 0.5f);
            t.tmp.raycastTarget = false;
            t.Refresh();
            return t;
        }

        void OnEnable() { Loc.Changed += Refresh; Refresh(); }
        void OnDisable() { Loc.Changed -= Refresh; }

        public void Set(string newKey, params object[] newArgs) { key = newKey; args = newArgs; Refresh(); }

        public void Refresh()
        {
            if (!tmp) return;
            bool en = Loc.English;
            tmp.font = UIKit.Font(face, en);
            string s = raw ? key : (args != null && args.Length > 0 ? Loc.F(key, args) : Loc.T(key));
            if (face == UIKit.Face.Title && en) s = s.ToUpperInvariant();
            tmp.text = s;
            tmp.fontSize = height * SizePerMeter * (en && face != UIKit.Face.Title ? 1.12f : 1f);   // 英文字形偏小，略放大
            tmp.characterSpacing = spacing ?? (face == UIKit.Face.Title ? (en ? 18f : 8f) : (en ? 2f : 6f));
            shownAlpha = -1;
            Apply();
        }

        void LateUpdate() { if (!Mathf.Approximately(shownAlpha, alpha)) Apply(); }

        void Apply()
        {
            if (!tmp) return;
            var c = color; c.a *= alpha; tmp.color = c;
            tmp.enabled = alpha > 0.003f;
            shownAlpha = alpha;
        }
    }

    /// <summary>始终转向玩家（只转水平方向，字不歪）</summary>
    public class FaceHead : MonoBehaviour
    {
        public bool yawOnly = true;
        void LateUpdate()
        {
            var cam = Camera.main; if (!cam) return;
            var d = transform.position - cam.transform.position;
            if (yawOnly) d.y = 0;
            if (d.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }
    }
}
