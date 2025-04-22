// ----------  UI/PerformanceUI.cs  ----------
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using cmetro25.Models;
using cmetro25.Views;

namespace cmetro25.UI
{
    /// <summary>
    /// Overlay für FPS, Speicher, Tile‑Queues usw.
    /// </summary>
    public sealed class PerformanceUI
    {
        private readonly SpriteFont _font;
        private readonly StringBuilder _sb = new(512);

        public PerformanceUI(SpriteFont font) => _font = font;

        /// <summary>
        ///     Zeichnet das Performance‑Overlay.
        /// </summary>
        public void Draw(
            SpriteBatch spriteBatch,
            int fps,
            int updatesPerSecond,
            MapCamera cam,
            List<District> districts,
            List<Road> roads,
            int drawnRoadSegs,
            int tileZoomLevel,
            /* --- neue Kennzahlen --- */
            int visibleTiles,
            int tileCacheCount,
            int genQueue,
            int buildQueue,
            int completedQueue,
            long memMB)
        {
            /* -------- Text zusammenstellen -------- */
            _sb.Clear();
            _sb.AppendLine($"FPS: {fps}");
            _sb.AppendLine($"Updates/Sec: {updatesPerSecond}");
            _sb.AppendLine($"Frame ms: {(fps > 0 ? 1000f / fps : 0):F1}");
            _sb.AppendLine($"Update ms: {(updatesPerSecond > 0 ? 1000f / updatesPerSecond : 0):F1}");
            _sb.AppendLine($"Camera: {cam.Position.X:F0}, {cam.Position.Y:F0}");
            _sb.AppendLine($"Zoom: {cam.Zoom:F2}");
            _sb.AppendLine($"TileZoomLevel: {tileZoomLevel}");
            _sb.AppendLine($"Visible Tiles: {visibleTiles}");
            _sb.AppendLine($"Tile Cache: {tileCacheCount}");
            _sb.AppendLine($"Gen Queue: {genQueue}");
            _sb.AppendLine($"Build Queue: {buildQueue}");
            _sb.AppendLine($"Results Pending: {completedQueue}");
            _sb.AppendLine($"Districts: {districts?.Count ?? 0}");
            _sb.AppendLine($"Roads: {roads?.Count ?? 0}");
            _sb.AppendLine($"Drawn Road Segs: {drawnRoadSegs}");
            _sb.AppendLine($"Mem (MB): {memMB}");

            var txt = _sb.ToString();

            /* -------- Zeichnen mit Schatten -------- */
            spriteBatch.Begin();
            spriteBatch.DrawString(_font, txt, new Vector2(11, 11), Color.Black * 0.8f);
            spriteBatch.DrawString(_font, txt, new Vector2(10, 10), Color.Yellow);
            spriteBatch.End();
        }
    }
}
