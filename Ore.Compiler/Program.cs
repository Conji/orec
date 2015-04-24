using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ore.Compiler.Zlib;

namespace Ore.Compiler
{
    class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("===================Eureka Ore Compiler===================");
            Console.WriteLine();
            switch (args.Length)
            {
                case 0:
                    New(Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj", SearchOption.TopDirectoryOnly).First());
                    break;
                default:
                    if (args.Contains("--restart"))
                    {
                        File.Delete(".ore");
                        New(Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj", SearchOption.TopDirectoryOnly).First());
                    }
                    if (args.Contains("--project"))
                    {
                        var arg = args.First(a => a.StartsWith("--project="));
                        New(arg.Split('=')[1]);
                    }
                    if (args.Contains("--pack-only"))
                    {
                        New(Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj", SearchOption.TopDirectoryOnly).First(), false);
                    }
                    break;
            }

        }

        private static void New(string f, bool install = true)
        {
            var file = f.Split('\\').Last();
            CompressionAssistant.CompileSolution(file);
            WriteLine("Compiled solution. Compressing.");
            WriteLine("Compressing library...");
            var fileBuffer = CompressionAssistant.CompressExecutable();
            WriteLine("Compressing dependencies...");
            var dependencyBuffer = CompressionAssistant.CompressDependencies();

            Ore ore;
            if (!File.Exists(".ore")) CreateNewOre(file, out ore);
            else UpdateOre(out ore);
            ore.Dependencies = CompressionAssistant.CommitDependencies();
            WriteLine("Ore data finished gathering...");
            ore.Persist();

            var mem = new MemoryStream();
            // write the plugin buffer
            WriteLine("Buffing file data...");
            mem.WriteUByteArray(fileBuffer);

            // write the compressed dependency buffer
            WriteLine("Buffing dependency data...");
            mem.WriteUByteArray(dependencyBuffer);

            var buffer = ZlibStream.CompressBuffer(mem.GetBuffer());

            File.WriteAllBytes(".filebuf", buffer);
            if (!install) return;
            WriteLine("Running install test...");
            Decompiler.InstallPlugin(ore, File.ReadAllBytes(".filebuf"));
        }

        public static void WriteLine(string input, params object[] args)
        {
            Console.Write("\b[orec 1.02]: >  ");
            Console.WriteLine(input, args);
        }

        private static void CreateNewOre(string file, out Ore ore)
        {
            var f = File.Create(".ore");
            f.Dispose();
            ore = new Ore();
            WriteLine("So we noticed this is a new Ore. Would you like to keep the name " + file.Replace(".csproj", "") + "? [y/n]");
            switch (Console.ReadKey().KeyChar)
            {
                case 'y':
                    ore.Name = file.Replace(".csproj", "");
                    break;
                case 'n':
                    WriteLine("Ok, what would you like to name this?");
                    ore.Name = Console.ReadLine();
                    break;
                default:
                    WriteLine("We noticed you didn't say 'y' or 'n', so we'll just assume 'yes'. You can change it later in the .ore file if you'd like.");
                    break;
            }
            #region Version managing
            WriteLine("In Ores, we have an online version controller. Would you like to use this?[y/n]");
            switch (Console.ReadKey().KeyChar)
            {
                case 'y':
                    WriteLine("Great! Now we'll just ask a few questions to get it situated.");

                    WriteLine("What's the major version? (If it's your first release, we'd suggest putting 1.)");
                    var major = Console.ReadLine();
                    int ma;
                    if (string.IsNullOrWhiteSpace(major) || !int.TryParse(major, out ma))
                    {
                        WriteLine("We'll just put '1'.");
                        ma = 1;
                    }
                    else WriteLine("Ok, great.");

                    WriteLine("What's the minor version?");
                    var minor = Console.ReadLine();
                    int mi;
                    if (string.IsNullOrWhiteSpace(minor) || !int.TryParse(minor, out mi))
                    {
                        WriteLine("We'll just put '1'.");
                        mi = 1;
                    }
                    else WriteLine("Ok, great.");

                    WriteLine("What's the build version?");
                    var build = Console.ReadLine();
                    int b;
                    if (string.IsNullOrWhiteSpace(build) || !int.TryParse(build, out b))
                    {
                        WriteLine("We'll just put '1'.");
                        b = 1;
                    }
                    else WriteLine("Ok, great.");

                    WriteLine("We have the versioning info as: {0}.{1}.{3}. If you'd like to change this, you can always open '.ore' in a text editor.", ma, mi, b);
                    ore.MajorVersion = ma;
                    ore.MinorVersion = mi;
                    ore.BuildVersion = b;
                    break;
                case 'n':
                    WriteLine("Alright. If you change your mind, then we can do it later.");
                    break;
                default:
                    WriteLine("That wasn't a choice, so we'll just avoid it for now. If you'd like to use it, you can reference the site for help.");
                    break;
            }
            #endregion
        }

        private static void UpdateOre(out Ore ore)
        {
            ore = Ore.GetPrevious();
            WriteLine("Updating the Ore \"{0}\"...", ore.Name);
            WriteLine("The last version was {0}.{1}.{2}. Would you like to increment it?", ore.MajorVersion, ore.MinorVersion, ore.BuildVersion);
            if (Console.ReadKey().KeyChar == 'y')
            {
                WriteLine("Ok. What's the new version?");
                var result = Console.ReadLine();
                ore.MajorVersion = int.Parse(result.Split('.')[0]);
                ore.MinorVersion = int.Parse(result.Split('.')[1]);
                if (result.Split('.').Length == 3)
                    ore.BuildVersion = int.Parse(result.Split('.')[2]);
            }
            else
            {
                WriteLine("Updating without incrementing...");
            }
        }
    }
}
