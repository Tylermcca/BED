namespace ImCode.Languages;

public sealed class LanguageRegistry
{
    readonly List<Language> _languages = [];

    public IReadOnlyList<Language> Languages => _languages;
    public Language Fallback { get; set; } = PlainTextLanguage.Instance;

    /// <summary>Minimum <see cref="Language.MatchScore"/> for content based detection.</summary>
    public double MinMatchScore { get; set; } = 0.3;

    public static LanguageRegistry CreateDefault()
    {
        var registry = new LanguageRegistry();
        registry.Register(PlainTextLanguage.Instance);
        registry.Register(CSharpLanguage.Create());
        registry.Register(new MarkdownLanguage());
        return registry;
    }

    public void Register(Language language)
    {
        _languages.RemoveAll(l => l.Name == language.Name);
        _languages.Add(language);
    }

    public Language? FindByName(string name) =>
        _languages.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    public Language? FindByExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext.Length == 0 ? null : _languages.FirstOrDefault(l => l.FileExtensions.Contains(ext));
    }

    /// <summary>Language by file extension, then by content, else <see cref="Fallback"/>.</summary>
    public Language Detect(string? fileName, string text)
    {
        if (fileName != null && FindByExtension(fileName) is { } byExtension)
            return byExtension;

        Language? best = null;
        double bestScore = MinMatchScore;
        foreach (var language in _languages)
        {
            var score = language.MatchScore(text);
            if (score >= bestScore)
            {
                best = language;
                bestScore = score;
            }
        }
        return best ?? Fallback;
    }
}
