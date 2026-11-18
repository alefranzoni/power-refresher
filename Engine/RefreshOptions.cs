using System;
using System.Collections.Generic;

namespace PowerRefresher.Engine
{
    /// <summary>What to do when Power BI asks whether to enable R/Python script visuals.</summary>
    public enum ScriptVisualsAction
    {
        /// <summary>Stop the run with an error (safe default for unattended use).</summary>
        Abort,
        /// <summary>Click Enable and continue.</summary>
        Enable,
        /// <summary>Click Cancel and continue with script visuals disabled.</summary>
        Disable
    }

    public sealed class RefreshOptions
    {
        /// <summary>Full path to the .pbix file.</summary>
        public string FilePath { get; set; }

        /// <summary>How long to wait for the Power BI window (and its field list) to appear after opening the file.</summary>
        public int OpenTimeoutSeconds { get; set; } = 300;

        /// <summary>Tables to refresh one by one. Empty means refresh the whole model with the ribbon Refresh button.</summary>
        public IList<string> Tables { get; set; } = new List<string>();

        /// <summary>Workspace to publish to after refreshing. Null or empty means don't publish.</summary>
        public string Workspace { get; set; }

        public ScriptVisualsAction ScriptVisuals { get; set; } = ScriptVisualsAction.Abort;

        /// <summary>Close the Power BI window once the file is saved.</summary>
        public bool CloseFile { get; set; } = true;

        /// <summary>Power BI Desktop UI language: "en" or "es".</summary>
        public string Language { get; set; } = "en";

        public bool RefreshAll => Tables == null || Tables.Count == 0;
        public bool Publish => !string.IsNullOrWhiteSpace(Workspace);
    }

    /// <summary>Power BI reported a failed refresh (its refresh dialog stayed open with errors).</summary>
    public sealed class RefreshFailedException : Exception
    {
        public RefreshFailedException(string message) : base(message) { }
    }

    /// <summary>The Power BI UI did not look or behave as expected (element missing, timeout, bad input).</summary>
    public sealed class AutomationException : Exception
    {
        public AutomationException(string message, Exception inner = null) : base(message, inner) { }
    }
}
