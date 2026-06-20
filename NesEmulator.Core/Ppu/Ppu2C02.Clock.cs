using NesEmulator.Core;

namespace NesEmulator.Core.Ppu;

public sealed partial class Ppu2C02
{
    // ── Rendering state ───────────────────────────────────────────────────────
    private int  _cycle    = 0;
    private int  _scanline = 0;
    private bool _oddFrame = false;

    // Background fetch latches
    private byte _bgNextId;
    private byte _bgNextAttrib;
    private byte _bgNextLo;
    private byte _bgNextHi;

    // Background 16-bit shift registers (filled low byte, shifted left each cycle)
    private ushort _bgShiftPatLo;
    private ushort _bgShiftPatHi;
    private ushort _bgShiftAttrLo;
    private ushort _bgShiftAttrHi;

    // Sprite data for current scanline
    private readonly byte[] _secOam      = new byte[32]; // secondary OAM: 8 sprites × 4 bytes
    private int              _sprCount    = 0;
    private readonly byte[]  _sprShiftLo = new byte[8];
    private readonly byte[]  _sprShiftHi = new byte[8];
    private bool             _spr0InRange;
    private bool             _spr0Rendered;

    // ── Main clock ────────────────────────────────────────────────────────────
    public void Clock()
    {
        if (_scanline is >= -1 and <= 239)
        {
            // ── Pre-render scanline housekeeping ──────────────────────────────
            if (_scanline == -1 && _cycle == 1)
            {
                _status &= 0x1F; // clear VBlank, sprite-zero, overflow
                Array.Clear(_sprShiftLo);
                Array.Clear(_sprShiftHi);
            }

            // ── Background fetch pipeline (cycles 2–257 and 322–337) ──────────
            if ((_cycle >= 2 && _cycle <= 257) || (_cycle >= 322 && _cycle <= 337))
            {
                UpdateBgShifters();

                switch ((_cycle - 1) % 8)
                {
                    case 0: LoadBgShifters(); FetchNt(); break;
                    case 2: FetchAt(); break;
                    case 4: FetchBgLo(); break;
                    case 6: FetchBgHi(); break;
                    case 7: IncrementScrollX(); break;
                }
            }

            if (_cycle == 256) IncrementScrollY();
            if (_cycle == 257) { LoadBgShifters(); CopyScrollX(); }
            if (_cycle == 338 || _cycle == 340) FetchNt(); // dummy NT fetches

            // Reload Y from t on pre-render scanline
            if (_scanline == -1 && _cycle >= 280 && _cycle <= 304)
                CopyScrollY();

            // ── Sprite evaluation ─────────────────────────────────────────────
            if (_cycle == 257 && _scanline >= 0)
                EvaluateSprites();

            // ── Sprite tile fetch (simplified: done all at cycle 340) ─────────
            if (_cycle == 340 && _scanline >= 0)
                FetchSpriteTiles();

            // ── Mapper scanline IRQ (e.g. MMC3) — simplified PPU-A12-edge proxy ─
            if (_cycle == 260 && RenderingEnabled)
                _cartridge?.OnScanline();
        }

        // ── VBlank ────────────────────────────────────────────────────────────
        if (_scanline == 241 && _cycle == 1)
        {
            _status |= 0x80; // set VBlank flag
            if (CtrlNmiEnable) NmiRequested = true;
        }

        // ── Output pixel ──────────────────────────────────────────────────────
        if (_scanline >= 0 && _scanline < 240 && _cycle >= 1 && _cycle <= 256)
            OutputPixel();

        // ── Advance counters ──────────────────────────────────────────────────
        _cycle++;
        // Odd-frame dot skip keeps NTSC's 3:1 PPU:CPU ratio in sync over 2 frames;
        // PAL does not perform this skip.
        if (TvSystem == TvSystem.Ntsc && RenderingEnabled && _oddFrame && _scanline == -1 && _cycle == 340)
            _cycle++;

        if (_cycle > 340)
        {
            _cycle = 0;
            _scanline++;
            int lastScanline = TvSystem == TvSystem.Pal ? 310 : 260; // 312 vs 262 total scanlines
            if (_scanline > lastScanline)
            {
                _scanline   = -1;
                FrameComplete = true;
                _oddFrame   = !_oddFrame;
            }
        }
    }

    // ── Background helpers ────────────────────────────────────────────────────
    private void FetchNt()
        => _bgNextId = PpuRead((ushort)(0x2000 | (_v & 0x0FFF)));

    private void FetchAt()
    {
        ushort addr = (ushort)(0x23C0 | (_v & 0x0C00) | ((_v >> 4) & 0x38) | ((_v >> 2) & 0x07));
        byte at = PpuRead(addr);
        if ((V_CoarseY & 0x02) != 0) at >>= 4;
        if ((V_CoarseX & 0x02) != 0) at >>= 2;
        _bgNextAttrib = (byte)(at & 0x03);
    }

    private void FetchBgLo()
        => _bgNextLo = PpuRead((ushort)(CtrlBgPatternBase + (_bgNextId << 4) + V_FineY));

    private void FetchBgHi()
        => _bgNextHi = PpuRead((ushort)(CtrlBgPatternBase + (_bgNextId << 4) + V_FineY + 8));

    private void LoadBgShifters()
    {
        _bgShiftPatLo  = (ushort)((_bgShiftPatLo  & 0xFF00) | _bgNextLo);
        _bgShiftPatHi  = (ushort)((_bgShiftPatHi  & 0xFF00) | _bgNextHi);
        _bgShiftAttrLo = (ushort)((_bgShiftAttrLo & 0xFF00) | ((_bgNextAttrib & 1) != 0 ? 0xFF : 0x00));
        _bgShiftAttrHi = (ushort)((_bgShiftAttrHi & 0xFF00) | ((_bgNextAttrib & 2) != 0 ? 0xFF : 0x00));
    }

    private void UpdateBgShifters()
    {
        if (!MaskShowBg) return;
        _bgShiftPatLo  <<= 1;
        _bgShiftPatHi  <<= 1;
        _bgShiftAttrLo <<= 1;
        _bgShiftAttrHi <<= 1;
    }

    private void IncrementScrollX()
    {
        if (!RenderingEnabled) return;
        if (V_CoarseX == 31) { V_CoarseX = 0; V_NtX ^= 1; }
        else V_CoarseX++;
    }

    private void IncrementScrollY()
    {
        if (!RenderingEnabled) return;
        if (V_FineY < 7) { V_FineY++; }
        else
        {
            V_FineY = 0;
            int cy = V_CoarseY;
            if      (cy == 29) { cy = 0; V_NtY ^= 1; }
            else if (cy == 31)   cy = 0;
            else                 cy++;
            V_CoarseY = cy;
        }
    }

    private void CopyScrollX()
    {
        if (!RenderingEnabled) return;
        V_NtX    = (_t >> 10) & 1;
        V_CoarseX = _t & 0x1F;
    }

    private void CopyScrollY()
    {
        if (!RenderingEnabled) return;
        _v = (ushort)((_v & 0x841F) | (_t & 0x7BE0));
    }

    // ── Sprite evaluation ─────────────────────────────────────────────────────
    private void EvaluateSprites()
    {
        Array.Clear(_secOam);
        _sprCount   = 0;
        _spr0InRange = false;

        int spriteHeight = CtrlSpriteSize ? 16 : 8;

        for (int i = 0; i < 64 && _sprCount < 8; i++)
        {
            int diff = _scanline - Oam[i * 4];
            if (diff >= 0 && diff < spriteHeight)
            {
                if (i == 0) _spr0InRange = true;
                int dst = _sprCount * 4;
                _secOam[dst]     = Oam[i * 4];
                _secOam[dst + 1] = Oam[i * 4 + 1];
                _secOam[dst + 2] = Oam[i * 4 + 2];
                _secOam[dst + 3] = Oam[i * 4 + 3];
                _sprCount++;
            }
        }

        if (_sprCount == 8) _status |= 0x20; // sprite overflow
    }

    private void FetchSpriteTiles()
    {
        for (int i = 0; i < _sprCount; i++)
        {
            byte sprY    = _secOam[i * 4];
            byte tileId  = _secOam[i * 4 + 1];
            byte attrib  = _secOam[i * 4 + 2];
            bool flipV   = (attrib & 0x80) != 0;
            bool flipH   = (attrib & 0x40) != 0;

            int row = _scanline - sprY;

            int addrLo;
            if (!CtrlSpriteSize) // 8x8
            {
                int r = flipV ? 7 - row : row;
                addrLo = CtrlSprPatternBase | (tileId << 4) | r;
            }
            else // 8x16
            {
                int bank = (tileId & 0x01) << 12;
                int tile = tileId & 0xFE;
                int r    = flipV ? 15 - row : row;
                if (r >= 8) { tile++; r -= 8; }
                addrLo = bank | (tile << 4) | r;
            }

            byte lo = PpuRead((ushort)addrLo);
            byte hi = PpuRead((ushort)(addrLo + 8));

            if (flipH)
            {
                lo = FlipByte(lo);
                hi = FlipByte(hi);
            }

            _sprShiftLo[i] = lo;
            _sprShiftHi[i] = hi;
        }
    }

    public void SaveClockState(BinaryWriter bw)
    {
        bw.Write(_cycle); bw.Write(_scanline); bw.Write(_oddFrame);
        bw.Write(_bgNextId); bw.Write(_bgNextAttrib);
        bw.Write(_bgNextLo); bw.Write(_bgNextHi);
        bw.Write(_bgShiftPatLo);  bw.Write(_bgShiftPatHi);
        bw.Write(_bgShiftAttrLo); bw.Write(_bgShiftAttrHi);
        bw.Write(_secOam); bw.Write(_sprCount);
        bw.Write(_sprShiftLo); bw.Write(_sprShiftHi);
        bw.Write(_spr0InRange); bw.Write(_spr0Rendered);
    }

    public void LoadClockState(BinaryReader br)
    {
        _cycle    = br.ReadInt32(); _scanline = br.ReadInt32(); _oddFrame = br.ReadBoolean();
        _bgNextId     = br.ReadByte(); _bgNextAttrib = br.ReadByte();
        _bgNextLo     = br.ReadByte(); _bgNextHi     = br.ReadByte();
        _bgShiftPatLo  = br.ReadUInt16(); _bgShiftPatHi  = br.ReadUInt16();
        _bgShiftAttrLo = br.ReadUInt16(); _bgShiftAttrHi = br.ReadUInt16();
        br.Read(_secOam); _sprCount = br.ReadInt32();
        br.Read(_sprShiftLo); br.Read(_sprShiftHi);
        _spr0InRange  = br.ReadBoolean();
        _spr0Rendered = br.ReadBoolean();
    }

    private static byte FlipByte(byte b)
    {
        b = (byte)((b & 0xF0) >> 4 | (b & 0x0F) << 4);
        b = (byte)((b & 0xCC) >> 2 | (b & 0x33) << 2);
        b = (byte)((b & 0xAA) >> 1 | (b & 0x55) << 1);
        return b;
    }

    // ── Pixel output ──────────────────────────────────────────────────────────
    private void OutputPixel()
    {
        int x = _cycle - 1;

        // Background
        uint bgPixel   = 0;
        uint bgPalette = 0;
        if (MaskShowBg && (MaskShowBgLeft || x >= 8))
        {
            ushort bit = (ushort)(0x8000 >> _x);
            uint p0 = (_bgShiftPatLo  & bit) != 0 ? 1u : 0u;
            uint p1 = (_bgShiftPatHi  & bit) != 0 ? 1u : 0u;
            bgPixel = (p1 << 1) | p0;

            uint a0 = (_bgShiftAttrLo & bit) != 0 ? 1u : 0u;
            uint a1 = (_bgShiftAttrHi & bit) != 0 ? 1u : 0u;
            bgPalette = (a1 << 1) | a0;
        }

        // Sprites
        uint fgPixel   = 0;
        uint fgPalette = 0;
        bool fgPriority = false;
        _spr0Rendered  = false;

        if (MaskShowSprites && (MaskShowSprLeft || x >= 8))
        {
            for (int i = 0; i < _sprCount; i++)
            {
                if (_secOam[i * 4 + 3] == 0)
                {
                    uint lo = (uint)(_sprShiftLo[i] & 0x80) >> 7;
                    uint hi = (uint)(_sprShiftHi[i] & 0x80) >> 6;
                    uint pix = lo | hi;

                    if (pix != 0 && fgPixel == 0)
                    {
                        if (i == 0) _spr0Rendered = true;
                        fgPixel    = pix;
                        fgPalette  = (uint)((_secOam[i * 4 + 2] & 0x03) + 4);
                        fgPriority = (_secOam[i * 4 + 2] & 0x20) == 0;
                    }

                    _sprShiftLo[i] <<= 1;
                    _sprShiftHi[i] <<= 1;
                }
                else
                {
                    _secOam[i * 4 + 3]--;
                }
            }
        }

        // Sprite-zero hit detection
        if (_spr0InRange && _spr0Rendered && MaskShowBg && MaskShowSprites)
        {
            if (x is >= 1 and <= 254)
            {
                bool leftClip = !MaskShowBgLeft || !MaskShowSprLeft;
                if (!(leftClip && x < 8))
                    if (bgPixel != 0 && fgPixel != 0)
                        _status |= 0x40;
            }
        }

        // Combine BG + sprite
        uint finalPixel   = 0;
        uint finalPalette = 0;

        if      (bgPixel == 0 && fgPixel == 0) { finalPixel = 0; finalPalette = 0; }
        else if (bgPixel == 0)                  { finalPixel = fgPixel; finalPalette = fgPalette; }
        else if (fgPixel == 0)                  { finalPixel = bgPixel; finalPalette = bgPalette; }
        else
        {
            if (fgPriority) { finalPixel = fgPixel; finalPalette = fgPalette; }
            else            { finalPixel = bgPixel; finalPalette = bgPalette; }
        }

        FrameBuffer[_scanline * 256 + x] = GetColour(finalPalette, finalPixel);
    }
}
