using Microsoft.Xna.Framework; // Wird für RectangleF benötigt, wenn MonoGame.Extended nicht global using ist
using MonoGame.Extended; // Für RectangleF
using System; // Für Math
using System.Collections.Generic;
using System.Linq; // Für Any()

namespace cmetro25.Utils
{
    public class Quadtree<T>
    {
        // Interne Struktur zum Speichern von Elementen und ihren Grenzen
        private struct QuadtreeItem(T item, RectangleF bounds)
        {
            public T Item = item;
            public RectangleF Bounds = bounds;
        }

        public RectangleF Bounds { get; } // Die Grenzen dieses Knotens (readonly nach Konstruktion)
        private readonly int _maxItems; // Maximale Elemente pro Knoten, bevor unterteilt wird
        private readonly int _maxDepth; // Maximale Tiefe des Baumes

        // Elemente, die direkt in diesem Knoten gespeichert sind (nicht in Kinderknoten passen)
        private readonly List<QuadtreeItem> _items = new List<QuadtreeItem>();

        // Kinderknoten (null, wenn dies ein Blattknoten ist)
        private Quadtree<T>[] _children;
        private bool IsLeaf => _children == null;

        // Zählt alle Elemente im Baum (rekursiv)
        public int Count
        {
            get
            {
                int count = _items.Count;
                if (!IsLeaf)
                {
                    foreach (var child in _children)
                    {
                        count += child.Count;
                    }
                }
                return count;
            }
        }

        public Quadtree(RectangleF bounds, int maxItems = 4, int maxDepth = 8) // MaxDepth angepasst, 10 kann sehr tief sein
        {
            // Stelle sicher, dass die Grenzen gültig sind
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                // Fallback oder Exception? Wir nehmen einen kleinen Fallback.
                bounds = new RectangleF(bounds.X, bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
                // Oder: throw new ArgumentException("Quadtree bounds must have positive width and height.");
            }

            Bounds = bounds;
            _maxItems = maxItems;
            _maxDepth = maxDepth;
        }

        // Fügt ein Element mit seinen Grenzen in den Baum ein
        public void Insert(T item, RectangleF itemBounds)
        {
            // Ignoriere Elemente, die überhaupt nicht in die Grenzen dieses Knotens fallen
            // OPTIMIERUNG: Prüfe auf Intersects statt Contains(Position)
            if (!Bounds.Intersects(itemBounds))
            {
                return; // Passt nicht hierher
            }

            // Wenn dies ein Blattknoten ist...
            if (IsLeaf)
            {
                // Füge das Element hier hinzu
                _items.Add(new QuadtreeItem(item, itemBounds));

                // Prüfe, ob unterteilt werden muss (und kann)
                if (_items.Count > _maxItems && _maxDepth > 0)
                {
                    Subdivide();

                    // Verschiebe Elemente von diesem Knoten in die neuen Kinderknoten, wenn möglich
                    PushItemsDown();
                }
            }
            // Wenn dies kein Blattknoten ist...
            else
            {
                // Versuche, das Element in einen passenden Kinderknoten einzufügen
                int targetChildIndex = GetTargetChildIndex(itemBounds);

                // Wenn es vollständig in einen Kinderknoten passt...
                if (targetChildIndex != -1)
                {
                    _children[targetChildIndex].Insert(item, itemBounds);
                }
                // Wenn es die Grenzen von Kindern überschneidet...
                else
                {
                    // Füge es direkt zu diesem Knoten hinzu
                    _items.Add(new QuadtreeItem(item, itemBounds));
                }
            }
        }

        // Fragt den Baum nach Elementen ab, deren Grenzen den angegebenen Bereich schneiden
        public List<T> Query(RectangleF area)
        {
            List<T> result = new List<T>();
            QueryRecursive(area, result);
            return result;
        }

        // Rekursive Hilfsmethode für die Abfrage
        private void QueryRecursive(RectangleF area, List<T> result)
        {
            // 1. Prüfe, ob der Abfragebereich diesen Knoten überhaupt schneidet
            if (!Bounds.Intersects(area))
            {
                return; // Kein Treffer in diesem Zweig
            }

            // 2. Füge Elemente hinzu, die direkt in diesem Knoten gespeichert sind und den Bereich schneiden
            // WICHTIG: Auch in Nicht-Blattknoten können Elemente gespeichert sein!
            foreach (var qtItem in _items)
            {
                if (qtItem.Bounds.Intersects(area))
                {
                    result.Add(qtItem.Item);
                }
            }

            // 3. Wenn es Kinderknoten gibt, frage diese rekursiv ab
            if (!IsLeaf)
            {
                foreach (var child in _children)
                {
                    child.QueryRecursive(area, result);
                }
            }
        }


        // Unterteilt diesen Knoten in vier Kinderknoten
        private void Subdivide()
        {
            if (!IsLeaf) return; // Sollte nicht passieren, aber sicher ist sicher

            float halfWidth = Bounds.Width / 2;
            float halfHeight = Bounds.Height / 2;
            float x = Bounds.X;
            float y = Bounds.Y;
            int nextDepth = _maxDepth - 1;

            _children = new Quadtree<T>[4];
            // Reihenfolge: Oben-Links, Oben-Rechts, Unten-Links, Unten-Rechts
            _children[0] = new Quadtree<T>(new RectangleF(x, y, halfWidth, halfHeight), _maxItems, nextDepth);
            _children[1] = new Quadtree<T>(new RectangleF(x + halfWidth, y, halfWidth, halfHeight), _maxItems, nextDepth);
            _children[2] = new Quadtree<T>(new RectangleF(x, y + halfHeight, halfWidth, halfHeight), _maxItems, nextDepth);
            _children[3] = new Quadtree<T>(new RectangleF(x + halfWidth, y + halfHeight, halfWidth, halfHeight), _maxItems, nextDepth);
        }

        // Verschiebt Elemente von diesem Knoten in passende Kinderknoten nach einer Unterteilung
        private void PushItemsDown()
        {
            if (IsLeaf) return; // Keine Kinder zum Verschieben

            List<QuadtreeItem> itemsToKeep = new List<QuadtreeItem>();

            foreach (var qtItem in _items)
            {
                int targetChildIndex = GetTargetChildIndex(qtItem.Bounds);
                if (targetChildIndex != -1)
                {
                    // Element passt vollständig in ein Kind -> Verschieben
                    _children[targetChildIndex].Insert(qtItem.Item, qtItem.Bounds);
                }
                else
                {
                    // Element überschneidet Grenzen -> Bleibt in diesem Knoten
                    itemsToKeep.Add(qtItem);
                }
            }

            // Ersetze die alte Item-Liste durch die Liste der Elemente, die hier bleiben müssen
            _items.Clear();
            _items.AddRange(itemsToKeep);
        }

        // Ermittelt den Index des Kinderknotens, in den ein Element vollständig passt.
        // Gibt -1 zurück, wenn es in keinen vollständig passt (Grenzen überschneidet).
        private int GetTargetChildIndex(RectangleF itemBounds)
        {
            if (IsLeaf) return -1; // Keine Kinder vorhanden

            int targetIndex = -1;
            for (int i = 0; i < _children.Length; i++)
            {
                RectangleF childBounds = _children[i].Bounds;

                // --- KORREKTUR: Manuelle Prüfung auf vollständige Enthaltung ---
                if (itemBounds.Left >= childBounds.Left &&
                    itemBounds.Right <= childBounds.Right &&
                    itemBounds.Top >= childBounds.Top &&
                    itemBounds.Bottom <= childBounds.Bottom)
                {
                    // Das itemBounds liegt vollständig innerhalb der childBounds
                    targetIndex = i;
                    break; // Gefunden, passt nur in dieses eine Kind
                }
                // --- Ende Korrektur ---
            }
            return targetIndex;
        }

        // Entfernt alle Elemente und Kinderknoten aus dem Baum
        public void Clear()
        {
            _items.Clear();
            if (!IsLeaf)
            {
                foreach (var child in _children)
                {
                    child.Clear();
                }
            }
            _children = null; // Mache diesen Knoten wieder zu einem Blattknoten
        }
    }
}