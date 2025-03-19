using MonoGame.Extended;
using System.Collections.Generic;

namespace cmetro25.Utils
{
    public class Quadtree<T>(RectangleF bounds, int maxItems = 4, int maxDepth = 10)
    {
        private RectangleF _bounds = bounds;
        private readonly List<(T item, RectangleF bounds)> _items = new List<(T item, RectangleF bounds)>();
        private Quadtree<T>[] _children;

        public void Insert(T item, RectangleF itemBounds)
        {
            // Wenn das Item nicht vollständig in diesen Knoten passt, füge es hier ein.
            if (!_bounds.Contains(itemBounds.Position))
            {
                _items.Add((item, itemBounds));
                return;
            }

            // Falls bereits unterteilt, prüfen, ob es komplett in einen der Kinder passt.
            if (_children != null)
            {
                foreach (var child in _children)
                {
                    if (child._bounds.Contains(itemBounds.Position))
                    {
                        child.Insert(item, itemBounds);
                        return;
                    }
                }
            }

            // Wenn es in keinen der Kinder passt (oder noch nicht unterteilt), füge es in den aktuellen Knoten ein.
            _items.Add((item, itemBounds));


            // Unterteile, wenn nötig
            if (_items.Count > maxItems && maxDepth > 0)
            {
                if (_children == null)
                {
                    Subdivide();
                }
                // Versuche, bereits vorhandene Items in passende Kinder zu verschieben.
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    var (existingItem, existingBounds) = _items[i];
                    foreach (var child in _children)
                    {
                        if (child._bounds.Contains(existingBounds.Position))
                        {
                            child.Insert(existingItem, existingBounds);
                            _items.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
        }


        public List<T> Query(RectangleF area)
        {
            List<T> result = new List<T>();
            if (!_bounds.Intersects(area))
                return result;

            if (_children == null)
            {
                foreach (var (item, bounds) in _items)
                {
                    if (bounds.Intersects(area))
                        result.Add(item);
                }
            }
            else
            {
                foreach (var child in _children)
                {
                    result.AddRange(child.Query(area));
                }
            }
            return result;
        }

        private void Subdivide()
        {
            _children = new Quadtree<T>[4];
            float halfWidth = _bounds.Width / 2;
            float halfHeight = _bounds.Height / 2;
            _children[0] = new Quadtree<T>(new RectangleF(_bounds.X, _bounds.Y, halfWidth, halfHeight), maxItems, maxDepth - 1);
            _children[1] = new Quadtree<T>(new RectangleF(_bounds.X + halfWidth, _bounds.Y, halfWidth, halfHeight), maxItems, maxDepth - 1);
            _children[2] = new Quadtree<T>(new RectangleF(_bounds.X, _bounds.Y + halfHeight, halfWidth, halfHeight), maxItems, maxDepth - 1);
            _children[3] = new Quadtree<T>(new RectangleF(_bounds.X + halfWidth, _bounds.Y + halfHeight, halfWidth, halfHeight), maxItems, maxDepth - 1);
        }
    }
}
