using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FloatShot;

internal sealed class HistoryWindow : Window
{
    private readonly StackPanel _items;
    public HistoryWindow()
    {
        Title = "FloatShot • Capture history";
        Width = 680;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Darker;
        Foreground = Ui.Light;
        var root = new DockPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 16, 18, 16) };
        header.Children.Add(Ui.Text("Capture history", 19, true));
        header.Children.Add(Ui.Button("Open folder", Captures.OpenFolder));
        header.Children.Add(Ui.Button("Refresh", Reload));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        _items = new StackPanel { Margin = new Thickness(14, 4, 14, 14) };
        root.Children.Add(new ScrollViewer { Content = _items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Reload();
    }
    private void Reload()
    {
        _items.Children.Clear();
        if (!Directory.Exists(Captures.Folder)) return;
        var files = Directory.GetFiles(Captures.Folder, "*.png").OrderByDescending(File.GetCreationTimeUtc).Take(100).ToArray();
        if (files.Length == 0) { _items.Children.Add(Ui.Text("No screenshots yet. Press Ctrl+Shift+2 to create one.")); return; }
        foreach (string path in files)
        {
            try
            {
                var row = new DockPanel { Margin = new Thickness(3, 5, 3, 5), Background = Ui.Dark, Height = 92, LastChildFill = true };
                var img = new Image { Source = Captures.Load(path), Width = 120, Margin = new Thickness(7), Stretch = Stretch.Uniform };
                DockPanel.SetDock(img, Dock.Left); row.Children.Add(img);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                buttons.Children.Add(Ui.Button("Restore", () => App.CurrentApp.ShowQuickOverlay(path)));
                buttons.Children.Add(Ui.Button("Copy", () => Clipboard.SetImage(Captures.Load(path))));
                buttons.Children.Add(Ui.Button("Edit", () => new AnnotationWindow(path).Show()));
                buttons.Children.Add(Ui.Button("Delete", () => Delete(path)));
                DockPanel.SetDock(buttons, Dock.Right); row.Children.Add(buttons);
                row.Children.Add(new TextBlock { Text = Path.GetFileName(path), Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                _items.Children.Add(row);
            }
            catch { /* ignore corrupt/missing individual files */ }
        }
    }
    private void Delete(string path)
    {
        if (MessageBox.Show("Delete this capture from history?", "FloatShot", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { File.Delete(path); Reload(); } catch (Exception ex) { App.Error(ex); }
    }
}
