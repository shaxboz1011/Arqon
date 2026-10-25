using System.Text.Json;

namespace Arqon.Core.Tests.Localization;

/// <summary>The browser extension (browser-extension/) is plain files; these checks catch a broken manifest or a missing translation.</summary>
public sealed class ExtensionFilesTests
{
    private static string ExtensionDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Arqon.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "browser-extension");
    }

    private static JsonElement Read(params string[] path) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine([ExtensionDirectory(), .. path]))).RootElement;

    private static HashSet<string> Keys(string language) =>
        Read("_locales", language, "messages.json").EnumerateObject().Select(p => p.Name).ToHashSet();

    [Theory]
    [InlineData("uz")]
    [InlineData("ru")]
    public void Every_language_has_exactly_the_english_messages(string language)
    {
        Assert.Equal(Keys("en").Order(), Keys(language).Order());
    }

    [Fact]
    public void The_manifest_uses_only_messages_and_files_that_exist()
    {
        var manifest = Read("manifest.json");
        var english = Keys("en");

        Assert.Equal(3, manifest.GetProperty("manifest_version").GetInt32());
        foreach (var field in new[] { "name", "description" })
        {
            var text = manifest.GetProperty(field).GetString()!;
            Assert.StartsWith("__MSG_", text);
            Assert.Contains(text["__MSG_".Length..^2], english);
        }
        foreach (var icon in manifest.GetProperty("icons").EnumerateObject())
            Assert.True(File.Exists(Path.Combine(ExtensionDirectory(), icon.Value.GetString()!)), icon.Value.GetString());
        Assert.True(File.Exists(Path.Combine(ExtensionDirectory(), manifest.GetProperty("background").GetProperty("service_worker").GetString()!)));
    }

    [Fact]
    public void The_extension_speaks_the_scheme_arqon_understands()
    {
        var script = File.ReadAllText(Path.Combine(ExtensionDirectory(), "background.js"));

        Assert.Contains("\"arqon://add?url=\"", script);
        Assert.StartsWith("arqon://add?url=", Arqon.Core.Download.ArqonLink.Build("https://x"));
    }
}
