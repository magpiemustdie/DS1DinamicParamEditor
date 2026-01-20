using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FileParser
{
    class AppConfig
    {
        public string ExePath { get; set; }

        public static AppConfig Load(string filePath)
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<AppConfig>(json);
        }

        public void Save(string filePath)
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

    }
}
