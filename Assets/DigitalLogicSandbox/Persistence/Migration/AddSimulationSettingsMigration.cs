using Newtonsoft.Json.Linq;

namespace DigitalLogicSandbox.Persistence.Migration
{
    /// <summary>
    /// v1 -> v2 (Phase B, sequential logic): adds <c>settings.simulation</c> with the clock and power-on defaults.
    /// Version 1 files cannot contain clock nodes, so circuits need no change.
    /// </summary>
    internal sealed class AddSimulationSettingsMigration : IProjectMigration
    {
        public int FromVersion => 1;

        public string Description => "Add simulation settings (clock frequency, clock enabled, power-on state)";

        public void Apply(JObject document)
        {
            if (!(document["settings"] is JObject settings))
            {
                settings = new JObject();
                document["settings"] = settings;
            }

            if (settings["simulation"] is JObject)
            {
                return;
            }

            settings["simulation"] = new JObject
            {
                ["clockFrequency"] = 2.0,
                ["clockEnabled"] = true,
                ["powerOnState"] = "Resolved",
            };
        }
    }
}
