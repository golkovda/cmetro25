using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using System.Collections.Generic;
using MonoGame.Extended;

namespace cmetro25.Models
{
    /// <summary>
    /// Enthält Informationen über einen Distrikt (z. B. Stadtbezirk).
    /// </summary>
    public class District
    {
        public string Name { get; set; }
        public int Population { get; set; }
        public List<List<Vector2>> Polygons { get; private set; } = [];
        public string ReferenceId { get; set; }
        public string AdminTitle { get; set; }
        public float AreaRatio { get; set; }
        public Vector2 Centroid { get; set; }
        public Vector2 TextPosition { get; set; }

        // NEU: Bounding Box für schnelle Sichtbarkeitsprüfungen
        public RectangleF BoundingBox { get; set; }
    }
}
