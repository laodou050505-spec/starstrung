using System.Collections.Generic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 时空裂缝（11-25 按用户要求重做）：
    /// - 一开始没有裂缝，是真实房间；每通一关裂开一道不规则的口子（共 20 道），口子里露出同一片星空天空盒；
    /// - 位置随机自然：第一道在正前方、第二道在正后方，之后用“最远候选点”散在四周和头顶（仰角 2°～72°，不开在地板上），每道新裂缝都开在还没裂的那片房间上；
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
            // 位置（11-26 修）：以前按黄金角 + 随机摆，没管和旧裂缝的距离 → 第 6 / 16 / 18 道离旧的只有 8° / 4° / 1°，
            // 新裂缝直接开在已经长大的旧裂缝里面，过关后看起来“什么都没多”。
            // 现在用“最远候选点”：每道生成 40 个随机候选位置（仰角 2°～72°，按面积均匀），取离所有已有裂缝最远的那个
            // → 仍然是随机、自然的分布，但每道新裂缝都开在还没裂开的那片房间上；之后旧裂缝继续长大、慢慢连成一片。
            Vector3 Dir(float az, float el) => Quaternion.Euler(-el, az, 0) * Vector3.forward;
            float s0 = Mathf.Sin(2f * Mathf.Deg2Rad), s1 = Mathf.Sin(72f * Mathf.Deg2Rad);   // 仰角 2°～72°：四周墙面到头顶，不开在地板上
            for (int k = 0; k < Total; k++)
            {
                if (k == 0) center[k] = Dir(-6f, 26f);          // 第一道：正前方（一抬眼就看到）
                else if (k == 1) center[k] = Dir(172f, 34f);    // 第二道：正后方
                else
                {
                    Vector3 best = Vector3.up; float bestD = -1f;
                    for (int c = 0; c < 40; c++)
                    {
                        float az = Rn() * 360f, el = Mathf.Asin(Mathf.Lerp(s0, s1, Rn())) * Mathf.Rad2Deg;
                        var d = Dir(az, el); float m = 999f;
                        for (int j = 0; j < k; j++) m = Mathf.Min(m, Vector3.Angle(d, center[j]));
                        if (m > bestD) { bestD = m; best = d; }
                    }
                    center[k] = best;
                }
                sizeVar[k] = 0.85f + 0.3f * Rn();          // 大小有差异，但不超过 ±15%
            }
            SetOpen(Count(Cleared()), true);
            if (runner) runner.OnEvent += e =>
            {
                if (e.Type != "won" || runner.Game.Level.Tutorial || runner.Game.Level.Endless) return;
                // 11-26 修：AppFlow 也在“过关”时记存档，两边谁先执行不确定。以前比较“记之前 / 记之后”的通关数，
                // AppFlow 先记了就永远相等 → 新裂缝不开（要重启游戏才一下子冒出来）。现在直接按存档里的通关数补齐到画面上
                MarkCleared(runner.Game.Level.Id);
                int n = Count(Cleared());
                if (n > targetN) SetOpen(n, false);
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
                // 初始 0.065、每关长 0.022（弧度）：模拟过——20 关里没有一关的新裂缝开在旧裂缝里面（以前 0.075 / 0.03 后 4～5 关看不见新的），
                // 第 20 关约 8 成空间已裂开，结局再全部张满
                float R = sizeVar[k] * (0.065f + 0.022f * age) * appear;
                blobs[k] = new Vector4(center[k].x, center[k].y, center[k].z, R > 0.002f ? R : 0f);
            }
            inst.SetVectorArray("_B", blobs);
            inst.SetFloat("_Final", finalK);
            dome.gameObject.SetActive(shownN > 0.001f || finalK > 0f);
        }
    }
}
