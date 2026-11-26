using Unity.Profiling;
using UnityEngine;

namespace Starstrung
{
    /// <summary>性能探针（测试用，运行时临时挂上）：记录最近 60 帧的绘制批次、SetPass、三角面数和帧时间的最大值 / 平均值。</summary>
    public class PerfProbe : MonoBehaviour
    {
        ProfilerRecorder batches, setpass, tris, verts;
        public float avgBatches, maxBatches, avgSetPass, maxTris, avgMs;
        int n;
        void OnEnable()
        {
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        }
        void OnDisable() { batches.Dispose(); setpass.Dispose(); tris.Dispose(); }
        public void ResetStats() { n = 0; avgBatches = maxBatches = avgSetPass = maxTris = avgMs = 0; }
        void Update()
        {
            n++; float k = 1f / Mathf.Min(n, 60);
            avgBatches += (batches.LastValue - avgBatches) * k; maxBatches = Mathf.Max(maxBatches, batches.LastValue);
            avgSetPass += (setpass.LastValue - avgSetPass) * k; maxTris = Mathf.Max(maxTris, tris.LastValue);
            avgMs += (Time.unscaledDeltaTime * 1000f - avgMs) * k;
        }
        public string Report() => $"绘制 平均 {avgBatches:0} 最多 {maxBatches:0} · SetPass {avgSetPass:0} · 三角面最多 {maxTris / 1000f:0}k · 帧 {avgMs:0.0} ms";
    }
}
