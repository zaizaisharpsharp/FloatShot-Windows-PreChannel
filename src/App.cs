using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace FloatShot;

public sealed class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private Forms.NotifyIcon? _tray;
    private Window? _messageWindow;
    private HwndSource? _source;
    private readonly List<QuickOverlay> _overlays = new();
    private bool _capturing;
    private enum TrayAction { CaptureArea, ShowMenu, CaptureFullScreen, CaptureMonitor, CaptureWindow }
    private TrayAction _trayAction = TrayAction.CaptureArea;
    private static string PreferencesPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatShot", "tray-action.txt");
    private Forms.ContextMenuStrip? _trayMenu;
    private Updates? _updates;
    private Int32Rect? _previousRegion;
    internal static App CurrentApp => (App)Current;

    [STAThread]
    public static void Main()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (args.Length == 5 && args[1] == "--apply-update")
        {
            UpdateInstaller.Apply(args[2], args[3], args[4]);
            return;
        }
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, "FloatShot.SingleInstance.2026", out bool created);
        if (!created) { Shutdown(); return; }
        try
        {
            Directory.CreateDirectory(Captures.Folder);
            try
            {
                if (File.Exists(PreferencesPath) && Enum.TryParse(File.ReadAllText(PreferencesPath).Trim(), out TrayAction saved))
                    _trayAction = saved;
            }
            catch { /* ignore inaccessible or invalid preferences */ }
            var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "floatshot.ico");
            var icon = File.Exists(iconPath) ? new Drawing.Icon(iconPath) : Drawing.SystemIcons.Application;
            // Native Windows system-tray menu: no permanent taskbar window is required.
            var menu = new Forms.ContextMenuStrip { ShowImageMargin = true };
            _trayMenu = menu;
            menu.Items.Add("Capture Area                 Ctrl+Shift+2", null, (_, _) => Dispatcher.BeginInvoke(CaptureRegion));
            menu.Items.Add("Capture Previous Area", null, (_, _) => Dispatcher.BeginInvoke(CapturePreviousArea));
            menu.Items.Add("Capture Fullscreen (all monitors)    Ctrl+Shift+3", null, (_, _) => Dispatcher.BeginInvoke(CaptureFullScreen));
            menu.Items.Add("Capture Current Monitor", null, (_, _) => Dispatcher.BeginInvoke(CaptureMonitor));
            menu.Items.Add("Capture Active Window        Ctrl+Shift+4", null, (_, _) => Dispatcher.BeginInvoke(new Action(() => StartWindowCapture(2200))));
            var timerMenu = new Forms.ToolStripMenuItem("Self-Timer");
            timerMenu.DropDownItems.Add("3 seconds, then select area", null, (_, _) => Dispatcher.BeginInvoke(new Action(() => CaptureAfterDelay(3))));
            timerMenu.DropDownItems.Add("5 seconds, then select area", null, (_, _) => Dispatcher.BeginInvoke(new Action(() => CaptureAfterDelay(5))));
            menu.Items.Add(timerMenu);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Open Image...", null, (_, _) => Dispatcher.BeginInvoke(OpenImage));
            menu.Items.Add("Show Last Screenshot", null, (_, _) => Dispatcher.BeginInvoke(RestoreLast));
            menu.Items.Add("Capture History...", null, (_, _) => Dispatcher.BeginInvoke(ShowHistory));
            menu.Items.Add("Open Capture Folder", null, (_, _) => Captures.OpenFolder());
            menu.Items.Add(new Forms.ToolStripSeparator());
            var settings = new Forms.ToolStripMenuItem("Settings");
            var behaviorChoices = new (string Label, TrayAction Action)[]
            {
                ("Left-click: Capture Area", TrayAction.CaptureArea),
                ("Left-click: Open Screenshot Menu", TrayAction.ShowMenu),
                ("Left-click: Capture Fullscreen", TrayAction.CaptureFullScreen),
                ("Left-click: Capture Current Monitor", TrayAction.CaptureMonitor),
                ("Left-click: Capture Active Window (2s delay)", TrayAction.CaptureWindow)
            };
            var behaviorItems = new List<Forms.ToolStripMenuItem>();
            foreach (var option in behaviorChoices)
            {
                var item = new Forms.ToolStripMenuItem(option.Label) { Checked = _trayAction == option.Action };
                behaviorItems.Add(item);
                item.Click += (_, _) =>
                {
                    _trayAction = option.Action;
                    foreach (var entry in behaviorItems) entry.Checked = ReferenceEquals(entry, item);
                    try { File.WriteAllText(PreferencesPath, _trayAction.ToString()); }
                    catch (Exception ex) { Error(ex); }
                };
                settings.DropDownItems.Add(item);
            }
            menu.Items.Add(settings);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Quit FloatShot", null, (_, _) => Dispatcher.BeginInvoke(Shutdown));
            _tray = new Forms.NotifyIcon { Icon = icon, Text = "FloatShot - left-click to capture, right-click for menu", Visible = true, ContextMenuStrip = menu };
            _updates = new Updates(this, _tray);
            _updates.AddToTrayMenu(menu);
            _tray.MouseClick += (_, args) =>
            {
                if (args.Button != Forms.MouseButtons.Left) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    switch (_trayAction)
                    {
                        case TrayAction.ShowMenu: _trayMenu?.Show(Forms.Cursor.Position); break;
                        case TrayAction.CaptureFullScreen: CaptureFullScreen(); break;
                        case TrayAction.CaptureMonitor: CaptureMonitor(); break;
                        case TrayAction.CaptureWindow: StartWindowCapture(2200); break;
                        default: CaptureRegion(); break;
                    }
                }));
            };
            _messageWindow = new Window { Width = 1, Height = 1, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Opacity = 0, Left = -10000, Top = -10000 };
            _messageWindow.Show();
            var helper = new WindowInteropHelper(_messageWindow);
            _source = HwndSource.FromHwnd(helper.Handle);
            _source?.AddHook(WndProc);
            Native.RegisterHotKey(helper.Handle, 1, Native.MOD_CONTROL | Native.MOD_SHIFT, 0x32); // 2
            Native.RegisterHotKey(helper.Handle, 2, Native.MOD_CONTROL | Native.MOD_SHIFT, 0x33); // 3
            Native.RegisterHotKey(helper.Handle, 3, Native.MOD_CONTROL | Native.MOD_SHIFT, 0x34); // 4
            if (e.Args.Contains("--capture")) Dispatcher.BeginInvoke(CaptureRegion);
            _updates.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to start FloatShot:\n{ex.Message}", "FloatShot", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY)
        {
            handled = true;
            switch (wParam.ToInt32())
            {
                case 1: CaptureRegion(); break;
                case 2: CaptureFullScreen(); break;
                case 3: StartWindowCapture(200); break;
            }
        }
        return IntPtr.Zero;
    }

    internal bool CanRestartForUpdate => !_capturing && !_overlays.Any(o => !o.IsClosed) &&
        !Windows.OfType<AnnotationWindow>().Any(w => w.IsVisible) &&
        !Windows.OfType<HistoryWindow>().Any(w => w.IsVisible) &&
        !Windows.OfType<CaptureSelection>().Any(w => w.IsVisible);

    private void HideOverlays() { foreach (var o in _overlays.ToArray()) o.Hide(); }
    private void ShowOverlays() { foreach (var o in _overlays.ToArray()) if (!o.IsClosed) o.Show(); }

    public void CaptureRegion()
    {
        if (_capturing) return;
        _capturing = true;
        try
        {
            HideOverlays();
            var capture = new CaptureSelection();
            capture.Closed += (_, _) => { _capturing = false; ShowOverlays(); };
            capture.Completed += image =>
            {
                if (image == null) return;
                _previousRegion = capture.SelectedPixelBounds;
                AddCapture(image);
            };
            capture.Show();
            capture.Activate();
        }
        catch (Exception ex) { _capturing = false; ShowOverlays(); Error(ex); }
    }

    private void CapturePreviousArea()
    {
        if (_capturing) return;
        if (!_previousRegion.HasValue) { CaptureRegion(); return; }
        HideOverlays();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                var desktop = Captures.CaptureVirtualDesktop();
                var rect = _previousRegion.Value;
                if (rect.X < 0 || rect.Y < 0 || (long)rect.X + rect.Width > desktop.PixelWidth || (long)rect.Y + rect.Height > desktop.PixelHeight)
                    throw new InvalidOperationException("Display layout changed. Please select a new capture area.");
                var cropped = new CroppedBitmap(desktop, rect);
                cropped.Freeze();
                AddCapture(cropped);
            }
            catch (Exception ex) { Error(ex); }
            finally { ShowOverlays(); }
        }));
    }

    private void CaptureAfterDelay(int seconds)
    {
        if (_capturing) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) => { timer.Stop(); CaptureRegion(); };
        timer.Start();
    }

    private void OpenImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;
        try { ShowQuickOverlay(dialog.FileName); }
        catch (Exception ex) { Error(ex); }
    }

    public void CaptureFullScreen()
    {
        if (_capturing) return;
        try
        {
            HideOverlays();
            // Allow the hidden floating windows to leave the compositor.
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try { AddCapture(Captures.CaptureVirtualDesktop()); }
                catch (Exception ex) { Error(ex); }
                finally { ShowOverlays(); }
            }));
        }
        catch (Exception ex) { ShowOverlays(); Error(ex); }
    }

    public void CaptureMonitor()
    {
        if (_capturing) return;
        // Remember the monitor before any change to window focus.
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        HideOverlays();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try { AddCapture(Captures.CaptureMonitor(screen)); }
            catch (Exception ex) { Error(ex); }
            finally { ShowOverlays(); }
        }));
    }

    // Tray launch gives time to focus the target; hotkey launch captures it almost immediately.
    private void StartWindowCapture(int delayMilliseconds)
    {
        if (_capturing) return;
        HideOverlays();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMilliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try { AddCapture(Captures.CaptureForegroundWindow()); }
            catch (Exception ex) { Error(ex); }
            finally { ShowOverlays(); }
        };
        timer.Start();
    }

    internal void AddCapture(BitmapSource image)
    {
        try
        {
            var file = Captures.SaveNewCapture(image);
            ShowQuickOverlay(file);
            System.Windows.Clipboard.SetImage(image);
        }
        catch (Exception ex) { Error(ex); }
    }

    internal void ShowQuickOverlay(string path)
    {
        if (!File.Exists(path)) return;
        var visible = _overlays.Where(o => !o.IsClosed && !o.IsPinned).ToList();
        // Every capture has already been saved to the Desktop folder by AddCapture.
        // A full overlay stack must not create a seventh overlapping window.
        if (visible.Count >= QuickOverlay.MaximumVisible) return;
        var overlay = new QuickOverlay(path);
        _overlays.Add(overlay);
        overlay.Closed += (_, _) => _overlays.Remove(overlay);
        overlay.Show();
        RepositionVisibleOverlays(overlay);
    }

    internal void RepositionVisibleOverlays(QuickOverlay? newOverlay = null)
    {
        // Newest at the bottom, older captures rise above it. Each one occupies
        // its own slot with a real gap. At six screenshots, all slots shrink
        // proportionally instead of overlapping off-screen.
        var floating = _overlays.Where(o => !o.IsClosed && !o.IsPinned).ToList();
        foreach (var group in floating.GroupBy(o => o.CaptureScreen.DeviceName))
        {
            var items = group.ToList();
            if (items.Count == 0) continue;
            var screen = items[0].CaptureScreen;
            Rect work;
            if (screen.Primary)
                work = SystemParameters.WorkArea;
            else
            {
                // Screen.WorkingArea uses device pixels while WPF positions
                // top-level windows in DIPs. Convert using the monitor's DPI.
                var dpi = VisualTreeHelper.GetDpi(items[0]);
                double scaleX = Math.Max(0.5, dpi.DpiScaleX);
                double scaleY = Math.Max(0.5, dpi.DpiScaleY);
                var area = screen.WorkingArea;
                work = new Rect(area.Left / scaleX, area.Top / scaleY,
                    area.Width / scaleX, area.Height / scaleY);
            }
            const double inset = 14;
            double space = Math.Max(160, work.Height - inset * 2);
            double gap = QuickOverlay.StackGap;
            double naturalSum = items.Sum(o => o.PreferredHeight);
            double availableForCards = Math.Max(QuickOverlay.MinimumHeight * items.Count,
                space - gap * (items.Count - 1));
            double factor = Math.Min(1.0, availableForCards / naturalSum);
            var heights = items.Select(o => Math.Max(QuickOverlay.MinimumHeight, o.PreferredHeight * factor)).ToArray();
            // On small displays, squeeze once more to remain completely on-screen.
            double total = heights.Sum() + gap * (items.Count - 1);
            if (total > space)
            {
                double usable = Math.Max(40, space - gap * (items.Count - 1));
                double ratio = usable / heights.Sum();
                for (int i = 0; i < heights.Length; i++) heights[i] *= ratio;
                total = heights.Sum() + gap * (items.Count - 1);
            }
            double y = work.Bottom - inset - total;
            double anchor = work.Right - inset - 240;
            for (int i = 0; i < items.Count; i++)
            {
                items[i].PositionAt(anchor, y, heights[i],
                    animate: newOverlay == null || !ReferenceEquals(items[i], newOverlay));
                y += heights[i] + gap;
            }
        }
    }

    internal void ShowHistory()
    {
        var window = new HistoryWindow();
        window.Show();
        window.Activate();
    }

    private void RestoreLast()
    {
        var latest = Captures.LatestFile();
        if (latest != null) ShowQuickOverlay(latest);
        else MessageBox.Show("No captures yet.", "FloatShot");
    }

    internal static void Error(Exception ex) => MessageBox.Show(ex.Message, "FloatShot", MessageBoxButton.OK, MessageBoxImage.Warning);

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_messageWindow != null)
            {
                var handle = new WindowInteropHelper(_messageWindow).Handle;
                for (int i = 1; i <= 3; i++) Native.UnregisterHotKey(handle, i);
            }
            _source?.RemoveHook(WndProc);
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            _singleInstance?.ReleaseMutex();
            _singleInstance?.Dispose();
        }
        catch { /* shutdown cleanup */ }
        base.OnExit(e);
    }
}
