using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;

using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace FloatShot;

internal sealed class CaptureSelection : Window
{
    public event Action<BitmapSource?>? Completed;
    public Int32Rect? SelectedPixelBounds { get; private set; }
    private readonly BitmapSource _desktop;
    private readonly Canvas _canvas;
    private readonly Rectangle _selection;
    private Point? _start;

    public CaptureSelection()
    {
        _desktop = Captures.CaptureVirtualDesktop();
        var bounds = Forms.SystemInformation.VirtualScreen;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        _canvas = new Canvas { Background = new ImageBrush(_desktop) { Stretch = Stretch.Fill } };
        Content = _canvas;
        _selection = new Rectangle
        {
            Stroke = Brushes.White, StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 5, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(45, 70, 160, 255)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(_selection);
        var label = new TextBlock
        {
            Text = "DRAG TO CAPTURE  ·  ESC TO CANCEL", Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(215, 18, 23, 35)),
            FontSize = 13, FontWeight = FontWeights.SemiBold, Padding = new Thickness(16, 10, 16, 10)
        };
        Canvas.SetTop(label, 24);
        Canvas.SetLeft(label, 24);
        _canvas.Children.Add(label);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseRightButtonDown += (_, _) => Close();
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        Loaded += (_, _) => Focus();
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(_canvas);
        Mouse.Capture(_canvas);
        _selection.Visibility = Visibility.Visible;
        UpdateSelection(_start.Value);
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_start.HasValue) UpdateSelection(e.GetPosition(_canvas));
    }

    private void UpdateSelection(Point end)
    {
        if (!_start.HasValue) return;
        double x1 = Math.Clamp(Math.Min(_start.Value.X, end.X), 0, _canvas.ActualWidth);
        double y1 = Math.Clamp(Math.Min(_start.Value.Y, end.Y), 0, _canvas.ActualHeight);
        double x2 = Math.Clamp(Math.Max(_start.Value.X, end.X), 0, _canvas.ActualWidth);
        double y2 = Math.Clamp(Math.Max(_start.Value.Y, end.Y), 0, _canvas.ActualHeight);
        Canvas.SetLeft(_selection, x1);
        Canvas.SetTop(_selection, y1);
        _selection.Width = x2 - x1;
        _selection.Height = y2 - y1;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_start.HasValue) return;
        UpdateSelection(e.GetPosition(_canvas));
        _start = null;
        Mouse.Capture(null);
        if (_selection.Width < 5 || _selection.Height < 5) { Close(); return; }
        try
        {
            double sx = _desktop.PixelWidth / _canvas.ActualWidth;
            double sy = _desktop.PixelHeight / _canvas.ActualHeight;
            int x = Math.Clamp((int)Math.Round(Canvas.GetLeft(_selection) * sx), 0, _desktop.PixelWidth - 1);
            int y = Math.Clamp((int)Math.Round(Canvas.GetTop(_selection) * sy), 0, _desktop.PixelHeight - 1);
            int w = Math.Clamp((int)Math.Round(_selection.Width * sx), 1, _desktop.PixelWidth - x);
            int h = Math.Clamp((int)Math.Round(_selection.Height * sy), 1, _desktop.PixelHeight - y);
            var cropped = new CroppedBitmap(_desktop, new Int32Rect(x, y, w, h));
            cropped.Freeze();
            SelectedPixelBounds = new Int32Rect(x, y, w, h);
            Close();
            Completed?.Invoke(cropped);
        }
        catch (Exception ex) { Close(); App.Error(ex); }
    }
}
