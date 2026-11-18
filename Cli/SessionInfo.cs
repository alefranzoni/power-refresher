using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PowerRefresher.Cli
{
    /// <summary>
    /// Facts about the Windows session the CLI runs in. UI automation of Power BI needs a real, interactive desktop:
    /// Task Scheduler's "Run whether user is logged on or not" runs tasks without one (session 0 or a hidden
    /// desktop), where Power BI's window never becomes usable.
    /// </summary>
    internal static class SessionInfo
    {
        public static int SessionId => Process.GetCurrentProcess().SessionId;

        /// <summary>False in services and in tasks set to "Run whether user is logged on or not".</summary>
        public static bool HasInteractiveDesktop => SessionId != 0 && Environment.UserInteractive && IsOnDefaultDesktop();

        /// <summary>True while the workstation is locked (the input desktop is the secure Winlogon desktop).</summary>
        public static bool IsLocked()
        {
            IntPtr desktop = OpenInputDesktop(0, false, DESKTOP_SWITCHDESKTOP);
            if (desktop == IntPtr.Zero) return true;
            try { return !SwitchDesktop(desktop); }
            finally { CloseDesktop(desktop); }
        }

        public static bool IsRemote => SystemInformation.TerminalServerSession;

        /// <summary>"active", "disconnected", ... for the current session, or "unknown".</summary>
        public static string ConnectionState()
        {
            if (!WTSQuerySessionInformation(IntPtr.Zero, WTS_CURRENT_SESSION, WTSConnectState, out IntPtr buffer, out _)) return "unknown";
            try
            {
                switch (Marshal.ReadInt32(buffer))
                {
                    case 0: return "active";
                    case 1: return "connected";
                    case 4: return "disconnected";
                    default: return "state " + Marshal.ReadInt32(buffer);
                }
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }

        public static string Describe() =>
            $"session {SessionId}, {(IsRemote ? "remote desktop" : "console")}, {ConnectionState()}, {(IsLocked() ? "locked" : "unlocked")}";

        /// <summary>The thread's desktop is the user's normal "Default" desktop (not a hidden one Task Scheduler made).</summary>
        private static bool IsOnDefaultDesktop()
        {
            IntPtr desktop = GetThreadDesktop(GetCurrentThreadId());
            if (desktop == IntPtr.Zero) return false;
            byte[] name = new byte[256];
            if (!GetUserObjectInformation(desktop, UOI_NAME, name, name.Length, out int needed)) return true; //Can't tell: don't block the run
            string desktopName = System.Text.Encoding.Unicode.GetString(name, 0, Math.Max(0, Math.Min(needed, name.Length) - 2));
            return string.Equals(desktopName, "Default", StringComparison.OrdinalIgnoreCase);
        }

        private const uint DESKTOP_SWITCHDESKTOP = 0x0100;
        private const int UOI_NAME = 2;
        private const int WTS_CURRENT_SESSION = -1;
        private const int WTSConnectState = 8;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint desiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SwitchDesktop(IntPtr desktop);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseDesktop(IntPtr desktop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetThreadDesktop(uint threadId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        //Unicode entry point explicitly: the default ANSI one returns the name as single-byte text
        [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetUserObjectInformation(IntPtr obj, int index, byte[] info, int length, out int lengthNeeded);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr memory);
    }
}
