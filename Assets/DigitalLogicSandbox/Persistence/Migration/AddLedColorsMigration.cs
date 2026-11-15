using Newtonsoft.Json.Linq;

namespace DigitalLogicSandbox.Persistence.Migration
{
    /// <summary>
    /// v4 -> v5: display nodes may carry an optional <c>color</c> (LED colour id, see <c>LedColors</c>). Files
    /// without it keep the default colour, so version 4 content is already valid version 5 content; the step only
    /// bumps the version (the migrator does that).
    /// </summary>
    internal sealed class AddLedColorsMigration : IProjectMigration
    {
        public int FromVersion => 4;

        public string Description => "LED display colours (optional display color)";

        public void Apply(JObject document)
        {
        }
    }
}
