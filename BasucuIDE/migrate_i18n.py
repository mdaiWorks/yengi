from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
RESOURCE_DIR = ROOT / "Resources"
TARGET_PROPERTIES = {"Content", "Text", "Header", "ToolTip", "Title"}
EXCLUDED_DIRS = {"bin", "obj", "publish", ".git", ".vs"}
ATTRIBUTE_RE = re.compile(r'(?P<name>Content|Text|Header|ToolTip|Title)="(?P<value>[^"]*)"')
ROOT_TAG_RE = re.compile(r"<(?P<tag>[A-Za-z_][\w.]*)\b(?P<attrs>[^>]*)>", re.DOTALL)
CS_STRING_RE = re.compile(r'(?P<prefix>\$?)"(?P<value>(?:\\.|[^"\\])*)"')
CS_INTERPOLATED_RE = re.compile(r'\$"(?P<value>(?:\\.|[^"\\])*)"')
INTERPOLATION_RE = re.compile(r"\{(?P<expression>[A-Za-z_][\w.]*)\}")
CS_UI_MARKERS = ("MessageBox.Show", ".Text =", ".Content =", ".Title =", "Text =", "Content =", "Title =")
COMMON_CSHARP_UI_KEYS = {"Hata": "Hata", "Error": "Hata", "Bilgi": "IlgiliBilgi", "Information": "IlgiliBilgi"}


def read_resource(path: Path) -> dict[str, str]:
    tree = ET.parse(path)
    return {
        item.attrib["name"]: (item.findtext("value") or "")
        for item in tree.getroot().findall("data")
        if "name" in item.attrib
    }


def is_technical(value: str) -> bool:
    text = value.strip()
    if not text or "{" in text or "}" in text:
        return True
    if text.startswith(("http://", "https://", "mailto:", "#", "\\", "/")):
        return True
    if re.fullmatch(r"[A-Za-z]:[\\/].*", text):
        return True
    if re.fullmatch(r"[\w.-]+@[\w.-]+", text):
        return True
    if re.fullmatch(r"[\W_]+", text):
        return True
    return False


def build_value_index(resources: list[dict[str, str]]) -> dict[str, list[str]]:
    index: dict[str, set[str]] = {}
    for resource in resources:
        for key, value in resource.items():
            normalized = value.strip()
            if normalized:
                index.setdefault(normalized, set()).add(key)
    return {value: sorted(keys) for value, keys in index.items()}


def xaml_files() -> list[Path]:
    return [
        path
        for path in ROOT.rglob("*.xaml")
        if not any(part in EXCLUDED_DIRS for part in path.parts)
    ]


def csharp_files() -> list[Path]:
    return [
        path
        for path in ROOT.rglob("*.cs")
        if not any(part in EXCLUDED_DIRS for part in path.parts)
    ]


def read_text_with_encoding(path: Path) -> tuple[str, str]:
    data = path.read_bytes()
    if data.startswith(b"\xff\xfe") or data.startswith(b"\xfe\xff"):
        return data.decode("utf-16"), "utf-16"
    if data.startswith(b"\xef\xbb\xbf"):
        return data.decode("utf-8-sig"), "utf-8-sig"
    return data.decode("utf-8"), "utf-8"


def localize_file(path: Path, value_index: dict[str, list[str]], apply: bool) -> tuple[int, int]:
    try:
        original, encoding = read_text_with_encoding(path)
    except UnicodeDecodeError:
        print(f"Skipped undecodable file: {path.relative_to(ROOT)}")
        return 0, 0
    changes: list[tuple[str, str, str]] = []

    def replace(match: re.Match[str]) -> str:
        property_name = match.group("name")
        value = match.group("value")
        if value.startswith("{") or is_technical(value):
            return match.group(0)
        keys = value_index.get(value.strip(), [])
        if not keys:
            return match.group(0)
        key = keys[0]
        replacement = f'{property_name}="{{local:Loc Key={key}}}"'
        if replacement != match.group(0):
            changes.append((property_name, value, key))
        return replacement

    updated = ATTRIBUTE_RE.sub(replace, original)
    if not changes:
        return 0, 0

    if "local:Loc" in updated and "xmlns:local=" not in updated.split(">", 1)[0]:
        updated = re.sub(
            r"(?P<start><(?:Window|Application|Page|UserControl)\b)(?P<attrs>[^>]*)>",
            lambda match: match.group("start") + match.group("attrs") + ' xmlns:local="clr-namespace:mdaiAgent">',
            updated,
            count=1,
            flags=re.DOTALL,
        )
    if apply:
        path.write_text(updated, encoding=encoding)

    for property_name, value, key in changes:
        relative = path.relative_to(ROOT)
        print(f"{relative}: {property_name}={value!r} -> {key}")
    return len(changes), 1 if apply else 0


def localize_csharp_file(path: Path, value_index: dict[str, list[str]], apply: bool) -> tuple[int, int]:
    try:
        original, encoding = read_text_with_encoding(path)
    except UnicodeDecodeError:
        print(f"Skipped undecodable file: {path.relative_to(ROOT)}")
        return 0, 0

    changes: list[tuple[str, str, str]] = []
    updated_lines: list[str] = []
    for line in original.splitlines(keepends=True):
        if not any(marker in line for marker in CS_UI_MARKERS):
            updated_lines.append(line)
            continue

        def replace(match: re.Match[str]) -> str:
            if match.group("prefix"):
                return match.group(0)
            if "GetString(" in line[max(0, match.start() - 40):match.start()]:
                return match.group(0)
            raw_value = match.group("value")
            try:
                value = json.loads(f'"{raw_value}"')
            except json.JSONDecodeError:
                return match.group(0)
            if is_technical(value):
                return match.group(0)
            keys = value_index.get(value.strip(), [])
            if not keys and value.strip() in COMMON_CSHARP_UI_KEYS:
                key = COMMON_CSHARP_UI_KEYS[value.strip()]
                changes.append(("C# UI title", value, key))
                return f'LocalizationManager.Instance.GetString("{key}")'
            if not keys:
                return match.group(0)
            key = keys[0]
            changes.append(("C# string", value, key))
            return f'LocalizationManager.Instance.GetString("{key}")'

        def replace_interpolated(match: re.Match[str]) -> str:
            if "GetString(" in line[max(0, match.start() - 40):match.start()]:
                return match.group(0)
            raw_value = match.group("value")
            try:
                value = json.loads(f'"{raw_value}"')
            except json.JSONDecodeError:
                return match.group(0)
            keys = value_index.get(value.strip(), [])
            expressions = INTERPOLATION_RE.findall(value)
            if not keys or not expressions or any("{" + expression + "}" not in value for expression in expressions):
                return match.group(0)
            localized = f'LocalizationManager.Instance.GetString("{keys[0]}")'
            for expression in expressions:
                localized += f'.Replace("{{{expression}}}", {expression}.ToString())'
            changes.append(("C# interpolated string", value, keys[0]))
            return localized

        updated_line = CS_INTERPOLATED_RE.sub(replace_interpolated, line)
        updated_lines.append(CS_STRING_RE.sub(replace, updated_line))

    if not changes:
        return 0, 0
    updated = "".join(updated_lines)
    if apply:
        path.write_text(updated, encoding=encoding)
    for property_name, value, key in changes:
        print(f"{path.relative_to(ROOT)}: {property_name}={value!r} -> {key}")
    return len(changes), 1 if apply else 0


def validate(resources: list[dict[str, str]]) -> int:
    first, second = resources
    missing_second = sorted(set(first) - set(second))
    missing_first = sorted(set(second) - set(first))
    if missing_second or missing_first:
        print(f"Missing English keys: {len(missing_second)}")
        print(f"Missing Turkish keys: {len(missing_first)}")
        return 1
    print(f"Resource keys valid: {len(first)}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Safe XAML localization migration for mdaiAgent")
    parser.add_argument("--scan", action="store_true", help="report deterministic XAML candidates")
    parser.add_argument("--dry-run", action="store_true", help="report changes without writing files")
    parser.add_argument("--migrate", action="store_true", help="apply deterministic XAML changes")
    parser.add_argument("--validate", action="store_true", help="validate resource key parity")
    parser.add_argument("--xaml-only", action="store_true", help="scan or migrate XAML only")
    parser.add_argument("--csharp-only", action="store_true", help="scan or migrate C# only")
    args = parser.parse_args()

    if not (args.scan or args.dry_run or args.migrate or args.validate):
        parser.error("choose --scan, --dry-run, --migrate, or --validate")
    if args.xaml_only and args.csharp_only:
        parser.error("--xaml-only and --csharp-only cannot be combined")

    turkish = read_resource(RESOURCE_DIR / "Strings.resx")
    english = read_resource(RESOURCE_DIR / "Strings.en.resx")
    if args.validate:
        return validate([turkish, english])

    value_index = build_value_index([turkish, english])
    total = 0
    files = 0
    apply = args.migrate
    if not args.csharp_only:
        for path in xaml_files():
            changed, written = localize_file(path, value_index, apply)
            total += changed
            files += written
    csharp_total = 0
    csharp_files_updated = 0
    if not args.xaml_only:
        for path in csharp_files():
            changed, written = localize_csharp_file(path, value_index, apply)
            csharp_total += changed
            csharp_files_updated += written
    print(f"XAML candidates: {total}; files {'updated' if apply else 'to update'}: {files}")
    print(f"C# candidates: {csharp_total}; files {'updated' if apply else 'to update'}: {csharp_files_updated}")
    if args.scan:
        print("Scan completed without changing files.")
    if args.dry_run:
        print("Dry run completed without changing files.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
