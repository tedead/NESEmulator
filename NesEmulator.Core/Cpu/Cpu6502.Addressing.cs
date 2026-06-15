namespace NesEmulator.Core.Cpu;

// Addressing modes — each returns 1 if an extra cycle may be needed, 0 otherwise.
public sealed partial class Cpu6502
{
    // Implied: no operand
    private byte IMP() { _fetched = A; return 0; }

    // Accumulator: operand is A
    private byte ACC() { _fetched = A; return 0; }

    // Immediate: operand follows opcode
    private byte IMM() { _addrAbs = PC++; return 0; }

    // Zero Page
    private byte ZP0() { _addrAbs = Read(PC++); return 0; }

    // Zero Page, X
    private byte ZPX() { _addrAbs = (byte)(Read(PC++) + X); return 0; }

    // Zero Page, Y
    private byte ZPY() { _addrAbs = (byte)(Read(PC++) + Y); return 0; }

    // Relative (branch offsets)
    private byte REL()
    {
        _addrRel = Read(PC++);
        if ((_addrRel & 0x80) != 0) _addrRel |= 0xFF00; // sign-extend
        return 0;
    }

    // Absolute
    private byte ABS()
    {
        ushort lo = Read(PC++);
        ushort hi = Read(PC++);
        _addrAbs = (ushort)((hi << 8) | lo);
        return 0;
    }

    // Absolute, X — extra cycle if page crossed
    private byte ABX()
    {
        ushort lo = Read(PC++);
        ushort hi = Read(PC++);
        _addrAbs = (ushort)(((hi << 8) | lo) + X);
        return (_addrAbs & 0xFF00) != (hi << 8) ? (byte)1 : (byte)0;
    }

    // Absolute, Y — extra cycle if page crossed
    private byte ABY()
    {
        ushort lo = Read(PC++);
        ushort hi = Read(PC++);
        _addrAbs = (ushort)(((hi << 8) | lo) + Y);
        return (_addrAbs & 0xFF00) != (hi << 8) ? (byte)1 : (byte)0;
    }

    // Indirect (JMP only) — hardware bug: page boundary wrap
    private byte IND()
    {
        ushort lo = Read(PC++);
        ushort hi = Read(PC++);
        ushort ptr = (ushort)((hi << 8) | lo);

        // 6502 bug: if lo byte is 0xFF, high byte wraps within same page
        if (lo == 0xFF)
            _addrAbs = (ushort)((Read((ushort)(ptr & 0xFF00)) << 8) | Read(ptr));
        else
            _addrAbs = (ushort)((Read((ushort)(ptr + 1)) << 8) | Read(ptr));

        return 0;
    }

    // Indexed Indirect (X)
    private byte IZX()
    {
        ushort t = Read(PC++);
        byte lo = Read((ushort)((t + X) & 0x00FF));
        byte hi = Read((ushort)((t + X + 1) & 0x00FF));
        _addrAbs = (ushort)((hi << 8) | lo);
        return 0;
    }

    // Indirect Indexed (Y) — extra cycle if page crossed
    private byte IZY()
    {
        ushort t = Read(PC++);
        byte lo = Read((ushort)(t & 0x00FF));
        byte hi = Read((ushort)((t + 1) & 0x00FF));
        _addrAbs = (ushort)(((hi << 8) | lo) + Y);
        return (_addrAbs & 0xFF00) != (hi << 8) ? (byte)1 : (byte)0;
    }
}
