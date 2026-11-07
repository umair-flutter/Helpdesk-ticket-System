using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Localization;

namespace Help_DeskTicket_System.Services;

public record LanguageInfo(string Code, string NativeName, bool Rtl);

/// <summary>
/// JSON-file localizer. Keys are the English text; Resources/Lang/{code}.json maps them to translations.
/// Missing keys fall back to English, so untranslated text never breaks a page.
/// </summary>
public class Lang : IStringLocalizer
{
    public static readonly LanguageInfo[] Languages =
    [
        new("en", "English", false),
        new("ar", "العربية", true),
        new("ur", "اردو", true),
        new("fa", "فارسی", true),
        new("hi", "हिन्दी", false),
        new("bn", "বাংলা", false),
        new("zh-CN", "中文", false),
        new("ja", "日本語", false),
        new("ko", "한국어", false),
        new("tr", "Türkçe", false),
        new("ru", "Русский", false),
        new("de", "Deutsch", false),
        new("fr", "Français", false),
        new("es", "Español", false),
        new("pt", "Português", false),
        new("id", "Bahasa Indonesia", false),
    ];

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Cache = new();
    private static string _root = "";

    public static void Init(string contentRoot) => _root = Path.Combine(contentRoot, "Resources", "Lang");

    public static LanguageInfo Current
    {
        get
        {
            var name = CultureInfo.CurrentUICulture.Name;
            return Languages.FirstOrDefault(l => l.Code.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? Languages.FirstOrDefault(l => l.Code.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
                ?? Languages[0];
        }
    }

    /// <summary>All supported cultures, forced onto the Gregorian calendar so dates and date inputs stay consistent.</summary>
    public static IList<CultureInfo> Cultures() => Languages.Select(l =>
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo(l.Code).Clone();
        if (c.DateTimeFormat.Calendar is not GregorianCalendar)
        {
            var greg = c.OptionalCalendars.OfType<GregorianCalendar>().FirstOrDefault();
            if (greg is not null) c.DateTimeFormat.Calendar = greg;
        }
        return c;
    }).ToList();

    private static IReadOnlyDictionary<string, string> Table(string code) => Cache.GetOrAdd(code, c =>
    {
        var file = Path.Combine(_root, $"{c}.json");
        if (!File.Exists(file)) return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? [];
    });

    /// <summary>Translate for the current request's UI language.</summary>
    public static string T(string key, params object?[] args)
    {
        var code = Current.Code;
        var text = code == "en" ? key : Table(code).GetValueOrDefault(key) is { Length: > 0 } v ? v : key;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    public LocalizedString this[string name] => new(name, T(name), Current.Code != "en" && !Table(Current.Code).ContainsKey(name));
    public LocalizedString this[string name, params object[] arguments] => new(name, T(name, arguments));

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        Table(Current.Code).Select(kv => new LocalizedString(kv.Key, kv.Value));

    /// <summary>Strings the browser scripts need.</summary>
    public static Dictionary<string, string> ClientStrings() => new[]
    {
        "Loading…", "No notifications yet.", "View all", "No matching articles.", "Delete selected tickets permanently?"
    }.ToDictionary(k => k, k => T(k));
}

/// <summary>Lets DataAnnotations ([Display], validation messages) use the same JSON tables.</summary>
public class LangFactory : IStringLocalizerFactory
{
    private static readonly Lang Instance = new();
    public IStringLocalizer Create(Type resourceSource) => Instance;
    public IStringLocalizer Create(string baseName, string location) => Instance;
}

/// <summary>Validation message keys (translated through <see cref="LangFactory"/>).</summary>
public static class V
{
    public const string Required = "The {0} field is required.";
    public const string Email = "The {0} field is not a valid email address.";
    public const string Phone = "The {0} field is not a valid phone number.";
    public const string MinLength = "{0} must be at least {1} characters.";
    public const string MaxLength = "{0} must be at most {1} characters.";
    public const string Compare = "{0} and {1} do not match.";
}
