using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
            var stream = new byte[8192*8192];
            var buffer = File.ReadAllBytes(name);
            Array.Copy(BitConverter.GetBytes(buffer.Length), 0, stream, 0, 4);
            
            Program.WriteLine("Compressed .dll into " + stream.Length + " bytes...");
            return ZlibStream.CompressBuffer(stream);
            
        }

        public static byte[] CompressDependencies()
        {
            var dependencies = CommitDependencies();
            using (var stream = new MemoryStream())
            {
                stream.WriteByte((byte)dependencies.Count); // it won't be any more than 255 dependencies, so just add a byte.
                foreach (var dep in dependencies)
                {
                    var file = Directory.GetCurrentDirectory() + dep["dll"];
                    Program.WriteLine("Compress dependency \"" + dep["name"] + "\"...");

                    var buffer = File.ReadAllBytes(file);
                    var name = Encoding.UTF8.GetBytes(dep["name"].ToString());

                    stream.WriteAsync(BitConverter.GetBytes(buffer.Length + name.Length), 0, 4); // write length of dependency
                    stream.WriteAsync(name, 0, name.Length); // write name of dependency
                    stream.WriteAsync(buffer, 0, buffer.Length, new CancellationToken()); // write dependency
                }
                Program.WriteLine("Compressed " + CommitDependencies().Count + " dependencies into buffer.");
                return ZlibStream.CompressBuffer(stream.GetBuffer());
            }
        }

        public static JArray CommitDependencies()
        {
            var file =
                Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.csproj", SearchOption.AllDirectories)
                    .First();
            var reader = new XmlTextReader(file);
            var deps = new Dictionary<string, string>();
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        switch (reader.Name)
                        {
                            case "Reference":
                                var include = reader["Include"];
                                if (include != null) deps.Add(include, string.Empty);
                                break;
                            case "HintPath":
                                var path = reader.ReadElementContentAsString();
                                if (path != null) deps[deps.Last().Key] = path;
                                break;
                        }
                        break;
                }
            }
            var array = new JArray();
            foreach (var json in from d in deps where d.Value != string.Empty select new JObject
                {
                    ["name"] = d.Key,
                    ["dll"] = d.Value
                })
            {
                array.Add(json);
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
    }
}
