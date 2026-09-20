# mdaiAgent Context & Performance Optimization Checklist

Bu liste, uzun planı takip edilebilir operasyonel görevler halinde dönüştürülmüş halidir.

önemli Not: Kod değiştirmeden önce her adımın ölçülmesi gerekir. Bu liste, ilerlemeyi takip etmek için kullanılır. Önce mevcut mimariyi doğrula. takıldığın ve unuttuğun yerde update.txt den yardım al.

---

## Phase 0 - Baseline ölçümü ve doğrulama

- [ ] 0.1. Her LLM request için telemetry ekle: provider, model, request zamanı, TTFT, total latency
- [ ] 0.2. Her request için yaklaşık input token sayısı hesapla
- [ ] 0.3. Her request için system/message/history/tool payload boyutunu ölç
- [ ] 0.4. Her request için context utilization oranını hesapla
- [ ] 0.5. Qwen local ve cloud provider için aynı görev üzerinden örnek benchmark topla
- [ ] 0.6. İlk görev ve ikinci görev için prompt büyüklüğünü karşılaştır
- [ ] 0.7. Payload loglamayı güvenli şekilde uygula; API key / secret / token loglanmasın
- [ ] 0.8. Debug modunda yalnızca sanitized payload kaydetsin
- [ ] 0.9. Mevcut history, file context, tool result ve discovery sürecini bir kez resmileştir

---

## Phase 1 - En düşük riskli context azaltma

- [ ] 1.1. Active file full content otomatik inject davranışını gözden geçir
- [ ] 1.2. Local/Ollama için active file full content gönderimini kapat
- [ ] 1.3. Active file için metadata-only / snippet-only davranışı tanımla
- [ ] 1.4. Aktif dosya içeriğini sadece kullanıcı açıkça isterse ekle
- [ ] 1.5. ProjectContextDiscovery otomatik çalışmasını local model için kapat
- [ ] 1.6. Discovery çıktısını sadece gerektiğinde, küçük ama ilgili şekilde üret
- [ ] 1.7. Discovery sonucu file içeriği taşıyorsa bunu engelle
- [ ] 1.8. Constitution dosyasını her request’te yeniden okumaya gerek kalmayacak şekilde cache et
- [ ] 1.9. Stable context / session context / volatile context ayrımını uygula
- [ ] 1.10. System promptu her turda yeniden üretmek yerine cache kullan

---

## Phase 2 - History ve tool result yönetimi

- [ ] 2.1. History sınırını mesaj sayısı yerine token bütçesi ile yönet
- [ ] 2.2. Local model için history budget belirle (ör. 4K-8K)
- [ ] 2.3. Cloud model için history budget belirle (ör. 12K-20K)
- [ ] 2.4. MaxHistoryMessagesToSend mantığını yeniden değerlendir
- [ ] 2.5. Büyük tool outputlarını tam olarak history’de tutma
- [ ] 2.6. ReadFile sonuçlarını gereksiz tekrar göndermeme politikası uygula
- [ ] 2.7. Aynı dosya/aynı aralık tekrar okunursa deduplication yap
- [ ] 2.8. Tool resultlarını küçük, anlamlı özetler halinde sakla
- [ ] 2.9. CreatePlan, CreateOrUpdateFile, SearchCode, ReadFile gibi sonuçların semantic summary formatını tanımla
- [ ] 2.10. Tool result lifecycle oluştur: keep / summarize / discard
- [ ] 2.11. CreatePlan aracının ürettiği plan çıktısının sadece UI'a değil, LLM'in okuyabilmesi için session.History'e (Sistem veya Assistant mesajı olarak) eklenmesini sağla.

---

## Phase 3 - Retrieval, repository map ve context compaction

- [ ] 3.1. ProjectContextDiscovery'yi context retrieval mantığına taşı
- [ ] 3.2. Tüm repository içeriğini her request’e koyma davranışını kaldır
- [ ] 3.3. Repository map / project map oluşturma mantığına geç
- [ ] 3.4. Repository map için token budget tanımla
- [ ] 3.5. Local için küçük repo map, cloud için daha geniş repo map kullan
- [ ] 3.6. Context usage threshold belirle: normal / warning / aggressive compaction
- [ ] 3.7. %50-70, %70-80, %80+ durumlarında otomatik compaction uygula
- [ ] 3.8. Compaction sırasında eski large tool outputlarını temizle
- [ ] 3.9. Duplicate file content ve eski discovery çıktılarını temizle
- [ ] 3.10. Compaction sonrası tool call / tool result bütünlüğünü koru
- [ ] 3.11. Summary formatı hazırla: current goal, files changed, decisions, open issues, pending work

---

## Phase 4 - Provider ve model özel politikaları

- [ ] 4.1. Cloud model ve local model için ayrı ContextPolicy oluştur
- [ ] 4.2. Model profile tanımla: provider, model, context window, supports tools, supports streaming, is local
- [ ] 4.3. Local model için daha agresif trimming aktif et
- [ ] 4.4. Cloud model için daha geniş history ve daha az agresif pruning uygula
- [ ] 4.5. Local provider için num_ctx / runtime context ayarlamalarını kontrol et
- [ ] 4.6. Context büyütmek yerine gereksiz input azaltma önceliği tanımla
- [ ] 4.7. Provider değişiminde cache / prompt context davranışını resetle
- [ ] 4.8. Prompt caching desteği varsa native cache kullan; değilse sahte cache üretme

---

## Phase 5 - Tool design ve filtering

- [ ] 5.1. Tüm tool’ları her request’e göndermenin maliyetini ölç
- [ ] 5.2. Tool definitions maliyetini ayrı ayrı raporla
- [ ] 5.3. Gereksiz tool filtering yapmadan önce ölçüm yap
- [ ] 5.4. Basit görevler için minimal tool set kullan
- [ ] 5.5. Karmaşık görevlerde daha geniş tool set aç
- [ ] 5.6. Tool Router / task classifier taslağı hazırla
- [ ] 5.7. File tools, search tools, terminal tools, build/test tools gibi tool grupları tanımla
- [ ] 5.8. Tool selection yanlışsa agent yeteneğini bozmayacak şekilde güvenli fallback ekle

---

## Phase 6 - File read ve output limitleri

- [ ] 6.1. ReadFile için max_chars / max_lines limitleri tanımla
- [ ] 6.2. Büyük dosyalar için offset / limit / range destek ekle
- [ ] 6.3. Dosya okuma için küçük parçalarla on-demand read mantığı uygula
- [ ] 6.4. SearchCode ve ListDirectory için max_results limitleri tanımla
- [ ] 6.5. Terminal output için max_output_chars sınırı koy
- [ ] 6.6. Büyük dosya içeriğini tek seferde inject etme
- [ ] 6.7. Modelin gerçekten dosya içeriğine ihtiyacı varsa ReadFile aracını kullanmasına izin ver

---

## Phase 7 - Context budget ve emergency reduction

- [ ] 7.1. Her request öncesi context budget hesapla
- [ ] 7.2. Output reserve ve safety reserve için alan ayır
- [ ] 7.3. System + tools + history + repo map + current task + file context için bütçe tanımla
- [ ] 7.4. Model context limitini aşacak requestleri reject etmeden önce trim uygula
- [ ] 7.5. Emergency reduction sırası tanımla:
  - eski tool outputları
  - eski discovery çıktıları
  - duplicate file content
  - eski terminal outputları
  - eski history
- [ ] 7.6. Current user request ve current task state asla düşürme
- [ ] 7.7. Context budget uygunsuzsa uygun azalma stratejisi uygula
- [ ] 7.8. Kullanıcıya UI üzerinden manuel olarak "Geçmişi Temizle (Ama projeyi tut)" yeteneği ver.

---

## Phase 8 - Request format ve provider adapter temizliği

- [ ] 8.1. Ortak internal message representation oluştur
- [ ] 8.2. Provider adapterlar kendi request formatına çevirsin
- [ ] 8.3. Cloud ve local provider formatlarını gereksiz yere farklılaştırma
- [ ] 8.4. Role, tool call, result ilişkisini koru
- [ ] 8.5. Yetim tool call / yetim tool result bırakma
- [ ] 8.6. OpenAI-compatible request formatını doğrula ve adapter katmanını temiz tut
- [ ] 8.7. Streaming davranışını bozma
- [ ] 8.8. Tool-calling formatlarını koru

---

## Phase 9 - Validation ve regression testleri

- [ ] 9.1. Task A: başlangıç oluşturma benchmarki yap
- [ ] 9.2. Task B: basit düzenleme benchmarki yap
- [ ] 9.3. Task C: ReadFile benchmarki yap
- [ ] 9.4. Task D: küçük edit benchmarki yap
- [ ] 9.5. Task E: orta ölçekli değişiklik benchmarki yap
- [ ] 9.6. Task F: multi-file karmaşık görev benchmarki yap
- [ ] 9.7. Her benchmark için TTFT, total latency, input tokens, output tokens, tool count kaydet
- [ ] 9.8. İlk ve ikinci mesaj için prompt büyüklüğünü kıyasla
- [ ] 9.9. Basit görevlerde gereksiz plan/discovery yükünü kontrol et
- [ ] 9.10. Karmaşık görevlerde üretkenlik korunup korunmadığını doğrula
- [ ] 9.11. Local model ve cloud model için ayrı acceptance criteria doğrula

---

## Phase 10 - Acceptance criteria

### Local model hedefleri

- [ ] 10.1. Düşük TTFT
- [ ] 10.2. Kontrollü context büyümesi
- [ ] 10.3. Küçük history bütçesi
- [ ] 10.4. Aggressive tool output trimming
- [ ] 10.5. On-demand file reads
- [ ] 10.6. Stable system prompt

### Cloud model hedefleri

- [ ] 10.7. Daha geniş context kullanımı
- [ ] 10.8. Daha az agresif trimming
- [ ] 10.9. Prompt caching varsa uygun şekilde kullanılmalı
- [ ] 10.10. Daha büyük task’lar için daha geniş history desteği

---

## Phase 11 - Son kontrol listesi

- [ ] 11.1. System prompt gereksiz yere her request’te yeniden oluşturulmuyor
- [ ] 11.2. Constitution gereksiz disk I/O yapmıyor
- [ ] 11.3. Active file full content her request’te otomatik gönderilmiyor
- [ ] 11.4. ProjectContextDiscovery her mesajda otomatik çalışmıyor
- [ ] 11.5. History mesaj sayısı değil token bütçesiyle kontrol ediliyor
- [ ] 11.6. Büyük tool outputları sınırsız büyümüyor
- [ ] 11.7. Aynı dosya tekrar tekrar gönderilmiyor
- [ ] 11.8. Büyük dosyalar offset/limit ile okunabiliyor
- [ ] 11.9. Context compaction çalışıyor
- [ ] 11.10. Compaction tool-call / tool-result bütünlüğünü koruyor
- [ ] 11.11. Cloud/local context policy ayrılmış
- [ ] 11.12. Model context limitini aşma önleniyor
- [ ] 11.13. Tool definitions maliyeti ölçülüyor
- [ ] 11.14. Gereksiz tool filtering yapılmadan önce ölçüm var
- [ ] 11.15. TTFT ve input/output token telemetry var
- [ ] 11.16. Debug payload loglama güvenli ve kapalı varsayılan
- [ ] 11.17. API key / secret / token loglanmıyor
- [ ] 11.18. Provider/model değişiminde cache ve context davranışı güvenli
- [ ] 11.19. İlk ve ikinci mesaj benchmark’ları tekrarlandı
- [ ] 11.20. Basit görevler gereksiz plan/discovery/context yüküne sokulmuyor
- [ ] 11.21. Karmaşık multi-file görevler eş zamanlı olarak düzgün çalışıyor

---

## Yorum / Notlar

- Hedef, promptu “sadece küçültmek” değil; contextin doğru yere ve doğru zamanda gelmesini sağlamaktır.
- Local model için agresif trimming gerekli olabilir; cloud modeli için aynı politikayı kullanmak yanlış olur.
- En önemli ilk adım, ölçüm yapmadan optimizasyon denememektir.
- Bu kontrol listesi ana hedefi takip etmek için tasarlanmıştır; gereksiz tekrar ve karışıklığı azaltır.

---

## Next Action

- [x] Takip listesi başlatıldı
- [x] Phase 0 ölçüm adımları sıraya alındı
- [x] Öncelikli düşük riskli optimizasyonlar belirlendi
- [ ] Kod değişikliğine geçmeden önce ölçüm ve doğrulama yapılacak
- [ ] Phase 0 tamamlanması için gerçek telemetry + benchmark ölçümü uygulanacak
