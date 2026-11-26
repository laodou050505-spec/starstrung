using UnityEngine;

namespace Starstrung
{
    /// <summary>一只手这一帧的状态（世界坐标）。真机来自 PICO 手部追踪，编辑器里来自 DeskSim 模拟手。</summary>
    public struct HandState
    {
        public bool tracked;
        /// <summary>捏合（带滞回、去掉握拳误判后的结果）。</summary>
        public bool pinch;
        /// <summary>拇指尖和食指尖的中点：捏住的那一点，也是“近摘 / 近放”的判定点。</summary>
        public Vector3 pinchPoint;
        public Vector3 palm, palmNormal, indexTip;
        /// <summary>四指平均伸展度（指尖到腕 / 指根到腕）：握拳 ≈ 1.0–1.15，放松 ≈ 1.3–1.5，张开 ≈ 1.8–2.0。</summary>
        public float curl;
        /// <summary>手掌射线：从肩膀经过手掌（远摘 / 远放用）。</summary>
        public Ray ray;
        public bool hasRay;
    }
}
