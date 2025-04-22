// Utils/TileBuildResult.cs
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace cmetro25.Utils
{
    internal sealed class TileBuildResult
    {
        public (int zoom, int x, int y) Key;
        public float WorldX, WorldY, WorldW, WorldH;

        // ► CPU‑fertige Daten  (Vertex‑/Index‑Listen pro Primitive‑Typ)
        public List<VertexPositionColor> FillVerts = [];
        public List<short> FillIndices = [];
        public List<(Vector2 p1, Vector2 p2, Color col, float thick)> Lines = [];
        public List<(Vector2 pos, Color col, float radius)> Points = [];

        // Convenience – ergibt direkt die lokale Kamera‑Matrix
        public Matrix CalcTransformMatrix(int tilePx)
        {
            var scale = tilePx / WorldW;
            return Matrix
                .CreateTranslation(-WorldX, -WorldY, 0) *
                Matrix.CreateScale(scale, scale, 1f);
        }
    }
}
