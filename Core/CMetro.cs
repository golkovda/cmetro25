using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using cmetro25.Models;
using cmetro25.Services;
using cmetro25.UI;
using cmetro25.Views;
using cmetro25.Utils;
using cmetro25.Core; // falls weitere Core-Komponenten benötigt werden

namespace cmetro25.Core
{
    public class CMetro : Game
    {
        private readonly GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;

        // Daten
        private List<District> _districts;
        private List<Road> _roads;

        // Services und Renderer
        private MapLoader _mapLoader;
        private RoadService _roadService;
        private DistrictRenderer _districtRenderer;
        private RoadRenderer _roadRenderer;
        private PerformanceUI _performanceUI;
        private RenderTarget2D _mapRenderTarget;
        private bool _mapIsReady = false;

        // Kamera
        private MapCamera _camera;

        // UI & Debug
        private SpriteFont _font;
        private Texture2D _pixelTexture;
        private bool _showPerformanceMenu = true;
        private int _frameCounter = 0;
        private int _fps = 0;
        private int _updateCounter = 0;
        private int _updatesPerSecond = 0;
        private double _elapsedTime = 0.0;
        private KeyboardState _previousKeyboardState;
        private float _lastZoom;
        private double _timeSinceLastZoomChange = 0.0;
        private const double ZoomDebounceThreshold = 0.1; // in Sekunden

        // Konstanten
        private const float _baseMaxDistance = 2f;
        private const float _baseOverlapFactor = 0.3f;
        private const int _curvesegments = 6;
        private const float MinTextScale = 0.1f;
        private const float MaxTextScale = 1.8f;
        private const float BaseZoom = 1.0f;
        private readonly Color _districtBorderColor = new Color(143, 37, 37);
        private readonly Color _districtLabelColor = new Color(143, 37, 37);
        private readonly Color _mapBackgroundColor = new Color(31, 31, 31);

        public CMetro()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
            _graphics.PreferredBackBufferWidth = 1600;
            _graphics.PreferredBackBufferHeight = 800;
        }

        protected override void Initialize()
        {
            _camera = new MapCamera(_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight);
            _camera.CenterOn(new Vector2(555, 555));

            _mapLoader = new MapLoader(_camera, _baseMaxDistance);
            string districtPath = Path.Combine(AppContext.BaseDirectory, "GeoJson", "dortmund_boundaries_census_finished.geojson");
            string roadPath = Path.Combine(AppContext.BaseDirectory, "GeoJson", "dortmund_roads_finished.geojson");
            _districts = _mapLoader.LoadDistricts(districtPath);
            _roads = _mapLoader.LoadRoads(roadPath);

            // Erstelle RoadService (inkl. Quadtree-Build)
            _roadService = new RoadService(_roads, _mapLoader);
            _lastZoom = _camera.Zoom;

            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _font = Content.Load<SpriteFont>("CMFont");

            _pixelTexture = new Texture2D(GraphicsDevice, 1, 1);
            _pixelTexture.SetData([Color.White]);

            // Initialisiere Renderer und UI-Komponenten:
            _districtRenderer = new DistrictRenderer(_pixelTexture, _font, _districtBorderColor, _districtLabelColor, MinTextScale, MaxTextScale, BaseZoom);
            _roadRenderer = new RoadRenderer(_pixelTexture, _baseOverlapFactor, _baseMaxDistance, true, _curvesegments);
            _performanceUI = new PerformanceUI(_font);

            LoadMapRenderTarget();
        }

        protected override void UnloadContent()
        {
            _pixelTexture.Dispose();
            base.UnloadContent();
        }

        private void LoadMapRenderTarget()
        {
            // Beispielhaft: Wir wählen hier als RenderTarget die Auflösung des BackBuffers.
            // Je nach Kartenumfang und gewünschter Detailgenauigkeit kann die Größe angepasst werden.
            _mapRenderTarget = new RenderTarget2D(GraphicsDevice,
                _graphics.PreferredBackBufferWidth,
                _graphics.PreferredBackBufferHeight);

            // Setze das RenderTarget als Ziel und render die Karte einmalig.
            GraphicsDevice.SetRenderTarget(_mapRenderTarget);
            GraphicsDevice.Clear(_mapBackgroundColor);

            // Wir gehen davon aus, dass die Map-Daten (Districts, Roads, etc.) bereits geladen und vorbereitet sind.
            // Hier rufen wir die Render-Methoden auf, ohne Kamera-Transform – oder du benutzt eine statische Ansicht.
            
            _districtRenderer.Draw(_spriteBatch, _districts, /* optional: statische Kamera oder Default-Transform */ new MapCamera(_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight));
            _districtRenderer.DrawLabels(_spriteBatch, _districts, new MapCamera(_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight));
            _spriteBatch.Begin();
            _roadRenderer.Draw(_spriteBatch, _roads, new RectangleF(0, 0, _graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight),
                new MapCamera(_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight));
            _spriteBatch.End();

            // Setze das RenderTarget zurück auf den BackBuffer.
            GraphicsDevice.SetRenderTarget(null);
            _mapIsReady = true;
        }

        protected override void Update(GameTime gameTime)
        {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
                Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            _camera.Update(gameTime);

            // Toggle Performance-Menü (F3)
            var currentKeyboardState = Keyboard.GetState();
            if (currentKeyboardState.IsKeyDown(Keys.F3) && !_previousKeyboardState.IsKeyDown(Keys.F3))
                _showPerformanceMenu = !_showPerformanceMenu;
            _previousKeyboardState = currentKeyboardState;

            // Zoom-Debounce: Neuberechnung der Road-Interpolationen nur, wenn sich der Zoom stabil verändert hat.
            float currentZoom = _camera.Zoom;
            if (Math.Abs(currentZoom - _lastZoom) > 0.001f)
            {
                _timeSinceLastZoomChange = 0.0;
                _lastZoom = currentZoom;
            }
            else
            {
                _timeSinceLastZoomChange += gameTime.ElapsedGameTime.TotalSeconds;
                if (_timeSinceLastZoomChange >= ZoomDebounceThreshold)
                {
                    _roadService.UpdateRoadInterpolationsAsync(_camera.Zoom, CalculateVisibleBounds(10f));
                    _timeSinceLastZoomChange = 0.0;
                }
            }

            // Update Performance-Zähler
            _updateCounter++;
            _elapsedTime += gameTime.ElapsedGameTime.TotalSeconds;
            if (_elapsedTime >= 1.0)
            {
                _fps = _frameCounter;
                _updatesPerSecond = _updateCounter;
                _frameCounter = 0;
                _updateCounter = 0;
                _elapsedTime = 0.0;
            }

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            _frameCounter++;
            GraphicsDevice.Clear(_mapBackgroundColor);

            //// Zeichne Distrikte und Labels
            //_districtRenderer.Draw(_spriteBatch, _districts, _camera);
            //_districtRenderer.DrawLabels(_spriteBatch, _districts, _camera);

            //// Berechne erweiterten Sichtbereich
            //RectangleF visibleBounds = CalculateVisibleBounds(500f);
            //// Query des Quadtrees
            //List<Road> candidateRoads = _roadService.GetQuadtree().Query(visibleBounds);
            //candidateRoads = candidateRoads.Distinct().ToList();
            //// Zeichne die Straßen
            //_spriteBatch.Begin(transformMatrix: _camera.TransformMatrix);
            //_roadRenderer.Draw(_spriteBatch, candidateRoads, visibleBounds, _camera);
            //_spriteBatch.End();

            //// Zeichne Performance-Menü (UI)
            //if (_showPerformanceMenu)
            //    _performanceUI.Draw(_spriteBatch, _fps, _updatesPerSecond, _camera, _districts, _roads,_roadRenderer.GetVisibleLineCount());

            //base.Draw(gameTime);

            if (!_mapIsReady)
            {
                // Ladebildschirm, solange das RenderTarget noch nicht erstellt ist.
                _spriteBatch.Begin();
                _spriteBatch.DrawString(_font, "Loading Map...", new Vector2(100, 100), Color.White);
                _spriteBatch.End();
            }
            else
            {
                // Zeichne das fertige RenderTarget und transformiere es mit der Kamera.
                _spriteBatch.Begin(transformMatrix: _camera.TransformMatrix);
                _spriteBatch.Draw(_mapRenderTarget, Vector2.Zero, Color.White);
                _spriteBatch.End();

                // Optional: UI und Performance-Anzeigen
                if (_showPerformanceMenu)
                    _performanceUI.Draw(_spriteBatch, _fps, _updatesPerSecond, _camera, _districts, _roads, _roadRenderer.GetVisibleLineCount());
            }

            base.Draw(gameTime);
        }

        private RectangleF CalculateVisibleBounds(float pixelMargin = 50f)
        {
            float marginWorld = pixelMargin / _camera.Zoom;
            RectangleF visibleBounds = _camera.BoundingRectangle;
            visibleBounds = new RectangleF(
                visibleBounds.X - marginWorld,
                visibleBounds.Y - marginWorld,
                visibleBounds.Width + 2 * marginWorld,
                visibleBounds.Height + 2 * marginWorld);
            return visibleBounds;
        }
    }
}
