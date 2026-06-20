using NesEmulator.Core.Apu;
using NesEmulator.Core.Cpu;
using NesEmulator.Core.Input;
using NesEmulator.Core.Ppu;
using NesEmulator.Core;
using Cart = NesEmulator.Core.Cartridge.Cartridge;

namespace NesEmulator.Core.Memory;

public sealed class Bus
{
    private readonly byte[] _ram = new byte[2048];
    public Cpu6502     Cpu         { get; }
    public Ppu2C02     Ppu         { get; }
    public Apu2A03     Apu         { get; }
    public Controller  Controller1 { get; } = new();
    public Controller  Controller2 { get; } = new();
    public Cart?       Cartridge   { get; private set; }

    public ulong SystemClock { get; private set; }

    private TvSystem _tvSystem = TvSystem.Ntsc;
    public TvSystem TvSystem
    {
        get => _tvSystem;
        set { _tvSystem = value; Ppu.TvSystem = value; Apu.TvSystem = value; }
    }

    public Bus()
    {
        Ppu = new Ppu2C02();
        Cpu = new Cpu6502(this);
        Apu = new Apu2A03(Read);  // DMC reads samples via CPU bus
    }

    // Auto-detects TV system from the ROM header; caller can override afterward via TvSystem.
    public void InsertCartridge(Cart cart)
    {
        Cartridge = cart;
        Ppu.InsertCartridge(cart);
        TvSystem = cart.DetectedTvSystem;
        Cpu.Reset();
    }

    public void Clock()
    {
        Ppu.Clock();
        if (SystemClock % 3 == 0)
        {
            Cpu.Clock();
            Apu.Clock();
        }
        if (Ppu.NmiRequested) { Ppu.ClearNmi(); Cpu.NMI(); }
        if (Apu.IrqPending)   Cpu.IRQ();
        if (Cartridge?.IrqPending == true) Cpu.IRQ();
        SystemClock++;
    }

    public void RunFrame()
    {
        Ppu.ClearFrameComplete();
        do { Clock(); } while (!Ppu.FrameComplete);
    }

    // ── Save / Load state ─────────────────────────────────────────────────────
    private static readonly byte[] StateMagic = "NST1"u8.ToArray();

    public void SaveState(string path)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(StateMagic);
        bw.Write((int)_tvSystem);
        bw.Write(_ram);
        bw.Write(SystemClock);
        Cpu.SaveState(bw);
        Ppu.SaveState(bw);
        Apu.SaveState(bw);
        Controller1.SaveState(bw);
        Controller2.SaveState(bw);
        Cartridge!.SaveState(bw);
    }

    public void LoadState(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        var magic = br.ReadBytes(4);
        if (!magic.SequenceEqual(StateMagic))
            throw new InvalidDataException("Not a valid NES save-state file.");
        TvSystem = (TvSystem)br.ReadInt32();
        br.Read(_ram);
        SystemClock = br.ReadUInt64();
        Cpu.LoadState(br);
        Ppu.LoadState(br);
        Apu.LoadState(br);
        Controller1.LoadState(br);
        Controller2.LoadState(br);
        Cartridge!.LoadState(br);
    }

    // ── CPU bus read ──────────────────────────────────────────────────────────
    public byte Read(ushort address)
    {
        if (address <= 0x1FFF)
            return _ram[address & 0x07FF];

        if (address >= 0x2000 && address <= 0x3FFF)
            return Ppu.CpuRead(address);

        if (address == 0x4015) return Apu.CpuRead(address);
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
            return;
        }

        // APU registers: $4000-$4013, $4015, $4017
        if ((address >= 0x4000 && address <= 0x4013) || address == 0x4015 || address == 0x4017)
        {
            Apu.CpuWrite(address, data);
            return;
        }

        if (address >= 0x8000) Cartridge?.CpuWrite(address, data);
    }
}
