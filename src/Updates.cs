using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace FloatShot;

internal sealed class UpdateSettings
{
    // Old settings may contain a Repository field. It is deliberately ignored.
    // ReleaseRepository below is the single official update source.
    public bool Automatic { get; set; } = true;
}

internal sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string AssetUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

/// <summary>
/// The only supported update channel is the official public FloatShot binary repository.
/// Checking is read-only. Installation is an explicit user action after the version
/// comparison, SHA-256 verification and safe ZIP extraction.
/// </summary>
internal sealed class Updates
{
    private const string ReleaseRepository = "classifiedamazing/FloatShot-Releases";
    private const long MaxCompressedBytes = 350L * 1024 * 1024;
    private const long MaxExtractedBytes = 1250L * 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatShot", "update-settings.json");
    private static readonly string UpdateRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatShot", "Updates");
    public static readonly string InstallRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FloatShot");

    private readonly App _app;
    private readonly Forms.NotifyIcon _tray;
    private readonly DispatcherTimer _repeat = new() { Interval = TimeSpan.FromHours(6) };
    private readonly DispatcherTimer _pendingTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly UpdateSettings _settings;
    private UpdateWindow? _window;
    private Forms.ToolStripMenuItem? _autoMenu;
    private UpdateManifest? _availableManifest;
    private bool _checking;
    private bool _installing;
    private string _latestVersion = "Not checked";
    private string _message = "Click Check Again to look for a newer version.";
    private string? _pendingStage;
    private string? _pendingVersion;

    public Updates(App app, Forms.NotifyIcon tray)
    {
        _app = app;
        _tray = tray;
        _settings = LoadSettings();
        _repeat.Tick += async (_, _) => { if (_settings.Automatic) await CheckAsync(false); };
        _pendingTimer.Tick += (_, _) => AttemptPendingInstallation();
    }

    public static Version CurrentVersion => typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
    public static bool IsInstalled => File.Exists(Path.Combine(InstallRoot, "installed.marker")) &&
        string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(InstallRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    public void AddToTrayMenu(Forms.ContextMenuStrip menu)
    {
        var updates = new Forms.ToolStripMenuItem($"Updates (v{CurrentVersion.ToString(3)})");
        updates.DropDownItems.Add("Check for Updates...", null, (_, _) => ShowUpdateWindow());
        _autoMenu = new Forms.ToolStripMenuItem("Automatically Check for Updates")
        {
            Checked = _settings.Automatic,
            CheckOnClick = true
        };
        _autoMenu.CheckedChanged += (_, _) =>
        {
            _settings.Automatic = _autoMenu.Checked;
            SaveSettings();
            if (_settings.Automatic) _ = CheckAsync(false);
        };
        updates.DropDownItems.Add(_autoMenu);
        updates.DropDownItems.Add(new Forms.ToolStripSeparator());
        updates.DropDownItems.Add("Update Log", null, (_, _) => OpenLog());
        menu.Items.Add(updates);
    }

    public void Start()
    {
        _repeat.Start();
        _pendingTimer.Start();
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        first.Tick += async (_, _) =>
        {
            first.Stop();
            if (_settings.Automatic) await CheckAsync(false);
        };
        first.Start();
    }

    private void ShowUpdateWindow()
    {
        if (_window is null)
        {
            _window = new UpdateWindow(CurrentVersion.ToString(3),
                () => _ = CheckAsync(true),
                () => _ = InstallAvailableAsync());
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        else
        {
            _window.Activate();
        }
        RefreshWindow();
        _ = CheckAsync(true);
    }

    private void RefreshWindow()
    {
        _window?.SetState(_latestVersion, _message,
            !_checking && !_installing && _availableManifest is not null && _pendingStage is null && IsInstalled,
            _checking, _installing);
    }

    public async Task CheckAsync(bool manual)
    {
        if (_checking || _installing) return;
        _checking = true;
        _message = "Checking the official FloatShot release channel...";
        if (_availableManifest is null) _latestVersion = "Checking...";
        RefreshWindow();
        try
        {
            string manifestUrl = $"https://github.com/{ReleaseRepository}/releases/latest/download/floatshot-update.json";
            using var response = await Client.GetAsync(manifestUrl, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _availableManifest = null;
                _latestVersion = "Not published";
                _message = "No downloadable FloatShot update has been published yet. You're running v" +
                    CurrentVersion.ToString(3) + ". Try again after the first release.";
                return;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length > 64 * 1024)
                throw new InvalidDataException("The update manifest is too large.");
            byte[] body = await response.Content.ReadAsByteArrayAsync();
            if (body.Length > 64 * 1024) throw new InvalidDataException("The update manifest is too large.");
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(body, JsonOptions)
                ?? throw new InvalidDataException("The release manifest is invalid.");
            if (string.IsNullOrWhiteSpace(manifest.Version) ||
                !Version.TryParse(manifest.Version.TrimStart('v', 'V'), out var remoteVersion))
                throw new InvalidDataException("The release version is invalid.");
            ValidateManifest(manifest);

            _latestVersion = "v" + remoteVersion.ToString(3);
            if (remoteVersion > CurrentVersion)
            {
                _availableManifest = manifest;
                _message = IsInstalled
                    ? $"Version {_latestVersion} is available. Click Update Now to install it."
                    : $"Version {_latestVersion} is available. Install FloatShot locally first to enable updates.";
                if (!manual && _window is null)
                    _tray.ShowBalloonTip(3500, "FloatShot update available",
                        $"{_latestVersion} is available. Open Updates > Check for Updates to install.",
                        Forms.ToolTipIcon.Info);
            }
            else
            {
                _availableManifest = null;
                _message = remoteVersion == CurrentVersion
                    ? "You're up to date."
                    : "You're already running a newer version than the published release.";
            }
        }
        catch (Exception ex)
        {
            _availableManifest = null;
            _latestVersion = "Unavailable";
            _message = "Could not check for updates: " + ex.Message;
            WriteLog("Update check failed: " + ex);
        }
        finally
        {
            _checking = false;
            RefreshWindow();
        }
    }

    private static void ValidateManifest(UpdateManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Sha256) ||
            !Regex.IsMatch(manifest.Sha256, @"^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("The release is missing a valid SHA-256 checksum.");
        if (!Uri.TryCreate(manifest.AssetUrl, UriKind.Absolute, out var assetUri) ||
            assetUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(assetUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !assetUri.AbsolutePath.StartsWith("/" + ReleaseRepository + "/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The release URL does not match the official FloatShot channel.");
    }

    private async Task InstallAvailableAsync()
    {
        if (_checking || _installing || _availableManifest is null) return;
        if (!IsInstalled)
        {
            _message = "This is a portable copy. Install FloatShot with INSTALL-WINDOWS.bat first.";
            RefreshWindow();
            return;
        }
        if (_pendingStage is not null)
        {
            _message = "An update is ready. Close screenshot and editor windows to finish installing.";
            RefreshWindow();
            return;
        }

        var manifest = _availableManifest;
        _installing = true;
        string? archive = null;
        string? stage = null;
        try
        {
            _message = "Downloading v" + manifest.Version.TrimStart('v', 'V') + " securely...";
            RefreshWindow();
            Directory.CreateDirectory(UpdateRoot);
            string id = Guid.NewGuid().ToString("N");
            archive = Path.Combine(UpdateRoot, "update-" + id + ".zip");
            stage = Path.Combine(UpdateRoot, "stage-" + id);
            await DownloadLimitedAsync(manifest.AssetUrl, archive);

            _message = "Verifying checksum and extracting the update...";
            RefreshWindow();
            using (var fs = File.OpenRead(archive))
            {
                string actual = Convert.ToHexString(SHA256.HashData(fs));
                if (!string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Release checksum verification failed. No update was installed.");
            }
            ExtractSafely(archive, stage);
            string exePath = Path.Combine(stage, "FloatShot.exe");
            if (!File.Exists(exePath))
                throw new InvalidDataException("The update archive does not contain FloatShot.exe.");
            var stagedVersion = FileVersionInfo.GetVersionInfo(exePath).FileVersion;
            if (!Version.TryParse(stagedVersion, out var staged) ||
                !Version.TryParse(manifest.Version.TrimStart('v', 'V'), out var remoteVersion) ||
                staged.Major != remoteVersion.Major || staged.Minor != remoteVersion.Minor || staged.Build != remoteVersion.Build)
                throw new InvalidDataException("The downloaded executable version does not match the release.");

            _pendingStage = stage;
            _pendingVersion = remoteVersion.ToString(3);
            _availableManifest = null;
            _message = _app.CanRestartForUpdate
                ? "Update verified. Closing FloatShot to install the new version..."
                : "Update ready. Close floating screenshots and editor windows to finish installing.";
            RefreshWindow();
            WriteLog("Downloaded and verified version " + _pendingVersion);
            AttemptPendingInstallation();
        }
        catch (Exception ex)
        {
            WriteLog("Update installation preparation failed: " + ex);
            _message = "Update was not installed: " + ex.Message;
            if (archive is not null) TryDeleteFile(archive);
            if (stage is not null) TryDeleteDirectory(stage);
        }
        finally
        {
            if (archive is not null) TryDeleteFile(archive);
            _installing = false;
            RefreshWindow();
        }
    }

    private void AttemptPendingInstallation()
    {
        if (_pendingStage is null || !_app.CanRestartForUpdate) return;
        string stage = _pendingStage;
        try
        {
            string exe = Path.Combine(stage, "FloatShot.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("The staged executable is missing.");
            var process = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = stage,
                UseShellExecute = false
            };
            process.ArgumentList.Add("--apply-update");
            process.ArgumentList.Add(stage);
            process.ArgumentList.Add(InstallRoot);
            process.ArgumentList.Add(Environment.ProcessId.ToString());
            if (Process.Start(process) is null)
                throw new InvalidOperationException("Could not launch the update helper.");
            WriteLog("Started installing version " + _pendingVersion);
            _pendingStage = null;
            _app.Shutdown();
        }
        catch (Exception ex)
        {
            WriteLog("Could not launch update helper: " + ex);
            _message = "Could not install update: " + ex.Message;
            RefreshWindow();
        }
    }

    private static async Task DownloadLimitedAsync(string url, string file)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long n && n > MaxCompressedBytes)
            throw new InvalidDataException("Release archive exceeds the maximum allowed size.");
        using var input = await response.Content.ReadAsStreamAsync();
        using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[64 * 1024];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer)) != 0)
        {
            received += read;
            if (received > MaxCompressedBytes) throw new InvalidDataException("Release archive exceeded the maximum allowed size.");
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
    }

    private static void ExtractSafely(string archive, string stage)
    {
        Directory.CreateDirectory(stage);
        string root = Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        long total = 0;
        using var zip = ZipFile.OpenRead(archive);
        foreach (var item in zip.Entries)
        {
            string dest = Path.GetFullPath(Path.Combine(stage, item.FullName));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unsafe path in release archive.");
            if (((item.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (item.ExternalAttributes & 0x400) != 0)
                throw new InvalidDataException("Symbolic links are not allowed in update archives.");
            total += item.Length;
            if (total > MaxExtractedBytes) throw new InvalidDataException("Uncompressed archive is too large.");
            if (string.IsNullOrEmpty(item.Name))
                Directory.CreateDirectory(dest);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                item.ExtractToFile(dest, overwrite: false);
            }
        }
    }

    internal static void WriteLog(string message)
    {
        try
        {
            string folder = Path.GetDirectoryName(SettingsFile)!;
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "updates.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
        }
        catch { /* do not disrupt screen captures when logging fails */ }
    }

    private static void OpenLog()
    {
        string path = Path.Combine(Path.GetDirectoryName(SettingsFile)!, "updates.log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) File.WriteAllText(path, "No update activity yet." + Environment.NewLine);
        Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = "\"" + path + "\"", UseShellExecute = true });
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(6) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FloatShot-Update/0.7 (Windows)");
        return http;
    }

    private static UpdateSettings LoadSettings()
    {
        try
        {
            return File.Exists(SettingsFile)
                ? JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(SettingsFile), JsonOptions) ?? new UpdateSettings()
                : new UpdateSettings();
        }
        catch { return new UpdateSettings(); }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            string temp = SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_settings, JsonOptions));
            File.Move(temp, SettingsFile, true);
        }
        catch (Exception ex) { App.Error(ex); }
    }

    private static void TryDeleteFile(string file) { try { if (File.Exists(file)) File.Delete(file); } catch { } }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
}

internal sealed class UpdateWindow : Window
{
    private readonly TextBlock _latestValue;
    private readonly TextBlock _status;
    private readonly Button _check;
    private readonly Button _install;

    public UpdateWindow(string currentVersion, Action onCheck, Action onInstall)
    {
        Title = "FloatShot · Updates";
        Width = 475;
        Height = 322;
        MinWidth = 475;
        MinHeight = 322;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Brush("#111722");
        Foreground = Ui.Light;
        ShowInTaskbar = true;

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };
        root.Children.Add(new TextBlock
        {
            Text = "FloatShot updates",
            Foreground = Ui.Light,
            FontSize = 23,
            FontWeight = FontWeights.Bold
        });
        root.Children.Add(new TextBlock
        {
            Text = "Stay current with one click.",
            Foreground = Ui.Muted,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 17)
        });

        var versions = new Grid();
        versions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        versions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        versions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var current = VersionCard("Installed version", "v" + currentVersion, out _);
        Grid.SetColumn(current, 0);
        versions.Children.Add(current);
        var latest = VersionCard("Latest available", "Checking...", out _latestValue);
        Grid.SetColumn(latest, 2);
        versions.Children.Add(latest);
        root.Children.Add(versions);

        var statusBorder = new Border
        {
            Background = Ui.Brush("#1B2535"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 14, 0, 14),
            MinHeight = 46
        };
        _status = new TextBlock
        {
            Text = "Checking the official release channel...",
            Foreground = Ui.Muted,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        statusBorder.Child = _status;
        root.Children.Add(statusBorder);

        var buttons = new Grid();
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _check = MakeButton("Check Again", Ui.Brush("#2D394B"), Ui.Light);
        _check.Click += (_, _) => onCheck();
        Grid.SetColumn(_check, 0);
        buttons.Children.Add(_check);
        _install = MakeButton("Update Now", Ui.Brush("#5B8CFF"), Brushes.White);
        _install.IsEnabled = false;
        _install.Click += (_, _) => onInstall();
        Grid.SetColumn(_install, 2);
        buttons.Children.Add(_install);
        root.Children.Add(buttons);
        Content = root;
    }

    private static Button MakeButton(string label, Brush background, Brush foreground)
    {
        return new Button
        {
            Content = label,
            Height = 38,
            BorderThickness = new Thickness(0),
            Background = background,
            Foreground = foreground,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 13
        };
    }

    private static Border VersionCard(string label, string value, out TextBlock valueText)
    {
        var contents = new StackPanel();
        contents.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Ui.Muted });
        valueText = new TextBlock
        {
            Text = value,
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = Ui.Light,
            Margin = new Thickness(0, 6, 0, 0)
        };
        contents.Children.Add(valueText);
        return new Border
        {
            Background = Ui.Brush("#222D3D"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(13),
            Child = contents
        };
    }

    public void SetState(string latest, string message, bool canInstall, bool checking, bool installing)
    {
        _latestValue.Text = latest;
        _status.Text = message;
        _check.IsEnabled = !checking && !installing;
        _check.Content = checking ? "Checking..." : "Check Again";
        _install.IsEnabled = canInstall;
        _install.Content = installing ? "Preparing update..." : "Update Now";
    }
}

/// <summary>Executed by the verified, staged *new* executable after the old app quits.</summary>
internal static class UpdateInstaller
{
    public static void Apply(string stage, string target, string previousPidText)
    {
        string incoming = target + ".incoming";
        string backup = target + ".previous";
        bool movedOld = false;
        try
        {
            stage = Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar);
            target = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
            incoming = target + ".incoming";
            backup = target + ".previous";
            string expectedStageBase = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatShot", "Updates"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!string.Equals(target, Path.GetFullPath(Updates.InstallRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                !stage.StartsWith(expectedStageBase, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(Path.Combine(stage, "FloatShot.exe")) ||
                !File.Exists(Path.Combine(target, "installed.marker")))
                throw new InvalidOperationException("Update helper received an invalid installation directory.");

            if (!int.TryParse(previousPidText, out int pid) || pid <= 0) throw new InvalidOperationException("Invalid app process ID.");
            try
            {
                using var old = Process.GetProcessById(pid);
                if (!old.WaitForExit(30000)) throw new TimeoutException("FloatShot did not exit within 30 seconds.");
            }
            catch (ArgumentException) { /* previous app already closed */ }

            if (Directory.Exists(incoming)) Directory.Delete(incoming, true);
            CopyDirectory(stage, incoming);
            File.WriteAllText(Path.Combine(incoming, "installed.marker"), "FloatShot per-user installation");
            if (!File.Exists(Path.Combine(incoming, "FloatShot.exe"))) throw new InvalidDataException("Update program is missing.");

            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            Directory.Move(target, backup);
            movedOld = true;
            Directory.Move(incoming, target);
            try
            {
                using var uninstallKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FloatShot", writable: true);
                var version = FileVersionInfo.GetVersionInfo(Path.Combine(target, "FloatShot.exe")).ProductVersion;
                if (!string.IsNullOrWhiteSpace(version)) uninstallKey?.SetValue("DisplayVersion", version);
            }
            catch (Exception registryError) { Updates.WriteLog("Could not refresh Installed Apps version: " + registryError.Message); }
            Updates.WriteLog("Update installed successfully; previous version remains in " + backup);
            Launch(target);
        }
        catch (Exception ex)
        {
            Updates.WriteLog("Update installation failed: " + ex);
            try
            {
                if (movedOld)
                {
                    if (Directory.Exists(target)) Directory.Delete(target, true);
                    if (Directory.Exists(backup)) Directory.Move(backup, target);
                    Launch(target);
                }
            }
            catch (Exception rollback) { Updates.WriteLog("Rollback failed: " + rollback); }
        }
        finally
        {
            try { if (Directory.Exists(incoming)) Directory.Delete(incoming, true); } catch { }
        }
    }

    private static void Launch(string folder)
    {
        if (Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(folder, "FloatShot.exe"),
            WorkingDirectory = folder,
            UseShellExecute = true
        }) is null)
            throw new InvalidOperationException("Could not launch FloatShot after update.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}