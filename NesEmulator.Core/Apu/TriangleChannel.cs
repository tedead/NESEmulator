namespace NesEmulator.Core.Apu;

internal sealed class TriangleChannel
{
    // 32-step triangle sequence: ramp down then up
    private static readonly byte[] Steps =
        [15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0,
          0,  1,  2,  3,  4,  5, 6, 7, 8, 9,10,11,12,13,14,15];

    public bool Enabled;
    public int  LengthCounter;

    private bool _halt, _linReload;
    private int  _linLoad, _linCounter;
    private int  _timerPeriod, _timer, _step;

    public void WriteLinear(byte d)
    {
        _halt    = (d & 0x80) != 0;
        _linLoad = d & 0x7F;
    }

    public void WriteTimerLow(byte d)  => _timerPeriod = (_timerPeriod & 0x700) | d;

    public void WriteTimerHigh(byte d)
    {
        _timerPeriod = (_timerPeriod & 0x0FF) | ((d & 7) << 8);
        if (Enabled) LengthCounter = PulseChannel.LengthTable[(d >> 3) & 0x1F];
        _linReload = true;
    }

    public void ClockLinearCounter()
    {
        if (_linReload) _linCounter = _linLoad;
        else if (_linCounter > 0) _linCounter--;
        if (!_halt) _linReload = false;
    }

    public void ClockLength()
    {
        if (!_halt && LengthCounter > 0) LengthCounter--;
    }

    // Clocked every CPU cycle
    public void ClockTimer()
    {
        if (_timer > 0) _timer--;
        else
        {
            _timer = _timerPeriod;
            if (LengthCounter > 0 && _linCounter > 0)
                _step = (_step + 1) % 32;
        }
    }

    public float Output()
    {
        if (!Enabled || LengthCounter == 0 || _linCounter == 0) return 0f;
        if (_timerPeriod < 2) return 0f; // ultrasonic frequencies: silence to avoid buzz
        return Steps[_step] / 15f;
    }

    public void SaveState(BinaryWriter bw)
    {
        bw.Write(Enabled); bw.Write(LengthCounter);
        bw.Write(_halt); bw.Write(_linReload);
        bw.Write(_linLoad); bw.Write(_linCounter);
        bw.Write(_timerPeriod); bw.Write(_timer); bw.Write(_step);
    }

    public void LoadState(BinaryReader br)
    {
        Enabled = br.ReadBoolean(); LengthCounter = br.ReadInt32();
        _halt = br.ReadBoolean(); _linReload = br.ReadBoolean();
        _linLoad = br.ReadInt32(); _linCounter = br.ReadInt32();
        _timerPeriod = br.ReadInt32(); _timer = br.ReadInt32(); _step = br.ReadInt32();
    }
}
