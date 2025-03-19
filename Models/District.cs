using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace cmetro25.Models
{
    /// <summary>
    /// Enthält Informationen über einen Distrikt (z. B. Stadtbezirk).
    /// </summary>
    public class District
    {
        public string Name { get; set; }
        public int Population { get; set; }
        public List<List<Vector2>> Polygons { get; private set; } = new List<List<Vector2>>();
        public string ReferenceId { get; set; }
        public string AdminTitle { get; set; }
        public float AreaRatio { get; set; }

        // NEU: Zentroid hinzufügen
        public Vector2 Centroid { get; set; }

        public Vector2 TextPosition { get; set; }
    }
}
