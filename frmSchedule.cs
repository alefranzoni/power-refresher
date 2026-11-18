using System;
using System.Drawing;
using System.Windows.Forms;

namespace PowerRefresher
{
    /// <summary>Asks how often and when to run a generated script, then creates the Windows scheduled task.</summary>
    internal sealed class frmSchedule : Form
    {
        private readonly TextBox txtName = new TextBox();
        private readonly ComboBox cboFrequency = new ComboBox();
        private readonly DateTimePicker dtpTime = new DateTimePicker();
        private readonly CheckBox chkRunMissed = new CheckBox();
        private readonly string scriptPath;
        private readonly TimeSpan limit;

        public frmSchedule(string scriptPath, string pbixName, TimeSpan limit)
        {
            this.scriptPath = scriptPath;
            this.limit = limit;

            Text = "PowerRefresher: Schedule refresh";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(460, 300);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                ColumnCount = 2,
                RowCount = 6,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            txtName.Text = $"PowerRefresher - {pbixName}";
            txtName.Dock = DockStyle.Fill;
            AddRow(layout, 0, "Task name", txtName);

            cboFrequency.DropDownStyle = ComboBoxStyle.DropDownList;
            cboFrequency.Items.AddRange(new object[] { "Every day", "Weekdays (Monday to Friday)" });
            cboFrequency.SelectedIndex = 0;
            cboFrequency.Dock = DockStyle.Fill;
            AddRow(layout, 1, "Run", cboFrequency);

            dtpTime.Format = DateTimePickerFormat.Time;
            dtpTime.ShowUpDown = true;
            dtpTime.Value = DateTime.Today.AddHours(6);
            dtpTime.Width = 120;
            AddRow(layout, 2, "At", dtpTime);

            chkRunMissed.Text = "If a start is missed (PC off or asleep), run as soon as possible";
            chkRunMissed.Checked = true;
            chkRunMissed.AutoSize = true;
            layout.Controls.Add(chkRunMissed, 1, 3);

            Label info = new Label
            {
                Text = "The task runs only while you are logged on, because Power BI needs your desktop. " +
                       "A start is skipped if the previous run is still going, and a run is stopped after " +
                       $"{(int)limit.TotalHours} hours. You can change it later in Task Scheduler.",
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 10, 3, 3),
            };
            layout.Controls.Add(info, 0, 4);
            layout.SetColumnSpan(info, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            Button create = new Button { Text = "Create task", AutoSize = true };
            create.Click += Create_Click;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(create);
            layout.Controls.Add(buttons, 0, 5);
            layout.SetColumnSpan(buttons, 2);

            Controls.Add(layout);
            AcceptButton = create;
            CancelButton = cancel;
        }

        /// <summary>Name of the created task, set when the dialog returns OK.</summary>
        public string CreatedTaskName { get; private set; }

        private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
        {
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 10, 3) }, 0, row);
            layout.Controls.Add(control, 1, row);
        }

        private void Create_Click(object sender, EventArgs e)
        {
            string name = txtName.Text.Trim();
            if (name.Length == 0 || name.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            {
                MessageBox.Show(this, "Enter a task name without \\ / : * ? \" < > |", Text, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            if (ScheduledTask.Exists(name) &&
                MessageBox.Show(this, $"A task named '{name}' already exists. Replace it?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                ScheduledTask.Create(name, scriptPath,
                    cboFrequency.SelectedIndex == 0 ? ScheduledTask.Frequency.Daily : ScheduledTask.Frequency.Weekdays,
                    dtpTime.Value.TimeOfDay, chkRunMissed.Checked, limit);
                CreatedTaskName = name;
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is System.IO.IOException)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }
}
