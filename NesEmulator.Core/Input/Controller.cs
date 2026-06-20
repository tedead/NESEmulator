namespace NesEmulator.Core.Input;

/// <summary>
/// NES standard controller. Buttons are latched on strobe, then read serially.
/// Bit order: A, B, Select, Start, Up, Down, Left, Right
/// </summary>
public sealed class Controller
{
    public enum Button { A, B, Select, Start, Up, Down, Left, Right }

    private byte _liveState;  // updated by host each frame
    private byte _shift;      // serial shift register
    private bool _strobe;

    public void SetButton(Button btn, bool pressed)
    {
        int bit = (int)btn;
        if (pressed) _liveState |=  (byte)(1 << bit);
        else         _liveState &= (byte)~(1 << bit);
    }

    // CPU writes $4016
    public void Write(byte data)
    {
        _strobe = (data & 0x01) != 0;
        if (_strobe) _shift = _liveState; // latch on high strobe
    }

    public void SaveState(BinaryWriter bw) { bw.Write(_liveState); bw.Write(_shift); bw.Write(_strobe); }
    public void LoadState(BinaryReader br) { _liveState = br.ReadByte(); _shift = br.ReadByte(); _strobe = br.ReadBoolean(); }

    // CPU reads $4016 / $4017
    public byte Read()
    {
        if (_strobe) return (byte)(_liveState & 0x01); // A button held while strobing

        byte bit = (byte)(_shift & 0x01);
        _shift = (byte)((_shift >> 1) | 0x80); // fill from top after all 8 bits
        return bit;
    }
}
