using Starstrung.Logic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 星核：让各个结构一直在动，并随局面变化。
    /// - 三道黄铜星环像陀螺仪：各自绕本地轴自转，同时整体缓慢进动，三道速度、方向都不同；中间那道环上跑两颗小卫星；
    /// - 两条光带沿倾斜的轨道绕星核流动；七片水晶碎片绕着星核漂浮、自转、上下浮动；
    /// - 心跳：星云的心和中心四芒星一起“咚咚”两下一拍；
    /// - 串头逼近星核：魔法阵光盘闪烁预警，下一道裂纹先在球面上闪；
    /// - 被撞：整颗闪白，裂纹 +1，对应一道星环开始抖动、宝石变暗（三道都坏了就是输）；
    /// - 连锁共鸣：连锁 ≥2 时星核跟着“共鸣”——星环瞬间转快再慢慢回落、星心亮一下、魔法阵光盘亮一下，连锁越长越强。
    /// </summary>
    public class CoreView : MonoBehaviour
    {
        public GameRunner runner;
        public MeshRenderer nebula;
        public Transform flare, disk;
        public MagicCircleStack diskStack;
        public Transform[] rings, moons, ribbons, shards;

        MaterialPropertyBlock nebMpb, diskMpb;
        MaterialPropertyBlock[] ringMpb;
        float flash, crackShown, danger, resonance, spinPhase;
        /// <summary>结局蓄力 0..1：裂纹爬满、星环越转越快、心跳加速、星核越来越亮</summary>
        [System.NonSerialized] public float endingCharge;
        readonly Quaternion[] ringTilt = { Quaternion.Euler(62, 0, 18), Quaternion.Euler(-38, 40, 0), Quaternion.Euler(12, -30, 74) };
        readonly float[] ringSpin = { 22f, -15f, 10f };
        readonly Vector3[] ringPrecess = { new(0, 6, 0), new(4, 0, 3), new(0, -5, 2) };

        void OnEnable()
        {
            nebMpb = new MaterialPropertyBlock(); diskMpb = new MaterialPropertyBlock();
            ringMpb = new MaterialPropertyBlock[rings != null ? rings.Length : 0];
            for (int i = 0; i < ringMpb.Length; i++) ringMpb[i] = new MaterialPropertyBlock();
            if (runner) { runner.OnEvent += OnEvent; runner.OnLevelStart += () => { crackShown = 0; flash = 0; }; }
        }
        void OnDisable() { if (runner) runner.OnEvent -= OnEvent; }

        void OnEvent(GameEvent e)
        {
            if (e.Type == "coreHit") flash = 1f;
            if (e.Type == "eliminated" && e.Combo >= 2) resonance = Mathf.Max(resonance, Mathf.Min(1.4f, 0.35f + 0.22f * e.Combo));
        }

        void Update()
        {
            float t = Time.time, dt = Time.deltaTime;
            resonance = Mathf.MoveTowards(resonance, 0f, dt * 0.7f);
            spinPhase += dt * (1f + resonance * 5f + endingCharge * endingCharge * 12f);   // 共鸣时星环转快，回落时平滑减速（相位累加，不跳）
            
            var g = runner ? runner.Game : null;
            int cracks = g != null ? g.Cracks : 0;
            crackShown = Mathf.MoveTowards(crackShown, cracks, dt * 1.5f);   // 裂纹慢慢“裂开”，不是瞬间出现
            flash = Mathf.Max(0, flash - dt * 1.8f);
            // 危险度：任何一条串的串头离星核还剩不到 12% 的路
            float d = 0;
            if (g != null) foreach (var c in g.Chains) if (c.Sim.Count > 0) d = Mathf.Max(d, Mathf.InverseLerp(0.88f, 0.98f, (float)((c.Sim.Stars[0].S + c.Sim.HeadOffset) / c.Track.Slots)));
            danger = Mathf.Lerp(danger, d, dt * 3f);

            // 心跳：每 1.5 秒“咚咚”两下
            float ph = Mathf.Repeat(t, 1.5f);
            float beat = Mathf.Exp(-ph * 9f) + 0.6f * Mathf.Exp(-Mathf.Max(0, ph - 0.28f) * 9f) * (ph > 0.28f ? 1 : 0);
            float rate = 1f + danger * 0.8f + endingCharge * 2.5f;
            nebMpb.SetFloat("_Pulse", Mathf.Clamp01(beat * rate));
            nebMpb.SetFloat("_Cracks", Mathf.Max(crackShown, endingCharge * 3f));
            nebMpb.SetFloat("_CrackWarn", danger);
            nebMpb.SetFloat("_Flash", Mathf.Max(flash, endingCharge * endingCharge * 0.5f));
            if (nebula) nebula.SetPropertyBlock(nebMpb);
            if (flare) flare.localScale = Vector3.one * (0.075f * 1.1f) * (0.85f + 0.35f * beat + flash * 0.8f + resonance * 0.5f);

            // 星环：陀螺仪式自转 + 进动；坏掉的环抖动、宝石变暗
            for (int i = 0; i < rings.Length; i++)
            {
                bool broken = i < Mathf.FloorToInt(crackShown + 0.001f);
                float wob = broken ? 1f : 0f;
                var prec = Quaternion.Euler(ringPrecess[i] * t);
                var shake = Quaternion.Euler(Mathf.Sin(t * 23f + i) * 4f * wob, 0, Mathf.Cos(t * 19f + i) * 4f * wob);
                rings[i].localRotation = prec * ringTilt[i] * shake * Quaternion.Euler(0, ringSpin[i] * spinPhase * (broken ? 0.4f : 1f), 0);
                rings[i].localPosition = broken ? new Vector3(Mathf.Sin(t * 13f + i) * 0.002f, Mathf.Cos(t * 11f) * 0.002f, 0) : Vector3.zero;
                var mr = rings[i].GetComponent<MeshRenderer>();
                ringMpb[i].SetColor("_EmissionColor", Color.white * (broken ? 0.2f + 0.2f * Mathf.Abs(Mathf.Sin(t * 9f)) : 1.3f + danger * Mathf.Abs(Mathf.Sin(t * 12f))));
                mr.SetPropertyBlock(ringMpb[i]);
            }
            // 小卫星沿中间那道环跑（环的本地 XZ 面上）
            float rr = 0.075f * 1.68f;   // 卫星骑在中间那道环的上边沿跑（不往外凸，免得碰到外环）
            for (int i = 0; i < moons.Length; i++)
            {
                float a = t * 0.9f + i * Mathf.PI;
                // 卫星挂在环上：本地坐标要除以环自身的缩放（环缩放为 1）
                moons[i].localPosition = new Vector3(Mathf.Cos(a) * rr, 0.011f + 0.0062f, Mathf.Sin(a) * rr);
            }
            // 光带：倾斜轨道，缓慢转动
            for (int i = 0; i < ribbons.Length; i++)
                ribbons[i].localRotation = Quaternion.Euler(0, t * (i == 0 ? 9f : -6f), 0) * Quaternion.Euler(i == 0 ? 24f : -16f, i * 70f, i == 0 ? 8f : 30f);
            // 水晶碎片：绕星核漂浮
            for (int i = 0; i < shards.Length; i++)
            {
                float k = i / (float)shards.Length;
                float a = t * (0.18f + 0.05f * (i % 3)) + k * Mathf.PI * 2f;
                // 在魔法阵光盘上方、星核下方漂（路线是从星核侧面水平进来的，碎片不穿过路线）
                float r = 0.165f + 0.045f * (0.5f + 0.5f * Mathf.Sin(i * 1.7f));   // 在星环扫过的球壳（≤ 15 cm）之外
                float y = -0.085f - 0.02f * (0.5f + 0.5f * Mathf.Sin(i * 2.3f)) + 0.008f * Mathf.Sin(t * 0.8f + i);
                shards[i].localPosition = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                shards[i].localRotation = Quaternion.Euler(20f * Mathf.Sin(i), t * (25f + i * 6f), 15f * Mathf.Cos(i));
            }
            // 魔法阵光盘：危险时闪
            if (diskStack) { diskStack.warn = danger; diskStack.boost = resonance; }
        }
    }
}
