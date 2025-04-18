using cmetro25.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;

namespace cmetro25.Views
{
    /// <summary>
    /// Repräsentiert eine Kamera für die Kartendarstellung, die Zoom- und Pan-Funktionen unterstützt.
    /// </summary>
    public class MapCamera
    {
        private Vector2 _position;
        private float _zoom;
        private bool _isDragging;
        private Vector2 _lastMousePosition;
        private readonly int _viewportWidth;
        private readonly int _viewportHeight;
        private int _previousScrollWheelValue;

        // --- NEU: Private Felder für separate Matrizen ---
        private Matrix _viewMatrix;
        private Matrix _projectionMatrix;

        // --- NEU: Öffentliche Properties für BasicEffect ---
        public Matrix ViewMatrix => _viewMatrix;
        public Matrix ProjectionMatrix => _projectionMatrix;

        // --- Bestehendes Feld und Property ---
        private Matrix _transformMatrix;
        public Matrix TransformMatrix => _transformMatrix; // Für SpriteBatch

        public int ViewportWidth => _viewportWidth;

        public int ViewportHeight => _viewportHeight;

        /// <summary>
        /// Gibt das sichtbare Rechteck in Weltkoordinaten zurück.
        /// </summary>
        public RectangleF BoundingRectangle
        {
            get
            {
                Vector2 topLeft = ScreenToWorld(Vector2.Zero);
                Vector2 bottomRight = ScreenToWorld(new Vector2(_viewportWidth, _viewportHeight));
                return new RectangleF(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
            }
        }

        /// <summary>
        /// Die Position der Kamera in Weltkoordinaten.
        /// </summary>
        public Vector2 Position
        {
            get => _position;
            set { _position = value; UpdateTransformMatrix(); }
        }

        /// <summary>
        /// Der Zoomfaktor der Kamera.
        /// </summary>
        public float Zoom
        {
            get => _zoom;
            set { _zoom = MathHelper.Clamp(value, GameSettings.CameraZoomMin, GameSettings.CameraZoomMax); UpdateTransformMatrix(); }
        }

        /// <summary>
        /// Initialisiert eine neue Instanz der <see cref="MapCamera"/> Klasse.
        /// </summary>
        /// <param name="viewportWidth">Die Breite des Viewports.</param>
        /// <param name="viewportHeight">Die Höhe des Viewports.</param>
        public MapCamera(int viewportWidth, int viewportHeight)
        {
            _position = Vector2.Zero;
            _zoom = 1.0f;
            _viewportWidth = viewportWidth;
            _viewportHeight = viewportHeight;
            UpdateTransformMatrix();
            _previousScrollWheelValue = Mouse.GetState().ScrollWheelValue;
        }

        /// <summary>
        /// Aktualisiert den Zustand der Kamera basierend auf Benutzereingaben.
        /// </summary>
        /// <param name="gameTime">Die verstrichene Zeit seit dem letzten Update.</param>
        public void Update(GameTime gameTime)
        {
            HandleInput();
        }

        /// <summary>
        /// Verarbeitet die Benutzereingaben zur Steuerung der Kamera.
        /// </summary>
        private void HandleInput()
        {
            MouseState mouseState = Mouse.GetState();
            KeyboardState keyboardState = Keyboard.GetState();

            int scrollDelta = mouseState.ScrollWheelValue - _previousScrollWheelValue;
            if (scrollDelta > 0)
                Zoom *= GameSettings.CameraZoomStepFactor;
            else if (scrollDelta < 0)
                Zoom /= GameSettings.CameraZoomStepFactor;
            _previousScrollWheelValue = mouseState.ScrollWheelValue;

            if (mouseState.MiddleButton == ButtonState.Pressed || (mouseState.LeftButton == ButtonState.Pressed && keyboardState.IsKeyDown(Keys.LeftShift)))
            {
                if (!_isDragging)
                {
                    _isDragging = true;
                }
                else
                {
                    var mouseDelta = new Vector2(mouseState.X, mouseState.Y) - _lastMousePosition;
                    Position -= mouseDelta / Zoom;
                }

                _lastMousePosition = new Vector2(mouseState.X, mouseState.Y);
            }
            else
            {
                _isDragging = false;
            }
        }

        /// <summary>
        /// Aktualisiert die Transformationsmatrix der Kamera basierend auf Position und Zoom.
        /// </summary>
        // --- Anpassen der UpdateTransformMatrix Methode ---
        private void UpdateTransformMatrix()
        {
            // --- Berechnung für SpriteBatch (bleibt gleich) ---
            _transformMatrix = Matrix.CreateTranslation(-_position.X, -_position.Y, 0) *
                               Matrix.CreateScale(_zoom, _zoom, 1f) *
                               Matrix.CreateTranslation(_viewportWidth * 0.5f, _viewportHeight * 0.5f, 0);

            // --- Berechnung für BasicEffect ---
            // View Matrix (bleibt mit Vector3.Down):
            _viewMatrix = Matrix.CreateLookAt(
                cameraPosition: new Vector3(_position.X, _position.Y, 1f),
                cameraTarget: new Vector3(_position.X, _position.Y, 0f),
                cameraUpVector: Vector3.Up); // Korrekt für Y-Down Welt

            // --- KORREKTUR: Projection Matrix für Y-Down anpassen ---
            // Berechne die sichtbaren Weltgrenzen
            float worldViewWidth = _viewportWidth / _zoom;
            float worldViewHeight = _viewportHeight / _zoom;
            float left = _position.X - worldViewWidth * 0.5f;
            float right = _position.X + worldViewWidth * 0.5f;
            // Wichtig: Für Y-Down ist der "obere" Wert im Weltraum der kleinere Y-Wert
            // und der "untere" Wert im Weltraum der größere Y-Wert.
            // CreateOrthographicOffCenter erwartet (left, right, bottom, top)
            // Wir müssen also die Y-Werte entsprechend übergeben:
            float bottom = _position.Y + worldViewHeight * 0.5f; // Größerer Y-Wert (unten in Y-Down Welt)
            float top = _position.Y - worldViewHeight * 0.5f;    // Kleinerer Y-Wert (oben in Y-Down Welt)

            _projectionMatrix = Matrix.CreateOrthographicOffCenter(
                left: left,
                right: right,
                bottom: bottom, // Größerer Y-Wert
                top: top,       // Kleinerer Y-Wert
                zNearPlane: 0.1f,
                zFarPlane: 100f);
            // --- Ende KORREKTUR ---
        }

        /// <summary>
        /// Setzt die Kamera auf die Standardposition und den Standardzoom zurück.
        /// </summary>
        public void Reset()
        {
            _position = Vector2.Zero;
            _zoom = 1.0f;
            UpdateTransformMatrix();
        }

        /// <summary>
        /// Zentriert die Kamera auf einen bestimmten Punkt in Weltkoordinaten.
        /// </summary>
        /// <param name="point">Der Punkt, auf den die Kamera zentriert werden soll.</param>
        public void CenterOn(Vector2 point)
        {
            _position = point;
            UpdateTransformMatrix();
        }

        /// <summary>
        /// Transformiert eine Position von Weltkoordinaten in Bildschirmkoordinaten.
        /// </summary>
        /// <param name="worldPosition">Die Position in Weltkoordinaten.</param>
        /// <returns>Die transformierte Position in Bildschirmkoordinaten.</returns>
        public Vector2 WorldToScreen(Vector2 worldPosition) => Vector2.Transform(worldPosition, TransformMatrix);

        /// <summary>
        /// Transformiert eine Position von Bildschirmkoordinaten in Weltkoordinaten.
        /// </summary>
        /// <param name="screenPosition">Die Position in Bildschirmkoordinaten.</param>
        /// <returns>Die transformierte Position in Weltkoordinaten.</returns>
        public Vector2 ScreenToWorld(Vector2 screenPosition)
        {
            var inverseMatrix = Matrix.Invert(TransformMatrix);
            return Vector2.Transform(screenPosition, inverseMatrix);
        }
    }
}
