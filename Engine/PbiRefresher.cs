using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace PowerRefresher.Engine
{
    /// <summary>
    /// Drives Power BI Desktop through UI Automation: open/bind a .pbix, refresh, publish, save, close.
    /// Has no UI of its own; progress goes to <see cref="Log"/>, failures are thrown as
    /// <see cref="RefreshFailedException"/> or <see cref="AutomationException"/>.
    /// Must be called from an STA thread (SendKeys fallback and UIA prefer it).
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

        public PbiRefresher(RefreshOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>Options are re-read at each step, so a caller (the GUI) may adjust them between Open and Run.</summary>
        public RefreshOptions Options { get; }

        /// <summary>Progress text. Messages are fragments in the style "\nSaving... " followed by "[DONE]".</summary>
        public event Action<string> Log;

        /// <summary>Tables found in the model by the last <see cref="Open"/>.</summary>
        public IReadOnlyList<string> Tables { get; private set; } = new string[0];

        /// <summary>True if <see cref="Open"/> launched Power BI, false if it bound to a window that was already open.</summary>
        public bool OpenedFile { get; private set; }

        /// <summary>Opens the file in Power BI Desktop (or binds to its open window), reads the table list and handles the script visuals prompt.</summary>
        public IReadOnlyList<string> Open()
        {
            ui = PbiStrings.For(Options.Language);
            if (string.IsNullOrWhiteSpace(Options.FilePath)) throw new AutomationException("No .pbix file given.");
            if (!File.Exists(Options.FilePath)) throw new AutomationException($"File not found: {Options.FilePath}");

            string fileBaseName = Path.GetFileNameWithoutExtension(Options.FilePath);
            OpenOrBindPbixFile(fileBaseName);
            FindPowerBIWindow(fileBaseName);
            Tables = FetchTables();
            HandleScriptVisuals();
            return Tables;
        }

        /// <summary>Refreshes, optionally publishes, saves and optionally closes. Call <see cref="Open"/> first.</summary>
        public void Run()
        {
            if (pbi == null) throw new InvalidOperationException("Call Open() before Run().");
            ui = PbiStrings.For(Options.Language);

            MaximizeWindow();

            if (Options.RefreshAll) RefreshAll(); else RefreshTables(ResolveTables());
            if (refreshDialog != null) throw new RefreshFailedException("Refresh failed. Please check the Power BI refresh dialog for details and try again.");

            if (Options.Publish)
            {
                SaveFile();
                PublishFile();
            }

            SaveFile();

            if (Options.CloseFile) Close();
        }

        /// <summary>
        /// Closes the Power BI window and waits until it is gone. Power BI ignores close requests while it is still
        /// starting up, so the request is retried. Unsaved changes make Power BI prompt instead, which ends in an error.
        /// </summary>
        public void Close()
        {
            if (pbi == null) return;
            Write("\nClosing... ");

            IntPtr hwnd;
            try { hwnd = new IntPtr(pbi.Current.NativeWindowHandle); }
            catch (ElementNotAvailableException) { pbi = null; Write("[DONE]"); return; }

            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    ((WindowPattern)pbi.GetCurrentPattern(WindowPattern.Pattern)).Close();
                }
                catch (ElementNotAvailableException) { }

                Stopwatch sw = Stopwatch.StartNew();
                while (IsWindow(hwnd) && sw.Elapsed.TotalSeconds < 5) Thread.Sleep(250);
                if (!IsWindow(hwnd))
                {
                    pbi = null;
                    Write("[DONE]");
                    return;
                }
            }

            Write("[FAILED]");
            throw new AutomationException("Power BI window did not close within 30s (it may be showing a prompt).");
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        private void Write(string text) => Log?.Invoke(text);

        #region Open / bind

        private void OpenOrBindPbixFile(string fileBaseName)
        {
            Write("Trying to open or bind PBIX file... ");

            OpenedFile = false;
            if (Process.GetProcessesByName(PBI_PROCESS_NAME).Any(p => IsPbiWindowTitle(p.MainWindowTitle, fileBaseName)))
            {
                Write("[DONE] (already open)");
                return;
            }

            Process.Start(new ProcessStartInfo(Options.FilePath) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized });
            OpenedFile = true;
            Write("[DONE]");
        }

        private void FindPowerBIWindow(string fileBaseName)
        {
            Write("\nLooking for PowerBI Application... ");
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                pbi = WaitForElement(() => FindPbiWindow(fileBaseName), "the PowerBI Application window", Options.OpenTimeoutSeconds, 1000);
            }
            catch (AutomationException)
            {
                Write("[FAILED]");
                throw new AutomationException($"Failed to find PowerBI Application window for '{fileBaseName}' within {Options.OpenTimeoutSeconds}s. Try a larger timeout.");
            }
            Write($"[DONE]\n[INFO] Found PowerBI application in {(int)sw.Elapsed.TotalSeconds} seconds (can be useful to help you to choose a correct timeout).");
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

        private IReadOnlyList<string> FetchTables()
        {
            Write("\nFetching tables from PowerBI model... ");
            List<string> tables = new List<string>();
            for (AutomationElement node = GetFirstTableNode(); node != null; node = TreeWalker.ControlViewWalker.GetNextSibling(node))
            {
                tables.Add(GetTableName(node));
            }
            Write($"[DONE] ({tables.Count} found)");
            return tables;
        }

        private void HandleScriptVisuals()
        {
            AutomationElement dialog;
            try
            {
                dialog = WaitForElement(() => FindById(pbi, ui.ScriptVisualsDialogId), "the script visuals prompt", 4, 1000);
            }
            catch (AutomationException)
            {
                return; //No prompt: file has no script visuals
            }

            Write("\nScript Visuals detected... ");
            switch (Options.ScriptVisuals)
            {
                case ScriptVisualsAction.Enable:
                    InvokeElement(WaitForElement(() => FindByName(dialog, ui.EnableScriptVisualsButton), $"the '{ui.EnableScriptVisualsButton}' button"));
                    Write("Enabled! [DONE]");
                    break;
                case ScriptVisualsAction.Disable:
                    InvokeElement(WaitForElement(() => FindByName(dialog, ui.CancelScriptVisualsButton), $"the '{ui.CancelScriptVisualsButton}' button"));
                    Write("Disabled [DONE]");
                    break;
                default:
                    Write("[FAILED]");
                    throw new AutomationException("Script visuals detected in the file and the run is not allowed to enable or skip them. Aborting.");
            }
        }

        #endregion

        #region Refresh

        private void MaximizeWindow()
        {
            Thread.Sleep(500);
            ((WindowPattern)pbi.GetCurrentPattern(WindowPattern.Pattern)).SetWindowVisualState(WindowVisualState.Maximized);
        }

        private void RefreshAll()
        {
            Write("\nTrying to refresh all queries...");
            InvokeElement(WaitForElement(FindRefreshButton, "the ribbon Refresh button"));
            LookForRefreshDialog();
            WaitForRefreshToFinish();
        }

        /// <summary>Checks the requested tables exist in the model. Call after <see cref="Open"/>; throws AutomationException listing what is available.</summary>
        public void ValidateTables()
        {
            if (!Options.RefreshAll) ResolveTables();
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

        private void RefreshTables(HashSet<string> tables)
        {
            int done = 0;
            for (AutomationElement node = GetFirstTableNode(); node != null && done < tables.Count; node = TreeWalker.ControlViewWalker.GetNextSibling(node))
            {
                string name = GetTableName(node);
                if (!tables.Contains(name)) continue;

                Write($"\n[INFO] TABLE {done + 1}/{tables.Count} ({name})");
                OpenTableMenuAndRefresh(node, name);
                LookForRefreshDialog();
                WaitForRefreshToFinish();
                if (refreshDialog != null) return;

                done++;
                WaitForWorkingDialog();
            }
        }

        private AutomationElement GetFirstTableNode() =>
            //Field list renders as a tree whose class is exactly "cdk-tree"; wait until it has its first node
            WaitForElement(() =>
            {
                AutomationElement tree = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "cdk-tree"));
                return tree == null ? null : TreeWalker.ControlViewWalker.GetFirstChild(tree);
            }, "the model field list", Math.Max(Options.OpenTimeoutSeconds, ELEMENT_TIMEOUT_SECONDS));

        /// <summary>
        /// Field list nodes are announced as "Table &lt;name&gt;" (localized prefix). The node's expander button
        /// carries the bare table name, so prefer it and fall back to the node name for older versions.
        /// </summary>
        private static string GetTableName(AutomationElement node)
        {
            AutomationElement expander = FindChild(node, c => c.Current.ControlType == ControlType.Button && c.Current.ClassName.Contains("expander") && !string.IsNullOrEmpty(c.Current.Name));
            return expander?.Current.Name ?? node.Current.Name;
        }

        private void OpenTableMenuAndRefresh(AutomationElement node, string name)
        {
            AutomationElement moreOptions = FindChild(node, c => c.Current.ControlType == ControlType.Button && c.Current.ClassName.Contains("menu-button"));
            if (moreOptions != null)
            {
                //Current UI: the node's "More options" (...) button opens the menu without needing keyboard focus
                InvokeElement(moreOptions);
            }
            else
            {
                //Legacy UI: select node and open its context menu by keyboard (needs Power BI focused)
                pbi.SetFocus();
                ((SelectionItemPattern)node.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                SendKeys.SendWait("+{F10}");
            }

            //Older versions: flat "Refresh data" item. Current versions: "Refresh" submenu -> "Data".
            AutomationElement item = WaitForElement(() => FindById(pbi, ui.RefreshMenuItemId) ?? FindRefreshSubmenu(), $"the Refresh menu item for {name}", 10);
            if (item.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object expand))
            {
                ((ExpandCollapsePattern)expand).Expand();
                item = WaitForElement(() => FindById(pbi, ui.RefreshSubmenuDataId), $"the Refresh > Data menu item for {name}", 10);
            }
            InvokeElement(item);
        }

        private AutomationElement FindRefreshSubmenu() =>
            pbi.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                new PropertyCondition(AutomationElement.NameProperty, ui.RefreshButtonName)));

        private AutomationElement FindRefreshButton()
        {
            AutomationElement legacy = FindById(pbi, ui.RefreshButtonId);
            if (legacy != null) return legacy;

            //Current ribbon renders Refresh as a split button; only the primary half supports Invoke
            AutomationElementCollection candidates = pbi.FindAll(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.NameProperty, ui.RefreshButtonName)));
            foreach (AutomationElement candidate in candidates)
            {
                if (candidate.Current.ClassName.Contains("splitPrimaryButton")) return candidate;
            }
            return null;
        }

        private void LookForRefreshDialog()
        {
            Write("\nLooking for refresh dialog... ");
            try
            {
                //A fast refresh can finish before we ever see the dialog, so not finding it is fine
                refreshDialog = WaitForElement(() => FindById(pbi, ui.RefreshDialogId), "the refresh dialog", 2, 100);
            }
            catch (AutomationException)
            {
                refreshDialog = null;
            }
            Write("[DONE]");
        }

        /// <summary>
        /// Success: the refresh dialog closes on its own.
        /// Failure: the dialog stays open showing errors and its Cancel button goes away (only Close remains).
        /// The search is scoped to the dialog: the window title bar also has a "Close" button.
        /// </summary>
        private void WaitForRefreshToFinish()
        {
            Write("\nRefresh in progress... ");
            Stopwatch sw = Stopwatch.StartNew();
            int pollsWithoutCancel = 0;
            while (true)
            {
                Thread.Sleep(500);
                try
                {
                    refreshDialog = FindById(pbi, ui.RefreshDialogId);
                    if (refreshDialog == null) break;

                    bool inProgress = FindByName(refreshDialog, ui.RefreshInProgressButton) != null;
                    pollsWithoutCancel = inProgress ? 0 : pollsWithoutCancel + 1;
                    if (pollsWithoutCancel >= 4) break;
                }
                catch (ElementNotAvailableException)
                {
                    //Dialog closed while being inspected; next poll will confirm
                }

                if (sw.Elapsed.TotalSeconds >= REFRESH_TIMEOUT_SECONDS)
                    throw new AutomationException($"Refresh did not finish within {REFRESH_TIMEOUT_SECONDS / 3600} hours.");
            }
            Write(refreshDialog == null ? "[DONE]" : "[FAILED]");
        }

        #endregion

        #region Save / publish / close

        private void SaveFile()
        {
            Write("\nSaving... ");
            InvokeElement(WaitForElement(() => FindById(pbi, ui.SaveButtonId), "the Save button"));
            Thread.Sleep(1500);
            WaitForWorkingDialog();
            Write("[DONE]");
        }

        private void WaitForWorkingDialog()
        {
            Thread.Sleep(1000);
            WaitForElementGone(() => FindByName(pbi, ui.SaveWaitMessage), $"the '{ui.SaveWaitMessage}' dialog", SAVE_TIMEOUT_SECONDS);
        }

        private void PublishFile()
        {
            Write("\nPublishing... ");
            try
            {
                InvokeElement(WaitForElement(() => FindById(pbi, ui.PublishButtonId), "the Publish button"));

                AutomationElement publishDialog = WaitForElement(() => FindById(pbi, ui.PublishGroupDialogId), "the publish destination dialog");
                AutomationElement list = WaitForElement(() => publishDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.LocalizedControlTypeProperty, ui.WorkspaceContainerType)), "the workspace list");

                string targetWorkspace = null;
                for (AutomationElement item = WaitForElement(() => TreeWalker.ControlViewWalker.GetFirstChild(list), "the workspace list items");
                     item != null && targetWorkspace == null;
                     item = TreeWalker.ContentViewWalker.GetNextSibling(item))
                {
                    if (string.Equals(item.Current.Name, Options.Workspace, StringComparison.OrdinalIgnoreCase)) targetWorkspace = item.Current.Name;
                }
                if (targetWorkspace == null) throw new AutomationException($"Workspace '{Options.Workspace}' not found in the publish dialog.");

                InvokeElement(WaitForElement(() => FindByName(publishDialog, targetWorkspace), $"workspace '{targetWorkspace}'"));
                InvokeElement(WaitForElement(() => FindByName(publishDialog, ui.SelectButton), $"the '{ui.SelectButton}' button"));

                //Replace dialog only appears when the dataset already exists in the workspace
                AutomationElement replaceDialog = null;
                try { replaceDialog = WaitForElement(() => FindById(pbi, ui.ReplaceDialogId), "the replace dataset dialog", 5, 1000); }
                catch (AutomationException) { }
                if (replaceDialog != null)
                {
                    InvokeElement(WaitForElement(() => FindByName(replaceDialog, ui.ReplaceButton), $"the '{ui.ReplaceButton}' button"));
                }

                //Upload can take a while on large models
                AutomationElement resultDialog = WaitForElement(() => FindById(pbi, ui.PublishDialogId), "the publishing progress dialog");
                InvokeElement(WaitForElement(() => FindByName(resultDialog, ui.SuccessPublishButton), $"the '{ui.SuccessPublishButton}' button (publish result)", SAVE_TIMEOUT_SECONDS));

                Write("[DONE]");
            }
            catch (Exception e) when (!(e is RefreshFailedException))
            {
                Write("[FAILED]");
                throw new AutomationException("An error has occurred trying to publish your file. " + e.Message, e);
            }
        }

        #endregion

        #region UIA helpers

        /// <summary>Polls <paramref name="find"/> until it returns an element, or throws AutomationException.</summary>
        private static AutomationElement WaitForElement(Func<AutomationElement> find, string description, int timeoutSeconds = ELEMENT_TIMEOUT_SECONDS, int pollMs = 500)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                AutomationElement element = null;
                try { element = find(); } catch (ElementNotAvailableException) { }
                if (element != null) return element;
                if (sw.Elapsed.TotalSeconds >= timeoutSeconds)
                    throw new AutomationException($"Timed out after {timeoutSeconds}s waiting for {description}.");
                Thread.Sleep(pollMs);
            }
        }

        /// <summary>Polls until <paramref name="find"/> returns null, or throws AutomationException.</summary>
        private static void WaitForElementGone(Func<AutomationElement> find, string description, int timeoutSeconds, int pollMs = 1000)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                AutomationElement element = null;
                try { element = find(); } catch (ElementNotAvailableException) { }
                if (element == null) return;
                if (sw.Elapsed.TotalSeconds >= timeoutSeconds)
                    throw new AutomationException($"Timed out after {timeoutSeconds}s waiting for {description} to close.");
                Thread.Sleep(pollMs);
            }
        }

        private static AutomationElement FindById(AutomationElement root, string automationId) =>
            root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

        private static AutomationElement FindByName(AutomationElement root, string name) =>
            root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name));

        private static AutomationElement FindChild(AutomationElement parent, Func<AutomationElement, bool> predicate)
        {
            for (AutomationElement child = TreeWalker.ControlViewWalker.GetFirstChild(parent); child != null; child = TreeWalker.ControlViewWalker.GetNextSibling(child))
            {
                if (predicate(child)) return child;
            }
            return null;
        }

        private static void InvokeElement(AutomationElement element) =>
            ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

        #endregion
    }
}
