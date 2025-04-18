using Microsoft.Xna.Framework;
using MonoGame.Extended; // Für RectangleF
using System.Collections.Generic;

namespace cmetro25.Models
{
    /// <summary>
    /// Repräsentiert eine Wasserfläche (See, Kanal, Hafen etc.).
    /// </summary>
    public class WaterBody
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; } // z.B. "river", "canal", "harbour", "lake" (aus 'natural' oder 'water' Tag)

        /// <summary>
        /// Liste der Polygone, die diese Wasserfläche definieren.
        /// Normalerweise nur ein äußeres Polygon, kann aber auch Löcher (innere Polygone) enthalten,
        /// oder mehrere getrennte Polygone (MultiPolygon).
        /// </summary>
        public List<List<Vector2>> Polygons { get; private set; } = new List<List<Vector2>>();

        /// <summary>
        /// Die Bounding Box, die alle Polygone dieser Wasserfläche umschließt.
        /// </summary>
        public RectangleF BoundingBox { get; set; }
    }
}
