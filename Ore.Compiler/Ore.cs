using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ore.Compiler
{
    [JsonObject(MemberSerialization.OptIn)]
    public class Ore
    {
        public static Ore Empty => new Ore();

        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("major")]
        public int MajorVersion { get; set; }
        [JsonProperty("minor")]
        public int MinorVersion { get; set; }
        [JsonProperty("build")]
        public int BuildVersion { get; set; }
        [JsonProperty("dependencies")]
        public JArray Dependencies { get; set; }
        public bool AutoIncrement { get; set; }

        public static Ore GetPrevious()
        {
            try
            {
                return JsonConvert.DeserializeObject<Ore>(File.ReadAllText(".ore"));
            }
            catch
            {
                return Empty;
            }
        }

        public void Persist()
        {
            File.WriteAllText(".ore", JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }
}
