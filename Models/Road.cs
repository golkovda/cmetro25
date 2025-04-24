using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace cmetro25.Models
{
    public class Road
    {
        public string Name { get; set; }
        public string RoadType { get; set; } //  "highway" Wert (z.B., "residential", "primary")
        public List<List<Vector2>> Lines { get; set; } = []; // Liste von Liniensegmenten (für MultiLineString)
        // Weitere Eigenschaften nach Bedarf (z.B. maxspeed, lanes, oneway)
        public string MaxSpeed { get; set; }
        public List<RectangleF> BoundingBoxes { get; set; } = [];
        public List<List<Vector2>> OriginalLines { get; set; } = [];
        public float LastInterpolationZoom { get; set; } = -1f;
        public List<List<Vector2>> CachedInterpolatedLines { get; set; } = [];
        public List<RectangleF> CachedBoundingBoxes { get; set; } = [];
        public float CachedZoom { get; set; } = -1f;

        public List<List<Vector2>> CachedSmoothedLines { get; set; } = [];
        public float CachedSmoothZoom { get; set; } = -1f;
        public List<bool> FreeStart { get; set; }    // je Segment
        public List<bool> FreeEnd { get; set; }

    }
}
