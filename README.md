# 摘星 STARSTRUNG

手可摘星辰——PICO 手眼 MR 游戏。一串宝石星沿着身边的银河流向你胸前的星核，伸手“指着捏”把星摘下来、放进同色之间，三颗同色相连就消散；别让星串碰到星核。序章 + 4 个星域 × 5 关 + 无尽模式（打完吞星兽解锁）。每过一关，真实房间就裂开一道不规则的时空裂缝，裂缝逐关长大、彼此合并，通关结局时整个房间变成裂缝后面的星空。

- 中文名《摘星》，英文名 **STARSTRUNG**；包名 `com.toybox.starstrung`
- Unity **6000.5.5f1**（URP 17.5），目标平台 Android（PICO），IL2CPP + ARM64
- 输入：只用手势 + 眼动（眼动只做辅助，不用眼动也能玩完）

## 1. 放 PICO SDK（不入库）

PICO SDK 的许可证不允许分发，仓库里没有。把 **PICO Unity Integration SDK 6.0.0** 解压后的 `XR` 文件夹放进**仓库根目录**下的 `PICO-SDK/`（这个文件夹已在 .gitignore 里，不会被提交）：

```
starstrung/            ← 本仓库根目录
├── Assets/
├── Packages/
├── PICO-SDK/
│   └── XR/            ← SDK 6.0.0 里的 XR 文件夹（含 package.json）
└── ...
```

`Packages/manifest.json` 里写的是 `"com.bytedance.pico.xr": "file:../PICO-SDK/XR"`——这个相对路径从 `Packages/` 文件夹算起，所以指向的是仓库根目录下的 `PICO-SDK/XR`。

## 2. 打开与打包

1. Unity Hub 用 6000.5.5f1 打开 `Starstrung/`，第一次导入较久。
2. 打开场景 `Assets/_Starstrung/Scenes/Main.unity`（菜单 **Starstrung/搭主场景** 可以一键重建整个场景）。
3. 签名：`Keystore/starstrung.keystore` + `Keystore/keystore_pass.txt` 不入库，找我要；或者自己用 keytool 生成同名文件（别名 `starstrung`）。
4. 菜单 **Starstrung/切到 Android**（只需一次，要重新压缩贴图）→ **Starstrung/打包 APK**。结果写在 `Logs/android_build.txt`，安装包在 `Builds/Starstrung_MMdd_HHmm.apk`。
   - 打包会自动：设置 PICO 手势 / 眼动 / 透视、启动画面（MADE WITH TRIPO，普通标志和头显专用图两处）、版本号（按时间递增）、清理旧的 Gradle 资源分片。
5. 命令行：`Unity -batchmode -quit -projectPath Starstrung -buildTarget Android -executeMethod Starstrung.EditorTools.StarstrungBuild.BuildBatch`

## 3. 操作

| 想做什么 | 怎么做 |
|---|---|
| 摘星 | 手指着一颗星（近处的星直接伸手过去），**拇指食指捏一下**。被指着的星会先亮起来 |
| 放星 | 捏着星指向串上发亮的缝，**松手**；随手在空中松开，星会飞回串尾（星不会凭空消失） |
| 星盘 | 胸前两个小星盘，可以先把星存进去 |
| 岔路关 | 指着岔口上方的小魔法阵捏一下，换下一条路 |
| 吞星兽 | 它张嘴时，把它嘴里亮着的那种颜色的星扔进嘴里 |
| 暂停 | 星核右边的暂停按钮（圆圈 + 两道竖线），指着捏一下；摘下头显 / 切后台也会自动暂停（编辑器里不自动暂停） |
| 重新对准 | 封面“设置”里的准星图标：星界平滑转到你现在面朝的方向；进关时偏开太多会自动对准一次 |
| 界面 | 所有按钮都是星核旁的宝石星，同样“指着捏”；返回 / 退出要**捏住不放**一会儿 |

编辑器里没有头显时用模拟手：鼠标 = 手（射线擦过附近的星会自动伸过去），左键 = 捏，Tab 换手，H 模拟跟丢，右键拖动 / Q E 转头，F5 录制手部动作、F6 回放，Esc 回选关。

## 4. 可调参数

- `Assets/_Starstrung/Settings/InputTuning.asset`：捏合阈值（on 4.0 cm / off 5.5 cm）、近摘半径、远摘锥角、防跳角度、捏合回看时间、眼动吸附角度、跟丢 / 冻结判定。真机装机后直接改资产即可生效，不用改代码。
- 关卡数据：`Assets/_Starstrung/Scripts/Logic/Levels.cs`（与 Three.js 原型逐行对齐，`Logic/SelfTest.cs` 自检）。
- 界面文字：`Scripts/Runtime/UI/Loc.cs`（中文原文为键）。改了文字后跑 `python3 Tools/subset_fonts.py`，再点菜单 **Starstrung/生成字体资源**（字体是只含游戏用字的子集）。
- 声音：`python3 Tools/make_music.py`（封面 / 关卡曲）、`make_music.py variants`（三个星域变奏 + 危险层）、`make_music.py ending`（结局曲）、`python3 Tools/make_sfx.py`（全部程序合成，来源见 `Assets/_Starstrung/Audio/CREDITS.md`）。
- 进度与设计记录：`Docs/进度.md`；概念图 `Docs/概念图/`（风格基准、吞星兽、封面、应用图标定稿）。

## 5. 真机检查清单（装机后 5～10 分钟逐项打勾）

**启动与透视**
- [ ] 启动画面是 MADE WITH + TRIPO 横排（没有 Unity 标志），之后进入封面
- [ ] 透视画面正常显示，房间被罩上一层淡淡的夜蓝；空中有错峰闪烁的星
- [ ] 封面在正前方约 1.3 m：“摘星”字标、玻璃碎片、星芒；星核在胸前约 0.35 m，高度合适（不顶下巴、不用低头太多）

**手势**
- [ ] 指着“开始”宝石星，周围出现星盘准星；捏一下能进入。不用刻意用力就能识别捏合，松开后不会误判成还在捏
- [ ] 握拳不会被当成捏；手在视野边缘跟丢再回来，不会误触发
- [ ] 序章：近处的星伸手直接捏得到；远处的星指着能亮、捏得到；放进发亮的缝能成功
- [ ] 如果捏合太灵 / 太钝：改 `InputTuning.asset` 的 pinchOn / pinchOff；远处的星指不准：改 coneDeg

**眼动**
- [ ] 第一次启动会弹眼动授权；授权后看向一颗星再捏更准（不授权也能正常玩）

**安全与舒适**
- [ ] 所有路线在身边 3～4.2 m、高度 1.2～2.4 m，站着原地转身即可，不需要走动；在系统安全边界之内
- [ ] 星域四的路线会绕到身后，转身时没有眩晕感
- [ ] 时空裂缝在四周和头顶，不在地板上；边缘缓慢起伏，不闪
- [ ] 结局：星从手里散开时不刺眼；房间变成星空的那段没有不适
- [ ] 暂停按钮捏得到、不会在捏星时误触；摘下头显再戴回是暂停状态

**性能**
- [ ] 帧率稳定（目标 72 fps）：封面、选关银河、关卡中连锁消除、吞星兽关都不明显掉帧
- [ ] 连续玩 10 分钟不发烫降频

**双语**
- [ ] 封面“设置”里切换语言后，所有字变英文且不溢出；重启后保持上次的语言
- [ ] 系统语言为英文时首次启动就是英文

**流程**
- [ ] 过关后结算（星级金宝石、分数），“下一关”能进下一关；输了能“重来”
- [ ] 打完 4-5 进结局：亲手捏住最后一颗星、松开放飞；之后无尽模式在选关里吞星兽后面亮起
- [ ] 每过一关，房间里多一道时空裂缝（重启后仍在）
- [ ] 音乐、音效音量四档可调；退出要捏住不放 1.5 秒
