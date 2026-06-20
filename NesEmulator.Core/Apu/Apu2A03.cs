namespace NesEmulator.Core.Apu;

public sealed class Apu2A03
{
    public const int SampleRate = 44100;
    private const double CpuFrequency = 1_789_773.0;

    private readonly PulseChannel    _pulse1;
    private readonly PulseChannel    _pulse2;
    private readonly TriangleChannel _triangle = new();
    private readonly NoiseChannel    _noise    = new();
    private readonly DmcChannel      _dmc;

    private int  _cycle;
    private bool _oddCycle;
    private bool _frameMode;       // false = 4-step, true = 5-step
    private bool _frameIrqInhibit;
    private bool _frameIrqPending;

    private double _sampleAccum;

    // Filled during RunFrame(); drained by the audio player after each frame.
    public readonly List<float> Samples = new(1024);

    public bool IrqPending => _frameIrqPending || _dmc.IrqPending;

    public Apu2A03(Func<ushort, byte> cpuRead)
    {
        _pulse1 = new PulseChannel(false);
        _pulse2 = new PulseChannel(true);
        _dmc    = new DmcChannel(cpuRead);
    }

    // Called once per CPU clock from Bus.Clock()
    public void Clock()
    {
        _cycle++;
        _oddCycle = !_oddCycle;

        // Triangle clocks every CPU cycle; pulse/noise/DMC every other
        _triangle.ClockTimer();
        if (_oddCycle)
        {
            _pulse1.ClockTimer();
            _pulse2.ClockTimer();
            _noise.ClockTimer();
            _dmc.ClockTimer();
        }

        // Frame counter (4-step mode)
        if (!_frameMode)
        {
            if      (_cycle == 3729)  QuarterFrame();
            else if (_cycle == 7457)  { QuarterFrame(); HalfFrame(); }
            else if (_cycle == 11186) QuarterFrame();
            else if (_cycle == 14915) { QuarterFrame(); HalfFrame(); if (!_frameIrqInhibit) _frameIrqPending = true; }
            else if (_cycle == 14916) { if (!_frameIrqInhibit) _frameIrqPending = true; _cycle = 0; }
        }
        else // 5-step mode
        {
            if      (_cycle == 3729)  QuarterFrame();
            else if (_cycle == 7457)  { QuarterFrame(); HalfFrame(); }
            else if (_cycle == 11186) QuarterFrame();
            else if (_cycle == 18641) { QuarterFrame(); HalfFrame(); _cycle = 0; }
        }

        // Downsample to target rate
        _sampleAccum += SampleRate / CpuFrequency;
        if (_sampleAccum >= 1.0)
        {
            _sampleAccum -= 1.0;
            Samples.Add(Mix());
        }
    }

    private void QuarterFrame()
    {
        _pulse1.ClockEnvelope();
        _pulse2.ClockEnvelope();
        _triangle.ClockLinearCounter();
        _noise.ClockEnvelope();
    }

    private void HalfFrame()
    {
        _pulse1.ClockLengthAndSweep();
        _pulse2.ClockLengthAndSweep();
        _triangle.ClockLength();
        _noise.ClockLength();
    }

    // NES APU non-linear mixing formula (linear approximation)
    private float Mix()
    {
        float p1    = _pulse1.Output();
        float p2    = _pulse2.Output();
        float tri   = _triangle.Output();
        float noise = _noise.Output();
        float dmc   = _dmc.Output();

        float pulse = 0.00752f * (p1 + p2);
        float tnd   = 0.00851f * tri + 0.00494f * noise + 0.00335f * dmc;
        return pulse + tnd;
    }

    public void CpuWrite(ushort address, byte data)
    {
        switch (address)
        {
            case 0x4000: _pulse1.WriteControl(data);    break;
            case 0x4001: _pulse1.WriteSweep(data);      break;
            case 0x4002: _pulse1.WriteTimerLow(data);   break;
            case 0x4003: _pulse1.WriteTimerHigh(data);  break;
            case 0x4004: _pulse2.WriteControl(data);    break;
            case 0x4005: _pulse2.WriteSweep(data);      break;
            case 0x4006: _pulse2.WriteTimerLow(data);   break;
            case 0x4007: _pulse2.WriteTimerHigh(data);  break;
            case 0x4008: _triangle.WriteLinear(data);   break;
            case 0x400A: _triangle.WriteTimerLow(data); break;
            case 0x400B: _triangle.WriteTimerHigh(data);break;
            case 0x400C: _noise.WriteControl(data);     break;
            case 0x400E: _noise.WritePeriod(data);      break;
            case 0x400F: _noise.WriteLength(data);      break;
            case 0x4010: _dmc.WriteControl(data);       break;
            case 0x4011: _dmc.WriteDirectLoad(data);    break;
            case 0x4012: _dmc.WriteAddress(data);       break;
            case 0x4013: _dmc.WriteLength(data);        break;
            case 0x4015: WriteStatus(data);             break;
            case 0x4017: WriteFrameCounter(data);       break;
        }
    }

    public byte CpuRead(ushort address)
    {
        if (address != 0x4015) return 0;
        byte s = 0;
        if (_pulse1.LengthCounter   > 0) s |= 0x01;
        if (_pulse2.LengthCounter   > 0) s |= 0x02;
        if (_triangle.LengthCounter > 0) s |= 0x04;
        if (_noise.LengthCounter    > 0) s |= 0x08;
        if (_dmc.BytesRemaining     > 0) s |= 0x10;
        if (_frameIrqPending)           s |= 0x40;
        if (_dmc.IrqPending)            s |= 0x80;
        _frameIrqPending = false;
        return s;
    }

    private void WriteStatus(byte d)
    {
        _pulse1.Enabled   = (d & 0x01) != 0;
        _pulse2.Enabled   = (d & 0x02) != 0;
        _triangle.Enabled = (d & 0x04) != 0;
        _noise.Enabled    = (d & 0x08) != 0;
        _dmc.Enabled      = (d & 0x10) != 0;
        if (!_pulse1.Enabled)   _pulse1.LengthCounter   = 0;
        if (!_pulse2.Enabled)   _pulse2.LengthCounter   = 0;
        if (!_triangle.Enabled) _triangle.LengthCounter = 0;
        if (!_noise.Enabled)    _noise.LengthCounter    = 0;
        _dmc.IrqPending = false;
    }

    public void SaveState(BinaryWriter bw)
    {
        bw.Write(_cycle); bw.Write(_oddCycle);
        bw.Write(_frameMode); bw.Write(_frameIrqInhibit); bw.Write(_frameIrqPending);
        bw.Write(_sampleAccum);
        _pulse1.SaveState(bw); _pulse2.SaveState(bw);
        _triangle.SaveState(bw); _noise.SaveState(bw); _dmc.SaveState(bw);
    }

    public void LoadState(BinaryReader br)
    {
        _cycle    = br.ReadInt32(); _oddCycle = br.ReadBoolean();
        _frameMode = br.ReadBoolean(); _frameIrqInhibit = br.ReadBoolean();
        _frameIrqPending = br.ReadBoolean();
        _sampleAccum = br.ReadDouble();
        _pulse1.LoadState(br); _pulse2.LoadState(br);
        _triangle.LoadState(br); _noise.LoadState(br); _dmc.LoadState(br);
        Samples.Clear();
    }

    private void WriteFrameCounter(byte d)
    {
        _frameMode       = (d & 0x80) != 0;
        _frameIrqInhibit = (d & 0x40) != 0;
        if (_frameIrqInhibit) _frameIrqPending = false;
        _cycle = 0;
        if (_frameMode) { QuarterFrame(); HalfFrame(); }
    }
}
