#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
My4KGame 素材生成器
===================

一次性生成（全部原创、不涉及任何版权素材）：

  1. Assets/Resources/Audio/NeonPulse.wav   —— 120 BPM、72 秒的程序合成练习曲
  2. Assets/Resources/Audio/Click.wav       —— UI 点击音
  3. Assets/Resources/Charts/*.json         —— 三档主谱面 + 手册示例谱面 + 校准谱面

为什么用脚本生成而不是下载素材：
  - 没有版权问题，可以直接交作业、直接打包发布
  - 节拍是数学上精确的（每个鼓点都落在 0.5 秒的整数倍上），
    谱面时间和音频自然严丝合缝，不会出现"音乐和谱面对不上"的经典 bug

用法：
  python tools/make_assets.py            # 生成全部（已存在的音频跳过）
  python tools/make_assets.py --force    # 覆盖已有文件
  python tools/make_assets.py --skip-audio
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
AUDIO_DIR = os.path.join(ROOT, "Assets", "Resources", "Audio")
CHART_DIR = os.path.join(ROOT, "Assets", "Resources", "Charts")

SR = 44_100          # 采样率
BPM = 120.0
BEAT = 60.0 / BPM    # 0.5 秒
BARS = 36            # 36 小节 = 144 拍 = 72 秒
DURATION = BARS * 4 * BEAT

LANES = 4


# ---------------------------------------------------------------- 合成器
def _t(dur: float) -> np.ndarray:
    return np.arange(int(round(dur * SR)), dtype=np.float64) / SR


def kick() -> np.ndarray:
    t = _t(0.35)
    f = 120.0 * np.exp(-t * 22.0) + 46.0          # 频率下滑，做出"咚"的下坠感
    phase = 2 * np.pi * np.cumsum(f) / SR
    body = np.sin(phase) * np.exp(-t * 9.0)
    click = np.exp(-t * 320.0) * 0.5              # 起音的一点"脆"
    return np.tanh((body + click) * 1.6) * 0.95


def snare() -> np.ndarray:
    rng = np.random.default_rng(7)
    t = _t(0.22)
    noise = rng.standard_normal(len(t))
    # 简易高通：两次一阶差分，听起来更"沙"
    noise = np.diff(noise, prepend=0.0)
    noise = np.diff(noise, prepend=0.0)
    noise /= (np.max(np.abs(noise)) + 1e-9)
    body = np.sin(2 * np.pi * 185.0 * t) * np.exp(-t * 26.0) * 0.5
    return (noise * np.exp(-t * 20.0) * 0.55 + body) * 0.75


def hat(open_hat: bool = False) -> np.ndarray:
    rng = np.random.default_rng(11)
    dur = 0.14 if open_hat else 0.06
    t = _t(dur)
    n = rng.standard_normal(len(t))
    n = np.diff(np.diff(n, prepend=0.0), prepend=0.0)
    n /= (np.max(np.abs(n)) + 1e-9)
    env = np.exp(-t * (14.0 if open_hat else 55.0))
    return n * env * (0.16 if open_hat else 0.11)


def bass(freq: float, dur: float) -> np.ndarray:
    t = _t(dur)
    saw = 2.0 * ((t * freq) % 1.0) - 1.0
    sub = np.sin(2 * np.pi * freq * t)
    env = np.minimum(1.0, t / 0.008) * np.exp(-t * 5.5)
    return (0.55 * sub + 0.45 * saw) * env * 0.42


def pad(freqs, dur: float, amp: float = 0.16) -> np.ndarray:
    t = _t(dur)
    out = np.zeros_like(t)
    for i, f in enumerate(freqs):
        detune = 1.0 + (i - 1) * 0.0016              # 轻微失谐，听起来更宽
        out += np.sin(2 * np.pi * f * detune * t) + 0.35 * np.sin(4 * np.pi * f * t)
    env = np.minimum(1.0, t / 0.35) * np.minimum(1.0, (dur - t) / 0.35)
    return out / len(freqs) * env * amp


def lead(freq: float, dur: float) -> np.ndarray:
    t = _t(dur)
    # 方波 + 三角波混合，像电子琴的主音色
    sq = np.sign(np.sin(2 * np.pi * freq * t)) * 0.5
    tri = 2.0 * np.abs(2.0 * ((t * freq) % 1.0) - 1.0) - 1.0
    env = np.minimum(1.0, t / 0.006) * np.exp(-t * 9.0)
    return (sq * 0.45 + tri * 0.55) * env * 0.22


# ---------------------------------------------------------------- 音符频率
A4 = 440.0
NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]


def freq(name: str, octave: int) -> float:
    semis = NAMES.index(name) + (octave - 4) * 12
    return A4 * (2.0 ** (semis / 12.0))


# 和弦进行：Am - F - C - G（经典 4 小节循环，明快不压抑）
PROGRESSION = [
    ("A", 2, ["A", "C", "E"]),
    ("F", 2, ["F", "A", "C"]),
    ("C", 3, ["C", "E", "G"]),
    ("G", 2, ["G", "B", "D"]),
]


def build_song() -> np.ndarray:
    print(f"[音频] 合成 {BARS} 小节 @ {BPM:.0f} BPM = {DURATION:.1f} 秒 ...")
    total = int(round(DURATION * SR)) + SR
    buf = np.zeros(total, dtype=np.float64)
    lead_buf = np.zeros(total, dtype=np.float64)   # 主旋律单独一条，方便加延迟

    def place(sig: np.ndarray, at: float, dst: np.ndarray = buf, gain: float = 1.0):
        i = int(round(at * SR))
        j = min(total, i + len(sig))
        if i < total:
            dst[i:j] += sig[: j - i] * gain

    k, s, h, ho = kick(), snare(), hat(), hat(True)

    for bar in range(BARS):
        t0 = bar * 4 * BEAT
        sec = bar // 8                     # 五段：0 前奏 / 1 加鼓 / 2 加和弦 / 3 高潮 / 4 收尾
        chord_root, chord_oct, chord = PROGRESSION[bar % 4]
        root = freq(chord_root, chord_oct)
        full = sec >= 2
        drums = sec >= 1 and bar < BARS - 2
        bassline = sec >= 1

        # --- 鼓
        if drums:
            for beat in range(4):
                place(k, t0 + beat * BEAT, gain=0.9)
            place(s, t0 + 1 * BEAT, gain=0.75)
            place(s, t0 + 3 * BEAT, gain=0.75)
            for eighth in range(8):
                place(ho if eighth % 4 == 3 else h, t0 + eighth * BEAT / 2,
                      gain=0.9 if eighth % 2 else 0.65)
        elif bar < BARS - 2:
            for eighth in range(8):        # 前奏只有踩镲
                place(h, t0 + eighth * BEAT / 2, gain=0.5)
            if bar % 4 == 0:
                place(k, t0, gain=0.5)
        else:
            if bar == BARS - 2:            # 结尾重音收场
                place(k, t0, gain=1.0)
                place(s, t0, gain=0.6)

        # --- 贝斯：八分音符律动，第 2、4 拍跳八度
        if bassline:
            for eighth in range(8):
                f = root * (2.0 if eighth in (3, 7) else 1.0)
                place(bass(f, BEAT * 0.48), t0 + eighth * BEAT / 2, gain=0.9)

        # --- 和弦垫：每小节铺一层
        if full or bar < 8:
            place(pad([freq(n, chord_oct) for n in chord], 4 * BEAT * 0.98),
                  t0, gain=0.5 if bar < 8 else 0.75)

        # --- 主旋律：琶音，最后一段变密
        if full:
            arp = [chord[0], chord[2], chord[1], chord[2]]
            step = 0.5 if sec == 4 else 1.0
            for i in range(int(8 / step)):
                note = arp[i % 4]
                octv = chord_oct + 1 + (1 if i % 4 == 3 else 0)
                place(lead(freq(note, octv), BEAT * step * 0.9),
                      t0 + i * BEAT * step, dst=lead_buf, gain=1.0)

    # --- 主旋律加一点延迟，空间感
    d = int(round(BEAT * 0.75 * SR))
    echo = np.zeros_like(lead_buf)
    echo[d:] = lead_buf[:-d] * 0.32
    echo2 = np.zeros_like(lead_buf)
    echo2[2 * d:] = lead_buf[:-2 * d] * 0.16
    buf += lead_buf + echo + echo2

    # --- 结尾淡出，避免"啪"的爆音
    fade = int(1.2 * SR)
    buf[-fade:] *= np.linspace(1.0, 0.0, fade)
    return buf[: int(round(DURATION * SR))]


def to_stereo(mono: np.ndarray) -> np.ndarray:
    """把单声道做出左右层次：高频偏右、低频偏左，听起来更宽。"""
    kernel = np.exp(-np.arange(64) / 18.0)
    kernel /= kernel.sum()
    low = np.convolve(mono, kernel, mode="same")
    high = mono - low
    left = mono + high * 0.08 + low * 0.02
    right = mono + high * 0.14 - low * 0.02
    return np.stack([left, right], axis=1)


def write_wav(path: str, stereo: np.ndarray) -> None:
    peak = float(np.max(np.abs(stereo)))
    stereo = np.tanh(stereo / max(peak, 1e-9) * 1.05)           # 软限幅，削掉刺耳瞬态
    stereo *= 0.92 / max(float(np.max(np.abs(stereo))), 1e-9)   # 响度统一
    pcm = np.clip(stereo * 32767.0, -32768, 32767).astype("<i2")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"[音频] 已写入 {os.path.relpath(path, ROOT)}  "
          f"({os.path.getsize(path) / 1e6:.2f} MB, {pcm.shape[0] / SR:.1f} 秒)")


def write_click(path: str) -> None:
    t = _t(0.09)
    noise = np.diff(np.random.default_rng(3).standard_normal(len(t)), prepend=0.0)
    sig = (np.sin(2 * np.pi * 1180 * t) * np.exp(-t * 42.0) + noise * np.exp(-t * 90.0) * 0.25)
    sig = np.tanh(sig * 1.4) * 0.5
    write_wav(path, np.stack([sig, sig], axis=1))


# ---------------------------------------------------------------- 谱面
CHORD_LANES = {
    "A": [0, 1], "B": [0, 2], "C": [0, 3], "D": [1, 2],
    "E": [1, 3], "F": [2, 3], "G": [0, 1, 2, 3],
}


def bar_notes(bar: int, pattern: str) -> list[tuple[float, list[int]]]:
    """
    把一小节的图案变成 [(时间, [轨道...]), ...]。
    pattern 是 8 个字符，对应一小节的 8 个八分音符：
      0-3  该轨道单键
      '.'  空拍
      A-G  双押/多押（见 CHORD_LANES）
    """
    out = []
    t0 = bar * 4 * BEAT
    for i, ch in enumerate(pattern[:8]):
        if ch in ".x":
            continue
        lanes = CHORD_LANES[ch] if ch in CHORD_LANES else [int(ch)]
        out.append((t0 + i * BEAT / 2, lanes))
    return out


EASY_PATTERNS = [
    "0...1...", "0...2...", "1...3...", "2...3...",
    "0.1.2.3.", "3.2.1.0.", "0...3...", "1...2...",
    "0.2.1.3.", "3.1.2.0.", "0...0...", "1.2.1.2.",
]
NORMAL_PATTERNS = [
    "0.1.2.3.", "0.23.1..", "0.1.23.3", "3.2.1.0.",
    "0.2.1.3.", "01..23..", "0.12.32.", "3.21.03.",
    "0.1.0.23", "2.3.2.10", "0123.012", "0..12..3",
]
HARD_PATTERNS = [
    "01230123", "0.1.2.3G", "33.22.1.1", "0A1B2C3D",
    "0.23.1.3", "012.3210", "0F1E2D3C", "3.12.032",
    "02130213", "0.1.2.3.", "30A1E2F3", "01230123",
]


def make_chart(difficulty: str, level: int, patterns: list[str], start_bar: int) -> dict:
    notes = []
    bar = start_bar
    step = 0
    while bar < BARS - 1:
        if bar % 8 != 7:                       # 每 8 小节留一小节空拍，谱面有呼吸感
            for t, lanes in bar_notes(bar, patterns[step % len(patterns)]):
                for lane in lanes:
                    notes.append({"time": round(t, 3), "lane": lane})
        bar += 1
        step += 1

    notes.sort(key=lambda n: (n["time"], n["lane"]))
    dedup, seen = [], set()
    for n in notes:                            # 同轨同一时刻只留一个
        key = (n["time"], n["lane"])
        if key in seen:
            continue
        seen.add(key)
        dedup.append(n)

    return {
        "songName": "Neon Pulse",
        "audio": "NeonPulse",
        "bpm": BPM,
        "difficulty": difficulty,
        "level": level,
        "offset": 0.0,
        "notes": dedup,
    }


def make_calibration_chart() -> dict:
    """校准谱：每秒一个键，四轨轮转，连续 32 个，用来确认自己的手感/延迟。"""
    notes = [{"time": round(2.0 + i * 1.0, 3), "lane": i % LANES} for i in range(32)]
    notes.append({"time": 34.0, "lane": 0})    # 最后一组双押，检查同时判定
    notes.append({"time": 34.0, "lane": 3})
    notes.sort(key=lambda n: (n["time"], n["lane"]))
    return {
        "songName": "Neon Pulse",
        "audio": "NeonPulse",
        "bpm": BPM,
        "difficulty": "Calibration",
        "level": 1,
        "offset": 0.0,
        "notes": notes,
    }


def write_json(path: str, obj) -> None:
    """手写排版的 JSON：一行一个音符，学生可以用记事本直接改。"""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    lines = ["{"]
    for k, v in obj.items():
        if k == "notes":
            lines.append('  "notes": [')
            for i, n in enumerate(v):
                comma = "," if i < len(v) - 1 else ""
                lines.append(f'    {{"time": {n["time"]:.3f}, "lane": {n["lane"]}}}{comma}')
            lines.append("  ]")
        elif isinstance(v, str):
            lines.append(f'  "{k}": "{v}",')
        else:
            lines.append(f'  "{k}": {json.dumps(v, ensure_ascii=False)},')
    lines.append("}")
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(lines) + "\n")
    print(f"[谱面] 已写入 {os.path.relpath(path, ROOT)}  ({len(obj['notes'])} 音符)")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--force", action="store_true", help="覆盖已存在的音频")
    ap.add_argument("--skip-audio", action="store_true", help="只生成谱面")
    args = ap.parse_args()

    os.makedirs(AUDIO_DIR, exist_ok=True)
    os.makedirs(CHART_DIR, exist_ok=True)

    wav = os.path.join(AUDIO_DIR, "NeonPulse.wav")
    if args.skip_audio and os.path.exists(wav):
        print("[音频] 已存在，跳过")
    elif os.path.exists(wav) and not args.force:
        print(f"[音频] {os.path.relpath(wav, ROOT)} 已存在，跳过（要重新生成加 --force）")
    else:
        write_wav(wav, to_stereo(build_song()))

    click = os.path.join(AUDIO_DIR, "Click.wav")
    if not os.path.exists(click) or args.force:
        write_click(click)

    write_json(os.path.join(CHART_DIR, "NeonPulse_Easy.json"),
               make_chart("Easy", 3, EASY_PATTERNS, start_bar=1))
    write_json(os.path.join(CHART_DIR, "NeonPulse_Normal.json"),
               make_chart("Normal", 6, NORMAL_PATTERNS, start_bar=1))
    write_json(os.path.join(CHART_DIR, "NeonPulse_Hard.json"),
               make_chart("Hard", 9, HARD_PATTERNS, start_bar=1))
    write_json(os.path.join(CHART_DIR, "NeonPulse_Calibration.json"), make_calibration_chart())

    # 手册第 6 章那个"最简谱面"：纯数组、没有元数据。
    # 工程里的谱面加载器两种写法都支持。
    manual = [{"time": 2.0, "lane": 0}, {"time": 3.0, "lane": 1},
              {"time": 4.0, "lane": 2}, {"time": 5.0, "lane": 3}]
    p = os.path.join(CHART_DIR, "ManualTest.json")
    with open(p, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("[\n")
        for i, n in enumerate(manual):
            comma = "," if i < len(manual) - 1 else ""
            fh.write(f'  {{"time": {n["time"]:.1f}, "lane": {n["lane"]}}}{comma}\n')
        fh.write("]\n")
    print(f"[谱面] 已写入 {os.path.relpath(p, ROOT)}  ({len(manual)} 音符，手册示例)")

    print("\n完成。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
