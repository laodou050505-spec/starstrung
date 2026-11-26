using System.Collections.Generic;
using Starstrung.Logic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 特殊星的标识（11-24 用户：特殊星没体现出来，玩家不知道）：
    /// - 双子：外面的“8”字双环 + 两颗之间一道细线（拆散就断，一眼看出它们要成对消）；
    /// - 星爆：八根放射尖刺 + 中心亮点（橙金色，像要爆开）；
    /// - 停摆：带刻度的环 + 一段缓慢转动的弧（时间）；
    /// - 冰封、彩星在宝石本体上已经有外观（冰壳 / 虹彩），这里不重复。
    /// 全部宝石星一次实例化画完（一个 draw call）；双子之间的连线另用几条手绘细线。
    /// </summary>
    public class StarBadges : MonoBehaviour
    {
        public GemField gems;
        public Material badgeMat;
        const int Max = 64;
        readonly Matrix4x4[] mats = new Matrix4x4[Max];
        readonly Vector4[] cols = new Vector4[Max];
        readonly float[] shape = new float[Max], k = new float[Max], rot = new float[Max];
        MaterialPropertyBlock mpb;
        readonly List<LineRenderer> links = new();
        static readonly Color TwinC = new(0.65f, 0.9f, 1f);
        static readonly Color BombC = new(1f, 0.72f, 0.32f);
        static readonly Color StopC = new(0.8f, 0.78f, 1f);

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            // 双子之间的细线（最多 8 对，循环用）
            for (int i = 0; i < 8; i++)
            {
                var lr = UIKit.Line(transform, "TwinLink" + i, new List<Vector3> { Vector3.zero, Vector3.zero }, 0.0022f, TwinC);
                lr.useWorldSpace = true; lr.enabled = false;
                links.Add(lr);
            }
        }

        void LateUpdate()
        {
            if (!gems || !badgeMat) return;
            var g = gems.runner ? gems.runner.Game : null;
            int n = 0, ln = 0;
            float t = Time.time;
            if (g != null)
                foreach (var c in g.Chains)
                {
                    var st = c.Sim.Stars;
                    for (int i = 0; i < st.Count; i++)
                    {
                        var s = st[i];
                        if (s.Kind == StarKind.Normal || s.Kind == StarKind.Ice || s.Kind == StarKind.Rainbow) continue;
                        if (!gems.Drawn.TryGetValue(s.Id, out var p) || n >= Max) continue;
                        float size = (float)c.Track.SizeAtU(System.Math.Max(0, s.S)) * 1.9f;
                        int sh = s.Kind == StarKind.Twin ? 1 : s.Kind == StarKind.Bomb ? 2 : 3;
                        mats[n] = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * size);
                        cols[n] = sh == 1 ? (Vector4)TwinC : sh == 2 ? (Vector4)BombC : (Vector4)StopC;
                        shape[n] = sh; k[n] = 0.8f + 0.2f * Mathf.Sin(t * 2.4f + s.Id); rot[n] = sh == 3 ? t * 0.25f : t * 0.4f;
                        n++;
                        // 双子：连线到搭档（只画一次：只在 id 较小的那颗画）
                        if (s.Kind == StarKind.Twin && s.Twin > 0 && s.Id < s.Twin && ln < links.Count && gems.Drawn.TryGetValue(s.Twin, out var q))
                        {
                            var lr = links[ln++];
                            var d = (q - p).normalized; var mid = (p + q) * 0.5f + d * 0.02f;
                            lr.positionCount = 3; lr.SetPositions(new[] { p, mid, q });
                            UIKit.SetAlpha(lr, TwinC, 0.55f);
                            lr.enabled = true;
                        }
                    }
                }
            for (; ln < links.Count; ln++) links[ln].enabled = false;
            if (n == 0) return;
            mpb.Clear();
            mpb.SetVectorArray("_Color", cols); mpb.SetFloatArray("_Shape", shape); mpb.SetFloatArray("_K", k); mpb.SetFloatArray("_Rot", rot);
            var rp = new RenderParams(badgeMat) { matProps = mpb, worldBounds = new Bounds(Vector3.zero, Vector3.one * 40f), layer = gameObject.layer };
            Graphics.RenderMeshInstanced(rp, Quad, 0, mats, n);
        }

        static Mesh quad;
        static Mesh Quad => quad ? quad : quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
    }
}
