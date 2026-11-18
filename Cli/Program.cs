using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PowerRefresher.Engine;

namespace PowerRefresher.Cli
{
    internal static class Program
    {
        private const int EXIT_OK = 0;
        private const int EXIT_USAGE = 1;
        private const int EXIT_REFRESH_FAILED = 2;
        private const int EXIT_AUTOMATION_ERROR = 3;

        private const string USAGE = @"PowerRefresherCli - refresh, publish and save a Power BI Desktop file unattended.

Usage:
  PowerRefresherCli <file.pbix> [options]

Options:
  --table <name>           Refresh only this table. Repeat for several tables.
                           Default: refresh the whole model (ribbon Refresh).
  --publish <workspace>    Publish to this workspace after refreshing.
  --timeout <seconds>      How long to wait for Power BI to open the file. Default: 300.
  --script-visuals <mode>  What to do if Power BI asks to enable R/Python visuals:
                           abort (default) | enable | disable
  --keep-open              Leave the file open in Power BI when done (default: close it).
  --lang <en|es>           Power BI Desktop UI language. Default: en.
  --list-tables            Open the file, print its table names one per line, and exit.
  -q, --quiet              Print errors only.
  -h, --help               Show this help.

Options also accept --name=value.

Exit codes:
  0  success
  1  bad arguments
  2  Power BI reported that the refresh failed
  3  automation error (timeout, Power BI UI element not found, publish failed)

Notes:
  Power BI Desktop is driven through its UI, so this needs a logged-in, unlocked
  Windows session. Don't interact with the Power BI window while it runs.

Examples:
  PowerRefresherCli ""C:\Reports\Sales.pbix""
  PowerRefresherCli ""C:\Reports\Sales.pbix"" --table Orders --table Customers
  PowerRefresherCli ""C:\Reports\Sales.pbix"" --publish ""Finance Reports"" --timeout 600";

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

            PbiRefresher refresher = new PbiRefresher(cli.Options);
            if (!cli.Quiet && !cli.ListTables) refresher.Log += Console.Write;

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                IReadOnlyList<string> tables = refresher.Open();

                if (cli.ListTables)
                {
                    foreach (string table in tables) Console.WriteLine(table);
                    if (cli.Options.CloseFile && refresher.OpenedFile) refresher.Close();
                    return EXIT_OK;
                }

                try
                {
                    refresher.ValidateTables();
                }
                catch (AutomationException e)
                {
                    //Bad input, nothing changed yet: don't leave a window behind that we opened
                    Console.Error.WriteLine($"\n\nerror: {e.Message}");
                    if (refresher.OpenedFile)
                    {
                        try { refresher.Close(); }
                        catch (AutomationException closeError) { Console.Error.WriteLine($"\nwarning: {closeError.Message}"); }
                    }
                    return EXIT_USAGE;
                }

                refresher.Run();

                if (!cli.Quiet) Console.WriteLine($"\n\nOK: {Path.GetFileName(cli.Options.FilePath)} refreshed in {FormatElapsed(sw.Elapsed)}.");
                return EXIT_OK;
            }
            catch (RefreshFailedException e)
            {
                Console.Error.WriteLine($"\n\nREFRESH FAILED after {FormatElapsed(sw.Elapsed)}: {e.Message}");
                return EXIT_REFRESH_FAILED;
            }
            catch (AutomationException e)
            {
                Console.Error.WriteLine($"\n\nERROR after {FormatElapsed(sw.Elapsed)}: {e.Message}");
                return EXIT_AUTOMATION_ERROR;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"\n\nUNEXPECTED ERROR after {FormatElapsed(sw.Elapsed)}: {e}");
                return EXIT_AUTOMATION_ERROR;
            }
        }

        private static string FormatElapsed(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s"
            : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds}s"
            : $"{t.Seconds}s";

        private sealed class CliArgs
        {
            public RefreshOptions Options = new RefreshOptions();
            public bool Help, Quiet, ListTables;
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

                string v;
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
                    case "--list-tables":
                        cli.ListTables = true;
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
                    case "--timeout":
                        if (!TakeValue(out v) || !int.TryParse(v, out int seconds) || seconds <= 0) { error = "--timeout needs a positive number of seconds."; return false; }
                        cli.Options.OpenTimeoutSeconds = seconds;
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
            if (cli.ListTables && (tables.Count > 0 || cli.Options.Publish)) { error = "--list-tables can't be combined with --table or --publish."; return false; }

            cli.Options.Tables = tables;
            return true;
        }
    }
}
