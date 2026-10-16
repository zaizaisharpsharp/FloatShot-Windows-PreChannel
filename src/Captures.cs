using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace FloatShot;
internal static class Captures
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "FloatShot Captures");

    public static BitmapSource Load(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public static string DefaultBaseName() => "Screenshot_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

    public static string BuildUniquePath(string? preferredBaseName = null)
    {
        Directory.CreateDirectory(Folder);
        string baseName = SanitizeFileName(string.IsNullOrWhiteSpace(preferredBaseName) ? DefaultBaseName() : preferredBaseName!);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = DefaultBaseName();
        string path = Path.Combine(Folder, baseName + ".png");
        if (!File.Exists(path)) return path;
        int i = 2;
        while (true)
        {
            string candidate = Path.Combine(Folder, $"{baseName}_{i}.png");
            if (!File.Exists(candidate)) return candidate;
            i++;
        }
    }

    public static BitmapSource CaptureVirtualDesktop()
    {
        var screen = Forms.SystemInformation.VirtualScreen;
        using var bitmap = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size, CopyPixelOperation.SourceCopy);
        return ToBitmapSource(bitmap);
    }

    public static BitmapSource CaptureMonitor(Forms.Screen monitor)
    {
        var area = monitor.Bounds;
        using var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size, CopyPixelOperation.SourceCopy);
        return ToBitmapSource(bitmap);
    }

    public static BitmapSource CaptureForegroundWindow()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var rect)) throw new InvalidOperationException("Could not find an active window. Click the window and try again.");
        var v = Forms.SystemInformation.VirtualScreen;
        int left = Math.Max(rect.Left, v.Left), top = Math.Max(rect.Top, v.Top);
        int right = Math.Min(rect.Right, v.Right), bottom = Math.Min(rect.Bottom, v.Bottom);
        if (right <= left || bottom <= top) throw new InvalidOperationException("The active window is outside the visible desktop.");
        using var bitmap = new Bitmap(right - left, bottom - top, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(bitmap.Width, bitmap.Height));
        return ToBitmapSource(bitmap);
    }

    public static BitmapSource ToBitmapSource(Bitmap image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        var result = new BitmapImage();
        result.BeginInit();
        result.CacheOption = BitmapCacheOption.OnLoad;
        result.StreamSource = stream;
        result.EndInit();
        result.Freeze();
        return result;
    }

    public static string SaveNewCapture(BitmapSource image, string? preferredBaseName = null)
    {
        string file = BuildUniquePath(preferredBaseName);
        Save(image, file);
        return file;
    }

    public static void Save(BitmapSource image, string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    public static void SaveAs(string from)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = Path.GetFileName(from), DefaultExt = ".png" };
        if (dlg.ShowDialog() == true) File.Copy(from, dlg.FileName, true);
    }

    public static string RenameFile(string originalPath, string newBaseName)
    {
        if (!File.Exists(originalPath)) throw new FileNotFoundException("Screenshot file not found.", originalPath);
        string target = BuildUniquePath(newBaseName);
        if (string.Equals(Path.GetFullPath(originalPath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return originalPath;
        File.Move(originalPath, target);
        return target;
    }

    public static string? LatestFile() => Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.png").OrderByDescending(File.GetCreationTimeUtc).FirstOrDefault() : null;
    public static void OpenFolder()
    {
        Directory.CreateDirectory(Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Folder + "\"") { UseShellExecute = true });
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim();
    }
}
