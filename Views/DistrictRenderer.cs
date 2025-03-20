using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;
using cmetro25.Models;
using cmetro25.Utils;

namespace cmetro25.Views
{
    public class DistrictRenderer(Texture2D pixelTexture, SpriteFont font, Color borderColor, Color labelColor, float minTextScale, float maxTextScale, float baseZoom)
    {
        public void Draw(SpriteBatch spriteBatch, List<District> districts, MapCamera camera)
        {
            //spriteBatch.Begin(transformMatrix: camera.TransformMatrix);
            foreach (var polygon in districts.SelectMany(district => district.Polygons))
            {
                DrawPolygon(spriteBatch, polygon, borderColor);
            }
            //spriteBatch.End();
        }

        public void DrawLabels(SpriteBatch spriteBatch, List<District> districts, MapCamera camera)
        {
            spriteBatch.Begin(transformMatrix: camera.TransformMatrix, sortMode: SpriteSortMode.FrontToBack);
            foreach (var district in districts)
            {
                Vector2 textSize = font.MeasureString(district.Name);
                float textScale = TextUtils.CalculateTextScale(camera.Zoom, baseZoom, minTextScale, maxTextScale);
                if (textScale >= minTextScale)
                    spriteBatch.DrawString(font, district.Name, district.TextPosition, labelColor, 0, textSize * 0.5f, textScale, SpriteEffects.None, 0.1f);
            }
            spriteBatch.End();
        }

        private void DrawPolygon(SpriteBatch spriteBatch, List<Vector2> polygon, Color color, float thickness = 1f)
        {
            if (polygon.Count < 2)
                return;
            float overlapFactor = 0.01f;
            for (var i = 0; i < polygon.Count - 1; i++)
            {
                var p1 = polygon[i];
                var p2 = polygon[i + 1];
                var direction = p2 - p1;
                p2 += direction * overlapFactor;
                var distance = Vector2.Distance(p1, p2);
                var angle = (float)System.Math.Atan2(direction.Y, direction.X);
                spriteBatch.Draw(pixelTexture, p1, null, color, angle, Vector2.Zero, new Vector2(distance, thickness), SpriteEffects.None, 0);
            }
            if (polygon.Count > 1)
            {
                Vector2 p1 = polygon[^1];
                Vector2 p2 = polygon[0];
                Vector2 direction = p2 - p1;
                p2 += direction * overlapFactor;
                float distance = Vector2.Distance(p1, p2);
                float angle = (float)System.Math.Atan2(direction.Y, direction.X);
                spriteBatch.Draw(pixelTexture, p1, null, color, angle, Vector2.Zero, new Vector2(distance, thickness), SpriteEffects.None, 0);
            }
        }
    }
}
