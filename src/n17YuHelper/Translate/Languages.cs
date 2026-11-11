namespace N17YuHelper.Translate;

public sealed record Language(string Code, string Name, string PromptName)
{
    /// <summary>Language tag for Office documents (spell checking).</summary>
    public string Tag => Code switch { "en" => "en-US", "ru" => "ru-RU", "uk" => "uk-UA", "zh" => "zh-CN", _ => Code };

    public override string ToString() => Name;
}

/// <summary>Supported languages and fast detection of the text language without a model.</summary>
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
    /// Detects the language by letters: CJK characters → Chinese; Cyrillic → Ukrainian
    /// if it has і/ї/є/ґ (more of them than ы/э/ъ/ё), otherwise Russian; Latin → English.
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

        // A CJK character carries more meaning than a letter, so a smaller share is enough.
        if (cjk > 0 && cjk * 3 >= cyr + latin) return Chinese;
        if (cyr > latin) return ukOnly > ruOnly ? Ukrainian : Russian;
        return English;
    }
}
