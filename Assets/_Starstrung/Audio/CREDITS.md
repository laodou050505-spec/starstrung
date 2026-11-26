# 音频来源

《摘星》里所有声音都是我们自己用程序合成的，没有使用任何第三方音频素材，不涉及版权和许可证问题。

| 文件 / 声音 | 来源 | 作者 | 许可 |
|---|---|---|---|
| `Music/title_theme.wav`（封面 / 选关，128 秒循环） | `Tools/make_music.py` 离线合成：合唱垫音（锯齿波 + 元音共振峰滤波）、弦乐、低音、钢片琴主旋律、竖琴刮奏（Karplus-Strong）、合成大教堂混响 | 本项目 | 自有，可商用 |
| `Music/play_theme.wav`（关卡，96 秒循环） | `Tools/make_music.py` 离线合成：竖琴 / 钢片琴分解和弦 + 薄垫音 | 本项目 | 自有，可商用 |
| `Sfx/*.wav`（界面、开始、进关、撞星核、过关、失败、裂缝、合唱、飞入） | `Tools/make_sfx.py` 离线合成：水晶钟琴、竖琴、合唱、铜锣、噪声扫频 + 合成混响 | 本项目 | 自有，可商用 |
| 钟琴（消除、摘 / 放、过关、撞星核、Boss） | `Scripts/Runtime/Audio/AudioKit.cs` 运行时合成（基频 + 2.76 / 5.4 倍泛音指数衰减） | 本项目 | 自有，可商用 |

字体来源和许可证见 `Assets/_Starstrung/Art/Fonts/*_OFL.txt`（均为 SIL Open Font License 1.1，可商用、可打包）。
