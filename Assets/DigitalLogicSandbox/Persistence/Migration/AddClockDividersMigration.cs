using Newtonsoft.Json.Linq;

namespace DigitalLogicSandbox.Persistence.Migration
{
    /// <summary>
    /// v5 -> v6: clock nodes may carry an optional <c>divider</c> (2, 4 … 64). Files without it run every clock at
    /// the base frequency (÷1), exactly as before, so version 5 content is already valid version 6 content; the step
    /// only bumps the version (the migrator does that).
    /// </summary>
    internal sealed class AddClockDividersMigration : IProjectMigration
    {
        public int FromVersion => 5;

        public string Description => "Clock dividers (optional clock divider)";

        public void Apply(JObject document)
        {
        }
    }
}
