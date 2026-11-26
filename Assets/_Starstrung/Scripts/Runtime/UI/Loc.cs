using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 双语文本表：以中文原文为键，对应英文。代码里只写 Loc.T("中文")，不写死英文。
    /// 语言默认跟随系统；玩家在标题页切换后存进 PlayerPrefs。
    /// 带参数的用 string.Format 占位：Loc.F("已撕开 {0}/20 道时空裂缝", n)。
    /// 关卡名在 Levels.cs 里自带中英（Level.Name），用 Loc.Name(level) 取。
    /// </summary>
    public static class Loc
    {
        public static readonly Dictionary<string, string> En = new()
        {
            // 标题
            ["摘星"] = "STARSTRUNG",
            ["手可摘星辰"] = "Reach out, pluck a star",
            ["Starstrung"] = "Starstrung",
            ["REACH OUT · PLUCK A STAR"] = "REACH OUT · PLUCK A STAR",
            ["开始"] = "Begin",
            ["设置"] = "Settings",
            ["退出"] = "Exit",
            ["捏住不放 · 退出"] = "Hold the pinch · Exit",
            ["切换语言 / Language"] = "切换语言 / Language",
            ["音乐"] = "Music",
            ["音效"] = "Sound",
            ["重新对准"] = "Recenter",
            ["Tripo XR 出品"] = "Tripo XR Developers",
            // 选关
            ["序章"] = "Prologue",
            ["晨曦"] = "Daybreak",
            ["霜川"] = "Frostwater",
            ["焰云"] = "Emberveil",
            ["深空"] = "The Deep",
            ["已撕开 {0}/20 道时空裂缝"] = "{0} of 20 rifts torn open",
            ["先通过序章"] = "Clear the Prologue first",

            // 关卡中
            ["捏住不放 · 返回"] = "Hold the pinch · Leave",
            ["连锁 ×{0}"] = "Chain ×{0}",
            ["吞星兽 · 退散"] = "Star Eater · Repelled",
            ["星盘"] = "Star dish",
            // 教学（每段一句，短）
            ["指着中间那颗，捏一下"] = "Point at the middle one, pinch",
            ["三颗同色相连，就会消散"] = "Three alike touching — they scatter",
            ["松开手，它会飞回串尾"] = "Let go — it flies back to the tail",
            ["把它放进同色之间"] = "Set it between its own color",
            ["远处的星：指着，捏住"] = "Far stars: point, then pinch",
            ["指向发亮的缝，松手"] = "Point at the glowing gap, let go",
            ["别让星串碰到星核"] = "Keep the chain off the core",
            ["很好"] = "Nicely done",
            ["放不下时，先存进胸前星盘"] = "Nowhere to put it? Park it on a dish",
            ["指着星盘，松手"] = "Point at the dish, let go",
            // 结算
            ["星核守住了"] = "The core holds",
            ["星核裂开了"] = "The core has cracked",
            ["分数"] = "Score",
            ["最大连锁"] = "Best chain",
            ["用时"] = "Time",
            ["重来"] = "Retry",
            ["下一关"] = "Next",
            ["返回"] = "Back",
            ["捏住它"] = "Pinch it",
            ["已暂停"] = "Paused",
            ["继续"] = "Resume",
            ["暂停"] = "Pause",
            ["{0} 秒"] = "{0}s",
        };

        public static bool English { get; private set; }
        public static event Action Changed;
        const string Key = "starstrung.lang";

        static Loc()
        {
            var saved = PlayerPrefs.GetString(Key, "");
            English = saved == "en" || (saved == "" && Application.systemLanguage != SystemLanguage.Chinese
                && Application.systemLanguage != SystemLanguage.ChineseSimplified && Application.systemLanguage != SystemLanguage.ChineseTraditional);
        }

        public static void Toggle() => Set(!English);
        public static void Set(bool english)
        {
            English = english;
            PlayerPrefs.SetString(Key, english ? "en" : "zh"); PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static string T(string zh)
        {
            if (!English) return zh;
            if (En.TryGetValue(zh, out var en)) return en;
#if UNITY_EDITOR
            Debug.LogWarning($"[Loc] 缺英文：{zh}");
#endif
            return zh;
        }
        public static string F(string zh, params object[] args) => string.Format(T(zh), args);
        public static string Name(Logic.Level L) => L == null ? "" : English ? L.Name.En : L.Name.Zh;

        /// <summary>表里出现的所有文字（编辑器裁字体子集、预烘图集用）</summary>
        public static string AllText()
        {
            var sb = new StringBuilder();
            foreach (var kv in En) { sb.Append(kv.Key); sb.Append(kv.Value); }
            foreach (var L in Logic.Levels.LEVELS) { sb.Append(L.Name.Zh); sb.Append(L.Name.En); sb.Append(L.Id); }
            return sb.ToString();
        }
    }
}
