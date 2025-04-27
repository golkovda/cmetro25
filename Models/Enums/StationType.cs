using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cmetro25.Models.Enums
{
    /// <summary>
    /// Kombinierbare Typen für eine Station.  [Flags] erlaubt Mischformen.
    /// </summary>
    [Flags]
    public enum StationType
    {
        None = 0,
        Bus = 1 << 0,
        Tram = 1 << 1,
        Subway = 1 << 2
    }
}
