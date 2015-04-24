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
        public static void InstallPlugin(Ore ore, byte[] buffer)
        {
            var installDirectory = "C:\\ores\\" + ore.Name + "\\";
            Directory.CreateDirectory(installDirectory);
            Program.WriteLine("Writing ore data to library...");
            File.WriteAllText(installDirectory + "data.ore", JObject.FromObject(ore).ToString(Formatting.Indented));
            DecompilePluginBuffer(buffer, installDirectory, ore);
        }

        public static void DecompilePluginBuffer(byte[] buffer, string directory, Ore ore)
        {
            Program.WriteLine("Beginning decompile process...");
            // for compiling the ore, there are certain steps.
            // 1: compile the solution. 
            // 2: compress the library.                 (read length, name, then library)
            // 3: compress the dependencies.            (read count of deps, foreach {read length, name, then dep})

            // now lets reverse the library
            try
            { 
                using (var ms = new MemoryStream(ZlibStream.UncompressBuffer(buffer))) 
                {
                    Program.WriteLine("Reading plugin library...");
                    var ol = ms.ReadInt(); // ore length
                    var of = ms.ReadUByteArray(ol);
                    File.WriteAllBytes(directory + ore.Name + ".dll", of);
                    Program.WriteLine("Finished installing plugin.");

                    var dc = ore.Dependencies.Count;
                    Program.WriteLine("Installing {0} dependencies.", dc);
                    for (var i = 0; i < dc; i++)
                    {
                        var dl = ms.ReadInt();
                        var df = ms.ReadUByteArray(dl);
                        Program.WriteLine("Copying {0}...", ore.Dependencies[i]);

                        File.WriteAllBytes(directory + ore.Dependencies[i], df);
                    }
                    Program.WriteLine("Finished installing dependencies.");
                    Program.WriteLine("Installation successful.");
                }
            }
            catch(Exception e)
            {
                Program.WriteLine("An error occured and installation has been halted with the error: {0}.", e.StackTrace);
            }
        }
    }
}
