namespace NesEmulator.Core.Cartridge;

// MMC1 (SxROM): banked PRG (16K or 32K) + banked CHR (4K or 8K) + dynamic mirroring.
// Covers ~28% of the NES library: Zelda, Metroid, Mega Man 2, Castlevania, Tetris, etc.
public sealed class Mapper001(int prgBanks, int chrBanks) : IMapper
{
    // Internal registers loaded via 5-bit serial shift
    private byte _shiftReg = 0x10; // bit 4 set = empty (counts 5 writes)
    private byte _control  = 0x0C; // PRG mode 3 on power-up (fix last bank at $C000)
    private byte _chrBank0;
    private byte _chrBank1;
    private byte _prgBank;

    // CHR RAM for games that have no CHR ROM (e.g. Zelda)
    private readonly byte[] _chrRam = chrBanks == 0 ? new byte[8192] : [];

    public bool IrqPending => false;
    public void OnScanline() { }

    public MirrorMode Mirror => (_control & 0x03) switch
    {
        0 => MirrorMode.SingleLow,
        1 => MirrorMode.SingleHigh,
        2 => MirrorMode.Vertical,
        _ => MirrorMode.Horizontal,
    };

    public bool CpuRead(ushort address, byte[] prgRom, out byte data)
    {

        int prgMode = (_control >> 2) & 0x03;

        int bank = _prgBank & 0x0F;

        data = 0;

        if (address < 0x8000)
        {
            return false;
        }

        if (prgMode <= 1) //32KB switching: treat bank as 32KB index (ignore low bit)
        {
            int bank32 = bank >> 1;

            data = prgRom[(bank32 * 0x8000 + (address - 0x8000)) % prgRom.Length];
        }
        else if (prgMode == 2) //Fix first 16KB at $8000, switch 16KB at $C000
        {
            if (address < 0xC000)
            {
                data = prgRom[address - 0x8000];
            }
            else
            {
                data = prgRom[(bank * 0x4000 + (address - 0xC000)) % prgRom.Length];
            }
        }
        else //Fix last 16KB at $C000, switch 16KB at $8000
        {
            if (address >= 0xC000)
            {
                data = prgRom[((prgBanks - 1) * 0x4000 + (address - 0xC000)) % prgRom.Length];
            }
            else
            {
                data = prgRom[(bank * 0x4000 + (address - 0x8000)) % prgRom.Length];
            }
        }

        return true;
    }

    public bool CpuWrite(ushort address, byte data)
    {
        if (address < 0x8000)
        { 
            return false;
        }

        if ((data & 0x80) != 0) //Reset bit
        {
            _shiftReg  = 0x10;
            _control  |= 0x0C;
            return true;
        }

        bool complete = (_shiftReg & 1) != 0; //Low bit of shift = "this is the 5th write"}
        _shiftReg = (byte)((_shiftReg >> 1) | ((data & 1) << 4));

        if (complete)
        {
            byte val  = _shiftReg;
            _shiftReg = 0x10;

            if      (address <= 0x9FFF) _control  = val;
            else if (address <= 0xBFFF) _chrBank0 = val;
            else if (address <= 0xDFFF) _chrBank1 = val;
            else                        _prgBank  = val;
        }
        return true;
    }

    public bool PpuRead(ushort address, byte[] chrRom, out byte data)
    {
        data = 0;

        if (address > 0x1FFF)
        {
            return false;
        }

        byte[] src = chrRom.Length > 0 ? chrRom : _chrRam;

        if ((_control & 0x10) == 0) //8KB CHR mode
        {
            int bank = _chrBank0 & ~1; //Ignore low bit

            data = src[(bank * 0x1000 + address) % src.Length];
        }
        else // 4KB CHR mode
        {
            if (address < 0x1000)
                data = src[(_chrBank0 * 0x1000 + address) % src.Length];
            else
                data = src[(_chrBank1 * 0x1000 + (address - 0x1000)) % src.Length];
        }
        return true;
    }

    public bool PpuWrite(ushort address, byte[] chrRom, byte data)
    {
        if (address > 0x1FFF) return false;
        if (chrRom.Length > 0) return false; // CHR ROM is read-only
        _chrRam[address] = data;
        return true;
    }

    public void SaveState(BinaryWriter bw)
    {
        bw.Write(_shiftReg);
        bw.Write(_control);
        bw.Write(_chrBank0);
        bw.Write(_chrBank1);
        bw.Write(_prgBank);
        bw.Write(_chrRam);
    }

    public void LoadState(BinaryReader br)
    {
        _shiftReg = br.ReadByte();
        _control  = br.ReadByte();
        _chrBank0 = br.ReadByte();
        _chrBank1 = br.ReadByte();
        _prgBank  = br.ReadByte();
        br.Read(_chrRam);
    }
}
