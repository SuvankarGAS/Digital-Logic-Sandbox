using Newtonsoft.Json.Linq;

namespace DigitalLogicSandbox.Persistence.Migration
{
    /// <summary>
    /// v2 -> v3 (Phase C, multi-bit logic): nodes may carry <c>width</c> and <c>arity</c>, and there are new node
    /// kinds (splitter, merger, busjoin, tristate). Both are optional with single-bit defaults, and input values keep
    /// "Low"/"High" for 0/1, so version 2 content is already valid version 3 content; the step only bumps the
    /// version (the migrator does that).
    /// </summary>
    internal sealed class AddBusSupportMigration : IProjectMigration
    {
        public int FromVersion => 2;

        public string Description => "Multi-bit buses (optional node width/arity; new bus node kinds)";

        public void Apply(JObject document)
        {
        }
    }
}
