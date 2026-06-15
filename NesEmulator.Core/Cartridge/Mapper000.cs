namespace NesEmulator.Core.Cartridge;

// NROM: no banking. 16K PRG mirrors, or straight 32K PRG.
public sealed class Mapper000(int prgBanks) : IMapper
{
    public bool CpuRead(ushort address, out byte data)
    {
        data = 0;
        if (address < 0x8000) return false;
        // Mirror mask: 16K ROM uses 0x3FFF, 32K uses 0x7FFF
        data = 0; // data filled by Cartridge
        return true;
    }

    // Mask for PRG address: 16K banks mirror, 32K don't
    public ushort PrgMask => prgBanks > 1 ? (ushort)0x7FFF : (ushort)0x3FFF;

    public bool CpuWrite(ushort address, byte data) => false; // ROM is read-only
}
