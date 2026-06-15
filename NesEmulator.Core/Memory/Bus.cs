using NesEmulator.Core.Cpu;
using NesEmulator.Core.Input;
using NesEmulator.Core.Ppu;
using Cart = NesEmulator.Core.Cartridge.Cartridge;

namespace NesEmulator.Core.Memory;

public sealed class Bus
{
    private readonly byte[] _ram = new byte[2048];
    public Cpu6502    Cpu        { get; }
    public Ppu2C02    Ppu        { get; }
    public Controller Controller1 { get; } = new();
    public Controller Controller2 { get; } = new();
    public Cart?      Cartridge   { get; private set; }

    public ulong SystemClock { get; private set; }

    public Bus()
    {
        Ppu = new Ppu2C02();
        Cpu = new Cpu6502(this);
    }

    public void InsertCartridge(Cart cart)
    {
        Cartridge = cart;
        Ppu.InsertCartridge(cart);
        Cpu.Reset();
    }

    public void Clock()
    {
        Ppu.Clock();
        if (SystemClock % 3 == 0) Cpu.Clock();
        if (Ppu.NmiRequested) { Ppu.ClearNmi(); Cpu.NMI(); }
        SystemClock++;
    }

    public void RunFrame()
    {
        Ppu.ClearFrameComplete();
        do { Clock(); } while (!Ppu.FrameComplete);
    }

    // ── CPU bus read ──────────────────────────────────────────────────────────
    public byte Read(ushort address)
    {
        if (address <= 0x1FFF)
            return _ram[address & 0x07FF];

        if (address >= 0x2000 && address <= 0x3FFF)
            return Ppu.CpuRead(address);

        if (address == 0x4016) return Controller1.Read();
        if (address == 0x4017) return Controller2.Read();

        if (address >= 0x8000 && Cartridge is not null && Cartridge.CpuRead(address, out byte data))
            return data;

        return 0x00;
    }

    // ── CPU bus write ─────────────────────────────────────────────────────────
    public void Write(ushort address, byte data)
    {
        if (address <= 0x1FFF) { _ram[address & 0x07FF] = data; return; }

        if (address >= 0x2000 && address <= 0x3FFF) { Ppu.CpuWrite(address, data); return; }

        if (address == 0x4014)
        {
            ushort page = (ushort)(data << 8);
            for (int i = 0; i < 256; i++) Ppu.Oam[i] = Read((ushort)(page + i));
            return;
        }

        if (address == 0x4016)
        {
            Controller1.Write(data);
            Controller2.Write(data);
        }
    }
}
