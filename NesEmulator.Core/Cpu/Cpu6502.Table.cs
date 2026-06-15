namespace NesEmulator.Core.Cpu;

public sealed partial class Cpu6502
{
    private struct Instruction
    {
        public string Name;
        public string AddrModeName;
        public Func<byte> Operate;
        public Func<byte> AddrMode;
        public byte Cycles;
    }

    private Instruction[] _lookup = null!;

    private void BuildLookupTable()
    {
        _lookup = new Instruction[256];

        // Helper to fill entry
        void Set(int op, string name, Func<byte> operate, string modeName, Func<byte> addrMode, byte cycles)
        {
            _lookup[op] = new Instruction
            {
                Name = name, AddrModeName = modeName,
                Operate = operate, AddrMode = addrMode, Cycles = cycles
            };
        }

        // Default all to XXX/IMP
        for (int i = 0; i < 256; i++)
            Set(i, "???", XXX, "IMP", IMP, 2);

        // Row 0x00
        Set(0x00, "BRK", BRK, "IMP", IMP, 7);
        Set(0x01, "ORA", ORA, "IZX", IZX, 6);
        Set(0x05, "ORA", ORA, "ZP0", ZP0, 3);
        Set(0x06, "ASL", ASL, "ZP0", ZP0, 5);
        Set(0x08, "PHP", PHP, "IMP", IMP, 3);
        Set(0x09, "ORA", ORA, "IMM", IMM, 2);
        Set(0x0A, "ASL", ASL, "IMP", IMP, 2);
        Set(0x0D, "ORA", ORA, "ABS", ABS, 4);
        Set(0x0E, "ASL", ASL, "ABS", ABS, 6);

        // Row 0x10
        Set(0x10, "BPL", BPL, "REL", REL, 2);
        Set(0x11, "ORA", ORA, "IZY", IZY, 5);
        Set(0x15, "ORA", ORA, "ZPX", ZPX, 4);
        Set(0x16, "ASL", ASL, "ZPX", ZPX, 6);
        Set(0x18, "CLC", CLC, "IMP", IMP, 2);
        Set(0x19, "ORA", ORA, "ABY", ABY, 4);
        Set(0x1D, "ORA", ORA, "ABX", ABX, 4);
        Set(0x1E, "ASL", ASL, "ABX", ABX, 7);

        // Row 0x20
        Set(0x20, "JSR", JSR, "ABS", ABS, 6);
        Set(0x21, "AND", AND, "IZX", IZX, 6);
        Set(0x24, "BIT", BIT, "ZP0", ZP0, 3);
        Set(0x25, "AND", AND, "ZP0", ZP0, 3);
        Set(0x26, "ROL", ROL, "ZP0", ZP0, 5);
        Set(0x28, "PLP", PLP, "IMP", IMP, 4);
        Set(0x29, "AND", AND, "IMM", IMM, 2);
        Set(0x2A, "ROL", ROL, "IMP", IMP, 2);
        Set(0x2C, "BIT", BIT, "ABS", ABS, 4);
        Set(0x2D, "AND", AND, "ABS", ABS, 4);
        Set(0x2E, "ROL", ROL, "ABS", ABS, 6);

        // Row 0x30
        Set(0x30, "BMI", BMI, "REL", REL, 2);
        Set(0x31, "AND", AND, "IZY", IZY, 5);
        Set(0x35, "AND", AND, "ZPX", ZPX, 4);
        Set(0x36, "ROL", ROL, "ZPX", ZPX, 6);
        Set(0x38, "SEC", SEC, "IMP", IMP, 2);
        Set(0x39, "AND", AND, "ABY", ABY, 4);
        Set(0x3D, "AND", AND, "ABX", ABX, 4);
        Set(0x3E, "ROL", ROL, "ABX", ABX, 7);

        // Row 0x40
        Set(0x40, "RTI", RTI, "IMP", IMP, 6);
        Set(0x41, "EOR", EOR, "IZX", IZX, 6);
        Set(0x45, "EOR", EOR, "ZP0", ZP0, 3);
        Set(0x46, "LSR", LSR, "ZP0", ZP0, 5);
        Set(0x48, "PHA", PHA, "IMP", IMP, 3);
        Set(0x49, "EOR", EOR, "IMM", IMM, 2);
        Set(0x4A, "LSR", LSR, "IMP", IMP, 2);
        Set(0x4C, "JMP", JMP, "ABS", ABS, 3);
        Set(0x4D, "EOR", EOR, "ABS", ABS, 4);
        Set(0x4E, "LSR", LSR, "ABS", ABS, 6);

        // Row 0x50
        Set(0x50, "BVC", BVC, "REL", REL, 2);
        Set(0x51, "EOR", EOR, "IZY", IZY, 5);
        Set(0x55, "EOR", EOR, "ZPX", ZPX, 4);
        Set(0x56, "LSR", LSR, "ZPX", ZPX, 6);
        Set(0x58, "CLI", CLI, "IMP", IMP, 2);
        Set(0x59, "EOR", EOR, "ABY", ABY, 4);
        Set(0x5D, "EOR", EOR, "ABX", ABX, 4);
        Set(0x5E, "LSR", LSR, "ABX", ABX, 7);

        // Row 0x60
        Set(0x60, "RTS", RTS, "IMP", IMP, 6);
        Set(0x61, "ADC", ADC, "IZX", IZX, 6);
        Set(0x65, "ADC", ADC, "ZP0", ZP0, 3);
        Set(0x66, "ROR", ROR, "ZP0", ZP0, 5);
        Set(0x68, "PLA", PLA, "IMP", IMP, 4);
        Set(0x69, "ADC", ADC, "IMM", IMM, 2);
        Set(0x6A, "ROR", ROR, "IMP", IMP, 2);
        Set(0x6C, "JMP", JMP, "IND", IND, 5);
        Set(0x6D, "ADC", ADC, "ABS", ABS, 4);
        Set(0x6E, "ROR", ROR, "ABS", ABS, 6);

        // Row 0x70
        Set(0x70, "BVS", BVS, "REL", REL, 2);
        Set(0x71, "ADC", ADC, "IZY", IZY, 5);
        Set(0x75, "ADC", ADC, "ZPX", ZPX, 4);
        Set(0x76, "ROR", ROR, "ZPX", ZPX, 6);
        Set(0x78, "SEI", SEI, "IMP", IMP, 2);
        Set(0x79, "ADC", ADC, "ABY", ABY, 4);
        Set(0x7D, "ADC", ADC, "ABX", ABX, 4);
        Set(0x7E, "ROR", ROR, "ABX", ABX, 7);

        // Row 0x80
        Set(0x81, "STA", STA, "IZX", IZX, 6);
        Set(0x84, "STY", STY, "ZP0", ZP0, 3);
        Set(0x85, "STA", STA, "ZP0", ZP0, 3);
        Set(0x86, "STX", STX, "ZP0", ZP0, 3);
        Set(0x88, "DEY", DEY, "IMP", IMP, 2);
        Set(0x8A, "TXA", TXA, "IMP", IMP, 2);
        Set(0x8C, "STY", STY, "ABS", ABS, 4);
        Set(0x8D, "STA", STA, "ABS", ABS, 4);
        Set(0x8E, "STX", STX, "ABS", ABS, 4);

        // Row 0x90
        Set(0x90, "BCC", BCC, "REL", REL, 2);
        Set(0x91, "STA", STA, "IZY", IZY, 6);
        Set(0x94, "STY", STY, "ZPX", ZPX, 4);
        Set(0x95, "STA", STA, "ZPX", ZPX, 4);
        Set(0x96, "STX", STX, "ZPY", ZPY, 4);
        Set(0x98, "TYA", TYA, "IMP", IMP, 2);
        Set(0x99, "STA", STA, "ABY", ABY, 5);
        Set(0x9A, "TXS", TXS, "IMP", IMP, 2);
        Set(0x9D, "STA", STA, "ABX", ABX, 5);

        // Row 0xA0
        Set(0xA0, "LDY", LDY, "IMM", IMM, 2);
        Set(0xA1, "LDA", LDA, "IZX", IZX, 6);
        Set(0xA2, "LDX", LDX, "IMM", IMM, 2);
        Set(0xA4, "LDY", LDY, "ZP0", ZP0, 3);
        Set(0xA5, "LDA", LDA, "ZP0", ZP0, 3);
        Set(0xA6, "LDX", LDX, "ZP0", ZP0, 3);
        Set(0xA8, "TAY", TAY, "IMP", IMP, 2);
        Set(0xA9, "LDA", LDA, "IMM", IMM, 2);
        Set(0xAA, "TAX", TAX, "IMP", IMP, 2);
        Set(0xAC, "LDY", LDY, "ABS", ABS, 4);
        Set(0xAD, "LDA", LDA, "ABS", ABS, 4);
        Set(0xAE, "LDX", LDX, "ABS", ABS, 4);

        // Row 0xB0
        Set(0xB0, "BCS", BCS, "REL", REL, 2);
        Set(0xB1, "LDA", LDA, "IZY", IZY, 5);
        Set(0xB4, "LDY", LDY, "ZPX", ZPX, 4);
        Set(0xB5, "LDA", LDA, "ZPX", ZPX, 4);
        Set(0xB6, "LDX", LDX, "ZPY", ZPY, 4);
        Set(0xB8, "CLV", CLV, "IMP", IMP, 2);
        Set(0xB9, "LDA", LDA, "ABY", ABY, 4);
        Set(0xBA, "TSX", TSX, "IMP", IMP, 2);
        Set(0xBC, "LDY", LDY, "ABX", ABX, 4);
        Set(0xBD, "LDA", LDA, "ABX", ABX, 4);
        Set(0xBE, "LDX", LDX, "ABY", ABY, 4);

        // Row 0xC0
        Set(0xC0, "CPY", CPY, "IMM", IMM, 2);
        Set(0xC1, "CMP", CMP, "IZX", IZX, 6);
        Set(0xC4, "CPY", CPY, "ZP0", ZP0, 3);
        Set(0xC5, "CMP", CMP, "ZP0", ZP0, 3);
        Set(0xC6, "DEC", DEC, "ZP0", ZP0, 5);
        Set(0xC8, "INY", INY, "IMP", IMP, 2);
        Set(0xC9, "CMP", CMP, "IMM", IMM, 2);
        Set(0xCA, "DEX", DEX, "IMP", IMP, 2);
        Set(0xCC, "CPY", CPY, "ABS", ABS, 4);
        Set(0xCD, "CMP", CMP, "ABS", ABS, 4);
        Set(0xCE, "DEC", DEC, "ABS", ABS, 6);

        // Row 0xD0
        Set(0xD0, "BNE", BNE, "REL", REL, 2);
        Set(0xD1, "CMP", CMP, "IZY", IZY, 5);
        Set(0xD5, "CMP", CMP, "ZPX", ZPX, 4);
        Set(0xD6, "DEC", DEC, "ZPX", ZPX, 6);
        Set(0xD8, "CLD", CLD, "IMP", IMP, 2);
        Set(0xD9, "CMP", CMP, "ABY", ABY, 4);
        Set(0xDD, "CMP", CMP, "ABX", ABX, 4);
        Set(0xDE, "DEC", DEC, "ABX", ABX, 7);

        // Row 0xE0
        Set(0xE0, "CPX", CPX, "IMM", IMM, 2);
        Set(0xE1, "SBC", SBC, "IZX", IZX, 6);
        Set(0xE4, "CPX", CPX, "ZP0", ZP0, 3);
        Set(0xE5, "SBC", SBC, "ZP0", ZP0, 3);
        Set(0xE6, "INC", INC, "ZP0", ZP0, 5);
        Set(0xE8, "INX", INX, "IMP", IMP, 2);
        Set(0xE9, "SBC", SBC, "IMM", IMM, 2);
        Set(0xEA, "NOP", NOP, "IMP", IMP, 2);
        Set(0xEC, "CPX", CPX, "ABS", ABS, 4);
        Set(0xED, "SBC", SBC, "ABS", ABS, 4);
        Set(0xEE, "INC", INC, "ABS", ABS, 6);

        // Row 0xF0
        Set(0xF0, "BEQ", BEQ, "REL", REL, 2);
        Set(0xF1, "SBC", SBC, "IZY", IZY, 5);
        Set(0xF5, "SBC", SBC, "ZPX", ZPX, 4);
        Set(0xF6, "INC", INC, "ZPX", ZPX, 6);
        Set(0xF8, "SED", SED, "IMP", IMP, 2);
        Set(0xF9, "SBC", SBC, "ABY", ABY, 4);
        Set(0xFD, "SBC", SBC, "ABX", ABX, 4);
        Set(0xFE, "INC", INC, "ABX", ABX, 7);
    }
}
