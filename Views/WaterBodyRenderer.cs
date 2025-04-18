// --- START OF FILE WaterBodyRenderer.cs ---

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using cmetro25.Models;
using LibTessDotNet;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
// Für ToArray(), Any() etc.
// Für MapCamera

// NEU: Für die Triangulierung

namespace cmetro25.Views;

public class WaterBodyRenderer
{
    private readonly GraphicsDevice _graphicsDevice; // NEU: Wird benötigt
    private readonly BasicEffect _basicEffect; // NEU: Für das Zeichnen
    private readonly Color _waterColor;


    // NEU: Konstruktor benötigt GraphicsDevice
    public WaterBodyRenderer(GraphicsDevice graphicsDevice, Color waterColor)
    {
        _graphicsDevice = graphicsDevice;
        _waterColor = waterColor;

        // BasicEffect initialisieren
        _basicEffect = new BasicEffect(graphicsDevice);
        _basicEffect.VertexColorEnabled = true; // Wichtig: Wir verwenden Vertex-Farben
        _basicEffect.TextureEnabled = false; // Keine Textur
        _basicEffect.LightingEnabled = false; // Keine Beleuchtung
    }

    /// <summary>
    ///     Zeichnet die übergebenen Wasserflächen durch Triangulierung.
    ///     Muss außerhalb eines SpriteBatch.Begin/End-Blocks aufgerufen werden.
    /// </summary>
    public void Draw(List<WaterBody> waterBodies, MapCamera camera)
    {
        if (waterBodies == null || !waterBodies.Any()) return;


        // 1. Deine SpriteBatch‑Matrix (World → Screen‑Pixel)
        var worldToScreen = camera.TransformMatrix;

        // 2. Screen‑Pixel (0..W / 0..H) → Clip‑Space (‑1..1)
        var screenToClip = Matrix.CreateOrthographicOffCenter(
            0, camera.ViewportWidth, // left / right
            camera.ViewportHeight, 0, // bottom / top  (Y‑Down!)
            0.0f, 1f); // near / far

        _basicEffect.World = Matrix.Identity;
        _basicEffect.View = Matrix.Identity; // nichts mehr verschieben
        _basicEffect.Projection = worldToScreen * screenToClip;

        _graphicsDevice.RasterizerState = RasterizerState.CullNone;

        // Wende den Effekt an
        foreach (var pass in _basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();

            foreach (var wb in waterBodies)
                // Optional: Culling basierend auf Kamera-Bounds
                // if (!camera.BoundingRectangle.Intersects(wb.BoundingBox)) continue;
                foreach (var polygon in wb.Polygons)
                    if (polygon != null && polygon.Count > 2)
                    {
                        Vector2 scr = camera.WorldToScreen(polygon[0]);
                        // Trianguliere das Polygon
                        var tess = TriangulatePolygon(polygon);
                        if (tess != null && tess.ElementCount > 0)
                        {
                            // Konvertiere das Ergebnis in VertexPositionColor und short[]
                            var vertices = ConvertTessVertices(tess.Vertices);
                            var indices = ConvertTessElements(tess.Elements);

                            if (vertices.Length > 0 && indices.Length > 0)
                                // Zeichne die triangulierten Polygone
                                _graphicsDevice.DrawUserIndexedPrimitives(
                                    PrimitiveType.TriangleList,
                                    vertices, // Die Vertex-Daten
                                    0, // Start-Vertex-Index
                                    vertices.Length, // Anzahl der Vertices
                                    indices, // Die Index-Daten
                                    0, // Start-Index-Index
                                    indices.Length / 3 // Anzahl der zu zeichnenden Dreiecke (Indices / 3)
                                );
                        }
                    }
        }
    }

    // --- NEU: Hilfsmethoden für Triangulierung ---

    private Tess TriangulatePolygon(List<Vector2> polygonPoints)
    {
        try
        {
            var tess = new Tess();
            var contour = new ContourVertex[polygonPoints.Count];
            for (var i = 0; i < polygonPoints.Count; i++)
                // LibTess erwartet Koordinaten in seiner eigenen Struktur. Z = 0 für 2D.
                contour[i] = new ContourVertex { Position = new Vec3(polygonPoints[i].X, polygonPoints[i].Y, 0) };
            //Array.Reverse(contour);
            tess.AddContour(contour, ContourOrientation.Clockwise); // Annahme: Orientierung ist korrekt
            // Führe Triangulierung durch
            // WindingRule.EvenOdd ist oft gut für Geodaten-Polygone
            // ElementType.Polygons gibt Dreiecke zurück
            tess.Tessellate(); // 3 Vertices pro Polygon (Dreieck)

            return tess;
        }
        catch (Exception ex)
        {
            var pointsPreview = string.Join(", ", polygonPoints.Take(5).Select(p => $"({p.X:F1},{p.Y:F1})"));
            Debug.WriteLine(
                $"[ERROR] Triangulation failed for polygon starting with [{pointsPreview}...]: {ex.Message}");
            return null;
        }
    }

    private VertexPositionColor[] ConvertTessVertices(ContourVertex[] tessVertices)
    {
        // Die Länge des Arrays ist korrekt
        var vertices = new VertexPositionColor[tessVertices.Length];
        for (var i = 0; i < tessVertices.Length; i++)
        {
            // Greife auf die .Position Eigenschaft zu, die Vec3 ist
            var position = tessVertices[i].Position;
            vertices[i] = new VertexPositionColor(
                new Vector3(position.X, position.Y, position.Z), // Konvertiere Vec3 zu Vector3
                _waterColor // Weise allen Vertices die Wasserfarbe zu
            );
        }

        return vertices;
    }

    private short[] ConvertTessElements(int[] tessElements)
    {
        // LibTess gibt int[] zurück, DrawUserIndexedPrimitives braucht short[]
        var indices = new short[tessElements.Length];
        for (var i = 0; i < tessElements.Length; i++)
            // Prüfe auf ungültigen Index (-1), den LibTess manchmal zurückgibt
            if (tessElements[i] < 0 || tessElements[i] > short.MaxValue)
            {
                Debug.WriteLine($"[Warning] Invalid index from LibTess: {tessElements[i]} at position {i}");
                // Was tun? Oft kann man das Dreieck ignorieren, indem man Indizes setzt, die ein degeneriertes Dreieck bilden (z.B. 0,0,0)
                // Hier setzen wir sie einfach auf 0, was zu visuellen Fehlern führen KANN, aber Abstürze vermeidet.
                indices[i] = 0;
            }
            else
            {
                indices[i] = (short)tessElements[i];
            }

        return indices;
    }
}
// --- END OF FILE WaterBodyRenderer.cs ---