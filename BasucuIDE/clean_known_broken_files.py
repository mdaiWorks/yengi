#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
mdaiAgent / BasucuIDE projesinde SADECE bozuk oldugu bilinen dosyalari bulup
fazla bos satirlari tamamen temizler (max-blank = 0).

Hedef klasor: E:\\andoridGames\\mdaiAgent\\BasucuIDE  (asagida ROOT ile degistirilebilir)

Hedef dosyalar (klasor derinligine bakmaksizin, dosya adina gore bulunur):
    MainWindow.xaml.cs
    MainWindow.ChatPanel.cs
    ChatFlowService.cs
    PlanWindow.xaml.cs
    SettingsWindow.xaml.cs
    ToolExecutor.cs
    App.xaml.cs

Bu listenin DISINDAKI hicbir dosyaya dokunulmaz.

GUVENLIK:
- Her dosya degistirilmeden once yaninda ".bak" uzantili bir yedegi olusturulur
  (orn. MainWindow.xaml.cs.bak). Bir sorun olursa o dosyayi geri kopyalayabilirsin.
- Varsayilan calisma modu DRY-RUN'dir; hicbir dosyaya yazmaz, sadece rapor verir.
  Gercekten uygulamak icin --apply parametresi ile calistir.
- Dosya kodlamasi (UTF-8 / UTF-8 BOM / UTF-16 LE/BE) ve CRLF/LF satir sonu bicimi
  OLDUGU GIBI korunur.

Kullanim (Windows'ta, proje klasorunde bir terminal acip):
    python clean_known_broken_files.py                 # dry-run, sadece rapor
    python clean_known_broken_files.py --apply          # gercekten temizle (yedekli)
    python clean_known_broken_files.py --root "D:\\baska\\yol" --apply
"""
import argparse
from pathlib import Path

ROOT_DEFAULT = r"E:\andoridGames\mdaiAgent\BasucuIDE"

TARGET_FILENAMES = {
    "MainWindow.xaml.cs",
    "MainWindow.ChatPanel.cs",
    "ChatFlowService.cs",
    "PlanWindow.xaml.cs",
    "SettingsWindow.xaml.cs",
    "ToolExecutor.cs",
    "App.xaml.cs",
}

EXCLUDED_DIRS = {".git", "bin", "obj", ".vs", "node_modules", "packages", "dist", "build"}


def detect_encoding(raw: bytes):
    if raw.startswith(b"\xff\xfe"):
        return "utf-16-le"
    if raw.startswith(b"\xfe\xff"):
        return "utf-16-be"
    if raw.startswith(b"\xef\xbb\xbf"):
        return "utf-8-sig"
    try:
        raw.decode("utf-8")
        return "utf-8"
    except UnicodeDecodeError:
        return None


def collapse_blank_lines(text: str, max_blank: int = 0):
    uses_crlf = text.count("\r\n") > 0
    normalized = text.replace("\r\n", "\n")
    lines = normalized.split("\n")

    out_lines = []
    blank_run = 0
    removed = 0
    for line in lines:
        if line.strip() == "":
            blank_run += 1
            if blank_run <= max_blank:
                out_lines.append("")
            else:
                removed += 1
        else:
            blank_run = 0
            out_lines.append(line)

    result = "\n".join(out_lines)
    if uses_crlf:
        result = result.replace("\n", "\r\n")
    return result, removed


def find_target_files(root: Path):
    found = []
    for path in root.rglob("*"):
        if not path.is_file():
            continue
        if any(part in EXCLUDED_DIRS for part in path.parts):
            continue
        if path.name in TARGET_FILENAMES:
            found.append(path)
    return sorted(found)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=ROOT_DEFAULT, help=f"Proje kok klasoru (varsayilan: {ROOT_DEFAULT})")
    ap.add_argument("--apply", action="store_true", help="Bu bayrak verilmezse hicbir dosya degismez, sadece raporlanir")
    ap.add_argument("--max-blank", type=int, default=0, help="Ardisik bos satirdan en fazla kac tanesi kalsin (varsayilan 0)")
    ap.add_argument("--no-backup", action="store_true", help="'.bak' yedegi olusturma (onerilmez)")
    args = ap.parse_args()

    root = Path(args.root)
    if not root.exists():
        print(f"HATA: Klasor bulunamadi: {root}")
        return

    targets = find_target_files(root)
    if not targets:
        print(f"'{root}' altinda hedef dosyalardan hicbiri bulunamadi.")
        print("Aranan dosya adlari:", ", ".join(sorted(TARGET_FILENAMES)))
        return

    print(f"Kok klasor: {root}")
    print(f"Bulunan hedef dosya sayisi: {len(targets)}\n")

    total_removed = 0
    for path in targets:
        raw = path.read_bytes()
        encoding = detect_encoding(raw)
        if encoding is None:
            print(f"[ATLANDI - encoding cozulemedi] {path}")
            continue

        text = raw.decode(encoding)
        fixed, removed = collapse_blank_lines(text, args.max_blank)

        if removed == 0:
            print(f"[degisiklik yok] {path}")
            continue

        total_removed += removed
        mode = "UYGULANDI" if args.apply else "dry-run"
        print(f"[{mode}] {path} : {removed} bos satir kaldirilacak/kaldirildi")

        if args.apply:
            if not args.no_backup:
                backup_path = path.with_suffix(path.suffix + ".bak")
                backup_path.write_bytes(raw)
            path.write_bytes(fixed.encode(encoding))

    print("\n--- OZET ---")
    print(f"Toplam kaldirilan bos satir: {total_removed}")
    if not args.apply:
        print("\nBu bir DRY-RUN idi, hicbir dosya degismedi.")
        print("Gercekten uygulamak icin: python clean_known_broken_files.py --apply")
    else:
        print("Islem tamamlandi. Her degisen dosyanin yaninda '.bak' uzantili orijinal yedegi var.")


if __name__ == "__main__":
    main()
