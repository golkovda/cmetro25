using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace cmetro25.Primitives
{
    public static class BezierHelper
    {
        //Used for generating the mesh for the curve
        //First object is vertex data, second is indices (both as arrays)
        //Farbparameter hinzugefügt
        public static object[] ComputeCurve3D(List<Vector2> points3D, float curveWidth, int steps, Color color)
        {
            List<VertexPositionColor> path = new List<VertexPositionColor>(); // Verwende VertexPositionColor
            List<int> indices = new List<int>();

            List<Vector2> curvePoints = new List<Vector2>();
            for (float x = 0; x < 1; x += 1 / (float)steps)
            {
                curvePoints.Add(GetBezierPointRecursive(x, points3D.ToArray()));
            }

            //float curveWidth = 0.003f; //Breite rausgezogen

            for (int x = 0; x < curvePoints.Count; x++)
            {
                Vector2 normal;

                if (x == 0)
                {
                    //First point, Take normal from first line segment
                    normal = GetNormalizedVector(GetLineNormal(curvePoints[x + 1] - curvePoints[x]));
                }
                else if (x + 1 == curvePoints.Count)
                {
                    //Last point, take normal from last line segment
                    normal = GetNormalizedVector(GetLineNormal(curvePoints[x] - curvePoints[x - 1]));
                }
                else
                {
                    //Middle point, interpolate normals from adjacent line segments
                    normal = GetNormalizedVertexNormal(GetLineNormal(curvePoints[x] - curvePoints[x - 1]), GetLineNormal(curvePoints[x + 1] - curvePoints[x]));
                }

                path.Add(new VertexPositionColor(new Vector3(curvePoints[x] + normal * curveWidth, 0), color)); // Farbe verwenden
                path.Add(new VertexPositionColor(new Vector3(curvePoints[x] + normal * -curveWidth, 0), color)); // Farbe verwenden
            }

            for (int x = 0; x < curvePoints.Count - 1; x++)
            {
                indices.Add(2 * x + 0);
                indices.Add(2 * x + 1);
                indices.Add(2 * x + 2);

                indices.Add(2 * x + 1);
                indices.Add(2 * x + 3);
                indices.Add(2 * x + 2);
            }

            return
            [
                path.ToArray(),
                indices.ToArray()
            ];
        }

        //Recursive algorithm for getting the bezier curve points 
        private static Vector2 GetBezierPointRecursive(float timeStep, Vector2[] ps)
        {

            if (ps.Length > 2)
            {
                List<Vector2> newPoints = new List<Vector2>();
                for (int x = 0; x < ps.Length - 1; x++)
                {
                    newPoints.Add(InterpolatedPoint(ps[x], ps[x + 1], timeStep));
                }
                return GetBezierPointRecursive(timeStep, newPoints.ToArray());
            }
            else
            {
                return InterpolatedPoint(ps[0], ps[1], timeStep);
            }
        }

        //Gets the interpolated Vector2 based on t
        private static Vector2 InterpolatedPoint(Vector2 p1, Vector2 p2, float t)
        {
            return Vector2.Multiply(p2 - p1, t) + p1;
        }

        //Gets the normalized normal of a vertex, given two adjacent normals (2D)
        private static Vector2 GetNormalizedVertexNormal(Vector2 v1, Vector2 v2) //v1 and v2 are normals
        {
            return GetNormalizedVector(v1 + v2);
        }

        //Normalizes the given Vector2
        private static Vector2 GetNormalizedVector(Vector2 v)
        {
            Vector2 temp = new Vector2(v.X, v.Y);
            v.Normalize();
            return v;
        }

        //Gets the normal of a given Vector2
        private static Vector2 GetLineNormal(Vector2 v)
        {
            Vector2 normal = new Vector2(v.Y, -v.X);
            return normal;
        }
    }
}
