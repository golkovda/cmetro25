using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cmetro25.Utils
{    public static class TextUtils
    {
        public static float CalculateTextScale(float zoom, float baseZoom, float minScale, float maxScale)
        {
            float scale = baseZoom / zoom;
            return Math.Max(minScale, Math.Min(scale, maxScale));
        }
    }
}
