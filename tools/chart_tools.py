#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
谱面工具：Hold 冲突修复 + Expert 键型重排（交互 / 梯子 / 楼梯）

设计原则（重要）
────────────────
1. **绝不动时间轴**：所有音符的 time / duration 原样保留 —— 作者的采音不动，
   这一工具只改 lane（以及必要时的删减）。
2. **Hold 优先**：Hold 占用的整段时间内，同轨道不允许出现任何音符，
   并且 Hold 结束后要留 `hold_gap_ms` 的空白，否则玩家来不及松手再按（反盾不可读）。
3. **键型只改单点**：只在"连续单点（没有双押、没有 Hold）"的段落里做键型，
   双押保持原样，避免把对拍/对切改成乱序。
4. **可用性优先于键型**：模板要求的轨道如果正被 Hold 占用，就退化成"挑一条空轨道"，
   而不是硬塞。

键型取自《下落式音游齐民要术》：梯子(rail)/钩子·楼梯(staircase)/交互(trill)。

用法
────
  python tools/chart_tools.py                 # 只体检
  python tools/chart_tools.py --repair        # 修 Hold 冲突（所有谱面）
  python tools/chart_tools.py --repair --patterns expert   # 冲突修复 + 只重排 Expert 键型
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import random
import re
import sys
from collections import Counter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CHART_DIR = os.path.join(ROOT, "Assets", "Resources", "Charts")
HOLD_GAP_MS = 140.0      # Hold 结束后同轨至少留这么多空白
MIN_REPEAT_MS = 110.0    # 同一轨道两个单点之间至少隔这么多（避免误读成纵连）
LANES = 4


# ------------------------------------------------------------------ 基础
def load_chart(path):
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    if isinstance(data, list):                 # 手册式纯数组
        data = {"notes": data, "bpm": 120.0}
    return data


def note_ms(note, bpm, offset_s):
    if note.get("beat", -1) >= 0:
        return note["beat"] * 60000.0 / bpm + offset_s * 1000.0
    return note.get("time", 0.0) * 1000.0 + offset_s * 1000.0


def note_dur_ms(note, bpm):
    if note.get("durationBeat", -1) >= 0:
        return note["durationBeat"] * 60000.0 / bpm
    return note.get("duration", 0.0) * 1000.0


def is_hold(note, bpm):
    return note_dur_ms(note, bpm) > 1.0


def sort_notes(notes):
    return sorted(notes, key=lambda n: (n.get("time", 0.0), n.get("lane", 0)))


# ------------------------------------------------------------------ 写回（保留原排版）
def _num(value, digits=3):
    """3.000 / 1.5 / 0.582 —— 去掉多余的 0，但至少留一位小数。"""
    text = ("%." + str(digits) + "f") % float(value)
    if "." in text:
        text = text.rstrip("0")
        if text.endswith("."):
            text += "0"
    return text


def _note_line(note):
    parts = ['"time": %s' % _num(note.get("time", 0.0)),
             '"lane": %d' % int(note.get("lane", 0))]
    if note.get("beat", -1) >= 0:
        parts.append('"beat": %s' % _num(note["beat"]))
    if note.get("duration", 0.0) > 0.0:
        parts.append('"duration": %s' % _num(note["duration"]))
    if note.get("durationBeat", -1) >= 0:
        parts.append('"durationBeat": %s' % _num(note["durationBeat"]))
    return "{" + ", ".join(parts) + "}"


def write_chart(path, data, notes):
    """写回谱面，尽量只改动真正变化的部分。

    仓库里存在两种排版风格：
      · 紧凑型（每个音符一行，例如 {"time": 2.000, "lane": 0}）—— 只替换 notes 数组文本；
      · 多行型（每个字段一行）—— 用 indent=2 整体重排，和原风格一致。
    两种都只在"内容真的变了"时才写。
    """
    with open(path, encoding="utf-8") as fh:
        raw = fh.read()

    match = re.search(r'"notes"\s*:\s*\[', raw)
    if match is None:
        return False

    start = match.end()
    depth, i, in_string, escaped = 1, start, False, False
    while i < len(raw) and depth > 0:
        ch = raw[i]
        if in_string:
            if escaped:
                escaped = False
            elif ch == "\\":
                escaped = True
            elif ch == '"':
                in_string = False
        else:
            if ch == '"':
                in_string = True
            elif ch == "[":
                depth += 1
            elif ch == "]":
                depth -= 1
        i += 1

    end = i - 1                      # ']' 的位置
    block = raw[start:end]
    # 判断原文件的写法：`{"time": ...` 在同一行 = 紧凑型；否则是多行型
    compact = re.search(r'\{[ \t]*"time"', block) is not None

    if compact:
        body = ",\n".join("    " + _note_line(n) for n in notes)
        new_text = raw[:start] + "\n" + body + "\n  " + raw[end:]
    else:
        data = dict(data)
        data["notes"] = notes
        new_text = json.dumps(data, ensure_ascii=False, indent=2) + "\n"

    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(new_text)
    return True


# ------------------------------------------------------------------ 占用表
class LaneBusy:
    """记录每条轨道在哪些时间区间被 Hold 占着。"""

    def __init__(self):
        self.intervals = {lane: [] for lane in range(LANES)}

    def add_hold(self, lane, start_ms, end_ms):
        self.intervals[lane].append((start_ms, end_ms + HOLD_GAP_MS))

    def free(self, lane, start_ms, end_ms):
        for s, e in self.intervals[lane]:
            if start_ms < e and end_ms > s - HOLD_GAP_MS:
                return False
        return True


def build_busy(notes, bpm, offset_s):
    busy = LaneBusy()
    for n in notes:
        if is_hold(n, bpm):
            s = note_ms(n, bpm, offset_s)
            busy.add_hold(n["lane"], s, s + note_dur_ms(n, bpm))
    return busy


# ------------------------------------------------------------------ 修复
def repair_holds(notes, bpm, offset_s):
    """把落在 Hold 区间内（或太贴近 Hold 结束）的音符挪到空轨道；实在没地方才删。

    实现要点：按时间顺序**增量**放置，只用一个"已被更早的 Hold 占住"的表来判断，
    因此不会有"早于 Hold 的音符被误判"的问题。
    时间轴完全不变；Hold 的轨道也不动（动它会让占用表失效）。
    """
    placed = LaneBusy()          # 只记录已经放下的 Hold
    used = set()                 # 已经放下的 (时刻, 轨道)，避免两个音符叠在一起
    stats = {"moved": 0, "dropped": 0, "checked": 0, "hold_moved": 0}
    kept = []

    for n in sort_notes(notes):
        stats["checked"] += 1
        s = note_ms(n, bpm, offset_s)
        dur = note_dur_ms(n, bpm)
        e = s + dur
        lane = int(n["lane"])
        key = (round(s, 1), lane)

        if placed.free(lane, s, e) and key not in used:
            kept.append(n)
            used.add(key)
            if dur > 1.0:
                placed.add_hold(lane, s, e)
            continue

        # 换轨：优先相邻轨道（读谱更自然）
        moved = False
        for cand in sorted(range(LANES), key=lambda l: (abs(l - lane), l)):
            if cand == lane:
                continue
            ckey = (round(s, 1), cand)
            if placed.free(cand, s, e) and ckey not in used:
                n["lane"] = cand
                kept.append(n)
                used.add(ckey)
                if dur > 1.0:
                    placed.add_hold(cand, s, e)
                    stats["hold_moved"] += 1
                else:
                    stats["moved"] += 1
                moved = True
                break

        if not moved:
            stats["dropped"] += 1

    return sort_notes(kept), stats


# ------------------------------------------------------------------ 键型
# 模板：一组轨道序列
TEMPLATES = {
    "rail_up": [0, 1, 2, 3],
    "rail_down": [3, 2, 1, 0],
    "stair_up": [0, 1, 2, 3, 2, 1],
    "stair_down": [3, 2, 1, 0, 1, 2],
    "trill_outer": [0, 3, 0, 3],
    "trill_inner": [1, 2, 1, 2],
    "trill_left": [0, 1, 0, 1],
    "trill_right": [2, 3, 2, 3],
    # 交互可选的两条轨道（外手 / 内手 / 双手）
    "trill_pairs": [(0, 1), (2, 3), (0, 3), (1, 2)],
}


def split_runs(notes, bpm, offset_s):
    """把"连续的单点"切成若干段。

    切段依据（三条都会断段）：
      · 段内不能有双押（同一时刻两个音符）；
      · 两个单点之间如果夹着 Hold，也断段 —— Hold 会打断手感，键型不该跨过它；
      · 间隔超过 620ms 视为换句。
    """
    times = Counter(round(note_ms(n, bpm, offset_s), 1) for n in notes)
    hold_times = sorted(note_ms(n, bpm, offset_s) for n in notes if is_hold(n, bpm))
    singles = [n for n in notes
               if not is_hold(n, bpm) and times[round(note_ms(n, bpm, offset_s), 1)] == 1]
    singles.sort(key=lambda n: note_ms(n, bpm, offset_s))

    runs, current = [], []
    prev_t = None
    for n in singles:
        t = note_ms(n, bpm, offset_s)
        if prev_t is not None:
            too_far = (t - prev_t) > 620.0
            hold_between = any(prev_t < ht < t for ht in hold_times)
            if too_far or hold_between:
                if len(current) >= 3:
                    runs.append(current)
                current = []
        current.append(n)
        prev_t = t
    if len(current) >= 3:
        runs.append(current)
    return runs


def apply_patterns(notes, bpm, offset_s, seed=20261025):
    """把 Expert 的连续单点段落重排成 交互 / 梯子 / 楼梯。

    约束：不动时间；不占用 Hold 的时间段；不与其它音符撞在同一 (时刻, 轨道)；
    同轨不贴太近；尽量不与上一个音同轨。
    """
    rng = random.Random(seed)
    busy = build_busy(notes, bpm, offset_s)
    runs = split_runs(notes, bpm, offset_s)

    run_ids = {id(n) for run in runs for n in run}
    # 不参与重排的音符（双押、Hold）：它们占着 (时刻, 轨道)，重排时不能撞上去
    occupied = {(round(note_ms(n, bpm, offset_s), 1), int(n["lane"]))
                for n in notes if id(n) not in run_ids}

    assigned = {}
    changed = 0

    prev_lane = None
    for run in runs:
        # 每段固定一个"交互对"，段内反复交替，读谱最自然
        pair = rng.choice(TEMPLATES["trill_pairs"])
        direction = rng.choice([1, -1])
        i = 0
        while i < len(run):
            remaining = len(run) - i
            if remaining >= 6 and i % 6 == 0:
                template = TEMPLATES["stair_up"] if direction > 0 else TEMPLATES["stair_down"]
            elif remaining >= 4 and i % 4 == 0:
                template = TEMPLATES["rail_up"] if direction > 0 else TEMPLATES["rail_down"]
            else:
                template = [pair[i % 2], pair[(i + 1) % 2], pair[i % 2], pair[(i + 1) % 2]]
            placed = 0
            for lane in template:
                if placed >= remaining:
                    break
                note = run[i + placed]
                t = note_ms(note, bpm, offset_s)
                key_t = round(t, 1)
                take = -1
                if (lane != prev_lane and busy.free(lane, t, t)
                        and not _too_close(assigned, lane, t)
                        and (key_t, lane) not in occupied):
                    take = lane
                else:
                    for cand in sorted(range(LANES), key=lambda l: (abs(l - lane), l)):
                        if (cand != prev_lane and busy.free(cand, t, t)
                                and not _too_close(assigned, cand, t)
                                and (key_t, cand) not in occupied):
                            take = cand
                            break
                if take < 0:
                    take = int(note["lane"])
                if int(note["lane"]) != take:
                    changed += 1
                note["lane"] = take
                assigned[take] = assigned.get(take, []) + [t]
                occupied.add((key_t, take))
                prev_lane = take
                placed += 1
            i += max(1, placed)
    return notes, changed


def _too_close(assigned, lane, t_ms):
    for t in assigned.get(lane, []):
        if abs(t - t_ms) < MIN_REPEAT_MS:
            return True
    return False


# ------------------------------------------------------------------ 体检
def validate(notes, bpm, offset_s):
    ents = sorted((note_ms(n, bpm, offset_s), note_ms(n, bpm, offset_s) + note_dur_ms(n, bpm),
                   n["lane"], is_hold(n, bpm)) for n in notes)
    conflicts = []
    for i, (s1, e1, l1, h1) in enumerate(ents):
        if not h1:
            continue
        for s2, e2, l2, h2 in ents[i + 1:]:
            if s2 > e1 + HOLD_GAP_MS:
                break
            if l2 == l1:
                conflicts.append((s1, e1, s2, "hold" if h2 else "tap"))

    # 同 (时刻, 轨道) 重复（两个音符叠在一起）
    dup = Counter((round(s, 1), l) for s, e, l, h in ents)
    duplicates = sum(1 for v in dup.values() if v > 1)

    # 同轨最小间距
    by_lane = {}
    for s, e, l, h in ents:
        by_lane.setdefault(l, []).append((s, e, h))
    min_gap = 1e9
    for arr in by_lane.values():
        arr.sort()
        for (s1, e1, h1), (s2, e2, h2) in zip(arr, arr[1:]):
            min_gap = min(min_gap, s2 - (e1 if h1 else s1))
    return conflicts, min_gap, duplicates


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repair", action="store_true", help="修 Hold 冲突")
    ap.add_argument("--patterns", choices=["none", "expert", "all"], default="none",
                    help="键型重排范围")
    ap.add_argument("--apply", action="store_true", help="真的写回文件（默认只打印预览）")
    ap.add_argument("--root", default=None, help="Unity 工程根目录（默认脚本上一级）")
    args = ap.parse_args()

    global CHART_DIR
    if args.root:
        CHART_DIR = os.path.join(args.root, "Assets", "Resources", "Charts")

    total_before = total_after = 0
    written, untouched = [], []
    for path in sorted(glob.glob(os.path.join(CHART_DIR, "*.json"))):
        data = load_chart(path)
        notes = data.get("notes", [])
        if not notes:
            continue
        original_notes = [dict(n) for n in notes]
        bpm = data.get("bpm", 120) or 120
        offset_s = data.get("offset", 0) or 0
        name = os.path.basename(path)

        c_before, gap_before, dup_before = validate(notes, bpm, offset_s)
        total_before += len(c_before)

        if not args.repair and args.patterns == "none":
            print("%-38s 冲突 %3d  同刻同轨重复 %2d  同轨最小间距 %7.0fms" %
                  (name, len(c_before), dup_before, gap_before))
            continue

        notes = sort_notes(notes)
        if args.repair:
            notes, stats = repair_holds(notes, bpm, offset_s)
            print("%-38s 修复: 检查 %d / 挪轨 %d / Hold挪 %d / 删除 %d" %
                  (name, stats["checked"], stats["moved"], stats["hold_moved"], stats["dropped"]))

        if args.patterns == "all" or (args.patterns == "expert" and "Expert" in name):
            notes, changed = apply_patterns(notes, bpm, offset_s)
            print("%-38s 键型重排: 改动 %d 个音符" % (name, changed))
            # 键型重排后再兜底一次，确保没把音符塞进 Hold
            notes, stats2 = repair_holds(notes, bpm, offset_s)
            if stats2["moved"] or stats2["dropped"] or stats2["hold_moved"]:
                print("%-38s 重排后兜底: 挪轨 %d / Hold挪 %d / 删除 %d" %
                      (name, stats2["moved"], stats2["hold_moved"], stats2["dropped"]))

        c_after, gap_after, dup_after = validate(notes, bpm, offset_s)
        total_after += len(c_after)
        print("%-38s 修复后: 冲突 %d  重复 %d  同轨最小间距 %7.0fms  音符 %d" %
              (name, len(c_after), dup_after, gap_after, len(notes)))
        if c_after:
            print("   !! 仍有冲突，示例：", [(round(a), round(b), round(c), d) for a, b, c, d in c_after[:5]])

        data["notes"] = notes
        changed_semantically = [(round(n.get("time", 0.0), 4), int(n.get("lane", 0)),
                                 round(n.get("duration", 0.0), 4)) for n in notes] != \
                              [(round(n.get("time", 0.0), 4), int(n.get("lane", 0)),
                                round(n.get("duration", 0.0), 4)) for n in original_notes]

        if args.apply and changed_semantically:
            if write_chart(path, data, notes):
                written.append(name)
            else:
                with open(path, "w", encoding="utf-8") as fh:
                    json.dump(data, fh, ensure_ascii=False, indent=2)
                    fh.write("\n")
                written.append(name + "（纯数组，整体重写）")
        elif args.apply:
            untouched.append(name)

    print("\n合计：修复前 %d 处冲突 → 修复后 %d 处" % (total_before, total_after))
    if written:
        print("已写回：%d 个谱面" % len(written))
        for name in written:
            print("  · " + name)
    if untouched:
        print("未改动（内容无变化，不写文件）：%d 个" % len(untouched))
    if args.apply and (args.repair or args.patterns != "none"):
        print("已写回文件。")
    elif args.repair or args.patterns != "none":
        print("（预览模式，未写回；加 --apply 才会改文件）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
