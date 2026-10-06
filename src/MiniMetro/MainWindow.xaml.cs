using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace MiniMetro;
public partial class MainWindow : Window
{
    private readonly BeatSynth synth = new();
    private WaveAudio? audio;
    private bool ready, mini, fullPinned;
    private UserSettings saved = new();
    private Point fullPosition;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private readonly List<double> taps = [];
    private readonly List<Ellipse> dots = [];
    private int lastPulse;
    private double pulseAt;
    private string? audioError;
    private readonly List<int> registeredKeys = [];
    private IntPtr handle;
    private static readonly Brush Mint = new SolidColorBrush(Color.FromRgb(123, 228, 192));
    private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(53, 66, 89));
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniMetro", "settings.json");

    public MainWindow()
    {
        InitializeComponent();
        try { if (File.Exists(SettingsPath)) saved = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath)) ?? new(); } catch { }
        BeatsCombo.ItemsSource = Enumerable.Range(1, 12).Select(x => $"{x} 拍").ToArray();
        DivisionCombo.ItemsSource = new[] { "1 · 不细分", "2 · 八分音符", "3 · 三连音", "4 · 十六分音符" };
        SoundCombo.ItemsSource = new[] { "木质", "清脆", "电子" };
        var s = saved.Beat ?? new BeatSettings();
        s = s with { Playing = false, Bpm = Math.Clamp(s.Bpm, 20, 300), Beats = Math.Clamp(s.Beats, 1, 12), Division = Math.Clamp(s.Division, 1, 4), Volume = Math.Clamp(s.Volume, 0, 1), Sound = Math.Clamp(s.Sound, 0, 2) };
        synth.Settings = s;
        BeatsCombo.SelectedIndex = s.Beats - 1; DivisionCombo.SelectedIndex = s.Division - 1;
        SoundCombo.SelectedIndex = s.Sound; AccentCheck.IsChecked = s.Accent; MuteCheck.IsChecked = s.Muted;
        VolumeSlider.Value = s.Volume * 100; fullPinned = saved.Pin; Topmost = fullPinned;
        ready = true; SyncTempo(); BuildDots(); SyncButtons(); SyncPinButton();
        timer.Tick += Animate;
        Loaded += (_, _) =>
        {
            fullPosition = new Point(Left, Top);
            audio = new WaveAudio(synth);
            audio.Failed += message => Dispatcher.BeginInvoke(() => { audioError = message; synth.Settings = synth.Settings with { Playing = false }; SyncButtons(); });
            timer.Start();
            if (saved.Mini) SetMini(true);
        };
    }
    private void SyncTempo()
    {
        bool previous = ready; ready = false;
        BpmBox.Text = synth.Settings.Bpm.ToString(); MiniBpm.Content = BpmBox.Text;
        TempoSlider.Value = synth.Settings.Bpm; ready = previous;
    }
    private void ChangeTempo(int value) { synth.Settings = synth.Settings with { Bpm = Math.Clamp(value, 20, 300) }; SyncTempo(); }
    private int Step => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 1;
    private void MinusClick(object sender, RoutedEventArgs e) => ChangeTempo(synth.Settings.Bpm - Step);
    private void PlusClick(object sender, RoutedEventArgs e) => ChangeTempo(synth.Settings.Bpm + Step);
    private void TempoChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (ready) ChangeTempo((int)Math.Round(e.NewValue)); }
    private void BpmCommit(object sender, RoutedEventArgs e) => CommitTempo();
    private void CommitTempo() { if (int.TryParse(BpmBox.Text, out int bpm)) ChangeTempo(bpm); else SyncTempo(); }
    private void BpmKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitTempo(); Keyboard.ClearFocus(); e.Handled = true; }
        if (e.Key == Key.Escape) { SyncTempo(); Keyboard.ClearFocus(); e.Handled = true; }
    }
    private void PlayClick(object sender, RoutedEventArgs e) => TogglePlay();
    private void TogglePlay()
    {
        CommitTempo();
        if (audioError != null) { MessageBox.Show(this, audioError, "音频设备", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        synth.Settings = synth.Settings with { Playing = !synth.Settings.Playing }; SyncButtons();
    }
    private void SyncButtons()
    {
        PlayButton.Content = synth.Settings.Playing ? "Ⅱ  暂停" : "▶  开始";
        MiniPlay.Content = synth.Settings.Playing ? "Ⅱ" : "▶";
        MiniMuteMenu.IsChecked = synth.Settings.Muted;
        VolumeLabel.Text = $"{synth.Settings.Volume:P0}";
    }
    private void PatternChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        synth.Settings = synth.Settings with { Beats = BeatsCombo.SelectedIndex + 1, Division = DivisionCombo.SelectedIndex + 1 };
        BuildDots();
    }
    private void OptionsChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        synth.Settings = synth.Settings with { Accent = AccentCheck.IsChecked == true, Sound = SoundCombo.SelectedIndex, Volume = VolumeSlider.Value / 100, Muted = MuteCheck.IsChecked == true }; SyncButtons();
    }
    private void BuildDots()
    {
        BeatDots.Children.Clear(); dots.Clear();
        for (int i = 0; i < synth.Settings.Beats; i++)
        {
            var dot = new Ellipse { Width = 11, Height = 11, Margin = new Thickness(5), Fill = Dim };
            dots.Add(dot); BeatDots.Children.Add(dot);
        }
    }
    private void Animate(object? sender, EventArgs e)
    {
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (audio?.Error != null && audioError == null) { audioError = audio.Error; synth.Settings = synth.Settings with { Playing = false }; SyncButtons(); }
        int pulse = synth.Pulse;
        if (pulse != lastPulse) { lastPulse = pulse; pulseAt = now; }
        bool lit = synth.Settings.Playing && now - pulseAt < 0.095;
        for (int i = 0; i < dots.Count; i++) dots[i].Fill = lit && synth.VisibleBeat == i ? Mint : Dim;
        MiniPulse.Fill = lit ? Mint : Dim;
        StatusText.Text = audioError ?? (synth.Settings.Playing ? $"第 {synth.VisibleBeat + 1} / {synth.Settings.Beats} 拍" + (synth.Settings.Muted ? " · 静音" : "") : "准备好就开始");
    }
    private void TapClick(object sender, RoutedEventArgs e)
    {
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (taps.Count > 0 && now - taps[^1] > 3) taps.Clear();
        taps.Add(now); if (taps.Count > 7) taps.RemoveAt(0);
        if (taps.Count >= 3) ChangeTempo((int)Math.Round(60 * (taps.Count - 1) / (taps[^1] - taps[0])));
    }
    private void PinClick(object sender, RoutedEventArgs e)
    {
        fullPinned = !fullPinned;
        Topmost = mini || fullPinned;
        SyncPinButton(); SaveSettings();
    }
    private void SyncPinButton()
    {
        PinButton.Content = fullPinned ? "已置顶" : "置顶";
        PinButton.Background = fullPinned ? Mint : new SolidColorBrush(Color.FromRgb(38, 49, 71));
        PinButton.Foreground = fullPinned ? new SolidColorBrush(Color.FromRgb(17, 42, 37)) : new SolidColorBrush(Color.FromRgb(231, 237, 247));
        PinButton.ToolTip = fullPinned ? "点击取消大窗口置顶" : "点击让大窗口保持在其他窗口上方";
        System.Windows.Automation.AutomationProperties.SetName(PinButton, fullPinned ? "取消大窗口置顶" : "开启大窗口置顶");
    }
    private void MiniClick(object sender, RoutedEventArgs e) => SetMini(true);
    private void ExpandClick(object sender, RoutedEventArgs e) => SetMini(false);
    private void MiniBpmClick(object sender, RoutedEventArgs e) { SetMini(false); BpmBox.Focus(); BpmBox.SelectAll(); }
    private void SetMini(bool value)
    {
        if (mini == value) return;
        CommitTempo();
        if (value)
        {
            fullPosition = new Point(Left, Top);
            mini = true; FullPanel.Visibility = Visibility.Collapsed; MiniPanel.Visibility = Visibility.Visible;
            Width = 180; Height = 30; Topmost = true;
            if (saved.MiniX.HasValue && saved.MiniY.HasValue)
            { Left = saved.MiniX.Value; Top = saved.MiniY.Value; ClampToMonitor(); }
            else MoveToCorner();
        }
        else
        {
            saved.MiniX = Left; saved.MiniY = Top; mini = false;
            MiniPanel.Visibility = Visibility.Collapsed; FullPanel.Visibility = Visibility.Visible;
            Width = 430; Height = 610; Topmost = fullPinned;
            Left = fullPosition.X; Top = fullPosition.Y; ClampToMonitor();
        }
        SaveSettings();
    }
    private void DragMini(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { SetMini(false); return; }
        try { DragMove(); } catch { }
        ClampToMonitor(); saved.MiniX = Left; saved.MiniY = Top; SaveSettings();
    }
    private void ResetCornerClick(object sender, RoutedEventArgs e) { MoveToCorner(); saved.MiniX = Left; saved.MiniY = Top; SaveSettings(); }
    private void MiniMuteClick(object sender, RoutedEventArgs e)
    { synth.Settings = synth.Settings with { Muted = MiniMuteMenu.IsChecked }; MuteCheck.IsChecked = synth.Settings.Muted; SyncButtons(); }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is ComboBox || FindParent<ComboBox>(e.OriginalSource as DependencyObject) != null) return;
        ChangeTempo(synth.Settings.Bpm + Math.Sign(e.Delta) * Step); e.Handled = true;
    }
    private static T? FindParent<T>(DependencyObject? obj) where T : DependencyObject
    { while (obj != null) { if (obj is T found) return found; obj = VisualTreeHelper.GetParent(obj); } return null; }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem or Slider) return;
        if (e.Key == Key.Space) { TogglePlay(); e.Handled = true; }
        else if (e.Key is Key.Up or Key.Add or Key.OemPlus) { ChangeTempo(synth.Settings.Bpm + Step); e.Handled = true; }
        else if (e.Key is Key.Down or Key.Subtract or Key.OemMinus) { ChangeTempo(synth.Settings.Bpm - Step); e.Handled = true; }
        else if (e.Key == Key.Escape && mini) { SetMini(false); e.Handled = true; }
    }
    private void SaveSettings()
    {
        saved.Beat = synth.Settings with { Playing = false }; saved.Mini = mini; saved.Pin = fullPinned;
        if (mini) { saved.MiniX = Left; saved.MiniY = Top; }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath + ".tmp", JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(SettingsPath + ".tmp", SettingsPath, true);
        }
        catch { /* A read-only profile must not prevent use. */ }
    }
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        timer.Stop(); SaveSettings();
        foreach (int id in registeredKeys) UnregisterHotKey(handle, id);
        audio?.Dispose();
    }
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        int[] keys = [0x20, 0x26, 0x28, 0x4D];
        for (int i = 0; i < keys.Length; i++) if (RegisterHotKey(handle, i + 1, 0x4003, (uint)keys[i])) registeredKeys.Add(i + 1);
        if (registeredKeys.Count < 4) HotkeyHint.Text = "部分全局快捷键被占用；仍可点击小条控制。";
    }
    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // A native caption hit lets Windows drag the whole title region, including
        // text and empty space, without depending on a WPF event's original source.
        if (msg == 0x0084 && !mini && TitleDragArea.IsVisible)
        {
            long coordinates = lParam.ToInt64();
            var screenPoint = new Point((short)(coordinates & 0xFFFF), (short)((coordinates >> 16) & 0xFFFF));
            var point = TitleDragArea.PointFromScreen(screenPoint);
            if (point.X >= 0 && point.Y >= 0 && point.X < TitleDragArea.ActualWidth && point.Y < TitleDragArea.ActualHeight)
            {
                handled = true;
                return new IntPtr(2); // HTCAPTION
            }
        }
        if (msg == 0x0312)
        {
            switch (wParam.ToInt32())
            {
                case 1: TogglePlay(); break;
                case 2: ChangeTempo(synth.Settings.Bpm + 1); break;
                case 3: ChangeTempo(synth.Settings.Bpm - 1); break;
                case 4: if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; SetMini(!mini); break;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }
    private Rect MonitorArea()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var point = new NativePoint { X = (int)((Left + Width / 2) * dpi.DpiScaleX), Y = (int)((Top + Height / 2) * dpi.DpiScaleY) };
        IntPtr monitor = MonitorFromPoint(point, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(monitor, ref info)) return new Rect(info.Work.Left / dpi.DpiScaleX, info.Work.Top / dpi.DpiScaleY,
            (info.Work.Right - info.Work.Left) / dpi.DpiScaleX, (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
        return SystemParameters.WorkArea;
    }
    private void MoveToCorner() { var area = MonitorArea(); Left = area.Right - Width - 12; Top = area.Top + 12; }
    private void ClampToMonitor()
    { var area = MonitorArea(); Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)); Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)); }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
public sealed class UserSettings
{
    public BeatSettings Beat { get; set; } = new();
    public bool Mini { get; set; }
    public bool Pin { get; set; }
    public double? MiniX { get; set; }
    public double? MiniY { get; set; }
}
