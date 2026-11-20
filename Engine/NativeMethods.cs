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

        /// <summary>Every process started by <paramref name="processId"/>, its children's children and so on (Power BI's
        /// data engine, query containers and browser processes), deepest first.</summary>
        public static List<int> DescendantProcessIds(int processId)
        {
            Dictionary<int, List<int>> children = new Dictionary<int, List<int>>();
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == INVALID_HANDLE_VALUE) return new List<int>();
            try
            {
                PROCESSENTRY32 entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                for (bool ok = Process32First(snapshot, ref entry); ok; ok = Process32Next(snapshot, ref entry))
                {
                    int parent = (int)entry.th32ParentProcessID, pid = (int)entry.th32ProcessID;
                    if (pid == parent) continue;
                    if (!children.TryGetValue(parent, out List<int> list)) children[parent] = list = new List<int>();
                    list.Add(pid);
                }
            }
            finally { CloseHandle(snapshot); }

            List<int> result = new List<int>();
            HashSet<int> seen = new HashSet<int> { processId };
            void Walk(int pid)
            {
                if (!children.TryGetValue(pid, out List<int> list)) return;
                foreach (int child in list)
                {
                    if (!seen.Add(child)) continue; //Process ids get reused; guard against cycles
                    Walk(child);
                    result.Add(child);
                }
            }
            Walk(processId);
            return result;
        }

        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize, cntUsage, th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID, cntThreads, th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")]
        private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")]
        private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

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
