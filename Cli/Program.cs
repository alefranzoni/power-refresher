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
        private const int EXIT_ALREADY_RUNNING = 5;
        private const int EXIT_NO_DESKTOP = 6;

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
                           Add --publish <workspace> to also check the publish dialog:
                           it is opened, checked and cancelled; nothing is uploaded.
  --dump-ui [file]         With --diagnose (implied): also write Power BI's UI structure
                           (control types, ids, labels; no model data) to a text file, by
                           default in the log folder. Send it when --diagnose reports
                           MISSING elements on your Power BI version.

Logging:
  --log <path>             Append a detailed, timestamped log of the run to this file.
                           If <path> is a folder (or has no extension), each run gets its
                           own file in it, e.g. PowerRefresher_20261118_163001_Sales.log
                           Write folders without a trailing backslash: ""C:\Logs""
                           If it can't be written, the log goes to
                           %LOCALAPPDATA%\PowerRefresher\Logs instead and the run continues.
  --log-keep-days <days>   In a log folder, delete run logs older than this. Default: 30.
                           0 keeps everything. A single named log file is never pruned.
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
  5  another run on the same file was still in progress; nothing was done
  6  no interactive desktop: in Task Scheduler use ""Run only when user is logged on""

Scheduled runs:
  Power BI Desktop is driven through its UI, so the task must run in a logged-on
  user's session: ""Run only when user is logged on"". Also set ""If the task is already
  running: Do not start a new instance"". Don't use the Power BI window while it runs.
  If the file is already open, a healthy window is reused; a hung window, or one stuck
  on a dialog, is killed and reopened. A failed run closes the Power BI it opened.

Examples:
  PowerRefresherCli ""C:\Reports\Sales.pbix"" --log ""C:\Logs""
  PowerRefresherCli ""C:\Reports\Sales.pbix"" --table Orders --table Customers --retries 1
  PowerRefresherCli ""C:\Reports\Sales.pbix"" --publish ""Finance Reports"" --timeout 600
  PowerRefresherCli ""C:\Reports\Sales.pbix"" --diagnose --publish ""My workspace""";

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
                string logProblem = cli.LogPath == null ? null : OpenLogWithFallback(logger, cli, started);

                FileSnapshot before = FileSnapshot.Of(cli.Options.FilePath);
                WriteHeader(logger, cli, args, before);
                if (logProblem != null) logger.Write(LogLevel.Warn, logProblem);
                Stopwatch runClock = Stopwatch.StartNew();

                int deleted = logger.PruneOldLogs(cli.LogKeepDays);
                if (deleted > 0) logger.Write(LogLevel.Debug, $"Deleted {deleted} run log(s) older than {cli.LogKeepDays} days from {logger.LogFolder}");

                int exitCode = CheckSession(logger);
                if (exitCode == EXIT_OK)
                {
                    using (RunLock runLock = RunLock.TryAcquire(cli.Options.FilePath, logger))
                    {
                        exitCode = runLock == null ? EXIT_ALREADY_RUNNING : Execute(cli, logger, runClock);
                    }
                }

                WriteFooter(logger, cli, exitCode, runClock.Elapsed, before);
                return exitCode;
            }
        }

        /// <summary>Folder used when the requested log location can't be written: always local, writable and never synced.</summary>
        private static string FallbackLogFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerRefresher", "Logs");

        /// <summary>
        /// Opens the requested log; if it can't be written (locked-down folders, redirected Documents, bad path), logs to
        /// <see cref="FallbackLogFolder"/> instead so a scheduled refresh still runs and is still recorded.
        /// Returns a warning to put in the log, or null if the requested location worked.
        /// </summary>
        private static string OpenLogWithFallback(RunLogger logger, CliArgs cli, DateTime started)
        {
            string reason;
            try
            {
                logger.OpenFile(cli.LogPath, cli.Options.FilePath, started);
                return null;
            }
            catch (Exception e) when (IsLogFileError(e))
            {
                reason = e.Message;
            }

            try
            {
                logger.OpenFile(FallbackLogFolder, cli.Options.FilePath, started);
                return $"Can't write the log to '{cli.LogPath}' ({reason}); logging to {logger.FilePath} instead.";
            }
            catch (Exception e) when (IsLogFileError(e))
            {
                logger.Write(LogLevel.Warn, $"Can't write the log to '{cli.LogPath}' ({reason}) or to {FallbackLogFolder} ({e.Message}); continuing with console output only.");
                return null;
            }
        }

        private static bool IsLogFileError(Exception e) =>
            e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException || e is System.Security.SecurityException;

        /// <summary>Fails fast where Power BI can't be automated; warns about session states known to cause trouble.</summary>
        private static int CheckSession(RunLogger logger)
        {
            if (!SessionInfo.HasInteractiveDesktop)
            {
                logger.Write(LogLevel.Error,
                    $"No interactive desktop (session {SessionInfo.SessionId}). Power BI Desktop can only be automated in a logged-on user's session.\n" +
                    "In Task Scheduler, set the task to \"Run only when user is logged on\" (not \"Run whether user is logged on or not\").");
                return EXIT_NO_DESKTOP;
            }

            logger.Write(LogLevel.Debug, $"Windows session: {SessionInfo.Describe()}");
            if (SessionInfo.IsLocked())
            {
                logger.Write(LogLevel.Warn, "The workstation is locked. Clicks through UI Automation normally still work, but keyboard fallbacks don't. " +
                    "If runs fail only while locked, keep the session unlocked (for example on a dedicated machine).");
            }
            if (SessionInfo.IsRemote && SessionInfo.ConnectionState() == "disconnected")
            {
                logger.Write(LogLevel.Warn, "This is a disconnected Remote Desktop session; Windows may stop drawing windows, and Power BI's UI can vanish from automation. " +
                    "Prefer a console session, or keep the Remote Desktop window connected (minimized is fine).");
            }
            return EXIT_OK;
        }

        /// <summary>
        /// One run per .pbix at a time. A second scheduled run starting while the first is still refreshing would
        /// drive the same Power BI window; it exits with code 5 instead.
        /// </summary>
        private sealed class RunLock : IDisposable
        {
            private readonly System.Threading.Mutex mutex;

            private RunLock(System.Threading.Mutex mutex) { this.mutex = mutex; }

            public static RunLock TryAcquire(string pbixPath, RunLogger logger)
            {
                string name = @"Local\PowerRefresher_" + Hash(Path.GetFullPath(pbixPath).ToUpperInvariant());
                System.Threading.Mutex mutex = new System.Threading.Mutex(false, name);
                try
                {
                    if (mutex.WaitOne(0)) return new RunLock(mutex);
                }
                catch (System.Threading.AbandonedMutexException)
                {
                    //A previous run died without releasing it; we now own it
                    logger.Write(LogLevel.Warn, "A previous run on this file ended abnormally (it was killed or crashed); continuing");
                    return new RunLock(mutex);
                }
                mutex.Dispose();
                logger.Write(LogLevel.Error, "Another PowerRefresherCli run is already working on this file; not starting a second one. " +
                    "If this happens on every scheduled run, the previous run may be stuck: check its log.");
                return null;
            }

            private static string Hash(string text)
            {
                using (System.Security.Cryptography.SHA1 sha = System.Security.Cryptography.SHA1.Create())
                {
                    return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty);
                }
            }

            public void Dispose()
            {
                try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
                mutex.Dispose();
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
                    bool retryable;
                    try
                    {
                        //Outcomes returned as codes (success, bad table names, --diagnose results) are final: no retry
                        return RunOnce(cli, refresher, logger);
                    }
                    catch (Exception e) when (watchdog.Tripped)
                    {
                        logger.Write(LogLevel.Debug, $"Automation stopped after Power BI was killed: {e.GetType().Name}: {e.Message}");
                        exitCode = EXIT_HUNG;
                        retryable = watchdog.Reason != HangReason.MaxRuntime; //Retrying would only run longer
                    }
                    catch (PowerBIUnresponsiveException e)
                    {
                        //Watchdog hadn't tripped yet, but UI Automation itself timed out: same situation
                        if (!PbiRefresher.WasLogged(e)) logger.Write(LogLevel.Error, e.Message);
                        exitCode = EXIT_HUNG;
                        retryable = true;
                    }
                    catch (RefreshFailedException e)
                    {
                        //Data or credential problem in Power BI: retrying won't fix it. The error text is in the log.
                        logger.Write(LogLevel.Error, e.Message);
                        exitCode = EXIT_REFRESH_FAILED;
                        retryable = false;
                    }
                    catch (AutomationException e)
                    {
                        if (!PbiRefresher.WasLogged(e)) logger.Write(LogLevel.Error, e.Message);
                        exitCode = EXIT_AUTOMATION_ERROR;
                        retryable = true;
                    }
                    catch (Exception e)
                    {
                        //Unexpected: always log the full exception (type and stack) for bug reports
                        logger.Write(LogLevel.Error, $"Unexpected error: {e}");
                        exitCode = EXIT_AUTOMATION_ERROR;
                        retryable = true;
                    }

                    if (retryable && attempt < attempts)
                    {
                        refresher.KillPowerBI("retrying from a clean start");
                        logger.Write(LogLevel.Warn, $"Attempt {attempt} failed (exit code {exitCode}); retrying");
                        continue;
                    }

                    CleanUpAfterFailure(cli, refresher, logger);
                    return exitCode;
                }
            }
            return exitCode;
        }

        /// <summary>
        /// A failed run must not leave its Power BI window behind: the next scheduled run would find it stuck on an
        /// error dialog. Kills the instance this run opened (or restarted); windows it merely attached to are left
        /// alone, and the next run's health check restarts them if they are stuck. --keep-open keeps it for inspection.
        /// </summary>
        private static void CleanUpAfterFailure(CliArgs cli, PbiRefresher refresher, RunLogger logger)
        {
            if (!refresher.OpenedFile || refresher.ProcessId == 0) return;
            if (!cli.Options.CloseFile)
            {
                logger.Write(LogLevel.Info, $"Leaving Power BI (pid {refresher.ProcessId}) open because of --keep-open");
                return;
            }
            refresher.KillPowerBI("cleaning up after the failed run so the next run starts clean");
        }

        /// <summary>One attempt. Returns an exit code for outcomes that are not exceptions.</summary>
        private static int RunOnce(CliArgs cli, PbiRefresher refresher, RunLogger logger)
        {
            if (cli.Diagnose)
            {
                //Only the window is needed, so this still reports (and dumps) when reading the table list would fail
                refresher.OpenWindow();
                IReadOnlyList<DiagnosticResult> results = refresher.Diagnose();
                PrintDiagnosis(results);
                if (cli.DumpUi) DumpUi(cli, refresher, logger);
                CloseIfOpenedByUs(refresher, logger);
                return results.Any(r => r.Status == DiagnosticStatus.Missing) ? EXIT_AUTOMATION_ERROR : EXIT_OK;
            }

            IReadOnlyList<string> tables = refresher.Open();

            if (cli.ListTables)
            {
                foreach (string table in tables) Console.WriteLine(table);
                CloseIfOpenedByUs(refresher, logger);
                return EXIT_OK;
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

        private static void DumpUi(CliArgs cli, PbiRefresher refresher, RunLogger logger)
        {
            string folder = logger.LogFolder ?? (logger.FilePath != null ? Path.GetDirectoryName(logger.FilePath) : FallbackLogFolder);
            string path = cli.DumpUiPath ??
                Path.Combine(folder, $"PowerRefresher_{DateTime.Now:yyyyMMdd_HHmmss}_{Path.GetFileNameWithoutExtension(cli.Options.FilePath)}_ui.txt");
            try
            {
                string fullPath = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                refresher.DumpUi(fullPath);
                Console.WriteLine($"  UI dump written to {fullPath}");
                Console.WriteLine("  It lists Power BI's controls and their labels (no model data). Labels can include visible");
                Console.WriteLine("  report text such as titles and table names: skim it before sharing.");
            }
            catch (Exception e) when (IsLogFileError(e))
            {
                logger.Write(LogLevel.Error, $"Could not write the UI dump to '{path}': {e.Message}");
            }
        }

        private static void CloseIfOpenedByUs(PbiRefresher refresher, RunLogger logger)
        {
            if (!refresher.OpenedFile || !refresher.Options.CloseFile) return;
            try { refresher.Close(); }
            catch (AutomationException e)
            {
                //Probably a prompt (e.g. a first-run "what's new" popup); don't leave an instance behind that we started
                logger.Write(LogLevel.Warn, e.Message);
                refresher.KillPowerBI("did not close");
            }
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
            string mode = cli.Diagnose ? (o.Publish ? $"diagnose, including publish dialog for workspace '{o.Workspace}'" : "diagnose") : cli.ListTables ? "list tables"
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
                case EXIT_ALREADY_RUNNING: result = "SKIPPED: another run on this file is in progress"; break;
                case EXIT_NO_DESKTOP: result = "FAILED: no interactive desktop (check the task's \"Run only when user is logged on\" setting)"; break;
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
            public bool Help, Quiet, Verbose, ListTables, Diagnose, DumpUi;
            public string LogPath, DumpUiPath;
            public TimeSpan HangTimeout = TimeSpan.FromSeconds(300);
            public TimeSpan MaxRuntime = TimeSpan.FromMinutes(360);
            public int Retries;
            public int LogKeepDays = 30;
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
                    case "--dump-ui":
                        //Optional path: "--dump-ui" alone writes next to the log
                        cli.Diagnose = cli.DumpUi = true;
                        if (inlineValue != null || (i + 1 < args.Length && !args[i + 1].StartsWith("-") && !args[i + 1].EndsWith(".pbix", StringComparison.OrdinalIgnoreCase)))
                        {
                            TakeValue(out v);
                            cli.DumpUiPath = v;
                        }
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
                    case "--log-keep-days":
                        if (!TakeInt(out n, 0)) { error = "--log-keep-days needs a number of days (0 keeps everything)."; return false; }
                        cli.LogKeepDays = n;
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
            if (cli.ListTables && (tables.Count > 0 || cli.Options.Publish)) { error = "--list-tables can't be combined with --table or --publish."; return false; }
            if (cli.Diagnose && tables.Count > 0) { error = "--diagnose can't be combined with --table."; return false; }

            cli.Options.Tables = tables;
            return true;
        }

        #endregion
    }
}
