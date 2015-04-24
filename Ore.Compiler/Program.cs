using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;

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
                    New();
                    break;
                default:
                    if (args.Contains("--restart"))
                    {
                        File.Delete(".ore");
                        New();
                    }
                    break;
            }

        }

        private static void New()
        {
            var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.sln");
            if (files.Length == 0)
            {
                WriteLine("No solutions found inside directory.");
                return;
            }
            string file;
            if (files.Length > 1)
            {
                WriteLine("Multiple solutions found. Which do we use?");
                var index = 0;
                foreach (var f in files)
                {
                    WriteLine("[{0}] - {1}", index, f);
                    index++;
                }
                var response = int.Parse(Console.ReadKey().KeyChar.ToString());
                if (response > index)
                {
                    WriteLine("Error: no index of " + response);
                    return;
                }
                file = files[index];
            }
            else
            {
                file = files[0];
            }

            file = file.Split('\\')[file.Split('\\').Length - 1];

            WriteLine("Compiled solution. Compressing.");
            CompressionAssistant.CompileSolution(file);
            WriteLine("Compressing executable...");
            var fileBuffer = CompressionAssistant.CompressExecutable();
            WriteLine("Compressing dependencies...");
            var dependencyBuffer = CompressionAssistant.CompressDependencies();

            Ore ore;
            if (!File.Exists(".ore")) CreateNewOre(file, out ore);
            else UpdateOre(file, out ore);
            ore.Dependencies = CompressionAssistant.CommitDependencies();
            WriteLine("Ore data finished gathering...");
            ore.Persist();

            var buffer = new byte[4096*4096];
            var fbLength = BitConverter.GetBytes(fileBuffer.Length);
            var dbLength = BitConverter.GetBytes(dependencyBuffer.Length);

            var pos = 0;
            // write the plugin buffer
            WriteLine("Buffing file data...");
            Array.Copy(fbLength, 0, buffer, pos, fbLength.Length);
            pos += fbLength.Length;
            Array.Copy(fileBuffer, 0, buffer, pos, fileBuffer.Length);
            pos += fileBuffer.Length;

            // write the compressed dependency buffer
            WriteLine("Buffing dependency data...");
            Array.Copy(dbLength, 0, buffer, pos, dbLength.Length);
            pos += dbLength.Length;
            Array.Copy(dependencyBuffer, 0, buffer, pos, dependencyBuffer.Length);
            pos += dependencyBuffer.Length;
            WriteLine("Finished with buffer length of " + pos);

            File.WriteAllBytes(".filebuf", buffer);
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
            WriteLine("So we noticed this is a new Ore. Would you like to keep the name " + file.Replace(".sln", "") + "? [y/n]");
            switch (Console.ReadKey().KeyChar)
            {
                case 'y':
                    ore.Name = file.Replace(".sln", "");
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

        private static void UpdateOre(string file, out Ore ore)
        {
            ore = Ore.GetPrevious();
            WriteLine("Updating the Ore \"{0}\"...", ore.Name);
            WriteLine("The last version was {0}.{1}.{2}. Would you like to increment it?");
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
