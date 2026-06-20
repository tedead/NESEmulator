using NAudio.Wave;

namespace NesEmulator.Desktop.Audio;

public sealed class NesAudioProvider : IDisposable
{
    private readonly WaveOutEvent         _waveOut;
    private readonly BufferedWaveProvider _buffer;
    private readonly byte[]               _scratch = new byte[4096 * 4];

    public NesAudioProvider()
    {
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        _buffer = new BufferedWaveProvider(fmt)
        {
            BufferDuration          = TimeSpan.FromMilliseconds(300),
            DiscardOnBufferOverflow = true,
        };
        _waveOut = new WaveOutEvent { DesiredLatency = 80 };
        _waveOut.Init(_buffer);
        _waveOut.Play();
    }

    // Called once per rendered frame to push ~735 samples from the APU buffer.
    public void Submit(List<float> samples)
    {
        if (samples.Count == 0) return;

        int byteCount = samples.Count * 4;
        if (_scratch.Length < byteCount)
        {
            // reallocate only if the frame produced unusually many samples
            Submit(samples, new byte[byteCount]);
            samples.Clear();
            return;
        }

        Submit(samples, _scratch);
        samples.Clear();
    }

    private void Submit(List<float> samples, byte[] buf)
    {
        for (int i = 0; i < samples.Count; i++)
        {
            float s = Math.Clamp(samples[i], -1f, 1f);
            BitConverter.TryWriteBytes(buf.AsSpan(i * 4), s);
        }
        _buffer.AddSamples(buf, 0, samples.Count * 4);
    }

    public void Dispose()
    {
        _waveOut.Stop();
        _waveOut.Dispose();
    }
}
