using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning; // Needed
using System.Text;

namespace OverlayApp.Services
{
    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = "";
        public override string ToString() => Title;
    }

    [SupportedOSPlatform("windows")] // <--- FIXES PLATFORM WARNINGS
    public static class WindowFactory
    {
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        public static List<WindowInfo> GetOpenWindows()
        {
            var list = new List<WindowInfo>();
            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd) && GetWindowTextLength(hWnd) > 0)
                {
                    StringBuilder sb = new StringBuilder(256);
                    GetWindowText(hWnd, sb, 256);
                    string title = sb.ToString();

                    if (!string.IsNullOrWhiteSpace(title) && title != "Program Manager")
                    {
                        list.Add(new WindowInfo { Handle = hWnd, Title = title });
                    }
                }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static Bitmap? CaptureWindow(IntPtr handle)
        {
            try
            {
                GetWindowRect(handle, out RECT rect);
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;

                if (width <= 0 || height <= 0) return null;

                var bmp = new Bitmap(width, height);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
                }
                return bmp;
            }
            catch { return null; }
        }
    }
}