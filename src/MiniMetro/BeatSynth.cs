namespace MiniMetro;

public sealed record BeatSettings(bool Playing = false, int Bpm = 100, int Beats = 4, int Division = 1,
    bool Accent = true, double Volume = 0.65, int Sound = 0, bool Muted = false);

// Tempo is advanced by audio samples, so UI load cannot change the beat interval.
public sealed class BeatSynth
{
    public const int SampleRate = 48000;
    private BeatSettings settings = new();
    public BeatSettings Settings { get => Volatile.Read(ref settings); set => Volatile.Write(ref settings, value); }
    private readonly double[][] sounds = CreateSounds();
    private double phase;
    private long tick;
    private bool wasPlaying;
    private int previousDivision = 1;
    private int voicePosition = int.MaxValue;
    private double[]? voice;
    private double voiceGain;
    private int visibleBeat = -1;
    private int pulse;
    public int VisibleBeat => Volatile.Read(ref visibleBeat);
    public int Pulse => Volatile.Read(ref pulse);
    public long TotalTicks { get; private set; }
    public long RenderedSamples { get; private set; }

    public void Render(short[] buffer)
    {
        var s = Settings;
        if (s.Playing && !wasPlaying) { phase = 1; tick = 0; voicePosition = int.MaxValue; }
        if (s.Division != previousDivision) { tick = (tick / previousDivision) * s.Division; previousDivision = s.Division; }
        if (!s.Playing) { voicePosition = int.MaxValue; Volatile.Write(ref visibleBeat, -1); }
        wasPlaying = s.Playing;
        double step = Math.Clamp(s.Bpm, 20, 300) * Math.Clamp(s.Division, 1, 4) / (60.0 * SampleRate);
        for (int i = 0; i < buffer.Length; i++)
        {
            if (s.Playing && phase >= 1)
            {
                phase -= 1;
                bool whole = tick % s.Division == 0;
                int beat = (int)(tick / s.Division % s.Beats);
                bool strong = whole && beat == 0 && s.Accent;
                voice = sounds[Math.Clamp(s.Sound, 0, 2) * 2 + (strong ? 1 : 0)];
                voicePosition = 0;
                voiceGain = whole ? 1 : 0.36;
                if (whole) { Volatile.Write(ref visibleBeat, beat); Interlocked.Increment(ref pulse); }
                tick++; TotalTicks++;
            }
            double value = voice != null && voicePosition < voice.Length ? voice[voicePosition++] * voiceGain : 0;
            buffer[i] = s.Playing && !s.Muted ? (short)(value * Math.Clamp(s.Volume, 0, 1) * 28000) : (short)0;
            if (s.Playing) phase += step;
            RenderedSamples++;
        }
    }
    private static double[][] CreateSounds()
    {
        var result = new double[6][];
        for (int style = 0; style < 3; style++) for (int accent = 0; accent < 2; accent++)
        {
            int length = (int)(SampleRate * (style == 2 ? 0.035 : 0.022));
            var samples = new double[length];
            double f = (style == 0 ? 1050 : style == 1 ? 1800 : 850) * (accent == 1 ? 1.55 : 1);
            for (int i = 0; i < length; i++)
            {
                double t = i / (double)SampleRate;
                double envelope = Math.Min(1, i / 25.0) * Math.Exp(-t * (style == 2 ? 110 : 220));
                double v = Math.Sin(2 * Math.PI * f * t);
                if (style == 0) v = 0.72 * v + 0.28 * Math.Sin(2 * Math.PI * f * 2.73 * t);
                if (style == 1) v = 0.7 * v + 0.3 * Math.Sin(2 * Math.PI * f * 4.1 * t);
                samples[i] = v * envelope;
            }
            result[style * 2 + accent] = samples;
        }
        return result;
    }
}
