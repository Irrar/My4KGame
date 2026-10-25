#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成 Master（大师）难度谱面。

设计目标（用户需求）
────────────────────
· **采音密集**：以该曲现有最难的谱面为"采音锚点"（作者已经对齐过音乐），
  在此基础上按 16 分/8 分网格加密，而不是凭空塞音符；
· **几乎没有整屏空白的休息段**：任何超过 max_gap 的空档都会用 8 分音符补上；
· **可玩**：严格遵守 Hold 规则（Hold 期间同轨不得有音符、结束后留 140ms）、
  同刻同轨不重复、同轨最小间距不低于该 BPM 的 16 分音符；
· **键型**：交互 / 梯子 / 楼梯 / 对切（双押），按段落轮换，不无脑随机。

采音来源
────────
1. 锚点：`*_Expert.json`（没有 Expert 的曲子用 Hard）；时间戳原样保留；
2. 有 WAV 的曲子（NeonPulse）额外做频谱通量 onset 检测，把真实音头并进候选；
3. 其余候选是 BPM 网格上的 16 分 / 8 分位置。

用法
────
    python tools/make_master.py              # 只生成 + 体检（写到磁盘）
    python tools/make_master.py --dry-run    # 只打印统计
"""

from __future__ import annotations

import argparse
import json
import math
import os
import random
import re
import sys
import wave

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import chart_tools as ct  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CHART_DIR = os.path.join(ROOT, "Assets", "Resources", "Charts")
AUDIO_DIR = os.path.join(ROOT, "Assets", "Resources", "Audio")

# 每首歌的参数：基准谱面 / 目标密度 / 最大空档 / 同轨最小间距
SONGS = [
    {"audio": "NeonPulse", "base": "NeonPulse_Hard.json", "name": "Neon Pulse",
     "wav": "NeonPulse.wav", "nps": 5.4, "max_gap": 600, "min_gap": 118, "chord": 0.16},
    {"audio": "WorldsEndBLACKBOX", "base": "WorldsEndBLACKBOX_Expert.json", "name": "World's end BLACKBOX",
     "wav": None, "nps": 7.6, "max_gap": 600, "min_gap": 70, "chord": 0.18},
    {"audio": "in_your_blue_eyes", "base": "InYourBlueEyes_Expert.json", "name": "in your blue eyes",
     "wav": None, "nps": 6.1, "max_gap": 600, "min_gap": 98, "chord": 0.16},
]


# ------------------------------------------------------------------ 音频 onset
def detect_onsets(wav_path, min_gap_ms=60.0):
    """频谱通量 onset 检测（纯 numpy，不需要 ffmpeg）。返回毫秒时间戳列表。"""
    import numpy as np

    with wave.open(wav_path, "rb") as wav:
        channels = wav.getnchannels()
        rate = wav.getframerate()
        width = wav.getsampwidth()
        frames = wav.getnframes()
        raw = wav.readframes(frames)

    if width != 2:
        return []

    data = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0
    if channels > 1:
        data = data.reshape(-1, channels).mean(axis=1)

    win, hop = 1024, 256
    count = max(0, (len(data) - win) // hop)
    if count <= 0:
        return []

    window = np.hanning(win).astype(np.float32)
    flux = np.zeros(count, dtype=np.float32)
    previous = None
    for i in range(count):
        chunk = data[i * hop: i * hop + win] * window
        spectrum = np.abs(np.fft.rfft(chunk))
        if previous is not None:
            diff = spectrum - previous
            flux[i] = np.maximum(diff, 0).sum()
        previous = spectrum

    if flux.max() <= 0:
        return []

    flux /= flux.max()
    # 自适应阈值：局部均值 + 两倍标准差，再加一个绝对下限
    onsets = []
    span = max(1, int(0.25 * rate / hop))
    last_ms = -1e9
    for i in range(1, count - 1):
        lo, hi = max(0, i - span), min(count, i + span)
        local = flux[lo:hi]
        threshold = max(0.08, local.mean() + 1.2 * local.std())
        if flux[i] < threshold or flux[i] < flux[i - 1] or flux[i] < flux[i + 1]:
            continue
        ms = (i * hop + win / 2) / rate * 1000.0
        if ms - last_ms < min_gap_ms:
            continue
        onsets.append(ms)
        last_ms = ms
    return onsets


# ------------------------------------------------------------------ 候选时间
def build_candidates(base_notes, bpm, start_ms, end_ms, onsets, cfg):
    """候选时刻 → 优先级。数字越小越优先。"""
    beat = 60000.0 / bpm
    sixteenth = beat / 4.0
    eighth = beat / 2.0

    anchors = sorted({round(n["time"] * 1000.0, 1) for n in base_notes})

    def snap(ms):
        return round(round(ms / sixteenth) * sixteenth, 1)

    prio = {}

    def push(t, p):
        t = round(t, 1)
        if t < start_ms or t > end_ms:
            return
        if t not in prio or p < prio[t]:
            prio[t] = p

    for t in anchors:
        push(t, 0)                                   # 作者采音，最高优先

    for t in onsets:
        push(snap(t), 1)                             # 真实音头
        push(snap(t) - sixteenth, 2)
        push(snap(t) + sixteenth, 2)

    for t in anchors:                                # 锚点前后 16 分 = 加密的"过门"
        push(t - sixteenth, 2)
        push(t + sixteenth, 2)

    # 网格铺满（8 分优先，16 分兜底）
    t = math.ceil(start_ms / eighth) * eighth
    while t <= end_ms:
        push(t, 3)
        t += eighth

    t = math.ceil(start_ms / sixteenth) * sixteenth
    while t <= end_ms:
        push(t, 4)
        t += sixteenth

    return prio


def pick_times(prio, cfg, start_ms, end_ms, bpm):
    """挑出最终时间点。

    分三步：
      A. 锚点（作者采音）+ 真实 onset 无条件保留（彼此只做去重）；
      B. 网格候选按**时间顺序**加入：与最近邻距离 ≥ min_gap，且局部密度没到目标；
      C. 补洞：任何超过 max_gap 的空档用 8 分音符填上（"没有休息段"）。
    """
    import bisect

    sixteenth = 60000.0 / bpm / 4.0
    eighth = sixteenth * 2.0
    min_gap = max(cfg["min_gap"], sixteenth * 0.92)

    accepted = []

    def too_close(t):
        i = bisect.bisect_left(accepted, t)
        if i > 0 and t - accepted[i - 1] < min_gap:
            return True
        if i < len(accepted) and accepted[i] - t < min_gap:
            return True
        return False

    # ---- A. 锚点 + onset
    for t in sorted(t for t in prio if prio[t] <= 1):
        if not too_close(t):
            bisect.insort(accepted, t)

    # ---- B. 网格候选：时间顺序 + 局部密度
    # 注意：accepted 里在 A 阶段已经放好了**全部**锚点（包括未来的），
    # 所以统计"最近 window 内"必须用 [t-window, t] 的区间计数，
    # 不能用 len(accepted) - lo（那会把未来锚点也数进去，导致几乎全部被拒）。
    target = cfg["nps"]
    window = 3000.0
    for t in sorted(t for t in prio if prio[t] > 1):
        if too_close(t):
            continue
        lo = bisect.bisect_left(accepted, t - window)
        hi = bisect.bisect_right(accepted, t)
        if (hi - lo) / (window / 1000.0) >= target:
            continue
        bisect.insort(accepted, t)

    # ---- C. 补洞
    result = list(accepted)
    for a, b in zip(accepted, accepted[1:]):
        if b - a <= cfg["max_gap"]:
            continue
        t = a + eighth
        while t < b - min_gap * 0.5:
            result.append(round(t, 1))
            t += eighth
    return sorted(set(round(t, 1) for t in result))


# ------------------------------------------------------------------ 键型分配
def assign_lanes(times, holds, bpm, cfg, seed=20261025):
    """给每个时刻分配轨道。

    holds: {time_ms: (duration_ms, lane)} —— 基准谱的 Hold 原样保留（含轨道）。
    单点用 交互/梯子/楼梯 轮换；同一时刻两个音符 = 对切（0-3 / 1-2）。
    """
    rng = random.Random(seed)
    busy = ct.LaneBusy()

    # 先把 Hold 放进占用表（它们的轨道不动）
    for t, (dur, lane) in sorted(holds.items()):
        busy.add_hold(lane, t, t + dur)

    sixteenth = 60000.0 / bpm / 4.0
    lanes_of = {}
    prev_lane = 1
    run = 0
    pair = rng.choice([(0, 1), (2, 3), (0, 3), (1, 2)])
    rail_dir = 1

    def free(lane, t):
        return busy.free(lane, t, t)

    for t in times:
        if t in holds:
            lanes_of[t] = [holds[t][1]]
            prev_lane = holds[t][1]
            continue

        # 每个段落换一次键型（每 4~8 个音）
        if run % 6 == 0:
            kind = rng.random()
            if kind < 0.45:
                pair = rng.choice([(0, 1), (2, 3), (0, 3), (1, 2)])
            elif kind < 0.75:
                rail_dir = rng.choice([1, -1])
            else:
                pair = rng.choice([(0, 3), (1, 2)])

        # 每小节（4 拍 ≈ 16 个 16 分位置）给一个双押，做出层次
        beat_index = round(t / (60000.0 / bpm))
        want_chord = (beat_index % 4 == 0) and rng.random() < cfg.get("chord", 0.18)

        if want_chord:
            options = [(0, 3), (1, 2), (0, 2), (1, 3)]
            rng.shuffle(options)
            for a, b in options:
                if free(a, t) and free(b, t):
                    lanes_of[t] = [a, b]
                    prev_lane = a
                    run += 1
                    break
            if t in lanes_of:
                continue

        lane = -1
        if run % 6 < 3:
            candidate = pair[run % 2]
            if candidate != prev_lane and free(candidate, t):
                lane = candidate
        if lane < 0:
            # 梯子：顺着方向走，撞墙就折返
            for _ in range(4):
                step = rail_dir * 1
                candidate = min(3, max(0, prev_lane + step))
                if candidate == prev_lane:
                    rail_dir *= -1
                    continue
                if free(candidate, t):
                    lane = candidate
                    break
                rail_dir *= -1
        if lane < 0:
            for candidate in sorted(range(4), key=lambda l: (abs(l - prev_lane), l)):
                if free(candidate, t) and candidate != prev_lane:
                    lane = candidate
                    break
        if lane < 0:
            lane = prev_lane

        lanes_of[t] = [lane]
        prev_lane = lane
        run += 1

    return lanes_of


# ------------------------------------------------------------------ 主流程
def build_song(cfg, dry_run=False):
    base_path = os.path.join(CHART_DIR, cfg["base"])
    base = ct.load_chart(base_path)
    base_notes = base["notes"]
    bpm = base.get("bpm", 120) or 120
    offset = base.get("offset", 0) or 0

    # 时间轴范围：基准谱第一个音 ~ 最后一个音 + 收尾
    first = min(n["time"] for n in base_notes) * 1000.0
    last = max(n["time"] + n.get("duration", 0.0) for n in base_notes) * 1000.0
    start_ms = max(0.0, first - 300.0)
    end_ms = last + 2000.0

    onsets = []
    wav_name = cfg.get("wav")
    if wav_name:
        wav_path = os.path.join(AUDIO_DIR, wav_name)
        if os.path.exists(wav_path):
            onsets = detect_onsets(wav_path)
            end_ms = max(end_ms, onsets[-1] if onsets else end_ms)

    # Hold：保留基准谱的 Hold，但砍掉过短的（<260ms 来不及按）
    holds = {}
    for n in base_notes:
        dur = n.get("duration", 0.0) * 1000.0
        if dur >= 260.0:
            holds[round(n["time"] * 1000.0, 1)] = (dur, int(n["lane"]))

    prio = build_candidates(base_notes, bpm, start_ms, end_ms, onsets, cfg)
    times = pick_times(prio, cfg, start_ms, end_ms, bpm)

    # Hold 的时间点必须在 times 里（它们本来就是锚点，正常都在）
    times = sorted(set(times) | set(holds.keys()))

    lanes_of = assign_lanes(times, holds, bpm, cfg)

    notes = []
    for t in sorted(times):
        lanes = lanes_of.get(t, [0])
        if t in holds:
            dur, lane = holds[t]
            notes.append({"time": round((t + offset * 0.0) / 1000.0, 3),
                          "lane": int(lane), "duration": round(dur / 1000.0, 3)})
            continue
        for lane in lanes:
            notes.append({"time": round(t / 1000.0, 3), "lane": int(lane)})

    notes.sort(key=lambda n: (n["time"], n["lane"]))

    # 体检
    conflicts, min_gap, dups = ct.validate(notes, bpm, offset)
    gaps = [round((b["time"] - a["time"]) * 1000.0) for a, b in zip(notes, notes[1:])
            if abs(b["time"] - a["time"]) > 1e-6]
    max_gap = max(gaps) if gaps else 0
    duration_s = (notes[-1]["time"] - notes[0]["time"]) or 1.0
    nps = len(notes) / duration_s

    data = {
        "songName": base.get("songName", cfg["name"]),
        "audio": base.get("audio", cfg["audio"]),
        "bpm": base.get("bpm", 120),
        "difficulty": "Master",
        "level": 15,
        "offset": base.get("offset", 0),
        "notes": notes,
    }

    print("\n==== %s（Master）" % cfg["name"])
    print("  基准: %-32s BPM %-6g 锚点 %d 个" % (cfg["base"], bpm, len({n['time'] for n in base_notes})))
    print("  音频 onset: %s" % (("%d 个" % len(onsets)) if onsets else "无（用谱面锚点+网格）"))
    print("  音符 %d（基准 %d，加密 %.1f 倍）  时长 %.1f s  平均 %.2f 音符/秒"
          % (len(notes), len(base_notes), len(notes) / max(1, len(base_notes)), duration_s, nps))
    print("  最大空档 %.0f ms（目标 ≤ %d）  同轨最小间距 %.0f ms  冲突 %d  重复 %d"
          % (max_gap, cfg["max_gap"], min_gap, len(conflicts), dups))
    over = [g for g in gaps if g > cfg["max_gap"]]
    if over:
        print("  !! 仍有 %d 个超过目标的空档，最大 %d ms" % (len(over), max(over)))

    if not dry_run:
        stem = cfg["base"].replace("_Expert.json", "").replace("_Hard.json", "").replace("_Normal.json", "")
        out = os.path.join(CHART_DIR, "%s_Master.json" % stem)
        with open(out, "w", encoding="utf-8", newline="\n") as fh:
            fh.write("{\n")
            fh.write('  "songName": %s,\n' % json.dumps(data["songName"], ensure_ascii=False))
            fh.write('  "audio": %s,\n' % json.dumps(data["audio"], ensure_ascii=False))
            fh.write('  "bpm": %s,\n' % ("%g" % float(data["bpm"])))
            fh.write('  "difficulty": "Master",\n')
            fh.write('  "level": %d,\n' % data["level"])
            fh.write('  "offset": %s,\n' % ("%g" % float(data["offset"])))
            fh.write('  "notes": [\n')
            lines = ["    " + ct._note_line(n) for n in notes]
            fh.write(",\n".join(lines))
            fh.write("\n  ]\n}\n")
        print("  已写出: %s" % os.path.basename(out))

    return data, conflicts, dups, max_gap


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--root", default=None)
    args = ap.parse_args()

    global CHART_DIR, AUDIO_DIR
    if args.root:
        CHART_DIR = os.path.join(args.root, "Assets", "Resources", "Charts")
        AUDIO_DIR = os.path.join(args.root, "Assets", "Resources", "Audio")

    ok = True
    for cfg in SONGS:
        data, conflicts, dups, max_gap = build_song(cfg, args.dry_run)
        if conflicts or dups:
            ok = False
            print("  !! 体检不通过：冲突 %d 重复 %d" % (len(conflicts), dups))
        if max_gap > cfg["max_gap"] * 1.3:
            ok = False
            print("  !! 空档过大：%d ms" % max_gap)

    print("\n结论：%s" % ("全部通过" if ok else "有问题，请检查上面的 !! 行"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
