#!/usr/bin/env python3
"""
mdaiAgent Türkçe String Çıkarıcı & Çeviri Yardımcısı
---------------------------------------------------
Bu script proje içindeki tüm .cs ve .xaml dosyalarını tarar,
Türkçe karakter içeren string literal'ları bulur ve:
  1. Bunları benzersiz (unique) olarak listeler
  2. Önerilen .resx anahtarı üretir (PascalCase)
  3. İsteğe bağlı olarak DeepL / Google Translate ile çeviri yapar
  4. Çıktıyı CSV veya .resx XML şablonu olarak kaydeder

Kullanım:
  python extract_strings.py --help
  python extract_strings.py
  python extract_strings.py --output csv --out strings_found.csv
  python extract_strings.py --output resx --tr-resx out_tr.resx --en-resx out_en.resx
  python extract_strings.py --translate --deepl-key YOUR_KEY  # opsiyonel
"""

from __future__ import annotations

import argparse
import csv
import re
import sys
import textwrap
import urllib.parse
import urllib.request
import json
import concurrent.futures
from pathlib import Path
from typing import Dict, List, Tuple, Optional

PROJECT_ROOT = Path(__file__).resolve().parent
TURKISH_CHARS = set("çğıöşüÇĞİÖŞÜâîûÂÎÛ")

# --- Regex'ler ---
# C# string literal: "..."  veya  @"..."  (verbatim)
CS_STRING_RE = re.compile(
    r'(?<!\\)"((?:[^"\\\r\n]|\\.)*)"'       # normal "..."
    r'|@"([^"]*(?:""[^"]*)*)"',              # verbatim @"..."
    re.UNICODE,
)

# XAML text özellikleri: Text="..."  Content="..."  Title="..."  vb
XAML_ATTR_RE = re.compile(
    r'\b(Text|Content|Title|Header|ToolTip|PlaceholderText|Description|Label'
    r'|Hint|Subtitle|Name|DisplayName|Placeholder)\s*=\s*"([^"]+)"',
    re.UNICODE,
)

# Localization.Get("tr", "en") çağrıları
LOC_GET_RE = re.compile(
    r'Localization\.Get\(\s*"((?:[^"\\]|\\.)*)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\)',
    re.UNICODE,
)

def is_turkish(text: str) -> bool:
    return any(ch in TURKISH_CHARS for ch in text)

def clean_text(text: str) -> str:
    text = text.replace("\\\"", '"').replace("\\n", " ").replace("\\t", " ")
    text = re.sub(r'\s+', ' ', text).strip()
    return text

def pascal_case(text: str) -> str:
    """Türkçe bir metinden geçerli bir C#/PascalCase tanımlayıcı üretir."""
    replacements = {
        "ç": "c", "ğ": "g", "ı": "i", "ö": "o", "ş": "s", "ü": "u",
        "Ç": "C", "Ğ": "G", "İ": "I", "Ö": "O", "Ş": "S", "Ü": "U",
        "â": "a", "î": "i", "û": "u", "Â": "A", "Î": "I", "Û": "U",
    }
    for k, v in replacements.items():
        text = text.replace(k, v)
    words = re.findall(r"[A-Za-z0-9]+", text)
    if not words:
        return "UnknownKey"
    key = "".join(w[0].upper() + w[1:] if w else "" for w in words)
    # tanımlayıcı başında rakam varsa önek ekle
    if key and key[0].isdigit():
        key = "_" + key
    if not key:
        key = "UnknownKey"
    return key[:120]

def make_unique_key(base_key: str, used: Dict[str, int]) -> str:
    if base_key not in used:
        used[base_key] = 1
        return base_key
    used[base_key] += 1
    return f"{base_key}{used[base_key]}"

def find_source_files() -> List[Path]:
    files: List[Path] = []
    for pattern in ("*.cs", "*.xaml"):
        files.extend(PROJECT_ROOT.rglob(pattern))
    skip_dirs = ("bin", "obj", ".git")
    files = [
        f for f in files
        if not any(part in skip_dirs for part in f.parts)
    ]
    return sorted(files)

# --- Tarama fonksiyonları ---
def scan_cs_file(path: Path) -> List[Tuple[str, str, int]]:
    """(snippet, temizlenmiş Türkçe metin, satır no) listesi döner."""
    items: List[Tuple[str, str, int]] = []
    try:
        text = path.read_text(encoding="utf-8", errors="ignore")
    except Exception:
        return items
    lines = text.splitlines()
    for lineno, line in enumerate(lines, 1):
        for m in CS_STRING_RE.finditer(line):
            snippet = m.group(1) if m.group(1) is not None else (m.group(2) or "").replace('""', '"')
            cleaned = clean_text(snippet)
            if cleaned and is_turkish(cleaned):
                items.append((snippet, cleaned, lineno))
    return items

def scan_xaml_file(path: Path) -> List[Tuple[str, str, int]]:
    items: List[Tuple[str, str, int]] = []
    try:
        text = path.read_text(encoding="utf-8", errors="ignore")
    except Exception:
        return items
    lines = text.splitlines()
    for lineno, line in enumerate(lines, 1):
        for attr, value in XAML_ATTR_RE.findall(line):
            cleaned = clean_text(value)
            if cleaned and is_turkish(cleaned):
                items.append((f'{attr}="{value}"', cleaned, lineno))
    return items

def scan_loc_get(path: Path) -> List[Tuple[str, str, int]]:
    """Localization.Get(tr, en) çağrılarını bulur: (tr, en, lineno)"""
    items: List[Tuple[str, str, int]] = []
    if path.suffix.lower() != ".cs":
        return items
    try:
        text = path.read_text(encoding="utf-8", errors="ignore")
    except Exception:
        return items
    lines = text.splitlines()
    for lineno, line in enumerate(lines, 1):
        for tr, en in LOC_GET_RE.findall(line):
            items.append((clean_text(tr), clean_text(en), lineno))
    return items

# --- Çeviri (Opsiyonel) ---
def translate_deepl(texts: List[str], target_lang: str, api_key: str) -> Dict[str, str]:
    """Basit DeepL çevirisi. (Ücretsiz katman için api-free.deepl.com)"""
    if not texts:
        return {}
    url = "https://api-free.deepl.com/v2/translate"
    if ":" not in api_key and len(api_key) > 20:  # pro key
        url = "https://api.deepl.com/v2/translate"
    data = urllib.parse.urlencode(
        [("target_lang", target_lang.upper())] +
        [("text", t) for t in texts]
    ).encode("utf-8")
    req = urllib.request.Request(
        url, data=data,
        headers={"Authorization": f"DeepL-Auth-Key {api_key}"},
    )
    with urllib.request.urlopen(req, timeout=60) as resp:
        result = json.loads(resp.read().decode("utf-8"))
    out: Dict[str, str] = {}
    for orig, t in zip(texts, result.get("translations", [])):
        out[orig] = t.get("text", orig)
    return out

def translate_google_free(texts: List[str], target_lang: str) -> Dict[str, str]:
    """Google Translate'in ücretsiz (ancak kısıtlı) endpointini kullanır.
       Büyük hacimde/çok sık kullanım banlanma riski taşır. Dikkatli kullanın."""
    out: Dict[str, str] = {}
    for text in texts:
        try:
            url = (
                "https://translate.googleapis.com/translate_a/single"
                "?client=gtx&sl=tr&tl={tl}&dt=t&q={q}"
            ).format(tl=urllib.parse.quote(target_lang), q=urllib.parse.quote(text))
            req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
            with urllib.request.urlopen(req, timeout=30) as resp:
                data = json.loads(resp.read().decode("utf-8"))
            translated = "".join(part[0] for part in data[0] if part and part[0])
            out[text] = translated or text
        except Exception as e:
            print(f"  [çeviri hatası] {text[:40]!r}: {e}", file=sys.stderr)
            out[text] = text
    return out

# --- Yerel LLM / OpenAI Uyumlu API (Ollama v.b.) ---
def _chunk_list(items: List[str], n: int) -> List[List[str]]:
    return [items[i:i + n] for i in range(0, len(items), n)]

def translate_openai_compatible(
    texts: List[str],
    target_lang: str,
    *,
    base_url: str,
    model: str,
    api_key: str = "ollama",
    temperature: float = 0.15,
    batch_size: int = 40,
    timeout: int = 300,
    parallel: int = 1,
) -> Dict[str, str]:
    """
    OpenAI Chat Completions uyumlu herhangi bir endpoint (Ollama, LM Studio, OpenRouter,
    yerel sunucu v.b.) üzerinden Türkçe -> İngilizce çeviri yapar.

    Qwen gibi çok dilli modellerde çok iyi sonuç verir.
    Çıktıyı N lines = N input olarak döndürmesini sağlar.

    parallel > 1 verilirse ThreadPoolExecutor ile aynı anda N batch gönderir
    (toplam istek süresini ~parallel oranında azaltır).
    """
    out: Dict[str, str] = {t: t for t in texts}
    if not texts:
        return out

    endpoint = base_url.rstrip("/") + "/chat/completions"

    sys_prompt = (
        "You are a professional software localization translator. "
        "Translate Turkish UI strings to natural, concise, idiomatic American English. "
        "Rules:\n"
        "  - Keep technical terms (API, .resx, WPF, RAG, LLM, JSON, CSV, XML) unchanged.\n"
        "  - Preserve emoji, placeholders such as {0} and @mentions, punctuation, and trailing punctuation.\n"
        "  - Keep code identifiers and file names exactly as they appear.\n"
        "  - Do not add explanations, notes, or surrounding quotes.\n"
        "  - Output ONE translated sentence per line. Exactly N lines for N inputs. No extra lines.\n"
    )

    chunks = _chunk_list(texts, batch_size)

    def _process_chunk(args):
        ci, chunk = args
        numbered = "\n".join(f"{i+1}. {t}" for i, t in enumerate(chunk))
        user_prompt = (
            f"Translate each of the following {len(chunk)} Turkish UI strings into English. "
            f"Return exactly {len(chunk)} lines, each line being the translation of the corresponding line above. "
            "Do not include the line numbers in your output.\n\n"
            + numbered
        )

        payload = {
            "model": model,
            "temperature": temperature,
            "messages": [
                {"role": "system", "content": sys_prompt},
                {"role": "user", "content": user_prompt},
            ],
            "stream": False,
        }

        data_bytes = json.dumps(payload).encode("utf-8")
        headers = {
            "Content-Type": "application/json",
            "Authorization": f"Bearer {api_key}",
        }
        req = urllib.request.Request(endpoint, data=data_bytes, headers=headers, method="POST")
        local_out = {t: t for t in chunk}
        try:
            print(
                f"  [LLM {ci}/{len(chunks)}] {len(chunk)} metin gönderiliyor -> {model}",
                file=sys.stderr,
            )
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                body = resp.read().decode("utf-8")
            j = json.loads(body)
            content = j["choices"][0]["message"]["content"].strip()

            lines = [ln.strip() for ln in content.splitlines() if ln.strip()]
            for i, orig in enumerate(chunk):
                if i < len(lines):
                    tr_line = re.sub(r"^\s*\d+\.\s*", "", lines[i]).strip()
                    tr_line = tr_line.strip('"""').strip("'''").strip('"').strip("'")
                    local_out[orig] = tr_line or orig
                else:
                    print(f"    [uyarı] satır eksik -> {orig[:40]!r}", file=sys.stderr)
        except Exception as e:
            print(f"  [LLM hata] chunk {ci}: {e}", file=sys.stderr)
            for orig in chunk:
                try:
                    local_out[orig] = _translate_one_openai(
                        orig, sys_prompt, endpoint, model, api_key, temperature, timeout
                    )
                except Exception as e2:
                    print(f"    [tekil hata] {orig[:40]!r}: {e2}", file=sys.stderr)
        return local_out

    work_items = list(enumerate(chunks, 1))
    parallel = max(1, int(parallel or 1))

    if parallel == 1 or len(work_items) == 1:
        # Sıralı (eski davranış)
        for item in work_items:
            local = _process_chunk(item)
            out.update(local)
    else:
        # Paralel - ThreadPoolExecutor
        print(f"  [LLM] Paralel mod: Aynı anda {parallel} batch çalışıyor", file=sys.stderr)
        with concurrent.futures.ThreadPoolExecutor(max_workers=parallel) as ex:
            for local in ex.map(_process_chunk, work_items):
                out.update(local)
    return out

def _translate_one_openai(
    text: str, sys_prompt: str, endpoint: str, model: str,
    api_key: str, temperature: float, timeout: int
) -> str:
    payload = {
        "model": model,
        "temperature": temperature,
        "messages": [
            {"role": "system", "content": sys_prompt + "\nReply with ONLY the single translated string, nothing else."},
            {"role": "user", "content": f"Translate this Turkish UI string to English:\n{text}"},
        ],
        "stream": False,
    }
    req = urllib.request.Request(
        endpoint,
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {api_key}",
        },
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        j = json.loads(resp.read().decode("utf-8"))
    val = j["choices"][0]["message"]["content"].strip()
    val = val.strip('"""').strip("'''").strip('"').strip("'")
    return val or text

# --- Çıktı Formatları ---
def write_csv(path: Path, rows: List[dict]) -> None:
    fields = ["key", "turkish", "english", "source_file", "line", "raw_snippet"]
    with path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)

def write_resx(path: Path, entries: Dict[str, str]) -> None:
    body_lines: List[str] = []
    for k, v in sorted(entries.items()):
        safe_v = (
            v.replace("&", "&amp;")
             .replace("<", "&lt;")
             .replace(">", "&gt;")
        )
        body_lines.append(
            f'  <data name="{k}" xml:space="preserve">'
            f"<value>{safe_v}</value></data>"
        )
    template = textwrap.dedent(
        """\
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
            <xsd:element name="root" msdata:IsDataSet="true">
              <xsd:complexType>
                <xsd:choice maxOccurs="unbounded">
                  <xsd:element name="resheader">
                    <xsd:complexType>
                      <xsd:sequence><xsd:element name="value" type="xsd:string" minOccurs="0"/></xsd:sequence>
                      <xsd:attribute name="name" type="xsd:string" use="required"/>
                    </xsd:complexType>
                  </xsd:element>
                  <xsd:element name="data">
                    <xsd:complexType>
                      <xsd:sequence>
                        <xsd:element name="value" type="xsd:string" minOccurs="0"/>
                        <xsd:element name="comment" type="xsd:string" minOccurs="0"/>
                      </xsd:sequence>
                      <xsd:attribute name="name" type="xsd:string" use="required"/>
                      <xsd:attribute ref="xml:space"/>
                    </xsd:complexType>
                  </xsd:element>
                </xsd:choice>
              </xsd:complexType>
            </xsd:element>
          </xsd:schema>
          <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
          <resheader name="version"><value>2.0</value></resheader>
        BODY
        </root>
        """
    ).replace("BODY", "\n".join(body_lines))
    path.write_text(template, encoding="utf-8")

# --- Ana ---
def main() -> int:
    global PROJECT_ROOT
    parser = argparse.ArgumentParser(
        description="mdaiAgent - Türkçe string çıkarıcı & çeviri yardımcısı",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("--root", default=str(PROJECT_ROOT), help="Proje kök dizini")
    parser.add_argument("--output", choices=("console", "csv", "resx"), default="console")
    parser.add_argument("--out", default=None, help="CSV çıktısı dosya yolu")
    parser.add_argument("--tr-resx", default=None, help="Türkçe .resx çıktı yolu")
    parser.add_argument("--en-resx", default=None, help="İngilizce .resx çıktı yolu (çeviri olmadan sadece anahtarlar + tr)")
    parser.add_argument("--translate", action="store_true", help="İngilizceye çeviri yap")
    parser.add_argument(
        "--translator",
        choices=("google-free", "deepl", "local-llm", "ollama", "openai-compatible"),
        default="google-free",
        help="Çeviri motoru: local-llm/ollama/openai-compatible (hepsi OpenAI formatında)",
    )
    parser.add_argument("--deepl-key", default=None, help="DeepL API anahtarı (--translator deepl için)")
    parser.add_argument(
        "--llm-base-url",
        default="http://localhost:11434/v1",
        help="Yerel LLM / OpenAI uyumlu API URL (ör. http://192.168.1.122:11434/v1)",
    )
    parser.add_argument(
        "--llm-model",
        default=None,
        help="LLM model adı (ör. qwen3.8:27b-mlx, llama3.1:8b, gpt-4o-mini)",
    )
    parser.add_argument(
        "--llm-api-key",
        default="ollama",
        help="LLM API anahtarı (Ollama için varsayılan 'ollama' yeterli)",
    )
    parser.add_argument(
        "--llm-batch-size",
        type=int,
        default=40,
        help="Her LLM çağrısında kaç metin birden gönderilecek (daha az = daha yavaş ama daha doğru)",
    )
    parser.add_argument(
        "--llm-temperature",
        type=float,
        default=0.15,
        help="Çeviri yaratıcılığı (0-1 arası, çeviri için düşük olmalı)",
    )
    parser.add_argument(
        "--llm-timeout",
        type=int,
        default=300,
        help="LLM isteği başına saniye cinsinden zaman aşımı",
    )
    parser.add_argument(
        "--llm-parallel",
        type=int,
        default=1,
        help="Aynı anda kaç batch LLM'e gönderilsin? (1=Sıralı, 2-4=Hızlı ~2-4x)",
    )
    parser.add_argument("--include-loc-get", action="store_true", default=True,
                        help="Localization.Get() çağrılarını da tara")
    args = parser.parse_args()

    PROJECT_ROOT = Path(args.root).resolve()
    files = find_source_files()
    print(f"[tarama] {PROJECT_ROOT} içinde {len(files)} kaynak dosyası bulundu.", file=sys.stderr)

    found: Dict[str, dict] = {}  # key -> row
    loc_pairs: List[Tuple[str, str]] = []
    key_counter: Dict[str, int] = {}

    for f in files:
        rel = f.relative_to(PROJECT_ROOT)
        if f.suffix.lower() == ".cs":
            for snippet, turkish, lineno in scan_cs_file(f):
                base = pascal_case(turkish)
                key = base
                if turkish in found:
                    found[turkish]["source_file"] += f"; {rel}"
                    continue
                while key in {v["key"] for v in found.values()}:
                    # çakışma önle
                    key_counter[base] = key_counter.get(base, 1) + 1
                    key = f"{base}{key_counter[base]}"
                found[turkish] = {
                    "key": key, "turkish": turkish, "english": "",
                    "source_file": str(rel), "line": lineno,
                    "raw_snippet": snippet,
                }
            if args.include_loc_get:
                for tr, en, lineno in scan_loc_get(f):
                    loc_pairs.append((tr, en))
                    if tr and tr not in found:
                        base = pascal_case(tr)
                        key = base
                        while key in {v["key"] for v in found.values()}:
                            key_counter[base] = key_counter.get(base, 1) + 1
                            key = f"{base}{key_counter[base]}"
                        found[tr] = {
                            "key": key, "turkish": tr, "english": en,
                            "source_file": str(rel), "line": lineno,
                            "raw_snippet": f"Localization.Get({tr!r}, {en!r})",
                        }
        elif f.suffix.lower() == ".xaml":
            for snippet, turkish, lineno in scan_xaml_file(f):
                if turkish in found:
                    found[turkish]["source_file"] += f"; {rel}"
                    continue
                base = pascal_case(turkish)
                key = base
                while key in {v["key"] for v in found.values()}:
                    key_counter[base] = key_counter.get(base, 1) + 1
                    key = f"{base}{key_counter[base]}"
                found[turkish] = {
                    "key": key, "turkish": turkish, "english": "",
                    "source_file": str(rel), "line": lineno,
                    "raw_snippet": snippet,
                }

    rows = list(found.values())
    print(f"[tarama] {len(rows)} benzersiz Türkçe string bulundu.", file=sys.stderr)
    if loc_pairs:
        print(f"[tarama] Bunlardan {len(loc_pairs)} tanesi zaten Localization.Get() içinde.", file=sys.stderr)

    # Çeviri
    if args.translate and rows:
        need_tr = [r["turkish"] for r in rows if not r["english"]]
        print(f"[çeviri] {len(need_tr)} metin {args.translator} ile çevriliyor...", file=sys.stderr)
        if args.translator == "deepl":
            if not args.deepl_key:
                print("HATA: --translator deepl için --deepl-key gerekli.", file=sys.stderr)
                return 2
            translations = translate_deepl(need_tr, "en", args.deepl_key)
        elif args.translator in ("local-llm", "ollama", "openai-compatible"):
            if not args.llm_model:
                print(
                    "HATA: Yerel LLM çevirisi için --llm-model (örn: qwen3.8:27b-mlx) belirtmelisiniz.",
                    file=sys.stderr,
                )
                return 2
            print(
                f"  [LLM] Endpoint: {args.llm_base_url} | Model: {args.llm_model} | Batch: {args.llm_batch_size} | Paralel: {args.llm_parallel}",
                file=sys.stderr,
            )
            translations = translate_openai_compatible(
                need_tr,
                "en",
                base_url=args.llm_base_url,
                model=args.llm_model,
                api_key=args.llm_api_key,
                temperature=args.llm_temperature,
                batch_size=args.llm_batch_size,
                timeout=args.llm_timeout,
                parallel=args.llm_parallel,
            )
        else:
            translations = translate_google_free(need_tr, "en")
        for r in rows:
            if not r["english"]:
                r["english"] = translations.get(r["turkish"], r["turkish"])
    else:
        # Loc.Get içindeki en değerlerini kullanalım
        tr_to_en = {tr: en for tr, en in loc_pairs if en}
        for r in rows:
            if not r["english"] and r["turkish"] in tr_to_en:
                r["english"] = tr_to_en[r["turkish"]]

    # Çıktı
    if args.output == "console":
        print("\n" + "="*110)
        print(f"{'KEY':<40} | {'TÜRKÇE':<50} | {'İNGİLİZCE'}")
        print("-"*110)
        for r in rows[:200]:
            tr = (r["turkish"][:47] + "...") if len(r["turkish"]) > 50 else r["turkish"]
            en = (r["english"][:47] + "...") if len(r["english"]) > 50 else r["english"]
            print(f"{r['key']:<40} | {tr:<50} | {en}")
        if len(rows) > 200:
            print(f"... (toplam {len(rows)} satır, --output csv/resx kullanın)")
        print("="*110)
        print(f"\nÖzet: {len(rows)} benzersiz string bulundu.")
        print(f"  Konum örnekleri: {rows[0]['source_file']}:{rows[0]['line']}" if rows else "")

    elif args.output == "csv":
        out = Path(args.out or "strings_found.csv")
        write_csv(out, rows)
        print(f"[yazıldı] CSV -> {out.resolve()}", file=sys.stderr)

    elif args.output == "resx":
        tr_resx = Path(args.tr_resx or "Strings.generated.tr.resx")
        en_resx = Path(args.en_resx or "Strings.generated.en.resx")
        tr_map = {r["key"]: r["turkish"] for r in rows}
        en_map = {r["key"]: r["english"] if r["english"] else r["turkish"] for r in rows}
        write_resx(tr_resx, tr_map)
        write_resx(en_resx, en_map)
        print(f"[yazıldı] Türkçe  .resx -> {tr_resx.resolve()}", file=sys.stderr)
        print(f"[yazıldı] İngilizce .resx -> {en_resx.resolve()}", file=sys.stderr)

    return 0

if __name__ == "__main__":
    raise SystemExit(main())
