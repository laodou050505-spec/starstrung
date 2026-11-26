using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;

namespace Starstrung.EditorTools
{
    /// <summary>一键搭主场景 Assets/_Starstrung/Scenes/Main.unity（可重复执行，会覆盖）。</summary>
    public static class SceneBuilder
    {
        [MenuItem("Starstrung/搭主场景")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // XR 原点 + 相机（头显里位姿由 TrackedPoseDriver 驱动；编辑器里由 DeskSim 摆）
            var origin = new GameObject("XR Origin");
            var camGo = new GameObject("Main Camera"); camGo.transform.SetParent(origin.transform, false);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 60f; cam.fieldOfView = 70f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.02f, 0.025f, 0.05f, 1f);
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<TrackedPoseDriver>();
            var desk = camGo.AddComponent<DeskSim>(); desk.cam = cam;

            // 逻辑原点
            var rig = new GameObject("Rig");
            var game = new GameObject("Game"); game.transform.SetParent(rig.transform, false);
            var tuning = AssetDatabase.LoadAssetAtPath<InputTuning>("Assets/_Starstrung/Settings/InputTuning.asset");
            var pico = game.AddComponent<PicoHandInput>(); pico.cam = cam; pico.origin = origin.transform; pico.tuning = tuning;
            var mr = game.AddComponent<MRRig>(); mr.cam = cam;
            var runner = game.AddComponent<GameRunner>();
            runner.cam = cam; runner.rig = rig.transform; runner.desk = desk; runner.pico = pico; runner.tuning = tuning;

            var gems = new GameObject("Gems"); gems.transform.SetParent(rig.transform, false);
            var gf = gems.AddComponent<GemField>(); gf.runner = runner;
            // 特殊星标识（双子双环 + 连线、星爆尖刺、停摆刻度环）：一次实例化画完
            var badges = gems.AddComponent<StarBadges>(); badges.gems = gf;
            badges.badgeMat = MatAt("Assets/_Starstrung/Resources/UI/StarBadge.mat", "Starstrung/StarBadge", m => m.SetFloat("_Intensity", 1.5f));
            gf.mesh = MeshGen.UVSphere(32, 20, "Assets/_Starstrung/Meshes/Sphere32.asset");   // 宝石星最大约 7 cm，32×20 看不出棱角；同屏上百颗，面数省一半
            gf.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starstrung/Materials/GemStar.mat");

            var ribbonMat = new Material(Shader.Find("Starstrung/StarChartRibbon"));
            AssetDatabase.CreateAsset(ribbonMat, "Assets/_Starstrung/Materials/StarChartRibbon.mat");
            var sparkMat = new Material(Shader.Find("Starstrung/StarSpark"));
            AssetDatabase.CreateAsset(sparkMat, "Assets/_Starstrung/Materials/StarSpark.mat");
            ribbonMat.SetFloat("_Intensity", 0.55f); ribbonMat.SetFloat("_Glow", 0.1f);   // 金色刻度降成淡淡的底纹，主角是银河星流
            var galaxyMat = new Material(Shader.Find("Starstrung/GalaxyStream"));
            AssetDatabase.CreateAsset(galaxyMat, "Assets/_Starstrung/Materials/GalaxyStream.mat");
            var nebulaMat = new Material(Shader.Find("Starstrung/NebulaRibbon"));
            AssetDatabase.CreateAsset(nebulaMat, "Assets/_Starstrung/Materials/NebulaRibbon.mat");
            var tracks = new GameObject("StarChart"); tracks.transform.SetParent(rig.transform, false);
            var sc = tracks.AddComponent<StarChartTrack>(); sc.runner = runner; sc.ribbonMat = ribbonMat; sc.sparkMat = sparkMat; sc.galaxyMat = galaxyMat; sc.nebulaMat = nebulaMat;
            // 入口魔法阵模板：多层（半径 1，按路线带宽缩放），场景里隐藏，开局按路线复制
            var portalTpl = CoreAssets.MakeStack(tracks.transform, "PortalTemplate", 1f, 0.09f, 8, new Color(0.35f, 0.45f, 1f), new Color(0.7f, 0.95f, 1f), new Color(0.85f, 0.4f, 1f), 1.1f, true);
            portalTpl.gameObject.SetActive(false);
            sc.portalStack = portalTpl;

            // 特效（消除、摘放、撞星核、过关）
            var pMat = new Material(Shader.Find("Starstrung/ParticleAdd")); AssetDatabase.CreateAsset(pMat, "Assets/_Starstrung/Materials/ParticleAdd.mat");
            var rMat = new Material(Shader.Find("Starstrung/FxRing")) { enableInstancing = true }; AssetDatabase.CreateAsset(rMat, "Assets/_Starstrung/Materials/FxRing.mat");
            var fMat = new Material(Shader.Find("Starstrung/FxGlow")); fMat.SetFloat("_Mode", 0); fMat.SetColor("_Color", new Color(1f, 0.95f, 0.88f)); fMat.SetColor("_Color2", new Color(0.6f, 0.55f, 1f));
            AssetDatabase.CreateAsset(fMat, "Assets/_Starstrung/Materials/FxFlash.mat");
            var fxGo = new GameObject("Fx"); fxGo.transform.SetParent(rig.transform, false);
            var fx = fxGo.AddComponent<FxSystem>(); fx.runner = runner; fx.gems = gf; fx.particleMat = pMat; fx.ringMat = rMat; fx.flareMat = fMat;

            // 时空裂缝（每通一关裂开一块）
            var riftMat = new Material(Shader.Find("Starstrung/RiftDome")); AssetDatabase.CreateAsset(riftMat, "Assets/_Starstrung/Materials/RiftDome.mat");
            var riftGo = new GameObject("Rifts"); riftGo.transform.SetParent(rig.transform, false);
            var rv = riftGo.AddComponent<RiftView>(); rv.runner = runner; rv.riftMat = riftMat;

            // 柔和辉光（参考图里亮的东西都有一圈光晕）
            var camData = cam.GetUniversalAdditionalCameraData(); camData.renderPostProcessing = true;
            var volGo = new GameObject("Glow Volume");
            var vol = volGo.AddComponent<UnityEngine.Rendering.Volume>(); vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
            bloom.threshold.Override(0.9f); bloom.intensity.Override(0.95f); bloom.scatter.Override(0.68f);   // 护眼 + 让宝石星不被背景辉光淹没
            bloom.highQualityFiltering.Override(false);
            AssetDatabase.CreateAsset(profile, "Assets/_Starstrung/Settings/GlowProfile.asset");
            vol.sharedProfile = profile;

            // 星核（精致版：星云球 + 三道黄铜星环 + 魔法阵光盘 + 光带 + 水晶碎片）
            var coreView = CoreAssets.Build(rig.transform, runner);
            coreView.transform.localPosition = GameRunner.ToLocal(Starstrung.Logic.LAYOUT.Core);

            // 灯光：一盏暖色主光（照黄铜）+ 星核里一盏紫色点光；反射用程序生成的太空反射图（透视画面里没有天空可反射）
            var sun = new GameObject("Key Light").AddComponent<Light>(); sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.92f, 0.82f); sun.intensity = 1.1f; sun.shadows = LightShadows.None;
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            var coreLight = new GameObject("Core Light").AddComponent<Light>(); coreLight.type = LightType.Point;
            coreLight.color = new Color(1f, 0.9f, 0.78f); coreLight.intensity = 0.45f; coreLight.range = 0.45f; coreLight.shadows = LightShadows.None;
            coreLight.transform.SetParent(coreView.transform, false);
            var cube = CoreAssets.SpaceCubemap();
            if (AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/_Starstrung/Art/Core/SpaceReflection.cubemap")) AssetDatabase.DeleteAsset("Assets/_Starstrung/Art/Core/SpaceReflection.cubemap");
            AssetDatabase.CreateAsset(cube, "Assets/_Starstrung/Art/Core/SpaceReflection.cubemap");
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cube;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.12f, 0.12f, 0.22f);

            // 界面 + 流程（标题 → 选关 → 关卡 → 结算）、声音
            runner.autoStart = false;
            BuildUI(rig.transform, game, runner, gf, coreView.transform, fx);

            EditorSceneManager.SaveScene(scene, "Assets/_Starstrung/Scenes/Main.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/_Starstrung/Scenes/Main.unity", true) };
        }

        static Material MatAt(string path, string shader, System.Action<Material> setup = null)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find(shader); setup?.Invoke(m); EditorUtility.SetDirty(m);
            return m;
        }

        static void BuildUI(Transform rig, GameObject game, GameRunner runner, GemField gems, Transform core, FxSystem fx)
        {
            // 资源：手绘细线、四芒星光点（Resources/UI，运行时 UIKit 读）；标题字标材质；Tripo 标志
            MatAt("Assets/_Starstrung/Resources/UI/HandLine.mat", "Starstrung/HandLine", m => { m.SetFloat("_Intensity", 1.25f); m.SetFloat("_Taper", 0.12f); });
            MatAt("Assets/_Starstrung/Resources/UI/StarGlyph.mat", "Starstrung/FxGlow", m => { m.SetFloat("_Mode", 0); m.SetFloat("_Billboard", 1); m.SetColor("_Color", new Color(1f, 0.95f, 0.85f)); m.SetColor("_Color2", new Color(0.6f, 0.55f, 1f)); m.enableInstancing = false; });
            var starText = MatAt("Assets/_Starstrung/Art/UI/StarText.mat", "Starstrung/StarText");
            var logoPath = "Assets/_Starstrung/Art/Brand/tripo_logo_h_800_white.png";
            var imp = (TextureImporter)AssetImporter.GetAtPath(logoPath);
            if (imp) { imp.textureType = TextureImporterType.Default; imp.alphaIsTransparency = true; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Clamp; imp.npotScale = TextureImporterNPOTScale.None; imp.SaveAndReimport(); }
            var logoTex = AssetDatabase.LoadAssetAtPath<Texture2D>(logoPath);
            var logoMat = MatAt("Assets/_Starstrung/Art/UI/TripoLogo.mat", "Starstrung/LogoAdd", m => { m.mainTexture = logoTex; m.SetColor("_Color", new Color(0.82f, 0.84f, 0.92f)); });

            // 房间里的星空氛围（夜蓝罩 + 错峰闪烁的星）
            var skyGo = new GameObject("Sky"); skyGo.transform.SetParent(rig, false);
            var sky = skyGo.AddComponent<SkyView>();
            sky.starMat = MatAt("Assets/_Starstrung/Art/UI/SkyStars.mat", "Starstrung/SkyStars");
            sky.veilMat = MatAt("Assets/_Starstrung/Art/UI/NightVeil.mat", "Starstrung/NightVeil");

            var ui = new GameObject("UI"); ui.transform.SetParent(rig, false);
            var flow = ui.AddComponent<AppFlow>(); flow.runner = runner; flow.gems = gems; flow.sky = sky;
            var picker = game.AddComponent<MenuPicker>(); picker.runner = runner; flow.picker = picker;
            T View<T>(string name) where T : FlowView
            {
                var go = new GameObject(name); go.transform.SetParent(ui.transform, false);
                var v = go.AddComponent<T>(); v.flow = flow; return v;
            }
            var title = View<TitleView>("Title"); title.gems = gems; title.starTextMat = starText; title.logoMat = logoMat; flow.title = title;
            title.shardMat = MatAt("Assets/_Starstrung/Art/UI/ShardSpace.mat", "Starstrung/ShardSpace");
            title.circleMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starstrung/Art/Core/MagicLayer.mat");
            title.moteMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starstrung/Art/Core/MagicMote.mat");
            title.core = core;
            var select = View<SelectView>("Select"); select.gems = gems; flow.select = select; select.core = core;
            select.galaxyMat = MatAt("Assets/_Starstrung/Art/UI/GalaxyDisk.mat", "Starstrung/GalaxyDisk");
            var result = View<ResultView>("Result"); result.gems = gems; result.anchor = core; flow.result = result;
            var hud = View<PlayHud>("PlayHud"); hud.gems = gems; hud.runner = runner; hud.core = core; flow.hud = hud;
            hud.circleMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starstrung/Art/Core/MagicLayer.mat");
            var settings = View<SettingsView>("Settings"); flow.settings = settings;
            var pause = View<PauseView>("Pause"); pause.runner = runner; pause.gems = gems; pause.core = core; pause.sky = sky; flow.pause = pause;
            var ending = View<EndingView>("Ending"); ending.gems = gems; ending.core = core.GetComponent<CoreView>(); flow.ending = ending; ending.sky = sky;
            ending.starMat = MatAt("Assets/_Starstrung/Art/UI/EndingStars.mat", "Starstrung/EndingStars");
            ending.starTextMat = starText; ending.logoMat = logoMat;
            ending.shardMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starstrung/Art/Core/CoreCrystal.mat");
            ending.shardMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_Starstrung/Art/Core/CrystalMesh.asset");
            ending.rifts = Object.FindAnyObjectByType<RiftView>();

            // 岔路信标 + Boss 吞星兽（和星核同一套视觉语言）
            var fbGo = new GameObject("ForkBoss"); fbGo.transform.SetParent(rig, false);
            var fb = fbGo.AddComponent<ForkBossView>(); fb.runner = runner; fb.gems = gems;
            fb.crystalMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_Starstrung/Art/Core/CrystalMesh.asset");
            fb.hoopMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_Starstrung/Art/Core/Hoop0.asset");
            fb.crystalMat = MatAt("Assets/_Starstrung/Art/Boss/BossHorn.mat", "Starstrung/FxGlow", m => { m.SetFloat("_Mode", 2); m.SetColor("_Color", new Color(0.95f, 0.6f, 1f)); m.SetColor("_Color2", new Color(0.45f, 0.2f, 0.9f)); m.SetFloat("_Intensity", 1.3f); });
            fb.brassMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starstrung/Art/Core/BrassRing.mat");
            fb.shellMat = MatAt("Assets/_Starstrung/Art/Boss/BossShell.mat", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", new Color(0.3f, 0.19f, 0.46f)); m.SetFloat("_Metallic", 0.7f); m.SetFloat("_Smoothness", 0.8f);
                m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(0.13f, 0.05f, 0.22f)); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.SetFloat("_Cull", 0f);   // 双面：张嘴时看得到壳的内面，外表面也一定画出来
            });
            fb.mawMat = MatAt("Assets/_Starstrung/Art/Boss/BossMaw.mat", "Starstrung/CoreNebula", m =>
            {
                m.SetColor("_ColDeep", new Color(0.06f, 0.0f, 0.06f)); m.SetColor("_ColMid", new Color(0.55f, 0.08f, 0.45f)); m.SetColor("_ColHot", new Color(1f, 0.6f, 0.9f));
                m.SetColor("_ColAccent", new Color(0.4f, 0.2f, 1f)); m.SetColor("_ColHeart", new Color(1f, 0.75f, 0.85f)); m.SetColor("_ColRim", new Color(0.8f, 0.3f, 0.9f));
            });
            fb.circleMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starstrung/Art/Core/MagicLayer.mat");
            fb.moteMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starstrung/Art/Core/MagicMote.mat");
            fb.fx = fx;
            // 吞星兽头：Tripo P2 模型 + 4K 贴图（不读写、压缩；离玩家 3～4 m，4K 足够近看）
            const string SE = "Assets/_Starstrung/Art/Boss/StarEater/";
            var seTi = (TextureImporter)AssetImporter.GetAtPath(SE + "StarEater_albedo.png");
            if (seTi && (seTi.isReadable || seTi.maxTextureSize != 4096)) { seTi.isReadable = false; seTi.maxTextureSize = 4096; seTi.mipmapEnabled = true; seTi.anisoLevel = 4; seTi.SaveAndReimport(); }
            var seMi = (ModelImporter)AssetImporter.GetAtPath(SE + "StarEater.fbx");
            if (seMi && (seMi.materialImportMode != ModelImporterMaterialImportMode.None || seMi.animationType != ModelImporterAnimationType.None)) { seMi.materialImportMode = ModelImporterMaterialImportMode.None; seMi.animationType = ModelImporterAnimationType.None; seMi.importAnimation = false; seMi.SaveAndReimport(); }
            fb.headPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SE + "StarEater.fbx");
            fb.headMat = MatAt("Assets/_Starstrung/Art/Boss/StarEaterHead.mat", "Starstrung/StarEater", m => m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(SE + "StarEater_albedo.png"));

            // 声音：音乐流式 + Vorbis；音效 ADPCM 单声道（规范第 9 条）
            AudioClip Clip(string path, bool music)
            {
                var ai = (AudioImporter)AssetImporter.GetAtPath(path);
                if (ai)
                {
                    var ss = ai.defaultSampleSettings;
                    ss.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                    ss.compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
                    ss.quality = 0.6f; ai.forceToMono = !music; ai.defaultSampleSettings = ss; ai.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            const string M = "Assets/_Starstrung/Audio/Music/", S = "Assets/_Starstrung/Audio/Sfx/";
            var audio = game.AddComponent<AudioKit>(); audio.runner = runner; audio.flow = flow;
            audio.titleMusic = Clip(M + "title_theme.wav", true); audio.playMusic = Clip(M + "play_theme.wav", true);
            audio.zoneMusic = new[] { Clip(M + "play_theme_z2.wav", true), Clip(M + "play_theme_z3.wav", true), Clip(M + "play_theme_z4.wav", true) };
            audio.dangerLayer = Clip(M + "danger_layer.wav", true);
            audio.endingMusic = Clip(M + "ending_theme.wav", true);
            audio.hover = Clip(S + "ui_hover.wav", false); audio.pick = Clip(S + "ui_pick.wav", false); audio.begin = Clip(S + "begin.wav", false);
            audio.levelStart = Clip(S + "level_start.wav", false); audio.coreHit = Clip(S + "core_hit.wav", false); audio.win = Clip(S + "win.wav", false);
            audio.lose = Clip(S + "lose.wav", false); audio.riftOpen = Clip(S + "rift_open.wav", false); audio.choirSwell = Clip(S + "choir_swell.wav", false);
            audio.flyIn = Clip(S + "fly_in.wav", false);
        }
    }
}