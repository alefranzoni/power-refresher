﻿using System;
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
        //Default Control Strings : English
        private string REFRESH_BUTTON = "refreshQueries";
        private string REFRESH_DIALOG = "modalDialog";
        private string CANCEL_REFRESH_BUTTON = "Close";
        private string REFRESH_CONTEXTUAL_MENU = "FieldListMenuItem_RefreshEntity";
        private string SAVE_BUTTON = "save";
        private string SAVE_WAIT_MESSAGE = "Working on it";
        private string PUBLISH_BUTTON = "publish";
        private string PUBLISH_GROUP_DIALOG = "KoPublishToGroupDialog";
        private string PUBLISH_DIALOG = "KoPublishDialog";
        private string WORKSPACE_CONTAINER = "list";
        private string SELECT_BUTTON = "Select";
        private string REPLACE_DIALOG = "KoPublishWithImpactViewDialog";
        private string REPLACE_BUTTON = "Replace";
        private string SUCCESS_PUBLISH = "Got it";
        private string SCRIPT_VISUALS_DIALOG = "MessageDialog";
        private string ENABLE_SCRIPT_VISUALS_BUTTON = "Enable";
        private string CANCEL_SCRIPT_VISUALS_BUTTON = "Cancel";


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
        private AutomationElement enableScriptVisualsDialog;   
        private AutomationElement enableScriptVisualsButton;   
        private AutomationElement cancelScriptVisualsButton;

        private WindowPattern windowPattern;
        private InvokePattern invokePattern;
        private SelectionItemPattern selectionItemPattern;

        private int timeout;
        private string targetCmd, refreshModeCmd, fieldsCmd, workspaceNameCmd, pbiLangCmd;
        private bool enableScriptVisualsCmd, continueRefreshCmd, publishCmd, closeFileCmd, closeAppCmd, userArgsPassed;
        private int timeoutCmd;
        private Stopwatch stopWatch;

        public frmMain() => InitializeComponent();

        private void frmMain_Load(object sender, EventArgs e)
        {
            userArgsPassed = false;
            string[] args = Environment.GetCommandLineArgs();

            if (args.Length == 1) return;

            if ((args.Length < 12 && args.Length > 1) || (args.Length > 12 && args.Length > 1) || args[1].Contains("help") || !IsValidArgs() || !GetAndStoreArguments())
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
                SetPbiControlStringsByLang(englishAppLang.Checked ? "en" : "es");
                if (GetFileData() == false) return;
                Thread.Sleep(1500);
                cmdStartRefresh.PerformClick();
            }
        }

        private void cmdSetInput_Click(object sender, EventArgs e) => BrowseInputFile();
        private void cmdStartRefresh_Click(object sender, EventArgs e)
        {
            //Validations
            if (!CheckRequiredFields()) return;

            //Getting application window pattern to handle its behavior
            GetApplicationWindowPattern();

            try
            {
                stopWatch = new Stopwatch();
                stopWatch.Start();
                
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
                stopWatch.Stop();
                TimeSpan t = TimeSpan.FromMilliseconds(stopWatch.ElapsedMilliseconds);
                string timeElapsed = string.Format("{0:D1} hours {1:D1} minutes {2:D1} secs",
                            t.Hours,
                            t.Minutes,
                            t.Seconds);


                txtOutput.Text += $"\n\n=== REFRESH FINISHED ===\nTime elapsed: {timeElapsed}\nThanks for using PowerRefresher!\n";
                txtOutput.Text += "-\nPowerRefresher @ https://github.com/alefranzoni/power-refresher" +
                    "\nAlejandro Franzoni Gimenez @ https://alejandrofranzoni.com.ar \n";

                //Close application
                if (chkCloseAppOnFinish.Checked) CloseApplication();

                BringToFront();
                Activate();
            }
            catch (Exception err)
            {
                stopWatch.Stop();
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
        private void txtOutput_LinkClicked(object sender, LinkClickedEventArgs e) => OpenLink(e.LinkText);
        private void chkRefreshAll_CheckedChanged(object sender, EventArgs e)
        {
            chklModelFields.Enabled = !chkRefreshAll.Checked;
            lblModelFields.Enabled = !chkRefreshAll.Checked;
            if (chklModelFields.CheckedItems.Count == 0) { return; }
            SetModelFieldsSelectionState(false);
            
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
        private void englishAppLang_CheckedChanged(object sender, EventArgs e) => SetPbiControlStringsByLang(englishAppLang.Checked ? "en" : "es");
        private void spanishAppLang_CheckedChanged(object sender, EventArgs e) => SetPbiControlStringsByLang(englishAppLang.Checked ? "en" : "es");

        private void selectAllFieldsMenuItem_Click(object sender, EventArgs e) => SetModelFieldsSelectionState(true);
        private void clearSelectionMenuItem_Click(object sender, EventArgs e) => SetModelFieldsSelectionState(false);
        private void copySelectedMenuItem_Click(object sender, EventArgs e) => SetTextToClipboard(txtOutput.SelectedText);
        private void selectAllTextMenuItem_Click(object sender, EventArgs e) => txtOutput.SelectAll();

        private void RefreshAll()
        {
            PressModelRefreshButton();
            LookForRefreshDialog();
            WaitForRefreshToFinish();
        }
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

                    OpenContextualMenuAndRefresh();
                    LookForRefreshDialog();
                    WaitForRefreshToFinish();

                    if (!RefreshSuccess()) return;

                    selectedFieldsUpdatedCounter += 1;
                    if (selectedFieldsUpdatedCounter == selectedFieldsCounter) return;
                }

                if (fieldSelected)
                {
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
        private void GetSelectionItemPattern() => selectionItemPattern = (SelectionItemPattern)treeElementNode.GetCurrentPattern(SelectionItemPattern.Pattern);
        private void GetApplicationWindowPattern()
        {
            Thread.Sleep(500);
            windowPattern = (WindowPattern)pbi.GetCurrentPattern(WindowPattern.Pattern);
            windowPattern.SetWindowVisualState(WindowVisualState.Maximized);
        }
        private bool GetFileData()
        {
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

                //Selecting field if needed
                if (refreshModeCmd == "fields") SetModelFieldsFromArgs();

                //Enable script visual if needed
                checkScriptVisuals();

                return true;
                //do
                //{
                //    Thread.Sleep(500);
                //    refreshDialog = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, REFRESH_DIALOG));
                //    cancelRefreshButton = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.HelpTextProperty, CANCEL_REFRESH_BUTTON));
                //} while (refreshDialog != null && cancelRefreshButton == null);

            }
            catch (Exception e)
            {
                txtOutput.Text += "[FAILED]\nAn unexpected error has occurred: " + e.Message;
                SetOutputLineColor(txtOutput.Lines.Length - 1, Color.Red);
                return false;
            }
        }

        private void checkScriptVisuals()
        {
            timeout = 0;
            do
            {
                timeout += 1;
                Thread.Sleep(1000);
                enableScriptVisualsDialog = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, SCRIPT_VISUALS_DIALOG));
            } while (enableScriptVisualsDialog == null && timeout < 4);

            if (enableScriptVisualsDialog != null)
            {
                txtOutput.Text += "\nScript Visuals detected... ";

                if (chkEnableScriptVisuals.Checked)
                {
                    enableScriptVisualsButton = enableScriptVisualsDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, ENABLE_SCRIPT_VISUALS_BUTTON));
                    invokePattern = (InvokePattern)enableScriptVisualsButton.GetCurrentPattern(InvokePattern.Pattern);
                    invokePattern.Invoke();

                    txtOutput.Text += "Enabled! [DONE]";
                } else {
                    if (!chkContinueRefresh.Checked)
                    {
                        throw new Exception("Visual scripts detected in the file. The application is not authorized to enable them, the process will be aborted.");
                    } else
                    {
                        cancelScriptVisualsButton = enableScriptVisualsDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, CANCEL_SCRIPT_VISUALS_BUTTON));
                        invokePattern = (InvokePattern)cancelScriptVisualsButton.GetCurrentPattern(InvokePattern.Pattern);
                        invokePattern.Invoke();
                        txtOutput.Text += "Disabled [DONE]";
                    }
                }
            }

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
                        timeoutCmd = (int.Parse(args[i].Replace("-timeout=", null)) > 600) ? 600 : int.Parse(args[i].Replace("-timeout=", null));
                        break;
                    case 3:
                        if (args[i].Replace("-refresh_mode=", null).ToLower() != "all" && args[i].Replace("-refresh_mode=", null).ToLower() != "fields") return false;
                        refreshModeCmd = args[i].Replace("-refresh_mode=", null).ToLower();
                        break;
                    case 4:
                        if (refreshModeCmd == "fields" && args[i].Replace("-fields=", null) == string.Empty) return false;
                        fieldsCmd = args[i].Replace("-fields=", null);
                        break;
                    case 5:
                        if (args[i].Replace("-publish=", null).ToLower() != "false" && args[i].Replace("-publish=", null).ToLower() != "true") return false;
                        publishCmd = args[i].Replace("-publish=", null).ToLower() == "true";
                        break;
                    case 6:
                        if (publishCmd && args[i] == null) return false;
                        workspaceNameCmd = publishCmd ? args[i].Replace("-workspace=", null) : null;
                        break;
                    case 7:
                        if (args[i].Replace("-enable_script_visuals=", null).ToLower() != "false" && args[i].Replace("-enable_script_visuals=", null).ToLower() != "true") return false;
                        enableScriptVisualsCmd = args[i].Replace("-enable_script_visuals=", null).ToLower() == "true";
                        break;
                    case 8:
                        if (args[i].Replace("-sv_force_refresh=", null).ToLower() != "false" && args[i].Replace("-sv_force_refresh=", null).ToLower() != "true") return false;
                        continueRefreshCmd = args[i].Replace("-sv_force_refresh=", null).ToLower() == "true";
                        break;
                    case 9:
                        if (args[i].Replace("-closefile=", null).ToLower() != "false" && args[i].Replace("-closefile=", null).ToLower() != "true") return false;
                        closeFileCmd = args[i].Replace("-closefile=", null).ToLower() == "true";
                        break;
                    case 10:
                        if (args[i].Replace("-closeapp=", null).ToLower() != "false" && args[i].Replace("-closeapp=", null).ToLower() != "true") return false;
                        closeAppCmd = args[i].Replace("-closeapp=", null).ToLower() == "true";
                        break;
                    case 11:
                        if (args[i].Replace("-pbi_lang=", null).ToLower() != "en" && args[i].Replace("-pbi_lang=", null).ToLower() != "es") return false;
                        pbiLangCmd = args[i].Replace("-pbi_lang=", null).ToLower();
                        break;
                }
            }
            return true;
        }
        private string GetSelectedFieldsForScript()
        {
            string fields = "[";
            foreach (var item in chklModelFields.CheckedItems)
            {
                fields += (fields.Length > 1 ? "," : "") + item.ToString();
            }
            return fields += "]";
        }
        
        private void SetPbiControlStringsByLang(string lang)
        {
            switch (lang.ToLower())
            {
                case "es":
                    REFRESH_BUTTON = "refreshQueries";
                    REFRESH_DIALOG = "modalDialog";
                    CANCEL_REFRESH_BUTTON = "Cerrar";
                    SAVE_BUTTON = "save";
                    SAVE_WAIT_MESSAGE = "En proceso";
                    PUBLISH_BUTTON = "publish";
                    PUBLISH_GROUP_DIALOG = "KoPublishToGroupDialog";
                    PUBLISH_DIALOG = "KoPublishDialog";
                    WORKSPACE_CONTAINER = "list";
                    SELECT_BUTTON = "Seleccionar";
                    REPLACE_DIALOG = "KoPublishWithImpactViewDialog";
                    REPLACE_BUTTON = "Reemplazar";
                    SUCCESS_PUBLISH = "Entendido";
                    REFRESH_CONTEXTUAL_MENU = "FieldListMenuItem_RefreshEntity";
                    SCRIPT_VISUALS_DIALOG = "MessageDialog";
                    ENABLE_SCRIPT_VISUALS_BUTTON = "Habilitar";
                    CANCEL_SCRIPT_VISUALS_BUTTON = "Cancelar";
                    break;
                case "en":
                    REFRESH_BUTTON = "refreshQueries";
                    REFRESH_DIALOG = "modalDialog";
                    CANCEL_REFRESH_BUTTON = "Close";
                    SAVE_BUTTON = "save";
                    SAVE_WAIT_MESSAGE = "Working on it";
                    PUBLISH_BUTTON = "publish";
                    PUBLISH_GROUP_DIALOG = "KoPublishToGroupDialog";
                    PUBLISH_DIALOG = "KoPublishDialog";
                    WORKSPACE_CONTAINER = "list";
                    SELECT_BUTTON = "Select";
                    REPLACE_DIALOG = "KoPublishWithImpactViewDialog";
                    REPLACE_BUTTON = "Replace";
                    SUCCESS_PUBLISH = "Got it";
                    REFRESH_CONTEXTUAL_MENU = "FieldListMenuItem_RefreshEntity";
                    SCRIPT_VISUALS_DIALOG = "MessageDialog";
                    ENABLE_SCRIPT_VISUALS_BUTTON = "Enable";
                    CANCEL_SCRIPT_VISUALS_BUTTON = "Cancel";
                    break;
                default:
                    break;
            }
        }
        private void SetModelFieldsFromArgs()
        {
            string[] fieldsFromArgs = fieldsCmd.Replace("[", null).Replace("]", null).Split(',');

            foreach (string field in fieldsFromArgs)
            {
                for (int i = 0; i < chklModelFields.Items.Count; i++)
                {
                    if (chklModelFields.Items[i].ToString().ToLower() == field.ToLower()) chklModelFields.SetItemChecked(i, true);
                }
            }

            if (chklModelFields.CheckedItems.Count == 0) throw new Exception("Model fields passed by argument was not found.");
        }
        private void SetPowerBIReference(string windowTitleFilename)
        {
            timeout = 0;
            desktop = AutomationElement.RootElement;
            txtOutput.Text += "\nLooking for for PowerBI Application... ";

            do
            {
                pbi = desktop.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.NameProperty, windowTitleFilename));
                timeout += 1;
                Thread.Sleep(1000);
            } while (pbi == null && timeout < numericTimeout.Value);

            if (pbi == null)
            {
                throw new Exception("Failed to find PowerBI Application (timeout)");
            }
            else
            {
                txtOutput.Text += "[DONE]\n" +
                    "[INFO] Found PowerBI application in " + timeout + " seconds (can be useful to help you to choose a correct timeout).";
            }
        }
        private void SetOutputLineColor(int line, Color color)
        {
            txtOutput.Select(txtOutput.GetFirstCharIndexFromLine(line), txtOutput.TextLength);
            txtOutput.SelectionColor = color;
        }
        private void SetFormValues()
        {
            txtInput.Text = targetCmd;
            englishAppLang.Checked = pbiLangCmd == "en";
            spanishAppLang.Checked = pbiLangCmd == "es";
            SetPbiControlStringsByLang(pbiLangCmd);
            numericTimeout.Value = timeoutCmd;
            chkRefreshAll.Checked = refreshModeCmd == "all";
            chkEnableScriptVisuals.Checked = enableScriptVisualsCmd;
            chkContinueRefresh.Checked = continueRefreshCmd;
            chkPublish.Checked = publishCmd;
            if (publishCmd) txtWorkspace.Text = workspaceNameCmd;
            chkCloseFileOnFinish.Checked = closeFileCmd;
            chkCloseAppOnFinish.Checked = closeAppCmd;
        }
        private void SetModelFieldsSelectionState(bool checkStatus)
        {
            for (int i = 0; i < chklModelFields.Items.Count; i++) { chklModelFields.SetItemChecked(i, checkStatus); }
        }
        private void SetTextToClipboard(string text)
        {
            if (text != "") Clipboard.SetText(text);
        }

        private void GenerateScript()
        {
            string commandLineScript = $"\"{Process.GetCurrentProcess().MainModule.FileName}\" -target=\"{txtInput.Text}\" ";
            commandLineScript += $"-timeout={numericTimeout.Value} ";
            commandLineScript += $"-refresh_mode={(chkRefreshAll.Checked ? "all" : "fields")} ";
            commandLineScript += $"-fields=\"{(chkRefreshAll.Checked ? null : GetSelectedFieldsForScript())}\" ";
            commandLineScript += $"-publish={chkPublish.Checked} ";
            commandLineScript += $"-workspace=\"{(chkPublish.Checked ? txtWorkspace.Text : null)}\" ";
            commandLineScript += $"-enable_script_visuals={chkEnableScriptVisuals.Checked} ";
            commandLineScript += $"-sv_force_refresh={chkContinueRefresh.Checked} ";
            commandLineScript += $"-closefile={chkCloseFileOnFinish.Checked} ";
            commandLineScript += $"-closeapp={chkCloseAppOnFinish.Checked} ";
            commandLineScript += $"-pbi_lang={(englishAppLang.Checked ? "en" : "es")}";

            SetTextToClipboard(commandLineScript);
            txtOutput.Text += "\n[INFO] Script was generated successfully and copied to clipboard.";

            if (MessageBox.Show("Script was generated successfully and copied to clipboard!\n\nDo you want to save it into a batch file?",
                "PowerRefresher: Script Generator", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                CreateBatchFile(commandLineScript);
            }
        }
        private void CreateBatchFile(string commandLineScript)
        {
            string filename = $@"{AppContext.BaseDirectory}\PowerRefresher_Script_{DateTime.Now:ddmmyyyy_HHmmss}.bat";

            using (StreamWriter streamWriter = File.CreateText(filename))
            {
                streamWriter.WriteLine($@"@echo off");
                streamWriter.WriteLine($@"@REM Script auto-generated by PowerRefresher");
                streamWriter.WriteLine($@"@REM PowerRefresher @ https://github.com/alefranzoni/power-refresher");
                streamWriter.WriteLine($@"@REM Alejandro Franzoni Gimenez @ https://alejandrofranzoni.com.ar");
                streamWriter.WriteLine($"\necho PowerRefresher @ Alejandro Franzoni Gimenez");
                streamWriter.WriteLine($"echo https://github.com/alefranzoni/power-refresher");
                streamWriter.WriteLine($"echo https://alejandrofranzoni.com.ar");
                streamWriter.WriteLine($"echo.");
                streamWriter.WriteLine($"echo Running, please wait...");
                streamWriter.WriteLine($"echo.");
                streamWriter.WriteLine($"\n{commandLineScript}");
            }

            txtOutput.Text += "\n[INFO] Script was saved successfully into a batch file on application directory.";
            ShowMessage("Script saved successfully!", MessageBoxIcon.Information, "Script Generator");
            OpenFile(AppContext.BaseDirectory);
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
        private void OpenContextualMenuAndRefresh()
        {
            /**
             * There are two workarounds to do it:
             * 1) Send right-click event to the ClickablePoint of each element node
             * 2) Send the hotkey [Shift+F10] with a previous selected field/table
             **/

            GetSelectionItemPattern();
            do
            {
                //pbi.SetFocus();
                selectionItemPattern.Select();
                selectionItemPattern.Select();
                selectionItemPattern.Select();
                SendKeys.Send("+{F10}");
                Thread.Sleep(300);
                refreshContextualMenu = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, REFRESH_CONTEXTUAL_MENU));
            } while (refreshContextualMenu == null);

            invokePattern = (InvokePattern)refreshContextualMenu.GetCurrentPattern(InvokePattern.Pattern);
            invokePattern.Invoke();
        }
        private void OpenOrBindPbixFile(string filePath, ref string windowTitleFilename)
        {
            txtOutput.Text = "Trying to open or bind PBIX file... ";
            windowTitleFilename = (filePath.Substring(filePath.LastIndexOf("\\") + 1) + " - Power BI Desktop").Replace(".pbix", "");

            //Check if file is already open
            var processList = Process.GetProcesses();
            foreach (Process process in processList)
            {
                if (process.MainWindowTitle == windowTitleFilename)
                {
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
        private void OpenLink(string url)
        {
            var process = new Process();
            process.StartInfo.FileName = url;
            process.StartInfo.UseShellExecute = true;
            process.Start();
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
        private void WaitForWorkingDialog()
        {
            do
            {
                Thread.Sleep(1000);
                saveDialog = pbi.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, SAVE_WAIT_MESSAGE));
            } while (saveDialog != null);
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

        private void chkEnableScriptVisuals_CheckedChanged(object sender, EventArgs e)
        {
            chkContinueRefresh.Enabled = !chkEnableScriptVisuals.Checked;
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
                } while (refreshDialog == null && timeout <= 15);
                txtOutput.Text += "[DONE]";
            }
            catch (Exception e)
            {
                ShowMessage("An error has occurred looking for refresh dialog. Check output for details.", MessageBoxIcon.Error, "Refresh error");
                throw new Exception("An error has occurred looking for refresh dialog. " + e.Message + "\n");
            }
        }
        private void PressModelRefreshButton()
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

        private void FetchFieldsFromModel()
        {
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
        private bool IsValidArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < 11; i++)
            {
                switch (i)
                {
                    case 1:
                        if (!args[i].Contains("target")) return false;
                        break;
                    case 2:
                        if (!args[i].Contains("timeout")) return false;
                        break;
                    case 3:
                        if (!args[i].Contains("refresh_mode")) return false;
                        break;
                    case 4:
                        if (!args[i].Contains("fields")) return false;
                        break;
                    case 5:
                        if (!args[i].Contains("publish")) return false;
                        break;
                    case 6:
                        if (!args[i].Contains("workspace")) return false;
                        break;
                    case 7:
                        if (!args[i].Contains("enable_script_visuals")) return false;
                        break;
                    case 8:
                        if (!args[i].Contains("sv_force_refresh")) return false;
                        break;
                    case 9:
                        if (!args[i].Contains("closefile")) return false;
                        break;
                    case 10:
                        if (!args[i].Contains("closeapp")) return false;
                        break;
                    case 11:
                        if (!args[i].Contains("pbi_lang")) return false;
                        break;
                }
            }
            return true;
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
        private bool RefreshSuccess() => (refreshDialog == null); //Success -> refreshDialog = null | Failure -> cancelRefreshButton != null
    }
}
