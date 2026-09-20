# mdaiRouterTrainer

Bağımsız `mdai-0.6B` tool-router eğitim verisi üreticisi.

Bu proje `mdaiAgent` kaynaklarını çalıştırmaz ve değiştirmez. Tool bilgisi, `catalog/` altındaki versioned snapshot dosyasından okunur.

## İlk PoC kapsamı

- Görevleri JSONL olarak okumak
- Teacher yanıtlarındaki tool adlarını canonical isimlere normalize etmek
- Bilinmeyen tool adlarını açıkça işaretlemek
- İleride provider, resume, rate-limit, consensus ve `training.jsonl` üretimi eklemek

## Yerel kontrol

```powershell
$env:PYTHONPATH = "src"
python -m pytest tests -q
```
