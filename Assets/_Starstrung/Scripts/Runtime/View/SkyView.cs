using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 房间里的星空氛围：
    /// - 夜蓝罩：包住玩家的大球，把透视画面罩一层夜蓝（浓度随界面变：封面 / 选关深一点，玩的时候 32%）；
    /// - 错峰闪烁的星：几百颗暗星点 + 几十颗四芒星，散在 1.6～6 m 的上半空间（不在脚下、不贴脸），每颗闪烁周期 2～7 秒、相位随机。
    /// 一个网格一次画完（一个 draw call）。
    /// </summary>
    public class SkyView : MonoBehaviour
    {
        public Material starMat, veilMat;
        public int dots = 520, crosses = 70;
        [Range(0, 1)] public float veil = 0.32f;
        Material veilInst;

        void Start()
        {
            if (veilMat)
            {
                var sphere = new GameObject("NightVeil"); sphere.transform.SetParent(transform, false);
                sphere.transform.localPosition = new Vector3(0, 1.6f, 0); sphere.transform.localScale = Vector3.one * 40f;
                sphere.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
                var mr = sphere.AddComponent<MeshRenderer>(); veilInst = new Material(veilMat); mr.sharedMaterial = veilInst;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (!starMat) return;
            int n = dots + crosses;
            var v = new Vector3[n * 4]; var uv = new Vector2[n * 4]; var d = new Vector4[n * 4]; var col = new Color[n * 4]; var tri = new int[n * 6];
            var rnd = new System.Random(77);
            float R() => (float)rnd.NextDouble();
            Color[] pal = { new(0.75f, 0.88f, 1f), new(0.6f, 0.7f, 1f), new(0.85f, 0.7f, 1f), new(1f, 0.95f, 0.85f), new(0.55f, 0.95f, 1f) };
            for (int i = 0; i < n; i++)
            {
                bool cross = i >= dots;
                // 上半空间：仰角 -5°～80°，距离 1.6～6 m
                float az = R() * 360f, el = Mathf.Lerp(-5f, 80f, Mathf.Pow(R(), 0.8f)), dist = Mathf.Lerp(1.6f, 6f, R());
                var p = new Vector3(0, 1.6f, 0) + Quaternion.Euler(-el, az, 0) * Vector3.forward * dist;
                float size = cross ? Mathf.Lerp(0.012f, 0.03f, R()) * (dist / 3f) : Mathf.Lerp(0.004f, 0.009f, R()) * (dist / 3f);
                var data = new Vector4(size, R() * 6.28f, Mathf.Lerp(2f, 7f, R()), cross ? 1 : 0);
                var c = pal[rnd.Next(pal.Length)] * (cross ? 1f : Mathf.Lerp(0.35f, 0.8f, R()));
                for (int k = 0; k < 4; k++)
                {
                    v[i * 4 + k] = p; d[i * 4 + k] = data; col[i * 4 + k] = c;
                    uv[i * 4 + k] = new Vector2(k == 1 || k == 2 ? 1 : 0, k >= 2 ? 1 : 0);
                }
                int b = i * 4; int t = i * 6;
                tri[t] = b; tri[t + 1] = b + 2; tri[t + 2] = b + 1; tri[t + 3] = b; tri[t + 4] = b + 3; tri[t + 5] = b + 2;
            }
            var m = new Mesh { name = "SkyStars" };
            m.vertices = v; m.uv = uv; m.SetUVs(1, d); m.colors = col; m.triangles = tri;
            m.bounds = new Bounds(new Vector3(0, 1.6f, 0), Vector3.one * 14f);
            var go = new GameObject("Stars"); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = starMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Update()
        {
            if (veilInst) veilInst.SetFloat("_Amount", Mathf.MoveTowards(veilInst.GetFloat("_Amount"), veil, Time.deltaTime * 0.15f));
        }
    }
}
