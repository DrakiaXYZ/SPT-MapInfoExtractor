using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Dynamic;
using System.Text;
using YamlDotNet.Serialization;

namespace DrakiaXYZ_MapInfoExtractor
{
    internal class Program
    {
        private static Dictionary<string, int> _sceneLookup = new Dictionary<string, int>();
        private static Dictionary<string, dynamic> _presetCache = new Dictionary<string, dynamic>();

        static void Main(string[] args)
        {
            // First, extract "globalmanagers/BuildSettings" from "EscapeFromTarkov_Data\globalgamemanagers" into "Extract" (This should end up in a folder called "globalgamemanagers")
            // Second, extract all of the map preset bundles from "EscapeFromTarkov_Data\StreamingAssets\Windows\maps" into "Extract". The script will look for the first folder containing ".bundle" in its name
            string outputDir = ".\\Extract";
            if (!buildSceneLookup(outputDir))
            {
                return;
            }

            // Find the bundle dir, and parse it
            foreach (string dir in Directory.GetDirectories(outputDir))
            {
                if (!dir.Contains(".bundle")) continue;

                if (!parseBundles(dir))
                {
                    return;
                }

                break;
            }

            Console.WriteLine("Press Enter to Exit");
            Console.ReadLine();
        }

        static bool buildSceneLookup(string outputDir)
        {
            // Create a mapping of scene -> id from ExportedProject\ProjectSettings\EditorBuildSettings.asset
            string buildSettingsPath = $"{outputDir}\\globalgamemanagers\\ExportedProject\\ProjectSettings\\EditorBuildSettings.asset";
            dynamic? buildSettings = loadYamlAsDynamic(buildSettingsPath);
            if (buildSettings == null)
            {
                Console.Error.WriteLine("Error loading EditorBuildSettings.asset");
                return false;
            }

            for (int i = 0; i < buildSettings.EditorBuildSettings.m_Scenes.Count; i++)
            {
                dynamic scene = buildSettings.EditorBuildSettings.m_Scenes[i];
                if (!_sceneLookup.ContainsKey(scene.path))
                {
                    _sceneLookup.Add(scene.path, i);
                }
            }

            return true;
        }

        static bool parseBundles(string bundleDir)
        {
            if (!buildPresetCache(bundleDir))
            {
                return false;
            }

            string presetDir = $"{bundleDir}\\ExportedProject\\Assets\\Content\\Locations\\_Presets";
            List<string> assetList;
            if (!parsePresetDir(presetDir, out assetList))
            {
                return false;
            }

            return true;
        }

        static bool buildPresetCache(string bundleDir)
        {
            string monoBehaviourDir = $"{bundleDir}\\ExportedProject\\Assets\\MonoBehaviour";
            foreach (string file in Directory.GetFiles(monoBehaviourDir))
            {
                if (!file.EndsWith(".asset")) continue;
                dynamic? meta = loadYamlAsDynamic(file + ".meta");
                dynamic? preset = loadYamlAsDynamic(file);
                if (meta == null || preset == null)
                {
                    Console.Error.WriteLine("Error reading meta or preset " + file);
                    continue;
                }

                _presetCache.Add(meta?.guid, preset);
            }

            return true;
        }

        static bool parsePresetDir(string assetDir, out List<string> assetList)
        {
            assetList = new List<string>();

            foreach (string file in Directory.GetFiles(assetDir))
            {
                if (!file.EndsWith(".asset")) continue;

                dynamic? preset = loadYamlAsDynamic(file);
                if (preset == null)
                {
                    Console.Error.WriteLine("Error loading preset " + file);
                    continue;
                }

                assetList = new List<string>();
                processPreset(preset, ref assetList);

                if (!string.IsNullOrEmpty(preset.MonoBehaviour.ServerName))
                {
                    Dictionary<int, string> assetMap = new Dictionary<int, string>();
                    Console.WriteLine($"{preset.MonoBehaviour.ServerName} consists of the following assets:");
                    foreach (string asset in assetList)
                    {
                        if (!_sceneLookup.ContainsKey(asset))
                        {
                            Console.WriteLine($"    {asset}");
                        }
                        else
                        {
                            int levelNumber = _sceneLookup[asset];
                            if (!assetMap.ContainsKey(levelNumber))
                            {
                                assetMap.Add(levelNumber, asset);
                            }
                        }
                    }

                    foreach (int levelNumber in assetMap.Keys.OrderBy(x => x))
                    {
                        Console.WriteLine($"    level{levelNumber}  ({assetMap[levelNumber]})");
                    }
                }
            }

            foreach (string folder in Directory.GetDirectories(assetDir))
            {
                List<string> childAssets;
                if (!parsePresetDir(folder, out childAssets))
                {
                    return false;
                }

                assetList.AddRange(childAssets);
            }

            return true;
        }

        static void processPreset(dynamic preset, ref List<string> assetList)
        {
            foreach (dynamic scene in preset.MonoBehaviour._scenesResourceKeys)
            {
                assetList.Add(scene.path);
            }

            foreach (dynamic childPreset in preset.MonoBehaviour.ChildPresets)
            {
                if (_presetCache.ContainsKey(childPreset.guid))
                {
                    processPreset(_presetCache[childPreset.guid], ref assetList);
                }
            }
        }

        static dynamic? loadYamlAsDynamic(string yamlPath)
        {
            // Hacky workaround to bypass tags in the YAML
            var inputStringBuilder = new StringBuilder();
            using (var inputReader = new StreamReader(yamlPath))
            {
                bool bStartReading = yamlPath.EndsWith(".meta");
                string? line;
                while ((line = inputReader.ReadLine()) != null)
                {
                    if (!bStartReading)
                    {
                        if (line.StartsWith("---")) bStartReading = true;
                        continue;
                    }

                    inputStringBuilder.AppendLine(line);
                }
            }
            var inputString = inputStringBuilder.ToString();
            var inputStringReader = new StringReader(inputString);

            var deserializer = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();
            var yamlObject = deserializer.Deserialize(inputStringReader);
            var serializer = new SerializerBuilder()
                .JsonCompatible()
                .Build();

            var json = serializer.Serialize(yamlObject);
            return JsonConvert.DeserializeObject<ExpandoObject>(json, new ExpandoObjectConverter());
        }
    }
}