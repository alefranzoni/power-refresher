using System;

namespace PowerRefresher.Engine
{
    /// <summary>
    /// UI Automation identifiers for Power BI Desktop. AutomationIds are language independent;
    /// Names are the visible (localized) labels.
    /// </summary>
    internal sealed class PbiStrings
    {
        public string RefreshButtonId = "refreshQueries";       //Legacy ribbon AutomationId
        public string RefreshButtonName = "Refresh";            //Current ribbon split button / field menu submenu
        public string RefreshDialogId = "modalDialog";
        public string RefreshInProgressButton = "Cancel";       //Only present while a refresh is running
        public string RefreshMenuItemId = "FieldListMenuItem_RefreshEntity"; //Legacy flat field menu item
        public string RefreshSubmenuDataId = "RefreshData";     //Current field menu: Refresh > Data
        public string SaveButtonId = "save";
        public string SaveWaitMessage = "Working on it";
        public string PublishButtonId = "publish";
        public string PublishGroupDialogId = "KoPublishToGroupDialog";
        public string PublishDialogId = "KoPublishDialog";
        public string WorkspaceContainerType = "list";
        public string SelectButton = "Select";
        public string ReplaceDialogId = "KoPublishWithImpactViewDialog";
        public string ReplaceButton = "Replace";
        public string SuccessPublishButton = "Got it";
        public string ScriptVisualsDialogId = "MessageDialog";
        public string EnableScriptVisualsButton = "Enable";
        public string CancelScriptVisualsButton = "Cancel";

        public static PbiStrings For(string language)
        {
            switch ((language ?? "en").ToLowerInvariant())
            {
                case "en":
                    return new PbiStrings();
                case "es":
                    return new PbiStrings
                    {
                        RefreshButtonName = "Actualizar",
                        RefreshInProgressButton = "Cancelar",
                        SaveWaitMessage = "En proceso",
                        SelectButton = "Seleccionar",
                        ReplaceButton = "Reemplazar",
                        SuccessPublishButton = "Entendido",
                        EnableScriptVisualsButton = "Habilitar",
                        CancelScriptVisualsButton = "Cancelar",
                    };
                default:
                    throw new ArgumentException($"Unsupported Power BI language '{language}'. Use 'en' or 'es'.");
            }
        }
    }
}
