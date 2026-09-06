#!/usr/bin/env python3
"""ModularMech の C# ソースに対する静的サニティチェック。

この環境には .NET も Unity も無く、コンパイルによる検証ができない。
その穴を埋めるための最低限の機械的チェックであり、コンパイラの代わりではない。
検出できるのは「括弧の不整合」「ファイル名と型名の不一致」「名前空間の規約違反」
「Unity で禁止した API の混入」程度であることに注意する。

使い方: python3 Tools/check_sources.py [--root ModularMech]
"""

from __future__ import annotations

import argparse
import os
import re
import sys

# ディレクトリ -> 期待する名前空間 (ModularMech/CLAUDE.md の表と一致させること)
NAMESPACE_RULES = {
    "Assets/Scripts/Data": "ModularMech.Data",
    "Assets/Scripts/Loadout": "ModularMech.Loadouts",
    "Assets/Scripts/Serialization": "ModularMech.Serialization",
    "Assets/Scripts/Assembly": "ModularMech.Assembling",
    "Assets/Scripts/Runtime": "ModularMech.Mechs",
    "Assets/Scripts/UI": "ModularMech.UI",
    "Assets/Scripts/Editor": "ModularMech.EditorTools",
    "Assets/Tests/EditMode": "ModularMech.Tests",
}

# 規約で禁止した API。value は理由。
BANNED = [
    (re.compile(r"\.HasFlag\s*\("), "CapabilityFlags.HasFlag はボックス化する。Has() 拡張メソッドを使う"),
    (re.compile(r"JsonUtility\s*\."), "JsonUtility は Dictionary を扱えない。MiniJson を使う"),
    (re.compile(r"\bNewtonsoft\b"), "外部 JSON ライブラリに依存しない"),
]

TYPE_DECL = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*(?:public|internal)\s+"
    r"(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+|unsafe\s+)*"
    r"(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)",
    re.MULTILINE,
)
NAMESPACE_DECL = re.compile(r"^\s*namespace\s+([A-Za-z0-9_.]+)", re.MULTILINE)


def strip_noise(text: str) -> str:
    """文字列リテラル・文字リテラル・コメントを空白に潰す。括弧数え用。"""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            while i < n and text[i] != "\n":
                i += 1
        elif c == "/" and nxt == "*":
            i += 2
            while i < n - 1 and not (text[i] == "*" and text[i + 1] == "/"):
                i += 1
            i += 2
        elif c == '"' and text[i - 1: i] == "@":
            i += 1  # 逐語的文字列 @"..." ("" が唯一のエスケープ)
            while i < n:
                if text[i] == '"':
                    if text[i + 1: i + 2] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
        elif c in ('"', "'"):
            quote = c
            i += 1
            while i < n:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == quote:
                    i += 1
                    break
                i += 1
        else:
            out.append(c)
            i += 1
    return "".join(out)


def check_file(path: str, root: str, errors: list, warnings: list) -> None:
    with open(path, encoding="utf-8") as fh:
        text = fh.read()
    rel = os.path.relpath(path, root).replace(os.sep, "/")
    code = strip_noise(text)

    for open_ch, close_ch, label in (("{", "}", "波括弧"), ("(", ")", "丸括弧"), ("[", "]", "角括弧")):
        diff = code.count(open_ch) - code.count(close_ch)
        if diff != 0:
            errors.append(f"{rel}: {label}の対応が {diff:+d} ずれている")

    stem = os.path.splitext(os.path.basename(path))[0]
    decls = TYPE_DECL.findall(code)
    if not decls:
        warnings.append(f"{rel}: public/internal な型宣言が見つからない")
    elif stem not in [name for _, name in decls]:
        errors.append(
            f"{rel}: ファイル名と型名が一致しない (宣言: {', '.join(n for _, n in decls)})"
        )

    ns_match = NAMESPACE_DECL.search(code)
    dir_rel = os.path.dirname(rel)
    expected = None
    for prefix, ns in NAMESPACE_RULES.items():
        if dir_rel == prefix or dir_rel.startswith(prefix + "/"):
            expected = ns
            break
    if ns_match is None:
        errors.append(f"{rel}: namespace が無い")
    elif expected and not (ns_match.group(1) == expected or ns_match.group(1).startswith(expected + ".")):
        errors.append(
            f"{rel}: namespace が {ns_match.group(1)} だが規約では {expected} (またはその配下)"
        )

    for pattern, reason in BANNED:
        for lineno, line in enumerate(text.splitlines(), start=1):
            if line.lstrip().startswith("//"):
                continue
            if pattern.search(line):
                errors.append(f"{rel}:{lineno}: 禁止 API — {reason}")

    if "\t" in text:
        warnings.append(f"{rel}: タブ文字が含まれる (スペース4つに統一)")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default="ModularMech")
    args = parser.parse_args()
    root = os.path.abspath(args.root)

    errors: list = []
    warnings: list = []
    count = 0
    for dirpath, _dirnames, filenames in os.walk(root):
        for name in sorted(filenames):
            if not name.endswith(".cs"):
                continue
            count += 1
            check_file(os.path.join(dirpath, name), root, errors, warnings)

    if count == 0:
        print(f"対象 .cs ファイルが見つからない: {root}")
        return 1

    for w in warnings:
        print(f"WARN  {w}")
    for e in errors:
        print(f"ERROR {e}")
    print(f"\n{count} ファイル検査 / エラー {len(errors)} 件 / 警告 {len(warnings)} 件")
    print("注意: これはコンパイルの代わりにはならない。型の整合は Unity 上で確認すること。")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
