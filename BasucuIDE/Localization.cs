namespace mdaiAgent;

public static class Localization
{
    private static readonly Dictionary<(string tr, string en), string> _keyMap = new()
    {
        { ("İptal", "Cancel"), "Cancel" },
        { ("Kaydet", "Save"), "Save" },
        { ("Devam", "Continue"), "Continue" },
        { ("Tamam", "OK"), "OK" },
        { ("Evet", "Yes"), "Yes" },
        { ("Hayır", "No"), "No" },
        { ("Kapat", "Close"), "Close" },
        { ("Sil", "Delete"), "Delete" },
        { ("Onayla", "Confirm"), "Confirm" },
        { ("Tekrar Dene", "Retry"), "Retry" },
        { ("Yeni Sohbet", "New Chat"), "NewChat" },
        { ("Yeni sohbet oluşturuldu.", "New chat created."), "NewChatCreated" },
        { ("Sohbet adı güncellendi.", "Chat name updated."), "ChatNameUpdated" },
        { ("Sohbet Adını Değiştir", "Rename Chat"), "RenameChat" },
        { ("Yeni sohbet adını girin:", "Enter a new chat name:"), "EnterNewChatName" },
        { ("Sohbeti Sil", "Delete Chat"), "DeleteChat" },
        { ("Plan modu zaten hazırlanıyor. Lütfen bekleyin.", "Plan mode is already being prepared. Please wait."), "PlanModePreparing" },
        { ("Komut Paletini Aç", "Open command palette"), "OpenCommandPalette" },
        { ("Aktif Dosyayı Kaydet", "Save active file"), "SaveActiveFile" },
        { ("Tüm Dosyaları Kaydet", "Save all files"), "SaveAllFiles" },
        { ("Terminal Çıktısını Temizle", "Clear terminal output"), "ClearTerminalOutput" },
        { ("Terminal Girişine Odaklan", "Focus terminal input"), "FocusTerminalInput" },
        { ("Metin Editöründe Arama", "Search in text editor"), "SearchInTextEditor" },
        { ("Komut Paletinde Seçili Komutu Çalıştır / Sohbet Mesajı Gönder", "Run selected command in palette / send chat message"), "RunPaletteOrSendMsg" },
        { ("Sohbet Mesajında Yeni Satır", "New line in chat message"), "NewLineInChat" },
        { ("Komut Paletinde Gezin / Terminal Geçmişini Gezin", "Navigate in command palette / terminal history"), "NavigatePaletteOrHistory" },
        { ("Komut Paletini Kapat / Pencereleri Kapat", "Close command palette / windows"), "ClosePaletteOrWindows" },
        { ("İstediğiniz projenin teknik mimari planını çizer (implementation_plan.md)", "Generates the technical architecture plan for your project (implementation_plan.md)"), "PlanCommandDesc" },
        { ("Çizilen planı uygulanabilir görev kutucuklarına (checklist) böler (task.md)", "Breaks the plan into actionable task checklist items (task.md)"), "TasksCommandDesc" },
        { ("Projeniz için gereksinimler dosyası oluşturur (spec.md)", "Creates a requirements document for your project (spec.md)"), "SpecCommandDesc" },
        { ("Belirlenen plana göre kodlamaya ve uygulamaya başlar", "Starts coding and implementation according to the selected plan"), "ImplementCommandDesc" },
        { ("Projenin temel kurallarını ve yapı taşlarını oluşturur", "Creates the basic rules and building blocks for the project"), "ConstitutionCommandDesc" },
    };

    public static string CurrentLanguage => LocalizationManager.Instance.CurrentLanguageCode;

    public static void SetLanguage(string? languageCode)
    {
        LocalizationManager.Instance.SetLanguage(languageCode);
    }

    public static bool IsEnglish() => LocalizationManager.Instance.IsEnglish;

    public static string Get(string turkishText, string englishText)
    {
        var key = LookupKey(turkishText, englishText);
        if (key != null)
            return LocalizationManager.Instance.GetString(key);

        return LocalizationManager.Instance.IsEnglish ? englishText : turkishText;
    }

    public static string Format(string turkishText, string englishText, params object[] args)
    {
        var key = LookupKey(turkishText, englishText);
        if (key != null)
            return LocalizationManager.Instance.GetString(key, args);

        var template = LocalizationManager.Instance.IsEnglish ? englishText : turkishText;
        try { return string.Format(template, args); }
        catch { return template; }
    }

    private static string? LookupKey(string turkishText, string englishText)
    {
        if (_keyMap.TryGetValue((turkishText, englishText), out var key))
            return key;

        foreach (var entry in _keyMap)
        {
            if (string.Equals(entry.Key.tr, turkishText, StringComparison.Ordinal) ||
                string.Equals(entry.Key.en, englishText, StringComparison.Ordinal))
                return entry.Value;
        }
        return null;
    }
}
