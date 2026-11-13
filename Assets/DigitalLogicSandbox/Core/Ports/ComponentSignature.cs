using System;
using System.Collections.Generic;

namespace DigitalLogicSandbox.Core.Ports
{
    /// <summary>
    /// The ordered external pins of a component. This is all the editor, renderer, validator and simulator need
    /// to know about a component's shape; it is identical in form for primitives and custom gates.
    /// </summary>
    public sealed class ComponentSignature
    {
        private readonly PortDescriptor[] _inputs;
        private readonly PortDescriptor[] _outputs;

        public ComponentSignature(PortDescriptor[] inputs, PortDescriptor[] outputs)
        {
            _inputs = inputs != null ? (PortDescriptor[])inputs.Clone() : Array.Empty<PortDescriptor>();
            _outputs = outputs != null ? (PortDescriptor[])outputs.Clone() : Array.Empty<PortDescriptor>();
        }

        public IReadOnlyList<PortDescriptor> Inputs => _inputs;

        public IReadOnlyList<PortDescriptor> Outputs => _outputs;

        public int InputCount => _inputs.Length;

        public int OutputCount => _outputs.Length;

        /// <summary>Finds the ordinal position of an input port, or -1.</summary>
        public int IndexOfInput(PortId id)
        {
            for (int i = 0; i < _inputs.Length; i++)
            {
                if (_inputs[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Finds the ordinal position of an output port, or -1.</summary>
        public int IndexOfOutput(PortId id)
        {
            for (int i = 0; i < _outputs.Length; i++)
            {
                if (_outputs[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        public bool HasInput(PortId id)
        {
            return IndexOfInput(id) >= 0;
        }

        public bool HasOutput(PortId id)
        {
            return IndexOfOutput(id) >= 0;
        }
    }
}
