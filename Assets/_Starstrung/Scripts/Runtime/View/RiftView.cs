using System.Collections.Generic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 时空裂缝（11-25 按用户要求重做）：
    /// - 一开始没有裂缝，是真实房间；每通一关裂开一道不规则的口子（共 20 道），口子里露出同一片星空天空盒；
    /// - 位置随机自然：第一道在正前方、第二道在正后方，之后按黄金角加随机扰动散在四周和头顶（仰角 8°～72°，不开在地板上）；
    /// - 大小有差异但不悬殊（±15%）；已有的口子随关卡一点点变大，碰上了就合成一个大口（着色器里平滑并集 + 同一张噪声当边界）；
    /// - 边界不规则、一直在轻轻起伏变化；结局 SetFinal：张满整个空间、边光消失，只剩星空天空盒。
    /// 过程：通关时在 openSeconds 秒内平滑长到新的大小，新口子从一点裂开；开始长的那一刻响一声。
    /// </summary>
    public class RiftView : MonoBehaviour
    {
        public GameRunner runner;
        public Material riftMat;
        public float radius = 5.5f;
        public float openSeconds = 3.2f;
        [System.NonSerialized] public bool quiet;   // 结局里只响第一声

        const int Total = 20;

        Material inst; Transform dome;
        float shownN = -1f, targetN; float finalK;
        readonly Vector4[] blobs = new Vector4[Total];
        readonly Vector3[] center = new Vector3[Total];
        readonly float[] sizeVar = new float[Total];
        const string SaveKey = "starstrung.cleared";

        public static HashSet<string> Cleared()
        {
            var s = PlayerPrefs.GetString(SaveKey, "");
            return new HashSet<string>(s.Split(',', System.StringSplitOptions.RemoveEmptyEntries));
        }
        public static void MarkCleared(string id)
        {
            var set = Cleared(); if (!set.Add(id)) return;
            PlayerPrefs.SetString(SaveKey, string.Join(",", set)); PlayerPrefs.Save();
        }
        static int Count(HashSet<string> set) { int n = 0; foreach (var id in set) if (id != "T" && id != "E") n++; return Mathf.Min(Total, n); }

        void Start()
        {
            var sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            var go = new GameObject("RiftDome"); go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 1.6f, 0); go.transform.localScale = Vector3.one * radius * 2f;
            go.AddComponent<MeshFilter>().sharedMesh = sphere;
            var mr = go.AddComponent<MeshRenderer>(); inst = new Material(riftMat) { name = "RiftDome(实例)" }; mr.sharedMaterial = inst;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            dome = go.transform;
            var rnd = new System.Random(11);
            float Rn() => (float)rnd.NextDouble();
            for (int k = 0; k < Total; k++)
            {
                float az, el;
                if (k == 0) { az = -6f; el = 26f; }          // 第一道：正前方（一抬眼就看到）
                else if (k == 1) { az = 172f; el = 34f; }    // 第二道：正后方
                else { az = k * 137.5f + 40f + (Rn() - 0.5f) * 40f; el = Mathf.Lerp(8f, 72f, Mathf.Repeat(k * 0.618f + 0.37f + (Rn() - 0.5f) * 0.2f, 1f)); }
                center[k] = Quaternion.Euler(-el, az, 0) * Vector3.forward;
                sizeVar[k] = 0.85f + 0.3f * Rn();          // 大小有差异，但不超过 ±15%
            }
            SetOpen(Count(Cleared()), true);
            if (runner) runner.OnEvent += e =>
            {
                if (e.Type != "won" || runner.Game.Level.Tutorial || runner.Game.Level.Endless) return;
                int before = Count(Cleared());
                MarkCleared(runner.Game.Level.Id);
                int after = Count(Cleared());
                if (after > before) SetOpen(after, false);
            };
        }

        /// <summary>通关数 n 对应的裂缝大小（instant = 直接到位，不演过程）</summary>
        public void SetOpen(int n, bool instant)
        {
            targetN = Mathf.Clamp(n, 0, Total);
            if (instant || shownN < 0f) shownN = targetN;
            else if (targetN > shownN + 0.01f && !quiet) AudioKit.Instance?.PlayClip(AudioKit.Instance.riftOpen, 0.8f);
        }

        /// <summary>结局：0..1，开口张满整个球面、边光消失（只剩星空天空盒）</summary>
        public void SetFinal(float k) => finalK = Mathf.Clamp01(k);

        void Update()
        {
            if (!inst) return;
            shownN = Mathf.MoveTowards(shownN, targetN, Time.deltaTime / openSeconds);
            for (int k = 0; k < Total; k++)
            {
                // 第 k 道在通关数从 k 走到 k+1 的那几秒里从一点裂开；之后每多过一关长大一圈
                float appear = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(shownN - k));
                float age = Mathf.Max(0f, shownN - (k + 1));
                float R = sizeVar[k] * (0.075f + 0.03f * age) * appear;
                blobs[k] = new Vector4(center[k].x, center[k].y, center[k].z, R > 0.002f ? R : 0f);
            }
            inst.SetVectorArray("_B", blobs);
            inst.SetFloat("_Final", finalK);
            dome.gameObject.SetActive(shownN > 0.001f || finalK > 0f);
        }
    }
}
