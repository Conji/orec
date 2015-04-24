using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ore.Compiler
{
    internal static class CredentialManager
    {
        public static string Guid { get; set; }

        public static string NewGuid => System.Guid.NewGuid().ToString("N") + System.Guid.NewGuid().ToString("N");

        public static void Invoke()
        {
            if (!File.Exists("c:\\ores\\.ecm"))
            {
                using (var file = File.Open("c:\\ores\\ecm", FileMode.OpenOrCreate))
                {
                    var guid = NewGuid;
                    var b = Encoding.UTF8.GetBytes(guid);
                    file.Write(b, 0, b.Length);
                    Guid = guid;
                }
            }
            else
            {
                using (var file = File.OpenText("c:\\ores\\ecm"))
                {
                    var guid = file.ReadToEnd();
                    Guid = guid;
                }
            }
        }


    }
}
