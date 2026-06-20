using NesEmulator.Core;

namespace NesEmulator.Core.Cartridge;

public enum MirrorMode { Horizontal, Vertical, SingleLow, SingleHigh }

public sealed class Cartridge
{
    private readonly byte[]  _prgRom;
    private readonly byte[]  _chrRom;
    private readonly IMapper _mapper;

    public string     FileName        { get; }
    public int        MapperId        { get; }
    public MirrorMode Mirror          => _mapper.Mirror;
    public bool       IrqPending      => _mapper.IrqPending;
    public TvSystem   DetectedTvSystem { get; }

    private Cartridge(string fileName, byte[] prgRom, byte[] chrRom, int mapperId, IMapper mapper, TvSystem tvSystem)
    {
        FileName = fileName;
        _prgRom  = prgRom;
        _chrRom  = chrRom;
        MapperId = mapperId;
        _mapper  = mapper;
        DetectedTvSystem = tvSystem;
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
        byte[] chrRom = chrBanks > 0 ? br.ReadBytes(chrBanks * 8192) : [];

        IMapper mapper = mapperId switch
        {
            0 => new Mapper000(prgBanks, mirror),
            1 => new Mapper001(prgBanks, chrBanks),
            2 => new Mapper002(prgBanks, mirror),
            4 => new Mapper004(prgBanks, chrBanks),
            _ => throw new NotSupportedException($"Mapper {mapperId} is not yet supported.")
        };

        var tvSystem = DetectTvSystem(header);

        return new Cartridge(Path.GetFileName(path), prgRom, chrRom, mapperId, mapper, tvSystem);
    }

    // NES 2.0 (header[7] bits 2-3 == 10b) encodes TV system explicitly in byte 12.
    // Plain iNES 1.0 has no reliable region flag, but some dumps informally set
    // byte 9 bit 0 anyway; we honor it as a best-effort fallback. Default: NTSC.
    private static TvSystem DetectTvSystem(byte[] header)
    {
        bool isNes20 = (header[7] & 0x0C) == 0x08;
        if (isNes20)
        {
            int tv = header[12] & 0x03;
            return tv == 1 ? TvSystem.Pal : TvSystem.Ntsc; // 0=NTSC, 1=PAL, 2=dual, 3=Dendy
        }
        return (header[9] & 0x01) != 0 ? TvSystem.Pal : TvSystem.Ntsc;
    }

    public bool CpuRead (ushort address, out byte data) => _mapper.CpuRead (address, _prgRom, out data);
    public bool CpuWrite(ushort address, byte data)     => _mapper.CpuWrite(address, data);
    public bool PpuRead (ushort address, out byte data) => _mapper.PpuRead (address, _chrRom, out data);
    public bool PpuWrite(ushort address, byte data)     => _mapper.PpuWrite(address, _chrRom, data);

    public void OnScanline() => _mapper.OnScanline();

    public void SaveState(BinaryWriter bw) => _mapper.SaveState(bw);
    public void LoadState(BinaryReader br) => _mapper.LoadState(br);
}
