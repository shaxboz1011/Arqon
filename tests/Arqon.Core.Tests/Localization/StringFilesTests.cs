using System.Xml.Linq;

namespace Arqon.Core.Tests.Localization;

/// <summary>The interface texts live in Strings.{language}.xaml; every language must offer exactly the same keys, or a page shows a raw key.</summary>
public sealed class StringFilesTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string ResourcesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Arqon.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Arqon.App", "Resources");
    }

    private static Dictionary<string, string> Load(string language) =>
        XDocument.Load(Path.Combine(ResourcesDirectory(), $"Strings.{language}.xaml")).Root!.Elements()
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => e.Value);

    [Theory]
    [InlineData("uz")]
    [InlineData("ru")]
    public void A_language_has_exactly_the_keys_of_the_english_file(string language)
    {
        var english = Load("en");
        var other = Load(language);

        Assert.Empty(english.Keys.Except(other.Keys));
        Assert.Empty(other.Keys.Except(english.Keys));
    }

    [Theory]
    [InlineData("uz")]
    [InlineData("ru")]
    public void A_translation_keeps_the_placeholders_of_the_english_text(string language)
    {
        var english = Load("en");
        var other = Load(language);

        foreach (var (key, text) in english)
        {
            var placeholders = System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order().ToList();
            var translated = System.Text.RegularExpressions.Regex.Matches(other[key], @"\{\d+\}").Select(m => m.Value).Order().ToList();
            Assert.True(placeholders.SequenceEqual(translated), $"{language}: {key} has different placeholders");
        }
    }

    [Fact]
    public void No_translation_is_left_empty()
    {
        foreach (var language in new[] { "en", "uz", "ru" })
            Assert.DoesNotContain(Load(language), kv => string.IsNullOrWhiteSpace(kv.Value));
    }
}
