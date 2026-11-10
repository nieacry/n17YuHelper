namespace N17YuHelper.Translate;

public sealed record Language(string Code, string Name, string PromptName)
{
    /// <summary>Тег языка для документов Office (проверка орфографии).</summary>
    public string Tag => Code switch { "en" => "en-US", "ru" => "ru-RU", "uk" => "uk-UA", "zh" => "zh-CN", _ => Code };

    public override string ToString() => Name;
}

/// <summary>Поддерживаемые языки и быстрое определение языка текста без модели.</summary>
public static class Languages
{
    public static readonly Language English = new("en", "English", "English");
    public static readonly Language Russian = new("ru", "Russian", "Russian");
    public static readonly Language Ukrainian = new("uk", "Ukrainian", "Ukrainian");
    public static readonly Language Chinese = new("zh", "Chinese", "Simplified Chinese");

    public static readonly IReadOnlyList<Language> All = [English, Russian, Ukrainian, Chinese];

    public static Language? ByCode(string? code) =>
        All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Определяет язык по буквам: иероглифы → китайский; кириллица → украинский,
    /// если есть і/ї/є/ґ (и их больше, чем ы/э/ъ/ё), иначе русский; латиница → английский.
    /// </summary>
    public static Language Detect(string text)
    {
        int cjk = 0, cyr = 0, latin = 0, ukOnly = 0, ruOnly = 0;
        foreach (char c in text)
        {
            if ((c >= '一' && c <= '鿿') || (c >= '㐀' && c <= '䶿') || (c >= '豈' && c <= '﫿'))
                cjk++;
            else if (c >= 'Ѐ' && c <= 'ӿ')
            {
                cyr++;
                if ("іїєґІЇЄҐ".Contains(c)) ukOnly++;
                else if ("ыэъёЫЭЪЁ".Contains(c)) ruOnly++;
            }
            else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                latin++;
        }

        // Иероглиф несёт больше смысла, чем буква, поэтому им хватает меньшей доли.
        if (cjk > 0 && cjk * 3 >= cyr + latin) return Chinese;
        if (cyr > latin) return ukOnly > ruOnly ? Ukrainian : Russian;
        return English;
    }
}
