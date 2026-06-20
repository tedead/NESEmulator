from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib import colors
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle,
    PageBreak, HRFlowable, Preformatted
)
from reportlab.lib.enums import TA_LEFT, TA_CENTER

OUTPUT = r"C:\NesEmulator\NES_Emulator_Architecture.pdf"

# ── Styles ────────────────────────────────────────────────────────────────────
base = getSampleStyleSheet()

def style(name, parent="Normal", **kw):
    s = ParagraphStyle(name, parent=base[parent], **kw)
    return s

ST = {
    "title":   style("DocTitle",  "Title",
                     fontSize=24, textColor=colors.HexColor("#1a1a2e"),
                     spaceAfter=6),
    "sub":     style("DocSub",    "Normal",
                     fontSize=13, textColor=colors.HexColor("#4a4a8a"),
                     spaceAfter=18, alignment=TA_CENTER),
    "h1":      style("H1",        "Heading1",
                     fontSize=16, textColor=colors.HexColor("#1a1a2e"),
                     spaceBefore=18, spaceAfter=6,
                     borderPad=4),
    "h2":      style("H2",        "Heading2",
                     fontSize=13, textColor=colors.HexColor("#2e4a8a"),
                     spaceBefore=12, spaceAfter=4),
    "h3":      style("H3",        "Heading3",
                     fontSize=11, textColor=colors.HexColor("#3a3a6a"),
                     spaceBefore=8, spaceAfter=3),
    "body":    style("Body",      "Normal",
                     fontSize=10, leading=15, spaceAfter=6),
    "bullet":  style("Bullet",    "Normal",
                     fontSize=10, leading=14, leftIndent=18,
                     spaceAfter=3, bulletIndent=6),
    "code":    style("Code",      "Code",
                     fontSize=8.5, leading=12,
                     backColor=colors.HexColor("#f4f4f8"),
                     leftIndent=12, rightIndent=12,
                     borderPad=6, spaceAfter=8, spaceBefore=4),
    "caption": style("Caption",   "Normal",
                     fontSize=8, textColor=colors.grey,
                     alignment=TA_CENTER, spaceAfter=8),
}

# ── Helpers ───────────────────────────────────────────────────────────────────
def p(text, s="body"):   return Paragraph(text, ST[s])
def h1(text):            return Paragraph(text, ST["h1"])
def h2(text):            return Paragraph(text, ST["h2"])
def h3(text):            return Paragraph(text, ST["h3"])
def sp(n=6):             return Spacer(1, n)
def rule():              return HRFlowable(width="100%", thickness=0.5,
                                           color=colors.HexColor("#ccccdd"), spaceAfter=4)
def bullet(text):        return Paragraph(f"&bull;&nbsp;&nbsp;{text}", ST["bullet"])

def code(text):
    return Preformatted(text, ST["code"])

def table(data, col_widths=None, header=True):
    t = Table(data, colWidths=col_widths, repeatRows=1 if header else 0)
    cmds = [
        ("FONTNAME",    (0,0), (-1,0),  "Helvetica-Bold"),
        ("FONTSIZE",    (0,0), (-1,-1), 9),
        ("BACKGROUND",  (0,0), (-1,0),  colors.HexColor("#1a1a2e")),
        ("TEXTCOLOR",   (0,0), (-1,0),  colors.white),
        ("ROWBACKGROUNDS", (0,1), (-1,-1),
         [colors.HexColor("#f8f8fc"), colors.white]),
        ("GRID",        (0,0), (-1,-1), 0.4, colors.HexColor("#ccccdd")),
        ("LEFTPADDING", (0,0), (-1,-1), 6),
        ("RIGHTPADDING",(0,0), (-1,-1), 6),
        ("TOPPADDING",  (0,0), (-1,-1), 4),
        ("BOTTOMPADDING",(0,0),(-1,-1), 4),
        ("VALIGN",      (0,0), (-1,-1), "TOP"),
    ]
    t.setStyle(TableStyle(cmds))
    return t

# ── Document ──────────────────────────────────────────────────────────────────
doc = SimpleDocTemplate(
    OUTPUT,
    pagesize=letter,
    leftMargin=0.85*inch, rightMargin=0.85*inch,
    topMargin=0.9*inch,   bottomMargin=0.9*inch,
    title="NES Emulator Architecture",
    author="NES Emulator Project",
)

story = []

# ── Cover ─────────────────────────────────────────────────────────────────────
story += [
    sp(60),
    p("NES Emulator", "title"),
    p("Architecture &amp; 6502 Emulation Reference", "sub"),
    rule(),
    sp(8),
    p("This document describes the internal architecture of a cycle-accurate NES "
      "emulator written in C# .NET 10 (WinForms). It covers how the 6502 CPU, "
      "PPU, APU, cartridge mappers, and front-end all fit together — grounded "
      "in the actual source code.", "body"),
    PageBreak(),
]

# ── 1. Project Structure ──────────────────────────────────────────────────────
story += [
    h1("1. Project Structure"),
    rule(),
    p("The solution contains two projects:", "body"),
    bullet("<b>NesEmulator.Core</b> — platform-independent emulator logic: CPU, PPU, APU, "
           "cartridge, memory bus, and input."),
    bullet("<b>NesEmulator.Desktop</b> — WinForms front-end: window, GDI+ video rendering, "
           "NAudio audio output, and the debugger UI."),
    sp(8),
]

# ── 2. Master Clock and Bus ───────────────────────────────────────────────────
story += [
    h1("2. The Master Clock and the Bus"),
    rule(),
    p("Everything is driven by a single method — <b>Bus.Clock()</b> — which the "
      "front-end calls in a tight loop until one video frame is complete:", "body"),
    code(
"public void Clock()\n"
"{\n"
"    Ppu.Clock();\n"
"    if (SystemClock % 3 == 0)\n"
"    {\n"
"        Cpu.Clock();\n"
"        Apu.Clock();\n"
"    }\n"
"    if (Ppu.NmiRequested) { Ppu.ClearNmi(); Cpu.NMI(); }\n"
"    if (Apu.IrqPending)   Cpu.IRQ();\n"
"    SystemClock++;\n"
"}"
    ),
    p("The real NES runs at <b>21.477 MHz</b> (master clock). The PPU divides that "
      "by 4 (~5.37 MHz) and the CPU divides it by 12 (~1.79 MHz). Because 12/4 = 3, "
      "the PPU runs exactly <b>3 ticks for every 1 CPU tick</b>. The emulator captures "
      "this with the <code>% 3</code> gate: every third master tick, both the CPU and "
      "APU execute one cycle; the PPU executes every tick.", "body"),
    p("<b>RunFrame()</b> loops Clock() until <b>Ppu.FrameComplete</b> is set — once "
      "every 262 scanlines × 341 PPU ticks = ~89,342 PPU cycles (~29,781 CPU cycles) "
      "per frame.", "body"),
    sp(8),
    h2("2.1 Memory Map"),
    p("The 6502 has a 16-bit address space ($0000–$FFFF). Bus.Read/Write maps it as follows:", "body"),
    sp(4),
    table(
        [
            ["Address Range", "Hardware"],
            ["$0000–$07FF", "2 KB internal RAM (mirrored to $1FFF)"],
            ["$2000–$3FFF", "PPU registers (8 regs, mirrored every 8 bytes)"],
            ["$4000–$4013", "APU channel registers"],
            ["$4014",       "OAM DMA — copies 256 bytes of RAM into PPU sprite table"],
            ["$4015",       "APU status read/write"],
            ["$4016",       "Controller 1 strobe / read"],
            ["$4017",       "Controller 2 read / APU frame counter write"],
            ["$8000–$FFFF", "Cartridge PRG-ROM (bank-switched by mapper)"],
        ],
        col_widths=[1.6*inch, 4.4*inch],
    ),
    sp(4),
]

# ── 3. The 6502 CPU ───────────────────────────────────────────────────────────
story += [
    PageBreak(),
    h1("3. The 6502 CPU"),
    rule(),
    h2("3.1 Registers"),
    table(
        [
            ["Register", "Width",   "Purpose"],
            ["PC",       "16-bit",  "Program Counter — address of the next instruction"],
            ["A",        "8-bit",   "Accumulator — primary arithmetic register"],
            ["X, Y",     "8-bit",   "Index registers — used for address arithmetic"],
            ["SP",       "8-bit",   "Stack Pointer — offset into page $0100–$01FF"],
            ["P",        "8-bit",   "Status register — eight condition flags: N V U B D I Z C"],
        ],
        col_widths=[0.7*inch, 0.7*inch, 4.6*inch],
    ),
    sp(8),
    h2("3.2 Status Flags"),
    table(
        [
            ["Flag", "Name",      "Set when…"],
            ["N",    "Negative",  "Result bit 7 is 1"],
            ["V",    "Overflow",  "Signed arithmetic overflows"],
            ["U",    "Unused",    "Always 1"],
            ["B",    "Break",     "Set during a BRK push; not a real hardware flag"],
            ["D",    "Decimal",   "Unused on NES — the 2A03 has no BCD mode"],
            ["I",    "IRQ Disable","When set, maskable IRQs are ignored"],
            ["Z",    "Zero",      "Result is 0"],
            ["C",    "Carry",     "Unsigned overflow, or set/cleared by shift instructions"],
        ],
        col_widths=[0.5*inch, 1.1*inch, 4.4*inch],
    ),
    sp(8),
    h2("3.3 The Fetch–Decode–Execute Cycle"),
    p("The CPU uses a <b>cycle-accurate countdown model</b>. Clock() decrements a "
      "<code>_cycles</code> counter; when it hits zero the next opcode is fetched:", "body"),
    code(
"public void Clock()\n"
"{\n"
"    if (_cycles == 0)\n"
"    {\n"
"        _opcode = Read(PC++);\n"
"        ref var instr = ref _lookup[_opcode];\n"
"        _cycles = instr.Cycles;\n"
"\n"
"        byte extra1 = instr.AddrMode();   // resolve effective address\n"
"        byte extra2 = instr.Operate();    // execute instruction\n"
"\n"
"        _cycles += extra1 & extra2;       // optional page-cross penalty\n"
"    }\n"
"    _cycles--;\n"
"    TotalCycles++;\n"
"}"
    ),
    p("An instruction with a base cost of 4 cycles causes Clock() to do nothing for "
      "the next 3 calls, so the CPU consumes exactly the right number of bus cycles "
      "relative to the PPU.", "body"),
    sp(8),
    h2("3.4 The Opcode Lookup Table"),
    p("Cpu6502.Table.cs builds a 256-entry array at construction time. Each entry is "
      "a struct holding: the instruction name (used by the disassembler), a delegate "
      "to the addressing mode method, a delegate to the instruction method, and the "
      "base cycle count. Undefined/illegal opcodes map to XXX(), a no-op stub.", "body"),
    sp(8),
    h2("3.5 Addressing Modes"),
    p("Before an instruction executes, one of 13 addressing mode methods runs to "
      "load <code>_addrAbs</code> (effective 16-bit address) or <code>_addrRel</code> "
      "(branch offset). Page-crossing adds 1 extra cycle via "
      "<code>_cycles += extra1 &amp; extra2</code>.", "body"),
    sp(4),
    table(
        [
            ["Mode",              "Symbol", "Description"],
            ["Implied",           "IMP",    "No operand; prefetches A for shift instructions"],
            ["Accumulator",       "ACC",    "Operand is the A register"],
            ["Immediate",         "IMM",    "Operand is the byte following the opcode"],
            ["Zero Page",         "ZP0",    "8-bit address in page $00xx"],
            ["Zero Page, X",      "ZPX",    "8-bit address + X, wraps within page 0"],
            ["Zero Page, Y",      "ZPY",    "8-bit address + Y, wraps within page 0"],
            ["Relative",          "REL",    "Signed 8-bit offset from PC (branches only)"],
            ["Absolute",          "ABS",    "Full 16-bit address"],
            ["Absolute, X",       "ABX",    "16-bit + X; +1 cycle if page crossed"],
            ["Absolute, Y",       "ABY",    "16-bit + Y; +1 cycle if page crossed"],
            ["Indirect",          "IND",    "16-bit pointer; replicates 6502 page-wrap bug"],
            ["Indexed Indirect",  "IZX",    "(zp + X) — pointer in zero page offset by X"],
            ["Indirect Indexed",  "IZY",    "(zp) + Y — pointer in zero page then add Y; +1 if page crossed"],
        ],
        col_widths=[1.4*inch, 0.65*inch, 3.95*inch],
    ),
    sp(6),
    p("The <b>indirect page-wrap bug</b> is faithfully emulated in IND(): if the low "
      "byte of the pointer is $FF, the high byte is fetched from $xx00 instead of the "
      "next page — exactly as the real hardware behaves.", "body"),
    sp(8),
    h2("3.6 Instruction Set"),
    p("Cpu6502.Instructions.cs implements all 56 official instructions:", "body"),
    table(
        [
            ["Group",                   "Instructions"],
            ["Load / Store",            "LDA  LDX  LDY  STA  STX  STY"],
            ["Transfer",                "TAX  TAY  TXA  TYA  TSX  TXS"],
            ["Stack",                   "PHA  PHP  PLA  PLP"],
            ["Logical",                 "AND  EOR  ORA  BIT"],
            ["Arithmetic",              "ADC  SBC  CMP  CPX  CPY"],
            ["Increment / Decrement",   "INC  INX  INY  DEC  DEX  DEY"],
            ["Shift / Rotate",          "ASL  LSR  ROL  ROR"],
            ["Jump / Call",             "JMP  JSR  RTS  RTI  BRK"],
            ["Branch",                  "BCC  BCS  BEQ  BNE  BMI  BPL  BVC  BVS"],
            ["Flag operations",         "CLC  SEC  CLD  SED  CLI  SEI  CLV"],
            ["No-op / Illegal",         "NOP  (XXX stub for illegal opcodes)"],
        ],
        col_widths=[2.0*inch, 4.0*inch],
    ),
    sp(6),
    p("<b>ADC / SBC:</b> SBC is implemented by XOR-inverting the operand "
      "(<code>value ^ 0xFF</code>) and reusing ADC's logic — subtraction with borrow "
      "is identical to addition with the inverted operand when the carry flag acts as "
      "'borrow inverted'. The overflow flag V uses the formula: overflow occurs when "
      "both inputs have the same sign but the result has a different sign.", "body"),
    p("<b>Branches</b> add 1 cycle if taken, and another if the destination crosses a "
      "page boundary — handled entirely inside BranchIf().", "body"),
    sp(8),
    h2("3.7 Interrupts"),
    table(
        [
            ["Interrupt", "Vector",        "Maskable?", "Description"],
            ["Reset",     "$FFFC / $FFFD", "No",        "Reads reset vector; sets SP=$FD. Called on cartridge insert."],
            ["NMI",       "$FFFA / $FFFB", "No",        "Pushes PC+P, jumps to vector. Fired by PPU at VBlank each frame."],
            ["IRQ",       "$FFFE / $FFFF", "Yes (I flag)","Same push sequence. Fired by APU frame counter and DMC channel."],
        ],
        col_widths=[0.75*inch, 1.1*inch, 1.05*inch, 3.1*inch],
    ),
    sp(4),
]

# ── 4. PPU ────────────────────────────────────────────────────────────────────
story += [
    PageBreak(),
    h1("4. The PPU (2C02)"),
    rule(),
    p("The PPU renders 262 scanlines per frame at 341 PPU cycles per scanline:", "body"),
    table(
        [
            ["Scanlines",  "Role"],
            ["−1",         "Pre-render: clears VBlank/sprite-zero/overflow flags; reloads Y scroll"],
            ["0–239",      "Visible: pixel output"],
            ["240",        "Idle"],
            ["241",        "Sets VBlank flag; fires NMI to CPU if PPUCTRL bit 7 is set"],
            ["242–260",    "VBlank period"],
        ],
        col_widths=[1.2*inch, 4.8*inch],
    ),
    sp(8),
    h2("4.1 The Loopy Scroll Mechanism"),
    p("The PPU maintains two 15-bit internal registers (<b>_v</b> and <b>_t</b>), a "
      "3-bit fine-X offset (<b>_x</b>), and a write-latch (<b>_w</b>). The bits of "
      "_v encode five fields simultaneously:", "body"),
    code(
"_v:  yyy NN YYYYY XXXXX\n"
"      |  ||   |     +----- Coarse X: tile column 0-31\n"
"      |  ||   +----------- Coarse Y: tile row 0-29\n"
"      |  +---------------- Nametable select (X/Y)\n"
"      +------------------- Fine Y: pixel row within tile 0-7"
    ),
    p("Writes to $2005 (PPUSCROLL) and $2006 (PPUADDR) manipulate <b>_t</b> and <b>_w</b>. "
      "At cycle 257 of each scanline the X portion of _t is copied into _v; during the "
      "pre-render scanline (cycles 280–304) the Y portion is copied. This is what makes "
      "smooth horizontal and vertical scrolling work.", "body"),
    sp(8),
    h2("4.2 Background Rendering Pipeline"),
    p("Every 8 PPU cycles the pipeline fetches 4 bytes for the next tile column:", "body"),
    bullet("<b>Nametable byte</b> — tile ID from $2000 + (v &amp; $0FFF)"),
    bullet("<b>Attribute byte</b> — 2-bit palette selector from $23C0 + ..."),
    bullet("<b>Pattern low byte</b> — bit-plane 0 of the tile from CHR-ROM"),
    bullet("<b>Pattern high byte</b> — bit-plane 1 of the tile from CHR-ROM"),
    sp(4),
    p("These are loaded into four 16-bit shift registers. Each cycle the registers shift "
      "left by 1, and the bits that fall off — selected by fine-X — form the 2-bit pixel "
      "index and 2-bit palette index for the current dot.", "body"),
    sp(8),
    h2("4.3 Sprite Rendering"),
    p("At cycle 257 of each scanline, <b>EvaluateSprites()</b> scans all 64 OAM entries "
      "and copies up to 8 visible sprites into secondary OAM. At cycle 340, "
      "<b>FetchSpriteTiles()</b> loads their pattern data into 8 pairs of shift registers, "
      "handling both 8×8 and 8×16 sprites and horizontal/vertical flipping.", "body"),
    p("During pixel output, sprite shift registers are scanned left-to-right; the first "
      "non-transparent pixel wins. Priority (behind / in-front-of background) is "
      "controlled by bit 5 of each sprite's attribute byte.", "body"),
    p("<b>Sprite-zero hit</b> is set when both the background and the first OAM entry "
      "produce non-transparent pixels at the same dot. Games use this as a raster timing "
      "signal to split the screen mid-frame.", "body"),
    sp(8),
    h2("4.4 Nametable Mirroring"),
    p("The NES has 2 KB of VRAM but addresses 4 nametables. NtIndex() maps a nametable "
      "address to physical VRAM bank 0 or 1:", "body"),
    table(
        [
            ["Mirror Mode",  "Mapping"],
            ["Horizontal",   "NT 0 and 1 → bank 0; NT 2 and 3 → bank 1 (vertical scrolling)"],
            ["Vertical",     "NT 0 and 2 → bank 0; NT 1 and 3 → bank 1 (horizontal scrolling)"],
            ["Single Low",   "All NTs → bank 0"],
            ["Single High",  "All NTs → bank 1"],
        ],
        col_widths=[1.3*inch, 4.7*inch],
    ),
    p("Mapper 1 (MMC1) changes mirror mode dynamically via its control register, enabling "
      "four-screen scrolling effects.", "body"),
    sp(4),
]

# ── 5. APU ────────────────────────────────────────────────────────────────────
story += [
    PageBreak(),
    h1("5. The APU (2A03)"),
    rule(),
    p("The APU generates audio at ~1.789 MHz and downsamples to 44,100 Hz. Every CPU "
      "cycle, Apu2A03.Clock() advances the channels, then accumulates a fractional "
      "counter to know when to emit a sample:", "body"),
    code(
"_sampleAccum += 44100 / 1_789_773.0;   // ~0.02465 per CPU cycle\n"
"if (_sampleAccum >= 1.0)\n"
"{\n"
"    _sampleAccum -= 1.0;\n"
"    Samples.Add(Mix());                 // ~735 samples/frame at 60 fps\n"
"}"
    ),
    p("After RunFrame() the front-end calls NesAudioProvider.Submit(), which converts "
      "the float samples to IEEE bytes and pushes them into NAudio's BufferedWaveProvider "
      "(80 ms target latency).", "body"),
    sp(8),
    h2("5.1 Channels"),
    table(
        [
            ["Channel",    "Waveform",      "Key Mechanism"],
            ["Pulse 1 & 2","Square wave",   "4 selectable duty cycles (12.5/25/50/75%); sweep unit bends pitch each half-frame"],
            ["Triangle",   "Triangle wave", "32-step sequencer; silenced if timer period < 2 (ultrasonic)"],
            ["Noise",      "White noise",   "15-bit LFSR; short mode (bit 6 feedback) produces metallic tones"],
            ["DMC",        "1-bit delta",   "Reads raw sample bytes from cartridge ROM at $C000+; output climbs/falls by 2 per bit"],
        ],
        col_widths=[0.9*inch, 1.1*inch, 4.0*inch],
    ),
    sp(8),
    h2("5.2 Frame Counter"),
    p("The frame counter steps at CPU cycles 3729, 7457, 11186, and 14915 (4-step mode). "
      "Each step clocks:", "body"),
    bullet("<b>Quarter frame</b> — advances envelope decay and the triangle's linear counter"),
    bullet("<b>Half frame</b> — advances length counters (note durations) and sweep units"),
    sp(4),
    p("5-step mode adds a fifth step at cycle 18641 and disables the frame IRQ. Written "
      "by the game to $4017.", "body"),
    sp(8),
    h2("5.3 Mixing"),
    p("The five channel outputs are combined using the NES's non-linear mixing formula, "
      "approximated linearly. Coefficients are derived from the real NES lookup-table "
      "mixing curves:", "body"),
    code(
"float pulse = 0.00752f * (p1 + p2);\n"
"float tnd   = 0.00851f * tri + 0.00494f * noise + 0.00335f * dmc;\n"
"return pulse + tnd;   // output range approximately 0.0-0.9"
    ),
    sp(4),
]

# ── 6. Cartridges and Mappers ─────────────────────────────────────────────────
story += [
    PageBreak(),
    h1("6. Cartridges and Mappers"),
    rule(),
    p("iNES ROM files begin with a 16-byte header identifying the number of 16 KB "
      "PRG-ROM banks, 8 KB CHR-ROM banks, the mapper number, and the nametable mirror "
      "mode. Cartridge.Load() parses this and instantiates the correct mapper via a "
      "switch expression.", "body"),
    sp(8),
    h2("6.1 Mapper 0 — NROM"),
    p("No banking. A 16 KB ROM mirrors itself to fill $8000–$FFFF; a 32 KB ROM maps "
      "directly. Read-only. Covers Donkey Kong, Pac-Man, Super Mario Bros. 1, and "
      "roughly 10% of the library.", "body"),
    sp(8),
    h2("6.2 Mapper 1 — MMC1"),
    p("Uses a 5-bit serial shift register. Writing bit 7 of any $8000–$FFFF address "
      "resets it. After 5 consecutive writes the accumulated value is loaded into one "
      "of four internal registers based on the address range written:", "body"),
    table(
        [
            ["Address",        "Register", "Controls"],
            ["$8000–$9FFF",    "Control",  "Mirror mode (2 bits), PRG banking mode (2 bits), CHR banking mode (1 bit)"],
            ["$A000–$BFFF",    "CHR Bank 0","4 KB or 8 KB CHR bank at PPU $0000"],
            ["$C000–$DFFF",    "CHR Bank 1","4 KB CHR bank at PPU $1000 (4 KB mode only)"],
            ["$E000–$FFFF",    "PRG Bank", "16 KB PRG bank number (bits 0–3); PRG-RAM disable (bit 4)"],
        ],
        col_widths=[1.1*inch, 1.1*inch, 3.8*inch],
    ),
    sp(6),
    p("PRG banking supports three modes: 32 KB switchable, fix first 16 KB / switch "
      "last, or switch first / fix last. CHR banking supports 8 KB or dual 4 KB modes. "
      "Games with no CHR-ROM (e.g. Zelda, Metroid) use 8 KB of CHR-RAM allocated "
      "internally by the mapper. Covers ~28% of the library.", "body"),
    sp(4),
]

# ── 7. Controllers ────────────────────────────────────────────────────────────
story += [
    PageBreak(),
    h1("7. Controllers"),
    rule(),
    p("Controller holds a single shift register byte. On strobe (Write(1) then "
      "Write(0)), the current button state is latched. Each Read() returns bit 0 and "
      "shifts right, so the CPU reads buttons in order:", "body"),
    table(
        [
            ["Bit", "Button"],
            ["0",   "A"],
            ["1",   "B"],
            ["2",   "Select"],
            ["3",   "Start"],
            ["4",   "Up"],
            ["5",   "Down"],
            ["6",   "Left"],
            ["7",   "Right"],
        ],
        col_widths=[0.6*inch, 5.4*inch],
    ),
    sp(6),
    p("The front-end maps keyboard keys to Controller.SetButton() calls in "
      "MainForm.SetKey():", "body"),
    table(
        [
            ["Key",           "NES Button"],
            ["Z",             "A"],
            ["X",             "B"],
            ["Right Shift",   "Select"],
            ["Enter",         "Start"],
            ["Arrow keys",    "Up / Down / Left / Right"],
        ],
        col_widths=[1.5*inch, 4.5*inch],
    ),
    sp(4),
]

# ── 8. Front-End ──────────────────────────────────────────────────────────────
story += [
    h1("8. Front-End (WinForms)"),
    rule(),
    p("MainForm owns a 16 ms System.Windows.Forms.Timer (~60 fps). Each tick:", "body"),
    bullet("<b>Emulate</b> — Bus.RunFrame() executes ~29,781 CPU cycles, producing one complete video frame and ~735 audio samples."),
    bullet("<b>Audio</b> — NesAudioProvider.Submit() drains the APU sample list into NAudio's ring buffer (300 ms buffer, 80 ms target latency, overflow silently discarded)."),
    bullet("<b>Video</b> — BlitFrame() copies Ppu.FrameBuffer (a uint[] of ARGB pixels) into a 256x240 Bitmap via Marshal.Copy and assigns it to a PictureBox with SizeMode=Zoom."),
    bullet("<b>Debug UI</b> — UpdateUi() refreshes registers, flags, stack, disassembly, and memory dump panels."),
    sp(8),
    h2("8.1 Debugger"),
    p("The debugger panel disassembles $8000–$FFFF on ROM load using "
      "Cpu6502.Disassemble(), which walks the address range interpreting bytes as "
      "instructions and formatting operands. The currently-executing address is "
      "highlighted in blue via owner-draw on the ListBox.", "body"),
    p("Step modes: <b>Step Instruction (F7)</b> advances one CPU instruction at a time; "
      "<b>Step Frame (F8)</b> advances exactly one video frame. <b>Run (F5)</b> starts "
      "the 60 fps timer; <b>Stop (F6)</b> halts it.", "body"),
    sp(4),
]

# ── Build ──────────────────────────────────────────────────────────────────────
def on_page(canvas, doc):
    canvas.saveState()
    canvas.setFont("Helvetica", 8)
    canvas.setFillColor(colors.HexColor("#888888"))
    canvas.drawString(0.85*inch, 0.55*inch,
                      "NES Emulator Architecture Reference")
    canvas.drawRightString(letter[0] - 0.85*inch, 0.55*inch,
                           f"Page {doc.page}")
    canvas.restoreState()

doc.build(story, onFirstPage=on_page, onLaterPages=on_page)
print(f"Written: {OUTPUT}")
