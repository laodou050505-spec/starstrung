# 背景音乐：程序合成（我们自己做的，没有版权问题）。11-24 用户要“宏大的星空感”。
# 两首，都在 D 大调（和消除时的 G 五声钟琴同一套音，玩家消出来的音能叠成旋律）：
#   title_theme.wav（封面 / 选关，128 秒循环）：慢起的人声合唱垫音 + 弦乐铺底 + 低音涌动 + 钢片琴主旋律 + 换和弦时竖琴刮奏 + 高处星光；
#   play_theme.wav（关卡里，96 秒循环）：更轻，竖琴 / 钢片琴的分解和弦律动 + 薄垫音，给玩家的钟琴留出空间。
# 做法：
#   - 合唱：锯齿波（三根略失谐 + 轻微颤音）经过“啊”元音的三个共振峰带通滤波；
#   - 弦乐：失谐锯齿波低通；低音：正弦 + 慢涌；
#   - 竖琴：Karplus-Strong 拨弦（梳状滤波器 + 平均，用 lfilter 一次算完）；钢片琴：近似谐波的泛音 + 指数衰减；
#   - 混响：合成的大教堂冲激响应（5.5 秒，高频衰减得更快），FFT 卷积，左右声道去相关；
#   - 无缝循环：渲染一圈 + 尾巴，把尾巴叠回开头。
# 用法：python3 Tools/make_music.py → Assets/_Starstrung/Audio/Music/*.wav
import numpy as np, wave, pathlib
from scipy import signal

SR = 44100
rng = np.random.default_rng(7)
OUT = pathlib.Path(__file__).resolve().parent.parent / 'Assets/_Starstrung/Audio/Music'
OUT.mkdir(parents=True, exist_ok=True)

def mtof(m): return 440.0 * 2 ** ((m - 69) / 12)

def env_adsr(n, a, r, sus=1.0):
    e = np.ones(n) * sus
    na, nr = int(a * SR), int(r * SR)
    e[:na] = np.linspace(0, sus, na) ** 2 if na > 0 else e[:na]
    if nr > 0: e[-nr:] *= np.linspace(1, 0, nr) ** 1.5
    return e

def saw(f, n, ph=0.0, vib=0.0, vibf=5.0):
    t = np.arange(n) / SR
    inst = f * (1 + vib * np.sin(2 * np.pi * vibf * t + ph))
    phase = np.cumsum(inst) / SR + ph
    return 2 * (phase % 1.0) - 1

def bp(x, fc, q):
    b, a = signal.iirpeak(fc / (SR / 2), q)
    return signal.lfilter(b, a, x)

def lp(x, fc, order=2):
    b, a = signal.butter(order, fc / (SR / 2))
    return signal.lfilter(b, a, x)

def choir(notes, dur, a=3.0, r=3.5, amp=0.05):
    n = int(dur * SR); out = np.zeros((n, 2))
    for k, m in enumerate(notes):
        f = mtof(m)
        for d, pan in [(-7, 0.25), (0, 0.5), (8, 0.75)]:
            s = saw(f * 2 ** (d / 1200), n, ph=rng.uniform(0, 1), vib=0.004, vibf=rng.uniform(4.5, 5.5))
            v = bp(s, 760, 5) * 1.0 + bp(s, 1180, 6) * 0.6 + bp(s, 2850, 8) * 0.25   # “啊”
            v = v * env_adsr(n, a, r) * amp / 3
            out[:, 0] += v * (1 - pan); out[:, 1] += v * pan
    return out

def strings(notes, dur, a=2.5, r=3.0, amp=0.035, cutoff=1800):
    n = int(dur * SR); out = np.zeros((n, 2))
    for m in notes:
        f = mtof(m)
        for d, pan in [(-5, 0.3), (6, 0.7)]:
            s = lp(saw(f * 2 ** (d / 1200), n, ph=rng.uniform(0, 1), vib=0.002, vibf=rng.uniform(5, 6)), cutoff)
            s *= env_adsr(n, a, r) * amp / 2
            out[:, 0] += s * (1 - pan); out[:, 1] += s * pan
    return out

def sub(m, dur, amp=0.09):
    n = int(dur * SR); t = np.arange(n) / SR
    s = np.sin(2 * np.pi * mtof(m) * t) + 0.25 * np.sin(2 * np.pi * mtof(m) * 2 * t)
    s *= env_adsr(n, dur * 0.4, dur * 0.4) * amp
    return np.stack([s, s], 1)

def celesta(m, amp=0.06, dur=3.5, pan=0.5):
    n = int(dur * SR); t = np.arange(n) / SR; f = mtof(m)
    s = np.zeros(n)
    for mul, g, dk in [(1, 1, 1.0), (2.0, 0.35, 0.55), (3.0, 0.12, 0.35), (4.07, 0.08, 0.25), (5.97, 0.03, 0.15)]:
        s += g * np.sin(2 * np.pi * f * mul * t) * np.exp(-t / (0.9 * dk))
    s *= np.minimum(1, t / 0.003) * amp
    return np.stack([s * (1 - pan), s * pan], 1)

def harp(m, amp=0.05, dur=3.0, pan=0.5):
    f = mtof(m); N = max(2, int(SR / f)); n = int(dur * SR)
    x = np.zeros(n); x[:N] = rng.uniform(-1, 1, N)
    x[:N] = lp(x[:N], min(8000, f * 12), 1)
    a = np.zeros(N + 2); a[0] = 1; a[N] = -0.4985; a[N + 1] = -0.4985
    y = signal.lfilter([1.0], a, x)
    y *= amp / max(1e-6, np.abs(y[:N * 4]).max())
    return np.stack([y * (1 - pan), y * pan], 1)

LOOP_N = [0]   # 当前这首一圈的长度（样本）：提前开始的声部（t0 < 0）绕到这一圈末尾，循环时正好接上
def place(buf, clip, t0):
    i = int(t0 * SR)
    if i < 0:
        j = LOOP_N[0] + i
        place(buf, clip[:-i], j / SR); clip = clip[-i:]; i = 0
    if i >= len(buf): return
    e = min(len(buf), i + len(clip)); buf[i:e] += clip[:e - i]

def reverb(x, length=5.5, predelay=0.04, wet=0.7, dry=0.6):
    n = int(SR * length); t = np.arange(n) / SR
    out = np.zeros_like(x)
    for c in range(2):
        noise = rng.standard_normal(n)
        bright = noise * np.exp(-t / (length / 9))
        dark = lp(noise, 2500, 2) * np.exp(-t / (length / 5))
        ir = bright * 0.5 + dark
        ir = np.concatenate([np.zeros(int(predelay * SR)), ir])
        ir[:int(SR * 0.01)] *= np.linspace(0, 1, int(SR * 0.01))
        ir /= np.sqrt((ir ** 2).sum())
        nfft = 1 << int(np.ceil(np.log2(len(x) + len(ir))))
        y = np.fft.irfft(np.fft.rfft(x[:, c], nfft) * np.fft.rfft(ir, nfft), nfft)[:len(x)]
        out[:, c] = y
    return x * dry + out * wet

def air(n, amp=0.01):
    noise = rng.standard_normal((n, 2))
    b, a = signal.butter(2, [5000 / (SR / 2), 11000 / (SR / 2)], 'band')
    y = signal.lfilter(b, a, noise, axis=0)
    y /= np.abs(y).max()
    t = np.arange(n) / SR
    return y * amp * (0.6 + 0.4 * np.sin(2 * np.pi * 0.04 * t))[:, None]

def finish(mix, L, name, peak=0.6):
    N = int(L * SR)
    loop = mix[:N].copy(); tail = mix[N:]
    loop[:len(tail)] += tail[:N]
    loop /= np.abs(loop).max() / peak
    pcm = (np.clip(loop, -1, 1) * 32767).astype(np.int16)
    with wave.open(str(OUT / name), 'wb') as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
    print(name, f'{L:.0f}s rms={np.sqrt((loop ** 2).mean()):.3f}')

# ───────── 封面 / 选关主题 ─────────
def title_theme():
    L = 128.0; seg = 16.0; LOOP_N[0] = int(L * SR)
    buf = np.zeros((int((L + 10) * SR), 2))
    # 和弦（MIDI）：D · Bm · G · A(sus) · D/F# · Em9 · Gmaj9 · Asus4
    prog = [(38, [62, 66, 69, 74]), (35, [62, 66, 71, 73]), (31, [62, 67, 71, 74]), (33, [64, 69, 71, 76]),
            (42, [62, 66, 69, 76]), (40, [64, 67, 71, 74]), (31, [62, 66, 71, 78]), (33, [62, 67, 69, 76])]
    motif = [(0, 81), (0.75, 78), (1.5, 76), (2.25, 74), (3.5, 76), (4.25, 69)]   # 钢片琴主旋律（D 五声）
    motif2 = [(0, 83), (0.6, 81), (1.2, 78), (2.4, 76), (3.0, 78), (4.2, 74)]
    for i, (root, notes) in enumerate(prog):
        t0 = i * seg
        place(buf, strings([n - 12 for n in notes], seg + 4), t0 - 2)
        place(buf, choir(notes[:3], seg + 5, a=4.5 if i % 2 == 0 else 3, amp=0.05 + 0.015 * (i % 4 == 3)), t0 - 1)
        place(buf, sub(root, seg + 4), t0 - 1)
        if i in (0, 4): # 竖琴刮奏：沿和弦往上
            pitches = sorted(set([n + o for n in notes for o in (0, 12)]))
            for k, m in enumerate(pitches): place(buf, harp(m, amp=0.035, pan=0.3 + 0.4 * k / len(pitches)), t0 + 0.4 + k * 0.07)
        if i % 2 == 1:
            mo = motif if i % 4 == 1 else motif2
            for dt, m in mo: place(buf, celesta(m, amp=0.05, pan=rng.uniform(0.3, 0.7)), t0 + 3.0 + dt * 1.25)
        # 星光：零星的高音钟琴
        for k in range(5):
            place(buf, celesta(rng.choice([86, 88, 90, 93, 95]), amp=rng.uniform(0.008, 0.018), pan=rng.uniform(0.1, 0.9)), t0 + rng.uniform(0, seg))
    buf += air(len(buf), 0.008)
    finish(reverb(buf, 5.5, wet=0.85, dry=0.5), L, 'title_theme.wav')

# ───────── 关卡主题 ─────────
def play_theme():
    L = 96.0; seg = 12.0; beat = 60 / 84 / 2; LOOP_N[0] = int(L * SR)   # 84 BPM 的八分音符
    buf = np.zeros((int((L + 10) * SR), 2))
    prog = [(38, [62, 66, 69, 74]), (31, [62, 67, 71, 74]), (35, [62, 66, 71, 73]), (33, [64, 69, 71, 76]),
            (38, [62, 66, 69, 76]), (40, [64, 67, 71, 74]), (31, [62, 66, 71, 78]), (33, [61, 64, 69, 76])]
    for i, (root, notes) in enumerate(prog):
        t0 = i * seg
        place(buf, strings([n - 12 for n in notes], seg + 3, amp=0.022, cutoff=1400), t0 - 1.5)
        place(buf, sub(root, seg + 3, amp=0.06), t0 - 1)
        if i >= 4: place(buf, choir(notes[:2], seg + 4, a=4, amp=0.026), t0 - 1)
        # 分解和弦：竖琴 / 钢片琴交替，每拍音高按和弦走一个上下的形状
        arp = [notes[0], notes[1], notes[2], notes[3], notes[2] + 12, notes[3], notes[2], notes[1]]
        nb = int(seg / beat)
        for k in range(nb):
            if rng.random() < 0.12: continue   # 偶尔空一拍，不机械
            m = arp[k % len(arp)] + (12 if (k // 8) % 2 == 1 and k % 8 in (3, 4) else 0)
            clip = harp(m, amp=0.024, dur=2.2, pan=0.35 + 0.3 * ((k % 8) / 7)) if k % 2 == 0 else celesta(m + 12, amp=0.014, dur=2.0, pan=0.65 - 0.3 * ((k % 8) / 7))
            place(buf, clip, t0 + k * beat + rng.uniform(-0.008, 0.008))
    buf += air(len(buf), 0.006)
    finish(reverb(buf, 4.5, wet=0.7, dry=0.6), L, 'play_theme.wav', peak=0.5)

# ───────── 星域变奏（11-25：玩完 20 关约 40 分钟，一首循环太重复）─────────
# 和 play_theme 同一套和弦进行、同一主题，只换配器和律动：
#   霜川 z2：只有钢片琴，高八度、音更稀、余音长，像冰晶；垫音换成“呜”元音的薄合唱
#   焰云 z3：弦乐加厚、竖琴降八度、低音按拍脉动，92 BPM，更热
#   深空 z4：合唱 + 低音长音为主，主题零星出现，混响最长，空旷神秘（也是吞星兽 / 无尽关的底）
PROG = [(38, [62, 66, 69, 74]), (31, [62, 67, 71, 74]), (35, [62, 66, 71, 73]), (33, [64, 69, 71, 76]),
        (38, [62, 66, 69, 76]), (40, [64, 67, 71, 74]), (31, [62, 66, 71, 78]), (33, [61, 64, 69, 76])]

def choir_oo(notes, dur, a=3.0, r=3.5, amp=0.04):
    n = int(dur * SR); out = np.zeros((n, 2))
    for m in notes:
        f = mtof(m)
        for d, pan in [(-6, 0.3), (7, 0.7)]:
            s = saw(f * 2 ** (d / 1200), n, ph=rng.uniform(0, 1), vib=0.004, vibf=rng.uniform(4.5, 5.5))
            v = bp(s, 320, 5) + bp(s, 800, 6) * 0.45 + bp(s, 2400, 8) * 0.1   # “呜”
            v = v * env_adsr(n, a, r) * amp / 2
            out[:, 0] += v * (1 - pan); out[:, 1] += v * pan
    return out

def zone2():
    L = 96.0; seg = 12.0; beat = 60 / 84 / 2; LOOP_N[0] = int(L * SR)
    buf = np.zeros((int((L + 10) * SR), 2))
    for i, (root, notes) in enumerate(PROG):
        t0 = i * seg
        place(buf, choir_oo([notes[0], notes[2]], seg + 4, a=4, amp=0.03), t0 - 1)
        place(buf, sub(root, seg + 3, amp=0.04), t0 - 1)
        arp = [notes[0] + 12, notes[2] + 12, notes[3] + 12, notes[1] + 24, notes[3] + 12, notes[2] + 12]
        for k in range(int(seg / beat)):
            if k % 2 == 1 or rng.random() < 0.25: continue
            m = arp[(k // 2) % len(arp)]
            place(buf, celesta(m, amp=0.016, dur=3.2, pan=rng.uniform(0.2, 0.8)), t0 + k * beat + rng.uniform(-0.01, 0.01))
    buf += air(len(buf), 0.009)
    finish(reverb(buf, 6.0, wet=0.85, dry=0.5), L, 'play_theme_z2.wav', peak=0.5)

def zone3():
    beat = 60 / 92 / 2; seg = beat * 32; L = seg * 8; LOOP_N[0] = int(L * SR)   # 每个和弦 32 个八分音符，一圈约 83 秒
    buf = np.zeros((int((L + 10) * SR), 2))
    for i, (root, notes) in enumerate(PROG):
        t0 = i * seg
        place(buf, strings([n - 12 for n in notes], seg + 3, amp=0.034, cutoff=2200), t0 - 1.5)
        place(buf, strings([notes[3]], seg + 3, amp=0.012, cutoff=3000), t0 - 1)
        for k in range(32):   # 低音按拍脉动（每拍一下，强拍重）
            if k % 2: continue
            place(buf, sub(root, beat * 1.6, amp=0.05 if k % 8 == 0 else 0.032), t0 + k * beat)
        arp = [notes[0] - 12, notes[1] - 12, notes[2] - 12, notes[3] - 12, notes[2], notes[3] - 12, notes[2] - 12, notes[1] - 12]
        for k in range(32):
            if rng.random() < 0.08: continue
            place(buf, harp(arp[k % 8] + 12, amp=0.026, dur=2.0, pan=0.35 + 0.3 * ((k % 8) / 7)), t0 + k * beat + rng.uniform(-0.006, 0.006))
        if i % 2 == 1:
            for dt, m in [(0, 81), (0.75, 78), (1.5, 76), (2.25, 74), (3.5, 76), (4.25, 69)]: place(buf, celesta(m, amp=0.02, pan=0.6), t0 + 2.0 + dt * beat * 2)
    buf += air(len(buf), 0.005)
    finish(reverb(buf, 4.0, wet=0.6, dry=0.65), L, 'play_theme_z3.wav', peak=0.5)

def zone4():
    L = 96.0; seg = 12.0; LOOP_N[0] = int(L * SR)
    buf = np.zeros((int((L + 10) * SR), 2))
    for i, (root, notes) in enumerate(PROG):
        t0 = i * seg
        place(buf, choir(notes[:3], seg + 5, a=5, r=5, amp=0.035), t0 - 2)
        place(buf, sub(root - 12, seg + 4, amp=0.07), t0 - 1.5)
        place(buf, strings([root + 12], seg + 4, amp=0.018, cutoff=900), t0 - 1)
        if i % 2 == 0:
            for j, (dt, m) in enumerate([(0, 81), (1.2, 78), (2.4, 76), (4.0, 74)]):
                place(buf, celesta(m, amp=0.022, dur=4.5, pan=0.3 + 0.13 * j), t0 + 3.5 + dt)
        for k in range(3):
            place(buf, celesta(rng.choice([86, 90, 93, 98]), amp=rng.uniform(0.006, 0.012), dur=5, pan=rng.uniform(0.1, 0.9)), t0 + rng.uniform(0, seg))
    buf += air(len(buf), 0.01)
    finish(reverb(buf, 7.5, wet=0.95, dry=0.45), L, 'play_theme_z4.wav', peak=0.5)

# ───────── 危险层（11-25 紧张感：串头逼近星核时由游戏按危险度推大音量）─────────
# 低音 D + A 五度、按 1.4 Hz 不安地脉动；上面一层很淡的小二度（D–Eb）弦乐摩擦；底下低沉的隆隆声。16 秒无缝循环。
def danger():
    L = 16.0; LOOP_N[0] = int(L * SR); n = int((L + 6) * SR); t = np.arange(n) / SR
    buf = np.zeros((n, 2))
    pulse = 0.55 + 0.45 * np.sin(2 * np.pi * 1.4 * t) ** 2
    for m, g in [(38, 1.0), (45, 0.5), (50, 0.25)]:
        f = mtof(m); v = (np.sin(2 * np.pi * f * t) + 0.3 * np.sin(2 * np.pi * f * 2 * t)) * g * 0.08 * pulse
        buf[:, 0] += v; buf[:, 1] += v
    place(buf, strings([62, 63], L + 6, a=3, r=3, amp=0.02, cutoff=1600), 0)
    rum = lp(rng.standard_normal(n), 140, 2); rum /= np.abs(rum).max()
    buf += np.stack([rum, np.roll(rum, 1500)], 1) * 0.05 * (0.6 + 0.4 * np.sin(2 * np.pi * 0.11 * t))[:, None]
    finish(reverb(buf, 3.5, wet=0.5, dry=0.8), L, 'danger_layer.wav', peak=0.5)

# ───────── 结局（11-25）：星从玩家手里炸开那一刻起，约 75 秒，不循环 ─────────
# 竖琴往上刮奏 → 合唱 + 弦乐整片铺开 → 钢片琴把封面主旋律放慢一倍完整奏一遍 → 中段转关系小调 → 回 D 大调长音、十秒淡出
def ending():
    L = 76.0; LOOP_N[0] = int(L * SR) * 4   # 不循环：提前开始的声部不绕回
    buf = np.zeros((int((L + 10) * SR), 2))
    prog = [(38, [62, 66, 69, 74], 10), (31, [62, 67, 71, 74], 8), (35, [62, 66, 71, 74], 8), (33, [64, 69, 73, 76], 8),
            (35, [59, 62, 66, 71], 8), (31, [59, 62, 67, 71], 8), (33, [61, 64, 69, 76], 8), (38, [62, 66, 69, 74, 78], 18)]
    t0 = 0.0
    for i, (root, notes, seg) in enumerate(prog):
        last = i == len(prog) - 1
        place(buf, choir(notes[:4], seg + 5, a=2.0 if i == 0 else 3.5, r=8 if last else 3.5, amp=0.055 if last else 0.045), t0 - (0 if i == 0 else 1.5))
        place(buf, strings([n - 12 for n in notes[:4]], seg + 4, a=1.5 if i == 0 else 3, r=8 if last else 3, amp=0.034, cutoff=2400), t0 - (0 if i == 0 else 1))
        place(buf, sub(root, seg + 4, amp=0.08), t0)
        if i in (0, 4, 7):   # 竖琴刮奏
            pitches = sorted(set([n + o for n in notes for o in (0, 12, 24)]))
            for k, m in enumerate(pitches): place(buf, harp(m, amp=0.032, pan=0.2 + 0.6 * k / len(pitches)), t0 + 0.1 + k * 0.06)
        for k in range(int(seg * 0.8)):
            place(buf, celesta(rng.choice([86, 88, 90, 93, 95, 98]), amp=rng.uniform(0.006, 0.014), dur=4, pan=rng.uniform(0.1, 0.9)), t0 + rng.uniform(0, seg))
        t0 += seg
    # 主旋律放慢一倍（从第 10 秒起两遍）
    motif = [(0, 81), (0.75, 78), (1.5, 76), (2.25, 74), (3.5, 76), (4.25, 69), (6.0, 83), (6.6, 81), (7.2, 78), (8.4, 76), (9.0, 78), (10.2, 74)]
    for rep, start in enumerate((10.0, 36.0)):
        for dt, m in motif: place(buf, celesta(m, amp=0.055, dur=4.5, pan=0.5), start + dt * 2.0)
    fade = np.ones(len(buf)); n0 = int((L - 12) * SR); n1 = int(L * SR)
    fade[n0:n1] = np.linspace(1, 0, n1 - n0) ** 1.6; fade[n1:] = 0
    buf += air(len(buf), 0.008)
    out = reverb(buf, 6.5, wet=0.9, dry=0.5) * fade[:, None]
    N = int(L * SR); out = out[:N]; out /= np.abs(out).max() / 0.6
    pcm = (np.clip(out, -1, 1) * 32767).astype(np.int16)
    with wave.open(str(OUT / 'ending_theme.wav'), 'wb') as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
    print('ending_theme.wav', f'{L:.0f}s')

import sys
if 'ending' in sys.argv:
    ending()
elif 'variants' in sys.argv:
    zone2(); zone3(); zone4(); danger()
else:
    title_theme()
    play_theme()
    old = OUT / 'starfield_loop.wav'
    if old.exists(): old.unlink(); (OUT / 'starfield_loop.wav.meta').unlink(missing_ok=True)
