using Newtonsoft.Json.Linq;

namespace DigitalLogicSandbox.Persistence.Migration
{
    /// <summary>
    /// v6 -> v7: adds the <c>constant</c> node kind (with a <c>value</c>). Version 6 files contain no constants, so
    /// their content is already valid version 7 content; the step only bumps the version (the migrator does that).
    /// </summary>
    internal sealed class AddConstantsMigration : IProjectMigration
    {
        public int FromVersion => 6;

        public string Description => "Constant sources";

        public void Apply(JObject document)
        {
        }
    }
}
