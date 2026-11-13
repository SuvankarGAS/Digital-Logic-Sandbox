using DigitalLogicSandbox.Core.Nodes;

namespace DigitalLogicSandbox.CircuitEditor.Placement
{
    /// <summary>What the user is about to place (chosen in the gate library).</summary>
    public readonly struct PlacementRequest
    {
        public PlacementRequest(ComponentRef component, string displayName)
        {
            Component = component;
            DisplayName = displayName;
        }

        public ComponentRef Component { get; }

        public string DisplayName { get; }
    }
}
