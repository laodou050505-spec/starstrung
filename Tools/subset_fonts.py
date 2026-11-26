# 字体子集：只保留游戏里会出现的字（双语表 Loc.cs + 关卡名 Levels.cs 里的字符串 + 数字字母标点），
# 打包体积从几十 MB 降到几百 KB。可变字体先定成一个静态字重。字体都是 SIL OFL，许可证一起拷进工程。
# 用法：python3 Tools/subset_fonts.py（改了 Loc.cs 里的文字后重跑，再在 Unity 菜单 Starstrung/生成字体资源）
import re, shutil, pathlib
from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

root = pathlib.Path(__file__).resolve().parent.parent
src = root / 'ArtSource/Fonts'
dst = root / 'Assets/_Starstrung/Art/Fonts'
dst.mkdir(parents=True, exist_ok=True)

text = ''
for f in ['Assets/_Starstrung/Scripts/Runtime/UI/Loc.cs', 'Assets/_Starstrung/Scripts/Logic/Levels.cs']:
    for s in re.findall(r'"((?:[^"\\]|\\.)*)"', (root / f).read_text(encoding='utf-8')):
        text += s
text += ''.join(chr(c) for c in range(0x20, 0x7f))
text += '，。！？：；、“”‘’（）—…·×／ '
chars = ''.join(sorted(set(text)))

# (源文件, 输出名, 可变字体的字重, 额外字符)
fonts = [
    ('LXGWWenKai-Regular.ttf', 'UIZh.ttf', None, ''),
    ('Cormorant.ttf', 'UIEn.ttf', {'wght': 500}, ''),
    ('Cormorant-Italic.ttf', 'UIEnItalic.ttf', {'wght': 500}, ''),
    ('NotoSerifSC-VF.ttf', 'TitleZh.ttf', {'wght': 300}, ''),   # 封面字标：细宋体（11-24 用户选 B 稿）
    ('NotoSerifSC-VF.ttf', 'SerifZh.ttf', {'wght': 400}, ''),   # 竖排副标题等
    ('PinyonScript-Regular.ttf', 'Script.ttf', None, ''),      # 背后淡淡的英文花体
    ('Cinzel.ttf', 'TitleEn.ttf', {'wght': 400}, ''),
    ('Outfit[wght].ttf', 'Brand.ttf', {'wght': 400}, ''),
    ('NotoSansSC-Medium.otf', 'BrandZh.otf', None, ''),
]
for name, out, wght, extra in fonts:
    f = TTFont(src / name)
    if wght and 'fvar' in f:
        f = instancer.instantiateVariableFont(f, wght)
    opts = subset.Options(); opts.layout_features = ['*']; opts.name_IDs = ['*']; opts.notdef_outline = True
    sub = subset.Subsetter(opts); sub.populate(text=chars + extra); sub.subset(f)
    f.save(dst / out)
    print(f'{out}: {(dst / out).stat().st_size // 1024} KB')
for lic in src.glob('*_OFL.txt'):
    shutil.copy(lic, dst / lic.name)
print('字符数', len(chars))
