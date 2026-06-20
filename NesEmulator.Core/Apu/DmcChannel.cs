using NesEmulator.Core;

namespace NesEmulator.Core.Apu;

internal sealed class DmcChannel
{
    // CPU-cycle periods between output level changes
    private static readonly ushort[] RateTableNtsc =
        [428, 380, 340, 320, 286, 254, 226, 214, 190, 160, 142, 128, 106, 84, 72, 54];
    private static readonly ushort[] RateTablePal =
        [398, 354, 316, 298, 276, 236, 210, 198, 176, 148, 132, 118, 98, 78, 66, 50];

    public TvSystem TvSystem { get; set; } = TvSystem.Ntsc;
    private ushort[] RateTable => TvSystem == TvSystem.Pal ? RateTablePal : RateTableNtsc;

    private readonly Func<ushort, byte> _cpuRead;

    public bool IrqPending;
    public int  BytesRemaining => _bytesRemaining;

    private bool   _irqEnable, _loop;
    private int    _rateIdx;
    private int    _timer;
    private byte   _outputLevel;
    private ushort _sampleAddr, _currentAddr;
    private int    _sampleLen, _bytesRemaining;
    private byte   _shiftReg;
    private int    _bitsRemaining;
    private bool   _silence;

    public DmcChannel(Func<ushort, byte> cpuRead) => _cpuRead = cpuRead;

    public bool Enabled
    {
        get => _bytesRemaining > 0;
        set
        {
            if (value)
            {
                if (_bytesRemaining == 0) { _currentAddr = _sampleAddr; _bytesRemaining = _sampleLen; }
            }
            else
            {
                _bytesRemaining = 0;
            }
        }
    }

    public void WriteControl(byte d)
    {
        _irqEnable = (d & 0x80) != 0;
        _loop      = (d & 0x40) != 0;
        _rateIdx   = d & 0x0F;
        if (!_irqEnable) IrqPending = false;
    }

    public void WriteDirectLoad(byte d) => _outputLevel = (byte)(d & 0x7F);
    public void WriteAddress(byte d)    => _sampleAddr  = (ushort)(0xC000 | (d << 6));
    public void WriteLength(byte d)     => _sampleLen   = (d << 4) + 1;

    // Clocked every other CPU cycle; rate table is in CPU cycles so we use half-values
    public void ClockTimer()
    {
        if (_timer > 0) { _timer--; return; }
        _timer = RateTable[_rateIdx] / 2;

        if (!_silence)
        {
            if ((_shiftReg & 1) != 0) { if (_outputLevel <= 125) _outputLevel += 2; }
            else                      { if (_outputLevel >= 2)   _outputLevel -= 2; }
        }
        _shiftReg >>= 1;

        if (--_bitsRemaining <= 0)
        {
            _bitsRemaining = 8;
            if (_bytesRemaining == 0)
            {
                _silence = true;
                if (_loop)
                {
                    _currentAddr    = _sampleAddr;
                    _bytesRemaining = _sampleLen;
                }
                else if (_irqEnable) IrqPending = true;
            }

            if (_bytesRemaining > 0)
            {
                _silence  = false;
                _shiftReg = _cpuRead(_currentAddr);
                _currentAddr = _currentAddr == 0xFFFF ? (ushort)0x8000 : (ushort)(_currentAddr + 1);
                _bytesRemaining--;
            }
        }
    }

    public float Output() => _outputLevel / 127f;

    public void SaveState(BinaryWriter bw)
    {
        bw.Write(IrqPending);
        bw.Write(_irqEnable); bw.Write(_loop); bw.Write(_rateIdx); bw.Write(_timer);
        bw.Write(_outputLevel);
        bw.Write(_sampleAddr); bw.Write(_currentAddr);
        bw.Write(_sampleLen); bw.Write(_bytesRemaining);
        bw.Write(_shiftReg); bw.Write(_bitsRemaining); bw.Write(_silence);
    }

    public void LoadState(BinaryReader br)
    {
        IrqPending = br.ReadBoolean();
        _irqEnable = br.ReadBoolean(); _loop = br.ReadBoolean();
        _rateIdx = br.ReadInt32(); _timer = br.ReadInt32();
        _outputLevel = br.ReadByte();
        _sampleAddr = br.ReadUInt16(); _currentAddr = br.ReadUInt16();
        _sampleLen = br.ReadInt32(); _bytesRemaining = br.ReadInt32();
        _shiftReg = br.ReadByte(); _bitsRemaining = br.ReadInt32(); _silence = br.ReadBoolean();
    }
}
