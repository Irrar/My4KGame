#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""谱面体检：hold 期间同轨道是否还有别的音符、hold 是否互相重叠、密度与空档分布。"""
import glob
import json
import os
import sys

ROOT = r"D:\My4KGame\My4KGame\Assets\Resources\Charts"


def load(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def main():
    rows = []
    total_conflicts = 0
    for path in sorted(glob.glob(os.path.join(ROOT, "*.json"))):
        d = load(path)
        name = os.path.basename(path)
        if isinstance(d, list):          # 手册式纯数组谱面
            d = {"notes": d}
        notes = d.get("notes", [])
        bpm = d.get("bpm", 120) or 120
        offset_ms = (d.get("offset", 0) or 0) * 1000.0

        def ms(n):
            if n.get("beat", -1) >= 0:
                return n["beat"] * 60000.0 / bpm + offset_ms
            return n.get("time", 0) * 1000.0 + offset_ms

        def dur(n):
            if n.get("durationBeat", -1) >= 0:
                return n["durationBeat"] * 60000.0 / bpm
            return n.get("duration", 0) * 1000.0

        ents = sorted((ms(n), ms(n) + dur(n), n.get("lane", 0), dur(n) > 1.0) for n in notes)
        holds = [e for e in ents if e[3]]

        conflicts = []
        for i, (s1, e1, l1, h1) in enumerate(ents):
            if not h1:
                continue
            for s2, e2, l2, h2 in ents[i + 1:]:
                if s2 > e1 + 1.0:
                    break
                if l2 == l1:
                    conflicts.append((s1, e1, s2, "hold" if h2 else "tap"))

        total_conflicts += len(conflicts)
        key = "beat" if notes and notes[0].get("beat", -1) >= 0 else "time"
        rows.append((name, len(notes), len(holds), len(conflicts), bpm, key, conflicts))

    print("%-40s %6s %6s %10s %6s %6s" % ("chart", "notes", "holds", "conflicts", "bpm", "key"))
    print("-" * 80)
    for name, n, h, c, bpm, key, _ in rows:
        print("%-40s %6d %6d %10d %6g %6s" % (name, n, h, c, bpm, key))

    print()
    print("=== 冲突明细 ===")
    for name, n, h, c, bpm, key, conflicts in rows:
        if not conflicts:
            continue
        print("\n%s  (%d 处)" % (name, len(conflicts)))
        for s1, e1, s2, kind in conflicts[:12]:
            print("   hold %.0f→%.0fms  期间同轨 %s @ %.0fms" % (s1, e1, kind, s2))
        if len(conflicts) > 12:
            print("   ... 其余 %d 处省略" % (len(conflicts) - 12))

    print("\n合计冲突：%d" % total_conflicts)
    return 0


if __name__ == "__main__":
    sys.exit(main())
