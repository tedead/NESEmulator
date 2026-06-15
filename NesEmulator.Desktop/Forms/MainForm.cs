using NesEmulator.Core.Cpu;
using NesEmulator.Core.Memory;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace NesEmulator.Desktop.Forms;

public sealed class MainForm : Form
{
    // ── Emulator ──────────────────────────────────────────────────────────────
    private readonly Bus  _bus     = new();
    private Dictionary<ushort, string> _disasm = [];
    private bool _running;
    private readonly Timer _runTimer = new() { Interval = 16 }; // ~60 fps

    // Video bitmap (256×240, reused every frame)
    private readonly Bitmap _frameBitmap = new(256, 240, PixelFormat.Format32bppArgb);

    // ── UI controls ───────────────────────────────────────────────────────────
    private readonly PictureBox  _picVideo     = new();
    private readonly ListBox     _lstDisasm    = new();
    private readonly Label       _lblRegisters = new();
    private readonly Label       _lblFlags     = new();
    private readonly Label       _lblStack     = new();
    private readonly RichTextBox _txtMemory    = new();
    private readonly Button      _btnStep      = new();
    private readonly Button      _btnRun       = new();
    private readonly Button      _btnStop      = new();
    private readonly Button      _btnReset     = new();
    private readonly Label       _lblCycles    = new();
    private readonly Label       _lblStatus    = new();

    public MainForm()
    {
        Text          = "NES Emulator — CPU + PPU Debugger";
        Size          = new Size(1200, 720);
        MinimumSize   = new Size(1100, 680);
        BackColor     = Color.FromArgb(28, 28, 28);
        ForeColor     = Color.FromArgb(220, 220, 220);
        Font          = new Font("Consolas", 10);

        BuildMenu();
        BuildLayout();

        _runTimer.Tick += RunTimer_Tick;

        KeyPreview = true;
        KeyDown += OnKeyDown;
        KeyUp   += OnKeyUp;

        UpdateUi();
    }

    // ── Controller key mapping ────────────────────────────────────────────────
    // A=Z  B=X  Select=RShift  Start=Enter  Up/Down/Left/Right=Arrow keys
    private void OnKeyDown(object? sender, KeyEventArgs e) => SetKey(e.KeyCode, true);
    private void OnKeyUp  (object? sender, KeyEventArgs e) => SetKey(e.KeyCode, false);

    private void SetKey(Keys key, bool pressed)
    {
        var c = _bus.Controller1;
        switch (key)
        {
            case Keys.Z:          c.SetButton(Core.Input.Controller.Button.A,      pressed); break;
            case Keys.X:          c.SetButton(Core.Input.Controller.Button.B,      pressed); break;
            case Keys.RShiftKey:
            case Keys.ShiftKey:   c.SetButton(Core.Input.Controller.Button.Select, pressed); break;
            case Keys.Enter:      c.SetButton(Core.Input.Controller.Button.Start,  pressed); break;
            case Keys.Up:         c.SetButton(Core.Input.Controller.Button.Up,     pressed); break;
            case Keys.Down:       c.SetButton(Core.Input.Controller.Button.Down,   pressed); break;
            case Keys.Left:       c.SetButton(Core.Input.Controller.Button.Left,   pressed); break;
            case Keys.Right:      c.SetButton(Core.Input.Controller.Button.Right,  pressed); break;
        }
    }

    // ── Menu ──────────────────────────────────────────────────────────────────
    private void BuildMenu()
    {
        var menu = new MenuStrip { BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White };

        var fileMenu = new ToolStripMenuItem("File");
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Load ROM…", null, OnLoadRom) { ShortcutKeys = Keys.Control | Keys.O });
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Exit", null, (_, _) => Close()));

        var emuMenu = new ToolStripMenuItem("Emulator");
        emuMenu.DropDownItems.Add(new ToolStripMenuItem("Step Instruction", null, (_, _) => StepInstruction()) { ShortcutKeys = Keys.F7 });
        emuMenu.DropDownItems.Add(new ToolStripMenuItem("Step Frame",       null, (_, _) => StepFrame())       { ShortcutKeys = Keys.F8 });
        emuMenu.DropDownItems.Add(new ToolStripMenuItem("Run",              null, (_, _) => Run())             { ShortcutKeys = Keys.F5 });
        emuMenu.DropDownItems.Add(new ToolStripMenuItem("Stop",             null, (_, _) => Stop())            { ShortcutKeys = Keys.F6 });
        emuMenu.DropDownItems.Add(new ToolStripSeparator());
        emuMenu.DropDownItems.Add(new ToolStripMenuItem("Reset",            null, (_, _) => ResetSystem())     { ShortcutKeys = Keys.F2 });

        menu.Items.AddRange([fileMenu, emuMenu]);
        Controls.Add(menu);
        MainMenuStrip = menu;
    }

    // ── Layout ────────────────────────────────────────────────────────────────
    private void BuildLayout()
    {
        var dark  = Color.FromArgb(28, 28, 28);
        var panel = Color.FromArgb(40, 40, 40);
        var text  = Color.FromArgb(220, 220, 220);

        // Left: video output (256×240 scaled 2×)
        var videoGroup = MakeGroup("Video Output  (256×240)", 8, 30, 524, 510);
        _picVideo.Dock          = DockStyle.Fill;
        _picVideo.SizeMode      = PictureBoxSizeMode.Zoom;
        _picVideo.BackColor     = Color.Black;
        videoGroup.Controls.Add(_picVideo);

        // Right-top: registers
        var regGroup = MakeGroup("Registers", 540, 30, 200, 140);
        _lblRegisters.Dock      = DockStyle.Fill;
        _lblRegisters.BackColor = panel;
        _lblRegisters.ForeColor = text;
        _lblRegisters.Font      = new Font("Consolas", 10);
        _lblRegisters.Padding   = new Padding(6);
        regGroup.Controls.Add(_lblRegisters);

        // Right-top: flags
        var flagGroup = MakeGroup("Flags", 748, 30, 430, 140);
        _lblFlags.Dock      = DockStyle.Fill;
        _lblFlags.BackColor = panel;
        _lblFlags.ForeColor = text;
        _lblFlags.Font      = new Font("Consolas", 10);
        _lblFlags.Padding   = new Padding(6);
        flagGroup.Controls.Add(_lblFlags);

        // Right-mid: disassembler
        var disasmGroup = MakeGroup("Disassembly", 540, 178, 638, 260);
        _lstDisasm.Dock         = DockStyle.Fill;
        _lstDisasm.BackColor    = dark;
        _lstDisasm.ForeColor    = text;
        _lstDisasm.BorderStyle  = BorderStyle.None;
        _lstDisasm.Font         = new Font("Consolas", 10);
        _lstDisasm.DrawMode     = DrawMode.OwnerDrawFixed;
        _lstDisasm.ItemHeight   = 18;
        _lstDisasm.DrawItem    += LstDisasm_DrawItem;
        disasmGroup.Controls.Add(_lstDisasm);

        // Right-mid: stack
        var stackGroup = MakeGroup("Stack", 540, 446, 638, 90);
        _lblStack.Dock      = DockStyle.Fill;
        _lblStack.BackColor = panel;
        _lblStack.ForeColor = text;
        _lblStack.Font      = new Font("Consolas", 10);
        _lblStack.Padding   = new Padding(6);
        stackGroup.Controls.Add(_lblStack);

        // Bottom: memory
        var memGroup = MakeGroup("Memory  $0000–$00FF", 8, 548, 1170, 130);
        _txtMemory.Dock        = DockStyle.Fill;
        _txtMemory.BackColor   = dark;
        _txtMemory.ForeColor   = text;
        _txtMemory.Font        = new Font("Consolas", 9);
        _txtMemory.ReadOnly    = true;
        _txtMemory.BorderStyle = BorderStyle.None;
        _txtMemory.ScrollBars  = RichTextBoxScrollBars.Vertical;
        memGroup.Controls.Add(_txtMemory);

        // Controls
        var ctrlPanel = new Panel { Bounds = new Rectangle(540, 540, 638, 44), BackColor = Color.Transparent };

        StyleButton(_btnStep,  "Step (F7)", Color.FromArgb(0, 100, 180));
        StyleButton(_btnRun,   "Run (F5)",  Color.FromArgb(40, 140, 40));
        StyleButton(_btnStop,  "Stop (F6)", Color.FromArgb(180, 60, 60));
        StyleButton(_btnReset, "Reset (F2)",Color.FromArgb(130, 80, 0));

        _btnStep.Bounds  = new Rectangle(0,   4, 110, 36);
        _btnRun.Bounds   = new Rectangle(116, 4, 110, 36);
        _btnStop.Bounds  = new Rectangle(232, 4, 110, 36);
        _btnReset.Bounds = new Rectangle(348, 4, 110, 36);

        _btnStep.Click  += (_, _) => StepInstruction();
        _btnRun.Click   += (_, _) => Run();
        _btnStop.Click  += (_, _) => Stop();
        _btnReset.Click += (_, _) => ResetSystem();

        _lblCycles.Bounds    = new Rectangle(466, 8, 170, 22);
        _lblCycles.BackColor = Color.Transparent;
        _lblCycles.ForeColor = Color.FromArgb(140, 140, 140);
        _lblCycles.Font      = new Font("Consolas", 9);

        _lblStatus.Bounds    = new Rectangle(0, 4, 638, 40);
        _lblStatus.BackColor = Color.Transparent;
        _lblStatus.ForeColor = Color.FromArgb(150, 210, 150);
        _lblStatus.Font      = new Font("Consolas", 9);
        _lblStatus.TextAlign = ContentAlignment.MiddleRight;

        ctrlPanel.Controls.AddRange([_btnStep, _btnRun, _btnStop, _btnReset, _lblCycles, _lblStatus]);
        Controls.Add(ctrlPanel);
    }

    // ── Owner-draw disassembler ───────────────────────────────────────────────
    private void LstDisasm_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        string line = (string)_lstDisasm.Items[e.Index];
        bool isCurrent = line.StartsWith($"${_bus.Cpu.PC:X4}:");
        var bg = isCurrent ? Color.FromArgb(0, 80, 150)
               : e.Index % 2 == 0 ? Color.FromArgb(28, 28, 28)
               : Color.FromArgb(34, 34, 34);
        e.Graphics.FillRectangle(new SolidBrush(bg), e.Bounds);
        TextRenderer.DrawText(e.Graphics, line, e.Font ?? Font, e.Bounds,
            isCurrent ? Color.White : Color.FromArgb(210, 210, 210),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    // ── Emulator actions ──────────────────────────────────────────────────────
    private void StepInstruction()
    {
        if (_bus.Cartridge is null) return;
        do { _bus.Clock(); } while (!_bus.Cpu.Complete);
        BlitFrame();
        UpdateUi();
    }

    private void StepFrame()
    {
        if (_bus.Cartridge is null) return;
        _bus.RunFrame();
        BlitFrame();
        UpdateUi();
    }

    private void Run()
    {
        if (_bus.Cartridge is null || _running) return;
        _running = true;
        _btnRun.Enabled = _btnStep.Enabled = false;
        _lblStatus.Text = "Running";
        _runTimer.Start();
    }

    private void Stop()
    {
        _running = false;
        _runTimer.Stop();
        _btnRun.Enabled = _btnStep.Enabled = true;
        UpdateUi();
    }

    private void ResetSystem()
    {
        Stop();
        _bus.Cpu.Reset();
        if (_bus.Cartridge is not null) RefreshDisasm();
        UpdateUi();
    }

    private void RunTimer_Tick(object? sender, EventArgs e)
    {
        if (_bus.Cartridge is null) return;
        _bus.RunFrame();
        BlitFrame();
        UpdateUi();
    }

    private void OnLoadRom(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Title = "Load NES ROM", Filter = "NES ROM (*.nes)|*.nes|All files|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            Stop();
            var cart = Core.Cartridge.Cartridge.Load(dlg.FileName);
            _bus.InsertCartridge(cart);
            RefreshDisasm();
            UpdateUi();
            _lblStatus.Text = $"Loaded: {cart.FileName}  |  Mapper {cart.MapperId}  |  Mirror: {cart.Mirror}";
            Text = $"NES Emulator — {cart.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshDisasm() =>
        _disasm = _bus.Cpu.Disassemble(0x8000, 0xFFFF);

    // ── Frame blit ────────────────────────────────────────────────────────────
    private void BlitFrame()
    {
        var rect = new Rectangle(0, 0, 256, 240);
        var bmpData = _frameBitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(
            Array.ConvertAll(_bus.Ppu.FrameBuffer, v => (int)v),
            0, bmpData.Scan0, _bus.Ppu.FrameBuffer.Length);
        _frameBitmap.UnlockBits(bmpData);
        _picVideo.Image = _frameBitmap;
        _picVideo.Refresh();
    }

    // ── UI update ─────────────────────────────────────────────────────────────
    private void UpdateUi()
    {
        var cpu = _bus.Cpu;

        _lblRegisters.Text =
            $"PC: ${cpu.PC:X4}\r\n" +
            $" A: ${cpu.A:X2}  ({cpu.A})\r\n" +
            $" X: ${cpu.X:X2}  ({cpu.X})\r\n" +
            $" Y: ${cpu.Y:X2}  ({cpu.Y})\r\n" +
            $"SP: ${cpu.SP:X2}";

        static string F(bool v, string n) => v ? $"[{n}]" : $" {n} ";
        _lblFlags.Text =
            $"N={F(cpu.GetFlag(Cpu6502.Flag.N),"N")}  V={F(cpu.GetFlag(Cpu6502.Flag.V),"V")}  " +
            $"U={F(cpu.GetFlag(Cpu6502.Flag.U),"U")}  B={F(cpu.GetFlag(Cpu6502.Flag.B),"B")}\r\n" +
            $"D={F(cpu.GetFlag(Cpu6502.Flag.D),"D")}  I={F(cpu.GetFlag(Cpu6502.Flag.I),"I")}  " +
            $"Z={F(cpu.GetFlag(Cpu6502.Flag.Z),"Z")}  C={F(cpu.GetFlag(Cpu6502.Flag.C),"C")}";

        // Stack
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 8; i++)
        {
            ushort addr = (ushort)(0x0100 + ((cpu.SP + 1 + i) & 0xFF));
            sb.Append($"${addr:X4}:${_bus.Read(addr):X2}  ");
            if (i % 4 == 3) sb.AppendLine();
        }
        _lblStack.Text = sb.ToString();

        _lblCycles.Text = $"Cycles: {cpu.TotalCycles:N0}";

        // Disassembler
        if (_disasm.Count > 0)
        {
            var keys  = _disasm.Keys.OrderBy(k => k).ToList();
            int pcIdx = keys.BinarySearch(cpu.PC);
            if (pcIdx < 0) pcIdx = ~pcIdx;
            int start = Math.Max(0, pcIdx - 8);
            int end   = Math.Min(keys.Count - 1, pcIdx + 18);

            _lstDisasm.BeginUpdate();
            _lstDisasm.Items.Clear();
            for (int i = start; i <= end; i++) _lstDisasm.Items.Add(_disasm[keys[i]]);
            int vis = pcIdx - start;
            if (vis >= 0 && vis < _lstDisasm.Items.Count) _lstDisasm.SelectedIndex = vis;
            _lstDisasm.EndUpdate();
            _lstDisasm.Refresh();
        }

        // Memory
        var mem = new System.Text.StringBuilder();
        for (ushort row = 0; row <= 0xF0; row += 16)
        {
            mem.Append($"${row:X4}: ");
            for (int col = 0; col < 16; col++) mem.Append($"{_bus.Read((ushort)(row + col)):X2} ");
            mem.AppendLine();
        }
        _txtMemory.Text = mem.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private GroupBox MakeGroup(string title, int x, int y, int w, int h)
    {
        var g = new GroupBox
        {
            Text = title, Bounds = new Rectangle(x, y, w, h),
            BackColor = Color.FromArgb(40, 40, 40),
            ForeColor = Color.FromArgb(140, 140, 140),
            Font = new Font("Consolas", 8)
        };
        Controls.Add(g);
        return g;
    }

    private static void StyleButton(Button btn, string text, Color color)
    {
        btn.Text = text;
        btn.FlatStyle = FlatStyle.Flat;
        btn.BackColor = color;
        btn.ForeColor = Color.White;
        btn.FlatAppearance.BorderSize = 0;
        btn.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        btn.Cursor = Cursors.Hand;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _runTimer.Dispose(); _frameBitmap.Dispose(); }
        base.Dispose(disposing);
    }
}
