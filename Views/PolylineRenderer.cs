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
    public class PolylineRenderer
    {
        private readonly Texture2D _pixel;
        public PolylineRenderer(Texture2D px) => _pixel = px;

        public void Draw(SpriteBatch sb, IEnumerable<PolylineElement> els,
                         RectangleF visible, MapCamera cam)
        {
            foreach (var e in els)
                foreach (var line in e.Lines)
                    if (line.Count >= 2)
                    {
                        GetStyle(e.Kind, cam.Zoom, out var w, out var col);
                        DrawLine(sb, line, w, col);
                    }
        }

        private static void GetStyle(string kind, float z, out float w, out Color c)
        {
            switch (kind)
            {
                case "river": w = 3f; c = GameSettings.WaterBodyColor; break;
                case "rail": w = 2f; c = GameSettings.RailColor; break;
                case "road": w = 1.5f; c = GameSettings.RoadColorDefault; break; //TODO: RoadRenderer löschen und hier integrieren
                default: w = 1f; c = Color.White; break;
            }
            w = Math.Clamp(w / MathF.Sqrt(Math.Max(0.1f, z)),
                           GameSettings.RoadMinPixelWidth, 6f);
        }

        private void DrawLine(SpriteBatch sb, IList<Vector2> pts, float thick, Color col)
        {
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p1 = pts[i]; var p2 = pts[i + 1];
                var dir = p2 - p1; if (dir.LengthSquared() < 0.0001f) continue;
                float ang = MathF.Atan2(dir.Y, dir.X);
                sb.Draw(_pixel, p1, null, col, ang,
                        Vector2.Zero, new Vector2(dir.Length(), thick),
                        SpriteEffects.None, 0f);
            }
        }
    }

}
