using MiniMetro;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;

class Program
{
    private static int checks;
    private static void Check(bool test, string message) { if (!test) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    [STAThread]
    static int Main()
    {
        string settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniMetro", "settings.json");
        string? backup = File.Exists(settings) ? File.ReadAllText(settings) : null;
        MainWindow? window = null;
        try
        {
            TestRhythm(); TestNativeAudio();
            var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
            File.WriteAllText(settings, "{}");
            window = new MainWindow { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = 0 };
            window.Show(); Pump(120); window.UpdateLayout();
            var titleArea = (FrameworkElement)window.FindName("TitleDragArea");
            var pin = (Button)window.FindName("PinButton");
            Check(NativeHit(window, titleArea, new Point(8, 19)) == 2, "Title text is a native draggable caption");
            Check(NativeHit(window, titleArea, new Point(titleArea.ActualWidth - 2, 19)) == 2, "Blank title area is also draggable");
            Check(NativeHit(window, pin, new Point(pin.ActualWidth / 2, 19)) != 2, "Pin button is clickable rather than draggable");
            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(30); window.UpdateLayout();
            Check(window.Topmost && (string)pin.Content == "已置顶", "Header pin button enables topmost and shows its state");
            Check(JsonPin(settings), "Pinned state is saved immediately");
            Invoke(window, "SetMini", true); Invoke(window, "SetMini", false);
            Check(window.Topmost, "Full window stays pinned after a mini round trip");
            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!window.Topmost && (string)pin.Content == "置顶", "Header pin button disables topmost");
            Invoke(window, "SetMini", true); Check(window.Topmost, "Mini always stays on top when full pin is off");
            Invoke(window, "SetMini", false); Check(!window.Topmost, "Returning restores unpinned full window state");
            Check(!JsonPin(settings), "Unpinned state is saved");
            window.UpdateLayout();
            Render(window, "full-preview.png");
            var synth = (BeatSynth)typeof(MainWindow).GetField("synth", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            var bpm = (TextBox)window.FindName("BpmBox");
            bpm.Text = "999"; bpm.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            Invoke(window, "CommitTempo"); Check(synth.Settings.Bpm == 300, "BPM input clamps to 300");
            bpm.Text = "abc"; Invoke(window, "CommitTempo"); Check(bpm.Text == "300", "Invalid input restores previous BPM");
            bpm.Text = "100"; Invoke(window, "CommitTempo");
            Invoke(window, "SetMini", true); Pump(100); window.UpdateLayout();
            Check(window.Width == 180 && window.Height == 30 && window.Topmost, "Mini bar is 180 x 30 and always on top");
            Check(((FrameworkElement)window.FindName("FullPanel")).Visibility == Visibility.Collapsed, "Mini mode hides full controls");
            Render(window, "mini-preview.png");
            var video = new Window { Title = "Fullscreen overlay integration test", WindowStyle = WindowStyle.None, WindowState = WindowState.Maximized, Background = Brushes.Black, ShowInTaskbar = false };
            try
            {
                video.Show(); video.Activate(); Pump(100);
                IntPtr miniHandle = new WindowInteropHelper(window).Handle;
                IntPtr cursor = new WindowInteropHelper(video).Handle;
                bool above = false;
                while ((cursor = GetWindow(cursor, 3)) != IntPtr.Zero) { if (cursor == miniHandle) { above = true; break; } }
                Check(above, "Mini stays above an activated borderless fullscreen window");
            }
            finally { video.Close(); }
            ((Button)window.FindName("MiniPlay")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(synth.Settings.Playing, "Mini play button starts playback");
            Pump(1000); Console.WriteLine($"Mini audio: pulses={synth.Pulse}, frames={synth.RenderedSamples}, bpm={synth.Settings.Bpm}"); Check(synth.Pulse >= 2, "Playback continues in mini mode");
            Invoke(window, "SetMini", false); Pump(80);
            Check(synth.Settings.Playing && window.Width == 430, "Expanding preserves playback");
            ((ComboBox)window.FindName("BeatsCombo")).SelectedIndex = 6;
            ((ComboBox)window.FindName("DivisionCombo")).SelectedIndex = 2;
            Check(synth.Settings.Beats == 7 && synth.Settings.Division == 3, "Advanced controls apply meter and subdivisions");
            ((Button)window.FindName("PlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!synth.Settings.Playing, "Full panel pauses playback");
            var bounds = new List<string>();
            var panel = (FrameworkElement)window.FindName("FullPanel");
            foreach (string name in new[] { "TitleDragArea", "PinButton", "BpmBox", "TempoSlider", "PlayButton", "StatusText", "BeatsCombo", "DivisionCombo", "AccentCheck", "SoundCombo", "VolumeSlider", "HotkeyHint" })
            {
                var element = (FrameworkElement)window.FindName(name);
                var p = element.TranslatePoint(new Point(), panel);
                Check(p.X >= -1 && p.Y >= -1 && p.X + element.ActualWidth <= panel.ActualWidth + 1 && p.Y + element.ActualHeight <= panel.ActualHeight + 1, name + " fits within full panel");
            }
            window.Close(); window = null;
            Console.WriteLine($"All {checks} checks passed."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            window?.Close();
            if (backup != null) File.WriteAllText(settings, backup); else if (File.Exists(settings)) File.Delete(settings);
        }
    }
    private static void Invoke(MainWindow window, string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private static int NativeHit(Window window, FrameworkElement element, Point local)
    {
        Point screen = element.PointToScreen(local);
        int coordinates = ((int)screen.X & 0xFFFF) | (((int)screen.Y & 0xFFFF) << 16);
        return SendMessage(new WindowInteropHelper(window).Handle, 0x0084, IntPtr.Zero, new IntPtr(coordinates)).ToInt32();
    }
    private static bool JsonPin(string settings)
    {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settings));
        return json.RootElement.GetProperty("Pin").GetBoolean();
    }
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Render(Window window, string file)
    {
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../work"));
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, file)); encoder.Save(stream);
    }
    private static void TestRhythm()
    {
        foreach (int bpm in new[] { 20, 100, 137, 300 }) foreach (int division in new[] { 1, 2, 3, 4 })
        {
            var s = new BeatSynth { Settings = new BeatSettings(Playing: true, Bpm: bpm, Division: division) };
            var buffer = new short[480];
            for (int i = 0; i < 6000; i++) s.Render(buffer);
            Check(s.TotalTicks == bpm * division, $"60 seconds at {bpm} BPM / {division} subdivisions = {bpm * division} ticks");
        }
        var synth = new BeatSynth { Settings = new BeatSettings(Playing: true, Bpm: 120) };
        var first = new short[24000]; synth.Render(first);
        Check(first.Any(v => v != 0), "Playing generates PCM audio");
        synth.Settings = synth.Settings with { Playing = false }; synth.Render(first);
        Check(first.All(v => v == 0), "Pause produces silence");
        synth.Settings = synth.Settings with { Playing = true, Muted = true }; synth.Render(first);
        Check(first.All(v => v == 0) && synth.VisibleBeat == 0, "Mute keeps beat progression with silent audio");
        synth.Settings = synth.Settings with { Muted = false }; synth.Render(first);
        Check(synth.VisibleBeat == 1, "Meter advances correctly");
    }
    private static void TestNativeAudio()
    {
        var synth = new BeatSynth();
        using var audio = new WaveAudio(synth);
        Thread.Sleep(250);
        Check(audio.Error == null, "Windows audio device opens successfully");
        var start = synth.RenderedSamples;
        var watch = Stopwatch.StartNew(); Thread.Sleep(2000); watch.Stop();
        long frames = synth.RenderedSamples - start;
        double expected = watch.Elapsed.TotalSeconds * BeatSynth.SampleRate;
        Check(Math.Abs(frames - expected) < 3840, $"Native audio follows wall clock ({frames} samples in {watch.Elapsed.TotalSeconds:F3}s)");
    }
}
