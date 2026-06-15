namespace NesEmulator.Core.Cartridge;

public interface IMapper
{
    bool CpuRead(ushort address, out byte data);
    bool CpuWrite(ushort address, byte data);
}
