#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
离线编译检查（不需要打开 Unity 编辑器）
=====================================

直接用 Unity 自带的 Roslyn 编译器 + Unity 自带引用程序集，把 Assets 下所有 .cs 编一遍。
几秒钟就能拿到精确的错误列表，比"打开 Unity 等它转圈"快得多。

两个 pass：
  pass 1  只编运行时脚本（不含 Assets/Editor），并且不定义 UNITY_EDITOR
          —— 用来发现"运行时代码误用了编辑器才有的 API"这种打包后才会炸的坑
  pass 2  编全部脚本并定义 UNITY_EDITOR
          —— 用来检查编辑器工具脚本本身

uGUI 的 UnityEngine.UI.dll 不是 Unity 安装自带的（属于 com.unity.ugui 包），
默认从相邻的 Rhythm4K 工程的 Library 里借用；也可以 --refs 指定目录。

用法：
  python tools/compile_check.py
  python tools/compile_check.py --defines UNITY_EDITOR
"""

from __future__ import annotations

import argparse
import os
import subprocess
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ASSETS = os.path.join(ROOT, "Assets")
TMP = os.path.join(ROOT, ".compile_tmp")

UNITY_DATA = r"C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Data"
# 借用隔壁工程已经导入好的 uGUI 程序集（同一个 Unity 版本，可以直接用）
DEFAULT_REFS = [
    os.path.join(os.path.dirname(ROOT), "Rhythm4K", "Library", "ScriptAssemblies"),
]

BASE_DEFINES = [
    "UNITY_STANDALONE_WIN",
    "UNITY_2022_3_OR_NEWER",
    "ENABLE_INPUT_SYSTEM",
    "ENABLE_LEGACY_INPUT_MANAGER",
]


def collect_sources(include_editor: bool) -> list[str]:
    sources = []
    for dirpath, _dirs, files in os.walk(ASSETS):
        rel = os.path.relpath(dirpath, ASSETS).replace("\\", "/")
        is_editor = rel == "Editor" or rel.startswith("Editor/") or "/Editor/" in f"/{rel}/"
        if is_editor and not include_editor:
            continue
        for name in files:
            if name.endswith(".cs"):
                sources.append(os.path.join(dirpath, name))
    sources.sort()
    return sources


def build_refs(extra_dirs: list[str]) -> list[str]:
    managed = os.path.join(UNITY_DATA, "Managed", "UnityEngine")
    refs = [os.path.join(managed, f) for f in os.listdir(managed) if f.endswith(".dll")]

    netstandard = os.path.join(UNITY_DATA, "NetStandard", "ref", "2.1.0", "netstandard.dll")
    if os.path.isfile(netstandard):
        refs.append(netstandard)

    for folder in extra_dirs:
        if not os.path.isdir(folder):
            continue
        for name in os.listdir(folder):
            if not name.endswith(".dll"):
                continue
            # 排除我们自己工程/隔壁工程的业务程序集，避免类型重复
            if name.startswith("Assembly-CSharp"):
                continue
            refs.append(os.path.join(folder, name))

    return sorted(set(refs))


def run_pass(tag: str, sources: list[str], refs: list[str], defines: list[str]) -> tuple[int, list[str]]:
    os.makedirs(TMP, exist_ok=True)
    rsp = os.path.join(TMP, f"csc_{tag}.rsp")
    dll = os.path.join(TMP, f"My4KGame_{tag}.dll")

    csc = os.path.join(UNITY_DATA, "DotNetSdkRoslyn", "csc.dll")
    dotnet = os.path.join(UNITY_DATA, "NetCoreRuntime", "dotnet.exe")

    lines = ["/nologo", "/target:library", f'/out:"{dll}"', "/langversion:9.0",
             "/nostdlib+", "/define:" + ";".join(defines)]
    lines += [f'/r:"{r}"' for r in refs]
    lines += [f'"{s}"' for s in sources]
    with open(rsp, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines))

    proc = subprocess.run([dotnet, csc, "@" + rsp], capture_output=True, text=False)
    out = (proc.stdout or b"").decode("utf-8", "replace") + (proc.stderr or b"").decode("utf-8", "replace")
    return proc.returncode, [l.strip() for l in out.splitlines() if l.strip()]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--refs", nargs="*", default=DEFAULT_REFS, help="额外的引用程序集目录")
    args = ap.parse_args()

    csc = os.path.join(UNITY_DATA, "DotNetSdkRoslyn", "csc.dll")
    if not os.path.isfile(csc):
        print(f"[环境不满足] 找不到 Roslyn：{csc}")
        return 2

    refs = build_refs(args.refs)
    has_ui = any("UnityEngine.UI.dll" in r for r in refs)
    print("=" * 72)
    print(f" My4KGame 离线编译检查   引用 {len(refs)} 个程序集   uGUI={'有' if has_ui else '缺(会报 CS0246)'}")
    print("=" * 72)

    failures = 0
    for tag, include_editor, extra_defines in (
        ("runtime", False, BASE_DEFINES),
        ("editor", True, BASE_DEFINES + ["UNITY_EDITOR", "UNITY_EDITOR_WIN"]),
    ):
        sources = collect_sources(include_editor)
        code, lines = run_pass(tag, sources, refs, extra_defines)
        errors = [l for l in lines if ": error " in l]
        warnings = [l for l in lines if ": warning " in l]

        label = "运行时脚本" if tag == "runtime" else "全部脚本（含编辑器）"
        print(f"\n--- {label}：{len(sources)} 个文件 ---")
        for line in lines:
            print("  " + line)
        if code == 0:
            print(f"  ✓ 通过（{len(warnings)} 个警告）")
        else:
            failures += 1
            print(f"  ✗ 失败：{len(errors)} 个错误 / {len(warnings)} 个警告")

    print("\n" + "=" * 72)
    if failures == 0:
        print(" ✓ 两个 pass 都编译通过")
    else:
        print(f" ✗ 有 {failures} 个 pass 未通过")
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
