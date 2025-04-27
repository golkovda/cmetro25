using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using cmetro25.Models.Enums;
using Microsoft.Xna.Framework;

namespace cmetro25.Models
{
    /// <summary>
    /// Datenhaltung einer einzelnen Station.
    /// </summary>
    public sealed record Station : PointElement
    {
        public string Name { get; set; }
        public StationType Modes { get; set; }

        public Station(Vector2 pos, string name, StationType modes)
            : base("station", pos)
        {
            Name = name;
            Modes = modes;
        }
    }
}
