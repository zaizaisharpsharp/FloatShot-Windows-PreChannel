using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Rectangle = System.Windows.Shapes.Rectangle;
using Ellipse = System.Windows.Shapes.Ellipse;
using PathIO = System.IO.Path;

namespace FloatShot;

internal sealed class AnnotationWindow : Window
{
    private enum Tool { Pen, Highlight, Rectangle, Ellipse, Arrow, Text, Redact }
    private readonly string _path;
    private readonly BitmapSource _original;
    private readonly InkCanvas _ink;
    private readonly List<UIElement> _shapes = new();
    private Tool _tool = Tool.Pen;
    private Point? _shapeStart;
    private Shape? _activeShape;

    public AnnotationWindow(string path)
    {
        _path = path;
        _original = Captures.Load(path);
        Title = "FloatShot • Edit Screenshot";
        Width = Math.Min(SystemParameters.WorkArea.Width * 0.90, Math.Max(820, _original.PixelWidth + 90));
        Height = Math.Min(SystemParameters.WorkArea.Height * 0.88, Math.Max(580, _original.PixelHeight + 140));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Dark;
        Foreground = Ui.Light;

        var layout = new DockPanel { LastChildFill = true };
        var bar = new WrapPanel { Margin = new Thickness(12, 10, 12, 10), ItemHeight = 36 };
        bar.Children.Add(Ui.Text("EDIT  ", 16, true));
        bar.Children.Add(Ui.Button("Pen", () => SetTool(Tool.Pen)));
        bar.Children.Add(Ui.Button("Highlight", () => SetTool(Tool.Highlight)));
        bar.Children.Add(Ui.Button("Redact", () => SetTool(Tool.Redact)));
        bar.Children.Add(Ui.Button("Rectangle", () => SetTool(Tool.Rectangle)));
        bar.Children.Add(Ui.Button("Circle", () => SetTool(Tool.Ellipse)));
        bar.Children.Add(Ui.Button("Arrow", () => SetTool(Tool.Arrow)));
        bar.Children.Add(Ui.Button("Text", () => SetTool(Tool.Text)));
        bar.Children.Add(Ui.Button("Undo", Undo));
        bar.Children.Add(Ui.Button("Copy", CopyEdited));
        bar.Children.Add(Ui.Button("Save", SaveEdited));
        bar.Children.Add(Ui.Button("Drop", DragEdited, "Create a new edited PNG and show it as a floating card"));
        DockPanel.SetDock(bar, Dock.Top);
        layout.Children.Add(bar);

        _ink = new InkCanvas
        {
            Width = _original.PixelWidth,
            Height = _original.PixelHeight,
            Background = new ImageBrush(_original) { Stretch = Stretch.Fill },
            EditingMode = InkCanvasEditingMode.Ink,
            DefaultDrawingAttributes = new DrawingAttributes { Color = Colors.Red, Width = 3, Height = 3, FitToCurve = true }
        };
        _ink.MouseLeftButtonDown += InkDown;
        _ink.MouseMove += InkMove;
        _ink.MouseLeftButtonUp += InkUp;
        layout.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Ui.Darker,
            Content = _ink
        });
        Content = layout;
    }

    private void SetTool(Tool tool)
    {
        _tool = tool;
        _ink.EditingMode = (tool == Tool.Pen || tool == Tool.Highlight) ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        var attrs = new DrawingAttributes
        {
            Color = tool == Tool.Highlight ? Colors.Yellow : Colors.Red,
            Width = tool == Tool.Highlight ? 22 : 3,
            Height = tool == Tool.Highlight ? 22 : 3,
            FitToCurve = true,
            IsHighlighter = tool == Tool.Highlight
        };
        _ink.DefaultDrawingAttributes = attrs;
        _ink.Cursor = tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;
    }

    private void InkDown(object sender, MouseButtonEventArgs e)
    {
        if (_tool is Tool.Pen or Tool.Highlight) return;
        Point p = e.GetPosition(_ink);
        if (_tool == Tool.Text)
        {
            var editor = new TextBox
            {
                Text = "Text", Width = 220, MinHeight = 32, FontSize = 24, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Red, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                AcceptsReturn = false
            };
            InkCanvas.SetLeft(editor, p.X);
            InkCanvas.SetTop(editor, p.Y);
            _ink.Children.Add(editor);
            _shapes.Add(editor);
            editor.Focus();
            editor.SelectAll();
            e.Handled = true;
            return;
        }

        _shapeStart = p;
        _activeShape = _tool switch
        {
            Tool.Rectangle => new Rectangle { Stroke = Brushes.Red, StrokeThickness = 3, Fill = Brushes.Transparent, Width = 1, Height = 1, IsHitTestVisible = false },
            Tool.Ellipse => new Ellipse { Stroke = Brushes.Red, StrokeThickness = 3, Fill = Brushes.Transparent, Width = 1, Height = 1, IsHitTestVisible = false },
            Tool.Redact => new Rectangle { Stroke = Brushes.Black, StrokeThickness = 1, Fill = Brushes.Black, Width = 1, Height = 1, IsHitTestVisible = false },
            _ => new Polyline { Stroke = Brushes.Red, StrokeThickness = 3, IsHitTestVisible = false, Points = new PointCollection { p, p } }
        };
        InkCanvas.SetLeft(_activeShape, (_tool == Tool.Rectangle || _tool == Tool.Ellipse || _tool == Tool.Redact) ? p.X : 0);
        InkCanvas.SetTop(_activeShape, (_tool == Tool.Rectangle || _tool == Tool.Ellipse || _tool == Tool.Redact) ? p.Y : 0);
        _ink.Children.Add(_activeShape);
        _shapes.Add(_activeShape);
        Mouse.Capture(_ink);
        e.Handled = true;
    }

    private void InkMove(object sender, MouseEventArgs e)
    {
        if (!_shapeStart.HasValue || _activeShape is null) return;
        var start = _shapeStart.Value;
        var end = e.GetPosition(_ink);
        if (_activeShape is Rectangle rect)
        {
            InkCanvas.SetLeft(rect, Math.Min(start.X, end.X));
            InkCanvas.SetTop(rect, Math.Min(start.Y, end.Y));
            rect.Width = Math.Max(1, Math.Abs(end.X - start.X));
            rect.Height = Math.Max(1, Math.Abs(end.Y - start.Y));
        }
        else if (_activeShape is Ellipse ellipse)
        {
            InkCanvas.SetLeft(ellipse, Math.Min(start.X, end.X));
            InkCanvas.SetTop(ellipse, Math.Min(start.Y, end.Y));
            ellipse.Width = Math.Max(1, Math.Abs(end.X - start.X));
            ellipse.Height = Math.Max(1, Math.Abs(end.Y - start.Y));
        }
        else if (_activeShape is Polyline arrow)
        {
            double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
            double wing = 18, tilt = Math.PI / 6;
            var p1 = new Point(end.X - wing * Math.Cos(angle - tilt), end.Y - wing * Math.Sin(angle - tilt));
            var p2 = new Point(end.X - wing * Math.Cos(angle + tilt), end.Y - wing * Math.Sin(angle + tilt));
            arrow.Points = new PointCollection { start, end, p1, end, p2 };
        }
    }

    private void InkUp(object sender, MouseButtonEventArgs e)
    {
        if (_shapeStart.HasValue) InkMove(sender, e);
        _shapeStart = null;
        _activeShape = null;
        if (Mouse.Captured == _ink) Mouse.Capture(null);
    }

    private void Undo()
    {
        if (_shapes.Count > 0)
        {
            var last = _shapes[^1];
            _shapes.RemoveAt(_shapes.Count - 1);
            _ink.Children.Remove(last);
        }
        else if (_ink.Strokes.Count > 0)
        {
            _ink.Strokes.RemoveAt(_ink.Strokes.Count - 1);
        }
    }

    private BitmapSource RenderEdited()
    {
        Keyboard.ClearFocus();
        _ink.Measure(new Size(_original.PixelWidth, _original.PixelHeight));
        _ink.Arrange(new Rect(0, 0, _original.PixelWidth, _original.PixelHeight));
        _ink.UpdateLayout();
        var bitmap = new RenderTargetBitmap(_original.PixelWidth, _original.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_ink);
        bitmap.Freeze();
        return bitmap;
    }

    private void CopyEdited()
    {
        try { Clipboard.SetImage(RenderEdited()); }
        catch (Exception ex) { App.Error(ex); }
    }

    private void SaveEdited()
    {
        try
        {
            var edited = RenderEdited();
            string name = PathIO.GetFileNameWithoutExtension(_path) + "_edited_" + DateTime.Now.ToString("HH-mm-ss");
            // One-click save: no Save As dialog. The user's Desktop library already exists.
            Captures.SaveNewCapture(edited, name);
            Clipboard.SetImage(edited);
            Close();
        }
        catch (Exception ex) { App.Error(ex); }
    }

    private void DragEdited()
    {
        try
        {
            var edited = RenderEdited();
            string name = PathIO.GetFileNameWithoutExtension(_path) + "_edited_" + DateTime.Now.ToString("HH-mm-ss");
            string path = Captures.SaveNewCapture(edited, name);
            Clipboard.SetImage(edited);
            App.CurrentApp.ShowQuickOverlay(path);
            Close();
        }
        catch (Exception ex) { App.Error(ex); }
    }
}
