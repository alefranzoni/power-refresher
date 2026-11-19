using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace PowerRefresher
{
    /// <summary>
    /// Creates Windows scheduled tasks that run a PowerRefresherCli script, with the settings unattended Power BI
    /// automation needs. Uses schtasks.exe with a task XML so every setting can be specified.
    /// </summary>
    internal static class ScheduledTask
    {
        public enum Frequency { Daily, Weekdays }

        public static bool Exists(string name) => RunSchtasks($"/Query /TN {Quote(name)}", out _) == 0;

        /// <summary>
        /// Creates (or replaces) the task. Settings, and why:
        /// - Run only when the user is logged on (InteractiveToken): Power BI needs the user's desktop.
        /// - If already running, don't start a new instance: overlapping runs would fight over the window.
        /// - Start when available: a run missed while the PC was off or asleep happens as soon as possible.
        /// - No battery restrictions, and stop after <paramref name="limit"/> as a last-resort safety net.
        /// </summary>
        public static void Create(string name, string scriptPath, Frequency frequency, TimeSpan timeOfDay, bool runMissed, TimeSpan limit)
        {
            DateTime now = DateTime.Now;
            DateTime start = now.Date + timeOfDay;
            if (start <= now) start = start.AddDays(1);

            string schedule = frequency == Frequency.Daily
                ? "<ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>"
                : "<ScheduleByWeek><DaysOfWeek><Monday /><Tuesday /><Wednesday /><Thursday /><Friday /></DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek>";

            string user = WindowsIdentity.GetCurrent().Name;
            string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Author>{Esc(user)}</Author>
    <Description>{Esc($"Refreshes {Path.GetFileName(scriptPath)} with PowerRefresher. Created {now:yyyy-MM-dd HH:mm}.")}</Description>
  </RegistrationInfo>
  <Triggers>
    <CalendarTrigger>
      <StartBoundary>{start:yyyy-MM-ddTHH:mm:ss}</StartBoundary>
      <Enabled>true</Enabled>
      {schedule}
    </CalendarTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{Esc(user)}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>{(runMissed ? "true" : "false")}</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT{(int)limit.TotalMinutes}M</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{Esc(Quote(scriptPath))}</Command>
      <WorkingDirectory>{Esc(Path.GetDirectoryName(scriptPath))}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";

            string xmlPath = Path.Combine(Path.GetTempPath(), $"PowerRefresher_task_{Guid.NewGuid():N}.xml");
            try
            {
                File.WriteAllText(xmlPath, xml, Encoding.Unicode);
                if (RunSchtasks($"/Create /TN {Quote(name)} /XML {Quote(xmlPath)} /F", out string output) != 0)
                    throw new InvalidOperationException("Windows could not create the scheduled task:\n" + output.Trim());
            }
            finally
            {
                try { File.Delete(xmlPath); } catch (IOException) { }
            }
        }

        /// <summary>Next run time as Task Scheduler reports it, or null.</summary>
        public static string NextRunTime(string name)
        {
            if (RunSchtasks($"/Query /TN {Quote(name)} /FO LIST", out string output) != 0) return null;
            foreach (string line in output.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && line.Substring(0, colon).Trim().Equals("Next Run Time", StringComparison.OrdinalIgnoreCase))
                    return line.Substring(colon + 1).Trim();
            }
            return null;
        }

        private static int RunSchtasks(string arguments, out string output)
        {
            ProcessStartInfo info = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (Process p = Process.Start(info))
            {
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                output = stdout + stderr;
                return p.ExitCode;
            }
        }

        private static string Quote(string value) => "\"" + value + "\"";
        private static string Esc(string value) => SecurityElement.Escape(value);
    }
}
