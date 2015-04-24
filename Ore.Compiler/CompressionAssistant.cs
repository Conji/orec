using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using Newtonsoft.Json.Linq;
using Ore.Compiler.Zlib;

namespace Ore.Compiler
{
    public static class CompressionAssistant
    {
        public static void CompileSolution(string solutionfile)
        {
            Program.WriteLine("Compiling " + solutionfile + "...");
            Process.Start("C:\\Windows\\Microsoft.NET\\Framework\\v4.0.30319\\msbuild.exe", solutionfile + ".sln /p:Configuration=Release");
        }

        public static byte[] CompressExecutable()
        {
            var name = GetPluginAssemblyName() + ".dll";
            using (var stream = new MemoryStream())
            {
                var file = Directory.GetCurrentDirectory() + "\\bin\\" + GetBuildType() + "\\" + name;
                var bytes = File.ReadAllBytes(file);
                var temp = new MemoryStream();
                temp.WriteUByteArray(bytes); // write library
                stream.WriteInt(temp.GetBuffer().Length); // write length of content
                stream.WriteUByteArray(temp.GetBuffer()); // write library
                temp.Flush();
                return stream.GetBuffer();
            }
            
        }

        public static byte[] CompressDependencies()
        {
            var dependencies = CommitDependencies();
            using (var stream = new MemoryStream())
            {
                foreach (var dep in dependencies)
                {
                    var file = Directory.GetCurrentDirectory() + "\\bin\\" + GetBuildType() + "\\" + dep;
                    Program.WriteLine("Compress dependency \"" + dep + "\"...");
                    var buffer = File.ReadAllBytes(file);
                    var temp = new MemoryStream();

                    temp.WriteUByteArray(buffer);
                    stream.WriteInt(temp.GetBuffer().Length);
                    stream.WriteUByteArray(temp.GetBuffer());
                    temp.Flush();
                }
                Program.WriteLine("Compressed " + CommitDependencies().Count + " dependencies into buffer.");
                return stream.GetBuffer();
            }
        }

        public static JArray CommitDependencies()
        {
            var array = new JArray();
            var files =
                    Directory.EnumerateFiles(
                        Directory.GetCurrentDirectory() +
                        (Directory.GetFiles(Directory.GetCurrentDirectory()).All(f => !f.EndsWith(".csproj")) ? ("\\" + GetPluginAssemblyName()) : "") +
                        "\\bin\\" + GetBuildType() +
                        "\\", "*.dll",
                        SearchOption.AllDirectories)
                        .ToArray();
            foreach (var file in files.Select(f => f.Split('\\').Last()).Where(f => !f.ToLower().Contains(GetPluginAssemblyName().ToLower())))
            {
                array.Add(JValue.CreateString(file));
            }
            return array;
        }

        public static string GetPluginAssemblyName()
        {
            var file =
                Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.csproj", SearchOption.AllDirectories)
                    .First();
            var reader = new XmlTextReader(file);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (reader.Name == "AssemblyName") return reader.ReadElementContentAsString();
            }
            throw new COMException();
        }

        public static string GetBuildType()
        {
            if (!Directory.Exists(string.Format("{0}\\bin\\Release\\", Directory.GetCurrentDirectory())))
                return "debug";
            return Directory.GetFiles(string.Format("{0}\\bin\\Release\\", Directory.GetCurrentDirectory())).Length > 0
                ? "release"
                : "debug";
        }
    }
}
