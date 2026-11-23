using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace NumenX;

public partial class MainWindow : Window
{
    public record Item(string Name, string Info, string Path);

    const int HotkeyId = 0x4E58;
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    static readonly string[] AccentColors = { "#7C5CFF", "#22D3EE", "#34D399", "#F59E0B", "#F43F5E", "#3B82F6" };

    readonly Settings _s = Settings.Load();
    readonly Recorder _rec = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };

    string? _ff;
    List<string> _encoders = new() { "libx264" };
    bool _dda;
    bool _hwReady;
    bool _busy;
    bool _loading = true;
    bool _closing;
    DateTime _t0;
    HwndSource? _src;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        _rec.Failed += msg => Dispatcher.Invoke(() => OnRecFailed(msg));
        _toastTimer.Tick += (_, _) => { Toast.Visibility = Visibility.Collapsed; _toastTimer.Stop(); };
    }

    // ---------- startup ----------
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        _src = HwndSource.FromHwnd(h);
        _src?.AddHook(Hook);
        // Ctrl + Shift + R  (MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT)
        if (!RegisterHotKey(h, HotkeyId, 0x0002 | 0x0004 | 0x4000, 0x52))
        {
            HotkeyHint.Text = "Hotkey unavailable (in use)";
            Logger.Write("Could not register Ctrl+Shift+R hotkey.");
        }
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _ = ToggleRecordAsync();
        }
        return IntPtr.Zero;
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyAccent(_s.Accent);
        BuildAccentSwatches();
        FolderBox.Text = _s.Folder;
        Mp4Check.IsChecked = _s.Mp4;
        SelectRadio("res", _s.Resolution);
        SelectRadio("fps", _s.Fps.ToString());
        SelectRadio("quality", _s.Quality);
        _loading = false;
        UpdateInfo();
        RefreshLists();

        _timer.Tick += (_, _) => TickStats();
        _timer.Start();
        TickStats();

        _ff = Hardware.FindFfmpeg();
        if (_ff == null)
        {
            StatusText.Text = "FFmpeg is missing";
            SubStatusText.Text = "Put ffmpeg.exe next to NumenX.exe (the installer does this for you).";
            return;
        }

        SubStatusText.Text = "Detecting your hardware...";
        _encoders = await Hardware.DetectEncodersAsync(_ff);
        _dda = await Hardware.HasFilterAsync(_ff, "ddagrab");
        var devs = await Hardware.ListAudioDevicesAsync(_ff);
        _hwReady = true;
        _loading = true;
        BuildEncoderPills();
        FillAudioLists(devs);
        _loading = false;
        UpdateInfo();

        var best = Hardware.EncoderNames[_encoders[0]];
        StatusText.Text = "Ready to record";
        SubStatusText.Text = $"Using {best}" + (_dda ? " with GPU capture" : " with compatible capture") + ".  Press Ctrl+Shift+R anywhere to start/stop.";
        RecordBtn.IsEnabled = true;
        Logger.Write($"Startup OK. Encoders: {string.Join(",", _encoders)}; ddagrab={_dda}");
    }

    // ---------- navigation ----------
    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not RadioButton rb) return;
        var tag = rb.Tag?.ToString();
        HomePage.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        LibraryPage.Visibility = tag == "Library" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        if (tag != "Settings") RefreshLists();
    }

    void GoLibrary_Click(object sender, RoutedEventArgs e) => NavLib.IsChecked = true;

    // ---------- recording ----------
    void RecordBtn_Click(object sender, RoutedEventArgs e) => _ = ToggleRecordAsync();

    async Task ToggleRecordAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (_ff == null || !_hwReady) { ShowToast("NumenX is still getting ready (or FFmpeg is missing)."); return; }
            if (_rec.IsRecording) await StopRecordingAsync();
            else StartRecording();
        }
        finally { _busy = false; }
    }

    void StartRecording()
    {
        double free = SystemStats.FreeDiskGb(_s.Folder);
        if (free < 2.0)
        {
            ShowToast($"Not enough disk space ({free:0.0} GB free). Free up space or change the folder.");
            return;
        }
        string enc = _s.Encoder != "Auto" && _encoders.Contains(_s.Encoder) ? _s.Encoder : _encoders[0];
        try
        {
            _rec.Start(_ff!, _s, enc, _dda);
        }
        catch (Exception ex)
        {
            Logger.Write("Start failed: " + ex.Message);
            ShowToast("Could not start recording: " + ex.Message);
            return;
        }
        _t0 = DateTime.Now;
        RecordBtn.Background = (Brush)FindResource("Danger");
        RecordBtnText.Text = "■  STOP";
        StatusText.Text = "● Recording";
        Title = "● REC - NumenX";
        ShowToast("Recording started");
    }

    async Task StopRecordingAsync()
    {
        RecordBtn.IsEnabled = false;
        StatusText.Text = "Finishing your file...";
        var path = await _rec.StopAsync(_ff!, _s.Mp4);
        ResetRecordUi();
        if (path != null)
        {
            ShowToast("Saved: " + System.IO.Path.GetFileName(path));
            SubStatusText.Text = path;
        }
        else ShowToast("Recording stopped, but no file was produced. Check the log.");
        RefreshLists();
    }

    void OnRecFailed(string tail)
    {
        ResetRecordUi();
        RefreshLists();
        MessageBox.Show("Recording stopped unexpectedly.\n\nAnything captured so far is kept (" +
                        System.IO.Path.GetFileName(_rec.OutputPath) + ").\n\nDetails:\n" + tail,
                        "NumenX", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    void ResetRecordUi()
    {
        RecordBtn.ClearValue(BackgroundProperty);   // back to the accent colour from the style
        RecordBtn.IsEnabled = true;
        RecordBtnText.Text = "●  RECORD";
        RecordTimer.Text = "Full desktop";
        StatusText.Text = "Ready to record";
        Title = "NumenX";
    }

    // ---------- live stats ----------
    void TickStats()
    {
        CpuText.Text = $"{SystemStats.CpuPercent():0}%";
        var (used, total, pct) = SystemStats.Ram();
        RamText.Text = $"{used:0.0} / {total:0.0} GB";
        DiskText.Text = $"{SystemStats.FreeDiskGb(_s.Folder):0} GB";
        using var self = Process.GetCurrentProcess();
        double app = SystemStats.ProcessMb(self);
        double ff = SystemStats.ProcessMb(_rec.Proc);
        AppMemText.Text = ff > 0 ? $"{app:0} MB + {ff:0} MB enc" : $"{app:0} MB";
        if (_rec.IsRecording)
        {
            var t = DateTime.Now - _t0;
            RecordTimer.Text = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
        }
    }

    void UpdateInfo()
    {
        ResText.Text = _s.Resolution switch
        {
            "Native" => $"{SystemStats.ScreenWidth()}×{SystemStats.ScreenHeight()}",
            "2160p" => "4K (2160p)",
            var r => r,
        };
        FpsText.Text = $"{_s.Fps} FPS";
        EncText.Text = _hwReady
            ? Hardware.EncoderNames[_s.Encoder != "Auto" && _encoders.Contains(_s.Encoder) ? _s.Encoder : _encoders[0]]
            : "Detecting...";
    }

    // ---------- settings ----------
    static IEnumerable<RadioButton> FindRadios(DependencyObject d)
    {
        foreach (var c in LogicalTreeHelper.GetChildren(d))
        {
            if (c is RadioButton r) yield return r;
            if (c is DependencyObject dd)
                foreach (var x in FindRadios(dd)) yield return x;
        }
    }

    void SelectRadio(string group, string tag)
    {
        foreach (var rb in FindRadios(this))
            if (rb.GroupName == group) rb.IsChecked = rb.Tag?.ToString() == tag;
    }

    void Choice_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton rb) return;
        var tag = rb.Tag?.ToString() ?? "";
        switch (rb.GroupName)
        {
            case "res": _s.Resolution = tag; break;
            case "fps": _s.Fps = int.Parse(tag); break;
            case "quality": _s.Quality = tag; break;
            case "enc": _s.Encoder = tag; break;
        }
        _s.Save();
        UpdateInfo();
    }

    void BuildEncoderPills()
    {
        EncoderPanel.Children.Clear();
        var options = new List<string> { "Auto" };
        options.AddRange(_encoders);
        if (!options.Contains(_s.Encoder)) _s.Encoder = "Auto";
        foreach (var o in options)
        {
            var rb = new RadioButton
            {
                GroupName = "enc",
                Tag = o,
                Content = o == "Auto" ? "Auto (recommended)" : Hardware.EncoderNames[o],
                Style = (Style)FindResource("PillRadio"),
                IsChecked = o == _s.Encoder,
            };
            rb.Checked += Choice_Checked;
            EncoderPanel.Children.Add(rb);
        }
    }

    void FillAudioLists(List<string> devs)
    {
        MicList.Items.Clear(); DeskList.Items.Clear();
        MicList.Items.Add("None"); DeskList.Items.Add("None");
        foreach (var d in devs) { MicList.Items.Add(d); DeskList.Items.Add(d); }
        MicList.SelectedItem = MicList.Items.Contains(_s.Mic) ? _s.Mic : "None";
        DeskList.SelectedItem = DeskList.Items.Contains(_s.Desktop) ? _s.Desktop : "None";
    }

    void Mic_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MicList.SelectedItem is not string v) return;
        _s.Mic = v; _s.Save();
    }

    void Desk_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DeskList.SelectedItem is not string v) return;
        _s.Desktop = v; _s.Save();
    }

    void Mp4_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Mp4 = Mp4Check.IsChecked == true; _s.Save();
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose where NumenX saves recordings", InitialDirectory = _s.Folder };
        if (dlg.ShowDialog() == true)
        {
            _s.Folder = dlg.FolderName; _s.Save();
            FolderBox.Text = _s.Folder;
            RefreshLists();
        }
    }

    void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Logger.Dir);
        Process.Start(new ProcessStartInfo(Logger.Dir) { UseShellExecute = true });
    }

    /// <summary>Basic Smart Optimize: uses detected encoder + screen size only (CPU/RAM/disk scoring not built yet).</summary>
    void Smart_Click(object sender, RoutedEventArgs e)
    {
        if (!_hwReady) { ShowToast("Still detecting hardware, try again in a moment."); return; }
        bool hw = _encoders.Any(x => x != "libx264");
        int screenH = SystemStats.ScreenHeight();
        string res = !hw ? "720p" : screenH >= 1440 ? "1440p" : "1080p";
        int fps = hw ? 60 : 30;
        string q = hw ? "Balanced" : "Performance";
        _s.Resolution = res; _s.Fps = fps; _s.Quality = q; _s.Encoder = "Auto"; _s.Save();
        _loading = true;
        SelectRadio("res", res); SelectRadio("fps", fps.ToString()); SelectRadio("quality", q); SelectRadio("enc", "Auto");
        _loading = false;
        UpdateInfo();
        ShowToast(hw ? $"Optimized: {res} {fps} FPS with {Hardware.EncoderNames[_encoders[0]]}"
                     : $"No hardware encoder found. Using light settings: {res} {fps} FPS");
    }

    void ApplyAccent(string hex)
    {
        try { Application.Current.Resources["Accent"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { }
    }

    void BuildAccentSwatches()
    {
        foreach (var hex in AccentColors)
        {
            var b = new Button
            {
                Width = 38, Height = 38, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(0),
                Style = (Style)FindResource("Btn"),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                Tag = hex,
            };
            b.Click += (_, _) => { _s.Accent = hex; _s.Save(); ApplyAccent(hex); };
            AccentPanel.Children.Add(b);
        }
    }

    // ---------- library ----------
    List<Item> Scan()
    {
        try
        {
            if (!Directory.Exists(_s.Folder)) return new();
            return new DirectoryInfo(_s.Folder).EnumerateFiles()
                .Where(f => f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                            f.Extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => new Item(f.Name, $"{f.LastWriteTime:g}   •   {f.Length / 1048576.0:0.#} MB", f.FullName))
                .ToList();
        }
        catch (Exception ex) { Logger.Write("Scan failed: " + ex.Message); return new(); }
    }

    void RefreshLists()
    {
        var all = Scan();
        LibList.ItemsSource = all;
        RecentList.ItemsSource = all.Take(5).ToList();
        RecCountText.Text = all.Count == 0 ? "No recordings yet" : $"{all.Count} recording{(all.Count == 1 ? "" : "s")}";
    }

    void LibRefresh_Click(object sender, RoutedEventArgs e) => RefreshLists();

    void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { ShowToast("Could not open file: " + ex.Message); }
    }

    void LibPlay_Click(object sender, RoutedEventArgs e)
    {
        if (LibList.SelectedItem is Item it) OpenFile(it.Path); else ShowToast("Select a recording first.");
    }

    void LibShow_Click(object sender, RoutedEventArgs e)
    {
        if (LibList.SelectedItem is Item it) Process.Start("explorer.exe", $"/select,\"{it.Path}\"");
        else ShowToast("Select a recording first.");
    }

    void LibDelete_Click(object sender, RoutedEventArgs e)
    {
        if (LibList.SelectedItem is not Item it) { ShowToast("Select a recording first."); return; }
        if (MessageBox.Show($"Permanently delete \"{it.Name}\"?", "NumenX", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { File.Delete(it.Path); RefreshLists(); ShowToast("Deleted"); }
        catch (Exception ex) { ShowToast("Could not delete: " + ex.Message); }
    }

    void List_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is Item it) OpenFile(it.Path);
    }

    // ---------- misc ----------
    void ShowToast(string text)
    {
        ToastText.Text = text;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop(); _toastTimer.Start();
    }

    async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_rec.IsRecording && !_closing)
        {
            e.Cancel = true;
            _closing = true;
            await StopRecordingAsync();   // finalize the file, then close
            Close();
            return;
        }
        try { UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId); } catch { }
        _s.Save();
    }
}
