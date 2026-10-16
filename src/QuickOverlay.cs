using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

using Point = System.Windows.Point;
using Path = System.IO.Path;
using DataObject = System.Windows.DataObject;
using DragDrop = System.Windows.DragDrop;

namespace FloatShot;

/// <summary>
/// Compact, image-first Quick Access Overlay. The image occupies the whole card;
/// the controls fade in over it only while hovering or using the keyboard.
/// Native FileDrop dragging works from every part of the picture not covered by a button.
/// </summary>
internal sealed class QuickOverlay : Window
{
    public const int MaximumVisible = 6;
    public const double StackGap = 10;
    public const double NaturalHeight = 165;
    public const double MinimumHeight = 78;

    private string _path;
    private readonly BitmapSource _image;
    private readonly Border _card;
    private readonly Grid _hoverLayer;
    private readonly StackPanel _middleActions;
    private readonly StackPanel _bottomActions;
    private readonly TextBlock _nameText;
    private readonly Forms.Screen _captureScreen;
    private Point? _dragStart;
    private bool _pinned;
    private bool _closing;

    public bool IsClosed { get; private set; }
    public bool IsPinned => _pinned;
    public Forms.Screen CaptureScreen => _captureScreen;
    public double PreferredHeight { get; }

    public QuickOverlay(string path)
    {
        _path = path;
        _image = Captures.Load(path);
        _captureScreen = Forms.Screen.FromPoint(Forms.Cursor.Position);

        double aspect = _image.PixelWidth / (double)Math.Max(1, _image.PixelHeight);
        PreferredHeight = Math.Clamp(240.0 / aspect, 86, NaturalHeight);
        Width = Math.Clamp(PreferredHeight * aspect, 92, 240);
        Height = PreferredHeight;
        MinWidth = 75;
        MinHeight = MinimumHeight;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        SnapsToDevicePixels = true;

        _card = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            BorderBrush = Solid("#3B526579"),
            Background = new ImageBrush(_image)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            },
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                ShadowDepth = 5,
                BlurRadius = 18,
                Opacity = 0.36
            },
            Cursor = Cursors.Hand
        };

        // The scrim is decorative only, so dragging the image stays possible.
        var root = new Grid();
        var hoverScrim = new Border
        {
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(176, 12, 19, 29), 0),
                    new GradientStop(Color.FromArgb(113, 15, 21, 31), 0.48),
                    new GradientStop(Color.FromArgb(174, 12, 19, 29), 1)
                },
                new Point(0, 0), new Point(0, 1)),
            CornerRadius = new CornerRadius(16),
            IsHitTestVisible = false
        };

        _hoverLayer = new Grid { Opacity = 0, IsHitTestVisible = false };
        _hoverLayer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _hoverLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _hoverLayer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRowSpan(hoverScrim, 3);
        _hoverLayer.Children.Add(hoverScrim);

        var top = new Grid { Margin = new Thickness(7, 6, 7, 0) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var close = IconButton("✕", "Dismiss thumbnail (screenshot stays in Desktop folder)", () => CloseSmoothly());
        top.Children.Add(close);

        var label = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _nameText = new TextBlock
        {
            Text = Path.GetFileNameWithoutExtension(_path),
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Medium,
            FontSize = 10,
            MaxWidth = 100,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Path.GetFileName(_path)
        };
        label.Children.Add(_nameText);
        label.Children.Add(IconButton("✎", "Rename screenshot", RenameCapture, 23));
        Grid.SetColumn(label, 1);
        top.Children.Add(label);
        var pin = IconButton("📌", "Pin screenshot to screen", TogglePin);
        Grid.SetColumn(pin, 2);
        top.Children.Add(pin);
        Grid.SetRow(top, 0);
        _hoverLayer.Children.Add(top);

        _middleActions = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _middleActions.Children.Add(PillButton("Copy", Copy, "Copy image, then use Ctrl+V"));
        _middleActions.Children.Add(PillButton("Save", SaveToDesktop, "Save in Desktop/FloatShot Captures"));
        Grid.SetRow(_middleActions, 1);
        _hoverLayer.Children.Add(_middleActions);

        _bottomActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 5)
        };
        _bottomActions.Children.Add(IconButton("✎", "Edit / redact / highlight / circle", OpenAnnotation, 25));
        _bottomActions.Children.Add(IconButton("▣", "Open screenshots folder", Captures.OpenFolder, 25));
        Grid.SetRow(_bottomActions, 2);
        _hoverLayer.Children.Add(_bottomActions);
        root.Children.Add(_hoverLayer);
        _card.Child = root;
        Content = _card;

        _card.MouseEnter += (_, _) => ShowHover(true);
        _card.MouseLeave += (_, _) => ShowHover(false);
        _card.PreviewMouseLeftButtonDown += BeginFileDrag;
        _card.PreviewMouseMove += UpdateFileDrag;
        _card.PreviewMouseLeftButtonUp += (_, _) => _dragStart = null;
        _card.MouseRightButtonUp += (_, e) =>
        {
            if (!IsOverControl(e.OriginalSource as DependencyObject)) OpenAnnotation();
        };
        Closed += (_, _) =>
        {
            IsClosed = true;
            App.CurrentApp.RepositionVisibleOverlays();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !_pinned) CloseSmoothly();
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C) Copy();
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) SaveToDesktop();
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E) OpenAnnotation();
        };
        Opacity = 0;
        Loaded += (_, _) =>
        {
            var intro = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(165)) { EasingFunction = Ease() };
            BeginAnimation(OpacityProperty, intro);
        };
    }


    private static SolidColorBrush Solid(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static CubicEase Ease() => new() { EasingMode = EasingMode.EaseOut };

    private static System.Windows.Controls.Button IconButton(string glyph, string tip, Action action, double size = 27)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = glyph,
            Width = size,
            Height = size,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Background = Solid("#E8EDF4"),
            Foreground = Solid("#222A36"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 2, 0),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Template = RoundedButtonTemplate(size / 2)
        };
        var idle = button.Background;
        button.MouseEnter += (_, _) => button.Background = Solid("#FFFFFF");
        button.MouseLeave += (_, _) => button.Background = idle;
        button.Click += (_, _) => action();
        return button;
    }

    private static System.Windows.Controls.Button PillButton(string label, Action action, string tip)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = label,
            Width = 79,
            Height = 29,
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Background = Solid("#F2F5F8"),
            Foreground = Solid("#1B222D"),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Template = RoundedButtonTemplate(14)
        };
        var idle = button.Background;
        button.MouseEnter += (_, _) => button.Background = Solid("#FFFFFF");
        button.MouseLeave += (_, _) => button.Background = idle;
        button.Click += (_, _) => action();
        return button;
    }

    private static ControlTemplate RoundedButtonTemplate(double radius)
    {
        var t = new ControlTemplate(typeof(System.Windows.Controls.Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        t.VisualTree = border;
        return t;
    }

    private void ShowHover(bool show)
    {
        if (_closing) return;
        if (show) _hoverLayer.IsHitTestVisible = true;
        var animation = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 125 : 155))
        {
            EasingFunction = Ease()
        };
        if (!show)
        {
            animation.Completed += (_, _) =>
            {
                if (!_card.IsMouseOver) _hoverLayer.IsHitTestVisible = false;
            };
        }
        _hoverLayer.BeginAnimation(OpacityProperty, animation);
    }

    private static bool IsOverControl(DependencyObject? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is System.Windows.Controls.Button || current is System.Windows.Controls.TextBox) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void BeginFileDrag(object sender, MouseButtonEventArgs e)
    {
        if (IsOverControl(e.OriginalSource as DependencyObject))
        {
            _dragStart = null;
            return;
        }
        if (e.ClickCount == 2)
        {
            _dragStart = null;
            OpenAnnotation();
            e.Handled = true;
            return;
        }
        _dragStart = e.GetPosition(this);
    }

    private void UpdateFileDrag(object sender, MouseEventArgs e)
    {
        if (!_dragStart.HasValue || e.LeftButton != MouseButtonState.Pressed) return;
        Point now = e.GetPosition(this);
        if (Math.Abs(now.X - _dragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(now.Y - _dragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragStart = null;
        if (!File.Exists(_path))
        {
            MessageBox.Show("The screenshot file no longer exists.", "FloatShot");
            return;
        }
        try
        {
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { Path.GetFullPath(_path) }, true);
            // On a successful drop, retire this particular floating card.
            // On Esc/cancel or unsupported targets, keep it visible.
            var effect = DragDrop.DoDragDrop(_card, data, DragDropEffects.Copy);
            if ((effect & DragDropEffects.Copy) != 0) CloseSmoothly();
        }
        catch (Exception ex) { App.Error(ex); }
    }

    // App lays out the cards as non-overlapping vertical slots. Pinned images
    // retain their chosen location and do not consume slots in the stack.
    public void PositionAt(double left, double top, double height, bool animate)
    {
        if (_pinned || IsClosed || _closing) return;
        double ratio = _image.PixelWidth / (double)Math.Max(1, _image.PixelHeight);
        Height = height;
        Width = Math.Clamp(height * ratio, 92, 240);
        _bottomActions.Visibility = height < 128 ? Visibility.Collapsed : Visibility.Visible;
        _nameText.Visibility = Width < 175 ? Visibility.Collapsed : Visibility.Visible;
        _nameText.MaxWidth = Math.Max(28, Width - 110);
        bool narrow = height < 112;
        foreach (var element in _middleActions.Children)
        {
            if (element is System.Windows.Controls.Button button)
            {
                button.Height = narrow ? 23 : 29;
                button.Width = narrow ? 66 : 79;
                button.FontSize = narrow ? 11 : 13;
                button.Margin = new Thickness(0, narrow ? 1 : 2, 0, narrow ? 1 : 2);
            }
        }
        Left = left + (240 - Width);
        if (!animate || Math.Abs(Top - top) < 0.8)
        {
            BeginAnimation(TopProperty, null);
            Top = top;
        }
        else
        {
            var start = Top;
            BeginAnimation(TopProperty, null);
            Top = top;
            var move = new DoubleAnimation(start, top, TimeSpan.FromMilliseconds(225)) { EasingFunction = Ease() };
            BeginAnimation(TopProperty, move);
        }
    }

    public void CloseSmoothly()
    {
        if (_closing || IsClosed) return;
        _closing = true;
        IsHitTestVisible = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(145)) { EasingFunction = Ease() };
        fade.Completed += (_, _) => { if (!IsClosed) Close(); };
        BeginAnimation(OpacityProperty, fade);
        var slide = new DoubleAnimation(Top, Top - 18, TimeSpan.FromMilliseconds(145)) { EasingFunction = Ease() };
        BeginAnimation(TopProperty, slide);
    }

    private void Copy()
    {
        try
        {
            Clipboard.SetImage(_image);
            CloseSmoothly();
        }
        catch (Exception ex) { App.Error(ex); }
    }

    private void SaveToDesktop()
    {
        try
        {
            // New captures are already safely persisted in Desktop/FloatShot Captures.
            // Restored external files get a local copy, without opening any dialog.
            if (!Path.GetFullPath(_path).StartsWith(Path.GetFullPath(Captures.Folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                string output = Captures.BuildUniquePath(Path.GetFileNameWithoutExtension(_path));
                File.Copy(_path, output);
                _path = output;
            }
            else if (!File.Exists(_path)) throw new FileNotFoundException("Screenshot file missing", _path);
            CloseSmoothly();
        }
        catch (Exception ex) { App.Error(ex); }
    }

    private void OpenAnnotation()
    {
        if (!File.Exists(_path)) { App.Error(new FileNotFoundException("Screenshot file missing", _path)); return; }
        var editor = new AnnotationWindow(_path);
        editor.Show();
        editor.Activate();
    }

    private void TogglePin()
    {
        _pinned = !_pinned;
        if (_pinned)
        {
            Height = Math.Min(235, SystemParameters.WorkArea.Height * 0.35);
            Width = Math.Min(360, Math.Max(140, Height * _image.PixelWidth / (double)Math.Max(1, _image.PixelHeight)));
            var work = SystemParameters.WorkArea;
            Left = Math.Clamp(Left, work.Left + 8, Math.Max(work.Left + 8, work.Right - Width - 8));
            Top = Math.Clamp(Top, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - Height - 8));
        }
        App.CurrentApp.RepositionVisibleOverlays();
    }

    private void RenameCapture()
    {
        try
        {
            string name = Path.GetFileNameWithoutExtension(_path);
            string? edited = Ui.PromptForText("Rename screenshot", "Screenshot name", name);
            if (string.IsNullOrWhiteSpace(edited) || edited.Equals(name, StringComparison.Ordinal)) return;
            _path = Captures.RenameFile(_path, edited);
            _nameText.Text = Path.GetFileNameWithoutExtension(_path);
            _nameText.ToolTip = Path.GetFileName(_path);
        }
        catch (Exception ex) { App.Error(ex); }
    }
}
