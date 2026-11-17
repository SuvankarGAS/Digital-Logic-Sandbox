namespace DigitalLogicSandbox.Persistence
{
    /// <summary>Constants of the project file format.</summary>
    public static class ProjectFormat
    {
        /// <summary>
        /// Incremented whenever the JSON layout changes; older files are upgraded by migrations.
        /// v1: initial release. v2 (Phase B): clock nodes and <c>settings.simulation</c> (clock and power-on).
        /// v3 (Phase C): optional node <c>width</c>/<c>arity</c>, bus node kinds and decimal bus input values.
        /// v4 (Phase D): the <c>display</c> node kind with a <c>style</c>. v5: optional LED <c>color</c> on displays.
        /// v6: optional clock <c>divider</c>.
        /// </summary>
        public const int CurrentSchemaVersion = 6;

        /// <summary>Format marker stored in every file.</summary>
        public const string FormatName = "DigitalLogicSandbox.Project";

        public const string FileExtension = ".dlsproj";

        public const string AppName = "Digital Logic Sandbox";

        public const string AppVersion = "0.1.0";
    }
}
