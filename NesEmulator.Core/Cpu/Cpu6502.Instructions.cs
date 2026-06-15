namespace NesEmulator.Core.Cpu;

public sealed partial class Cpu6502
{
    // ── Load / Store ──────────────────────────────────────────────────────────
    private byte LDA() { A = Fetch(); SetFlag(Flag.Z, A == 0); SetFlag(Flag.N, (A & 0x80) != 0); return 1; }
    private byte LDX() { X = Fetch(); SetFlag(Flag.Z, X == 0); SetFlag(Flag.N, (X & 0x80) != 0); return 1; }
    private byte LDY() { Y = Fetch(); SetFlag(Flag.Z, Y == 0); SetFlag(Flag.N, (Y & 0x80) != 0); return 1; }
    private byte STA() { Write(_addrAbs, A); return 0; }
    private byte STX() { Write(_addrAbs, X); return 0; }
    private byte STY() { Write(_addrAbs, Y); return 0; }

    // ── Transfer ──────────────────────────────────────────────────────────────
    private byte TAX() { X = A; SetFlag(Flag.Z, X == 0); SetFlag(Flag.N, (X & 0x80) != 0); return 0; }
    private byte TAY() { Y = A; SetFlag(Flag.Z, Y == 0); SetFlag(Flag.N, (Y & 0x80) != 0); return 0; }
    private byte TXA() { A = X; SetFlag(Flag.Z, A == 0); SetFlag(Flag.N, (A & 0x80) != 0); return 0; }
    private byte TYA() { A = Y; SetFlag(Flag.Z, A == 0); SetFlag(Flag.N, (A & 0x80) != 0); return 0; }
    private byte TSX() { X = SP; SetFlag(Flag.Z, X == 0); SetFlag(Flag.N, (X & 0x80) != 0); return 0; }
    private byte TXS() { SP = X; return 0; }

    // ── Stack ─────────────────────────────────────────────────────────────────
    private byte PHA() { Push(A); return 0; }
    private byte PHP() { Push((byte)(P | (byte)Flag.B | (byte)Flag.U)); SetFlag(Flag.B, false); return 0; }
    private byte PLA() { A = Pop(); SetFlag(Flag.Z, A == 0); SetFlag(Flag.N, (A & 0x80) != 0); return 0; }
    private byte PLP() { P = Pop(); SetFlag(Flag.U, true); return 0; }

    // ── Logical ───────────────────────────────────────────────────────────────
    private byte AND() { A &= Fetch(); SetFlag(Flag.Z, A == 0); SetFlag(Flag.N, (A & 0x80) != 0); return 1; }
    private byte EOR() { A ^= Fetch(); SetFlag(Flag.Z, A == 0); SetFlag(Flag.N, (A & 0x80) != 0); return 1; }
    private byte ORA() { A |= Fetch(); SetFlag(Flag.Z, A == 0); SetFlag(Flag.N, (A & 0x80) != 0); return 1; }

    private byte BIT()
    {
        byte m = Fetch();
        SetFlag(Flag.Z, (A & m) == 0);
        SetFlag(Flag.N, (m & (byte)Flag.N) != 0);
        SetFlag(Flag.V, (m & (byte)Flag.V) != 0);
        return 0;
    }

    // ── Arithmetic ────────────────────────────────────────────────────────────
    private byte ADC()
    {
        ushort value = Fetch();
        ushort tmp = (ushort)(A + value + (GetFlag(Flag.C) ? 1 : 0));
        SetFlag(Flag.C, tmp > 0xFF);
        SetFlag(Flag.Z, (tmp & 0xFF) == 0);
        SetFlag(Flag.N, (tmp & 0x80) != 0);
        SetFlag(Flag.V, ((~(A ^ value) & (A ^ tmp)) & 0x80) != 0);
        A = (byte)(tmp & 0xFF);
        return 1;
    }

    private byte SBC()
    {
        ushort value = (ushort)(Fetch() ^ 0xFF);
        ushort tmp = (ushort)(A + value + (GetFlag(Flag.C) ? 1 : 0));
        SetFlag(Flag.C, (tmp & 0xFF00) != 0);
        SetFlag(Flag.Z, (tmp & 0xFF) == 0);
        SetFlag(Flag.N, (tmp & 0x80) != 0);
        SetFlag(Flag.V, ((tmp ^ A) & (tmp ^ value) & 0x80) != 0);
        A = (byte)(tmp & 0xFF);
        return 1;
    }

    // ── Compare ───────────────────────────────────────────────────────────────
    private byte CMP() { Compare(A); return 1; }
    private byte CPX() { Compare(X); return 0; }
    private byte CPY() { Compare(Y); return 0; }

    private void Compare(byte reg)
    {
        byte m = Fetch();
        int tmp = reg - m;
        SetFlag(Flag.C, reg >= m);
        SetFlag(Flag.Z, (tmp & 0xFF) == 0);
        SetFlag(Flag.N, (tmp & 0x80) != 0);
    }

    // ── Increment / Decrement ────────────────────────────────────────────────
    private byte INC()
    {
        byte m = (byte)(Fetch() + 1);
        Write(_addrAbs, m);
        SetFlag(Flag.Z, m == 0);
        SetFlag(Flag.N, (m & 0x80) != 0);
        return 0;
    }

    private byte INX() { X++; SetFlag(Flag.Z, X == 0); SetFlag(Flag.N, (X & 0x80) != 0); return 0; }
    private byte INY() { Y++; SetFlag(Flag.Z, Y == 0); SetFlag(Flag.N, (Y & 0x80) != 0); return 0; }

    private byte DEC()
    {
        byte m = (byte)(Fetch() - 1);
        Write(_addrAbs, m);
        SetFlag(Flag.Z, m == 0);
        SetFlag(Flag.N, (m & 0x80) != 0);
        return 0;
    }

    private byte DEX() { X--; SetFlag(Flag.Z, X == 0); SetFlag(Flag.N, (X & 0x80) != 0); return 0; }
    private byte DEY() { Y--; SetFlag(Flag.Z, Y == 0); SetFlag(Flag.N, (Y & 0x80) != 0); return 0; }

    // ── Shift ─────────────────────────────────────────────────────────────────
    private byte ASL()
    {
        byte m = Fetch();
        SetFlag(Flag.C, (m & 0x80) != 0);
        m <<= 1;
        SetFlag(Flag.Z, m == 0);
        SetFlag(Flag.N, (m & 0x80) != 0);
        if (_lookup[_opcode].AddrModeName == "IMP") A = m;
        else Write(_addrAbs, m);
        return 0;
    }

    private byte LSR()
    {
        byte m = Fetch();
        SetFlag(Flag.C, (m & 0x01) != 0);
        m >>= 1;
        SetFlag(Flag.Z, m == 0);
        SetFlag(Flag.N, false);
        if (_lookup[_opcode].AddrModeName == "IMP") A = m;
        else Write(_addrAbs, m);
        return 0;
    }

    private byte ROL()
    {
        byte m = Fetch();
        byte carry = GetFlag(Flag.C) ? (byte)1 : (byte)0;
        SetFlag(Flag.C, (m & 0x80) != 0);
        m = (byte)((m << 1) | carry);
        SetFlag(Flag.Z, m == 0);
        SetFlag(Flag.N, (m & 0x80) != 0);
        if (_lookup[_opcode].AddrModeName == "IMP") A = m;
        else Write(_addrAbs, m);
        return 0;
    }

    private byte ROR()
    {
        byte m = Fetch();
        byte carry = GetFlag(Flag.C) ? (byte)0x80 : (byte)0;
        SetFlag(Flag.C, (m & 0x01) != 0);
        m = (byte)((m >> 1) | carry);
        SetFlag(Flag.Z, m == 0);
        SetFlag(Flag.N, (m & 0x80) != 0);
        if (_lookup[_opcode].AddrModeName == "IMP") A = m;
        else Write(_addrAbs, m);
        return 0;
    }

    // ── Jump / Call ───────────────────────────────────────────────────────────
    private byte JMP() { PC = _addrAbs; return 0; }

    private byte JSR()
    {
        PC--;
        Push((byte)(PC >> 8));
        Push((byte)(PC & 0xFF));
        PC = _addrAbs;
        return 0;
    }

    private byte RTS()
    {
        byte lo = Pop();
        byte hi = Pop();
        PC = (ushort)(((hi << 8) | lo) + 1);
        return 0;
    }

    private byte RTI()
    {
        P = Pop();
        P &= unchecked((byte)~(byte)Flag.B);
        P &= unchecked((byte)~(byte)Flag.U);
        byte lo = Pop();
        byte hi = Pop();
        PC = (ushort)((hi << 8) | lo);
        return 0;
    }

    private byte BRK()
    {
        PC++;
        SetFlag(Flag.I, true);
        Push((byte)(PC >> 8));
        Push((byte)(PC & 0xFF));
        SetFlag(Flag.B, true);
        Push(P);
        SetFlag(Flag.B, false);
        ushort lo = Read(0xFFFE);
        ushort hi = Read(0xFFFF);
        PC = (ushort)((hi << 8) | lo);
        return 0;
    }

    // ── Branch ────────────────────────────────────────────────────────────────
    private byte BranchIf(bool condition)
    {
        if (!condition) return 0;
        _cycles++;
        ushort dest = (ushort)(PC + _addrRel);
        if ((dest & 0xFF00) != (PC & 0xFF00)) _cycles++;
        PC = dest;
        return 0;
    }

    private byte BCC() => BranchIf(!GetFlag(Flag.C));
    private byte BCS() => BranchIf(GetFlag(Flag.C));
    private byte BEQ() => BranchIf(GetFlag(Flag.Z));
    private byte BNE() => BranchIf(!GetFlag(Flag.Z));
    private byte BMI() => BranchIf(GetFlag(Flag.N));
    private byte BPL() => BranchIf(!GetFlag(Flag.N));
    private byte BVC() => BranchIf(!GetFlag(Flag.V));
    private byte BVS() => BranchIf(GetFlag(Flag.V));

    // ── Flag operations ───────────────────────────────────────────────────────
    private byte CLC() { SetFlag(Flag.C, false); return 0; }
    private byte CLD() { SetFlag(Flag.D, false); return 0; }
    private byte CLI() { SetFlag(Flag.I, false); return 0; }
    private byte CLV() { SetFlag(Flag.V, false); return 0; }
    private byte SEC() { SetFlag(Flag.C, true);  return 0; }
    private byte SED() { SetFlag(Flag.D, true);  return 0; }
    private byte SEI() { SetFlag(Flag.I, true);  return 0; }

    // ── No-op / Illegal ───────────────────────────────────────────────────────
    private byte NOP() => 1;
    private byte XXX() => 0; // illegal opcode stub
}
