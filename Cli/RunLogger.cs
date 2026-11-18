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
        /// Opens the log file for appending. If <paramref name="path"/> is an existing folder or ends with a slash,
        /// a new file named after the run is created inside it.
        /// </summary>
        public void OpenFile(string path, string pbixPath, DateTime started)
        {
            string fullPath = Path.GetFullPath(path);
            bool isFolder = Directory.Exists(fullPath) || path.EndsWith("\\") || path.EndsWith("/");
            if (isFolder)
            {
                fullPath = Path.Combine(fullPath, $"PowerRefresher_{started:yyyyMMdd_HHmmss}_{Path.GetFileNameWithoutExtension(pbixPath)}.log");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            FileStream stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            file = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            FilePath = fullPath;
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
