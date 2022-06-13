
namespace PowerRefresher
{
    partial class frmMain
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmMain));
            this.lblTitle = new System.Windows.Forms.Label();
            this.gbInput = new System.Windows.Forms.GroupBox();
            this.numericTimeout = new System.Windows.Forms.NumericUpDown();
            this.lblTimeout = new System.Windows.Forms.Label();
            this.cmdSetInput = new System.Windows.Forms.Button();
            this.txtInput = new System.Windows.Forms.TextBox();
            this.gbOptions = new System.Windows.Forms.GroupBox();
            this.txtWorkspace = new System.Windows.Forms.TextBox();
            this.chkCloseAppOnFinish = new System.Windows.Forms.CheckBox();
            this.lblWorkspace = new System.Windows.Forms.Label();
            this.chklModelFields = new System.Windows.Forms.CheckedListBox();
            this.modelFieldsContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.selectAllFieldsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.clearSelectionMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.lblModelFields = new System.Windows.Forms.Label();
            this.chkCloseFileOnFinish = new System.Windows.Forms.CheckBox();
            this.chkPublish = new System.Windows.Forms.CheckBox();
            this.chkRefreshAll = new System.Windows.Forms.CheckBox();
            this.grOutput = new System.Windows.Forms.GroupBox();
            this.txtOutput = new System.Windows.Forms.RichTextBox();
            this.richTextBoxContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.copySelectedMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.selectAllTextMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.cmdStartRefresh = new System.Windows.Forms.Button();
            this.helpToolTip = new System.Windows.Forms.ToolTip(this.components);
            this.cmdGenerateScript = new System.Windows.Forms.Button();
            this.gbInput.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericTimeout)).BeginInit();
            this.gbOptions.SuspendLayout();
            this.modelFieldsContextMenu.SuspendLayout();
            this.grOutput.SuspendLayout();
            this.richTextBoxContextMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI Semibold", 24F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblTitle.Location = new System.Drawing.Point(4, 4);
            this.lblTitle.Margin = new System.Windows.Forms.Padding(0);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(248, 45);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "PowerRefresher";
            // 
            // gbInput
            // 
            this.gbInput.Controls.Add(this.numericTimeout);
            this.gbInput.Controls.Add(this.lblTimeout);
            this.gbInput.Controls.Add(this.cmdSetInput);
            this.gbInput.Controls.Add(this.txtInput);
            this.gbInput.Location = new System.Drawing.Point(12, 52);
            this.gbInput.Name = "gbInput";
            this.gbInput.Size = new System.Drawing.Size(442, 59);
            this.gbInput.TabIndex = 1;
            this.gbInput.TabStop = false;
            this.gbInput.Text = "Input";
            // 
            // numericTimeout
            // 
            this.numericTimeout.Location = new System.Drawing.Point(391, 22);
            this.numericTimeout.Maximum = new decimal(new int[] {
            600,
            0,
            0,
            0});
            this.numericTimeout.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.numericTimeout.Name = "numericTimeout";
            this.numericTimeout.Size = new System.Drawing.Size(42, 22);
            this.numericTimeout.TabIndex = 0;
            this.helpToolTip.SetToolTip(this.numericTimeout, "Waiting time (in seconds) for file to be opened");
            this.numericTimeout.Value = new decimal(new int[] {
            180,
            0,
            0,
            0});
            // 
            // lblTimeout
            // 
            this.lblTimeout.AutoSize = true;
            this.lblTimeout.Location = new System.Drawing.Point(309, 26);
            this.lblTimeout.Name = "lblTimeout";
            this.lblTimeout.Size = new System.Drawing.Size(79, 13);
            this.lblTimeout.TabIndex = 2;
            this.lblTimeout.Text = "Timeout (secs)";
            this.helpToolTip.SetToolTip(this.lblTimeout, "Waiting time (in seconds) for file to be opened");
            // 
            // cmdSetInput
            // 
            this.cmdSetInput.Location = new System.Drawing.Point(207, 21);
            this.cmdSetInput.Name = "cmdSetInput";
            this.cmdSetInput.Size = new System.Drawing.Size(92, 23);
            this.cmdSetInput.TabIndex = 1;
            this.cmdSetInput.Text = "Browse/Set";
            this.helpToolTip.SetToolTip(this.cmdSetInput, "Select your *.pbix file");
            this.cmdSetInput.UseVisualStyleBackColor = true;
            this.cmdSetInput.Click += new System.EventHandler(this.cmdSetInput_Click);
            // 
            // txtInput
            // 
            this.txtInput.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtInput.ForeColor = System.Drawing.SystemColors.HotTrack;
            this.txtInput.Location = new System.Drawing.Point(9, 21);
            this.txtInput.Name = "txtInput";
            this.txtInput.ReadOnly = true;
            this.txtInput.Size = new System.Drawing.Size(192, 23);
            this.txtInput.TabIndex = 0;
            this.helpToolTip.SetToolTip(this.txtInput, "Select your *.pbix file");
            // 
            // gbOptions
            // 
            this.gbOptions.Controls.Add(this.txtWorkspace);
            this.gbOptions.Controls.Add(this.chkCloseAppOnFinish);
            this.gbOptions.Controls.Add(this.lblWorkspace);
            this.gbOptions.Controls.Add(this.chklModelFields);
            this.gbOptions.Controls.Add(this.lblModelFields);
            this.gbOptions.Controls.Add(this.chkCloseFileOnFinish);
            this.gbOptions.Controls.Add(this.chkPublish);
            this.gbOptions.Controls.Add(this.chkRefreshAll);
            this.gbOptions.Location = new System.Drawing.Point(12, 117);
            this.gbOptions.Name = "gbOptions";
            this.gbOptions.Size = new System.Drawing.Size(442, 236);
            this.gbOptions.TabIndex = 2;
            this.gbOptions.TabStop = false;
            this.gbOptions.Text = "Options";
            // 
            // txtWorkspace
            // 
            this.txtWorkspace.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.txtWorkspace.ForeColor = System.Drawing.SystemColors.HotTrack;
            this.txtWorkspace.Location = new System.Drawing.Point(192, 165);
            this.txtWorkspace.Name = "txtWorkspace";
            this.txtWorkspace.Size = new System.Drawing.Size(241, 23);
            this.txtWorkspace.TabIndex = 4;
            this.txtWorkspace.Text = "Put your workspace here";
            this.helpToolTip.SetToolTip(this.txtWorkspace, "Type the name of the target workspace to publish your file");
            this.txtWorkspace.Visible = false;
            // 
            // chkCloseAppOnFinish
            // 
            this.chkCloseAppOnFinish.AutoSize = true;
            this.chkCloseAppOnFinish.Checked = true;
            this.chkCloseAppOnFinish.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkCloseAppOnFinish.Location = new System.Drawing.Point(9, 214);
            this.chkCloseAppOnFinish.Name = "chkCloseAppOnFinish";
            this.chkCloseAppOnFinish.Size = new System.Drawing.Size(164, 17);
            this.chkCloseAppOnFinish.TabIndex = 0;
            this.chkCloseAppOnFinish.Text = "Close application on finish";
            this.helpToolTip.SetToolTip(this.chkCloseAppOnFinish, "Close PowerRefresher application on finish");
            this.chkCloseAppOnFinish.UseVisualStyleBackColor = true;
            // 
            // lblWorkspace
            // 
            this.lblWorkspace.AutoSize = true;
            this.lblWorkspace.Location = new System.Drawing.Point(92, 169);
            this.lblWorkspace.Name = "lblWorkspace";
            this.lblWorkspace.Size = new System.Drawing.Size(98, 13);
            this.lblWorkspace.TabIndex = 3;
            this.lblWorkspace.Text = "Workspace name:";
            this.helpToolTip.SetToolTip(this.lblWorkspace, "Type the name of the target workspace to publish your file");
            this.lblWorkspace.Visible = false;
            // 
            // chklModelFields
            // 
            this.chklModelFields.CheckOnClick = true;
            this.chklModelFields.ColumnWidth = 280;
            this.chklModelFields.ContextMenuStrip = this.modelFieldsContextMenu;
            this.chklModelFields.Enabled = false;
            this.chklModelFields.FormattingEnabled = true;
            this.chklModelFields.Location = new System.Drawing.Point(9, 66);
            this.chklModelFields.MultiColumn = true;
            this.chklModelFields.Name = "chklModelFields";
            this.chklModelFields.Size = new System.Drawing.Size(424, 89);
            this.chklModelFields.TabIndex = 2;
            this.helpToolTip.SetToolTip(this.chklModelFields, "Select all the fields that you want to be updated. At least one required");
            // 
            // modelFieldsContextMenu
            // 
            this.modelFieldsContextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.selectAllFieldsMenuItem,
            this.clearSelectionMenuItem});
            this.modelFieldsContextMenu.Name = "modelFieldsContextMenu";
            this.modelFieldsContextMenu.Size = new System.Drawing.Size(123, 48);
            // 
            // selectAllFieldsMenuItem
            // 
            this.selectAllFieldsMenuItem.Name = "selectAllFieldsMenuItem";
            this.selectAllFieldsMenuItem.Size = new System.Drawing.Size(122, 22);
            this.selectAllFieldsMenuItem.Text = "Select All";
            this.selectAllFieldsMenuItem.Click += new System.EventHandler(this.selectAllFieldsMenuItem_Click);
            // 
            // clearSelectionMenuItem
            // 
            this.clearSelectionMenuItem.Name = "clearSelectionMenuItem";
            this.clearSelectionMenuItem.Size = new System.Drawing.Size(122, 22);
            this.clearSelectionMenuItem.Text = "Clear";
            this.clearSelectionMenuItem.Click += new System.EventHandler(this.clearSelectionMenuItem_Click);
            // 
            // lblModelFields
            // 
            this.lblModelFields.AutoSize = true;
            this.lblModelFields.Enabled = false;
            this.lblModelFields.Location = new System.Drawing.Point(8, 48);
            this.lblModelFields.Name = "lblModelFields";
            this.lblModelFields.Size = new System.Drawing.Size(73, 13);
            this.lblModelFields.TabIndex = 1;
            this.lblModelFields.Text = "Model Fields";
            this.helpToolTip.SetToolTip(this.lblModelFields, "Select all the fields that you want to be updated. At least one required");
            // 
            // chkCloseFileOnFinish
            // 
            this.chkCloseFileOnFinish.AutoSize = true;
            this.chkCloseFileOnFinish.Checked = true;
            this.chkCloseFileOnFinish.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkCloseFileOnFinish.Location = new System.Drawing.Point(9, 191);
            this.chkCloseFileOnFinish.Name = "chkCloseFileOnFinish";
            this.chkCloseFileOnFinish.Size = new System.Drawing.Size(122, 17);
            this.chkCloseFileOnFinish.TabIndex = 0;
            this.chkCloseFileOnFinish.Text = "Close file on finish";
            this.helpToolTip.SetToolTip(this.chkCloseFileOnFinish, "Close *.pbix file on finish");
            this.chkCloseFileOnFinish.UseVisualStyleBackColor = true;
            // 
            // chkPublish
            // 
            this.chkPublish.AutoSize = true;
            this.chkPublish.Location = new System.Drawing.Point(9, 168);
            this.chkPublish.Name = "chkPublish";
            this.chkPublish.Size = new System.Drawing.Size(64, 17);
            this.chkPublish.TabIndex = 0;
            this.chkPublish.Text = "Publish";
            this.helpToolTip.SetToolTip(this.chkPublish, "Publish file into a specified workspace");
            this.chkPublish.UseVisualStyleBackColor = true;
            this.chkPublish.CheckedChanged += new System.EventHandler(this.chkPublish_CheckedChanged);
            // 
            // chkRefreshAll
            // 
            this.chkRefreshAll.AutoSize = true;
            this.chkRefreshAll.Checked = true;
            this.chkRefreshAll.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkRefreshAll.Location = new System.Drawing.Point(9, 22);
            this.chkRefreshAll.Name = "chkRefreshAll";
            this.chkRefreshAll.Size = new System.Drawing.Size(80, 17);
            this.chkRefreshAll.TabIndex = 0;
            this.chkRefreshAll.Text = "Refresh all";
            this.helpToolTip.SetToolTip(this.chkRefreshAll, "Refresh all the fields (tables, queries) available in model");
            this.chkRefreshAll.UseVisualStyleBackColor = true;
            this.chkRefreshAll.CheckedChanged += new System.EventHandler(this.chkRefreshAll_CheckedChanged);
            // 
            // grOutput
            // 
            this.grOutput.Controls.Add(this.txtOutput);
            this.grOutput.Location = new System.Drawing.Point(12, 361);
            this.grOutput.Name = "grOutput";
            this.grOutput.Size = new System.Drawing.Size(442, 143);
            this.grOutput.TabIndex = 3;
            this.grOutput.TabStop = false;
            this.grOutput.Text = "Output";
            // 
            // txtOutput
            // 
            this.txtOutput.BackColor = System.Drawing.Color.Black;
            this.txtOutput.ContextMenuStrip = this.richTextBoxContextMenu;
            this.txtOutput.ForeColor = System.Drawing.Color.LimeGreen;
            this.txtOutput.Location = new System.Drawing.Point(11, 21);
            this.txtOutput.Name = "txtOutput";
            this.txtOutput.ReadOnly = true;
            this.txtOutput.ShortcutsEnabled = false;
            this.txtOutput.Size = new System.Drawing.Size(422, 114);
            this.txtOutput.TabIndex = 0;
            this.txtOutput.TabStop = false;
            this.txtOutput.Text = "Ready to go!\n-\nPowerRefresher @ https://github.com/alefranzoni/power-refresher\nAl" +
    "ejandro Franzoni Gimenez @ https://alejandrofranzoni.com.ar";
            this.txtOutput.LinkClicked += new System.Windows.Forms.LinkClickedEventHandler(this.txtOutput_LinkClicked);
            this.txtOutput.TextChanged += new System.EventHandler(this.txtOutput_TextChanged);
            // 
            // richTextBoxContextMenu
            // 
            this.richTextBoxContextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.copySelectedMenuItem,
            this.selectAllTextMenuItem});
            this.richTextBoxContextMenu.Name = "richTextBoxContextMenu";
            this.richTextBoxContextMenu.Size = new System.Drawing.Size(165, 48);
            // 
            // copySelectedMenuItem
            // 
            this.copySelectedMenuItem.Name = "copySelectedMenuItem";
            this.copySelectedMenuItem.ShortcutKeyDisplayString = "";
            this.copySelectedMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.C)));
            this.copySelectedMenuItem.Size = new System.Drawing.Size(164, 22);
            this.copySelectedMenuItem.Text = "Copy";
            this.copySelectedMenuItem.Click += new System.EventHandler(this.copySelectedMenuItem_Click);
            // 
            // selectAllTextMenuItem
            // 
            this.selectAllTextMenuItem.Name = "selectAllTextMenuItem";
            this.selectAllTextMenuItem.ShortcutKeyDisplayString = "";
            this.selectAllTextMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.A)));
            this.selectAllTextMenuItem.Size = new System.Drawing.Size(164, 22);
            this.selectAllTextMenuItem.Text = "Select All";
            this.selectAllTextMenuItem.Click += new System.EventHandler(this.selectAllTextMenuItem_Click);
            // 
            // cmdStartRefresh
            // 
            this.cmdStartRefresh.Enabled = false;
            this.cmdStartRefresh.Location = new System.Drawing.Point(315, 512);
            this.cmdStartRefresh.Name = "cmdStartRefresh";
            this.cmdStartRefresh.Size = new System.Drawing.Size(139, 23);
            this.cmdStartRefresh.TabIndex = 4;
            this.cmdStartRefresh.Text = "Refresh";
            this.helpToolTip.SetToolTip(this.cmdStartRefresh, "Test your settings by refreshing the file!");
            this.cmdStartRefresh.UseVisualStyleBackColor = true;
            this.cmdStartRefresh.Click += new System.EventHandler(this.cmdStartRefresh_Click);
            // 
            // cmdGenerateScript
            // 
            this.cmdGenerateScript.Location = new System.Drawing.Point(172, 512);
            this.cmdGenerateScript.Name = "cmdGenerateScript";
            this.cmdGenerateScript.Size = new System.Drawing.Size(139, 23);
            this.cmdGenerateScript.TabIndex = 4;
            this.cmdGenerateScript.Text = "Generate Script";
            this.cmdGenerateScript.UseVisualStyleBackColor = true;
            this.cmdGenerateScript.Click += new System.EventHandler(this.cmdGenerateScript_Click);
            // 
            // frmMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.ClientSize = new System.Drawing.Size(466, 544);
            this.Controls.Add(this.cmdGenerateScript);
            this.Controls.Add(this.cmdStartRefresh);
            this.Controls.Add(this.grOutput);
            this.Controls.Add(this.gbOptions);
            this.Controls.Add(this.gbInput);
            this.Controls.Add(this.lblTitle);
            this.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.Name = "frmMain";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "PowerRefresher GUI @ Alejandro Franzoni Gimenez";
            this.Load += new System.EventHandler(this.frmMain_Load);
            this.Shown += new System.EventHandler(this.frmMain_Shown);
            this.gbInput.ResumeLayout(false);
            this.gbInput.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericTimeout)).EndInit();
            this.gbOptions.ResumeLayout(false);
            this.gbOptions.PerformLayout();
            this.modelFieldsContextMenu.ResumeLayout(false);
            this.grOutput.ResumeLayout(false);
            this.richTextBoxContextMenu.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.GroupBox gbInput;
        private System.Windows.Forms.Button cmdSetInput;
        private System.Windows.Forms.TextBox txtInput;
        private System.Windows.Forms.NumericUpDown numericTimeout;
        private System.Windows.Forms.Label lblTimeout;
        private System.Windows.Forms.GroupBox gbOptions;
        private System.Windows.Forms.CheckedListBox chklModelFields;
        private System.Windows.Forms.Label lblModelFields;
        private System.Windows.Forms.CheckBox chkRefreshAll;
        private System.Windows.Forms.TextBox txtWorkspace;
        private System.Windows.Forms.Label lblWorkspace;
        private System.Windows.Forms.CheckBox chkPublish;
        private System.Windows.Forms.CheckBox chkCloseFileOnFinish;
        private System.Windows.Forms.GroupBox grOutput;
        private System.Windows.Forms.RichTextBox txtOutput;
        private System.Windows.Forms.Button cmdStartRefresh;
        private System.Windows.Forms.ToolTip helpToolTip;
        private System.Windows.Forms.ContextMenuStrip modelFieldsContextMenu;
        private System.Windows.Forms.ToolStripMenuItem selectAllFieldsMenuItem;
        private System.Windows.Forms.ToolStripMenuItem clearSelectionMenuItem;
        private System.Windows.Forms.ContextMenuStrip richTextBoxContextMenu;
        private System.Windows.Forms.ToolStripMenuItem copySelectedMenuItem;
        private System.Windows.Forms.ToolStripMenuItem selectAllTextMenuItem;
        private System.Windows.Forms.CheckBox chkCloseAppOnFinish;
        private System.Windows.Forms.Button cmdGenerateScript;
    }
}

