using System;
using DigitalLogicSandbox.Core.Signals;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering.Wires
{
    /// <summary>How signal states are drawn on wires.</summary>
    public enum SignalVisualizationMode
    {
        /// <summary>Theme colours; HIGH wires are thicker than LOW ones.</summary>
        Standard = 0,

        /// <summary>
        /// Stronger separation for readability and colour-vision deficiencies: LOW is thin and dim, HIGH is wide
        /// and bright, UNKNOWN is wide and warning-coloured, HIGH-Z is thinnest.
        /// </summary>
        HighContrast = 1,
    }

    /// <summary>
    /// Runtime signal appearance shared by all wire views of a presenter. Kept separate from the
    /// <see cref="VisualTheme"/> asset so switching modes never modifies a project asset. Colour is always paired
    /// with width, so states stay distinguishable without relying on colour alone.
    /// </summary>
    public sealed class SignalStyle
    {
        private readonly VisualTheme _theme;

        public SignalStyle(VisualTheme theme)
        {
            _theme = theme ? theme : throw new ArgumentNullException(nameof(theme));
        }

        public SignalVisualizationMode Mode { get; set; }

        public Color ColorOf(Signal signal)
        {
            Color color = _theme.SignalColor(signal);
            if (Mode != SignalVisualizationMode.HighContrast)
            {
                return color;
            }

            switch (signal)
            {
                case Signal.High:
                    return Color.Lerp(color, Color.white, 0.35f);
                case Signal.Low:
                    return Color.Lerp(color, Color.black, 0.35f);
                default:
                    return color;
            }
        }

        public float WidthOf(Signal signal)
        {
            float width = _theme.WireWidth(signal);
            if (Mode != SignalVisualizationMode.HighContrast)
            {
                return signal == Signal.HighImpedance ? width * 0.7f : width;
            }

            switch (signal)
            {
                case Signal.High:
                case Signal.Unknown:
                    return width * 1.5f;
                case Signal.HighImpedance:
                    return width * 0.5f;
                default:
                    return width * 0.7f;
            }
        }

        /// <summary>Colour of the bright front of a travelling signal.</summary>
        public Color FrontColorOf(Signal signal)
        {
            return Color.Lerp(ColorOf(signal), Color.white, 0.55f);
        }
    }
}
