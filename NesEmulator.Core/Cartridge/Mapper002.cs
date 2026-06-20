namespace NesEmulator.Core.Cartridge;

// UxROM: switchable 16K PRG bank at $8000, fixed last 16K bank at $C000.
// CHR is always 8K of CHR-RAM (no CHR banking). Mirroring is fixed (from header).
// Covers Mega Man, Castlevania, Contra, Duck Tales, and ~11% of the library.
public sealed class Mapper002(int prgBanks, MirrorMode mirror) : IMapper
{
    private readonly byte[] _chrRam = new byte[8192];
    private int _prgBank;

    public MirrorMode Mirror     => mirror;
    public bool        IrqPending => false;
    public void OnScanline() { }

    public bool CpuRead(ushort address, byte[] prgRom, out byte data)
    {
        data = 0;
        if (address < 0x8000) return false;
        data = address < 0xC000
            ? prgRom[(_prgBank * 0x4000 + (address - 0x8000)) % prgRom.Length]
            : prgRom[((prgBanks - 1) * 0x4000 + (address - 0xC000)) % prgRom.Length];
        return true;
    }

    public bool CpuWrite(ushort address, byte data)
    {
        if (address < 0x8000) return false;
        _prgBank = data & 0x0F;
        return true;
    }

    public bool PpuRead(ushort address, byte[] chrRom, out byte data)
    {
        data = 0;
        if (address > 0x1FFF) return false;
        data = chrRom.Length > 0 ? chrRom[address] : _chrRam[address];
        return true;
    }

    public bool PpuWrite(ushort address, byte[] chrRom, byte data)
    {
        if (address > 0x1FFF || chrRom.Length > 0) return false;
        _chrRam[address] = data;
        return true;
    }

    public void SaveState(BinaryWriter bw) { bw.Write(_prgBank); bw.Write(_chrRam); }
    public void LoadState(BinaryReader br) { _prgBank = br.ReadInt32(); br.Read(_chrRam); }
}
