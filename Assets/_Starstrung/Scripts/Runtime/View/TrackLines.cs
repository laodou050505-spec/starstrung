using System.Collections.Generic;
using Starstrung.Logic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>临时路线显示（细线）。视觉阶段会换成星图刻度带。岔路两条都画，信标指着的那条亮。</summary>
    public class TrackLines : MonoBehaviour
    {
        public GameRunner runner;
        public Material lineMat;
        readonly List<(LineRenderer lr, int ci, int pi, bool fork)> lines = new();

        void OnEnable() { if (runner) runner.OnLevelStart += Rebuild; }
        void Start() => Rebuild();

        void Rebuild()
        {
            foreach (var l in lines) if (l.lr) Destroy(l.lr.gameObject);
            lines.Clear();
            var g = runner ? runner.Game : null;
            if (g == null) return;
            for (int ci = 0; ci < g.Tracks.Count; ci++)
            {
                var tr = g.Tracks[ci];
                var paths = tr is ForkTrack ft ? ft.Paths : new ITrack[] { tr };
                for (int pi = 0; pi < paths.Length; pi++)
                {
                    var go = new GameObject($"Track_{ci}_{pi}");
                    go.transform.SetParent(transform, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.sharedMaterial = lineMat; lr.useWorldSpace = true; lr.widthMultiplier = 0.004f;
                    var pts = paths[pi].Pts;
                    var arr = new Vector3[(pts.Count + 1) / 2];
                    for (int i = 0; i < arr.Length; i++) arr[i] = runner.ToWorld(pts[Mathf.Min(pts.Count - 1, i * 2)]);
                    lr.positionCount = arr.Length; lr.SetPositions(arr);
                    lines.Add((lr, ci, pi, tr is ForkTrack));
                }
            }
        }

        void Update()
        {
            var g = runner ? runner.Game : null;
            if (g == null) return;
            foreach (var l in lines)
            {
                float a = 0.5f;
                if (l.fork && g.Fork != null) { var tr = (ForkTrack)g.Tracks[l.ci]; a = g.Fork.Committed ? (l.pi == tr.Active ? 0.6f : 0.1f) : (l.pi == g.Fork.Next ? 0.7f : 0.2f); }
                var c = new Color(0.91f, 0.83f, 0.64f, a);
                l.lr.startColor = l.lr.endColor = c;
            }
        }
    }
}
