using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace cmetro25.Models
{
    // Basistyp für alles, was nur Koordinaten + Stil braucht
    public abstract record MapElement;

    // ►  Linienbasierte Elemente
    public record PolylineElement(string Kind, List<List<Vector2>> Lines) : MapElement
    {
        // „Kind“  bestimmt spätere Stilwahl (road, river, rail …)
        public List<RectangleF> BoundingBoxes { get; init; } = [];
    }

    // ►  Punkte
    public record PointElement(string Kind, Vector2 Position) : MapElement;
}