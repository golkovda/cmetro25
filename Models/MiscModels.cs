using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cmetro25.Models
{
    // ... (Deine Klassen Crs, Feature, Geometry, Properties, Root) ...
    public class Crs
    {
        public string type { get; set; }
        public Properties properties { get; set; }
    }

    public class Feature
    {
        public string type { get; set; }
        public Properties properties { get; set; }
        public Geometry geometry { get; set; }
    }

    public class Geometry
    {
        public string type { get; set; }

        [JsonProperty("coordinates")]
        public object Coordinates { get; set; } // WICHTIG: object, da der Typ variiert

        // Hilfsmethoden, um den Zugriff zu vereinfachen:
        public List<List<List<List<double>>>> CoordsAsMultiPolygonString() => (Coordinates as JToken)?.ToObject<List<List<List<List<double>>>>>();
        public List<List<List<double>>> CoordsAsMultiLineString() => (Coordinates as JToken)?.ToObject<List<List<List<double>>>>();
        public List<List<double>> CoordsAsLineString() => (Coordinates as JToken)?.ToObject<List<List<double>>>();
    }

    public class Properties
    {
        public string name { get; set; }
        public string id { get; set; }
        public string highway { get; set; }
        public string maxspeed { get; set; }

        [JsonProperty("@id")]
        public string at_id { get; set; }
        public string admin_level { get; set; }
        public string admin_title { get; set; }
        public string boundary { get; set; }

        [JsonProperty("name:prefix")]
        public string nameprefix { get; set; }
        public string @ref { get; set; }
        public string source { get; set; }
        public string type { get; set; }
        public string wikidata { get; set; }
        public string wikipedia { get; set; }
        public int stat_area { get; set; }
        public string id_2 { get; set; }

        [JsonProperty("@id_2")]
        public string at_id_2 { get; set; }

        [JsonProperty("@relations")]
        public object relations { get; set; }
        public string admin_level_2 { get; set; }
        public string admin_title_2 { get; set; }
        public string boundary_2 { get; set; }

        [JsonProperty("de:amtlicher_gemeindeschluessel")]
        public string deamtlicher_gemeindeschluessel { get; set; }
        public string name_2 { get; set; }

        [JsonProperty("name:prefix_2")]
        public string nameprefix_2 { get; set; }
        public string population { get; set; }

        [JsonProperty("population:date")]
        public string populationdate { get; set; }
        public string source_2 { get; set; }

        [JsonProperty("source:population")]
        public string sourcepopulation { get; set; }
        public string natural { get; set; }
        public string water { get; set; }
        public string type_2 { get; set; }
        public string wikidata_2 { get; set; }
        public string wikipedia_2 { get; set; }
        public int admin_area { get; set; }
        public int intersect_area { get; set; }
        public double area_ratio { get; set; }
        public int pop_stat { get; set; }
        public double centroid_x { get; set; }
        public double centroid_y { get; set; }
    }

    public class Root
    {
        public string type { get; set; }
        public string name { get; set; }
        public Crs crs { get; set; }
        public List<Feature> features { get; set; }
    }
}
