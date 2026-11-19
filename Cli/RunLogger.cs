using System;
using System.IO;
using System.Text;
using PowerRefresher.Engine;

namespace PowerRefresher.Cli
{
    /// <summary>
    /// Writes timestamped log lines to the console and, optionally, a log file. Thread-safe: the hang watchdog
    /// logs from its own thread. The file gets every level including Debug and is flushed after each line, so it is
    /// complete even if the process is killed.
    /// </summary>
    internal sealed class RunLogger : IDisposable
    {
        private readonly object sync = new object();
        private readonly LogLevel consoleLevel;
        private StreamWriter file;

        public RunLogger(LogLevel consoleLevel)
        {
            this.consoleLevel = consoleLevel;
        }

        /// <summary>Full path of the log file, or null when logging to the console only.</summary>
        public string FilePath { get; private set; }

        /// <summary>
        /// Opens the log file for appending. If <paramref name="path"/> is an existing folder, ends with a slash, or has
        /// no file extension, it is treated as a folder and a new file named after the run is created inside it.
        /// (A trailing backslash before a closing quote, as in "C:\Logs\", escapes the quote in cmd and batch files,
        /// so "C:\Logs" is the safe way to name a folder.)
        /// </summary>
        public void OpenFile(string path, string pbixPath, DateTime started)
        {
            string fullPath = Path.GetFullPath(path);
            bool isFolder = Directory.Exists(fullPath) || path.EndsWith("\\") || path.EndsWith("/") || Path.GetExtension(fullPath).Length == 0;
            if (isFolder)
            {
                fullPath = Path.Combine(fullPath, $"PowerRefresher_{started:yyyyMMdd_HHmmss}_{Path.GetFileNameWithoutExtension(pbixPath)}.log");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            FileStream stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            file = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            FilePath = fullPath;
            LogFolder = isFolder ? Path.GetDirectoryName(fullPath) : null;
        }

        /// <summary>The folder holding one log file per run, or null when logging to a single file.</summary>
        public string LogFolder { get; private set; }

        /// <summary>
        /// Deletes this tool's per-run log files (PowerRefresher_*.log) older than <paramref name="days"/> from the
        /// log folder. Only applies in folder mode; a single named log file is never touched. Returns how many were deleted.
        /// </summary>
        public int PruneOldLogs(int days)
        {
            if (LogFolder == null || days <= 0) return 0;
            DateTime cutoff = DateTime.Now.AddDays(-days);
            int deleted = 0;
            foreach (string path in Directory.GetFiles(LogFolder, "PowerRefresher_*.log"))
            {
                if (string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (File.GetLastWriteTime(path) >= cutoff) continue;
                    File.Delete(path);
                    deleted++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return deleted;
        }

        public void Write(LogLevel level, string message)
        {
            string line = LogFormat.Line(DateTime.Now, level, message);
            lock (sync)
            {
                file?.WriteLine(line);
                if (level >= consoleLevel)
                {
                    TextWriter console = level >= LogLevel.Warn ? Console.Error : Console.Out;
                    console.WriteLine(line);
                }
            }
        }

        /// <summary>Writes raw lines (no timestamp) to the log file only, for section separators.</summary>
        public void WriteFileOnly(string text)
        {
            lock (sync)
            {
                file?.WriteLine(text);
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                file?.Dispose();
                file = null;
            }
        }
    }
}
