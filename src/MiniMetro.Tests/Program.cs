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
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources = new ResourceDictionary { Source = new Uri("pack://application:,,,/小条节拍器;component/Resources.xaml") };
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
            TestMiniGestures(window, synth, settings);
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
    private static void TestMiniGestures(MainWindow window, BeatSynth synth, string settings)
    {
        foreach (string name in new[] { "MiniGrip", "MiniPlay", "MiniMinus", "MiniBpm", "MiniPlus", "MiniExpand", "MiniPulse", "MiniPanel" })
        {
            window.Left = 120; window.Top = 120; window.UpdateLayout();
            var element = (FrameworkElement)window.FindName(name);
            var down = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = element };
            window.RaiseEvent(down);
            Check(down.Handled && window.IsMouseCaptured, name + " starts the shared pointer gesture");
            var start = (Point)typeof(MainWindow).GetField("miniPressPoint", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            double oldLeft = window.Left, oldTop = window.Top;
            int bpm = synth.Settings.Bpm; bool playing = synth.Settings.Playing;
            Invoke(window, "MoveMiniGesture", new Point(start.X + 36, start.Y + 24));
            Invoke(window, "EndMiniGesture", true);
            Check(window.Left > oldLeft + 10 && window.Top > oldTop + 10, name + " drag moves the native window");
            Check(synth.Settings.Bpm == bpm && synth.Settings.Playing == playing && window.Width == 180, name + " drag suppresses the button click");
            Check(!window.IsMouseCaptured && window.Topmost, name + " releases capture and preserves topmost");
        }
        foreach (string name in new[] { "MiniMinus", "MiniPlus", "MiniPlay" })
        {
            var button = (Button)window.FindName(name);
            int bpm = synth.Settings.Bpm; bool playing = synth.Settings.Playing;
            Invoke(window, "BeginMiniGesture", new Point(200, 200), button);
            Invoke(window, "MoveMiniGesture", new Point(200.5, 200.5));
            Invoke(window, "EndMiniGesture", true);
            Check(name == "MiniPlay" ? synth.Settings.Playing != playing : synth.Settings.Bpm == bpm + (name == "MiniPlus" ? 1 : -1), name + " slight pointer jitter still produces one click");
        }
        synth.Settings = synth.Settings with { Playing = false };
        Invoke(window, "SyncButtons");
        var play = (Button)window.FindName("MiniPlay");
        Invoke(window, "BeginMiniGesture", new Point(200, 200), play);
        Invoke(window, "EndMiniGesture", false);
        Check(!synth.Settings.Playing, "Releasing outside a button cancels its click");
        Invoke(window, "BeginMiniGesture", new Point(200, 200), play);
        window.ReleaseMouseCapture();
        Invoke(window, "EndMiniGesture", true);
        Check(!synth.Settings.Playing && !window.IsMouseCaptured, "Lost capture cancels clicks and clears the gesture");
        Invoke(window, "BeginMiniGesture", new Point(200, 200), play);
        Invoke(window, "MoveMiniGesture", new Point(236, 224));
        Invoke(window, "MoveMiniGesture", new Point(200, 200));
        Invoke(window, "EndMiniGesture", true);
        Check(!synth.Settings.Playing, "Dragging away and back does not become a click");
        using (var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settings)))
            Check(Math.Abs(json.RootElement.GetProperty("MiniX").GetDouble() - window.Left) < 1, "Dragged position is saved");
        var bpmButton = (Button)window.FindName("MiniBpm");
        Invoke(window, "BeginMiniGesture", new Point(200, 200), bpmButton);
        Invoke(window, "EndMiniGesture", true);
        Check(window.Width == 430, "BPM single click still expands for editing");
        Invoke(window, "SetMini", true);
        var expand = (Button)window.FindName("MiniExpand");
        Invoke(window, "BeginMiniGesture", new Point(200, 200), expand);
        Invoke(window, "EndMiniGesture", true);
        Check(window.Width == 430, "Expand single click still opens the full panel");
        Invoke(window, "SetMini", true);
    }
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
