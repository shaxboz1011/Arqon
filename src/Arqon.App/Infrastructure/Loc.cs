using System.Globalization;
using System.Windows;

namespace Arqon.App.Infrastructure;

/// <summary>Runtime language switching. Strings live in Resources/Strings.{lang}.xaml and are used through DynamicResource.</summary>
public static class Loc
{
    private static ResourceDictionary? _current;

    public static event Action? Changed;

    public static string Language { get; private set; } = "uz";

    public static void Apply(string language)
    {
        var lang = language is "en" or "uz" or "ru" ? language : "uz";
        var dict = new ResourceDictionary { Source = new Uri($"/Arqon.App;component/Resources/Strings.{lang}.xaml", UriKind.Relative) };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Add(dict);
        _current = dict;
        Language = lang;
        Changed?.Invoke();
    }

    public static string Get(string key) =>
        Application.Current?.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
