#!/usr/bin/env python3
"""从原版文案基线生成 C# 文案表 · Generate core/I18n.Table.cs from src/toolbox/i18n.py

用法（在项目根目录）：
    python gen-i18n-table.py

为什么要有这个脚本：
    C# 侧 `core/I18n.Table.cs` 里的 200+ 条中英文案是**逐条移植**自原版 `src/toolbox/i18n.py`
    的 TEXTS 表。手抄 400+ 段文案不可能不出错，所以一律**由脚本生成**；
    生成结果再由 `tests/I18nTests.cs` 的保真测试（用真实 Python 解析 i18n.py 逐条比对）长期钉住。

加新文案的正确姿势：
    ① 在 `src/toolbox/i18n.py` 的 TEXTS 里加 key（原 Key 不要改名、文案不要动）；
    ② 跑本脚本重新生成 `core/I18n.Table.cs`；
    ③ `dotnet test`（保真测试会证明两边一致）。

⚠️ 脚本只读 i18n.py、只写 core/I18n.Table.cs，不碰任何用户数据。
"""

from __future__ import annotations

import ast
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / "src" / "toolbox" / "i18n.py"
TARGET = ROOT / "core" / "I18n.Table.cs"

HEADER = """// I18n.Table.cs —— 文案表（中/英），**逐条生成自原版 `src/toolbox/i18n.py` 的 TEXTS**
//
// ⚠️ 本文件由 `gen-i18n-table.py` 生成，**不要手改**：
//    加/改文案请改 `src/toolbox/i18n.py` 后重跑 `python gen-i18n-table.py`。
//
// ⚠️ 这张表是**保真契约**：key 命名与中英文案必须与原版逐字一致 ——
//    `tests/I18nTests.cs` 里有一条测试会**用真实 Python 解析 i18n.py** 并与本表逐条比对
//    （含多行文案与 `{name}` 这类命名占位符）。
//
// 表里的 key 分两段：前段是原版 v1.11.6/v2.0.1 就有的（key 名照旧）；
// 后段是 "WinUI 3 线新增（2026-09-18，v2.0.3 i18n）" 那些条目。

namespace ToolboxPanel.Core;

public static partial class I18n
{
    /// <summary>全部文案：key → (中文, 英文)。共 {count} 条。</summary>
    internal static readonly Dictionary<string, (string Zh, string En)> Table = new(StringComparer.Ordinal)
    {
"""


def load_texts() -> dict[str, dict[str, str]]:
    text = SOURCE.read_text(encoding="utf-8")
    # ⚠️ 必须定位**定义处**：文件前部 tr() 里还有 `TEXTS.get(key)` 与 f-string 的 `{key}`，
    #    只搜 "TEXTS" 会把 f-string 的大括号当成表起点（第一次就踩了）。
    start = text.index("TEXTS: dict")
    brace = text.index("{", text.index("=", start))

    depth = 0
    end = -1
    for index in range(brace, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                end = index + 1
                break

    if end < 0:
        raise SystemExit("解析失败：TEXTS 字典没有配平的大括号")

    return ast.literal_eval(text[brace:end])


def cs_literal(value: str) -> str:
    out = []
    for ch in value:
        if ch == "\\":
            out.append("\\\\")
        elif ch == '"':
            out.append('\\"')
        elif ch == "\n":
            out.append("\\n")
        elif ch == "\r":
            out.append("\\r")
        elif ch == "\t":
            out.append("\\t")
        elif ord(ch) < 0x20:
            out.append(f"\\u{ord(ch):04x}")
        else:
            out.append(ch)
    return '"' + "".join(out) + '"'


def main() -> int:
    if not SOURCE.exists():
        raise SystemExit(f"找不到原版文案基线：{SOURCE}")

    texts = load_texts()
    width = max(len(key) for key in texts) + 2

    lines = [HEADER.replace("{count}", str(len(texts)))]
    for key, entry in texts.items():
        lines.append(
            f"        [{cs_literal(key)}]".ljust(width + 12)
            + f"= ({cs_literal(entry['zh'])}, {cs_literal(entry['en'])}),"
        )
    lines += ["    };", "}", ""]

    TARGET.write_text("\n".join(lines), encoding="utf-8")
    print(f"已生成 {TARGET.relative_to(ROOT)}：{len(texts)} 条文案")
    return 0


if __name__ == "__main__":
    sys.exit(main())
