using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using cmetro25.Core;
using cmetro25.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;

namespace cmetro25.Views
{
    public class StationRenderer
    {
        private readonly Texture2D _pixel;
        public StationRenderer(Texture2D px) => _pixel = px;

        public void Draw(SpriteBatch sb, IEnumerable<PointElement> pts, MapCamera cam)
        {
            foreach (var p in pts)
            {
                float radius = 10f / cam.Zoom;           // ~5 px Bildschirm
                var circle = new CircleF(new Vector2(p.Position.X,
                                          p.Position.Y), radius);

                sb.DrawCircle(circle, 10, Color.LightGreen);      // später Sprite austauschen
            }
        }
    }
}
