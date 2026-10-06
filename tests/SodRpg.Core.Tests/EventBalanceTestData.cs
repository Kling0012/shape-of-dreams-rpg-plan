using System;
using System.IO;
using System.Text.Json;

namespace SodRpg.Core.Tests
{
    internal static class EventBalanceTestData
    {
        private static readonly JsonElement Table = Read();

        private static JsonElement Read()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                string path = Path.Combine(directory.FullName, "tools", "balance", "events.json");
                if (!File.Exists(path)) continue;
                using (var document = JsonDocument.Parse(File.ReadAllText(path)))
                    return document.RootElement.Clone();
            }
            throw new InvalidOperationException("tools/balance/events.json was not found above " + AppContext.BaseDirectory);
        }

        internal static int Number(string section, string key) => Table.GetProperty(section).GetProperty(key).GetInt32();
        internal static double Probability(string section, string key) => Table.GetProperty(section).GetProperty(key).GetDouble();
        internal static double Bonus(string key) => Table.GetProperty(key).GetDouble();
    }
}
