using UnityEngine;

namespace AlienSkies
{
    /// <summary>The game's entire color language. Every material, HUD label, and
    /// effect tint must come from here — coherence is enforced, not hoped for.</summary>
    public static class Palette
    {
        public static readonly Color Magenta = new Color(1f, 0.18f, 0.53f);
        public static readonly Color Cyan = new Color(0.22f, 0.88f, 1f);
        public static readonly Color Violet = new Color(0.6f, 0.36f, 1f);
        public static readonly Color Green = new Color(0.29f, 1f, 0.69f);
        public static readonly Color Amber = new Color(1f, 0.7f, 0.12f);
        public static readonly Color Ink = new Color(0.045f, 0.045f, 0.10f);

        /// <summary>Hues planets draw from (indexed by look-rng).</summary>
        public static readonly Color[] Planets = { Magenta, Cyan, Violet, Green, Amber };
    }
}
