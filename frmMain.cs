using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using PowerRefresher.Engine;

namespace PowerRefresher
{
    public partial class frmMain : Form
    {
        private const string CLI_EXE_NAME = "PowerRefresherCli.exe";

        private PbiRefresher refresher;

        //Legacy positional command line (kept so existing .bat scripts keep working)
        private string targetCmd, refreshModeCmd, fieldsCmd, workspaceNameCmd, pbiLangCmd;
        private bool enableScriptVisualsCmd, continueRefreshCmd, publishCmd, closeFileCmd, closeAppCmd, userArgsPassed;
        private int timeoutCmd;

        public frmMain() => InitializeComponent();

        private void frmMain_Load(object sender, EventArgs e)
        {
            userArgsPassed = false;
            string[] args = Environment.GetCommandLineArgs();

            if (args.Length == 1) return;

            if (args.Length != 12 || args[1].Contains("help") || !IsValidArgs() || !GetAndStoreArguments())
            {
                ShowMessage(Properties.Resources.helpMessage, MessageBoxIcon.Information);
                return;
            }

            userArgsPassed = true;
        }

        private void frmMain_Shown(object sender, EventArgs e)
        {
            if (userArgsPassed)
            {
                SetFormValues();
                if (GetFileData() == false) return;
                Thread.Sleep(1500);
                cmdStartRefresh.PerformClick();
            }
        }

        private void cmdSetInput_Click(object sender, EventArgs e) => BrowseInputFile();
        private void cmdStartRefresh_Click(object sender, EventArgs e)
        {
            if (!CheckRequiredFields()) return;

            Stopwatch stopWatch = Stopwatch.StartNew();
            try
            {
                ApplyFormToOptions(refresher.Options);
                refresher.Run();

                stopWatch.Stop();
                TimeSpan t = stopWatch.Elapsed;
                string timeElapsed = $"{t.Hours:D1} hours {t.Minutes:D1} minutes {t.Seconds:D1} secs";

                txtOutput.Text += $"\n\n=== REFRESH FINISHED ===\nTime elapsed: {timeElapsed}\nThanks for using PowerRefresher!\n";
                txtOutput.Text += "-\nPowerRefresher @ https://github.com/alefranzoni/power-refresher" +
                    "\nAlejandro Franzoni Gimenez @ https://alejandrofranzoni.com.ar \n";

                if (chkCloseAppOnFinish.Checked) CloseApplication();

                BringToFront();
                Activate();
            }
            catch (Exception err)
            {
                RefreshFailed(err);
            }
        }
        private void cmdGenerateScript_Click(object sender, EventArgs e)
        {
            if (!CheckRequiredFields()) return;
            try
            {
                GenerateScript();
            }
            catch (Exception err)
            {
                int errorLine = txtOutput.Lines.Length + 1;
                txtOutput.Text += "\n\n=== SCRIPT GENERATOR FAILED ===\n";
                txtOutput.Text += err.Message;
                SetOutputLineColor(errorLine, Color.Red);
                BringToFront();
                Activate();
            }
        }
        private void txtOutput_LinkClicked(object sender, LinkClickedEventArgs e) => OpenFile(e.LinkText);
        private void chkRefreshAll_CheckedChanged(object sender, EventArgs e)
        {
            chklModelFields.Enabled = !chkRefreshAll.Checked;
            lblModelFields.Enabled = !chkRefreshAll.Checked;
            if (chklModelFields.CheckedItems.Count == 0) return;
            SetModelFieldsSelectionState(false);
        }
        private void chkPublish_CheckedChanged(object sender, EventArgs e)
        {
            lblWorkspace.Visible = chkPublish.Checked;
            txtWorkspace.Visible = chkPublish.Checked;
        }
        private void chkEnableScriptVisuals_CheckedChanged(object sender, EventArgs e)
        {
            chkContinueRefresh.Enabled = !chkEnableScriptVisuals.Checked;
        }
        private void txtOutput_TextChanged(object sender, EventArgs e)
        {
            txtOutput.SelectionStart = txtOutput.TextLength;
            txtOutput.ScrollToCaret();
        }
        //Language is read from the radio buttons whenever options are built
        private void englishAppLang_CheckedChanged(object sender, EventArgs e) { }
        private void spanishAppLang_CheckedChanged(object sender, EventArgs e) { }

        private void selectAllFieldsMenuItem_Click(object sender, EventArgs e) => SetModelFieldsSelectionState(true);
        private void clearSelectionMenuItem_Click(object sender, EventArgs e) => SetModelFieldsSelectionState(false);
        private void copySelectedMenuItem_Click(object sender, EventArgs e) => SetTextToClipboard(txtOutput.SelectedText);
        private void selectAllTextMenuItem_Click(object sender, EventArgs e) => txtOutput.SelectAll();

        /// <summary>Opens/binds the file, lists its tables and handles the script visuals prompt.</summary>
        private bool GetFileData()
        {
            txtOutput.Text = null;
            cmdStartRefresh.Enabled = false;

            RefreshOptions options = new RefreshOptions();
            ApplyFormToOptions(options);
            refresher = new PbiRefresher(options);
            refresher.Log += (level, message) =>
            {
                if (level == LogLevel.Debug) return;
                txtOutput.Text += (txtOutput.TextLength > 0 ? "\n" : string.Empty) + LogFormat.Line(DateTime.Now, level, message);
            };

            try
            {
                chklModelFields.Items.Clear();
                foreach (string table in refresher.Open()) chklModelFields.Items.Add(table);

                if (refreshModeCmd == "fields") SetModelFieldsFromArgs();

                cmdStartRefresh.Enabled = true;
                BringToFront();
                Activate();
                return true;
            }
            catch (Exception e)
            {
                txtOutput.Text += "\nAn unexpected error has occurred: " + e.Message;
                SetOutputLineColor(txtOutput.Lines.Length - 1, Color.Red);
                return false;
            }
        }

        private void ApplyFormToOptions(RefreshOptions options)
        {
            options.FilePath = txtInput.Text;
            options.OpenTimeoutSeconds = (int)numericTimeout.Value;
            options.Language = englishAppLang.Checked ? "en" : "es";
            options.Tables = chkRefreshAll.Checked
                ? new List<string>()
                : chklModelFields.CheckedItems.Cast<object>().Select(o => o.ToString()).ToList();
            options.Workspace = chkPublish.Checked ? txtWorkspace.Text : null;
            options.CloseFile = chkCloseFileOnFinish.Checked;
            options.ScriptVisuals = chkEnableScriptVisuals.Checked ? ScriptVisualsAction.Enable
                : chkContinueRefresh.Checked ? ScriptVisualsAction.Disable
                : ScriptVisualsAction.Abort;
        }

        private bool GetAndStoreArguments()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i <= 11; i++)
            {
                switch (i)
                {
                    case 1:
                        if (args[i].Replace("-target=", null) == string.Empty) return false;
                        targetCmd = args[i].Replace("-target=", null);
                        break;
                    case 2:
                        if (!int.TryParse(args[i].Replace("-timeout=", null), out timeoutCmd)) return false;
                        timeoutCmd = Math.Min(timeoutCmd, 600);
                        break;
                    case 3:
                        refreshModeCmd = args[i].Replace("-refresh_mode=", null).ToLower();
                        if (refreshModeCmd != "all" && refreshModeCmd != "fields") return false;
                        break;
                    case 4:
                        fieldsCmd = args[i].Replace("-fields=", null);
                        if (refreshModeCmd == "fields" && fieldsCmd == string.Empty) return false;
                        break;
                    case 5:
                        if (!TryParseBoolArg(args[i], "-publish=", out publishCmd)) return false;
                        break;
                    case 6:
                        workspaceNameCmd = publishCmd ? args[i].Replace("-workspace=", null) : null;
                        if (publishCmd && string.IsNullOrEmpty(workspaceNameCmd)) return false;
                        break;
                    case 7:
                        if (!TryParseBoolArg(args[i], "-enable_script_visuals=", out enableScriptVisualsCmd)) return false;
                        break;
                    case 8:
                        if (!TryParseBoolArg(args[i], "-sv_force_refresh=", out continueRefreshCmd)) return false;
                        break;
                    case 9:
                        if (!TryParseBoolArg(args[i], "-closefile=", out closeFileCmd)) return false;
                        break;
                    case 10:
                        if (!TryParseBoolArg(args[i], "-closeapp=", out closeAppCmd)) return false;
                        break;
                    case 11:
                        pbiLangCmd = args[i].Replace("-pbi_lang=", null).ToLower();
                        if (pbiLangCmd != "en" && pbiLangCmd != "es") return false;
                        break;
                }
            }
            return true;
        }
        private static bool TryParseBoolArg(string arg, string prefix, out bool value)
        {
            string raw = arg.Replace(prefix, null).ToLower();
            value = raw == "true";
            return raw == "true" || raw == "false";
        }
        private bool IsValidArgs()
        {
            string[] expected = { "target", "timeout", "refresh_mode", "fields", "publish", "workspace", "enable_script_visuals", "sv_force_refresh", "closefile", "closeapp", "pbi_lang" };
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < expected.Length; i++)
            {
                if (!args[i + 1].Contains(expected[i])) return false;
            }
            return true;
        }
        private void SetModelFieldsFromArgs()
        {
            string[] fieldsFromArgs = fieldsCmd.Replace("[", null).Replace("]", null).Split(',');

            foreach (string field in fieldsFromArgs)
            {
                for (int i = 0; i < chklModelFields.Items.Count; i++)
                {
                    if (string.Equals(chklModelFields.Items[i].ToString(), field.Trim(), StringComparison.OrdinalIgnoreCase)) chklModelFields.SetItemChecked(i, true);
                }
            }

            if (chklModelFields.CheckedItems.Count == 0) throw new Exception("Model fields passed by argument was not found.");
        }
        private void SetFormValues()
        {
            txtInput.Text = targetCmd;
            englishAppLang.Checked = pbiLangCmd == "en";
            spanishAppLang.Checked = pbiLangCmd == "es";
            numericTimeout.Value = timeoutCmd;
            chkRefreshAll.Checked = refreshModeCmd == "all";
            chkEnableScriptVisuals.Checked = enableScriptVisualsCmd;
            chkContinueRefresh.Checked = continueRefreshCmd;
            chkPublish.Checked = publishCmd;
            if (publishCmd) txtWorkspace.Text = workspaceNameCmd;
            chkCloseFileOnFinish.Checked = closeFileCmd;
            chkCloseAppOnFinish.Checked = closeAppCmd;
        }

        private void SetOutputLineColor(int line, Color color)
        {
            txtOutput.Select(txtOutput.GetFirstCharIndexFromLine(Math.Max(0, Math.Min(line, txtOutput.Lines.Length - 1))), txtOutput.TextLength);
            txtOutput.SelectionColor = color;
        }
        private void SetModelFieldsSelectionState(bool checkStatus)
        {
            for (int i = 0; i < chklModelFields.Items.Count; i++) chklModelFields.SetItemChecked(i, checkStatus);
        }
        private void SetTextToClipboard(string text)
        {
            if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
        }

        /// <summary>Builds a PowerRefresherCli command line equivalent to the current form settings.</summary>
        //Defaults written into generated scripts; the .bat explains each one so it can be edited
        private const int SCRIPT_HANG_TIMEOUT_SECONDS = 300;
        private const int SCRIPT_MAX_RUNTIME_MINUTES = 360;
        private const int SCRIPT_RETRIES = 1;

        private static string ScriptLogFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PowerRefresher", "Logs");

        /// <summary>
        /// Builds a PowerRefresherCli command equivalent to the current form settings, plus logging and hang
        /// handling for unattended runs. Copies it to the clipboard and optionally saves a commented .bat.
        /// </summary>
        private void GenerateScript()
        {
            string cliPath = Path.Combine(AppContext.BaseDirectory, CLI_EXE_NAME);
            List<string> parts = new List<string> { Quote(cliPath), Quote(txtInput.Text), $"--timeout {numericTimeout.Value}" };
            if (!chkRefreshAll.Checked)
            {
                foreach (object table in chklModelFields.CheckedItems) parts.Add($"--table {Quote(table.ToString())}");
            }
            if (chkPublish.Checked) parts.Add($"--publish {Quote(txtWorkspace.Text)}");
            if (chkEnableScriptVisuals.Checked) parts.Add("--script-visuals enable");
            else if (chkContinueRefresh.Checked) parts.Add("--script-visuals disable");
            if (!chkCloseFileOnFinish.Checked) parts.Add("--keep-open");
            if (spanishAppLang.Checked) parts.Add("--lang es");
            parts.Add($"--log {Quote(ScriptLogFolder)}");
            parts.Add($"--hang-timeout {SCRIPT_HANG_TIMEOUT_SECONDS}");
            parts.Add($"--max-runtime {SCRIPT_MAX_RUNTIME_MINUTES}");
            parts.Add($"--retries {SCRIPT_RETRIES}");

            SetTextToClipboard(string.Join(" ", parts));
            txtOutput.Text += "\n[INFO] Script was generated successfully and copied to clipboard.";
            if (!File.Exists(cliPath)) txtOutput.Text += $"\n[WARN] {CLI_EXE_NAME} was not found next to this program; copy it there before running the script.";

            if (MessageBox.Show("Script was generated successfully and copied to clipboard!\n\nDo you want to save it into a batch file?",
                "PowerRefresher: Script Generator", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                CreateBatchFile(parts);
            }
        }

        /// <summary>Quotes a command-line value. Trailing backslashes are dropped: "C:\Logs\" would escape the closing quote.</summary>
        private static string Quote(string value) => "\"" + (value ?? string.Empty).TrimEnd('\\') + "\"";

        private void CreateBatchFile(List<string> parts)
        {
            string pbixName = Path.GetFileNameWithoutExtension(txtInput.Text);
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Save refresh script",
                Filter = "Batch files (*.bat)|*.bat",
                FileName = $"Refresh {pbixName}.bat",
                InitialDirectory = Path.GetDirectoryName(txtInput.Text),
            };
            if (dialog.ShowDialog() != DialogResult.OK) return;

            string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            using (StreamWriter w = new StreamWriter(dialog.FileName, false, new System.Text.UTF8Encoding(false)))
            {
                w.WriteLine("@echo off");
                w.WriteLine($"REM Refreshes {Path.GetFileName(txtInput.Text)} with PowerRefresherCli.");
                w.WriteLine($"REM Generated by PowerRefresher {version} on {DateTime.Now:yyyy-MM-dd HH:mm}. Edit the options below as needed.");
                w.WriteLine("REM");
                w.WriteLine("REM   --timeout       seconds to wait for Power BI to open the file");
                w.WriteLine("REM   --log           folder for the run logs (one timestamped file per run)");
                w.WriteLine("REM   --hang-timeout  seconds Power BI may stop responding before it is killed (0 = off)");
                w.WriteLine("REM   --max-runtime   minutes the whole run may take before Power BI is killed (0 = off)");
                w.WriteLine("REM   --retries       extra attempts after a hang or automation error (kills and reopens Power BI)");
                w.WriteLine("REM   Run PowerRefresherCli --help for all options.");
                w.WriteLine("REM");
                w.WriteLine("REM Exit codes: 0 ok, 1 bad arguments, 2 refresh failed, 3 automation error,");
                w.WriteLine("REM             4 Power BI hung or ran too long (it was killed),");
                w.WriteLine("REM             5 skipped: a previous run on this file was still going,");
                w.WriteLine("REM             6 no desktop: the task must run only when the user is logged on");
                w.WriteLine("REM");
                w.WriteLine("REM To schedule this yourself in Task Scheduler (Create Task, not Basic Task):");
                w.WriteLine("REM   General:  \"Run only when user is logged on\" (Power BI needs your desktop)");
                w.WriteLine("REM   Actions:  Start a program = this .bat file");
                w.WriteLine("REM   Settings: \"Run task as soon as possible after a scheduled start is missed\"");
                w.WriteLine($"REM             \"Stop the task if it runs longer than\" {SCRIPT_MAX_RUNTIME_MINUTES / 60 + 1} hours");
                w.WriteLine("REM             \"If the task is already running\" = Do not start a new instance");
                w.WriteLine();
                //One option per line; "%" must be doubled inside batch files
                w.WriteLine(string.Join(" ^" + Environment.NewLine + "  ", parts.Select(p => p.Replace("%", "%%"))));
                w.WriteLine("exit /b %ERRORLEVEL%");
            }

            txtOutput.Text += $"\n[INFO] Script saved to {dialog.FileName}";

            if (MessageBox.Show($"Script saved to:\n{dialog.FileName}\n\nLogs will be written to:\n{ScriptLogFolder}\n\n" +
                    "Create a Windows scheduled task to run it automatically?",
                    "PowerRefresher: Script Generator", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            //The task's hard stop sits an hour above the script's own --max-runtime, so the script can stop cleanly and log why first
            TimeSpan limit = TimeSpan.FromMinutes(SCRIPT_MAX_RUNTIME_MINUTES + 60);
            using (frmSchedule schedule = new frmSchedule(dialog.FileName, pbixName, limit))
            {
                if (schedule.ShowDialog(this) != DialogResult.OK) return;
                string next = ScheduledTask.NextRunTime(schedule.CreatedTaskName);
                txtOutput.Text += $"\n[INFO] Scheduled task '{schedule.CreatedTaskName}' created" + (next != null ? $"; next run {next}" : string.Empty);
                ShowMessage($"Scheduled task '{schedule.CreatedTaskName}' created." + (next != null ? $"\nNext run: {next}" : string.Empty) +
                    "\n\nTo check it or run it now, open Task Scheduler (Task Scheduler Library).", MessageBoxIcon.Information, "Script Generator");
            }
        }
        private void BrowseInputFile()
        {
            var fd = new OpenFileDialog
            {
                Title = "Choose PBIX file",
                Filter = "PowerBI FIles |*.pbix"
            };

            if (fd.ShowDialog() == DialogResult.OK)
            {
                txtInput.Text = fd.FileName;
                GetFileData();
            }
            else
            {
                txtInput.Text = null;
            }
        }
        private void CloseApplication()
        {
            CreateUpdateFileLog();
            Close();
        }
        private static void OpenFile(string path)
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        private void CreateUpdateFileLog()
        {
            string filename = Path.Combine(Path.GetTempPath(), $"PowerRefresher_LogReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            using (StreamWriter streamWriter = File.CreateText(filename))
            {
                streamWriter.WriteLine($"PowerRefresher ({System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}) - Update Information");
                streamWriter.WriteLine($"\nFile: {txtInput.Text}");
                streamWriter.WriteLine($"\n[LOGS]\n{txtOutput.Text}");
            }

            OpenFile(filename);
        }
        private void ShowMessage(string message, MessageBoxIcon icon, string title = null)
        {
            MessageBox.Show(message, "PowerRefresher: " + (title ?? "Information"), MessageBoxButtons.OK, icon);
        }
        private void RefreshFailed(Exception err)
        {
            int errorLine = txtOutput.Lines.Length + 1;
            cmdStartRefresh.Enabled = false;
            txtOutput.Text += "\n\n=== REFRESH FAILED ===\n";
            txtOutput.Text += err.Message;
            SetOutputLineColor(errorLine, Color.Red);

            BringToFront();
            Activate();
        }
        private bool CheckRequiredFields()
        {
            if (string.IsNullOrEmpty(txtInput.Text))
            {
                ShowMessage("You must select a PBIX file before continue!", MessageBoxIcon.Exclamation, "Input pbix missing");
                return false;
            }

            if (chkPublish.Checked && (txtWorkspace.Text == "Put your workspace here" || txtWorkspace.Text == ""))
            {
                ShowMessage("You must set a valid workspace or disable publish option!", MessageBoxIcon.Exclamation, "Workspace missing");
                return false;
            }

            if (!chkRefreshAll.Checked && chklModelFields.CheckedItems.Count == 0)
            {
                ShowMessage("You must select at least one field to refresh!", MessageBoxIcon.Exclamation, "Selection missing");
                return false;
            }

            return true;
        }
    }
}
