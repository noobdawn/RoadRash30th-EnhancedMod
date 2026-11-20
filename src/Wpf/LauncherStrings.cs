using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace RoadRash.Workbench
{
    internal sealed class LauncherStrings
    {
        private readonly Dictionary<string, string> entries;
        private static Stream OpenDefaults() =>
            Assembly.GetExecutingAssembly().GetManifestResourceStream("Launcher.zh-CN.json");
        internal static void ExportDefaults(string path)
        {
            using var defaults = OpenDefaults();
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = File.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    defaults.CopyTo(output);
                File.Move(temporary, path, true);
            }
            finally { File.Delete(temporary); }
        }
        internal LauncherStrings(string file)
        {
            using var defaults = OpenDefaults();
            entries = JsonSerializer.Deserialize<Dictionary<string, string>>(defaults);
            if (file == null || !File.Exists(file)) return;
            try
            {
                using var stream = File.OpenRead(file);
                var overrides = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                if (overrides != null)
                    foreach (var pair in overrides)
                        if (entries.ContainsKey(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                            entries[pair.Key] = pair.Value;
            }
            catch (JsonException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        internal string Get(string key) => entries[key];
        internal string Optional(string key) => entries.GetValueOrDefault(key, "");
        internal void AddWindowResources(System.Windows.ResourceDictionary resources)
        {
            foreach (var entry in entries)
                if (entry.Key.StartsWith("ui.", StringComparison.Ordinal)) resources[entry.Key] = entry.Value;
        }
    }
}
