using Cart = NesEmulator.Core.Cartridge.Cartridge;
using NesEmulator.Core.Cartridge;
using NesEmulator.Core;

namespace NesEmulator.Core.Ppu;

/// <summary>
/// NES Picture Processing Unit (2C02).
/// Implements background rendering, 8x8 sprites, VBlank/NMI, and the loopy scroll mechanism.
/// </summary>
public sealed partial class Ppu2C02
{
    // ── NTSC system palette (64 colours, ARGB) ────────────────────────────────
    private static readonly uint[] SystemPalette =
    [
        0xFF545454, 0xFF001E74, 0xFF081090, 0xFF300088, 0xFF440064, 0xFF5C0030, 0xFF540400, 0xFF3C1800,
        0xFF202A00, 0xFF083A00, 0xFF004000, 0xFF003C00, 0xFF00323C, 0xFF000000, 0xFF000000, 0xFF000000,
        0xFF989698, 0xFF084CC4, 0xFF3032EC, 0xFF5C1EE4, 0xFF8814B0, 0xFFA01464, 0xFF982220, 0xFF783C00,
        0xFF545A00, 0xFF287200, 0xFF087C00, 0xFF007628, 0xFF006678, 0xFF000000, 0xFF000000, 0xFF000000,
        0xFFECEEEC, 0xFF4C9AEC, 0xFF787CEC, 0xFFB062EC, 0xFFE454EC, 0xFFEC58B4, 0xFFEC6A64, 0xFFD48820,
        0xFFA0AA00, 0xFF74C400, 0xFF4CD020, 0xFF38CC6C, 0xFF38B4CC, 0xFF3C3C3C, 0xFF000000, 0xFF000000,
        0xFFECEEEC, 0xFFA8CCEC, 0xFFBCBCEC, 0xFFD4B2EC, 0xFFECAEEC, 0xFFECAED4, 0xFFECB4B0, 0xFFE4C490,
        0xFFCCD278, 0xFFB4DE78, 0xFFA8E290, 0xFF98E2B4, 0xFFA0D6E4, 0xFFA0A2A0, 0xFF000000, 0xFF000000,
    ];

    // ── CPU-visible registers ($2000–$2007) ───────────────────────────────────
    private byte _ctrl;    // PPUCTRL   $2000
    private byte _mask;    // PPUMASK   $2001
    private byte _status;  // PPUSTATUS $2002
    private byte _oamAddr; // OAMADDR   $2003

    // PPUCTRL helpers
    private bool CtrlNmiEnable      => (_ctrl & 0x80) != 0;
    private bool CtrlSpriteSize     => (_ctrl & 0x20) != 0; // 0=8x8, 1=8x16
    private int  CtrlBgPatternBase  => (_ctrl & 0x10) != 0 ? 0x1000 : 0x0000;
    private int  CtrlSprPatternBase => (_ctrl & 0x08) != 0 ? 0x1000 : 0x0000;
    private int  CtrlVramIncrement  => (_ctrl & 0x04) != 0 ? 32 : 1;

    // PPUMASK helpers
    private bool MaskShowBg      => (_mask & 0x08) != 0;
    private bool MaskShowSprites => (_mask & 0x10) != 0;
    private bool MaskShowBgLeft  => (_mask & 0x02) != 0;
    private bool MaskShowSprLeft => (_mask & 0x04) != 0;
    private bool RenderingEnabled => MaskShowBg || MaskShowSprites;

    // ── Loopy scroll registers ────────────────────────────────────────────────
    private ushort _v;   // current VRAM address
    private ushort _t;   // temporary VRAM address
    private byte   _x;   // fine X scroll (3 bits)
    private bool   _w;   // first/second write latch

    // Loopy v/t field accessors
    private int  V_CoarseX   { get => _v & 0x1F;         set => _v = (ushort)((_v & ~0x001F) | (value & 0x1F)); }
    private int  V_CoarseY   { get => (_v >> 5) & 0x1F;  set => _v = (ushort)((_v & ~0x03E0) | ((value & 0x1F) << 5)); }
    private int  V_NtX       { get => (_v >> 10) & 1;    set => _v = (ushort)((_v & ~0x0400) | ((value & 1) << 10)); }
    private int  V_NtY       { get => (_v >> 11) & 1;    set => _v = (ushort)((_v & ~0x0800) | ((value & 1) << 11)); }
    private int  V_FineY     { get => (_v >> 12) & 7;    set => _v = (ushort)((_v & ~0x7000) | ((value & 7) << 12)); }

    // ── PPU-internal memory ───────────────────────────────────────────────────
    private readonly byte[,] _nameTable = new byte[2, 1024]; // 2 nametables × 1K
    private readonly byte[]  _palette   = new byte[32];
    public  readonly byte[]  Oam        = new byte[256];     // primary OAM

    public TvSystem TvSystem { get; set; } = TvSystem.Ntsc;

    // ── Cartridge ─────────────────────────────────────────────────────────────
    private Cart? _cartridge;

    public void InsertCartridge(Cart cart) => _cartridge = cart;

    // ── Output / signals ──────────────────────────────────────────────────────
    public readonly uint[] FrameBuffer = new uint[256 * 240];
    public bool FrameComplete  { get; private set; }
    public bool NmiRequested   { get; private set; }

    public void ClearFrameComplete() => FrameComplete = false;
    public void ClearNmi()           => NmiRequested  = false;

    // ── CPU read/write of the 8 PPU registers ────────────────────────────────
    public byte CpuRead(ushort address)
    {
        byte data = 0;
        switch (address & 0x07)
        {
            case 0x00: break; // PPUCTRL not readable
            case 0x01: break; // PPUMASK not readable
            case 0x02: // PPUSTATUS
                data = (byte)((_status & 0xE0) | (_dataBuffer & 0x1F));
                _status &= 0x7F; // clear VBlank flag
                _w = false;
                break;
            case 0x03: break; // OAMADDR not readable
            case 0x04: // OAMDATA
                data = Oam[_oamAddr];
                break;
            case 0x05: break; // PPUSCROLL not readable
            case 0x06: break; // PPUADDR not readable
            case 0x07: // PPUDATA
                data = _dataBuffer;
                _dataBuffer = PpuRead(_v);
                if (_v >= 0x3F00) data = _dataBuffer; // palette reads are not buffered
                _v = (ushort)(_v + CtrlVramIncrement);
                break;
        }
        return data;
    }

    public void CpuWrite(ushort address, byte data)
    {
        switch (address & 0x07)
        {
            case 0x00: // PPUCTRL
                _ctrl = data;
                // t: nametable bits from ctrl bits 0-1
                _t = (ushort)((_t & 0xF3FF) | ((data & 0x03) << 10));
                break;
            case 0x01: // PPUMASK
                _mask = data;
                break;
            case 0x02: break; // PPUSTATUS not writable
            case 0x03: // OAMADDR
                _oamAddr = data;
                break;
            case 0x04: // OAMDATA
                Oam[_oamAddr++] = data;
                break;
            case 0x05: // PPUSCROLL
                if (!_w)
                {
                    _x = (byte)(data & 0x07);
                    _t = (ushort)((_t & 0xFFE0) | (data >> 3));
                }
                else
                {
                    _t = (ushort)((_t & 0x8FFF) | ((data & 0x07) << 12));
                    _t = (ushort)((_t & 0xFC1F) | ((data & 0xF8) << 2));
                }
                _w = !_w;
                break;
            case 0x06: // PPUADDR
                if (!_w)
                    _t = (ushort)((_t & 0x00FF) | ((data & 0x3F) << 8));
                else
                {
                    _t = (ushort)((_t & 0xFF00) | data);
                    _v = _t;
                }
                _w = !_w;
                break;
            case 0x07: // PPUDATA
                PpuWrite(_v, data);
                _v = (ushort)(_v + CtrlVramIncrement);
                break;
        }
    }

    // ── PPU memory read/write ─────────────────────────────────────────────────
    public byte PpuRead(ushort address)
    {
        address &= 0x3FFF;

        if (_cartridge is not null && _cartridge.PpuRead(address, out byte data))
            return data;

        if (address < 0x2000)
            return 0; // pattern tables handled by cartridge

        if (address < 0x3F00)
        {
            address &= 0x0FFF;
            return _nameTable[NtIndex(address), address & 0x03FF];
        }

        // Palette
        address &= 0x1F;
        // Mirrors: $3F10, $3F14, $3F18, $3F1C → $3F00, $3F04, $3F08, $3F0C
        if (address is 0x10 or 0x14 or 0x18 or 0x1C) address -= 0x10;
        byte val = _palette[address];
        return (_mask & 0x01) != 0 ? (byte)(val & 0x30) : val; // greyscale mask
    }

    public void PpuWrite(ushort address, byte data)
    {
        address &= 0x3FFF;

        if (_cartridge?.PpuWrite(address, data) == true) return;

        if (address < 0x2000) return; // pattern table ROM

        if (address < 0x3F00)
        {
            address &= 0x0FFF;
            _nameTable[NtIndex(address), address & 0x03FF] = data;
            return;
        }

        address &= 0x1F;
        if (address is 0x10 or 0x14 or 0x18 or 0x1C) address -= 0x10;
        _palette[address] = data;
    }

    private int NtIndex(ushort addr)
    {
        int nt = (addr >> 10) & 0x03;
        return (_cartridge?.Mirror ?? MirrorMode.Horizontal) switch
        {
            MirrorMode.Horizontal => nt < 2 ? 0 : 1,
            MirrorMode.Vertical   => nt % 2,
            MirrorMode.SingleLow  => 0,
            MirrorMode.SingleHigh => 1,
            _ => 0
        };
    }

    private uint GetColour(uint palette, uint pixel)
    {
        byte idx = PpuRead((ushort)(0x3F00 + (palette << 2) + pixel));
        return SystemPalette[idx & 0x3F];
    }

    private byte _dataBuffer;

    // ── Save / Load state ─────────────────────────────────────────────────────
    public void SaveState(BinaryWriter bw)
    {
        bw.Write(_ctrl); bw.Write(_mask); bw.Write(_status); bw.Write(_oamAddr);
        bw.Write(_v); bw.Write(_t); bw.Write(_x); bw.Write(_w);
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 1024; j++)
                bw.Write(_nameTable[i, j]);
        bw.Write(_palette);
        bw.Write(Oam);
        bw.Write(_dataBuffer);
        bw.Write(NmiRequested);
        SaveClockState(bw);
    }

    public void LoadState(BinaryReader br)
    {
        _ctrl    = br.ReadByte(); _mask    = br.ReadByte();
        _status  = br.ReadByte(); _oamAddr = br.ReadByte();
        _v = br.ReadUInt16(); _t = br.ReadUInt16();
        _x = br.ReadByte();   _w = br.ReadBoolean();
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 1024; j++)
                _nameTable[i, j] = br.ReadByte();
        br.Read(_palette);
        br.Read(Oam);
        _dataBuffer   = br.ReadByte();
        NmiRequested  = br.ReadBoolean();
        FrameComplete = false;
        LoadClockState(br);
    }
}
