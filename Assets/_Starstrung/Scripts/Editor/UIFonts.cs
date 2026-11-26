// 生成 TMP 字体资源（Resources/Fonts）。源字体是 Tools/subset_fonts.py 裁出来的子集（只含游戏用字，都是 SIL OFL）。
// 预先把双语表里所有字烘进图集；打包时保留预烘结果（真机不用现生成，避免卡顿）。
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Starstrung.EditorTools
{
    public static class UIFonts
    {
        const string Src = "Assets/_Starstrung/Art/Fonts/";
        const string Dst = "Assets/_Starstrung/Resources/Fonts/";

        [MenuItem("Starstrung/生成字体资源")]
        public static void Build()
        {
            var zh = Make("UIZh.ttf", "UIZh", 2048, 64);
            var en = Make("UIEn.ttf", "UIEn", 1024, 56);
            var enI = Make("UIEnItalic.ttf", "UIEnItalic", 1024, 56);
            var tz = Make("TitleZh.ttf", "TitleZh", 2048, 90);
            var te = Make("TitleEn.ttf", "TitleEn", 1024, 64);
            var sz = Make("SerifZh.ttf", "SerifZh", 2048, 64);
            Make("Script.ttf", "Script", 1024, 64);
            Fallback(te, tz); Fallback(sz, en);
            var br = Make("Brand.ttf", "Brand", 1024, 56);
            var brz = Make("BrandZh.otf", "BrandZh", 2048, 56);
            // 英文界面里偶尔有汉字（“切换语言 / Language”）：英文字体缺字时退到中文
            Fallback(en, zh); Fallback(enI, zh); Fallback(br, brz); Fallback(zh, en);
            Underlay();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 所有界面字加一圈柔和的深色衬底（TMP 的 Underlay）：字在亮的银河 / 星光前也看得清（11-24 用户：选关星图太亮看不清字）。
        /// 衬底是半透明深夜蓝、边缘很软，不是描边框。
        /// </summary>
        [MenuItem("Starstrung/字体加深色衬底")]
        public static void Underlay()
        {
            foreach (var n in new[] { "UIZh", "UIEn", "UIEnItalic", "SerifZh", "TitleEn", "TitleZh", "Brand", "BrandZh", "Script" })
            {
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Dst + n + ".asset");
                if (!fa || !fa.material) continue;
                var m = fa.material;
                m.EnableKeyword("UNDERLAY_ON");
                m.SetColor("_UnderlayColor", new Color(0.01f, 0.015f, 0.05f, 0.78f));
                m.SetFloat("_UnderlaySoftness", 0.75f); m.SetFloat("_UnderlayDilate", 0.65f);
                m.SetFloat("_UnderlayOffsetX", 0); m.SetFloat("_UnderlayOffsetY", 0);
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
        }

        static void Fallback(TMP_FontAsset a, TMP_FontAsset b)
        {
            if (!a || !b) return;
            a.fallbackFontAssetTable = new List<TMP_FontAsset> { b }; EditorUtility.SetDirty(a);
        }

        static TMP_FontAsset Make(string src, string name, int atlas, int size)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(Src + src);
            if (!font) { Debug.LogError("[摘星] 找不到字体 " + src); return null; }
            var dst = Dst + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(dst)) AssetDatabase.DeleteAsset(dst);
            var fa = TMP_FontAsset.CreateFontAsset(font, size, 9, GlyphRenderMode.SDFAA, atlas, atlas, AtlasPopulationMode.Dynamic, true);
            fa.name = name;
            AssetDatabase.CreateAsset(fa, dst);
            fa.atlasTextures[0].name = name + " Atlas"; AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa);
            string chars = Loc.AllText() + string.Concat(Enumerable.Range(0x20, 0x5f).Select(c => (char)c)) + "，。！？：；、“”‘’（）—…·×／";
            fa.TryAddCharacters(new string(chars.Distinct().ToArray()), out string missing);
            var so = new SerializedObject(fa); var pr = so.FindProperty("m_ClearDynamicDataOnBuild");
            if (pr != null) { pr.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
            EditorUtility.SetDirty(fa);
            if (!string.IsNullOrEmpty(missing)) missing = new string(missing.Where(c => c > 0x2000).ToArray());   // 英文字体缺汉字是正常的
            if (fa.atlasTextures.Length > 1) Debug.LogWarning($"[摘星] 字体 {name} 图集溢出成 {fa.atlasTextures.Length} 张，要加大图集");
            Debug.Log($"[摘星] 字体 {name}：{(string.IsNullOrEmpty(missing) ? "不缺字" : "缺 " + missing.Length + " 字")}");
            return fa;
        }
    }
}
