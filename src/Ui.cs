using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Button = System.Windows.Controls.Button;

namespace FloatShot;
internal static class Ui
{
    public static readonly SolidColorBrush Dark = Brush("#141923");
    public static readonly SolidColorBrush Darker = Brush("#0B0F17");
    public static readonly SolidColorBrush Light = Brush("#F7F9FF");
    public static readonly SolidColorBrush Muted = Brush("#A8B3C7");
    public static readonly SolidColorBrush Accent = Brush("#6B91FF");
    public static readonly SolidColorBrush Soft = Brush("#212938");

    public static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static Button Button(string name, Action action, string? tooltip = null)
    {
        var b = new Button
        {
            Content = name, MinWidth = 70, Height = 34, Padding = new Thickness(14, 4, 14, 4),
            Margin = new Thickness(4, 0, 4, 0), Background = Brush("#F0F2F8"), Foreground = Brush("#1A2030"),
            BorderBrush = Brush("#EEF2FF"), FontSize = 13, FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tooltip ?? name
        };
        b.Click += (_, _) => action();
        return b;
    }

    public static Button MiniButton(string label, Action action, string? tooltip = null)
    {
        var b = new Button
        {
            Content = label,
            Width = 28,
            Height = 28,
            Margin = new Thickness(3),
            Padding = new Thickness(0),
            Background = Brush("#EEF2F7"),
            Foreground = Brush("#202633"),
            BorderBrush = Brush("#D8DFEE"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = tooltip ?? label
        };
        b.Click += (_, _) => action();
        return b;
    }

    public static TextBlock Text(string txt, double size = 13, bool bold = false) => new()
    {
        Text = txt, Foreground = Light, FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center
    };

    public static string? PromptForText(string title, string prompt, string initialValue)
    {
        var window = new Window
        {
            Title = title,
            Width = 445,
            Height = 180,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Dark,
            Foreground = Light,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var label = Text(prompt, 14, true);
        root.Children.Add(label);
        var box = new TextBox
        {
            Text = initialValue,
            FontSize = 14,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 10, 0, 0),
            Background = Brush("#EFF2F8"),
            Foreground = Brush("#101522")
        };
        Grid.SetRow(box, 1);
        root.Children.Add(box);
        string? result = null;
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 12, 0, 0)
        };
        actions.Children.Add(Button("Cancel", () => window.DialogResult = false));
        actions.Children.Add(Button("OK", () => { result = box.Text.Trim(); window.DialogResult = true; }));
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        window.Content = root;
        window.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return window.ShowDialog() == true ? result : null;
    }
}
