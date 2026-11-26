using System.IO;
using UnityEditor;
using UnityEngine;

namespace Starstrung.EditorTools
{
    /// <summary>编辑器截图：用指定相机渲染到 RenderTexture 再存 PNG（规范第 3 条：一次别连续渲染几百张）。</summary>
    public static class Shot
    {
        public static string Render(Camera cam, string path, int w = 1600, int h = 1000, int msaa = 4)
        {
            var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 24) { msaaSamples = msaa, sRGB = true });
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(rt);
            var full = Path.IsPathRooted(path) ? path : Path.Combine(Application.dataPath, "..", path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return Path.GetFullPath(full);
        }
    }
}
