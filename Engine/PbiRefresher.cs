using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace PowerRefresher.Engine
{
    /// <summary>
    /// Drives Power BI Desktop through UI Automation: open/bind a .pbix, refresh, publish, save, close.
    /// Has no UI of its own; progress goes to <see cref="Log"/>, failures are thrown as
    /// <see cref="RefreshFailedException"/> or <see cref="AutomationException"/>.
    /// Must be called from an STA thread (keyboard fallbacks use SendKeys).
    ///
    /// Every element is found through a <see cref="Locator"/>: an ordered list of ways to find it
    /// (AutomationId first, then visible name and role, ...). When the first way fails but a later one works,
    /// a warning is logged once, so a Power BI UI change shows up in the log before it breaks a run.
    /// </summary>
    public sealed class PbiRefresher
    {
        private const string PBI_PROCESS_NAME = "PBIDesktop";
        private const int ELEMENT_TIMEOUT_SECONDS = 60;
        private const int SAVE_TIMEOUT_SECONDS = 30 * 60;
        private const int REFRESH_TIMEOUT_SECONDS = 6 * 60 * 60;

        private AutomationElement pbi;
        private AutomationElement refreshDialog;
        private PbiStrings ui;
        private Locators loc;
        private readonly HashSet<string> warnedFallbacks = new HashSet<string>();

        public PbiRefresher(RefreshOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>Options are re-read at each step, so a caller (the GUI) may adjust them between Open and Run.</summary>
        public RefreshOptions Options { get; }

        /// <summary>
        /// Progress, one complete message per call. Normally raised on the calling thread, but <see cref="KillPowerBI"/>
        /// (used by <see cref="HangWatchdog"/>) raises it from the watchdog thread, so handlers must be thread-safe.
        /// </summary>
        public event Action<LogLevel, string> Log;

        /// <summary>Tables found in the model by the last <see cref="Open"/>.</summary>
        public IReadOnlyList<string> Tables { get; private set; } = new string[0];

        /// <summary>True if <see cref="Open"/> launched Power BI, false if it bound to a window that was already open.</summary>
        public bool OpenedFile { get; private set; }

        /// <summary>Name of the step in progress, for watchdog and error messages. Safe to read from other threads.</summary>
        public string CurrentStep => currentStep;
        private volatile string currentStep = "Starting";

        /// <summary>Main window handle of the Power BI instance being driven, or zero before it is found. Safe to read from other threads.</summary>
        public IntPtr WindowHandle => new IntPtr(Interlocked.Read(ref windowHandle));
        private long windowHandle;

        /// <summary>Process id of the Power BI instance being driven, or zero before it is found. Safe to read from other threads.</summary>
        public int ProcessId => processId;
        private volatile int processId;

        #region Public operations

        /// <summary>Opens the file in Power BI Desktop (or binds to its open window), reads the table list and handles the script visuals prompt.</summary>
        public IReadOnlyList<string> Open()
        {
            Init();
            if (string.IsNullOrWhiteSpace(Options.FilePath)) throw new AutomationException("No .pbix file given.");
            if (!File.Exists(Options.FilePath)) throw new AutomationException($"File not found: {Options.FilePath}");

            string fileBaseName = Path.GetFileNameWithoutExtension(Options.FilePath);
            Step("Open file in Power BI", () => OpenOrBindPbixFile(fileBaseName));
            Step("Wait for Power BI window", () => FindPowerBIWindow(fileBaseName));
            Step("Read table list", () =>
            {
                Tables = FetchTables();
                Info($"Model has {Tables.Count} table(s): {string.Join(", ", Tables)}");
            });
            HandleScriptVisuals();
            return Tables;
        }

        /// <summary>Checks the requested tables exist in the model. Call after <see cref="Open"/>; throws AutomationException listing what is available.</summary>
        public void ValidateTables()
        {
            if (!Options.RefreshAll) ResolveTables();
        }

        /// <summary>Refreshes, optionally publishes, saves and optionally closes. Call <see cref="Open"/> first.</summary>
        public void Run()
        {
            if (pbi == null) throw new InvalidOperationException("Call Open() before Run().");
            Init();

            MaximizeWindow();

            if (Options.RefreshAll)
            {
                Step("Refresh all tables", RefreshAll);
            }
            else
            {
                HashSet<string> tables = ResolveTables();
                int index = 0;
                foreach (string table in Tables.Where(tables.Contains).ToList())
                {
                    index++;
                    Step($"Refresh table '{table}' ({index}/{tables.Count})", () => RefreshTable(table));
                    if (refreshDialog != null) break;
                }
            }

            if (refreshDialog != null)
            {
                string details = ReadDialogText(refreshDialog);
                throw new RefreshFailedException("Power BI reported that the refresh failed." +
                    (string.IsNullOrEmpty(details) ? " Check the refresh dialog in Power BI for details." : "\nPower BI says:\n" + details));
            }

            if (Options.Publish)
            {
                Step("Save before publishing", SaveFile);
                Step($"Publish to workspace '{Options.Workspace}'", PublishFile);
            }

            Step("Save file", SaveFile);

            if (Options.CloseFile)
            {
                try
                {
                    Close();
                }
                catch (AutomationException e)
                {
                    //The file is already saved. A window stuck on a prompt would break the next scheduled run, so remove it.
                    Warn($"Power BI did not close after saving ({e.Message}); killing it. The saved file is not affected.");
                    KillPowerBI("did not close after saving");
                }
            }
        }

        /// <summary>
        /// Closes the Power BI window and waits until it is gone. Power BI ignores close requests while it is still
        /// starting up, so the request is retried. Unsaved changes make Power BI prompt instead, which ends in an error.
        /// </summary>
        public void Close()
        {
            if (pbi == null) return;
            Step("Close Power BI", () =>
            {
                IntPtr hwnd = WindowHandle;
                for (int attempt = 1; attempt <= 6 && IsWindow(hwnd); attempt++)
                {
                    if (attempt > 1) Debug($"Window still open, close request {attempt}/6");
                    try { ((WindowPattern)pbi.GetCurrentPattern(WindowPattern.Pattern)).Close(); }
                    catch (ElementNotAvailableException) { }
                    catch (InvalidOperationException) { }

                    Stopwatch sw = Stopwatch.StartNew();
                    while (IsWindow(hwnd) && sw.Elapsed.TotalSeconds < 5) Thread.Sleep(250);
                }
                if (IsWindow(hwnd)) throw new AutomationException("Power BI window did not close within 30s (it may be showing a prompt).");
                pbi = null;
            });
        }

        /// <summary>Force-kills the Power BI process being driven (used after a hang or before a retry). Unsaved work in it is lost.</summary>
        public void KillPowerBI(string reason)
        {
            int pid = processId;
            if (pid == 0) return;
            Process process;
            try { process = Process.GetProcessById(pid); }
            catch (ArgumentException) { pbi = null; return; } //Already gone
            KillProcess(process, reason);
            pbi = null;
        }

        /// <summary>
        /// Checks every UI element the tool depends on without refreshing or saving anything, and reports which
        /// lookup method found each one. Call <see cref="Open"/> first. Opens and closes one table's "..." menu.
        /// </summary>
        public IReadOnlyList<DiagnosticResult> Diagnose()
        {
            if (pbi == null) throw new InvalidOperationException("Call Open() before Diagnose().");
            Init();
            List<DiagnosticResult> results = new List<DiagnosticResult>();
            currentStep = "Diagnose";

            results.Add(new DiagnosticResult("Power BI window", DiagnosticStatus.Ok, $"pid {ProcessId}, title '{Safe(() => pbi.Current.Name)}'"));
            results.Add(Check(loc.FieldTree, pbi));
            results.Add(Check(loc.RefreshButton, pbi));
            results.Add(Check(loc.SaveButton, pbi));
            results.Add(Check(loc.PublishButton, pbi));

            AutomationElement node = Safe(() => FirstTableNode(5));
            if (node == null)
            {
                results.Add(new DiagnosticResult("Table name", DiagnosticStatus.Missing, "no table found in the field list"));
            }
            else
            {
                results.Add(Check(loc.TableName, node));
                DiagnosticResult more = Check(loc.MoreOptions, node);
                results.Add(more);

                if (more.Status != DiagnosticStatus.Missing)
                {
                    AutomationElement moreButton = Find(loc.MoreOptions, node, out _);
                    TryDo(() => InvokeElement(moreButton));
                    Thread.Sleep(800);
                    DiagnosticResult menu = Check(loc.RefreshMenu, pbi);
                    results.Add(menu);

                    AutomationElement menuItem = Find(loc.RefreshMenu, pbi, out _);
                    if (menuItem != null && menuItem.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object expand))
                    {
                        TryDo(() => ((ExpandCollapsePattern)expand).Expand());
                        Thread.Sleep(800);
                        results.Add(Check(loc.RefreshDataItem, pbi));
                    }
                    else if (menuItem != null)
                    {
                        results.Add(new DiagnosticResult(loc.RefreshDataItem.Name, DiagnosticStatus.Ok, "not needed: legacy flat 'Refresh data' item"));
                    }
                    CloseMenus();
                }
            }

            if (Options.Publish) DiagnosePublish(results);

            foreach (string runtimeOnly in new[] { "Refresh dialog", "Save progress dialog", "Publish 'Replace' prompt and result", "Script visuals prompt" })
            {
                results.Add(new DiagnosticResult(runtimeOnly, DiagnosticStatus.NotChecked, "only appears during a real run"));
            }

            foreach (DiagnosticResult r in results)
            {
                LogLevel level = r.Status == DiagnosticStatus.Missing ? LogLevel.Error : r.Status == DiagnosticStatus.Fallback ? LogLevel.Warn : LogLevel.Info;
                Emit(level, $"{r.Element}: {r.Status} ({r.Detail})");
            }
            return results;
        }

        /// <summary>
        /// Opens the publish destination dialog, checks the list, the requested workspace and the Select button,
        /// then cancels. Nothing is uploaded: the upload only starts when Select is clicked.
        /// </summary>
        private void DiagnosePublish(List<DiagnosticResult> results)
        {
            AutomationElement publish = Find(loc.PublishButton, pbi, out _);
            if (publish == null) return; //Already reported as missing

            InvokeElement(publish);
            try
            {
                AutomationElement list;
                try { list = WaitFor(loc.WorkspaceList, pbi, 30); }
                catch (AutomationException e) when (IsElementTimeout(e)) { list = null; }
                results.Add(Check(loc.WorkspaceList, pbi));
                if (list == null) return;

                AutomationElement workspace = FindWorkspace(list, Options.Workspace, out List<string> offered);
                results.Add(workspace != null
                    ? new DiagnosticResult($"Workspace '{Options.Workspace}'", DiagnosticStatus.Ok, $"listed ({offered.Count} workspaces offered)")
                    : new DiagnosticResult($"Workspace '{Options.Workspace}'", DiagnosticStatus.Missing, "not listed. Offered: " + string.Join(", ", offered)));

                if (workspace != null)
                {
                    SelectListItem(workspace);
                    Thread.Sleep(500);
                }
                DiagnosticResult select = Check(loc.PublishSelectButton, pbi);
                AutomationElement selectButton = Find(loc.PublishSelectButton, pbi, out _);
                if (selectButton != null && workspace != null && !selectButton.Current.IsEnabled)
                    select = new DiagnosticResult(select.Element, DiagnosticStatus.Missing, "found but stays disabled after selecting the workspace | " + select.Detail);
                results.Add(select);
                results.Add(Check(loc.PublishCancelButton, pbi));
            }
            finally
            {
                CancelPublishDialog();
                Thread.Sleep(500);
                if (Find(loc.WorkspaceList, pbi, out _) != null) Warn("The publish dialog may still be open; close it in Power BI before the next run.");
                else Debug("Publish dialog cancelled; nothing was published");
            }
        }

        #endregion

        #region Logging

        private void Emit(LogLevel level, string message) => Log?.Invoke(level, message);
        private void Debug(string message) => Emit(LogLevel.Debug, message);
        private void Info(string message) => Emit(LogLevel.Info, message);
        private void Warn(string message) => Emit(LogLevel.Warn, message);

        private const string LoggedKey = "PowerRefresher.Logged";

        /// <summary>True if the engine already logged this exception as a failed step, so callers needn't log it again.</summary>
        public static bool WasLogged(Exception e) => e.Data.Contains(LoggedKey);

        private static bool IsAutomationTimeout(Exception e) =>
            e is TimeoutException || (e is COMException com && com.HResult == unchecked((int)0x80131505));

        /// <summary>Logs the start, duration and outcome of one step. Steps are not nested.</summary>
        private void Step(string name, Action action)
        {
            currentStep = name;
            Info($"{name}...");
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                action();
            }
            catch (Exception e)
            {
                //UI Automation gives up on a frozen target after about 2 minutes with a timeout; report that as a hang
                Exception unresponsive = IsAutomationTimeout(e)
                    ? new PowerBIUnresponsiveException($"Power BI stopped answering UI Automation requests during '{name}' (it appears to be hung).", e)
                    : null;
                Emit(LogLevel.Error, $"{name}: FAILED after {LogFormat.Duration(sw.Elapsed)}: {(unresponsive ?? e).Message}");
                (unresponsive ?? e).Data[LoggedKey] = true;
                if (unresponsive != null) throw unresponsive;
                throw;
            }
            Info($"{name}: done in {LogFormat.Duration(sw.Elapsed)}");
        }

        #endregion

        #region Open / bind

        private void Init()
        {
            ui = PbiStrings.For(Options.Language);
            loc = new Locators(ui);
        }

        /// <summary>
        /// Uses a window that already has the file open if it is healthy; otherwise kills every instance holding the
        /// file (debris from a failed run, a hung instance, a forgotten dialog) and opens it fresh.
        /// </summary>
        private void OpenOrBindPbixFile(string fileBaseName)
        {
            OpenedFile = false;
            List<Process> existing = Process.GetProcessesByName(PBI_PROCESS_NAME)
                .Where(p => IsPbiWindowTitle(p.MainWindowTitle, fileBaseName))
                .OrderByDescending(p => p.StartTime)
                .ToList();

            if (existing.Count > 0)
            {
                Process newest = existing[0]; //The one FindPbiWindow would bind to
                string problem = DescribeProblem(newest);
                if (problem == null)
                {
                    Info($"'{fileBaseName}' is already open in Power BI (pid {newest.Id}) and looks healthy; using that window");
                    if (existing.Count > 1) Warn($"{existing.Count} Power BI windows have '{fileBaseName}' open; using the newest (pid {newest.Id})");
                    return;
                }

                Warn($"'{fileBaseName}' is already open in Power BI (pid {newest.Id}) but it {problem}; restarting it");
                foreach (Process p in existing) KillProcess(p, "restarting an unhealthy window");
            }

            //Not minimized: Power BI exposes no UI to automation while its window is minimized
            Process.Start(new ProcessStartInfo(Options.FilePath) { UseShellExecute = true });
            OpenedFile = true;
            Debug($"Launched '{Options.FilePath}' through its file association");
        }

        /// <summary>Why an already-open Power BI window shouldn't be reused, or null if it looks fine.</summary>
        private string DescribeProblem(Process process)
        {
            IntPtr hwnd = process.MainWindowHandle;
            if (hwnd == IntPtr.Zero) return "has no main window (it may be starting or closing)";
            if (!NativeMethods.Responds(hwnd)) return "is not responding";
            if (!NativeMethods.IsWindowEnabled(hwnd)) return "is blocked by a dialog";

            List<string> others = NativeMethods.OtherVisibleWindows(process.Id, hwnd);
            if (others.Count > 0) return "is showing another window: " + string.Join(", ", others);

            try
            {
                AutomationElement window = AutomationElement.FromHandle(hwnd);
                WindowPattern pattern = (WindowPattern)window.GetCurrentPattern(WindowPattern.Pattern);
                if (pattern.Current.WindowVisualState == WindowVisualState.Minimized)
                {
                    //Its UI is invisible to automation while minimized, so dialogs couldn't be seen
                    Debug("Existing window is minimized; restoring it to check for open dialogs");
                    pattern.SetWindowVisualState(WindowVisualState.Normal);
                    Thread.Sleep(1500);
                }

                if (Find(loc.RefreshDialog, window, out _) != null) return "has a refresh dialog open (probably left over from a failed refresh)";
                if (Find(loc.WorkspaceList, window, out _) != null) return "has the publish dialog open";
                if (FindById(window, ui.ScriptVisualsDialogId) != null) return "is showing a prompt (script visuals or a message)";
            }
            catch (Exception e) when (e is ElementNotAvailableException || e is InvalidOperationException)
            {
                return $"could not be inspected ({e.Message})";
            }
            return null;
        }

        private void KillProcess(Process process, string reason)
        {
            try
            {
                Warn($"Killing Power BI (pid {process.Id}): {reason}");
                process.Kill();
                if (!process.WaitForExit(15000)) Warn($"Power BI (pid {process.Id}) did not exit within 15s of being killed");
            }
            catch (InvalidOperationException) { } //Already gone
            catch (System.ComponentModel.Win32Exception e) { Warn($"Could not kill Power BI (pid {process.Id}): {e.Message}"); }
        }

        private void FindPowerBIWindow(string fileBaseName)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                pbi = WaitForElement(() => FindPbiWindow(fileBaseName), "the Power BI window", Options.OpenTimeoutSeconds, 1000);
            }
            catch (AutomationException e) when (IsElementTimeout(e))
            {
                throw new AutomationException($"No Power BI window titled '{fileBaseName}' appeared within {Options.OpenTimeoutSeconds}s. Try a larger --timeout.");
            }

            Interlocked.Exchange(ref windowHandle, pbi.Current.NativeWindowHandle);
            processId = pbi.Current.ProcessId;
            Info($"Found Power BI window after {LogFormat.Duration(sw.Elapsed)} (pid {processId}, Power BI Desktop {GetPowerBIVersion(processId)})");
            EnsureNotMinimized();
        }

        /// <summary>
        /// Power BI's embedded browser stops exposing its UI to automation while the window is minimized
        /// (the tree shrinks to a single element), so every lookup would time out. Restore it first.
        /// </summary>
        private void EnsureNotMinimized()
        {
            try
            {
                WindowPattern window = (WindowPattern)pbi.GetCurrentPattern(WindowPattern.Pattern);
                if (window.Current.WindowVisualState != WindowVisualState.Minimized) return;
                Info("Power BI window is minimized; restoring it (Power BI hides its UI from automation while minimized)");
                window.SetWindowVisualState(WindowVisualState.Normal);
                Thread.Sleep(1500);
            }
            catch (InvalidOperationException e)
            {
                Debug($"Could not check or restore the window state: {e.Message}");
            }
        }

        /// <summary>
        /// Newer versions title the window with the bare file name ("Sales"), older ones with
        /// "Sales - Power BI Desktop". If several instances match, the newest wins.
        /// </summary>
        private static AutomationElement FindPbiWindow(string fileBaseName)
        {
            Process match = null;
            foreach (Process process in Process.GetProcessesByName(PBI_PROCESS_NAME))
            {
                if (process.MainWindowHandle == IntPtr.Zero || !IsPbiWindowTitle(process.MainWindowTitle, fileBaseName)) continue;
                if (match == null || process.StartTime > match.StartTime) match = process;
            }
            return match == null ? null : AutomationElement.FromHandle(match.MainWindowHandle);
        }

        private static bool IsPbiWindowTitle(string title, string fileBaseName) =>
            string.Equals(title, fileBaseName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(title, fileBaseName + " - Power BI Desktop", StringComparison.OrdinalIgnoreCase);

        private static string GetPowerBIVersion(int pid)
        {
            try { return Process.GetProcessById(pid).MainModule.FileVersionInfo.ProductVersion?.Split(' ')[0] ?? "unknown"; }
            catch (Exception) { return "unknown"; }
        }

        private IReadOnlyList<string> FetchTables()
        {
            List<string> tables = new List<string>();
            for (AutomationElement node = FirstTableNode(Math.Max(Options.OpenTimeoutSeconds, ELEMENT_TIMEOUT_SECONDS)); node != null; node = TreeWalker.ControlViewWalker.GetNextSibling(node))
            {
                tables.Add(GetTableName(node));
            }
            return tables;
        }

        private void HandleScriptVisuals()
        {
            AutomationElement dialog;
            try
            {
                dialog = WaitForElement(() => FindById(pbi, ui.ScriptVisualsDialogId), "the script visuals prompt", 4, 1000);
            }
            catch (AutomationException e) when (IsElementTimeout(e))
            {
                Debug("No script visuals prompt");
                return;
            }

            Step($"Answer script visuals prompt ({Options.ScriptVisuals.ToString().ToLowerInvariant()})", () =>
            {
                switch (Options.ScriptVisuals)
                {
                    case ScriptVisualsAction.Enable:
                        InvokeElement(WaitForElement(() => FindByName(dialog, ui.EnableScriptVisualsButton), $"the '{ui.EnableScriptVisualsButton}' button"));
                        break;
                    case ScriptVisualsAction.Disable:
                        InvokeElement(WaitForElement(() => FindByName(dialog, ui.CancelScriptVisualsButton), $"the '{ui.CancelScriptVisualsButton}' button"));
                        break;
                    default:
                        throw new AutomationException("The file has script visuals and the run is not allowed to enable or skip them (see --script-visuals).");
                }
            });
        }

        #endregion

        #region Refresh

        private void MaximizeWindow()
        {
            EnsureNotMinimized();
            Thread.Sleep(500);
            try { ((WindowPattern)pbi.GetCurrentPattern(WindowPattern.Pattern)).SetWindowVisualState(WindowVisualState.Maximized); }
            catch (InvalidOperationException e) { Debug($"Could not maximize the window: {e.Message}"); }
        }

        private void RefreshAll()
        {
            InvokeElement(WaitFor(loc.RefreshButton, pbi));
            LookForRefreshDialog();
            WaitForRefreshToFinish();
        }

        /// <summary>Maps requested table names (case-insensitive) to model names; fails listing what is available.</summary>
        private HashSet<string> ResolveTables()
        {
            HashSet<string> resolved = new HashSet<string>(StringComparer.Ordinal);
            List<string> missing = new List<string>();
            foreach (string requested in Options.Tables)
            {
                string match = Tables.FirstOrDefault(t => string.Equals(t, requested.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match == null) missing.Add(requested); else resolved.Add(match);
            }
            if (missing.Count > 0)
                throw new AutomationException($"Table(s) not found in model: {string.Join(", ", missing)}. Available: {string.Join(", ", Tables)}");
            return resolved;
        }

        private void RefreshTable(string table)
        {
            //Walk the field list fresh each time: refreshing can re-render the tree and invalidate old nodes
            AutomationElement node = null;
            for (AutomationElement n = FirstTableNode(ELEMENT_TIMEOUT_SECONDS); n != null; n = TreeWalker.ControlViewWalker.GetNextSibling(n))
            {
                if (GetTableName(n) == table) { node = n; break; }
            }
            if (node == null) throw new AutomationException($"Table '{table}' is no longer in the field list.");

            OpenTableMenuAndRefresh(node, table);
            LookForRefreshDialog();
            WaitForRefreshToFinish();
            if (refreshDialog == null) WaitForWorkingDialog();
        }

        private AutomationElement FirstTableNode(int timeoutSeconds) =>
            WaitForElement(() =>
            {
                AutomationElement tree = Find(loc.FieldTree, pbi, out _);
                return tree == null ? null : TreeWalker.ControlViewWalker.GetFirstChild(tree);
            }, "the model field list", timeoutSeconds);

        private string GetTableName(AutomationElement node)
        {
            AutomationElement nameSource = Find(loc.TableName, node, out int strategy);
            return strategy == 0 ? nameSource.Current.Name : node.Current.Name;
        }

        private void OpenTableMenuAndRefresh(AutomationElement node, string table)
        {
            AutomationElement moreOptions = Find(loc.MoreOptions, node, out _);
            if (moreOptions != null)
            {
                InvokeElement(moreOptions);
            }
            else
            {
                WarnOnce("table-menu-keyboard", $"No 'More options' button on table '{table}'; falling back to selecting it and pressing Shift+F10 (needs Power BI focused).");
                pbi.SetFocus();
                ((SelectionItemPattern)node.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                SendKeys.SendWait("+{F10}");
            }

            //Older versions: flat "Refresh data" item. Current versions: "Refresh" submenu > "Data".
            AutomationElement item = WaitFor(loc.RefreshMenu, pbi, 10);
            if (item.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object expand))
            {
                ((ExpandCollapsePattern)expand).Expand();
                item = WaitFor(loc.RefreshDataItem, pbi, 10);
            }
            InvokeElement(item);
        }

        private void LookForRefreshDialog()
        {
            try
            {
                //A fast refresh can finish before we ever see the dialog, so not finding it is fine
                refreshDialog = WaitFor(loc.RefreshDialog, pbi, 2, 100);
                Debug("Refresh dialog opened");
            }
            catch (AutomationException e) when (IsElementTimeout(e))
            {
                refreshDialog = null;
                Debug("Refresh dialog not seen (refresh may have finished immediately)");
            }
        }

        /// <summary>
        /// Success: the refresh dialog closes on its own.
        /// Failure: the dialog stays open showing errors and its Cancel button goes away (only Close remains).
        /// The search is scoped to the dialog: the window title bar also has a "Close" button.
        /// </summary>
        private void WaitForRefreshToFinish()
        {
            Stopwatch sw = Stopwatch.StartNew();
            TimeSpan nextProgress = TimeSpan.FromMinutes(1);
            int pollsWithoutCancel = 0;
            while (true)
            {
                Thread.Sleep(500);
                ThrowIfWindowGone("the refresh to finish");
                try
                {
                    refreshDialog = Find(loc.RefreshDialog, pbi, out _);
                    if (refreshDialog == null) break;

                    bool inProgress = FindByName(refreshDialog, ui.RefreshInProgressButton) != null;
                    pollsWithoutCancel = inProgress ? 0 : pollsWithoutCancel + 1;
                    if (pollsWithoutCancel >= 4) break;
                }
                catch (ElementNotAvailableException)
                {
                    //Dialog closed while being inspected; next poll will confirm
                }

                if (sw.Elapsed >= nextProgress)
                {
                    Info($"Still refreshing ({LogFormat.Duration(sw.Elapsed)} so far)");
                    nextProgress += nextProgress.TotalMinutes < 10 ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(5);
                }
                if (sw.Elapsed.TotalSeconds >= REFRESH_TIMEOUT_SECONDS)
                    throw new AutomationException($"Refresh did not finish within {REFRESH_TIMEOUT_SECONDS / 3600} hours.");
            }
            if (refreshDialog != null) Emit(LogLevel.Error, "Refresh dialog stayed open without its Cancel button: Power BI reported an error");
        }

        /// <summary>All text in a dialog, one line per element, for logging Power BI's error messages.</summary>
        private static string ReadDialogText(AutomationElement dialog)
        {
            try
            {
                IEnumerable<string> lines = dialog.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
                    .Cast<AutomationElement>()
                    .Select(e => e.Current.Name?.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct();
                return string.Join("\n", lines);
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
        }

        #endregion

        #region Save / publish

        private void SaveFile()
        {
            AutomationElement save;
            try
            {
                save = WaitFor(loc.SaveButton, pbi, 30);
            }
            catch (AutomationException e) when (IsElementTimeout(e))
            {
                save = null;
            }

            if (save != null)
            {
                InvokeElement(save);
            }
            else
            {
                WarnOnce("save-keyboard", "Save button not found by any lookup; falling back to Ctrl+S (needs Power BI focused).");
                pbi.SetFocus();
                SendKeys.SendWait("^s");
            }

            Thread.Sleep(1500);
            WaitForWorkingDialog();
        }

        private void WaitForWorkingDialog()
        {
            Thread.Sleep(1000);
            WaitForElementGone(() => FindByName(pbi, ui.SaveWaitMessage), $"the '{ui.SaveWaitMessage}' dialog", SAVE_TIMEOUT_SECONDS);
        }

        private void PublishFile()
        {
            InvokeElement(WaitFor(loc.PublishButton, pbi));

            //Destination dialog: pick the workspace, then "Select" starts the upload
            AutomationElement list = WaitFor(loc.WorkspaceList, pbi);
            AutomationElement workspace = FindWorkspace(list, Options.Workspace, out List<string> offered);
            if (workspace == null)
            {
                CancelPublishDialog();
                throw new AutomationException($"Workspace '{Options.Workspace}' not found. Workspaces offered: {string.Join(", ", offered)}");
            }
            SelectListItem(workspace);
            Debug($"Selected workspace '{workspace.Current.Name}'");
            InvokeElement(WaitFor(loc.PublishSelectButton, pbi));

            //Only shown when a dataset with this name already exists in the workspace
            AutomationElement replace = null;
            try { replace = WaitFor(loc.ReplaceButton, pbi, 10, 1000); }
            catch (AutomationException e) when (IsElementTimeout(e)) { Debug("No replace prompt (new dataset in this workspace)"); }
            if (replace != null)
            {
                Info("A dataset with this name already exists in the workspace; replacing it");
                InvokeElement(replace);
            }

            //Upload can take a while on large models
            Info("Uploading...");
            InvokeElement(WaitFor(loc.PublishSuccessButton, pbi, SAVE_TIMEOUT_SECONDS, 1000));
        }

        /// <summary>Finds a workspace in the destination list by name (case-insensitive) and returns every name offered.</summary>
        private static AutomationElement FindWorkspace(AutomationElement list, string name, out List<string> offered)
        {
            offered = new List<string>();
            AutomationElement match = null;
            for (AutomationElement item = TreeWalker.ControlViewWalker.GetFirstChild(list); item != null; item = TreeWalker.ControlViewWalker.GetNextSibling(item))
            {
                string itemName = item.Current.Name?.Trim();
                offered.Add(itemName);
                if (match == null && string.Equals(itemName, name?.Trim(), StringComparison.OrdinalIgnoreCase)) match = item;
            }
            return match;
        }

        private static void SelectListItem(AutomationElement item)
        {
            if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object selection)) ((SelectionItemPattern)selection).Select();
            else InvokeElement(item);
        }

        /// <summary>Closes the destination dialog without publishing: Cancel button, or Escape as a fallback.</summary>
        private void CancelPublishDialog()
        {
            AutomationElement cancel = Find(loc.PublishCancelButton, pbi, out _);
            if (cancel != null)
            {
                TryDo(() => InvokeElement(cancel));
                return;
            }
            Debug("No Cancel button in the publish dialog; pressing Escape");
            CloseMenus();
        }

        #endregion

        #region Locators

        /// <summary>One way to find an element, relative to a root element.</summary>
        private sealed class Strategy
        {
            public Strategy(string description, Func<AutomationElement, AutomationElement> find)
            {
                Description = description;
                Find = find;
            }
            public string Description { get; }
            public Func<AutomationElement, AutomationElement> Find { get; }
        }

        /// <summary>An element and the ordered ways to find it; the first is the expected one.</summary>
        private sealed class Locator
        {
            public Locator(string name, params Strategy[] strategies)
            {
                Name = name;
                Strategies = strategies;
            }
            public string Name { get; }
            public Strategy[] Strategies { get; }
        }

        private sealed class Locators
        {
            public readonly Locator RefreshButton, SaveButton, PublishButton, FieldTree, TableName, MoreOptions, RefreshMenu, RefreshDataItem, RefreshDialog,
                WorkspaceList, PublishSelectButton, PublishCancelButton, ReplaceButton, PublishSuccessButton;

            public Locators(PbiStrings ui)
            {
                RefreshButton = new Locator("Ribbon Refresh button",
                    new Strategy("split button named '" + ui.RefreshButtonName + "' (splitPrimaryButton)", r => FindButtons(r, ui.RefreshButtonName).FirstOrDefault(b => Cls(b).Contains("splitPrimaryButton"))),
                    new Strategy("AutomationId '" + ui.RefreshButtonId + "' (pre-2024 ribbon)", r => FindById(r, ui.RefreshButtonId)),
                    new Strategy("any clickable button named '" + ui.RefreshButtonName + "'", r => FindButtons(r, ui.RefreshButtonName).FirstOrDefault(Invokable)));

                SaveButton = new Locator("Save button",
                    new Strategy("AutomationId '" + ui.SaveButtonId + "'", r => FindById(r, ui.SaveButtonId)),
                    new Strategy("clickable button named '" + ui.SaveButtonName + "'", r => FindButtons(r, ui.SaveButtonName).FirstOrDefault(Invokable)));

                PublishButton = new Locator("Publish button",
                    new Strategy("AutomationId '" + ui.PublishButtonId + "'", r => FindById(r, ui.PublishButtonId)),
                    new Strategy("clickable button named '" + ui.PublishButtonName + "'", r => FindButtons(r, ui.PublishButtonName).FirstOrDefault(Invokable)));

                FieldTree = new Locator("Field list",
                    new Strategy("tree with class '" + ui.FieldTreeClass + "'", r => r.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, ui.FieldTreeClass))),
                    new Strategy("tree control whose first child is a tree item", r => r.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tree))
                        .Cast<AutomationElement>()
                        .FirstOrDefault(t => TreeWalker.ControlViewWalker.GetFirstChild(t)?.Current.ControlType == ControlType.TreeItem)));

                //Relative to a table node. The first strategy returns the expander, whose name is the bare table name;
                //the fallback returns the node itself, whose name may carry a "Table " prefix.
                TableName = new Locator("Table name",
                    new Strategy("expander button inside the table node", n => Child(n, c => c.Current.ControlType == ControlType.Button && Cls(c).Contains("expander") && !string.IsNullOrEmpty(c.Current.Name))),
                    new Strategy("table node's own name (may include a 'Table ' prefix)", n => n));

                MoreOptions = new Locator("Table 'More options' (...) button",
                    new Strategy("button with class 'menu-button' inside the table node", n => Child(n, c => c.Current.ControlType == ControlType.Button && Cls(c).Contains("menu-button"))),
                    new Strategy("button named '" + ui.MoreOptionsName + "' inside the table node", n => Child(n, c => c.Current.ControlType == ControlType.Button && c.Current.Name == ui.MoreOptionsName)));

                RefreshMenu = new Locator("Table menu 'Refresh' item",
                    new Strategy("submenu named '" + ui.RefreshButtonName + "'", r => r.FindFirst(TreeScope.Descendants, new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                        new PropertyCondition(AutomationElement.NameProperty, ui.RefreshButtonName)))),
                    new Strategy("AutomationId '" + ui.RefreshMenuItemId + "' (legacy flat item)", r => FindById(r, ui.RefreshMenuItemId)));

                RefreshDataItem = new Locator("Table menu 'Refresh > Data' item",
                    new Strategy("AutomationId '" + ui.RefreshSubmenuDataId + "'", r => FindById(r, ui.RefreshSubmenuDataId)),
                    new Strategy("menu item named '" + ui.RefreshSubmenuDataName + "'", r => r.FindFirst(TreeScope.Descendants, new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                        new PropertyCondition(AutomationElement.NameProperty, ui.RefreshSubmenuDataName)))));

                RefreshDialog = new Locator("Refresh dialog",
                    new Strategy("AutomationId '" + ui.RefreshDialogId + "'", r => FindById(r, ui.RefreshDialogId)),
                    //UIA has no Dialog control type; Power BI's web UI reports dialogs with localized type "dialog"
                    new Strategy("dialog named '" + ui.RefreshButtonName + "'", r => r.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, ui.RefreshButtonName))
                        .Cast<AutomationElement>()
                        .FirstOrDefault(e => string.Equals(e.Current.LocalizedControlType, "dialog", StringComparison.OrdinalIgnoreCase))));
                WorkspaceList = new Locator("Publish dialog workspace list",
                    new Strategy("list named '" + ui.WorkspacesListName + "'", r => r.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, ui.WorkspacesListName))
                        .Cast<AutomationElement>()
                        .FirstOrDefault(e => e.Current.ControlType == ControlType.List)),
                    new Strategy("selection list with class 'mat-mdc-selection-list'", r => r.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List))
                        .Cast<AutomationElement>()
                        .FirstOrDefault(e => Cls(e).Contains("mat-mdc-selection-list"))),
                    new Strategy("first list inside dialog '" + ui.PublishGroupDialogId + "' (legacy)", r =>
                    {
                        AutomationElement dialog = FindById(r, ui.PublishGroupDialogId);
                        return dialog?.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.LocalizedControlTypeProperty, ui.WorkspaceContainerType));
                    }));

                PublishSelectButton = new Locator("Publish dialog 'Select' button",
                    new Strategy("AutomationId '" + ui.PublishSelectButtonId + "'", r => FindById(r, ui.PublishSelectButtonId)),
                    new Strategy("clickable button named '" + ui.SelectButton + "'", r => FindButtons(r, ui.SelectButton).FirstOrDefault(Invokable)));

                //No by-name fallback: the formula bar also has a button named "Cancel"
                PublishCancelButton = new Locator("Publish dialog 'Cancel' button",
                    new Strategy("AutomationId '" + ui.PublishCancelButtonId + "'", r => FindById(r, ui.PublishCancelButtonId)));

                //The 2.159 replace prompt and result screen haven't been inspected yet, so the by-name lookup comes first
                ReplaceButton = new Locator("Publish 'Replace' button",
                    new Strategy("clickable button named '" + ui.ReplaceButton + "'", r => FindButtons(r, ui.ReplaceButton).FirstOrDefault(Invokable)),
                    new Strategy("button named '" + ui.ReplaceButton + "' inside dialog '" + ui.ReplaceDialogId + "' (legacy)", r =>
                    {
                        AutomationElement dialog = FindById(r, ui.ReplaceDialogId);
                        return dialog == null ? null : FindButtons(dialog, ui.ReplaceButton).FirstOrDefault();
                    }));

                PublishSuccessButton = new Locator("Publish result 'Got it' button",
                    new Strategy("clickable button named '" + ui.SuccessPublishButton + "'", r => FindButtons(r, ui.SuccessPublishButton).FirstOrDefault(Invokable)),
                    new Strategy("button named '" + ui.SuccessPublishButton + "' inside dialog '" + ui.PublishDialogId + "' (legacy)", r =>
                    {
                        AutomationElement dialog = FindById(r, ui.PublishDialogId);
                        return dialog == null ? null : FindButtons(dialog, ui.SuccessPublishButton).FirstOrDefault();
                    }));
            }

            private static IEnumerable<AutomationElement> FindButtons(AutomationElement root, string name) =>
                root.FindAll(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.NameProperty, name))).Cast<AutomationElement>();

            private static string Cls(AutomationElement e) => e.Current.ClassName ?? string.Empty;

            private static bool Invokable(AutomationElement e) => (bool)e.GetCurrentPropertyValue(AutomationElement.IsInvokePatternAvailableProperty);

            private static AutomationElement Child(AutomationElement parent, Func<AutomationElement, bool> predicate)
            {
                for (AutomationElement c = TreeWalker.ControlViewWalker.GetFirstChild(parent); c != null; c = TreeWalker.ControlViewWalker.GetNextSibling(c))
                {
                    if (predicate(c)) return c;
                }
                return null;
            }
        }

        /// <summary>Tries each strategy in order; warns once per run when the expected (first) one no longer works.</summary>
        private AutomationElement Find(Locator locator, AutomationElement root, out int strategyIndex)
        {
            for (strategyIndex = 0; strategyIndex < locator.Strategies.Length; strategyIndex++)
            {
                AutomationElement found;
                try { found = locator.Strategies[strategyIndex].Find(root); }
                catch (ElementNotAvailableException) { found = null; }
                if (found == null) continue;

                if (strategyIndex > 0)
                {
                    //The element may simply have appeared between the first lookup and this one (menus opening,
                    //dialogs rendering). Re-check the expected lookup before calling it a UI change.
                    AutomationElement expected = null;
                    try { expected = locator.Strategies[0].Find(root); } catch (ElementNotAvailableException) { }
                    if (expected != null)
                    {
                        strategyIndex = 0;
                        return expected;
                    }

                    WarnOnce(locator.Name, $"{locator.Name}: expected lookup ({locator.Strategies[0].Description}) failed; " +
                        $"found it by {locator.Strategies[strategyIndex].Description}. Power BI's UI has probably changed; run --diagnose.");
                    Debug($"{locator.Name} fallback matched: {Describe(found)}");
                }
                return found;
            }
            strategyIndex = -1;
            return null;
        }

        private AutomationElement WaitFor(Locator locator, AutomationElement root, int timeoutSeconds = ELEMENT_TIMEOUT_SECONDS, int pollMs = 500)
        {
            try
            {
                return WaitForElement(() => Find(locator, root, out _), locator.Name, timeoutSeconds, pollMs);
            }
            catch (AutomationException e) when (IsElementTimeout(e))
            {
                throw ElementTimeout($"Timed out after {timeoutSeconds}s waiting for the {locator.Name}. Tried: " +
                    string.Join("; ", locator.Strategies.Select(s => s.Description)) + ". Run --diagnose to see what changed.");
            }
        }

        private DiagnosticResult Check(Locator locator, AutomationElement root)
        {
            List<string> matched = new List<string>();
            foreach (Strategy s in locator.Strategies)
            {
                AutomationElement found = null;
                try { found = s.Find(root); } catch (ElementNotAvailableException) { }
                matched.Add((found != null ? "yes: " : "no: ") + s.Description);
            }
            DiagnosticStatus status = matched[0].StartsWith("yes") ? DiagnosticStatus.Ok
                : matched.Any(m => m.StartsWith("yes")) ? DiagnosticStatus.Fallback
                : DiagnosticStatus.Missing;
            return new DiagnosticResult(locator.Name, status, string.Join(" | ", matched));
        }

        private static string Describe(AutomationElement e)
        {
            try
            {
                AutomationElement.AutomationElementInformation c = e.Current;
                return $"type={c.LocalizedControlType}, id='{c.AutomationId}', name='{c.Name}', class='{c.ClassName}'";
            }
            catch (ElementNotAvailableException)
            {
                return "(element no longer available)";
            }
        }

        private void WarnOnce(string key, string message)
        {
            if (warnedFallbacks.Add(key)) Warn(message);
        }

        private void CloseMenus()
        {
            try
            {
                pbi.SetFocus();
                SendKeys.SendWait("{ESC}{ESC}");
            }
            catch (Exception e) when (e is InvalidOperationException || e is ElementNotAvailableException)
            {
                Debug($"Could not close the table menu: {e.Message}");
            }
        }

        #endregion

        #region UIA helpers

        /// <summary>Polls <paramref name="find"/> until it returns an element, or throws AutomationException.</summary>
        private AutomationElement WaitForElement(Func<AutomationElement> find, string description, int timeoutSeconds = ELEMENT_TIMEOUT_SECONDS, int pollMs = 500)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                ThrowIfWindowGone(description);
                AutomationElement element = null;
                try { element = find(); } catch (ElementNotAvailableException) { }
                if (element != null) return element;
                if (sw.Elapsed.TotalSeconds >= timeoutSeconds)
                    throw ElementTimeout($"Timed out after {timeoutSeconds}s waiting for {description}.");
                Thread.Sleep(pollMs);
            }
        }

        /// <summary>Polls until <paramref name="find"/> returns null, or throws AutomationException.</summary>
        private void WaitForElementGone(Func<AutomationElement> find, string description, int timeoutSeconds, int pollMs = 1000)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                ThrowIfWindowGone(description + " to close");
                AutomationElement element = null;
                try { element = find(); } catch (ElementNotAvailableException) { }
                if (element == null) return;
                if (sw.Elapsed.TotalSeconds >= timeoutSeconds)
                    throw ElementTimeout($"Timed out after {timeoutSeconds}s waiting for {description} to close.");
                Thread.Sleep(pollMs);
            }
        }

        private const string ElementTimeoutKey = "PowerRefresher.ElementTimeout";

        /// <summary>A wait that simply ran out of time (as opposed to Power BI disappearing), which callers may treat as "not there".</summary>
        private static AutomationException ElementTimeout(string message)
        {
            AutomationException e = new AutomationException(message);
            e.Data[ElementTimeoutKey] = true;
            return e;
        }

        private static bool IsElementTimeout(Exception e) => e.Data.Contains(ElementTimeoutKey);

        /// <summary>
        /// Fails fast when the Power BI window we are driving has disappeared (crashed, closed by someone, or killed by
        /// the watchdog) instead of waiting out a timeout and blaming a UI change.
        /// </summary>
        private void ThrowIfWindowGone(string waitingFor)
        {
            IntPtr hwnd = WindowHandle;
            if (hwnd != IntPtr.Zero && !IsWindow(hwnd))
                throw new AutomationException($"The Power BI window closed unexpectedly (crashed, was closed, or was killed) while waiting for {waitingFor}.");
        }

        private static AutomationElement FindById(AutomationElement root, string automationId) =>
            root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

        private static AutomationElement FindByName(AutomationElement root, string name) =>
            root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name));

        private static void InvokeElement(AutomationElement element) =>
            ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

        private static T Safe<T>(Func<T> f) where T : class
        {
            try { return f(); }
            catch (Exception e) when (e is ElementNotAvailableException || e is AutomationException || e is InvalidOperationException) { return null; }
        }

        private static void TryDo(Action a)
        {
            try { a(); }
            catch (Exception e) when (e is ElementNotAvailableException || e is InvalidOperationException) { }
        }

        private static string Safe(Func<string> f)
        {
            try { return f(); }
            catch (Exception e) when (e is ElementNotAvailableException || e is InvalidOperationException) { return "?"; }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        #endregion
    }

    public enum DiagnosticStatus
    {
        /// <summary>Found by the expected (first) lookup.</summary>
        Ok,
        /// <summary>Only found by a fallback lookup: works now, but Power BI's UI has changed.</summary>
        Fallback,
        /// <summary>Not found by any lookup: runs that need it will fail.</summary>
        Missing,
        /// <summary>Only exists during a real run (dialogs), so it can't be checked here.</summary>
        NotChecked
    }

    public sealed class DiagnosticResult
    {
        public DiagnosticResult(string element, DiagnosticStatus status, string detail)
        {
            Element = element;
            Status = status;
            Detail = detail;
        }
        public string Element { get; }
        public DiagnosticStatus Status { get; }
        public string Detail { get; }
    }
}
