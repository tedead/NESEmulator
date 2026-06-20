namespace NesEmulator.Core.Cartridge;

// MMC3 (TxROM): 8K-granularity PRG banking with fixed/swap modes, fine-grained
// CHR banking (2K/1K), switchable H/V mirroring, and a scanline-based IRQ
// counter (simplified PPU-A12-edge approximation, clocked once per visible
// scanline). Covers Super Mario Bros 2 & 3, Mega Man 3-6, Kirby's Adventure,
// and ~24% of the library.
public sealed class Mapper004 : IMapper
{
    private readonly int    _prgBanks8k; // total count of 8K PRG banks
    private readonly bool   _useChrRam;
    private readonly byte[] _chrRam;

    private readonly byte[] _reg = new byte[8]; // R0-R7 bank registers
    private byte _bankSelect;
    private bool _prgMode;  // $8000 bit 6
    private bool _chrMode;  // $8000 bit 7
    private MirrorMode _mirror = MirrorMode.Vertical;

    private byte _irqLatch;
    private byte _irqCounter;
    private bool _irqReload;
    private bool _irqEnabled;
    private bool _irqPending;

    public Mapper004(int prgBanks16k, int chrBanks8k)
    {
        _prgBanks8k = prgBanks16k * 2;
        _useChrRam  = chrBanks8k == 0;
        _chrRam     = _useChrRam ? new byte[8192] : [];
    }

    public MirrorMode Mirror     => _mirror;
    public bool        IrqPending => _irqPending;

    public bool CpuRead(ushort address, byte[] prgRom, out byte data)
    {
        data = 0;
        if (address < 0x8000) return false;
        int bank = ResolvePrgBank(address);
        int off  = address & 0x1FFF;
        data = prgRom[(bank * 0x2000 + off) % prgRom.Length];
        return true;
    }

    private int ResolvePrgBank(ushort address)
    {
        int slot = (address - 0x8000) >> 13; // 0..3, each 8K

        // PRG mode 0: [ R6 ][ R7 ][ -2 ][ -1 ]
        // PRG mode 1: [ -2 ][ R7 ][ R6 ][ -1 ]
        if (!_prgMode)
        {
            return slot switch
            {
                0 => _reg[6] & 0x3F,
                1 => _reg[7] & 0x3F,
                2 => _prgBanks8k - 2,
                _ => _prgBanks8k - 1,
            };
        }
        return slot switch
        {
            0 => _prgBanks8k - 2,
            1 => _reg[7] & 0x3F,
            2 => _reg[6] & 0x3F,
            _ => _prgBanks8k - 1,
        };
    }

    public bool CpuWrite(ushort address, byte data)
    {
        if (address < 0x8000) return false;
        bool even = (address & 1) == 0;

        if (address <= 0x9FFF)
        {
            if (even) { _bankSelect = data; _prgMode = (data & 0x40) != 0; _chrMode = (data & 0x80) != 0; }
            else        _reg[_bankSelect & 0x07] = data;
        }
        else if (address <= 0xBFFF)
        {
            if (even) _mirror = (data & 0x01) != 0 ? MirrorMode.Horizontal : MirrorMode.Vertical;
            // odd ($A001): PRG-RAM protect — not implemented (no PRG-RAM support)
        }
        else if (address <= 0xDFFF)
        {
            if (even) _irqLatch  = data;
            else      _irqReload = true;
        }
        else
        {
            if (even) { _irqEnabled = false; _irqPending = false; } // disable + acknowledge
            else        _irqEnabled = true;
        }
        return true;
    }

    public bool PpuRead(ushort address, byte[] chrRom, out byte data)
    {
        data = 0;
        if (address > 0x1FFF) return false;
        byte[] src = _useChrRam ? _chrRam : chrRom;
        int bank = ResolveChrBank(address, out int off);
        data = src[(bank * 0x0400 + off) % src.Length];
        return true;
    }

    private int ResolveChrBank(ushort address, out int offset)
    {
        int slot = address >> 10; // which 1K region, 0..7
        offset = address & 0x03FF;

        // CHR mode 0: [   R0 2K  ][   R1 2K  ][R2][R3][R4][R5]  (1K each for R2-R5)
        // CHR mode 1: [R2][R3][R4][R5][   R0 2K  ][   R1 2K  ]
        if (!_chrMode)
        {
            return slot switch
            {
                0 => _reg[0] & 0xFE,
                1 => (_reg[0] & 0xFE) + 1,
                2 => _reg[1] & 0xFE,
                3 => (_reg[1] & 0xFE) + 1,
                4 => _reg[2],
                5 => _reg[3],
                6 => _reg[4],
                _ => _reg[5],
            };
        }
        return slot switch
        {
            0 => _reg[2],
            1 => _reg[3],
            2 => _reg[4],
            3 => _reg[5],
            4 => _reg[0] & 0xFE,
            5 => (_reg[0] & 0xFE) + 1,
            6 => _reg[1] & 0xFE,
            _ => (_reg[1] & 0xFE) + 1,
        };
    }

    public bool PpuWrite(ushort address, byte[] chrRom, byte data)
    {
        if (address > 0x1FFF || !_useChrRam) return false;
        int bank = ResolveChrBank(address, out int off);
        _chrRam[(bank * 0x0400 + off) % _chrRam.Length] = data;
        return true;
    }

    // Simplified IRQ counter: real MMC3 clocks on PPU A12 rising edges; this
    // approximates that with one clock per visible scanline, which is accurate
    // enough for status-bar split effects (e.g. Super Mario Bros 3).
    public void OnScanline()
    {
        if (_irqCounter == 0 || _irqReload)
        {
            _irqCounter = _irqLatch;
            _irqReload  = false;
        }
        else
        {
            _irqCounter--;
        }

        if (_irqCounter == 0 && _irqEnabled)
            _irqPending = true;
    }

    public void SaveState(BinaryWriter bw)
    {
        bw.Write(_reg);
        bw.Write(_bankSelect); bw.Write(_prgMode); bw.Write(_chrMode);
        bw.Write((int)_mirror);
        bw.Write(_irqLatch); bw.Write(_irqCounter);
        bw.Write(_irqReload); bw.Write(_irqEnabled); bw.Write(_irqPending);
        if (_useChrRam) bw.Write(_chrRam);
    }

    public void LoadState(BinaryReader br)
    {
        br.Read(_reg);
        _bankSelect = br.ReadByte(); _prgMode = br.ReadBoolean(); _chrMode = br.ReadBoolean();
        _mirror = (MirrorMode)br.ReadInt32();
        _irqLatch = br.ReadByte(); _irqCounter = br.ReadByte();
        _irqReload = br.ReadBoolean(); _irqEnabled = br.ReadBoolean(); _irqPending = br.ReadBoolean();
        if (_useChrRam) br.Read(_chrRam);
    }
}
