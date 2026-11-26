using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Starstrung.EditorTools
{
    /// <summary>
    /// 星核的美术资产（全部程序生成，存成资产文件）：
    /// 黄铜刻纹贴图（颜色 / 法线 / 金属光滑 / 自发光）、环箍网格、水晶网格、光带网格、材质、太空反射图；
    /// 以及在场景里搭出星核的层级（运行时由 CoreView 让它动起来）。
    /// </summary>
    public static class CoreAssets
    {
        const string Dir = "Assets/_Starstrung/Art/Core";

        // ───────── 黄铜 + 珐琅贴图 ─────────
        // u = 沿环一圈（0..1），v = 截面分区（和 Hoop 的截面表一致）：
        //   0.00–0.14 内表面（暗色拉丝黄铜） 0.14–0.34 / 0.66–0.86 斜角与金边（抛光亮金，外沿滚花）
        //   0.34–0.38 / 0.62–0.66 金边内侧斜面  0.38–0.62 珐琅带（深蓝珐琅 + 金色刻纹 / 星座 / 四芒星 / 嵌宝石）  0.86–1 内斜角
        static void BrassTextures(out Texture2D baseTex, out Texture2D normalTex, out Texture2D maskTex, out Texture2D emisTex)
        {
            const int W = 2048, H = 256;
            var height = new float[W * H];
            var baseC = new Color[W * H]; var mask = new Color[W * H]; var emis = new Color[W * H];
            var gold = new Color(0.86f, 0.66f, 0.33f); var goldDark = new Color(0.42f, 0.29f, 0.12f); var brushed = new Color(0.45f, 0.33f, 0.17f);
            var enamel = new Color(0.03f, 0.07f, 0.24f); var enamel2 = new Color(0.07f, 0.16f, 0.45f);
            Color[] jewel = { new(0.95f, 0.25f, 0.35f), new(1f, 0.8f, 0.25f), new(0.2f, 0.9f, 0.5f), new(0.25f, 0.5f, 1f), new(0.7f, 0.35f, 1f) };
            var rnd = new System.Random(11);
            // 珐琅带上的小星座：随机几组点，沿 u 方向排开，用细线连
            var cons = new System.Collections.Generic.List<(float u, float v)[]>();
            for (int k = 0; k < 10; k++)
            {
                float u0 = (k + 0.15f) / 10f; var pts = new (float, float)[4 + rnd.Next(2)];
                for (int j = 0; j < pts.Length; j++) pts[j] = (u0 + j * 0.012f + (float)rnd.NextDouble() * 0.006f, 0.44f + (float)rnd.NextDouble() * 0.12f);
                cons.Add(pts);
            }
            float SegD(float px, float py, float ax, float ay, float bx, float by)
            {
                float vx = bx - ax, vy = by - ay; float h = Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy + 1e-9f));
                float dx = px - ax - vx * h, dy = py - ay - vy * h; return Mathf.Sqrt(dx * dx + dy * dy);
            }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W, v = y / (float)(H - 1), deg = u * 360f;
                    Color c; float h = 1f, metal = 1f, smooth = 0.8f; Color e = Color.black;
                    float pat = Mathf.PerlinNoise(u * 80f, v * 6f) * 0.14f + Mathf.PerlinNoise(u * 400f, v * 30f) * 0.05f;
                    if (v < 0.14f || v > 0.86f)
                    {
                        // 内表面：深金底 + 沿环拉丝 + 中线一排刻点 + 两条细槽 + 每 10° 一道短刻
                        float brush = Mathf.PerlinNoise(u * 1800f, v * 2f) * 0.22f;
                        float vi = v < 0.14f ? v / 0.14f : (v - 0.86f) / 0.14f;               // 0..1 横跨内表面
                        c = Color.Lerp(brushed, gold, 0.35f) * (0.9f + brush + pat); smooth = 0.65f + brush * 0.3f; h = 1f;
                        float g2 = Mathf.Max(Mathf.Clamp01(1.4f - Mathf.Abs(vi - 0.18f) * H * 0.14f * 6f), Mathf.Clamp01(1.4f - Mathf.Abs(vi - 0.82f) * H * 0.14f * 6f));
                        float d10 = Mathf.Abs(Mathf.Repeat(deg + 5f, 10f) - 5f) / 360f * W;
                        if (vi > 0.18f && vi < 0.82f) g2 = Mathf.Max(g2, Mathf.Clamp01(1.5f - d10) * (Mathf.Abs(vi - 0.5f) > 0.12f ? 1f : 0f));
                        float d2 = Mathf.Abs(Mathf.Repeat(deg + 1.25f, 2.5f) - 1.25f) / 360f * W;
                        float dotI = Mathf.Clamp01(2.2f - Mathf.Sqrt(d2 * d2 + Mathf.Pow((vi - 0.5f) * H * 0.14f * 6f, 2)));
                        g2 = Mathf.Max(g2, dotI);
                        c = Color.Lerp(c, goldDark * 0.8f, g2); h -= g2 * 0.25f; smooth = Mathf.Lerp(smooth, 0.45f, g2);
                    }
                    else if (v < 0.34f || v > 0.66f)
                    {
                        // 金边：抛光亮金；最外沿一圈细滚花（每 0.6°）
                        c = gold * (1.0f + pat * 0.6f); smooth = 0.9f - pat;
                        bool outerRim = (v > 0.28f && v < 0.34f) || (v > 0.66f && v < 0.72f);
                        if (outerRim)
                        {
                            float k = Mathf.Abs(Mathf.Repeat(deg / 0.6f, 1f) - 0.5f) * 2f;   // 0 刻痕中心 ~ 1
                            float knurl = Mathf.SmoothStep(0f, 1f, k);
                            c = Color.Lerp(goldDark, gold * 1.05f, 0.35f + 0.65f * knurl); h = 0.92f + 0.08f * knurl; smooth = 0.7f + 0.2f * knurl;
                        }
                        bool bevel = (v > 0.14f && v < 0.18f) || (v > 0.24f && v < 0.28f) || (v > 0.72f && v < 0.76f) || (v > 0.82f && v < 0.86f);
                        if (bevel) { c = gold * 1.18f; smooth = 0.95f; }
                    }
                    else if (v < 0.38f || v > 0.62f)
                    {
                        c = Color.Lerp(goldDark, gold, 0.55f); smooth = 0.75f; h = 0.85f;            // 金边内侧斜面（略暗，像阴影）
                    }
                    else
                    {
                        // 珐琅带：深蓝珐琅（带细小金属闪片），上面是金色刻纹
                        float vv = (v - 0.38f) / 0.24f;                                                 // 0..1 横跨珐琅带
                        float flake = rnd.NextDouble() < 0.004 ? 1f : 0f;
                        c = Color.Lerp(enamel, enamel2, Mathf.PerlinNoise(u * 40f, vv * 3f) * 0.8f + vv * 0.2f);
                        c += new Color(0.5f, 0.6f, 1f) * flake * 0.6f;
                        metal = 0f; smooth = 0.93f; h = 0.8f;
                        float px = 1f / W;   // 一个像素的 u 宽
                        // 刻度：每 5° 短刻（从两边伸进 22%）、每 30° 贯穿刻
                        float d5 = Mathf.Abs(Mathf.Repeat(deg + 2.5f, 5f) - 2.5f) / 360f / px;
                        float d30 = Mathf.Abs(Mathf.Repeat(deg + 15f, 30f) - 15f) / 360f / px;
                        float line = 0f;
                        if (vv < 0.22f || vv > 0.78f) line = Mathf.Max(line, Mathf.Clamp01(1.6f - d5));
                        line = Mathf.Max(line, Mathf.Clamp01(2.2f - d30) * (Mathf.Abs(vv - 0.5f) > 0.18f ? 1f : 0f));
                        // 两条细金线贴着珐琅带边缘
                        line = Mathf.Max(line, Mathf.Clamp01(1.5f - Mathf.Abs(vv - 0.05f) * H * 0.24f));
                        line = Mathf.Max(line, Mathf.Clamp01(1.5f - Mathf.Abs(vv - 0.95f) * H * 0.24f));
                        // 星座细线 + 星点
                        foreach (var pts in cons)
                            for (int j = 0; j < pts.Length; j++)
                            {
                                float du = (u - pts[j].u) * W, dv = (v - pts[j].v) * (H - 1);
                                float dd = Mathf.Sqrt(du * du + dv * dv);
                                line = Mathf.Max(line, Mathf.Clamp01(2.4f - dd));
                                if (j + 1 < pts.Length)
                                    line = Mathf.Max(line, Mathf.Clamp01(1.1f - SegD(u * W, v * (H - 1), pts[j].u * W, pts[j].v * (H - 1), pts[j + 1].u * W, pts[j + 1].v * (H - 1))) * 0.8f);
                            }
                        // 每 15° 一颗四芒小星（金色嵌件，微微发光）
                        float d15u = (Mathf.Repeat(deg + 7.5f, 15f) - 7.5f) / 360f * W, d15v = (vv - 0.5f) * 0.24f * (H - 1);
                        float star4 = Mathf.Clamp01(1.4f - Mathf.Abs(d15u) * 0.5f - Mathf.Abs(d15v) * 3f) + Mathf.Clamp01(1.4f - Mathf.Abs(d15v) * 0.5f - Mathf.Abs(d15u) * 3f);
                        star4 *= Mathf.Repeat(deg + 22.5f, 45f) > 6f && Mathf.Repeat(deg + 22.5f, 45f) < 39f ? 1f : 0f;   // 宝石位置让开
                        line = Mathf.Max(line, Mathf.Clamp01(star4));
                        c = Color.Lerp(c, gold * 1.1f, line); metal = Mathf.Lerp(metal, 1f, line); smooth = Mathf.Lerp(smooth, 0.85f, line); h = Mathf.Lerp(h, 0.72f, line);
                        e += new Color(1f, 0.8f, 0.45f) * Mathf.Clamp01(star4) * 0.25f;
                        // 每 45° 一颗嵌宝石：金托 + 刻面宝石
                        float d45u = (Mathf.Repeat(deg + 22.5f, 45f) - 22.5f) / 360f * W, d45v = (vv - 0.5f) * 0.24f * (H - 1);
                        float gd = Mathf.Sqrt(d45u * d45u + d45v * d45v);
                        int gi = Mathf.FloorToInt(Mathf.Repeat(deg + 22.5f, 360f) / 45f) % 5;
                        float bezel = Mathf.Clamp01(1f - Mathf.Abs(gd - 12.5f) / 2.2f);
                        if (gd < 11f)
                        {
                            // 刻面：按角度分 8 个面，每面亮度不同
                            float fa = Mathf.Atan2(d45v, d45u); int face = Mathf.FloorToInt((fa + Mathf.PI) / (Mathf.PI / 4f));
                            float facet = 0.7f + 0.3f * Mathf.Sin(face * 2.3f) + (gd < 4f ? 0.35f : 0f);
                            c = jewel[gi] * 0.55f * facet; metal = 0f; smooth = 0.98f; h = 1.3f - gd / 22f;
                            e = jewel[gi] * (0.9f + 0.4f * facet) * Mathf.Clamp01((11f - gd) / 3f);
                        }
                        else if (bezel > 0) { c = Color.Lerp(c, gold * 1.2f, bezel); metal = 1f; smooth = 0.9f; h = 1.1f; }
                    }
                    // 刻槽里积一点暗（假的环境光遮蔽）
                    float ao = Mathf.Clamp01(0.75f + (h - 0.72f) * 0.8f);
                    int i2 = y * W + x;
                    height[i2] = h; baseC[i2] = c * ao; mask[i2] = new Color(metal, 1, 1, Mathf.Clamp01(smooth)); emis[i2] = e;
                }
            normalTex = new Texture2D(W, H, TextureFormat.RGBA32, true, true);
            var nrm = new Color[W * H];
            float H0(int x, int y) => height[Mathf.Clamp(y, 0, H - 1) * W + ((x % W) + W) % W];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dx = (H0(x + 1, y) - H0(x - 1, y)) * 3f, dy = (H0(x, y + 1) - H0(x, y - 1)) * 3f;
                    var nv = new Vector3(-dx, -dy, 1f).normalized;
                    nrm[y * W + x] = new Color(nv.x * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.z * 0.5f + 0.5f, 1);
                }
            baseTex = new Texture2D(W, H, TextureFormat.RGBA32, true); baseTex.SetPixels(baseC); baseTex.Apply();
            normalTex.SetPixels(nrm); normalTex.Apply();
            maskTex = new Texture2D(W, H, TextureFormat.RGBA32, true, true); maskTex.SetPixels(mask); maskTex.Apply();
            emisTex = new Texture2D(W, H, TextureFormat.RGBA32, true); emisTex.SetPixels(emis); emisTex.Apply();
        }

        static T SavePng<T>(Texture2D tex, string name, bool normal = false, bool linear = false) where T : Object
        {
            var path = $"{Dir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !(normal || linear);
            imp.wrapModeU = TextureWrapMode.Repeat; imp.wrapModeV = TextureWrapMode.Clamp;
            imp.anisoLevel = 8; imp.mipmapEnabled = true; imp.maxTextureSize = 2048;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        // ───────── 网格 ─────────
        /// <summary>
        /// 环箍：绕本地 Y 轴的一圈，半径 r（截面中心）、轴向半宽 hw、径向半厚 ht。
        /// 截面是分层的：内表面 → 下斜角 → 下金边 → 凹下的珐琅带 → 上金边 → 上斜角（闭合）。
        /// 每段截面单独成一条带（截面方向硬边、绕环方向平滑）；uv.x = 绕环 0..1，uv.y = 截面分区（见 BrassTextures 的分区表）。
        /// </summary>
        public static Mesh Hoop(float r, float hw, float ht, int seg = 256)
        {
            float rimW = hw * 0.22f, bev = ht * 0.45f, rec = ht * 0.42f;
            // 截面点：(径向偏移, 轴向)，外侧为 +；顺序沿截面走一圈
            var prof = new (float dr, float dy, float v)[]
            {
                (-ht, -hw + bev, 0.00f), (-ht, hw - bev, 0.14f),                 // 内表面        v 0.00–0.14
                (-ht + bev, hw, 0.18f),                                            // 上内斜角      v 0.14–0.18
                (ht - bev, hw, 0.24f),                                             // 上边平面      v 0.18–0.24
                (ht, hw - bev, 0.28f),                                             // 上外斜角      v 0.24–0.28
                (ht, hw - rimW, 0.34f),                                            // 上金边外面    v 0.28–0.34
                (ht - rec, hw - rimW - rec * 0.6f, 0.38f),                         // 金边内侧斜面  v 0.34–0.38
                (ht - rec, -hw + rimW + rec * 0.6f, 0.62f),                        // 珐琅带        v 0.38–0.62
                (ht, -hw + rimW, 0.66f),                                           // 金边内侧斜面  v 0.62–0.66
                (ht, -hw + bev, 0.72f),                                            // 下金边外面    v 0.66–0.72
                (ht - bev, -hw, 0.76f),                                            // 下外斜角      v 0.72–0.76
                (-ht + bev, -hw, 0.82f),                                           // 下边平面      v 0.76–0.82
                (-ht, -hw + bev, 0.86f),                                           // 下内斜角      v 0.82–0.86
            };
            var v = new System.Collections.Generic.List<Vector3>(); var n = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>(); var t = new System.Collections.Generic.List<int>();
            for (int k = 0; k < prof.Length; k++)
            {
                var p0 = prof[k]; var p1 = prof[(k + 1) % prof.Length];
                float v0 = p0.v, v1 = k + 1 < prof.Length ? p1.v : 1f;
                // 截面上这一段的外法线（径向, 轴向）
                var seg2 = new Vector2(p1.dr - p0.dr, p1.dy - p0.dy);
                var nn = new Vector2(seg2.y, -seg2.x).normalized;   // 顺时针走截面时外法线
                int b0 = v.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float ang = i / (float)seg * Mathf.PI * 2f; var D = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                    v.Add(D * (r + p0.dr) + Vector3.up * p0.dy); v.Add(D * (r + p1.dr) + Vector3.up * p1.dy);
                    var nw = (D * nn.x + Vector3.up * nn.y).normalized; n.Add(nw); n.Add(nw);
                    uv.Add(new Vector2(i / (float)seg, v0)); uv.Add(new Vector2(i / (float)seg, v1));
                    if (i < seg) { int q = b0 + i * 2; t.AddRange(new[] { q, q + 1, q + 2, q + 1, q + 3, q + 2 }); }
                }
            }
            var m = new Mesh { name = $"Hoop_{r:0.000}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            // 法线方向自检：截面走向反了就整体翻转三角形
            m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        /// <summary>细长六棱双锥水晶（高 1、半径 0.28），平直面（每个面独立顶点）</summary>
        public static Mesh Crystal()
        {
            var m = new Mesh { name = "Crystal" };
            var v = new System.Collections.Generic.List<Vector3>(); var t = new System.Collections.Generic.List<int>();
            int S = 6; float R = 0.28f;
            var top = new Vector3(0, 0.5f, 0); var bot = new Vector3(0, -0.5f, 0);
            for (int i = 0; i < S; i++)
            {
                float a0 = i / (float)S * Mathf.PI * 2, a1 = (i + 1) / (float)S * Mathf.PI * 2;
                var p0 = new Vector3(Mathf.Cos(a0) * R, 0.08f, Mathf.Sin(a0) * R); var p1 = new Vector3(Mathf.Cos(a1) * R, 0.08f, Mathf.Sin(a1) * R);
                var q0 = new Vector3(Mathf.Cos(a0) * R, -0.08f, Mathf.Sin(a0) * R); var q1 = new Vector3(Mathf.Cos(a1) * R, -0.08f, Mathf.Sin(a1) * R);
                foreach (var tri in new[] { (top, p1, p0), (p0, p1, q1), (p0, q1, q0), (bot, q0, q1) })
                { int b = v.Count; v.Add(tri.Item1); v.Add(tri.Item2); v.Add(tri.Item3); t.AddRange(new[] { b, b + 1, b + 2 }); }
            }
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>光带：平放在 XZ 面上的一圈带子（半径 1、半宽 hw），uv.x 沿环 0..1，uv.y 横向 0..1</summary>
        public static Mesh RibbonRing(float hw = 0.035f, int seg = 160)
        {
            var m = new Mesh { name = "RibbonRing" };
            var v = new Vector3[(seg + 1) * 2]; var uv = new Vector2[v.Length]; var n = new Vector3[v.Length]; var t = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v[i * 2] = d * (1 - hw); v[i * 2 + 1] = d * (1 + hw); uv[i * 2] = new Vector2(i / (float)seg, 0); uv[i * 2 + 1] = new Vector2(i / (float)seg, 1);
                n[i * 2] = n[i * 2 + 1] = Vector3.up;
                if (i < seg) { int k = i * 2; t[i * 6] = k; t[i * 6 + 1] = k + 2; t[i * 6 + 2] = k + 1; t[i * 6 + 3] = k + 1; t[i * 6 + 4] = k + 2; t[i * 6 + 5] = k + 3; }
            }
            m.vertices = v; m.uv = uv; m.normals = n; m.triangles = t; m.RecalculateBounds();
            return m;
        }

        /// <summary>开口圆柱面（半径 1、高 0..1），uv.x 绕一圈 0..1，uv.y 高度 0..1</summary>
        public static Mesh Cylinder(int seg = 96)
        {
            var m = new Mesh { name = "Cylinder" };
            var v = new Vector3[(seg + 1) * 2]; var uv = new Vector2[v.Length]; var n = new Vector3[v.Length]; var t = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v[i * 2] = d; v[i * 2 + 1] = d + Vector3.up; uv[i * 2] = new Vector2(i / (float)seg, 0); uv[i * 2 + 1] = new Vector2(i / (float)seg, 1);
                n[i * 2] = n[i * 2 + 1] = d;
                if (i < seg) { int k = i * 2; t[i * 6] = k; t[i * 6 + 1] = k + 1; t[i * 6 + 2] = k + 2; t[i * 6 + 3] = k + 1; t[i * 6 + 4] = k + 3; t[i * 6 + 5] = k + 2; }
            }
            m.vertices = v; m.uv = uv; m.normals = n; m.triangles = t; m.RecalculateBounds();
            return m;
        }

        /// <summary>柔光圆点贴图（粒子用）</summary>
        public static Texture2D DotTexture()
        {
            const int S = 64; var tex = new Texture2D(S, S, TextureFormat.RGBA32, true);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2 - 1, v = (y + 0.5f) / S * 2 - 1, r = Mathf.Sqrt(u * u + v * v);
                    float a = (Mathf.Exp(-r * 4.5f) * 1.2f + Mathf.Exp(-r * 12f)) * Mathf.Clamp01((1 - r) * 3f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(a)));
                }
            tex.Apply(); return tex;
        }

        /// <summary>多层魔法阵所需的共享资产（圆阵材质、光幕材质、光点材质、网格）</summary>
        public static (Material circle, Material mote, Mesh quad) StackAssets()
        {
            Directory.CreateDirectory(Dir);
            // 注意：星核光盘和入口都调这里，必须复用同一份材质（删了重建会让先建的那个引用丢失 → 洋红色）
            var circle = Keep("Starstrung/MagicCircle", "MagicLayer");
            var dot = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/dot.png") ?? SavePng<Texture2D>(DotTexture(), "dot");
            var mote = Keep("Starstrung/ParticleAdd", "MagicMote", m => { m.mainTexture = dot; m.SetFloat("_Intensity", 1.6f); });
            return (circle, mote, Resources.GetBuiltinResource<Mesh>("Quad.fbx"));
        }

        /// <summary>圆环管（半径 1，管粗 tube），躺在 XZ 平面</summary>
        public static Mesh Torus(float tube, int seg = 128, int side = 16)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f; var c = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                for (int j = 0; j <= side; j++)
                {
                    float b = j / (float)side * Mathf.PI * 2f; var nn = c * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    v.Add(c + nn * tube); n.Add(nn);
                    if (i < seg && j < side) { int q = i * (side + 1) + j, q2 = q + side + 1; t.AddRange(new[] { q, q + 1, q2, q + 1, q2 + 1, q2 }); }
                }
            }
            var m = new Mesh { name = "Torus" }; m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        public static MagicCircleStack MakeStack(Transform parent, string name, float radius, float spacing, int points, Color a, Color b, Color c, float intensity, bool tube = false)
        {
            var sa = StackAssets();
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var st = go.AddComponent<MagicCircleStack>();
            st.radius = radius; st.spacing = spacing; st.points = points; st.colA = a; st.colB = b; st.colC = c; st.intensity = intensity;
            st.circleMat = sa.circle; st.moteMat = sa.mote; st.quad = sa.quad;
            if (tube)
            {
                st.tube = true;
                st.torus = AssetDatabase.LoadAssetAtPath<Mesh>($"{Dir}/TorusMesh.asset") ?? Save(Torus(0.028f), "TorusMesh");
                st.tubeMat = Keep("Starstrung/FxGlow", "MagicTube", m => { m.SetFloat("_Mode", 2); m.SetColor("_Color", new Color(0.7f, 0.9f, 1f)); m.SetColor("_Color2", new Color(0.45f, 0.35f, 1f)); m.SetFloat("_Intensity", 1.1f); });
            }
            st.Build();
            return st;
        }

        static T Save<T>(T obj, string name) where T : Object
        {
            var path = $"{Dir}/{name}.asset";
            var old = AssetDatabase.LoadAssetAtPath<T>(path);
            if (old) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }
        /// <summary>材质已存在就原地更新参数（不删不重建，已有引用保持有效），不存在才新建。</summary>
        static Material Keep(string shader, string name, System.Action<Material> setup = null)
        {
            var path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) return Mat(shader, name, setup);
            m.shader = Shader.Find(shader); setup?.Invoke(m); EditorUtility.SetDirty(m);
            return m;
        }

        static Material Mat(string shader, string name, System.Action<Material> setup = null)
        {
            var m = new Material(Shader.Find(shader)) { name = name };
            setup?.Invoke(m);
            var path = $"{Dir}/{name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>太空反射图：深蓝紫渐变 + 稀疏星点 + 两片淡星云（黄铜、宝石反射用，透视画面里没有天空可反射）</summary>
        public static Cubemap SpaceCubemap()
        {
            const int S = 128;
            var cm = new Cubemap(S, TextureFormat.RGBA32, true);
            var rnd = new System.Random(7);
            for (int f = 0; f < 6; f++)
            {
                var px = new Color[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        var d = FaceDir((CubemapFace)f, (x + 0.5f) / S * 2 - 1, (y + 0.5f) / S * 2 - 1);
                        var c = Color.Lerp(new Color(0.02f, 0.02f, 0.06f), new Color(0.12f, 0.1f, 0.3f), Mathf.Clamp01(d.y * 0.5f + 0.5f));
                        c += new Color(0.35f, 0.15f, 0.5f) * Mathf.Pow(Mathf.Clamp01(Vector3.Dot(d, new Vector3(0.6f, 0.4f, 0.7f).normalized)), 6) * 0.8f;
                        c += new Color(0.1f, 0.3f, 0.6f) * Mathf.Pow(Mathf.Clamp01(Vector3.Dot(d, new Vector3(-0.7f, 0.2f, -0.3f).normalized)), 5) * 0.7f;
                        c += new Color(1f, 0.92f, 0.8f) * 0.5f;   // 整体提一点亮，让黄铜不至于死黑
                        if (rnd.NextDouble() < 0.012) c += Color.white * (float)(0.6 + rnd.NextDouble() * 1.5);
                        px[y * S + x] = c * 0.5f;
                    }
                cm.SetPixels(px, (CubemapFace)f);
            }
            cm.Apply(true);
            return cm;
        }
        static Vector3 FaceDir(CubemapFace f, float u, float v) => (f switch
        {
            CubemapFace.PositiveX => new Vector3(1, -v, -u),
            CubemapFace.NegativeX => new Vector3(-1, -v, u),
            CubemapFace.PositiveY => new Vector3(u, 1, v),
            CubemapFace.NegativeY => new Vector3(u, -1, -v),
            CubemapFace.PositiveZ => new Vector3(u, -v, 1),
            _ => new Vector3(-u, -v, -1),
        }).normalized;

        /// <summary>生成所有资产并在 parent 下搭出星核（位置 = 本地原点；由调用方摆到 LAYOUT.Core）</summary>
        public static CoreView Build(Transform parent, GameRunner runner)
        {
            Directory.CreateDirectory(Dir);
            BrassTextures(out var b, out var n, out var mk, out var e);
            var baseT = SavePng<Texture2D>(b, "brass_base");
            var normT = SavePng<Texture2D>(n, "brass_normal", normal: true);
            var maskT = SavePng<Texture2D>(mk, "brass_mask", linear: true);
            var emisT = SavePng<Texture2D>(e, "brass_emis");

            var brass = Mat("Universal Render Pipeline/Lit", "BrassRing", m =>
            {
                m.SetTexture("_BaseMap", baseT); m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_BumpMap", normT); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP");
                m.SetTexture("_MetallicGlossMap", maskT); m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 1f); m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetTexture("_EmissionMap", emisT); m.SetColor("_EmissionColor", Color.white * 1.3f); m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            });
            var nebula = Mat("Starstrung/CoreNebula", "CoreNebula");
            var disk = Mat("Starstrung/MagicCircle", "CoreDisk");
            var ribbonA = Mat("Starstrung/FxGlow", "CoreRibbonA", m => { m.SetFloat("_Mode", 1); m.SetColor("_Color", new Color(0.75f, 0.9f, 1f)); m.SetColor("_Color2", new Color(0.35f, 0.25f, 1f)); m.SetFloat("_Speed", 0.22f); m.SetFloat("_Intensity", 1.6f); });
            var ribbonB = Mat("Starstrung/FxGlow", "CoreRibbonB", m => { m.SetFloat("_Mode", 1); m.SetColor("_Color", new Color(1f, 0.7f, 0.95f)); m.SetColor("_Color2", new Color(0.6f, 0.2f, 0.9f)); m.SetFloat("_Speed", -0.16f); m.SetFloat("_Intensity", 1.3f); });
            var crystal = Mat("Starstrung/FxGlow", "CoreCrystal", m => { m.SetFloat("_Mode", 2); m.SetColor("_Color", new Color(0.7f, 0.85f, 1f)); m.SetColor("_Color2", new Color(0.5f, 0.3f, 1f)); m.SetFloat("_Intensity", 1.4f); });
            var flare = Mat("Starstrung/FxGlow", "CoreFlare", m => { m.SetFloat("_Mode", 0); m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always); m.SetColor("_Color", new Color(1f, 0.95f, 0.85f)); m.SetColor("_Color2", new Color(0.6f, 0.5f, 1f)); m.SetFloat("_Intensity", 1.2f); });
            var moonMat = Mat("Starstrung/FxGlow", "CoreMoon", m => { m.SetFloat("_Mode", 2); m.SetColor("_Color", new Color(1f, 0.9f, 0.7f)); m.SetColor("_Color2", new Color(1f, 0.75f, 0.4f)); m.SetFloat("_Intensity", 2.2f); });

            float R = (float)Logic.LAYOUT.CoreRadius;
            var hoops = new[] { Save(Hoop(R * 1.42f, 0.0095f, 0.0034f), "Hoop0"), Save(Hoop(R * 1.68f, 0.0110f, 0.0037f), "Hoop1"), Save(Hoop(R * 1.94f, 0.0125f, 0.0040f), "Hoop2") };
            var crystalMesh = Save(Crystal(), "CrystalMesh");
            var ribbonMesh = Save(RibbonRing(), "RibbonRingMesh");
            var sphere = Save(Object.Instantiate(MeshGen.UVSphere(64, 40, $"{Dir}/_tmp_sphere.asset")), "CoreSphere");
            AssetDatabase.DeleteAsset($"{Dir}/_tmp_sphere.asset");
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            GameObject Part(string name, Transform p, Mesh mesh, Material mat)
            {
                var go = new GameObject(name); go.transform.SetParent(p, false);
                if (mesh) { go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                return go;
            }
            var root = new GameObject("Core"); root.transform.SetParent(parent, false);
            var view = root.AddComponent<CoreView>(); view.runner = runner;
            view.nebula = Part("Nebula", root.transform, sphere, nebula).GetComponent<MeshRenderer>();
            view.nebula.transform.localScale = Vector3.one * R * 2f;
            view.flare = Part("Flare", root.transform, quad, flare).transform; view.flare.localScale = Vector3.one * R * 1.1f;
            view.rings = new Transform[3];
            for (int i = 0; i < 3; i++) view.rings[i] = Part($"Ring{i}", root.transform, hoops[i], brass).transform;
            view.moons = new Transform[2];
            var moonSphere = Save(Object.Instantiate(MeshGen.UVSphere(16, 10, $"{Dir}/_tmp_moon.asset")), "MoonSphere");   // 小卫星直径 2 cm，16×10 足够
            for (int i = 0; i < 2; i++) { var mo = Part($"Moon{i}", view.rings[1], moonSphere, moonMat).transform; mo.localScale = Vector3.one * 0.011f; view.moons[i] = mo; }
            // 多层魔法阵：最下层在星核下 22 cm，最上层在 16.2 cm（星环转动时最低到 14.95 cm，留 1.25 cm 间隙）
            var stack = MakeStack(root.transform, "Disk", 0.2f, 0.018f, 7, new Color(0.55f, 0.45f, 1f), new Color(0.75f, 0.9f, 1f), new Color(1f, 0.45f, 0.85f), 1.2f);
            stack.transform.localPosition = new Vector3(0, -0.22f, 0);
            view.disk = stack.transform; view.diskStack = stack;
            view.ribbons = new[] { Part("RibbonA", root.transform, ribbonMesh, ribbonA).transform, Part("RibbonB", root.transform, ribbonMesh, ribbonB).transform };
            view.ribbons[0].localScale = Vector3.one * R * 1.18f; view.ribbons[1].localScale = Vector3.one * R * 1.3f;   // 贴着星云球表面流，不碰路线
            view.shards = new Transform[7];
            for (int i = 0; i < 7; i++) { var s = Part($"Shard{i}", root.transform, crystalMesh, crystal).transform; s.localScale = new Vector3(0.012f, 0.03f + 0.012f * (i % 3), 0.012f); view.shards[i] = s; }
            return view;
        }
    }
}
