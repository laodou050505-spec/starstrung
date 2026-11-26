using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Starstrung.EditorTools
{
    /// <summary>视觉调试场景：五颗宝石星（近 / 中 / 远）、深色背景，用来逐个近看材质。</summary>
    public static class LookDev
    {
        public static readonly Color[] Jewel =
        {
            new(0.886f, 0.227f, 0.306f), // 红宝石 ☆
            new(0.953f, 0.757f, 0.169f), // 金黄 ☾
            new(0.133f, 0.761f, 0.435f), // 祖母绿 ◎
            new(0.184f, 0.435f, 0.910f), // 蓝宝石 ◇
            new(0.631f, 0.333f, 0.902f), // 紫水晶 △
        };

        [MenuItem("Starstrung/视觉调试场景")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Camera"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.02f, 0.025f, 0.05f);
            cam.nearClipPlane = 0.02f; cam.fieldOfView = 60;
            camGo.transform.position = new Vector3(0, 1.6f, 0);
            camGo.transform.rotation = Quaternion.Euler(18, 0, 0);
            camGo.tag = "MainCamera";
            var mat = new Material(Shader.Find("Starstrung/GemStar")) { enableInstancing = true };
            AssetDatabase.CreateAsset(mat, "Assets/_Starstrung/Materials/GemStar.mat");
            var mesh = MeshGen.UVSphere();
            // 近处一排（离眼 0.5 m，直径 6.5 cm），中间一排（1.5 m），远处一排（3.5 m）
            void Row(float dist, float y, float size, float spread)
            {
                for (int k = 0; k < 5; k++)
                {
                    var go = new GameObject($"Star_{dist}_{k}");
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
                    float x = (k - 2) * spread;
                    go.transform.position = new Vector3(x, y, dist);
                    go.transform.localScale = Vector3.one * size;
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetColor("_Color", Jewel[k]); mpb.SetFloat("_Symbol", k); mpb.SetFloat("_Seed", (k * 0.37f + dist * 0.11f) % 1f);
                    mr.SetPropertyBlock(mpb);
                }
            }
            Row(0.5f, 1.38f, 0.065f, 0.07f);
            Row(1.5f, 1.45f, 0.1f, 0.13f);
            Row(3.5f, 1.75f, 0.17f, 0.24f);
            EditorSceneManager.SaveScene(scene, "Assets/_Starstrung/Scenes/LookDev.unity");
        }
    }
}
