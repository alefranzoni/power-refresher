using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace PowerRefresher
{
    public partial class frmMain : Form
    {
        private const string REFRESH_BUTTON = "refreshQueries";
        private const string REFRESH_DIALOG = "modalDialog";
        private const string CANCEL_REFRESH_BUTTON = "Close";
        private const string SAVE_BUTTON = "save";
        private const string SAVE_WAIT_MESSAGE = "Working on it";
        private const string PUBLISH_BUTTON = "publish";
        private const string PUBLISH_GROUP_DIALOG = "KoPublishToGroupDialog";
        private const string PUBLISH_DIALOG = "KoPublishDialog";
        private const string WORKSPACE_CONTAINER = "list";
        private const string SELECT_BUTTON = "Select";
        private const string REPLACE_DIALOG = "KoPublishWithImpactViewDialog";
        private const string REPLACE_BUTTON = "Replace";
        private const string SUCCESS_PUBLISH = "Got it";
        private const string REFRESH_CONTEXTUAL_MENU = "FieldListMenuItem_RefreshEntity";

        private AutomationElement desktop;
        private AutomationElement pbi;
        private AutomationElement treeContainer;
        private AutomationElement treeElementNode;
        private AutomationElement refreshButton;
        private AutomationElement refreshDialog;
        private AutomationElement saveDialog;
        private AutomationElement publishDialog;
        private AutomationElement cancelRefreshButton;
        private AutomationElement saveButton;
        private AutomationElement publishButton;
        private AutomationElement targetWorkspaceElement;
        private AutomationElement replaceDatasetDialog;
        private AutomationElement refreshContextualMenu;

        private WindowPattern windowPattern;
        private InvokePattern invokePattern;
        private SelectionItemPattern selectionItemPattern;

        private int timeout;
        

        public frmMain() => InitializeComponent();
        private void txtOutput_LinkClicked(object sender, LinkClickedEventArgs e) => OpenLink(e.LinkText);
        private void cmdSetInput_Click(object sender, EventArgs e) => BrowseInputFile();
        private void chkRefreshAll_CheckedChanged(object sender, EventArgs e)
        {
            chklModelFields.Enabled = !chkRefreshAll.Checked;
            lblModelFields.Enabled = !chkRefreshAll.Checked;
            if (chklModelFields.CheckedItems.Count == 0) { return; }
            setModelFieldsSelectionState(false);
            
        }
        private void chkPublish_CheckedChanged(object sender, EventArgs e)
        {
            lblWorkspace.Visible = chkPublish.Checked;
            txtWorkspace.Visible = chkPublish.Checked;
        }
        private void txtOutput_TextChanged(object sender, EventArgs e)
        {
            txtOutput.SelectionStart = txtOutput.TextLength;
            txtOutput.ScrollToCaret();
        }
        private void cmdStartRefresh_Click(object sender, EventArgs e)
        {
            //Validations
            if (!CheckRequiredFields()) return;

            //Getting application window pattern to handle its behavior
            GetApplicationWindowPattern();

            try
            {
                //Refresh
                if (chkRefreshAll.Checked) RefreshAll(); else RefreshSelection();
                if (!RefreshSuccess()) throw new Exception("Refresh failed. Please check refresh dialog for details and try again.");

                //Publish
                if (chkPublish.Checked)
                {
                    SaveFile();
                    PublishFile();
                }

                //Save
                SaveFile();

                //Close file
                if (chkCloseFileOnFinish.Checked) CloseFile();
                
                //Success
                txtOutput.Text += "\n\n=== REFRESH FINISHED ===\nThanks for using PowerRefresher!\n";
                txtOutput.Text += "-\nPowerRefresher @ https://github.com/alefranzoni/power-refresher" +
                    "\nAlejandro Franzoni Gimenez @ https://alejandrofranzoni.com.ar \n";
                
                //Close application
                if (chkCloseAppOnFinish.Checked) CloseApplication();

                BringToFront();
                Activate();
            }
            catch (Exception err)
            {
                RefreshFailed(err);
            }
        }

        

        private void selectAllFieldsMenuItem_Click(object sender, EventArgs e) => setModelFieldsSelectionState(true);
        private void clearSelectionMenuItem_Click(object sender, EventArgs e) => setModelFieldsSelectionState(false);
        private void copySelectedMenuItem_Click(object sender, EventArgs e) => SetTextToClipboard(txtOutput.SelectedText);
        private void selectAllTextMenuItem_Click(object sender, EventArgs e) => txtOutput.SelectAll();



        private void RefreshSelection()
        {
            string selectedFields = null;
            int selectedFieldsCounter = 0;
            
            GetSelectedFields(ref selectedFields, ref selectedFieldsCounter);
            GetTreeContainer();
            RefreshSelectedFields(selectedFields, selectedFieldsCounter);
        }
        private void RefreshSelectedFields(string selectedFields, int selectedFieldsCounter)
        {
            int selectedFieldsUpdatedCounter = 0;

            do
            {
                bool fieldSelected = false;

                if (selectedFields.Contains(treeElementNode.Current.Name))
                {
                    txtOutput.Text += $"\n[INFO] FIELD {selectedFieldsUpdatedCounter + 1}/{selectedFieldsCounter} ({treeElementNode.Current.Name})";
                    fieldSelected = true;

                    GetSelectionItemPattern();
                    OpenContextualMenuAndRefresh();
                    LookForRefreshDialog();
                    WaitForRefreshToFinish();

                    if (!RefreshSuccess()) return;

                    selectedFieldsUpdatedCounter += 1;
                    if (selectedFieldsUpdatedCounter == selectedFieldsCounter) return;
                }

                if (fieldSelected)
                {
                    Thread.Sleep(1000);
                    WaitForWorkingDialog();
                }

                treeElementNode = TreeWalker.ControlViewWalker.GetNextSibling(treeElementNode);
            } while (treeElementNode != null);
        }
        private void GetTreeContainer()
        {
            do
            {
                treeContainer = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "cdk-tree"));
                treeElementNode = treeContainer == null ? null : TreeWalker.ControlViewWalker.GetFirstChild(treeContainer);
            } while (treeContainer == null && treeElementNode == null);
        }
        private void GetSelectedFields(ref string selectedFields, ref int selectedFieldsCounter)
        {
            foreach (object checkedItem in chklModelFields.CheckedItems)
            {
                selectedFields += ((selectedFields != null) ? ";" : null) + checkedItem.ToString();
                selectedFieldsCounter += 1;
            }
        }
        private void OpenContextualMenuAndRefresh()
        {
            /**
             * There are two workarounds to do it:
             * 1) Send right-click event to the ClickablePoint of each element node
             * 2) Send the hotkey [Shift+F10] with a previous selected field/table
             **/

            do
            {
                pbi.SetFocus();
                selectionItemPattern.Select();
                Thread.Sleep(100);
                SendKeys.SendWait("+{F10}");
                Thread.Sleep(100);
                refreshContextualMenu = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, REFRESH_CONTEXTUAL_MENU));
            } while (refreshContextualMenu == null);

            invokePattern = (InvokePattern)refreshContextualMenu.GetCurrentPattern(InvokePattern.Pattern);
            invokePattern.Invoke();
        }
        private void GetSelectionItemPattern() => selectionItemPattern = (SelectionItemPattern)treeElementNode.GetCurrentPattern(SelectionItemPattern.Pattern);
        private void RefreshAll()
        {
            PressRefreshButton();
            LookForRefreshDialog();
            WaitForRefreshToFinish();
        }
        private void CloseFile()
        {
            txtOutput.Text += "\nClosing... ";
            pbi.SetFocus();
            windowPattern = (WindowPattern)pbi.GetCurrentPattern(WindowPattern.Pattern);
            windowPattern.Close();
            txtOutput.Text += "[DONE]";
        }
        private void CloseApplication()
        {
            CreateUpdateFileLog();
            Close();
        }
        private void CreateUpdateFileLog()
        {
            string filename = $@"{Environment.GetEnvironmentVariable("TMP")}\PowerRefresher_LogReport_{DateTime.Now:ddmmyyyy_HHmmss}.txt";

            using (StreamWriter streamWriter = File.CreateText(filename))
            {
                streamWriter.WriteLine($"PowerRefresher ({System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}) - Update Information");
                streamWriter.WriteLine($"\nFile: {txtInput.Text}");
                streamWriter.WriteLine($"\n[LOGS]\n{txtOutput.Text}");
            }

            OpenFile(filename);
        }
        private void SaveFile()
        {
            txtOutput.Text += "\nSaving... ";
            do
            {
                saveButton = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, SAVE_BUTTON));
            } while (saveButton == null);
            invokePattern = (InvokePattern)saveButton.GetCurrentPattern(InvokePattern.Pattern);
            invokePattern.Invoke();

            Thread.Sleep(1500);

            WaitForWorkingDialog();

            txtOutput.Text += "[DONE]";
        }
        private void WaitForWorkingDialog()
        {
            do
            {
                Thread.Sleep(200);
                saveDialog = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, SAVE_WAIT_MESSAGE));
            } while (saveDialog != null);
        }
        private void PublishFile()
        {
            try
            {
                txtOutput.Text += "\nPublishing... ";

                //Publish button
                do
                {
                    publishButton = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, PUBLISH_BUTTON));
                } while (publishButton == null);

                invokePattern = (InvokePattern)publishButton.GetCurrentPattern(InvokePattern.Pattern);
                invokePattern.Invoke();

                //Looking for publish dialog
                do
                {
                    publishDialog = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, PUBLISH_GROUP_DIALOG));
                } while (publishDialog == null);

                //Fetching available workspaces
                bool validWorkspace = false;
                string targetWorkspaceInput = txtWorkspace.Text;
                string targetWorkspace = null;
                treeContainer = null;
                treeElementNode = null;

                do
                {
                    treeContainer = publishDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.LocalizedControlTypeProperty, WORKSPACE_CONTAINER));
                } while (treeContainer == null);

                do
                {
                    treeElementNode = TreeWalker.ControlViewWalker.GetFirstChild(treeContainer);
                } while (treeElementNode == null);

                do
                {
                    if (treeElementNode.Current.Name.ToUpper() == targetWorkspaceInput.ToUpper())
                    {
                        validWorkspace = true;
                        targetWorkspace = treeElementNode.Current.Name;
                    }
                    treeElementNode = TreeWalker.ContentViewWalker.GetNextSibling(treeElementNode);
                } while (validWorkspace != true && treeElementNode != null);

                //Checking workspace
                if (!validWorkspace)
                {
                    txtOutput.Text += "[FAILED]";
                    ShowMessage("The workspace is not valid. Check the output for details.", MessageBoxIcon.Error, "Publish error");
                    throw new Exception("Workspace: " + targetWorkspace + " - Not found.\n");
                }

                //Select workspace
                do
                {
                    targetWorkspaceElement = publishDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, targetWorkspace));
                } while (targetWorkspaceElement == null);
                invokePattern = (InvokePattern)targetWorkspaceElement.GetCurrentPattern(InvokePattern.Pattern);
                invokePattern.Invoke();

                //Click on 'select' button to publish
                do
                {
                    publishButton = publishDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, SELECT_BUTTON));

                } while (publishButton == null);
                invokePattern = (InvokePattern)publishButton.GetCurrentPattern(InvokePattern.Pattern);
                invokePattern.Invoke();

                //Check for replace dialog
                timeout = 0;
                do
                {
                    timeout += 1;
                    replaceDatasetDialog = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, REPLACE_DIALOG));
                    Thread.Sleep(1000);
                } while (replaceDatasetDialog == null && timeout <= 5);
                if (replaceDatasetDialog != null)
                {
                    do
                    {
                        publishButton = replaceDatasetDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, REPLACE_BUTTON));
                    } while (publishButton == null);
                    invokePattern = (InvokePattern)publishButton.GetCurrentPattern(InvokePattern.Pattern);
                    invokePattern.Invoke();
                }

                //Look for publish dialog to finish
                do
                {
                    publishDialog = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, PUBLISH_DIALOG));
                } while (publishDialog == null);
                do
                {
                    publishButton = publishDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, SUCCESS_PUBLISH));
                    Thread.Sleep(500);
                } while (publishButton == null);
                invokePattern = (InvokePattern)publishButton.GetCurrentPattern(InvokePattern.Pattern);
                invokePattern.Invoke();

                txtOutput.Text += "[DONE]";
            }
            catch (Exception e)
            {
                ShowMessage("An error has occurred trying to publish your file. Check output for details.", MessageBoxIcon.Error, "Publish error");
                throw new Exception("An error has occurred trying to publish your file. " + e.Message + "\n");
            }
        }
        private void WaitForRefreshToFinish()
        {
            txtOutput.Text += "\nRefresh in progress... ";
            do
            {
                Thread.Sleep(500);
                refreshDialog = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, REFRESH_DIALOG));
                cancelRefreshButton = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.HelpTextProperty, CANCEL_REFRESH_BUTTON));
            } while (refreshDialog != null && cancelRefreshButton == null);
            txtOutput.Text += "[DONE]";
        }
        private void LookForRefreshDialog()
        {
            try
            {
                txtOutput.Text += "\nLooking for refresh dialog... ";
                timeout = 0;
                do
                {
                    timeout += 1;
                    Thread.Sleep(100);
                    refreshDialog = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, REFRESH_DIALOG));
                } while (refreshDialog == null && timeout <= 25);
                txtOutput.Text += "[DONE]";
            }
            catch (Exception e)
            {
                ShowMessage("An error has occurred looking for refresh dialog. Check output for details.", MessageBoxIcon.Error, "Refresh error");
                throw new Exception("An error has occurred looking for refresh dialog. " + e.Message + "\n");
            }
        }
        private void PressRefreshButton()
        {
            try
            {
                txtOutput.Text += "\nTrying to refresh all queries...";
                timeout = 0;
                do
                {
                    timeout += 1;
                    Thread.Sleep(500);
                    refreshButton = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, REFRESH_BUTTON));
                } while (refreshButton == null && timeout <= 4);
                invokePattern = (InvokePattern)refreshButton.GetCurrentPattern(InvokePattern.Pattern);
                invokePattern.Invoke();
            }
            catch (Exception e)
            {
                ShowMessage("An error has occurred trying to invoke refresh button. Check output for details.", MessageBoxIcon.Error, "Refresh error");
                throw new Exception("An error has occurred trying to invoke refresh button. " + e.Message + "\n");
            }
        }
        private void GetApplicationWindowPattern()
        {
            Thread.Sleep(500);
            windowPattern = (WindowPattern)pbi.GetCurrentPattern(WindowPattern.Pattern);
            windowPattern.SetWindowVisualState(WindowVisualState.Maximized);
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
        private void GetFileData(){
            String windowTitleFilename = null;
            txtOutput.Text = null;

            try
            {
                //Open or bind PBIX file  
                OpenOrBindPbixFile(txtInput.Text, ref windowTitleFilename);

                //Set reference to PowerBI application
                SetPowerBIReference(windowTitleFilename);

                //Getting fields/tables from model
                FetchFieldsFromModel();
            }
            catch (Exception e) {
                txtOutput.Text += "[FAILED]\nAn unexpected error has occurred: " + e.Message;
                SetOutputLineColor(txtOutput.Lines.Length - 1, Color.Red);
                ShowMessage("An unexpected error has occurred. Check the output for details.", MessageBoxIcon.Error, "Unexpected error");
            }
        }
        private void FetchFieldsFromModel(){
            chkRefreshAll.Checked = true;
            txtOutput.Text += "\nFetching fields from PowerBI model... ";

            GetTreeContainer();

            chklModelFields.Items.Clear();

            do
            {
                chklModelFields.Items.Add(treeElementNode.Current.Name);
                treeElementNode = TreeWalker.ControlViewWalker.GetNextSibling(treeElementNode);
            } while (treeElementNode != null);

            cmdStartRefresh.Enabled = true;
            txtOutput.Text += "[DONE]";
            BringToFront();
            Activate();
        }
        private void SetPowerBIReference(string windowTitleFilename){
            timeout = 0;
            desktop = AutomationElement.RootElement;
            txtOutput.Text += "\nLooking for for PowerBI Application... ";

            do {
                pbi = desktop.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.NameProperty, windowTitleFilename));
                timeout += 1;
                Thread.Sleep(1000);
            } while (pbi == null && timeout < numericTimeout.Value);

            if (pbi == null) {
                throw new Exception("Failed to find PowerBI Application (timeout)");
            } else {
                txtOutput.Text += "[DONE]\n" +
                    "[INFO] Found PowerBI application in " + timeout + " seconds (can be useful to help you to choose a correct timeout).";
            }
        }
        private void OpenOrBindPbixFile(string filePath, ref string windowTitleFilename){
            txtOutput.Text = "Trying to open or bind PBIX file... ";
            windowTitleFilename = (filePath.Substring(filePath.LastIndexOf("\\") + 1) + " - Power BI Desktop").Replace(".pbix", "");

            //Check if file is already open
            var processList = Process.GetProcesses();
            foreach (Process process in processList) {
                if (process.MainWindowTitle == windowTitleFilename) {
                    txtOutput.Text += "[DONE]"; 
                    return; 
                }
            }

            //Open file
            OpenFile(filePath, processWindowStyle: ProcessWindowStyle.Minimized);
            txtOutput.Text += "[DONE]";
        }
        private void OpenFile(string filePath, bool useShellExec = true, ProcessWindowStyle processWindowStyle = ProcessWindowStyle.Normal)
        {
            var p = new Process();
            p.StartInfo.FileName = filePath;
            p.StartInfo.UseShellExecute = useShellExec;
            p.StartInfo.WindowStyle = processWindowStyle;
            p.Start();
        }
        private void SetOutputLineColor(int line, Color color) {
            txtOutput.Select(txtOutput.GetFirstCharIndexFromLine(line), txtOutput.TextLength);
            txtOutput.SelectionColor = color;
        }
        private void OpenLink(string url)
        {
            var process = new Process();
            process.StartInfo.FileName = url;
            process.StartInfo.UseShellExecute = true;
            process.Start();
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
            if (chkPublish.Checked && (txtWorkspace.Text == "Put your workspace here" || txtWorkspace.Text == ""))
            {
                txtOutput.Text += "\n[WARNING] Workspace name is required to publish your file.";
                SetOutputLineColor(txtOutput.Lines.Length, Color.Yellow);
                ShowMessage("You must set a valid workspace or disable publish option!", MessageBoxIcon.Exclamation, "Workspace missing");
                return false;
            }

            if (!chkRefreshAll.Checked && chklModelFields.CheckedItems.Count == 0)
            {
                txtOutput.Text += "\n[WARNING] At least one field selected is required to refresh";
                SetOutputLineColor(txtOutput.Lines.Length, Color.Yellow);
                ShowMessage("You must select at least one field to refresh!", MessageBoxIcon.Exclamation, "Selection missing");
                return false;
            }
            
            return true;
        }
        private bool RefreshSuccess() => (refreshDialog == null); //Success -> refreshDialog = null | Failure -> cancelRefreshButton != null
        private void setModelFieldsSelectionState(bool checkStatus)
        {
            for (int i = 0; i < chklModelFields.Items.Count; i++) { chklModelFields.SetItemChecked(i, checkStatus); }
        }
        private void SetTextToClipboard(string text)
        {
            if (text != "") Clipboard.SetText(text);
        }

        private void frmMain_Load(object sender, EventArgs e)
        {

            foreach (string command in Environment.GetCommandLineArgs())
            {
                if (!command.Contains(".exe"))
                {
                    if (command.Contains("help")) ShowMessage(command, MessageBoxIcon.Information);
                    if (command.Contains("target")) ShowMessage(command, MessageBoxIcon.Information);
                    //else mostrar error
                }
            }
        }
            
        
    }
}
