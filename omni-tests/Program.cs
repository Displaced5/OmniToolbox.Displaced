using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length != 4) throw new ArgumentException("Expected module assembly, Omni directory, Dalamud directory and source root.");
var modulePath = Path.GetFullPath(args[0]);
var omniDirectory = Path.GetFullPath(args[1]);
var dalamudDirectory = Path.GetFullPath(args[2]);
var sourceRoot = Path.GetFullPath(args[3]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (var directory in new[] { omniDirectory, dalamudDirectory })
    {
        var candidate = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(candidate)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
    }
    return null;
};
var passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    passed++; Console.WriteLine("PASS " + message);
}
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(modulePath);
var common = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(omniDirectory, "OmniToolbox.Common.dll"));
var baseType = common.GetType("OmniToolbox.Common.Module.Abstractions.OmniModuleBase", true)!;
var modules = assembly.GetTypes().Where(t => !t.IsAbstract && baseType.IsAssignableFrom(t)).ToArray();
Check(modules.Length == 1 && modules[0].Name == "ClothingPairingOnline", "exactly one concrete Omni module with matching type name");
var type = modules.Single();
Check(type.GetConstructor(Type.EmptyTypes) is not null, "public parameterless module constructor");
var api = type.CustomAttributes.Single(a => a.AttributeType.Name == "ModuleApiVersionAttribute");
Check((int)api.ConstructorArguments[0].Value! == 1, "module declares API 1");
Check(!assembly.GetReferencedAssemblies().Any(a => a.Name == "ClothingPairing"), "module is self contained and does not reference standalone plugin");
Check(!assembly.GetTypes().Any(t => t.GetInterfaces().Any(i => i.FullName == "Dalamud.Plugin.IDalamudPlugin")), "module contains no standalone plugin entry");

// Invoke the installed Common's real contract validator, without enabling any module or game service.
var contract = common.GetType("OmniToolbox.Common.Module.Runtime.ModuleContract", true)!;
contract.GetMethod("ValidateType", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [type]);
Check(true, "installed Omni Common accepts module type contract");
var instance = Activator.CreateInstance(type)!;
Check(!(bool)baseType.GetProperty("IsEnabled")!.GetValue(instance)!, "constructing module does not enable runtime");
var configField = type.GetField("config", BindingFlags.NonPublic | BindingFlags.Instance)!;
Check(configField.FieldType.IsNested && configField.FieldType.Name == "Settings" && !configField.IsInitOnly,
    "host-bindable mutable config member uses embedded Settings");
var config = configField.GetValue(instance)!;
var serializedConfig = JsonSerializer.Serialize(config, configField.FieldType);
var reloadedConfig = JsonSerializer.Deserialize(serializedConfig, configField.FieldType)!;
Check((bool)configField.FieldType.GetProperty("RestoreFacewearAfterGearset")!.GetValue(reloadedConfig)!,
    "embedded configuration roundtrip preserves default gearset restoration");
var replacement = Activator.CreateInstance(configField.FieldType, true)!;
configField.SetValue(instance, replacement);
Check(ReferenceEquals(configField.GetValue(instance), replacement), "host can replace config before activation");
var hostConfig = common.GetType("OmniToolbox.Config.TreeHouseConfig", true)!;
hostConfig.GetMethod("ValidateModuleConfig", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
    .Invoke(null, [replacement]);
Check(true, "installed Omni accepts embedded configuration shape");
var info = type.GetProperty("Info")!.GetValue(instance)!;
Check((string)info.GetType().GetProperty("Author")!.GetValue(info)! == "Displaced", "module author identity");
var localization = type.GetProperty("Localization")!.GetValue(instance)!;
var languageType = common.GetType("OmniToolbox.Config.UILanguage", true)!;
var catalogs = (IEnumerable)localization.GetType().GetProperty("Catalogs")!.GetValue(localization)!;
var source = new Dictionary<string, string>();
foreach (var item in catalogs)
{
    var entryType = item.GetType();
    if (entryType.GetProperty("Key")!.GetValue(item)!.ToString() != "SimplifiedChinese") continue;
    foreach (var pair in (IEnumerable)entryType.GetProperty("Value")!.GetValue(item)!)
    {
        var pairType = pair.GetType();
        source[(string)pairType.GetProperty("Key")!.GetValue(pair)!] = (string)pairType.GetProperty("Value")!.GetValue(pair)!;
    }
}
Check(source.Count > 90 && source.Values.All(v => !string.IsNullOrWhiteSpace(v)), "all module source localization entries are nonempty");
foreach (var language in new[] { "SimplifiedChinese", "TraditionalChinese" })
    Check((bool)localization.GetType().GetMethod("IsComplete")!.Invoke(localization, [Enum.Parse(languageType, language)])!,
        "installed Omni generates complete " + language + " catalog");
var sharedTexts = assembly.GetType("ClothingPairing.Online.OnlineTexts", true)!;
var keyMethod = sharedTexts.GetMethod("StateKey", BindingFlags.Static | BindingFlags.Public)!;
foreach (var file in new[] { "src/Core/RestoreController.cs", "src/GameAdapter.cs" })
{
    var texts = Regex.Matches(File.ReadAllText(Path.Combine(sourceRoot, file)), "\"([^\"\r\n]*)\"")
        .Select(m => m.Groups[1].Value).Where(s => Regex.IsMatch(s, "[\u4e00-\u9fff]"));
    Check(texts.All(text => (string)keyMethod.Invoke(null, [text])! != "UnknownState"), "all runtime states localized: " + file);
}
var adapterText = File.ReadAllText(Path.Combine(sourceRoot, "omni/ClothingPairingOnline.cs"));
var keys = Regex.Matches(adapterText, "(?:L|Check)\\(\"([^\"]+)\"").Select(m => m.Groups[1].Value);
Check(keys.All(source.ContainsKey), "all literal UI localization keys exist");
Check(!(bool)type.GetMethod("TryHandleCommand", [typeof(string)])!.Invoke(instance, ["restore"])!, "disabled module rejects commands without touching services");
Check(!(bool)type.GetMethod("ResetSettings")!.Invoke(instance, null)!, "no-op reset does not report a configuration change");

using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(sourceRoot, "TreeHouseModules.json")));
var root = manifest.RootElement; var modulesJson = root.GetProperty("Modules"); var module = modulesJson[0];
Check(root.GetProperty("SchemaVersion").GetInt32() == 1 && modulesJson.GetArrayLength() == 1, "online manifest schema and module count");
Check(module.GetProperty("InternalName").GetString() == type.Name && module.GetProperty("ApiVersion").GetInt32() == 1,
    "manifest matches compiled module identity and API");
var repositoryUrl = module.GetProperty("RepositoryURL").GetString();
var supportUrls = (IEnumerable<string>)info.GetType().GetProperty("SupportUrls")!.GetValue(info)!;
Check(repositoryUrl == "https://github.com/Displaced5/OmniToolbox.Displaced" && supportUrls.Contains(repositoryUrl),
    "manifest and module support URL use the dedicated online repository");
Check(module.GetProperty("Version").GetString() == "1.2.3" && assembly.GetName().Version == new Version(1, 2, 3, 0),
    "published version matches compiled online module");
var sourcePath = Path.Combine(sourceRoot, module.GetProperty("File").GetString()!);
Check(Path.GetFileNameWithoutExtension(sourcePath) == type.Name, "module filename matches type name");
Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))) == module.GetProperty("Sha256").GetString(),
    "manifest SHA256 matches exact delivered source bytes");
Check(module.GetProperty("Localization").GetProperty("Source").GetProperty("Title").GetString() == source["Title"] &&
    module.GetProperty("Localization").GetProperty("Source").GetProperty("Description").GetString() == source["Description"],
    "manifest localized title and description match module source");
await LifecycleChecks.RunAsync(assembly, common, dalamudDirectory, Check);
Console.WriteLine($"{passed} Omni integration checks passed.");
