using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ExileCore.Shared;
using SharpDX;
using BetterFollowbot.Core;

namespace BetterFollowbot;

public static class Mouse
{
    public const int MouseeventfMove = 0x0001;
    public const int MouseeventfLeftdown = 0x02;
    public const int MouseeventfLeftup = 0x04;
    public const int MouseeventfMiddown = 0x0020;
    public const int MouseeventfMidup = 0x0040;
    public const int MouseeventfRightdown = 0x0008;
    public const int MouseeventfRightup = 0x0010;
    public const int MouseEventWheel = 0x800;
        
    public static float speedMouse = 1;
    private static float? _screenScaleFactor = null;

    private enum DeviceCap
    {
        VERTRES = 10,
        DESKTOPVERTRES = 117
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true, ExactSpelling = true)]
    private static extern int GetDeviceCaps(nint hDC, int nIndex);

    public static float ScreenScaleFactor
    {
        get
        {
            if (_screenScaleFactor == null)
            {
                CalcScreenScaleFactor();
            }
            return _screenScaleFactor ?? 1.0f;
        }
    }

    private static void CalcScreenScaleFactor()
    {
        try
        {
            using System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(nint.Zero);
            nint desktop = g.GetHdc();
            int logicalScreenHeight = GetDeviceCaps(desktop, (int)DeviceCap.VERTRES);
            int physicalScreenHeight = GetDeviceCaps(desktop, (int)DeviceCap.DESKTOPVERTRES);
            g.ReleaseHdc(desktop);
            
            _screenScaleFactor = physicalScreenHeight / (float)logicalScreenHeight;
        }
        catch
        {
            _screenScaleFactor = 1.0f;
        }
    }

    public static Vector2 ApplyDpiScaling(Vector2 position)
    {
        float dpiScale = 1 / ScreenScaleFactor;
        return new Vector2(position.X * dpiScale, position.Y * dpiScale);
    }
    public static bool IsMouseLeftPressed()
    {
        return Control.MouseButtons == MouseButtons.Left;
    }
    [DllImport("user32.dll")]
    private static extern void mouse_event(int dwFlags, int dx, int dy, int cButtons, int dwExtraInfo);
    public static void LeftMouseDown()
    {
        mouse_event(MouseeventfLeftdown, 0, 0, 0, 0);
        APMTracker.RecordAction(); // Track action for APM
    }

    public static void LeftMouseUp()
    {
        mouse_event(MouseeventfLeftup, 0, 0, 0, 0);
    }

    public static void RightMouseDown()
    {
        mouse_event(MouseeventfRightdown, 0, 0, 0, 0);
    }

    public static void RightMouseUp()
    {
        mouse_event(MouseeventfRightup, 0, 0, 0, 0);
    }
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    public static void SetCursorPos(Vector2 vec)
    {
        SetCursorPos((int)vec.X, (int)vec.Y);
    }

    public static void LeftClick()
    {
        LeftMouseDown();
        System.Threading.Thread.Sleep(40);
        LeftMouseUp();
        System.Threading.Thread.Sleep(100);
    }
        
    public static void RightClick()
    {
        RightMouseDown();
        System.Threading.Thread.Sleep(40);
        RightMouseUp();
        System.Threading.Thread.Sleep(100);
    }

    public static void SetCursorPosHuman(Vector2 targetPos, bool limited = true)
    {
        // Keep Curser Away from Screen Edges to prevent UI Interaction.
        var windowRect = BetterFollowbot.Instance.GameController.Window.GetWindowRectangle();
        var edgeBoundsX = windowRect.Size.Width / 4;
        var edgeBoundsY = windowRect.Size.Height / 4;

        if (limited)
        {
            if (targetPos.X <= windowRect.Left + edgeBoundsX ) targetPos.X = windowRect.Left + edgeBoundsX;
            if (targetPos.Y <= windowRect.Top + edgeBoundsY) targetPos.Y = windowRect.Left + edgeBoundsY;
            if (targetPos.X >= windowRect.Right - edgeBoundsX) targetPos.X = windowRect.Right -edgeBoundsX;
            if (targetPos.Y >= windowRect.Bottom - edgeBoundsY) targetPos.Y = windowRect.Bottom - edgeBoundsY;
        }


        var step = (float)Math.Sqrt(Vector2.Distance(BetterFollowbot.Instance.GetMousePosition(), targetPos)) * speedMouse / 20;

        if (step > 6)
            for (var i = 0; i < step; i++)
            {
                var vector2 = Vector2.SmoothStep(BetterFollowbot.Instance.GetMousePosition(), targetPos, i / step);
                SetCursorPos((int)vector2.X, (int)vector2.Y);
                System.Threading.Thread.Sleep(5);
            }
        else
            SetCursorPos(targetPos);
    }
    public static void SetCursorPosAndLeftClickHuman(Vector2 coords, int extraDelay)
    {
        SetCursorPos(coords);
        System.Threading.Thread.Sleep(BetterFollowbot.Instance.Settings.autoPilotInputFrequency + extraDelay);
        LeftMouseDown();
        System.Threading.Thread.Sleep(BetterFollowbot.Instance.Settings.autoPilotInputFrequency + extraDelay);
        LeftMouseUp();
        System.Threading.Thread.Sleep(100);
    }
}