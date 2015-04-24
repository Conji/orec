using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ore.Compiler.Zlib;

namespace Ore.Compiler
{
    public static class Decompiler
    {
        public static void InstallPlugin(JObject json, byte[] buffer)
        {
            var installDirectory = "C:\\ores\\" + json["name"];
            Program.WriteLine("Writing ore data to library...");
            File.WriteAllText(installDirectory + "data.ore", json.ToString(Formatting.Indented));
            DecompilePluginBuffer(buffer, installDirectory);
        }

        public static void DecompilePluginBuffer(byte[] buffer, string directory)
        {
            Program.WriteLine("Beginning decompile process...");
            // for compiling the ore, there are certain steps.
            // 1: compile the solution. 
            // 2: compress the library.                 (read length, name, then library)
            // 3: compress the dependencies.            (read count of deps, foreach {read length, name, then dep})

            // now lets reverse the library
            try
            { 
                using (var ms = new MemoryStream(buffer)) // figure out why this stops
                {
                    Program.WriteLine("Reading plugin library...");
                    var ol = ms.ReadInt(); // ore length
                    Console.WriteLine(ol);
                    var on = ms.ReadString(); // ore name
                    Console.WriteLine(on);
                    var of = ms.ReadUByteArray(ol - 4);
                    File.WriteAllBytes(directory + on + ".dll", of);
                    Program.WriteLine("Finished installing plugin.");

                    var dc = ms.ReadUByte(); // dependency count
                    Program.WriteLine("Installing {0} dependencies for {1}.", dc, on);
                    for (var i = 0; i < dc; i++)
                    {
                        var dl = ms.ReadInt();
                        var dn = ms.ReadString();
                        var df = ms.ReadUByteArray(dl - 4);
                        Program.WriteLine("Copying {0}...", dn);

                        File.WriteAllBytes(directory + dn + ".dll", df);
                    }
                    Program.WriteLine("Finished installing dependencies.");
                    Program.WriteLine("Installation successful.");
                }
            }
            catch(Exception e)
            {
                Program.WriteLine("An error occured and installation has been halted with the error: {0}.", e.Message);
            }
        }
    }
}
