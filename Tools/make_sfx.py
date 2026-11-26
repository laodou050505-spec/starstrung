# 音效：程序合成（自有，无版权问题），统一“宏大的星空感”：水晶 / 钟琴 / 竖琴 / 合唱 / 铜锣 + 长混响。
# 都在 D 大调里，和背景音乐、消除钟琴同一套音。
# 用法：python3 Tools/make_sfx.py → Assets/_Starstrung/Audio/Sfx/*.wav（单声道，导入时设 ADPCM）
import numpy as np, wave, pathlib, sys
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from scipy import signal

SR = 44100
rng = np.random.default_rng(3)
OUT = pathlib.Path(__file__).resolve().parent.parent / 'Assets/_Starstrung/Audio/Sfx'
OUT.mkdir(parents=True, exist_ok=True)

def mtof(m): return 440.0 * 2 ** ((m - 69) / 12)
def T(d): return np.arange(int(d * SR)) / SR
def lp(x, fc, o=2): b, a = signal.butter(o, fc / (SR / 2)); return signal.lfilter(b, a, x)
def hp(x, fc, o=2): b, a = signal.butter(o, fc / (SR / 2), 'high'); return signal.lfilter(b, a, x)
def bp(x, fc, q): b, a = signal.iirpeak(fc / (SR / 2), q); return signal.lfilter(b, a, x)

def bell(m, d=2.5, amp=1.0, bright=1.0):
    t = T(d); f = mtof(m); s = np.zeros_like(t)
    for mul, g, dk in [(1, 1, 1), (2.01, 0.45, 0.6), (3.0, 0.2, 0.4), (4.17, 0.15 * bright, 0.3), (5.43, 0.08 * bright, 0.2), (6.8, 0.05 * bright, 0.12)]:
        s += g * np.sin(2 * np.pi * f * mul * t + rng.uniform(0, 6)) * np.exp(-t / (d * 0.32 * dk))
    return s * np.minimum(1, t / 0.002) * amp

def harp(m, d=2.5, amp=1.0):
    f = mtof(m); N = max(2, int(SR / f)); n = int(d * SR)
    x = np.zeros(n); x[:N] = lp(rng.uniform(-1, 1, N), min(9000, f * 14), 1)
    a = np.zeros(N + 2); a[0] = 1; a[N] = -0.499; a[N + 1] = -0.499
    y = signal.lfilter([1.0], a, x)
    return y / max(1e-6, np.abs(y).max()) * amp

def saw(f, n, vib=0.004):
    t = np.arange(n) / SR
    ph = np.cumsum(f * (1 + vib * np.sin(2 * np.pi * 5.2 * t + rng.uniform(0, 6)))) / SR
    return 2 * (ph % 1) - 1

def choir(notes, d, a=0.6, r=1.5, amp=1.0):
    n = int(d * SR); out = np.zeros(n)
    for m in notes:
        for det in (-8, 0, 9):
            s = saw(mtof(m) * 2 ** (det / 1200), n)
            out += bp(s, 760, 5) + 0.6 * bp(s, 1180, 6) + 0.25 * bp(s, 2850, 8)
    e = np.ones(n); na, nr = int(a * SR), int(r * SR)
    e[:na] = np.linspace(0, 1, na) ** 2; e[-nr:] *= np.linspace(1, 0, nr) ** 1.6
    out *= e
    return out / max(1e-6, np.abs(out).max()) * amp

def noise_sweep(d, f0, f1, amp=1.0, q=1.5):
    n = int(d * SR); x = rng.standard_normal(n); out = np.zeros(n)
    hop = 512
    for i in range(0, n, hop):
        k = i / n; fc = f0 * (f1 / f0) ** k
        b, a = signal.iirpeak(min(0.95, fc / (SR / 2)), q)
        seg = x[max(0, i - 2048):i + hop]
        out[i:i + hop] = signal.lfilter(b, a, seg)[-len(out[i:i + hop]):]
    return out / max(1e-6, np.abs(out).max()) * amp

def gong(f=70, d=4.5, amp=1.0):
    t = T(d); s = np.zeros_like(t)
    for mul, g, dk in [(1, 1, 1), (1.48, 0.6, 0.8), (2.2, 0.45, 0.6), (2.74, 0.35, 0.5), (3.9, 0.2, 0.35), (5.1, 0.12, 0.25)]:
        s += g * np.sin(2 * np.pi * f * mul * t * (1 + 0.004 * np.exp(-t * 3))) * np.exp(-t / (d * 0.3 * dk))
    s *= np.minimum(1, t / 0.006)
    return s / np.abs(s).max() * amp

def mix(d, *parts):
    n = int(d * SR); out = np.zeros(n)
    for t0, clip in parts:
        i = int(t0 * SR); e = min(n, i + len(clip)); out[i:e] += clip[:e - i]
    return out

def reverb(x, length=3.5, wet=0.6, dry=0.75):
    n = int(SR * length); t = np.arange(n) / SR
    ir = rng.standard_normal(n) * np.exp(-t / (length / 7)) * 0.5 + lp(rng.standard_normal(n), 3000) * np.exp(-t / (length / 4.5))
    ir = np.concatenate([np.zeros(int(0.025 * SR)), ir]); ir /= np.sqrt((ir ** 2).sum())
    y = signal.fftconvolve(np.concatenate([x, np.zeros(len(ir))]), ir)[:len(x) + len(ir)]
    xx = np.concatenate([x, np.zeros(len(ir))])
    out = xx * dry + y * wet
    # 去掉末尾几乎无声的部分
    thr = np.abs(out).max() * 0.002
    last = np.nonzero(np.abs(out) > thr)[0]
    out = out[:last[-1] + 1] if len(last) else out
    fade = min(len(out), int(0.05 * SR)); out[-fade:] *= np.linspace(1, 0, fade)
    return out

def save(name, x, peak=0.8):
    x = x / max(1e-6, np.abs(x).max()) * peak
    pcm = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(str(OUT / name), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
    print(name, f'{len(x) / SR:.2f}s')

# 界面：指着（很轻的玻璃声）
save('ui_hover.wav', reverb(bell(98, 0.5, 1, 0.6) * 0.5, 1.2, wet=0.5), peak=0.35)
# 界面：捏下（水晶双音）
save('ui_pick.wav', reverb(mix(1.0, (0, bell(93, 1.0)), (0.06, bell(100, 0.9, 0.7))), 2.2, wet=0.6), peak=0.6)
# 开始游戏：气流往上涌 + 合唱和弦慢慢升起 + 结尾一声水晶
save('begin.wav', reverb(mix(3.2, (0, noise_sweep(2.4, 300, 6000, 0.25)), (0.2, choir([62, 69, 74, 78], 2.8, a=1.8, r=0.9, amp=0.6)), (2.2, bell(86, 1.0, 0.8)), (2.26, bell(93, 1.0, 0.6))), 4.0, wet=0.75), peak=0.75)
# 进入关卡：竖琴往上刮 + 低音一声
seq = [62, 66, 69, 74, 78, 81, 86, 90]
save('level_start.wav', reverb(mix(2.6, *[(k * 0.06, harp(m, 2.0, 0.6)) for k, m in enumerate(seq)], (0, gong(73, 2.5, 0.35))), 3.5, wet=0.65), peak=0.75)
# 撞星核：深沉的铜锣 + 一道碎晶声
save('core_hit.wav', reverb(mix(4.5, (0, gong(62, 4.5, 1.0)), (0.02, hp(rng.standard_normal(int(0.4 * SR)), 3000) * np.exp(-T(0.4) * 12) * 0.25), (0.05, bell(81, 1.2, 0.25))), 4.5, wet=0.6, dry=0.8), peak=0.85)
# 过关：竖琴琶音 + 合唱大和弦 + 高处钟琴
save('win.wav', reverb(mix(5.0, *[(k * 0.09, harp(m, 2.5, 0.55)) for k, m in enumerate([62, 66, 69, 74, 78, 81, 86])], (0.35, choir([62, 66, 69, 74, 78], 4.2, a=0.9, r=2.2, amp=0.7)), (0.75, bell(90, 2.5, 0.5)), (0.9, bell(93, 2.5, 0.4))), 5.0, wet=0.75), peak=0.8)
# 失败：下行钢片琴 + 低沉合唱（小调色彩，不吓人）
save('lose.wav', reverb(mix(4.5, *[(k * 0.22, bell(m, 2.0, 0.6, 0.6)) for k, m in enumerate([81, 78, 74, 71, 69])], (0.3, choir([50, 57, 59], 3.8, a=1.2, r=2.0, amp=0.55))), 4.5, wet=0.75), peak=0.7)
# 时空裂缝打开：低频涌起 + 碎晶噼啪 + 空灵的高音
crack = mix(3.5, *[(rng.uniform(0.6, 2.6), hp(rng.standard_normal(int(0.05 * SR)), 2500) * np.exp(-T(0.05) * 60) * rng.uniform(0.15, 0.4)) for _ in range(28)])
save('rift_open.wav', reverb(mix(4.0, (0, noise_sweep(3.2, 80, 900, 0.5, q=0.8)), (0, gong(48, 3.8, 0.45)), (0, crack), (2.4, bell(95, 1.6, 0.35)), (2.55, bell(98, 1.6, 0.25))), 4.5, wet=0.8), peak=0.75)
# 合唱一声（连锁 ≥3 时叠在钟琴上）
save('choir_swell.wav', reverb(choir([69, 74, 78, 81], 2.2, a=0.35, r=1.4, amp=1.0), 3.5, wet=0.7), peak=0.55)
# 星星飞进星核（选关）
save('fly_in.wav', reverb(mix(1.4, (0, noise_sweep(1.1, 2500, 400, 0.4, q=2.5)), (1.0, bell(74, 1.5, 0.5))), 2.5, wet=0.6), peak=0.6)
