using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace cmetro25.Primitives
{
    public class CMetroPrimitiveBatch : IDisposable
    {
        private const int DefaultBufferSize = 500;

        private VertexPositionColor[] _vertices = new VertexPositionColor[DefaultBufferSize];
        private int _positionInBuffer = 0;
        private BasicEffect _basicEffect;
        private GraphicsDevice _graphicsDevice;
        private bool _hasBegun = false;
        private bool _isDisposed = false;

        public CMetroPrimitiveBatch(GraphicsDevice graphicsDevice)
        {
            if (graphicsDevice == null)
                throw new ArgumentNullException("graphicsDevice");

            _graphicsDevice = graphicsDevice;
            _basicEffect = new BasicEffect(graphicsDevice)
            {
                VertexColorEnabled = true,
                Projection = Matrix.CreateOrthographicOffCenter(0, graphicsDevice.Viewport.Width, graphicsDevice.Viewport.Height, 0, 0, 1)
            };
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !_isDisposed)
            {
                _basicEffect?.Dispose();
                _isDisposed = true;
            }
        }

        public void Begin(Matrix worldMatrix)
        {
            if (_hasBegun)
                throw new InvalidOperationException("End must be called before Begin can be called again.");

            _basicEffect.World = worldMatrix;
            _basicEffect.View = Matrix.Identity; // Keine zusätzliche View-Transformation
            _basicEffect.CurrentTechnique.Passes[0].Apply();
            _hasBegun = true;
        }

        public void AddVertex(Vector2 vertex, Color color)
        {
            if (!_hasBegun)
                throw new InvalidOperationException("Begin must be called before AddVertex can be called.");

            if (_positionInBuffer >= _vertices.Length)
                Flush();

            _vertices[_positionInBuffer].Position = new Vector3(vertex, 0);
            _vertices[_positionInBuffer].Color = color;
            _positionInBuffer++;
        }

        // Verbesserte DrawLine-Methode (KORREKTE VERTEX-REIHENFOLGE)
        private Vector2 _lastNormal = Vector2.UnitY; // Speichere die letzte Normale
        private const float NormalTolerance = 0.1f; // Toleranz für die Normalenberechnung

        public void DrawLine(Vector2 start, Vector2 end, Color color, float thickness = 1f)
        {
            Vector2 edge = end - start;
            float edgeLength = edge.Length();

            Vector2 normal;
            if (edgeLength > NormalTolerance)
            {
                normal = new Vector2(-edge.Y, edge.X);
                normal.Normalize();
                _lastNormal = normal; // Aktualisiere die letzte Normale
            }
            else
            {
                normal = _lastNormal; // Verwende die letzte Normale
            }

            normal *= thickness / 2f;

            // Korrekte Vertex-Reihenfolge für TriangleList (GEGEN den Uhrzeigersinn):
            //
            //   1----4  (Normalenvektor zeigt nach "außen")
            //   |  / |
            //   | /  |
            //   2----3
            //
            // Dreieck 1: 1-2-3
            // Dreieck 2: 1-3-4  (NICHT 3-4-1, das wäre im Uhrzeigersinn!)

            AddVertex(start + normal, color);     // 1. Vertex (oben links)
            AddVertex(start - normal, color);     // 2. Vertex (unten links)
            AddVertex(end - normal, color);       // 3. Vertex (unten rechts)

            AddVertex(start + normal, color);     // 1. Vertex (oben links) - Wiederholung für Dreieck 2
            AddVertex(end - normal, color);       // 3. Vertex (unten rechts) - Wiederholung für Dreieck 2
            AddVertex(end + normal, color);       // 4. Vertex (oben rechts)
        }

        // Methode zum Zeichnen eines Polygons (als Umriss)
        public void DrawPolygonOutline(Vector2[] vertices, Color color, float thickness = 1f)
        {
            if (vertices.Length < 2) return;

            for (int i = 0; i < vertices.Length - 1; i++)
            {
                DrawLine(vertices[i], vertices[i + 1], color, thickness);
            }
            DrawLine(vertices[vertices.Length - 1], vertices[0], color, thickness); // Schließe das Polygon
        }

        // Methode zum Zeichnen eines gefüllten Polygons (optional, erfordert Triangulierung)
        public void FillPolygon(Vector2[] vertices, Color color)
        {
            // Hier kommt die Triangulierung (siehe unten)
            int[] indices = Triangulate(vertices);

            if (indices == null) return; // Triangulierung fehlgeschlagen

            // Füge die Dreiecke hinzu
            for (int i = 0; i < indices.Length; i += 3)
            {
                AddVertex(vertices[indices[i]], color);
                AddVertex(vertices[indices[i + 1]], color);
                AddVertex(vertices[indices[i + 2]], color);
            }
        }

        public void End()
        {
            if (!_hasBegun)
                throw new InvalidOperationException("Begin must be called before End can be called.");

            Flush();
            _hasBegun = false;
        }

        private void Flush()
        {
            if (!_hasBegun || _positionInBuffer == 0)
                return;

            // Bestimme den PrimitiveType basierend auf der Anzahl der Vertices
            PrimitiveType primitiveType;
            int primitiveCount;

            if (_positionInBuffer % 3 == 0) // Für FillPolygon (Dreiecke)
            {
                primitiveType = PrimitiveType.TriangleList;
                primitiveCount = _positionInBuffer / 3;
            }
            else if (_positionInBuffer % 4 == 0) // für DrawLine (Rechtecke aus 2 Dreiecken)
            {
                primitiveType = PrimitiveType.TriangleList;
                primitiveCount = _positionInBuffer / 6; // KORREKT: Durch 6 teilen (2 Dreiecke * 3 Vertices/Dreieck)
            }
            else // Für Liniensegmente (Umrisse, dünne Linien)
            {
                primitiveType = PrimitiveType.LineList;
                primitiveCount = _positionInBuffer / 2;
            }

            _graphicsDevice.DrawUserPrimitives(primitiveType, _vertices, 0, primitiveCount);
            _positionInBuffer = 0;
        }

        // Triangulierung (Ear Clipping Algorithmus) - Hilfsmethode für FillPolygon
        private int[] Triangulate(Vector2[] vertices)
        {
            List<int> indices = new List<int>();
            List<int> remainingVertices = new List<int>();

            // Initialisiere die Liste der verbleibenden Vertices
            for (int i = 0; i < vertices.Length; i++)
            {
                remainingVertices.Add(i);
            }

            // Hauptschleife des Ear Clipping Algorithmus
            while (remainingVertices.Count > 2)
            {
                int earVertexIndex = -1;

                // Finde den nächsten "Ear" Vertex
                for (int i = 0; i < remainingVertices.Count; i++)
                {
                    int prevIndex = (i - 1 + remainingVertices.Count) % remainingVertices.Count;
                    int currentIndex = i;
                    int nextIndex = (i + 1) % remainingVertices.Count;

                    if (IsEar(vertices, remainingVertices[prevIndex], remainingVertices[currentIndex], remainingVertices[nextIndex]))
                    {
                        earVertexIndex = currentIndex;
                        break;
                    }
                }

                if (earVertexIndex == -1)
                {
                    // Keine Ohren gefunden (Fehlerfall)
                    Console.WriteLine("Triangulation failed: No ears found.");
                    return null;
                }

                // Füge das Dreieck hinzu
                indices.Add(remainingVertices[(earVertexIndex - 1 + remainingVertices.Count) % remainingVertices.Count]);
                indices.Add(remainingVertices[earVertexIndex]);
                indices.Add(remainingVertices[(earVertexIndex + 1) % remainingVertices.Count]);

                // Entferne den "Ear" Vertex
                remainingVertices.RemoveAt(earVertexIndex);
            }

            // Füge das letzte Dreieck hinzu
            indices.Add(remainingVertices[0]);
            indices.Add(remainingVertices[1]);
            indices.Add(remainingVertices[2]);

            return indices.ToArray();
        }

        // Hilfsmethode für Triangulate: Überprüft, ob ein Vertex ein "Ear" ist
        private bool IsEar(Vector2[] vertices, int prevIndex, int currentIndex, int nextIndex)
        {
            Vector2 a = vertices[prevIndex];
            Vector2 b = vertices[currentIndex];
            Vector2 c = vertices[nextIndex];

            // Überprüfe, ob das Dreieck im Uhrzeigersinn orientiert ist
            if (CrossProduct(b - a, c - a) < 0)
                return false;

            // Überprüfe, ob andere Punkte innerhalb des Dreiecks liegen
            for (int i = 0; i < vertices.Length; i++)
            {
                if (i != prevIndex && i != currentIndex && i != nextIndex)
                {
                    if (IsPointInTriangle(vertices[i], a, b, c))
                        return false;
                }
            }

            return true;
        }

        // Hilfsmethode: Kreuzprodukt für 2D-Vektoren
        private float CrossProduct(Vector2 a, Vector2 b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        // Hilfsmethode: Überprüft, ob ein Punkt innerhalb eines Dreiecks liegt
        private bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float areaABC = CrossProduct(b - a, c - a);
            float areaPBC = CrossProduct(b - p, c - p);
            float areaPCA = CrossProduct(c - p, a - p);

            return (areaPBC >= 0 && areaPCA >= 0 && (areaPBC + areaPCA) <= areaABC);
        }
    }
}