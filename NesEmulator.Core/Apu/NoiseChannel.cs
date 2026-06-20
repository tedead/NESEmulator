using NesEmulator.Core;

namespace NesEmulator.Core.Apu;

internal sealed class NoiseChannel
{
    private static readonly ushort[] PeriodTableNtsc =
        [4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068];
    private static readonly ushort[] PeriodTablePal =
        [4, 7, 14, 30, 60, 88, 118, 148, 188, 236, 354, 472, 708, 944, 1890, 3778];

    public TvSystem TvSystem { get; set; } = TvSystem.Ntsc;
    private ushort[] PeriodTable => TvSystem == TvSystem.Pal ? PeriodTablePal : PeriodTableNtsc;

    public bool Enabled;
    public int  LengthCounter;

    private bool _halt, _constVol, _loopNoise;
    private int  _volume, _envDecay, _envPeriod, _envCounter;
    private bool _envStart;
    private int  _periodIdx, _timer;
    private ushort _lfsr = 1;

    public void WriteControl(byte d)
    {
        _halt      = (d & 0x20) != 0;
        _constVol  = (d & 0x10) != 0;
        _volume    = d & 0x0F;
        _envPeriod = d & 0x0F;
    }

    public void WritePeriod(byte d)
    {
        _loopNoise = (d & 0x80) != 0;
        _periodIdx = d & 0x0F;
    }

    public void WriteLength(byte d)
    {
        if (Enabled) LengthCounter = PulseChannel.LengthTable[(d >> 3) & 0x1F];
        _envStart = true;
    }

    public void ClockEnvelope()
    {
        if (_envStart)
        {
            _envStart   = false;
            _envDecay   = 15;
            _envCounter = _envPeriod;
        }
        else if (_envCounter > 0)
        {
            _envCounter--;
        }
        else
        {
            _envCounter = _envPeriod;
            if (_envDecay > 0) _envDecay--;
            else if (_halt) _envDecay = 15;
        }
    }

    public void ClockLength()
    {
        if (!_halt && LengthCounter > 0) LengthCounter--;
    }

    // Clocked every other CPU cycle
    public void ClockTimer()
    {
        if (_timer > 0) { _timer--; return; }
        _timer = PeriodTable[_periodIdx];
        // Feedback: bit 0 XOR bit 1 (normal) or bit 0 XOR bit 6 (short mode)
        int feedback = (_lfsr & 1) ^ (_loopNoise ? (_lfsr >> 6) & 1 : (_lfsr >> 1) & 1);
        _lfsr = (ushort)((_lfsr >> 1) | (feedback << 14));
    }

    public float Output()
    {
        if (!Enabled || LengthCounter == 0 || (_lfsr & 1) != 0) return 0f;
        return (_constVol ? _volume : _envDecay) / 15f;
    }

    public void SaveState(BinaryWriter bw)
    {
        bw.Write(Enabled); bw.Write(LengthCounter);
        bw.Write(_halt); bw.Write(_constVol); bw.Write(_loopNoise);
        bw.Write(_volume); bw.Write(_envPeriod); bw.Write(_envDecay);
        bw.Write(_envCounter); bw.Write(_envStart);
        bw.Write(_periodIdx); bw.Write(_timer); bw.Write(_lfsr);
    }

    public void LoadState(BinaryReader br)
    {
        Enabled = br.ReadBoolean(); LengthCounter = br.ReadInt32();
        _halt = br.ReadBoolean(); _constVol = br.ReadBoolean(); _loopNoise = br.ReadBoolean();
        _volume = br.ReadInt32(); _envPeriod = br.ReadInt32(); _envDecay = br.ReadInt32();
        _envCounter = br.ReadInt32(); _envStart = br.ReadBoolean();
        _periodIdx = br.ReadInt32(); _timer = br.ReadInt32(); _lfsr = br.ReadUInt16();
    }
}
