using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using System;
using System.Collections.Generic;
using cmetro25.Models;
using cmetro25.Services;
using cmetro25.Views;

namespace cmetro25.Utils
{
    public class TileManager
    {
        private GraphicsDevice _graphicsDevice;
        private List<District> _districts;
        private List<Road> _roads;
        private MapLoader _mapLoader;
        private int _tileSize; // z. B. 256 Pixel
        private RectangleF _mapBounds; // Gesamtgrenzen der Karte
        private Dictionary<(int zoom, int tileX, int tileY), RenderTarget2D> _tileCache;
        private DistrictRenderer _districtRenderer;
        private RoadRenderer _roadRenderer;

        public TileManager(GraphicsDevice graphicsDevice, List<District> districts, List<Road> roads, MapLoader mapLoader, DistrictRenderer dr, RoadRenderer rr, int tileSize = 256)
        {
            _graphicsDevice = graphicsDevice;
            _districts = districts;
            _roads = roads;
            _mapLoader = mapLoader;
            _tileSize = tileSize;
            _tileCache = new Dictionary<(int, int, int), RenderTarget2D>();
            _mapBounds = ComputeGlobalMapBounds();
            _districtRenderer = dr;
            _roadRenderer = rr;
        }

        // Ermittelt die globalen Grenzen der Karte anhand der Bezirke und Straßen.
        private RectangleF ComputeGlobalMapBounds()
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            // Durchlaufe Bezirke:
            foreach (var district in _districts)
            {
                foreach (var poly in district.Polygons)
                {
                    foreach (var pt in poly)
                    {
                        if (pt.X < minX) minX = pt.X;
                        if (pt.Y < minY) minY = pt.Y;
                        if (pt.X > maxX) maxX = pt.X;
                        if (pt.Y > maxY) maxY = pt.Y;
                    }
                }
            }
            // Durchlaufe Straßen:
            foreach (var road in _roads)
            {
                foreach (var line in road.Lines)
                {
                    foreach (var pt in line)
                    {
                        if (pt.X < minX) minX = pt.X;
                        if (pt.Y < minY) minY = pt.Y;
                        if (pt.X > maxX) maxX = pt.X;
                        if (pt.Y > maxY) maxY = pt.Y;
                    }
                }
            }
            // Ursprüngliche Breite und Höhe:
            float width = maxX - minX;
            float height = maxY - minY;
            // Erweitere die kleinere Dimension, sodass das Raster quadratisch wird:
            if (width > height)
            {
                // Erhöhe die Höhe – zum Beispiel, indem du minY weiter nach unten verschiebst.
                float diff = width - height;
                minY -= diff * 0.5f;
                height = width;
            }
            else if (height > width)
            {
                float diff = height - width;
                minX -= diff * 0.5f;
                width = height;
            }
            // Optional: Du kannst hier noch eine Paddung einbauen, um Rundungsfehler abzufedern.
            return new RectangleF(minX, minY, width, height);
        }


        // Liefert ein RenderTarget (Tile) für den angegebenen Zoom und Tile-Koordinaten.
        public RenderTarget2D GetTile(int zoom, int tileX, int tileY)
        {
            var key = (zoom, tileX, tileY);
            if (!_tileCache.ContainsKey(key))
            {
                // Erstelle ein neues RenderTarget für dieses Tile.
                RenderTarget2D tileRT = new RenderTarget2D(
                    _graphicsDevice,
                    _tileSize, _tileSize,
                    false, SurfaceFormat.Color, DepthFormat.None, 0,
                    RenderTargetUsage.PreserveContents, true);
                _graphicsDevice.SetRenderTarget(tileRT);
                _graphicsDevice.Clear(Color.Transparent);

                // Berechne, wie viele Tiles in dieser Zoomstufe existieren (typischerweise 2^zoom pro Dimension).
                int numTiles = 1 << zoom;
                float tileWorldWidth = _mapBounds.Width / numTiles;
                float tileWorldHeight = _mapBounds.Height / numTiles;
                float tileWorldX = _mapBounds.X + tileX * tileWorldWidth;
                float tileWorldY = _mapBounds.Y + tileY * tileWorldHeight;

                // Erstelle eine statische Kamera, die auf dieses Tile zentriert ist.
                MapCamera tileCamera = new MapCamera(_tileSize, _tileSize);
                tileCamera.Zoom = _tileSize / tileWorldWidth;
                Vector2 tileCenter = new Vector2(tileWorldX + tileWorldWidth / 2, tileWorldY + tileWorldHeight / 2);
                tileCamera.CenterOn(tileCenter);

                SpriteBatch sb = new SpriteBatch(_graphicsDevice);
                sb.Begin(transformMatrix: tileCamera.TransformMatrix, samplerState: SamplerState.AnisotropicClamp);
                // Hier renderst du die statischen Map-Schichten in dieses Tile.
                // Du kannst die bestehenden Renderer verwenden – z. B.:
                _districtRenderer.Draw(sb, _districts, tileCamera);
                _roadRenderer.Draw(sb, _roads, new RectangleF(tileWorldX, tileWorldY, tileWorldWidth, tileWorldHeight), tileCamera);
                // Für Debug-Zwecke zeichnen wir einen Platzhaltertext:
                //sb.DrawString(Font, $"Tile {tileX},{tileY}\nZoom {zoom}", new Vector2(10, 10), Color.White);
                sb.End();

                _graphicsDevice.SetRenderTarget(null);
                _tileCache[key] = tileRT;
            }
            return _tileCache[key];
        }

        // Zeichnet alle Tiles, die im aktuellen Kamerasichtfeld sichtbar sind.
        public void DrawTiles(SpriteBatch spriteBatch, MapCamera camera, int zoomLevel)
        {
            RectangleF visibleWorld = camera.BoundingRectangle;
            int numTiles = 1 << zoomLevel;
            float tileWorldWidth = _mapBounds.Width / numTiles;
            float tileWorldHeight = _mapBounds.Height / numTiles;

            int minTileX = Math.Max(0, (int)Math.Floor((visibleWorld.X - _mapBounds.X) / tileWorldWidth));
            int maxTileX = Math.Min(numTiles - 1, (int)Math.Ceiling((visibleWorld.X + visibleWorld.Width - _mapBounds.X) / tileWorldWidth) - 1);
            int minTileY = Math.Max(0, (int)Math.Floor((visibleWorld.Y - _mapBounds.Y) / tileWorldHeight));
            int maxTileY = Math.Min(numTiles - 1, (int)Math.Ceiling((visibleWorld.Y + visibleWorld.Height - _mapBounds.Y) / tileWorldHeight) - 1);

            for (int ty = minTileY; ty <= maxTileY; ty++)
            {
                for (int tx = minTileX; tx <= maxTileX; tx++)
                {
                    RenderTarget2D tileTexture = GetTile(zoomLevel, tx, ty);
                    float tileWorldX = _mapBounds.X + tx * tileWorldWidth;
                    float tileWorldY = _mapBounds.Y + ty * tileWorldHeight;
                    Vector2 screenPos = camera.WorldToScreen(new Vector2(tileWorldX, tileWorldY));
                    float tileScreenWidth = tileWorldWidth * camera.Zoom;
                    float tileScreenHeight = tileWorldHeight * camera.Zoom;
                    // Berechne den Skalierungsfaktor basierend auf der gewünschten Bildschirmgröße und der Originalgröße des Tiles.
                    Vector2 scale = new Vector2(tileScreenWidth / tileTexture.Width, tileScreenHeight / tileTexture.Height);
                    // Verwende screenPos (als Vector2) direkt und übergebe den scale-Vektor.
                    spriteBatch.Draw(tileTexture, screenPos, null, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                }
            }
        }
    }
}
