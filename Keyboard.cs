using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BetterFollowbot;

public static class Keyboard
{
        
    private const int KeyeventfExtendedkey = 0x0001;
    private const int KeyeventfKeyup = 0x0002;

    [DllImport("user32.dll")]
    private static extern uint keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern bool BlockInput(bool fBlockIt);

    public static void KeyDown(Keys key)
    {
        keybd_event((byte) key, 0, KeyeventfExtendedkey | 0, 0);
    }

    public static void KeyUp(Keys key)
    {
        keybd_event((byte) key, 0, KeyeventfExtendedkey | KeyeventfKeyup, 0); //0x7F
    }

    [DllImport("USER32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    public static bool IsKeyDown(int nVirtKey)
    {
        return GetKeyState(nVirtKey) < 0;
    }
        
    public static void KeyPress(Keys key, bool anyDelay = true)
    {
        if (anyDelay)
            BetterFollowbot.Instance.LastTimeAny = DateTime.Now;
        KeyPressRoutine(key);
    }

    private static void KeyPressRoutine(Keys key)
    {
        KeyDown(key);
        System.Threading.Thread.Sleep(20);
        KeyUp(key);
    }
}