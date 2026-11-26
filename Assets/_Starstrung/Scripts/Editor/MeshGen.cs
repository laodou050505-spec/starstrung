using UnityEditor;
using UnityEngine;

namespace Starstrung.EditorTools
{
    /// <summary>生成高密度球网格（经纬球，半径 0.5）：近看的星轮廓要圆，内置球只有 768 个三角面会看到折线。</summary>
    public static class MeshGen
    {
        public static Mesh UVSphere(int lon = 48, int lat = 32, string path = "Assets/_Starstrung/Meshes/Sphere48.asset")
        {
            var m = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            var v = new Vector3[(lon + 1) * (lat + 1)]; var n = new Vector3[v.Length]; var uv = new Vector2[v.Length];
            for (int y = 0; y <= lat; y++)
                for (int x = 0; x <= lon; x++)
                {
                    float a = x / (float)lon * Mathf.PI * 2f, b = y / (float)lat * Mathf.PI;
                    var d = new Vector3(Mathf.Sin(b) * Mathf.Cos(a), Mathf.Cos(b), Mathf.Sin(b) * Mathf.Sin(a));
                    int i = y * (lon + 1) + x; v[i] = d * 0.5f; n[i] = d; uv[i] = new Vector2(x / (float)lon, 1f - y / (float)lat);
                }
            var t = new int[lon * lat * 6]; int k = 0;
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * (lon + 1) + x, b = a + lon + 1;
                    t[k++] = a; t[k++] = a + 1; t[k++] = b; t[k++] = a + 1; t[k++] = b + 1; t[k++] = b;
                }
            m.vertices = v; m.normals = n; m.uv = uv; m.triangles = t; m.RecalculateBounds();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old) { EditorUtility.CopySerialized(m, old); AssetDatabase.SaveAssets(); return old; }
            AssetDatabase.CreateAsset(m, path); AssetDatabase.SaveAssets();
            return m;
        }
    }
}
