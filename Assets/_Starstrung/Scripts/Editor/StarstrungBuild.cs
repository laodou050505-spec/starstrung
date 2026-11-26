// 打包：菜单 Starstrung/切到 Android（要重新压缩贴图，只切一次）、Starstrung/打包 APK。
// 结果写到 Logs/android_build.txt；打包在 delayCall 里跑，命令调用不会被卡住。做法照《天底》DLDBuild（已在头显上验证过）。
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Starstrung.EditorTools
{
    public static class StarstrungBuild
    {
        public const string PackageId = "com.toybox.starstrung", ProductName = "摘星";   // 头显桌面上显示的名字（11-26 从内部代号 Starstrung 改过来；包名不变）
        const string Report = "Logs/android_build.txt", Brand = "Assets/_Starstrung/Art/Brand/";

        [MenuItem("Starstrung/切到 Android")]
        public static string SwitchAndroid()
        {
            bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            return "切换 " + ok + " 现在 " + EditorUserBuildSettings.activeBuildTarget;
        }

        static void Pico()
        {
            var c = AssetDatabase.LoadAssetAtPath<ByteDance.PICO.XR.PXR_ProjectSetting>("Assets/Resources/PXR_ProjectSetting.asset");
            if (c) { c.handTracking = true; c.eyeTracking = true; c.videoSeeThrough = true; EditorUtility.SetDirty(c); }
            AssetDatabase.SaveAssets();
        }

        /// <summary>启动画面：去掉 Made with Unity，换成排版一样的 MADE WITH TRIPO（左边两行小号大写字 MADE / WITH，右边官方 TRIPO 标志）。
        /// 普通“标志列表”和头显专用的 Virtual Reality Splash Image 两处都要填（头显里只显示后者）。</summary>
        [MenuItem("Starstrung/设置启动画面")]
        public static void ApplySplash()
        {
            string path = Brand + "made_with_tripo.png", vrPath = Brand + "made_with_tripo_vr.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("[摘星] 没找到启动画面图 " + path); return; }
            if (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed)
            { ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.alphaIsTransparency = true; ti.SaveAndReimport(); }
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.backgroundColor = new Color(35 / 255f, 31 / 255f, 32 / 255f, 1);
            PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2f, sp) };
            var vi = AssetImporter.GetAtPath(vrPath) as TextureImporter;
            if (vi == null) { Debug.LogWarning("[摘星] 没找到头显启动图 " + vrPath); return; }
            if (vi.textureType != TextureImporterType.Default || vi.mipmapEnabled || vi.textureCompression != TextureImporterCompression.Uncompressed || vi.npotScale != TextureImporterNPOTScale.None)
            { vi.textureType = TextureImporterType.Default; vi.mipmapEnabled = false; vi.textureCompression = TextureImporterCompression.Uncompressed; vi.npotScale = TextureImporterNPOTScale.None; vi.wrapMode = TextureWrapMode.Clamp; vi.sRGBTexture = true; vi.SaveAndReimport(); }
            var vt = AssetDatabase.LoadAssetAtPath<Texture2D>(vrPath);
            var ps = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            var so = new SerializedObject(ps); var prop = so.FindProperty("m_VirtualRealitySplashScreen");
            if (prop != null && prop.objectReferenceValue != vt) { prop.objectReferenceValue = vt; so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets(); }
        }

        static void Apply()
        {
            var t = NamedBuildTarget.Android;
            Pico();
            ApplySplash();
            PlayerSettings.SetApplicationIdentifier(t, PackageId);
            PlayerSettings.productName = ProductName; PlayerSettings.companyName = "toybox";
            // 版本号按打包时间生成（2026-01-01 起的分钟数）：只增不减，头显不会拒装成“降级”
            int vc = Math.Max(PlayerSettings.Android.bundleVersionCode + 1, (int)((DateTime.Now - new DateTime(2026, 1, 1)).TotalMinutes));
            PlayerSettings.Android.bundleVersionCode = vc; PlayerSettings.bundleVersion = "0.1." + vc;
            PlayerSettings.SetScriptingBackend(t, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)29;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;      // PICO SDK 校验：不能用 GameActivity
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/_Starstrung/Scenes/Main.unity", true) };
            string ks = Path.GetFullPath("Keystore/starstrung.keystore"), pw = "Keystore/keystore_pass.txt";
            if (!File.Exists(ks) || !File.Exists(pw)) throw new Exception("找不到 Keystore/starstrung.keystore（不入库，见 README）");
            string pass = File.ReadAllText(pw).Trim();
            PlayerSettings.Android.useCustomKeystore = true; PlayerSettings.Android.keystoreName = ks; PlayerSettings.Android.keystorePass = pass;
            PlayerSettings.Android.keyaliasName = "starstrung"; PlayerSettings.Android.keyaliasPass = pass;
            // Gradle 导出目录里的旧资源分片会混进新包，以前在别的项目里造成过启动卡死
            foreach (var d in new[] { "Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary/src/main/assets", "Library/Bee/Android/Prj/Mono2x/Gradle/unityLibrary/src/main/assets" })
                if (Directory.Exists(d)) Directory.Delete(d, true);
        }

        [MenuItem("Starstrung/打包 APK")]
        public static string BuildApk()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) return "先执行 Starstrung/切到 Android，等导入和编译完成";
            EditorApplication.delayCall += Run;
            return "已开始";
        }

        /// <summary>命令行打包：Unity -batchmode -quit -buildTarget Android -executeMethod Starstrung.EditorTools.StarstrungBuild.BuildBatch</summary>
        public static void BuildBatch() { Run(); }

        static void Run()
        {
            string msg;
            try
            {
                Apply();
                Directory.CreateDirectory("Builds");
                var path = Path.GetFullPath($"Builds/Starstrung_{DateTime.Now:MMdd_HHmm}.apk");
                var rep = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/_Starstrung/Scenes/Main.unity" }, locationPathName = path, target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android, options = BuildOptions.None });
                var s = rep.summary;
                long bytes = File.Exists(path) ? new FileInfo(path).Length : 0;
                msg = $"{s.result} 用时 {s.totalTime.TotalMinutes:0.0} 分钟 安装包 {bytes / 1048576f:0.0} MB 错误 {s.totalErrors} → {path}";
            }
            catch (Exception e) { msg = "失败 " + e.Message; }
            Directory.CreateDirectory("Logs");
            File.AppendAllText(Report, DateTime.Now.ToString("HH:mm ") + msg + "\n");
            Debug.Log("[摘星] 打包：" + msg);
        }
    }
}
