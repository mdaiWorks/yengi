import os
import sys
import json
import urllib.request
import urllib.error
from datetime import datetime, timezone

REPO = "mdaiWorks/yengi"
BASE_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "traffic-data")

def get_utc_now_str():
    return datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")

def get_utc_iso_str():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

def get_token():
    return os.environ.get("TRAFFIC_TOKEN") or os.environ.get("GITHUB_TOKEN") or ""

def api_request(endpoint, token=""):
    url = f"https://api.github.com/repos/{REPO}/{endpoint}"
    headers = {
        "Accept": "application/vnd.github+json",
        "User-Agent": "Yengi-Traffic-Archiver"
    }
    if token:
        headers["Authorization"] = f"Bearer {token}"

    req = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(req) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        print(f"HTTP Error {e.code} on {endpoint}: {e.read().decode('utf-8', errors='ignore')}")
        return None
    except Exception as e:
        print(f"Error on {endpoint}: {e}")
        return None

def merge_daily_data(existing_list, incoming_list, key_field="timestamp"):
    data_map = {}
    for item in existing_list:
        data_map[item[key_field]] = item
    for item in incoming_list:
        data_map[item[key_field]] = item  # Overwrite with newer counts
    return sorted(data_map.values(), key=lambda x: x[key_field])

def merge_referrers(existing_list, incoming_list):
    data_map = {item.get("referrer"): item for item in existing_list if item.get("referrer")}
    for item in incoming_list:
        ref = item.get("referrer")
        if not ref:
            continue
        if ref in data_map:
            data_map[ref]["count"] = max(data_map[ref].get("count", 0), item.get("count", 0))
            data_map[ref]["uniques"] = max(data_map[ref].get("uniques", 0), item.get("uniques", 0))
        else:
            data_map[ref] = item
    return sorted(data_map.values(), key=lambda x: x.get("count", 0), reverse=True)

def load_json(filepath, default):
    if os.path.exists(filepath):
        try:
            with open(filepath, "r", encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            return default
    return default

def save_json(filepath, data):
    os.makedirs(os.path.dirname(filepath), exist_ok=True)
    with open(filepath, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)

def main():
    token = get_token()
    os.makedirs(BASE_DIR, exist_ok=True)

    views_file = os.path.join(BASE_DIR, "views.json")
    clones_file = os.path.join(BASE_DIR, "clones.json")
    referrers_file = os.path.join(BASE_DIR, "referrers.json")
    releases_file = os.path.join(BASE_DIR, "releases.json")
    summary_file = os.path.join(BASE_DIR, "README.md")

    # 1. Fetch Views
    print("Fetching traffic views...")
    views_data = api_request("traffic/views", token)
    existing_views = load_json(views_file, {"views": []})
    if views_data and "views" in views_data:
        merged_views = merge_daily_data(existing_views.get("views", []), views_data.get("views", []))
        total_views = sum(v.get("count", 0) for v in merged_views)
        total_uniques = sum(v.get("uniques", 0) for v in merged_views)
        existing_views = {
            "all_time_total_views": total_views,
            "all_time_unique_visitors_est": total_uniques,
            "last_updated": get_utc_iso_str(),
            "views": merged_views
        }
        save_json(views_file, existing_views)
        print(f"  [+] Views updated. Total days tracked: {len(merged_views)}")

    # 2. Fetch Clones
    print("Fetching traffic clones...")
    clones_data = api_request("traffic/clones", token)
    existing_clones = load_json(clones_file, {"clones": []})
    if clones_data and "clones" in clones_data:
        merged_clones = merge_daily_data(existing_clones.get("clones", []), clones_data.get("clones", []))
        total_clones = sum(c.get("count", 0) for c in merged_clones)
        total_unique_cloners = sum(c.get("uniques", 0) for c in merged_clones)
        existing_clones = {
            "all_time_total_clones": total_clones,
            "all_time_unique_cloners_est": total_unique_cloners,
            "last_updated": get_utc_iso_str(),
            "clones": merged_clones
        }
        save_json(clones_file, existing_clones)
        print(f"  [+] Clones updated. Total days tracked: {len(merged_clones)}")

    # 3. Fetch Referrers
    print("Fetching referrers...")
    referrers_data = api_request("traffic/popular/referrers", token)
    existing_referrers = load_json(referrers_file, [])
    if isinstance(referrers_data, list):
        merged_refs = merge_referrers(existing_referrers, referrers_data)
        save_json(referrers_file, merged_refs)
        print(f"  [+] Referrers updated: {len(merged_refs)} sources.")

    # 4. Fetch Releases Download Stats
    print("Fetching releases download stats...")
    releases_data = api_request("releases", token)
    release_stats = []
    total_downloads = 0
    if isinstance(releases_data, list):
        for rel in releases_data:
            tag = rel.get("tag_name", "N/A")
            for asset in rel.get("assets", []):
                name = asset.get("name")
                count = asset.get("download_count", 0)
                total_downloads += count
                release_stats.append({
                    "version": tag,
                    "file": name,
                    "downloads": count,
                    "updated_at": asset.get("updated_at")
                })
        save_json(releases_file, {
            "total_downloads": total_downloads,
            "last_updated": get_utc_iso_str(),
            "releases": release_stats
        })
        print(f"  [+] Releases updated. Total downloads: {total_downloads}")

    # 5. Generate Markdown Summary
    now_str = get_utc_now_str()
    lines = [
        "# 📊 Yengi - Tüm Zamanlar Trafik ve İndirme Raporu",
        f"> *Son Güncelleme: {now_str}*",
        "",
        "## 📈 Genel Özet (All-Time Totals)",
        f"- **Toplam EXE İndirme:** `{total_downloads}`",
        f"- **Takip Edilen Toplam Görüntülenme (Views):** `{existing_views.get('all_time_total_views', 0)}`",
        f"- **Takip Edilen Toplam Git Clone:** `{existing_clones.get('all_time_total_clones', 0)}`",
        "",
        "## 📦 Sürümlere Göre İndirmeler",
        "| Sürüm | Dosya | İndirme Sayısı |",
        "| :--- | :--- | :--- |"
    ]
    for r in release_stats[:10]:
        lines.append(f"| **{r['version']}** | `{r['file']}` | **{r['downloads']}** |")

    lines.extend([
        "",
        "## 🌐 Trafik Kaynakları (Referrers)",
        "| Kaynak (Referrer) | Görüntülenme | Tekil Ziyaretçi |",
        "| :--- | :--- | :--- |"
    ])
    refs = load_json(referrers_file, [])
    if refs:
        for ref in refs[:10]:
            lines.append(f"| {ref.get('referrer')} | {ref.get('count', 0)} | {ref.get('uniques', 0)} |")
    else:
        lines.append("| *Henüz veri yok* | - | - |")

    lines.extend([
        "",
        "## 📅 Günlük Trafik (Son 14 Gün)",
        "| Tarih | Görüntülenme (Views) | Tekil Ziyaretçi | Clone | Tekil Cloner |",
        "| :--- | :--- | :--- | :--- | :--- |"
    ])

    v_map = {v.get("timestamp", "")[:10]: v for v in existing_views.get("views", [])}
    c_map = {c.get("timestamp", "")[:10]: c for c in existing_clones.get("clones", [])}
    all_dates = sorted(set(list(v_map.keys()) + list(c_map.keys())), reverse=True)[:14]

    if all_dates:
        for d in all_dates:
            v_cnt = v_map.get(d, {}).get("count", 0)
            v_unq = v_map.get(d, {}).get("uniques", 0)
            c_cnt = c_map.get(d, {}).get("count", 0)
            c_unq = c_map.get(d, {}).get("uniques", 0)
            lines.append(f"| {d} | {v_cnt} | {v_unq} | {c_cnt} | {c_unq} |")
    else:
        lines.append("| *Henüz veri yok* | - | - | - | - |")

    lines.append("")
    with open(summary_file, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))

    print(f"  [+] Summary README updated at {summary_file}")

if __name__ == "__main__":
    main()
