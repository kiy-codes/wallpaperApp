using System.Runtime.InteropServices;

namespace WallpaperApp.Native;

/// <summary>A hidden message-only window that owns one global hotkey (RegisterHotKey needs no admin).</summary>
public sealed class HotkeyWindow : NativeWindow, IDisposable
{
    const int WmHotkey = 0x0312;
    const int HotkeyId = 1;
    const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000;
    static readonly IntPtr HwndMessage = new(-3);

    bool _registered;

    public event Action? Pressed;

    public HotkeyWindow() => CreateHandle(new CreateParams { Parent = HwndMessage });

    /// <summary>Replaces any previous hotkey. Returns false if another app already owns the combination.</summary>
    public bool Register(Keys keys)
    {
        Unregister();
        if (keys == Keys.None) return true;

        uint modifiers = ModNoRepeat;
        if (keys.HasFlag(Keys.Control)) modifiers |= ModControl;
        if (keys.HasFlag(Keys.Alt)) modifiers |= ModAlt;
        if (keys.HasFlag(Keys.Shift)) modifiers |= ModShift;

        _registered = RegisterHotKey(Handle, HotkeyId, modifiers, (uint)(keys & Keys.KeyCode));
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(Handle, HotkeyId);
        _registered = false;
    }

    public static Keys Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Keys.None;
        try { return (Keys)(new KeysConverter().ConvertFromInvariantString(text) ?? Keys.None); }
        catch (Exception) { return Keys.None; }
    }

    public static string Format(Keys keys) => new KeysConverter().ConvertToInvariantString(keys) ?? "";

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam == HotkeyId) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
