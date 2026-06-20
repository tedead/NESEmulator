namespace NesEmulator.Core.Cartridge;

public interface IMapper
{
    MirrorMode Mirror { get; }
    bool CpuRead (ushort address, byte[] prgRom, out byte data);
    bool CpuWrite(ushort address, byte data);
    bool PpuRead (ushort address, byte[] chrRom, out byte data);
    bool PpuWrite(ushort address, byte[] chrRom, byte data);
    void SaveState(BinaryWriter bw);
    void LoadState(BinaryReader br);
}
