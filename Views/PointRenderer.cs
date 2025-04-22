// ----------  Views/PointRenderer.cs  ----------
using System.Collections.Generic;
using cmetro25.Core;
using cmetro25.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;

namespace cmetro25.Views
{
    /// <summary>Generischer Punkt‑Renderer (Stationen, POIs …)</summary>
    public sealed class PointRenderer
    {
        private readonly Texture2D _px;

        public PointRenderer(Texture2D pixel) => _px = pixel;

        public void Draw(SpriteBatch sb, IEnumerable<PointElement> pts, float zoom)
        {
            if (pts == null) return;
            const float baseR = 5f;

            foreach (var p in pts)
            {
                float r = baseR / zoom;
                var c = p.Kind == "station" ? GameSettings.StationColor : Color.White;
                sb.DrawCircle(new CircleF(p.Position, r), 12, c, r);
            }
        }
    }
}
