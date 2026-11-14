using Newtonsoft.Json.Linq;

namespace DigitalLogicSandbox.Persistence.Migration
{
    /// <summary>
    /// v3 -> v4 (Phase D, visual components): adds the <c>display</c> node kind, whose optional <c>style</c> names
    /// what it shows (led, sevensegment, binary, hex, decimal, signeddecimal). Nothing existing changes, so version 3
    /// content is already valid version 4 content; the step only bumps the version (the migrator does that).
    /// </summary>
    internal sealed class AddDisplaysMigration : IProjectMigration
    {
        public int FromVersion => 3;

        public string Description => "Visual displays (new display node kind with a style)";

        public void Apply(JObject document)
        {
        }
    }
}
