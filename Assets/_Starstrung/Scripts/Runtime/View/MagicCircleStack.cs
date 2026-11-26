using System.Collections.Generic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 多层立体魔法阵（星核下方的光盘、路线入口都用）：
    /// - 三层完整的魔法阵沿法线方向叠放，但不规整：大小、间距、偏心、倾角、芒数、亮度都错开，各自转、各自摆、偏心缓缓漂；
        /// - 边缘有小光点缓缓沿法线方向飘升。
    /// 本组件的 +Y 是魔法阵的法线方向（光点往 +Y 飘）；Build() 由编辑器搭场景时调用。
    /// </summary>
    public class MagicCircleStack : MonoBehaviour
    {
        public float radius = 0.2f;      // 最大一层的半径（米）
        public float spacing = 0.025f;   // 层间距（米）
        public int points = 7;
        public Color colA = new(0.55f, 0.45f, 1f), colB = new(0.75f, 0.9f, 1f), colC = new(1f, 0.45f, 0.85f);
        public float intensity = 1.2f;
        public float seed;   // 笔迹种子：不同的魔法阵手绘笔迹不同
        public Material circleMat, moteMat, tubeMat;
        public Mesh torus;
        [Tooltip("外圈加发光玻璃环管（入口用：侧着看也有厚度）")] public bool tube;
        public Mesh quad;

        [System.Serializable] public class Layer { public Transform t; public float spin, tilt, tiltSpeed; public Vector3 basePos; }
        public List<Layer> layers = new();
        public ParticleSystem motes;
        public float warn;
        [System.NonSerialized] public float boost;   // 连锁共鸣时亮一下
        MaterialPropertyBlock mpb;

        // 每层都是完整的魔法阵（和最早的单层光盘一样的图案），但叠得不规整：
        // 大小、高度间距、偏心、倾角、芒数、亮度都错开，转速转向也不同，看着像随手叠上去的几张光阵。
        // (高度 ×spacing, 半径比, 偏心 x/z ×radius, 倾斜 度, 绕 Y 自转 度/秒, 芒数增量, 亮度, 画哪些)
        static readonly (float h, float r, float ox, float oz, float tilt, float spin, int dp, float k, Vector4 mask)[] Plan =
        {
            (0f,    1.00f,  0.00f,  0.00f, 2.5f,   5f,  0, 1.00f, new Vector4(1, 1, 1, 1)),
            (1.3f,  0.71f,  0.07f, -0.05f, 7.0f, -11f, -2, 0.70f, new Vector4(1, 1, 0, 1)),
            (2.1f,  0.46f, -0.09f,  0.06f, 11f,   19f,  2, 0.55f, new Vector4(1, 0, 1, 1)),
        };

        public void Build()
        {
            foreach (Transform c in transform) Object.DestroyImmediate(c.gameObject);
            layers.Clear();
            for (int i = 0; i < Plan.Length; i++)
            {
                var p = Plan[i];
                var go = new GameObject($"Layer{i}"); go.transform.SetParent(transform, false);
                var holder = new GameObject("Quad"); holder.transform.SetParent(go.transform, false);
                holder.transform.localRotation = Quaternion.Euler(90, 0, 0);       // 方片躺平，法线朝 +Y
                holder.transform.localScale = Vector3.one * radius * 2f * p.r;
                holder.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = holder.AddComponent<MeshRenderer>(); mr.sharedMaterial = circleMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.transform.localPosition = Vector3.up * p.h * spacing + new Vector3(p.ox, 0, p.oz) * radius;
                layers.Add(new Layer { t = go.transform, spin = p.spin, tilt = p.tilt, tiltSpeed = 0.11f + i * 0.09f, basePos = go.transform.localPosition });
            }
            // 外圈的发光玻璃环管：底层一道粗的、顶层一道细的（侧着看时魔法阵也有体积，不是几条细线）
            if (tube && torus && tubeMat)
            {
                for (int k = 0; k < 2; k++)
                {
                    var tg = new GameObject("Tube" + k); tg.transform.SetParent(transform, false);
                    tg.transform.localPosition = Vector3.up * (k == 0 ? 0f : Plan[Plan.Length - 1].h * spacing);
                    tg.transform.localScale = Vector3.one * radius * (k == 0 ? 0.97f : Plan[Plan.Length - 1].r * 0.98f);
                    tg.AddComponent<MeshFilter>().sharedMesh = torus;
                    var mr = tg.AddComponent<MeshRenderer>(); mr.sharedMaterial = tubeMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            // 飘升的小光点
            if (moteMat)
            {
                var go = new GameObject("Motes"); go.transform.SetParent(transform, false);
                go.transform.localRotation = Quaternion.Euler(-90, 0, 0);          // 粒子系统的 Z = 本组件的 +Y
                motes = go.AddComponent<ParticleSystem>();
                motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = motes.main; main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 0.08f, radius * 0.22f); main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.03f, radius * 0.07f);
                main.startColor = new ParticleSystem.MinMaxGradient(colB, colA); main.maxParticles = 120; main.simulationSpace = ParticleSystemSimulationSpace.Local;
                var em = motes.emission; em.rateOverTime = 8f;
                var sh = motes.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius * 0.85f; sh.radiusThickness = 0.3f; sh.rotation = new Vector3(0, 0, 0);
                var col = motes.colorOverLifetime; col.enabled = true;
                var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(0, 1) }); col.color = g;
                var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = moteMat;
            }
        }

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            for (int i = 0; i < layers.Count && i < Plan.Length; i++)
            {
                var mr = layers[i].t.GetComponentInChildren<MeshRenderer>();
                mpb.Clear(); mpb.SetVector("_Mask", Plan[i].mask); mpb.SetFloat("_Seed", i * 3.7f + seed);
                // 上面几层颜色往青 / 洋红偏一点，不和底层一模一样
                mpb.SetColor("_ColA", Color.Lerp(colA, i == 1 ? colB : colC, i * 0.22f)); mpb.SetColor("_ColB", colB); mpb.SetColor("_ColC", colC);
                mpb.SetFloat("_Points", Mathf.Max(5, points + Plan[i].dp)); mpb.SetFloat("_Intensity", intensity * Plan[i].k);
                // 图案内部各圈也各自转（和最早的单层光盘一样），每层速度错开
                float sg = i % 2 == 0 ? 1f : -1f;
                mpb.SetVector("_Spin", new Vector4(0.08f, -0.05f, 0.15f, -0.25f) * sg * (1f + i * 0.35f));
                mr.SetPropertyBlock(mpb);
            }
            if (motes) motes.Play();
        }

        void Update()
        {
            float t = Time.time;
            for (int i = 0; i < layers.Count; i++)
            {
                var L = layers[i];
                // 自转 + 绕不同方向轻轻摆（倾斜角随时间变化），层与层转起来互相交错
                var tilt = Quaternion.Euler(Mathf.Sin(t * L.tiltSpeed + i) * L.tilt, 0, Mathf.Cos(t * L.tiltSpeed * 0.8f + i * 2) * L.tilt);
                L.t.localRotation = tilt * Quaternion.Euler(0, t * L.spin, 0);
                // 偏心也慢慢漂（上面几层绕着底层缓缓游走，不死板地钉在一处）
                if (i > 0) L.t.localPosition = L.basePos + new Vector3(Mathf.Sin(t * 0.13f + i * 2.1f), Mathf.Sin(t * 0.21f + i) * 0.25f, Mathf.Cos(t * 0.17f + i * 1.3f)) * radius * 0.035f;
                var mr = L.t.GetComponentInChildren<MeshRenderer>();
                mr.GetPropertyBlock(mpb); mpb.SetFloat("_Warn", warn); if (i < Plan.Length) mpb.SetFloat("_Intensity", intensity * Plan[i].k * (1f + boost * 1.5f)); mr.SetPropertyBlock(mpb);
            }
        }
    }
}
