using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using cmetro25.Models;
using cmetro25.Views;

namespace cmetro25.UI
{
    public class PerformanceUI
    {
        private readonly SpriteFont _font;

        public PerformanceUI(SpriteFont font)
        {
            _font = font;
        }

        // NEU: Parameter für queueSize hinzugefügt
        public void Draw(SpriteBatch spriteBatch, int fps, int updatesPerSecond, MapCamera camera, List<District> districts, List<Road> roads, int loadedObjects, int tileZoomLevel, int queueSize)
        {
            var mouseState = Mouse.GetState();
            var mouseScreenPos = new Vector2(mouseState.X, mouseState.Y);
            var mouseWorldPos = camera.ScreenToWorld(mouseScreenPos);
            var visibleBounds = camera.BoundingRectangle;

            var perfText = $"FPS: {fps}\n" +
                              $"Updates/Sec: {updatesPerSecond}\n" +
                              $"Mouse (Screen): {mouseScreenPos.X:F0}, {mouseScreenPos.Y:F0}\n" +
                              $"Mouse (World): {mouseWorldPos.X:F2}, {mouseWorldPos.Y:F2}\n" +
                              $"Camera: {camera.Position.X:F2}, {camera.Position.Y:F2}\n" +
                              $"Zoom: {camera.Zoom:F2}\n" +
                              $"TileZoomLevel: {tileZoomLevel}\n" +
                              // $"Visible: X:{visibleBounds.X:F2} Y:{visibleBounds.Y:F2} W:{visibleBounds.Width:F2} H:{visibleBounds.Height:F2}\n" + // Weniger wichtig jetzt
                              $"Districts: {districts?.Count ?? 0}\n" + // Null-Check
                              $"Roads: {roads?.Count ?? 0}\n" + // Null-Check
                              $"Drawn Road Segs: {loadedObjects}\n" + // Umbenannt für Klarheit
                              $"Tile Gen Queue: {queueSize}"; // NEU

            spriteBatch.Begin();
            // Zeichne mit leichtem Schatten für bessere Lesbarkeit
            spriteBatch.DrawString(_font, perfText, new Vector2(11, 11), Color.Black * 0.8f);
            spriteBatch.DrawString(_font, perfText, new Vector2(10, 10), Color.Yellow);
            spriteBatch.End();
        }
    }
}