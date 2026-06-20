namespace NesEmulator.Core.Apu;

internal sealed class PulseChannel
{
    private static readonly byte[][] DutyTable =
    [
        [0, 1, 0, 0, 0, 0, 0, 0], // 12.5%
        [0, 1, 1, 0, 0, 0, 0, 0], // 25%
        [0, 1, 1, 1, 1, 0, 0, 0], // 50%
        [1, 0, 0, 1, 1, 1, 1, 1], // 25% negated
    ];

    internal static readonly byte[] LengthTable =
    [
        10, 254, 20,  2, 40,  4, 80,  6,
       160,   8, 60, 10, 14, 12, 26, 14,
        12,  16, 24, 18, 48, 20, 96, 22,
       192,  24, 72, 26, 16, 28, 32, 30,
    ];

    public bool Enabled;
    public int  LengthCounter;

    private readonly bool _negate2;   // channel 2 uses ones-complement negation
    private int  _duty, _timerPeriod, _timer, _dutyPos;
    private bool _haltLength, _constVol;
    private int  _volume, _envDecay, _envPeriod, _envCounter;
    private bool _envStart;
    private bool _sweepEnable;
    private int  _sweepPeriod, _sweepShift, _sweepCounter;
    private bool _sweepNegate, _sweepReload;

    public PulseChannel(bool negate2) => _negate2 = negate2;

    public void WriteControl(byte d)
    {
        _duty        = (d >> 6) & 3;
        _haltLength  = (d & 0x20) != 0;
        _constVol    = (d & 0x10) != 0;
        _volume      = d & 0x0F;
        _envPeriod   = d & 0x0F;
    }

    public void WriteSweep(byte d)
    {
        _sweepEnable = (d & 0x80) != 0;
        _sweepPeriod = (d >> 4) & 7;
        _sweepNegate = (d & 0x08) != 0;
        _sweepShift  = d & 7;
        _sweepReload = true;
    }

    public void WriteTimerLow(byte d)  => _timerPeriod = (_timerPeriod & 0x700) | d;

    public void WriteTimerHigh(byte d)
    {
        _timerPeriod = (_timerPeriod & 0x0FF) | ((d & 7) << 8);
        if (Enabled) LengthCounter = LengthTable[(d >> 3) & 0x1F];
        _dutyPos  = 0;
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
            else if (_haltLength) _envDecay = 15; // loop
        }
    }

    public void ClockLengthAndSweep()
    {
        if (!_haltLength && LengthCounter > 0) LengthCounter--;

        if (_sweepReload) { _sweepCounter = _sweepPeriod; _sweepReload = false; }
        else if (_sweepCounter > 0) { _sweepCounter--; }
        else
        {
            _sweepCounter = _sweepPeriod;
            if (_sweepEnable && _sweepShift > 0 && !Muting())
            {
                int delta = _timerPeriod >> _sweepShift;
                // Channel 1: ones-complement, channel 2: twos-complement
                _timerPeriod += _sweepNegate ? (_negate2 ? -delta : -(delta + 1)) : delta;
            }
        }
    }

    // Clocked every other CPU cycle (APU cycle)
    public void ClockTimer()
    {
        if (_timer > 0) _timer--;
        else { _timer = _timerPeriod; _dutyPos = (_dutyPos + 1) & 7; }
    }

    private bool Muting() => _timerPeriod < 8 || _timerPeriod > 0x7FF;

    public float Output()
    {
        if (!Enabled || LengthCounter == 0 || Muting() || DutyTable[_duty][_dutyPos] == 0) return 0f;
        return (_constVol ? _volume : _envDecay) / 15f;
    }

    public void SaveState(BinaryWriter bw)
    {
        bw.Write(Enabled); bw.Write(LengthCounter);
        bw.Write(_duty); bw.Write(_timerPeriod); bw.Write(_timer); bw.Write(_dutyPos);
        bw.Write(_haltLength); bw.Write(_constVol);
        bw.Write(_volume); bw.Write(_envPeriod); bw.Write(_envDecay); bw.Write(_envCounter);
        bw.Write(_envStart);
        bw.Write(_sweepEnable); bw.Write(_sweepPeriod); bw.Write(_sweepShift);
        bw.Write(_sweepCounter); bw.Write(_sweepNegate); bw.Write(_sweepReload);
    }

    public void LoadState(BinaryReader br)
    {
        Enabled = br.ReadBoolean(); LengthCounter = br.ReadInt32();
        _duty = br.ReadInt32(); _timerPeriod = br.ReadInt32();
        _timer = br.ReadInt32(); _dutyPos = br.ReadInt32();
        _haltLength = br.ReadBoolean(); _constVol = br.ReadBoolean();
        _volume = br.ReadInt32(); _envPeriod = br.ReadInt32();
        _envDecay = br.ReadInt32(); _envCounter = br.ReadInt32();
        _envStart = br.ReadBoolean();
        _sweepEnable = br.ReadBoolean(); _sweepPeriod = br.ReadInt32();
        _sweepShift = br.ReadInt32(); _sweepCounter = br.ReadInt32();
        _sweepNegate = br.ReadBoolean(); _sweepReload = br.ReadBoolean();
    }
}
