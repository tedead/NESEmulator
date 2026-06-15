namespace NesEmulator.Core.Cartridge;

public enum MirrorMode { Horizontal, Vertical, SingleLow, SingleHigh }

public sealed class Cartridge
{
    private readonly byte[] _prgRom;
    private readonly byte[] _chrRom;
    private readonly Mapper000 _mapper;

    public string     FileName   { get; }
    public int        MapperId   { get; }
    public MirrorMode Mirror     { get; }

    private Cartridge(string fileName, byte[] prgRom, byte[] chrRom, int mapperId, int prgBanks, MirrorMode mirror)
    {
        FileName = fileName;
        _prgRom  = prgRom;
        _chrRom  = chrRom;
        MapperId = mapperId;
        Mirror   = mirror;
        _mapper  = new Mapper000(prgBanks);
    }

    public static Cartridge Load(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        var header = br.ReadBytes(16);
        if (header[0] != 'N' || header[1] != 'E' || header[2] != 'S' || header[3] != 0x1A)
            throw new InvalidDataException("Not a valid iNES ROM file.");

        int prgBanks = header[4];
        int chrBanks = header[5];
        int mapperId = (header[7] & 0xF0) | (header[6] >> 4);

        var mirror = (header[6] & 0x08) != 0 ? MirrorMode.SingleLow
                   : (header[6] & 0x01) != 0 ? MirrorMode.Vertical
                   : MirrorMode.Horizontal;

        if ((header[6] & 0x04) != 0) br.ReadBytes(512); // skip trainer

        byte[] prgRom = br.ReadBytes(prgBanks * 16384);
        byte[] chrRom = chrBanks > 0 ? br.ReadBytes(chrBanks * 8192) : new byte[8192];

        if (mapperId != 0)
            throw new NotSupportedException($"Mapper {mapperId} is not yet supported. Only Mapper 0 (NROM) is implemented.");

        return new Cartridge(Path.GetFileName(path), prgRom, chrRom, mapperId, prgBanks, mirror);
    }

    public bool CpuRead(ushort address, out byte data)
    {
        data = 0;
        if (address < 0x8000) return false;
        data = _prgRom[(address - 0x8000) & _mapper.PrgMask];
        return true;
    }

    public bool CpuWrite(ushort address, byte data) => false;

    public bool PpuRead(ushort address, out byte data)
    {
        data = 0;
        if (address > 0x1FFF) return false;
        data = _chrRom[address];
        return true;
    }

    public bool PpuWrite(ushort address, byte data) => false; // CHR RAM not supported yet
}
