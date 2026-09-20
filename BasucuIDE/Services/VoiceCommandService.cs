using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace mdaiAgent;

/// <summary>
/// Sesli Komut servisi - Microphone kayıt ve STT transkripsiyonu yönetir
/// 
/// Özellikler:
/// - Push-to-talk: Buton basılıyken kayıt, bırakılınca durdur
/// - Dedicated STT API: AppSettings.SttBaseUrl/SttApiKey/SttModel kullanır
/// - Oto-deteksyon: Sessizlik süresi aşıldığında otomatik durdur
/// </summary>
public class VoiceCommandService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly string _tempRecordingDir;

    // Audio recording state
    private WaveInEvent? _waveInDevice;
    private WaveFileWriter? _waveFileWriter;
    private bool _isRecording;
    private string? _currentRecordingFile;
    private CancellationTokenSource? _recordingCts;

    // Configuration
    private const int SampleRate = 16000;  // 16kHz for Whisper
    private const int ChannelCount = 1;    // Mono
    private const int BitsPerSample = 16;
    private const int SilenceThresholdMs = 3000;  // Auto-stop after 3 seconds of silence

    // Silence detection
    private DateTime _lastSoundTime;
    private float _silenceThreshold = 0.05f;  // Amplitude threshold for "silence"

    // Events
    public event EventHandler<RecordingStartedEventArgs>? RecordingStarted;
    public event EventHandler<RecordingStoppedEventArgs>? RecordingStopped;
    public event EventHandler<TranscriptionCompleteEventArgs>? TranscriptionComplete;
    public event EventHandler<TranscriptionErrorEventArgs>? TranscriptionError;
    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    public VoiceCommandService(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        _tempRecordingDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Yengi", "voice");

        try
        {
            if (!Directory.Exists(_tempRecordingDir))
                Directory.CreateDirectory(_tempRecordingDir);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Ses geçici klasörü oluşturulamadı: {ex.Message}");
        }

        _lastSoundTime = DateTime.Now;
    }

    /// <summary>
    /// Microphone'dan kayıt başlat (push-to-talk)
    /// </summary>
    public void StartRecording()
    {
        if (_isRecording)
        {
            Logger.LogInfo("Kayıt zaten devam ediyor");
            return;
        }

        try
        {
            _recordingCts = new CancellationTokenSource();
            _currentRecordingFile = Path.Combine(_tempRecordingDir, $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

            _waveInDevice = new WaveInEvent
            {
                WaveFormat = new WaveFormat(SampleRate, BitsPerSample, ChannelCount)
            };

            _waveFileWriter = new WaveFileWriter(_currentRecordingFile, _waveInDevice.WaveFormat);

            // Volume detection
            _waveInDevice.DataAvailable += WaveInDevice_DataAvailable;
            _waveInDevice.RecordingStopped += WaveInDevice_RecordingStopped;

            _isRecording = true;
            _lastSoundTime = DateTime.Now;

            _waveInDevice.StartRecording();

            Logger.LogInfo($"Sesli kayıt başladı: {_currentRecordingFile}");
            RecordingStarted?.Invoke(this, new RecordingStartedEventArgs { FilePath = _currentRecordingFile });

            // Sessizlik detection döngüsü
            _ = Task.Run(() => SilenceDetectionLoopAsync(_recordingCts.Token));
        }
        catch (Exception ex)
        {
            Logger.LogError($"Kayıt başlatılamadı: {ex.Message}");
            TranscriptionError?.Invoke(this, new TranscriptionErrorEventArgs { Message = $"Kayıt başlatılamadı: {ex.Message}" });
            _isRecording = false;
        }
    }

    /// <summary>
    /// Microphone kayıtını durdur
    /// </summary>
    public void StopRecording()
    {
        if (!_isRecording || _waveInDevice == null)
            return;

        try
        {
            _waveInDevice.StopRecording();
            _isRecording = false;

            // Cancel silence detection loop
            _recordingCts?.Cancel();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Kayıt durdurulurken hata: {ex.Message}");
        }
    }

    /// <summary>
    /// Kaydedilen dosyayı metne dönüştür (Provider-aware STT)
    /// </summary>
    public async Task<string> TranscribeRecordingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(_currentRecordingFile) || !File.Exists(_currentRecordingFile))
        {
            var msg = "Kayıt dosyası bulunamadı";
            Logger.LogError(msg);
            TranscriptionError?.Invoke(this, new TranscriptionErrorEventArgs { Message = msg });
            return "";
        }

        if (string.IsNullOrEmpty(_settings.SttApiKey))
        {
            var msg = "STT API anahtarı ayarlanmamış. Mikrofon butonuna sağ tıklayarak ayarlayın.";
            Logger.LogError(msg);
            TranscriptionError?.Invoke(this, new TranscriptionErrorEventArgs { Message = msg });
            return "";
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger.LogInfo($"Transkripsiyon başladı: {_currentRecordingFile}");

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _settings.SttApiKey);
            client.Timeout = TimeSpan.FromSeconds(30);

            using var form = new MultipartFormDataContent();
            var fileBytes = await File.ReadAllBytesAsync(_currentRecordingFile, cancellationToken);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(fileContent, "file", Path.GetFileName(_currentRecordingFile));
            form.Add(new StringContent(_settings.SttModel), "model");
            form.Add(new StringContent("json"), "response_format");

            var url = $"{_settings.SttBaseUrl.TrimEnd('/')}/audio/transcriptions";
            var response = await client.PostAsync(url, form, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var msg = $"STT API hatası ({(int)response.StatusCode}): {errBody}";
                Logger.LogError(msg);
                TranscriptionError?.Invoke(this, new TranscriptionErrorEventArgs { Message = msg });
                return "";
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.GetProperty("text").GetString() ?? "";

            Logger.LogInfo($"Transkripsiyon tamamlandı: {text[..Math.Min(80, text.Length)]}");
            TranscriptionComplete?.Invoke(this, new TranscriptionCompleteEventArgs { Text = text });

            return text;
        }
        catch (OperationCanceledException)
        {
            var msg = "Transkripsiyon iptal edildi";
            Logger.LogInfo(msg);
            TranscriptionError?.Invoke(this, new TranscriptionErrorEventArgs { Message = msg });
            return "";
        }
        catch (Exception ex)
        {
            var msg = $"Transkripsiyon başarısız: {ex.Message}";
            Logger.LogError(msg);
            TranscriptionError?.Invoke(this, new TranscriptionErrorEventArgs { Message = msg });
            return "";
        }
    }

    /// <summary>
    /// Sessizlik algılama döngüsü - 3 saniye sessizlik sonra otomatik durdur
    /// </summary>
    private async Task SilenceDetectionLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _isRecording)
            {
                var timeSinceLastSound = DateTime.Now - _lastSoundTime;
                
                if (timeSinceLastSound.TotalMilliseconds > SilenceThresholdMs)
                {
                    Logger.LogInfo("Sessizlik algılandı, kayıt otomatik durduruldu");
                    StopRecording();
                    break;
                }

                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when StopRecording() is called
        }
        catch (Exception ex)
        {
            Logger.LogError($"Sessizlik detection hatası: {ex.Message}");
        }
    }

    /// <summary>
    /// Ses seviyesi algılama ve volume event'i gönder
    /// </summary>
    private void WaveInDevice_DataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            _waveFileWriter?.Write(e.Buffer, 0, e.BytesRecorded);

            // Calculate volume level (RMS)
            var buffer = e.Buffer;
            float sum = 0f;
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                var sample = BitConverter.ToInt16(buffer, i) / 32768f;
                sum += sample * sample;
            }

            float rms = (float)Math.Sqrt(sum / (e.BytesRecorded / 2));

            // Sessizlik algılama: RMS eğer threshold'ı aşarsa ses var
            if (rms > _silenceThreshold)
            {
                _lastSoundTime = DateTime.Now;
            }

            // Volume event gönder (UI güncelleme için)
            VolumeChanged?.Invoke(this, new VolumeChangedEventArgs { Level = rms });
        }
        catch (Exception ex)
        {
            Logger.LogError($"Volume algılamada hata: {ex.Message}");
        }
    }

    /// <summary>
    /// Kayıt durduğunda tetiklenir
    /// </summary>
    private void WaveInDevice_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        try
        {
            _waveFileWriter?.Dispose();
            _waveInDevice?.Dispose();

            Logger.LogInfo("Kayıt kapatıldı");
            RecordingStopped?.Invoke(this, new RecordingStoppedEventArgs 
            { 
                FilePath = _currentRecordingFile, 
                IsSuccessful = true 
            });
        }
        catch (Exception ex)
        {
            Logger.LogError($"Kayıt sonlandırma hatası: {ex.Message}");
        }
    }

    /// <summary>
    /// Eski kayıt dosyalarını temizle
    /// </summary>
    public void CleanupOldRecordings(int maxAgeHours = 24)
    {
        try
        {
            if (!Directory.Exists(_tempRecordingDir))
                return;

            var cutoffTime = DateTime.Now.AddHours(-maxAgeHours);
            var files = Directory.GetFiles(_tempRecordingDir, "recording_*.wav");

            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < cutoffTime)
                    {
                        File.Delete(file);
                        Logger.LogInfo($"Eski kayıt silindi: {file}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Dosya silemedi: {file} - {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Cleanup hatası: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            if (_isRecording)
                StopRecording();

            _waveInDevice?.Dispose();
            _waveFileWriter?.Dispose();
            _recordingCts?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Dispose hatası: {ex.Message}");
        }
    }
}

// Event Arguments
public class RecordingStartedEventArgs : EventArgs
{
    public string? FilePath { get; set; }
    public DateTime StartTime { get; set; } = DateTime.Now;
}

public class RecordingStoppedEventArgs : EventArgs
{
    public string? FilePath { get; set; }
    public bool IsSuccessful { get; set; }
    public DateTime StopTime { get; set; } = DateTime.Now;
}

public class TranscriptionCompleteEventArgs : EventArgs
{
    public string Text { get; set; } = "";
    public DateTime CompleteTime { get; set; } = DateTime.Now;
}

public class TranscriptionErrorEventArgs : EventArgs
{
    public string Message { get; set; } = "";
    public Exception? Exception { get; set; }
}

public class VolumeChangedEventArgs : EventArgs
{
    public float Level { get; set; }  // 0.0 to 1.0 (roughly)
}
