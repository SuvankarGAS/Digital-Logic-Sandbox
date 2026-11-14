namespace DigitalLogicSandbox.Persistence
{
    /// <summary>Constants of the project file format.</summary>
    public static class ProjectFormat
    {
        /// <summary>Incremented whenever the JSON layout changes; older files are upgraded by migrations.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Format marker stored in every file.</summary>
        public const string FormatName = "DigitalLogicSandbox.Project";

        public const string FileExtension = ".dlsproj";

        public const string AppName = "Digital Logic Sandbox";

        public const string AppVersion = "0.1.0";
    }
}
