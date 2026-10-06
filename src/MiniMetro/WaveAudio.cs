using System.Runtime.InteropServices;

namespace MiniMetro;
public sealed class WaveAudio : IDisposable
{
    // Keep 80 ms queued: legacy drivers may report completed blocks in batches.
    private const int Frames = 480, Count = 8;
    private readonly BeatSynth synth;
    private readonly AutoResetEvent signal = new(false);
    private readonly Thread thread;
    private volatile bool stopping;
    public event Action<string>? Failed;
    public string? Error { get; private set; }
    public WaveAudio(BeatSynth synth)
    {
        this.synth = synth;
        thread = new Thread(Run) { IsBackground = true, Name = "Metronome audio", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }
    private void Run()
    {
        IntPtr device = IntPtr.Zero;
        var data = new IntPtr[Count]; var headers = new IntPtr[Count]; var prepared = new bool[Count];
        try
        {
            var format = new WaveFormat { Format = 1, Channels = 1, SampleRate = BeatSynth.SampleRate,
                AvgBytes = BeatSynth.SampleRate * 2, BlockAlign = 2, Bits = 16 };
            Check(waveOutOpen(out device, uint.MaxValue, ref format, signal.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, 0x50000));
            int headerSize = Marshal.SizeOf<WaveHeader>();
            var samples = new short[Frames];
            for (int i = 0; i < Count; i++)
            {
                data[i] = Marshal.AllocHGlobal(Frames * 2); headers[i] = Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(new WaveHeader { Data = data[i], Length = Frames * 2 }, headers[i], false);
                Check(waveOutPrepareHeader(device, headers[i], headerSize)); prepared[i] = true;
                synth.Render(samples); Marshal.Copy(samples, 0, data[i], Frames);
                Check(waveOutWrite(device, headers[i], headerSize));
            }
            int next = 0;
            while (!stopping)
            {
                signal.WaitOne(20);
                for (int n = 0; n < Count && !stopping; n++)
                {
                    var h = Marshal.PtrToStructure<WaveHeader>(headers[next]);
                    if ((h.Flags & 1) == 0) break;
                    synth.Render(samples); Marshal.Copy(samples, 0, data[next], Frames);
                    Check(waveOutWrite(device, headers[next], headerSize));
                    next = (next + 1) % Count;
                }
            }
        }
        catch (Exception ex) { if (!stopping) { Error = "音频无法播放：" + ex.Message; Failed?.Invoke(Error); } }
        finally
        {
            if (device != IntPtr.Zero) waveOutReset(device);
            for (int i = 0; i < Count; i++)
            {
                if (prepared[i]) waveOutUnprepareHeader(device, headers[i], Marshal.SizeOf<WaveHeader>());
                if (headers[i] != IntPtr.Zero) Marshal.FreeHGlobal(headers[i]);
                if (data[i] != IntPtr.Zero) Marshal.FreeHGlobal(data[i]);
            }
            if (device != IntPtr.Zero) waveOutClose(device);
        }
    }
    private static void Check(uint code) { if (code != 0) throw new InvalidOperationException($"设备错误 {code}，请检查输出设备后重新打开。"); }
    public void Dispose() { stopping = true; signal.Set(); thread.Join(); signal.Dispose(); }
    [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct WaveFormat { public ushort Format, Channels; public uint SampleRate, AvgBytes; public ushort BlockAlign, Bits, Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct WaveHeader { public IntPtr Data; public uint Length, Recorded; public UIntPtr User; public uint Flags, Loops; public IntPtr Next; public UIntPtr Reserved; }
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr handle, uint id, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr handle);
}
