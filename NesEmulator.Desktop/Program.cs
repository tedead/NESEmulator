using System.Runtime.InteropServices;
using NesEmulator.Desktop.Forms;

namespace NesEmulator.Desktop;

static class Program
{
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint uMilliseconds);

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Windows' default system timer tick is ~15.6ms, which starves
        // System.Windows.Forms.Timer (WM_TIMER) of the precision a 60fps
        // emulator needs — overruns can cause it to skip an entire extra
        // tick instead of firing promptly. Requesting 1ms resolution for
        // the process lifetime fixes this.
        TimeBeginPeriod(1);
        try
        {
            Application.Run(new MainForm());
        }
        finally
        {
            TimeEndPeriod(1);
        }
    }
}