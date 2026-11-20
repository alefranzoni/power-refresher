using System;

namespace PowerRefresher.Engine
{
    /// <summary>
    /// UI Automation identifiers for Power BI Desktop. AutomationIds and class names are language independent;
    /// Names are the visible (localized) labels used as fallbacks when an id or class changes.
    /// Spanish labels marked "unverified" have not been checked against a current Spanish Power BI build.
    /// </summary>
    internal sealed class PbiStrings
    {
        //Ribbon
        public string RefreshButtonId = "refreshQueries";       //Legacy ribbon AutomationId (gone in 2.159)
        public string RefreshButtonName = "Refresh";            //Ribbon split button, field menu submenu and refresh dialog title
        public string SaveButtonId = "save";
        public string SaveButtonName = "Save";
        public string PublishButtonId = "publish";
        public string PublishButtonName = "Publish";

        //Field list
        public string FieldTreeClass = "cdk-tree";
        public string MoreOptionsName = "More options";
        public string TableNodePrefix = "Table ";  //Older builds name the field-list node "Table Sales"
        public string RefreshMenuItemId = "FieldListMenuItem_RefreshEntity"; //Legacy flat field menu item
        public string RefreshSubmenuDataId = "RefreshData";     //Current field menu: Refresh > Data
        public string RefreshSubmenuDataName = "Data";

        //Dialogs
        public string RefreshDialogId = "modalDialog";
        public string RefreshInProgressButton = "Cancel";       //Only present while a refresh is running
        public string SaveWaitMessage = "Working on it";
        public string PublishGroupDialogId = "KoPublishToGroupDialog"; //Legacy destination dialog (gone in 2.159)
        public string WorkspacesListName = "Workspaces";        //2.159: destination list, items are "option"s
        public string PublishSelectButtonId = "okButton";       //2.159: "Select" in the destination dialog
        public string PublishCancelButtonId = "cancelButton";   //2.159: "Cancel" in the destination dialog
        public string CancelButton = "Cancel";
        public string PublishDialogId = "KoPublishDialog";      //Legacy result dialog (2.159 equivalent not yet seen)
        public string WorkspaceContainerType = "list";
        public string SelectButton = "Select";
        public string ReplaceDialogId = "KoPublishWithImpactViewDialog"; //Legacy (2.159 equivalent not yet seen)
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
                        RefreshButtonName = "Actualizar",           //unverified
                        SaveButtonName = "Guardar",                 //unverified
                        PublishButtonName = "Publicar",             //unverified
                        MoreOptionsName = "Más opciones",           //unverified
                        TableNodePrefix = "Tabla ",                 //unverified
                        RefreshSubmenuDataName = "Datos",           //unverified
                        RefreshInProgressButton = "Cancelar",
                        SaveWaitMessage = "En proceso",
                        SelectButton = "Seleccionar",
                        WorkspacesListName = "Áreas de trabajo",    //unverified
                        CancelButton = "Cancelar",
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
