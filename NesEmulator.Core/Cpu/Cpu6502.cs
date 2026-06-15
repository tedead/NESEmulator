using NesEmulator.Core.Memory;

namespace NesEmulator.Core.Cpu;

public sealed partial class Cpu6502
{
    // ── Registers ────────────────────────────────────────────────────────────
    public byte A  { get; private set; }  // Accumulator
    public byte X  { get; private set; }  // Index X
    public byte Y  { get; private set; }  // Index Y
    public byte SP { get; private set; }  // Stack Pointer
    public byte P  { get; private set; }  // Status
    public ushort PC { get; private set; } // Program Counter

    // ── Status flags ─────────────────────────────────────────────────────────
    [Flags]
    public enum Flag : byte
    {
        C = 1 << 0, // Carry
        Z = 1 << 1, // Zero
        I = 1 << 2, // Interrupt Disable
        D = 1 << 3, // Decimal (unused on NES)
        B = 1 << 4, // Break
        U = 1 << 5, // Unused (always 1)
        V = 1 << 6, // Overflow
        N = 1 << 7, // Negative
    }

    // ── Internal state ────────────────────────────────────────────────────────
    private readonly Bus _bus;
    private byte  _fetched;
    private ushort _addrAbs;
    private ushort _addrRel;
    private byte  _opcode;
    private int   _cycles;

    public ulong TotalCycles { get; private set; }

    public Cpu6502(Bus bus)
    {
        _bus = bus;
        BuildLookupTable();
    }

    // ── Bus access ────────────────────────────────────────────────────────────
    private byte Read(ushort addr) => _bus.Read(addr);
    private void Write(ushort addr, byte data) => _bus.Write(addr, data);

    // ── Flag helpers ──────────────────────────────────────────────────────────
    public bool GetFlag(Flag f) => (P & (byte)f) != 0;

    private void SetFlag(Flag f, bool v)
    {
        if (v) P |= (byte)f;
        else   P &= (byte)~(byte)f;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    public void Reset()
    {
        A = X = Y = 0;
        SP = 0xFD;
        P = (byte)(0x00 | (byte)Flag.U);

        _addrAbs = 0xFFFC;
        ushort lo = Read(_addrAbs);
        ushort hi = Read((ushort)(_addrAbs + 1));
        PC = (ushort)((hi << 8) | lo);

        _addrAbs = _addrRel = 0;
        _fetched = 0;
        _cycles = 8;
    }

    public void IRQ()
    {
        if (GetFlag(Flag.I)) return;

        Push((byte)(PC >> 8));
        Push((byte)(PC & 0xFF));

        SetFlag(Flag.B, false);
        SetFlag(Flag.U, true);
        SetFlag(Flag.I, true);
        Push(P);

        _addrAbs = 0xFFFE;
        ushort lo = Read(_addrAbs);
        ushort hi = Read((ushort)(_addrAbs + 1));
        PC = (ushort)((hi << 8) | lo);

        _cycles = 7;
    }

    public void NMI()
    {
        Push((byte)(PC >> 8));
        Push((byte)(PC & 0xFF));

        SetFlag(Flag.B, false);
        SetFlag(Flag.U, true);
        SetFlag(Flag.I, true);
        Push(P);

        _addrAbs = 0xFFFA;
        ushort lo = Read(_addrAbs);
        ushort hi = Read((ushort)(_addrAbs + 1));
        PC = (ushort)((hi << 8) | lo);

        _cycles = 8;
    }

    // ── Clock ────────────────────────────────────────────────────────────────
    public void Clock()
    {
        if (_cycles == 0)
        {
            _opcode = Read(PC);
            SetFlag(Flag.U, true);
            PC++;

            ref var instr = ref _lookup[_opcode];
            _cycles = instr.Cycles;

            byte extra1 = instr.AddrMode();
            byte extra2 = instr.Operate();

            _cycles += extra1 & extra2;
            SetFlag(Flag.U, true);
        }
        _cycles--;
        TotalCycles++;
    }

    public bool Complete => _cycles == 0;

    // ── Stack helpers ─────────────────────────────────────────────────────────
    private void Push(byte data) => Write((ushort)(0x0100 + SP--), data);
    private byte Pop() => Read((ushort)(0x0100 + ++SP));

    // ── Fetch ─────────────────────────────────────────────────────────────────
    private byte Fetch()
    {
        if (_lookup[_opcode].AddrModeName != "IMP")
            _fetched = Read(_addrAbs);
        return _fetched;
    }

    // ── Disassembler ──────────────────────────────────────────────────────────
    public Dictionary<ushort, string> Disassemble(ushort start, ushort end)
    {
        var result = new Dictionary<ushort, string>();
        uint addr = start;

        while (addr <= end)
        {
            ushort lineAddr = (ushort)addr;
            byte op = _bus.Read((ushort)addr++);
            ref var instr = ref _lookup[op];

            string operand = instr.AddrModeName switch
            {
                "IMP" => "",
                "ACC" => " A",
                "IMM" => $" #${_bus.Read((ushort)addr++):X2}",
                "ZP0" => $" ${_bus.Read((ushort)addr++):X2} {{ZP}}",
                "ZPX" => $" ${_bus.Read((ushort)addr++):X2},X {{ZPX}}",
                "ZPY" => $" ${_bus.Read((ushort)addr++):X2},Y {{ZPY}}",
                "REL" => BuildRel(ref addr),
                "ABS" => BuildAbs(ref addr),
                "ABX" => BuildAbsIdx(ref addr, 'X'),
                "ABY" => BuildAbsIdx(ref addr, 'Y'),
                "IND" => BuildInd(ref addr),
                "IZX" => $" (${_bus.Read((ushort)addr++):X2},X) {{IZX}}",
                "IZY" => $" (${_bus.Read((ushort)addr++):X2}),Y {{IZY}}",
                _ => ""
            };

            result[lineAddr] = $"${lineAddr:X4}: {instr.Name}{operand}";
        }

        return result;
    }

    private string BuildRel(ref uint addr)
    {
        byte offset = _bus.Read((ushort)addr++);
        int target = (int)addr + (sbyte)offset;
        return $" ${target:X4} {{REL}}";
    }

    private string BuildAbs(ref uint addr)
    {
        ushort lo = _bus.Read((ushort)addr++);
        ushort hi = _bus.Read((ushort)addr++);
        return $" ${(ushort)((hi << 8) | lo):X4} {{ABS}}";
    }

    private string BuildAbsIdx(ref uint addr, char reg)
    {
        ushort lo = _bus.Read((ushort)addr++);
        ushort hi = _bus.Read((ushort)addr++);
        return $" ${(ushort)((hi << 8) | lo):X4},{reg} {{AB{reg}}}";
    }

    private string BuildInd(ref uint addr)
    {
        ushort lo = _bus.Read((ushort)addr++);
        ushort hi = _bus.Read((ushort)addr++);
        return $" (${(ushort)((hi << 8) | lo):X4}) {{IND}}";
    }
}
