namespace NesEmulator.Core.Cartridge;

// NROM: no banking. 16K PRG mirrors to fill $8000-$FFFF, or straight 32K.
public sealed class Mapper000(int prgBanks, MirrorMode mirror) : IMapper
{
    private readonly ushort _prgMask = prgBanks > 1 ? (ushort)0x7FFF : (ushort)0x3FFF;

    public MirrorMode Mirror => mirror;
    public bool        IrqPending => false;
    public void OnScanline() { }

    public bool CpuRead(ushort address, byte[] prgRom, out byte data)
    {
        data = 0;
        if (address < 0x8000) return false;
        data = prgRom[(address - 0x8000) & _prgMask];
        return true;
    }

    public bool CpuWrite(ushort address, byte data) => false;

    public bool PpuRead(ushort address, byte[] chrRom, out byte data)
    {
        data = 0;
        if (address > 0x1FFF) return false;
        data = chrRom[address];
        return true;
    }

    public bool PpuWrite(ushort address, byte[] chrRom, byte data) => false;

    // Mapper 0 has no dynamic state — nothing to save/load.
    public void SaveState(BinaryWriter bw) { }
    public void LoadState(BinaryReader br) { }
}
