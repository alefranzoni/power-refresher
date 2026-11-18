using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using PowerRefresher.Engine;

namespace PowerRefresher.Cli
{
    internal static class Program
    {
        private const int EXIT_OK = 0;
        private const int EXIT_USAGE = 1;
        private const int EXIT_REFRESH_FAILED = 2;
        private const int EXIT_AUTOMATION_ERROR = 3;
        private const int EXIT_HUNG = 4;

        private const string USAGE = @"PowerRefresherCli - refresh, publish and save a Power BI Desktop file unattended.

Usage:
  PowerRefresherCli <file.pbix> [options]

What to do:
  --table <name>           Refresh only this table. Repeat for several tables.
                           Default: refresh the whole model (ribbon Refresh).
  --publish <workspace>    Publish to this workspace after refreshing.
  --list-tables            Open the file, print its table names one per line, and exit.
  --diagnose               Open the file and check every Power BI UI element this tool
                           relies on, without refreshing or saving. Run this after a
                           Power BI update, or when a run fails with a timeout.

Logging:
  --log <path>             Append a detailed, timestamped log of the run to this file.
                           If <path> is a folder (or ends with \), each run gets its own
                           file in it, e.g. PowerRefresher_20261118_163001_Sales.log
  -v, --verbose            Also show debug detail on the console (the log file always has it).
  -q, --quiet              Show errors only on the console.

Hangs and limits:
  --timeout <seconds>      How long to wait for Power BI to open the file. Default: 300.
  --hang-timeout <seconds> Kill Power BI if its window stops responding this long.
                           Default: 300. 0 disables hang detection.
  --max-runtime <minutes>  Kill Power BI and stop if the whole run takes longer than this.
                           Default: 360. 0 disables the limit.
  --retries <n>            After a hang or automation error, kill Power BI, reopen the file
                           and try again, up to n more times. Default: 0.

Other:
  --script-visuals <mode>  What to do if Power BI asks to enable R/Python visuals:
                           abort (default) | enable | disable
  --keep-open              Leave the file open in Power BI when done (default: close it).
  --lang <en|es>           Power BI Desktop UI language. Default: en.
  -h, --help               Show this help.

Options also accept --name=value.

Exit codes:
  0  success
  1  bad arguments (including a table name that isn't in the model)
  2  Power BI reported that the refresh failed
  3  automation error (timeout, Power BI UI element not found, publish failed);
     for --diagnose: at least one element could not be found
  4  Power BI hung or the run exceeded --max-runtime; Power BI was killed

Notes:
  Power BI Desktop is driven through its UI, so this needs a logged-in, unlocked
  Windows session. Don't interact with the Power BI window while it runs.

Examples:
  PowerRefresherCli ""C:\Reports\Sales.pbix"" --log ""C:\Logs\""
  PowerRefresherCli ""C:\Reports\Sales.pbix"" --table Orders --table Customers --retries 1
  PowerRefresherCli ""C:\Reports\Sales.pbix"" --publish ""Finance Reports"" --timeout 600
  PowerRefresherCli ""C:\Reports\Sales.pbix"" --diagnose";

        [STAThread]
        private static int Main(string[] args)
        {
            if (!TryParse(args, out CliArgs cli, out string error))
            {
                Console.Error.WriteLine($"error: {error}");
                Console.Error.WriteLine("Run with --help for usage.");
                return EXIT_USAGE;
            }
            if (cli.Help)
            {
                Console.WriteLine(USAGE);
                return EXIT_OK;
            }

            DateTime started = DateTime.Now;
            LogLevel consoleLevel = cli.Quiet || cli.ListTables ? LogLevel.Error : cli.Verbose ? LogLevel.Debug : LogLevel.Info;
            using (RunLogger logger = new RunLogger(consoleLevel))
            {
                if (cli.LogPath != null)
                {
                    try
                    {
                        logger.OpenFile(cli.LogPath, cli.Options.FilePath, started);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
                    {
                        Console.Error.WriteLine($"error: can't write log file '{cli.LogPath}': {e.Message}");
                        return EXIT_USAGE;
                    }
                }

                FileSnapshot before = FileSnapshot.Of(cli.Options.FilePath);
                WriteHeader(logger, cli, args, before);

                Stopwatch runClock = Stopwatch.StartNew();
                int exitCode = Execute(cli, logger, runClock);

                WriteFooter(logger, cli, exitCode, runClock.Elapsed, before);
                return exitCode;
            }
        }

        /// <summary>Runs the requested operation with the hang watchdog, retrying if allowed.</summary>
        private static int Execute(CliArgs cli, RunLogger logger, Stopwatch runClock)
        {
            int attempts = cli.Retries + 1;
            int exitCode = EXIT_AUTOMATION_ERROR;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                if (attempts > 1) logger.Write(LogLevel.Info, $"Attempt {attempt} of {attempts}");

                PbiRefresher refresher = new PbiRefresher(cli.Options);
                refresher.Log += logger.Write;

                using (HangWatchdog watchdog = new HangWatchdog(refresher, cli.HangTimeout, cli.MaxRuntime, runClock, logger.Write,
                    onStuck: () =>
                    {
                        logger.Write(LogLevel.Error, $"Exiting with code {EXIT_HUNG}");
                        logger.Dispose();
                        Environment.Exit(EXIT_HUNG);
                    }))
                {
                    try
                    {
                        //Outcomes returned as codes (success, bad table names, --diagnose results) are final: no retry
                        return RunOnce(cli, refresher, logger);
                    }
                    catch (Exception e) when (watchdog.Tripped)
                    {
                        logger.Write(LogLevel.Debug, $"Automation stopped after Power BI was killed: {e.GetType().Name}: {e.Message}");
                        exitCode = EXIT_HUNG;
                        if (watchdog.Reason == HangReason.MaxRuntime) return exitCode; //Retrying would only run longer
                    }
                    catch (PowerBIUnresponsiveException e)
                    {
                        //Watchdog hadn't tripped yet, but UI Automation itself timed out: same situation
                        if (!PbiRefresher.WasLogged(e)) logger.Write(LogLevel.Error, e.Message);
                        exitCode = EXIT_HUNG;
                    }
                    catch (RefreshFailedException e)
                    {
                        //Data or credential problem in Power BI: retrying won't fix it. Leave the window open so the error can be read.
                        logger.Write(LogLevel.Error, e.Message);
                        return EXIT_REFRESH_FAILED;
                    }
                    catch (AutomationException e)
                    {
                        if (!PbiRefresher.WasLogged(e)) logger.Write(LogLevel.Error, e.Message);
                        exitCode = EXIT_AUTOMATION_ERROR;
                    }
                    catch (Exception e)
                    {
                        //Unexpected: always log the full exception (type and stack) for bug reports
                        logger.Write(LogLevel.Error, $"Unexpected error: {e}");
                        exitCode = EXIT_AUTOMATION_ERROR;
                    }

                    if (attempt < attempts)
                    {
                        refresher.KillPowerBI("retrying from a clean start");
                        logger.Write(LogLevel.Warn, $"Attempt {attempt} failed (exit code {exitCode}); retrying");
                    }
                }
            }
            return exitCode;
        }

        /// <summary>One attempt. Returns an exit code for outcomes that are not exceptions.</summary>
        private static int RunOnce(CliArgs cli, PbiRefresher refresher, RunLogger logger)
        {
            IReadOnlyList<string> tables = refresher.Open();

            if (cli.ListTables)
            {
                foreach (string table in tables) Console.WriteLine(table);
                CloseIfOpenedByUs(refresher, logger);
                return EXIT_OK;
            }

            if (cli.Diagnose)
            {
                IReadOnlyList<DiagnosticResult> results = refresher.Diagnose();
                PrintDiagnosis(results);
                CloseIfOpenedByUs(refresher, logger);
                return results.Any(r => r.Status == DiagnosticStatus.Missing) ? EXIT_AUTOMATION_ERROR : EXIT_OK;
            }

            try
            {
                refresher.ValidateTables();
            }
            catch (AutomationException e)
            {
                //Bad input, nothing changed yet: report it as a usage error and don't leave a window behind that we opened
                logger.Write(LogLevel.Error, e.Message);
                CloseIfOpenedByUs(refresher, logger);
                return EXIT_USAGE;
            }

            refresher.Run();
            return EXIT_OK;
        }

        private static void CloseIfOpenedByUs(PbiRefresher refresher, RunLogger logger)
        {
            if (!refresher.OpenedFile || !refresher.Options.CloseFile) return;
            try { refresher.Close(); }
            catch (AutomationException e) { logger.Write(LogLevel.Warn, e.Message); }
        }

        private static void PrintDiagnosis(IReadOnlyList<DiagnosticResult> results)
        {
            int width = results.Max(r => r.Element.Length);
            Console.WriteLine();
            foreach (DiagnosticResult r in results)
            {
                string status = r.Status == DiagnosticStatus.Ok ? "OK"
                    : r.Status == DiagnosticStatus.Fallback ? "FALLBACK"
                    : r.Status == DiagnosticStatus.Missing ? "MISSING"
                    : "-";
                Console.WriteLine($"  {r.Element.PadRight(width)}  {status,-8}  {r.Detail}");
            }
            Console.WriteLine();
            Console.WriteLine("  OK = found the expected way. FALLBACK = still works, but Power BI's UI changed.");
            Console.WriteLine("  MISSING = runs that need it will fail.");
        }

        #region Log header / footer

        private static void WriteHeader(RunLogger logger, CliArgs cli, string[] args, FileSnapshot before)
        {
            RefreshOptions o = cli.Options;
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            string mode = cli.Diagnose ? "diagnose" : cli.ListTables ? "list tables"
                : o.RefreshAll ? "refresh all tables" : $"refresh {o.Tables.Count} table(s): {string.Join(", ", o.Tables)}";

            logger.WriteFileOnly(string.Empty);
            logger.WriteFileOnly(new string('=', 100));
            logger.Write(LogLevel.Info, $"PowerRefresherCli {version} started: {mode}");
            logger.Write(LogLevel.Debug, $"Command line: {Environment.CommandLine}");
            logger.Write(LogLevel.Debug, $"Machine {Environment.MachineName}, user {Environment.UserDomainName}\\{Environment.UserName}, pid {Process.GetCurrentProcess().Id}, {(Environment.Is64BitProcess ? "64" : "32")}-bit");
            if (logger.FilePath != null) logger.Write(LogLevel.Debug, $"Log file: {logger.FilePath}");
            logger.Write(LogLevel.Info, $"File: {o.FilePath} ({before})");
            logger.Write(LogLevel.Debug,
                $"Options: publish={(o.Publish ? "'" + o.Workspace + "'" : "no")}, close file={(o.CloseFile ? "yes" : "no")}, " +
                $"open timeout={o.OpenTimeoutSeconds}s, hang timeout={(cli.HangTimeout > TimeSpan.Zero ? LogFormat.Duration(cli.HangTimeout) : "off")}, " +
                $"max runtime={(cli.MaxRuntime > TimeSpan.Zero ? LogFormat.Duration(cli.MaxRuntime) : "off")}, retries={cli.Retries}, " +
                $"script visuals={o.ScriptVisuals.ToString().ToLowerInvariant()}, lang={o.Language}");
        }

        private static void WriteFooter(RunLogger logger, CliArgs cli, int exitCode, TimeSpan elapsed, FileSnapshot before)
        {
            FileSnapshot after = FileSnapshot.Of(cli.Options.FilePath);
            bool saved = after.LastWrite != before.LastWrite || after.Size != before.Size;
            bool expectsSave = !cli.ListTables && !cli.Diagnose;

            if (expectsSave)
            {
                if (saved) logger.Write(LogLevel.Info, $"File after run: {after} (saved)");
                else if (exitCode == EXIT_OK) logger.Write(LogLevel.Warn, $"File after run: {after}. It was NOT modified, although the run reported success; check that Power BI saved it.");
                else logger.Write(LogLevel.Info, $"File after run: {after} (not modified)");
            }

            string result;
            switch (exitCode)
            {
                case EXIT_OK: result = "SUCCESS"; break;
                case EXIT_USAGE: result = "FAILED: bad arguments"; break;
                case EXIT_REFRESH_FAILED: result = "FAILED: Power BI reported a refresh error"; break;
                case EXIT_HUNG: result = "FAILED: Power BI hung or the run took too long; Power BI was killed"; break;
                default: result = cli.Diagnose ? "FAILED: some UI elements are missing" : "FAILED: automation error"; break;
            }
            logger.Write(exitCode == EXIT_OK ? LogLevel.Info : LogLevel.Error, $"Finished in {LogFormat.Duration(elapsed)}. Result: {result} (exit code {exitCode})");
            logger.WriteFileOnly(new string('=', 100));
        }

        private sealed class FileSnapshot
        {
            public long Size;
            public DateTime LastWrite;

            public static FileSnapshot Of(string path)
            {
                FileInfo info = new FileInfo(path);
                info.Refresh();
                return new FileSnapshot { Size = info.Exists ? info.Length : -1, LastWrite = info.Exists ? info.LastWriteTime : DateTime.MinValue };
            }

            public override string ToString() =>
                Size < 0 ? "missing" : $"{Size / 1024.0:#,0.0} KB, last modified {LastWrite:yyyy-MM-dd HH:mm:ss}";
        }

        #endregion

        #region Arguments

        private sealed class CliArgs
        {
            public RefreshOptions Options = new RefreshOptions();
            public bool Help, Quiet, Verbose, ListTables, Diagnose;
            public string LogPath;
            public TimeSpan HangTimeout = TimeSpan.FromSeconds(300);
            public TimeSpan MaxRuntime = TimeSpan.FromMinutes(360);
            public int Retries;
        }

        private static bool TryParse(string[] args, out CliArgs cli, out string error)
        {
            cli = new CliArgs();
            error = null;
            List<string> tables = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string name = arg, inlineValue = null;
                if (arg.StartsWith("--") && arg.Contains("="))
                {
                    name = arg.Substring(0, arg.IndexOf('='));
                    inlineValue = arg.Substring(arg.IndexOf('=') + 1);
                }

                //Reads the option's value from "--name=value" or the next argument
                bool TakeValue(out string value)
                {
                    if (inlineValue != null) { value = inlineValue; return true; }
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) { value = args[++i]; return true; }
                    value = null;
                    return false;
                }

                bool TakeInt(out int value, int min)
                {
                    value = 0;
                    return TakeValue(out string raw) && int.TryParse(raw, out value) && value >= min;
                }

                string v;
                int n;
                switch (name.ToLowerInvariant())
                {
                    case "-h":
                    case "--help":
                    case "/?":
                        cli.Help = true;
                        return true;
                    case "-q":
                    case "--quiet":
                        cli.Quiet = true;
                        break;
                    case "-v":
                    case "--verbose":
                        cli.Verbose = true;
                        break;
                    case "--list-tables":
                        cli.ListTables = true;
                        break;
                    case "--diagnose":
                        cli.Diagnose = true;
                        break;
                    case "--keep-open":
                        cli.Options.CloseFile = false;
                        break;
                    case "--table":
                        if (!TakeValue(out v) || string.IsNullOrWhiteSpace(v)) { error = "--table needs a table name."; return false; }
                        tables.Add(v);
                        break;
                    case "--publish":
                        if (!TakeValue(out v) || string.IsNullOrWhiteSpace(v)) { error = "--publish needs a workspace name."; return false; }
                        cli.Options.Workspace = v;
                        break;
                    case "--log":
                        if (!TakeValue(out v) || string.IsNullOrWhiteSpace(v)) { error = "--log needs a file or folder path."; return false; }
                        cli.LogPath = v;
                        break;
                    case "--timeout":
                        if (!TakeInt(out n, 1)) { error = "--timeout needs a positive number of seconds."; return false; }
                        cli.Options.OpenTimeoutSeconds = n;
                        break;
                    case "--hang-timeout":
                        if (!TakeInt(out n, 0)) { error = "--hang-timeout needs a number of seconds (0 disables it)."; return false; }
                        if (n > 0 && n < 30) { error = "--hang-timeout must be at least 30 seconds (Power BI can briefly stop responding during normal work)."; return false; }
                        cli.HangTimeout = TimeSpan.FromSeconds(n);
                        break;
                    case "--max-runtime":
                        if (!TakeInt(out n, 0)) { error = "--max-runtime needs a number of minutes (0 disables it)."; return false; }
                        cli.MaxRuntime = TimeSpan.FromMinutes(n);
                        break;
                    case "--retries":
                        if (!TakeInt(out n, 0) || n > 5) { error = "--retries needs a number from 0 to 5."; return false; }
                        cli.Retries = n;
                        break;
                    case "--script-visuals":
                        if (!TakeValue(out v) || !Enum.TryParse(v, true, out ScriptVisualsAction action) || !Enum.IsDefined(typeof(ScriptVisualsAction), action))
                        { error = "--script-visuals must be abort, enable or disable."; return false; }
                        cli.Options.ScriptVisuals = action;
                        break;
                    case "--lang":
                        if (!TakeValue(out v) || (v.ToLowerInvariant() != "en" && v.ToLowerInvariant() != "es")) { error = "--lang must be en or es."; return false; }
                        cli.Options.Language = v.ToLowerInvariant();
                        break;
                    default:
                        if (arg.StartsWith("-")) { error = $"unknown option '{arg}'."; return false; }
                        if (cli.Options.FilePath != null) { error = $"only one .pbix file can be given (got '{cli.Options.FilePath}' and '{arg}')."; return false; }
                        cli.Options.FilePath = Path.GetFullPath(arg);
                        break;
                }
            }

            if (cli.Options.FilePath == null) { error = "no .pbix file given."; return false; }
            if (!cli.Options.FilePath.EndsWith(".pbix", StringComparison.OrdinalIgnoreCase)) { error = $"'{cli.Options.FilePath}' is not a .pbix file."; return false; }
            if (!File.Exists(cli.Options.FilePath)) { error = $"file not found: {cli.Options.FilePath}"; return false; }
            if (cli.Quiet && cli.Verbose) { error = "--quiet and --verbose can't be used together."; return false; }
            if (cli.ListTables && cli.Diagnose) { error = "--list-tables and --diagnose can't be used together."; return false; }
            if ((cli.ListTables || cli.Diagnose) && (tables.Count > 0 || cli.Options.Publish))
            { error = "--list-tables and --diagnose can't be combined with --table or --publish."; return false; }

            cli.Options.Tables = tables;
            return true;
        }

        #endregion
    }
}
