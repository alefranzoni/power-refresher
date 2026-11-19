using System;

namespace PowerRefresher.Engine
{
    public enum LogLevel
    {
        /// <summary>Internal detail: lookups, window handles, timings of sub-steps. Log file only by default.</summary>
        Debug,
        /// <summary>Normal progress: each step starting and finishing.</summary>
        Info,
        /// <summary>Something unexpected that was worked around (e.g. a fallback lookup was needed).</summary>
        Warn,
        /// <summary>A step failed.</summary>
        Error
    }

    public static class LogFormat
    {
        /// <summary>"1h 02m 03s", "4m 05s" or "6.7s".</summary>
        public static string Duration(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:D2}m {t.Seconds:D2}s"
            : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds:D2}s"
            : $"{t.TotalSeconds:0.0}s";

        /// <summary>"2026-11-18 16:30:01.123  INFO   message", with continuation lines indented under the message.</summary>
        public static string Line(DateTime time, LogLevel level, string message)
        {
            string prefix = $"{time:yyyy-MM-dd HH:mm:ss.fff}  {LevelName(level),-5}  ";
            string indent = new string(' ', prefix.Length);
            return prefix + (message ?? string.Empty).Replace("\r\n", "\n").Replace("\n", Environment.NewLine + indent);
        }

        public static string LevelName(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Warn: return "WARN";
                case LogLevel.Error: return "ERROR";
                default: return "INFO";
            }
        }
    }
}
