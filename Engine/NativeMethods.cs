using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerRefresher.Engine
{
    /// <summary>Win32 helpers for window state checks.</summary>
    internal static class NativeMethods
    {
        /// <summary>
        /// Sends the window a no-op message and waits up to <paramref name="timeoutMs"/> for it to be processed.
        /// IsHungAppWindow alone misses a process frozen while idle (Windows only flags windows that leave pending input unprocessed).
        /// </summary>
        public static bool Responds(IntPtr hwnd, uint timeoutMs = 5000)
        {
            if (IsHungAppWindow(hwnd)) return false;
            IntPtr result = SendMessageTimeout(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG | SMTO_BLOCK, timeoutMs, out _);
            return result != IntPtr.Zero;
        }

        /// <summary>Visible top-level windows of a process other than <paramref name="except"/>, as "title (class)".</summary>
        public static List<string> OtherVisibleWindows(int processId, IntPtr except)
        {
            List<string> windows = new List<string>();
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == processId && hwnd != except && IsWindowVisible(hwnd))
                {
                    StringBuilder title = new StringBuilder(256);
                    StringBuilder cls = new StringBuilder(256);
                    GetWindowText(hwnd, title, title.Capacity);
                    GetClassName(hwnd, cls, cls.Capacity);
                    //Ignore untitled helper windows (tooltips, IME, drag shadows); dialogs have titles
                    if (title.Length > 0) windows.Add($"'{title}' ({cls})");
                }
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        private const uint WM_NULL = 0x0000;
        private const uint SMTO_BLOCK = 0x0001;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsHungAppWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);
    }
}
