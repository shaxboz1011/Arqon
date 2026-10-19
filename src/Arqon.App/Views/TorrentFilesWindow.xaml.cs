using System.Windows;
using Arqon.App.Infrastructure;
using Arqon.Core.Torrents;

namespace Arqon.App.Views;

/// <summary>One row of the file list: a file of the torrent and whether it is going to be downloaded.</summary>
public sealed class TorrentFileChoice(TorrentFileInfo file, bool isSelected)
{
    public int Index => file.Index;
    public string Path => file.Path;
    public string SizeText => Units.Size(file.Size);
    public long Size => file.Size;
    public bool IsSelected { get; set; } = isSelected;
}

/// <summary>Lets the user choose which files of a multi-file torrent to download.</summary>
public partial class TorrentFilesWindow : Window
{
    private readonly List<TorrentFileChoice> _rows;

    public TorrentFilesWindow(string torrentName, IReadOnlyList<TorrentFileInfo> files, IReadOnlySet<int>? selected)
    {
        InitializeComponent();
        _rows = files.Select(f => new TorrentFileChoice(f, selected is null || selected.Contains(f.Index))).ToList();
        TorrentName.Text = torrentName;
        List.ItemsSource = _rows;
        Refresh();
    }

    /// <summary>The indexes chosen in the window (set when it was accepted).</summary>
    public IReadOnlySet<int> Selected { get; private set; } = new HashSet<int>();

    private void Refresh()
    {
        var chosen = _rows.Where(r => r.IsSelected).ToList();
        Summary.Text = Loc.Format("S.Dl.Files.Summary", chosen.Count, _rows.Count, Units.Size(chosen.Sum(r => r.Size)));
        Ok.IsEnabled = chosen.Count > 0;   // a torrent with nothing to download makes no sense
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        // The binding may not have written the new value back yet when this event arrives, so the box itself is read.
        if (sender is System.Windows.Controls.CheckBox { DataContext: TorrentFileChoice row } box) row.IsSelected = box.IsChecked == true;
        Refresh();
    }

    private void SetAll(bool value)
    {
        foreach (var r in _rows) r.IsSelected = value;
        List.Items.Refresh();
        Refresh();
    }

    private void SelectAll(object sender, RoutedEventArgs e) => SetAll(true);

    private void SelectNone(object sender, RoutedEventArgs e) => SetAll(false);

    private void Accept(object sender, RoutedEventArgs e)
    {
        Selected = _rows.Where(r => r.IsSelected).Select(r => r.Index).ToHashSet();
        DialogResult = true;
    }
}
