using System.Windows.Media;
using Arqon.Core.Links;
using Arqon.Core.Monitoring;
using Arqon.Core.Policy;
using Arqon.Core.Settings;
using Arqon.Core.Windows;

namespace Arqon.App.Services;

/// <summary>Composition root: the one place that decides between real Windows services and the demo data set.</summary>
public sealed class AppServices
{
    public bool Demo { get; }
    public ILinkProvider Links { get; }
    public LinkMonitor Monitor { get; }
    public ISettingsStore SettingsStore { get; }
    public WindowsFixService Fix { get; }
    public ArqonSettings Settings { get; private set; }
    public RuleEngine Rules => new(Settings);

    public event Action? SettingsChanged;

    private AppServices(bool demo, ILinkProvider links, LinkMonitor monitor, ISettingsStore store, WindowsFixService fix)
    {
        Demo = demo;
        Links = links;
        Monitor = monitor;
        SettingsStore = store;
        Fix = fix;
        Settings = store.Load();
    }

    public static AppServices Create(bool demo)
    {
        if (demo)
        {
            var provider = new DemoLinkProvider();
            var monitor = new LinkMonitor(provider, new DemoProbe(), new DemoCounters(), TimeSpan.FromSeconds(1));
            return new AppServices(true, provider, monitor, new MemorySettingsStore(), new WindowsFixService(new DemoWcmPolicy()));
        }

        var links = new NetworkLinkProvider();
        var realMonitor = new LinkMonitor(links, new TcpConnectProbe(), new InterfaceCounterReader(), TimeSpan.FromSeconds(1));
        return new AppServices(false, links, realMonitor, new JsonSettingsStore(JsonSettingsStore.DefaultPath),
            new WindowsFixService(new RegistryWcmPolicy()));
    }

    public void SaveSettings(ArqonSettings settings)
    {
        Settings = settings;
        SettingsStore.Save(settings);
        SettingsChanged?.Invoke();
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        private ArqonSettings _s = ArqonSettings.Default() with
        {
            Rules =
            [
                new AppRule("steam.exe", AppKind.Downloader, LinkChoice.Only, "phone", 1_250_000),
                new AppRule("telegram.exe", AppKind.Normal, LinkChoice.Both, null, null),
                new AppRule("windowsupdate", AppKind.Background, LinkChoice.Only, "phone", null),
            ],
        };

        public ArqonSettings Load() => _s;
        public void Save(ArqonSettings settings) => _s = settings;
    }
}

public static class LinkPalette
{
    public static Color ColorFor(LinkKind kind) => kind switch
    {
        LinkKind.Wifi => Color.FromRgb(0x4A, 0xDE, 0x80),
        LinkKind.Mobile => Color.FromRgb(0xF5, 0x9E, 0x0B),
        LinkKind.Ethernet => Color.FromRgb(0x6C, 0x8C, 0xFF),
        _ => Color.FromRgb(0x8B, 0x93, 0xA7),
    };

    public static string GlyphFor(LinkKind kind) => kind switch
    {
        LinkKind.Wifi => "",
        LinkKind.Mobile => "",
        LinkKind.Ethernet => "",
        _ => "",
    };
}
