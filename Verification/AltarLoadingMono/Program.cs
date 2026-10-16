using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using BepInEx;
using BepInEx.Bootstrap;
using Mono.Cecil;
using MonoMod.Utils;
using SoftReferenceableAssets;

internal static class Program
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const string MwlGuid = "warpalicious.More_World_Locations_AIO";
    private static Type Generator = null!;
    private static ZoneSystem World = null!;
    private static string ReferencePath = "";
    private static bool Server = true, Authority = true;
    private static int Checks, Builds, Writes, ValidityQueries;
    private static HashSet<AssetID>? LastExclusions;

    private static int Main(string[] args)
    {
        string[] dirs = { Path.GetDirectoryName(Path.GetFullPath(args[0]))!, args[1], args[2] };
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string? path = dirs.Select(d => Path.Combine(d, new AssemblyName(e.Name).Name + ".dll")).FirstOrDefault(File.Exists);
            return path == null ? null : Assembly.LoadFrom(path);
        };
        try { Run(Assembly.LoadFrom(args[0])); return 0; }
        catch (Exception ex) { System.Console.Error.WriteLine(ex); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(Assembly plugin)
    {
        // Expected malformed-manifest diagnostics must not call Unity's native logger.
        UnityEngine.Debug.unityLogger.logEnabled = false;
        Generator = plugin.GetType("BossRules.AltarReferenceGenerator", true)!;
        World = (ZoneSystem)FormatterServices.GetUninitializedObject(typeof(ZoneSystem));
        World.m_locations = new List<ZoneSystem.ZoneLocation> { new ZoneSystem.ZoneLocation() };
        string manifestPath = Environment.GetEnvironmentVariable("BOSSRULES_TEST_MWL_MANIFEST")
            ?? throw new InvalidOperationException("Set BOSSRULES_TEST_MWL_MANIFEST to the installed assetBundleManifest_full.");
        var manifest = SoftReferenceableAssets.AssetBundleManifest.DeserializeFromDisk(manifestPath);
        Check(manifest != null && manifest.AssetCount > 0, "original game parser reads installed MWL manifest");
        string tempRoot = Path.GetFullPath(Path.GetTempPath());
        string scratch = Path.GetFullPath(Path.Combine(tempRoot, "BossRules-AltarLoading-" + Guid.NewGuid().ToString("N")));
        if (!scratch.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid scratch path");
        Directory.CreateDirectory(scratch);
        ReferencePath = Path.Combine(scratch, "reference.yml");
        typeof(Paths).GetMethod("SetExecutablePath", Static)!.Invoke(null,
            new object[] { Path.Combine(scratch, "fixture.exe"), scratch, Path.GetDirectoryName(typeof(ZoneSystem).Assembly.Location)!, Array.Empty<string>() });
        try
        {
            CheckAutomaticPolicy(plugin, manifestPath, scratch, manifest!.m_assetToLocationMap.Keys.ToList());
            System.Console.WriteLine($"PASS: {Checks} altar loading checks; {manifest.AssetCount} MWL asset IDs. Original parser and compiled plugin decisions; native scene/scan/write boundaries substituted. No Unity world or performance measurement.");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static void CheckAutomaticPolicy(Assembly plugin, string manifestPath, string scratch, List<AssetID> ids)
    {
        string header = (string)Generator.GetField("PartialReferenceHeader", Static)!.GetRawConstantValue()!;
        Type files = plugin.GetType("BossRules.AltarConfigurationFiles", true)!;
        string placeholder = (string)files.GetMethod("BuildReferencePlaceholderYaml", Static)!.Invoke(null, null)!;
        bool MayWrite() => (bool)Generator.GetMethod("CanWritePartialReference", Static)!.Invoke(null, new object[] { ReferencePath })!;
        Check(MayWrite(), "new reference permits partial generation");
        File.WriteAllText(ReferencePath, placeholder);
        Check(MayWrite(), "startup placeholder permits partial generation");
        File.WriteAllText(ReferencePath, header + "\n[]\n");
        Check(MayWrite(), "marked partial reference permits refresh");
        const string full = "# full export\n- prefab: MWL_Test\n  itemStands: []\n";
        File.WriteAllText(ReferencePath, full);
        Check(!MayWrite(), "existing full reference cannot become partial");

        Reset(); Auto();
        Check(Builds == 1 && LastExclusions == null && Writes == 1, "without MWL, normal full auto scan remains available");
        Auto(); Check(Builds == 1, "completed auto scan does not repeat per Update");
        InstallMwl(Path.Combine(Path.GetDirectoryName(manifestPath)!, "More_World_Locations_AIO.dll"));
        Reset(); Auto();
        Check(Builds == 0 && Writes == 0 && File.ReadAllText(ReferencePath) == full, "MWL preserves full reference before scanning");

        File.WriteAllText(ReferencePath, placeholder);
        Reset(); Auto();
        Check(Builds == 1 && Writes == 1 && LastExclusions != null && ids.All(LastExclusions.Contains), "automatic scan receives every installed MWL exclusion");
        Check(LastExclusions!.Count == ids.Count, "exclusions contain only manifest IDs");
        int builds = Builds;
        Auto(); Check(Builds == builds, "restricted auto scan completes once");
        Copy(Generator.GetMethod("ExportFullReference", Static)!).Invoke(null, null);
        Check(Builds == builds + 1 && LastExclusions == null, "explicit full export bypasses automatic MWL exclusions");

        MethodInfo capture = Copy(Generator.GetMethod("TryCaptureReferenceEntries", Static)!);
        var excluded = new HashSet<AssetID>(ids);
        var loaded = new List<ZoneSystem.ZoneLocation>();
        foreach (AssetID id in ids)
        {
            var location = new ZoneSystem.ZoneLocation { m_prefab = new SoftReference<UnityEngine.GameObject>(id) };
            object?[] captureArgs = { location, excluded, new HashSet<string>(), loaded, null };
            Check(!(bool)capture.Invoke(null, captureArgs)!, "excluded location is rejected");
        }
        Check(ValidityQueries == 0 && loaded.Count == 0, "all MWL exclusions precede even loader validity access");
        AssetID unrelated = new AssetID(0xB055, 0, 0, 1);
        Check(!excluded.Contains(unrelated), "unrelated fixture ID is outside installed manifest");
        object?[] ordinary = { new ZoneSystem.ZoneLocation { m_prefab = new SoftReference<UnityEngine.GameObject>(unrelated) }, excluded, new HashSet<string>(), loaded, null };
        capture.Invoke(null, ordinary);
        Check(ValidityQueries == 1, "non-MWL locations reach the normal loader validation");

        InstallMwl(Path.Combine(scratch, "Missing.dll"));
        Reset(); Auto(); Auto();
        Check(Builds == 0 && Writes == 0 && Done(), "missing manifest defers this world without full fallback or repeated scans");
        File.WriteAllText(Path.Combine(scratch, "assetBundleManifest_full"), "invalid manifest");
        Reset(); Auto();
        Check(Builds == 0 && Writes == 0 && Done(), "malformed manifest preserves references and defers scanning");

        InstallMwl(Path.Combine(Path.GetDirectoryName(manifestPath)!, "More_World_Locations_AIO.dll"));
        Reset(); Server = false; Auto();
        Copy(Generator.GetMethod("ExportFullReference", Static)!).Invoke(null, null);
        Check(Builds == 0 && Writes == 0 && !Done(), "remote clients cannot auto scan or explicitly export");
        Server = true; Authority = false; Auto();
        Copy(Generator.GetMethod("ExportFullReference", Static)!).Invoke(null, null);
        Check(Builds == 0 && Writes == 0, "server role without sync authority cannot scan");
        Authority = true;
    }

    private static void InstallMwl(string dllPath)
    {
        var info = new PluginInfo();
        typeof(PluginInfo).GetProperty("Location")!.SetValue(info, dllPath);
        Chainloader.PluginInfos[MwlGuid] = info;
    }
    private static void Reset()
    {
        Generator.GetMethod("ResetAutoRefresh", Static)!.Invoke(null, null);
        Builds = Writes = 0; LastExclusions = null; Server = Authority = true;
    }
    private static bool Done() => (bool)Generator.GetField("_autoRefreshDone", Static)!.GetValue(null)!;
    private static void Auto() => Copy(Generator.GetMethod("TryAutoRefreshReferenceConfigurationFile", Static)!).Invoke(null, null);
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + name);
        Checks++;
    }

    // Run production control flow, replacing only native scene/clock and scan/write boundaries.
    private static MethodInfo Copy(MethodInfo method)
    {
        using var body = new DynamicMethodDefinition(method);
        typeof(DynamicMethodDefinition).GetProperty(nameof(DynamicMethodDefinition.OriginalMethod))!.SetValue(body, null);
        body.OwnerType = typeof(Program);
        foreach (var instruction in body.Definition.Body.Instructions)
        {
            if (!(instruction.Operand is MethodReference target)) continue;
            string type = target.DeclaringType.FullName;
            string? boundary = type == "BossRules.BossRulesPlugin" ? target.Name switch
            {
                "IsRuntimeServer" => nameof(IsServer), "get_IsSourceOfTruth" => nameof(IsAuthority),
                "get_AltarReferenceYamlFilePath" => nameof(GetReferencePath), _ => null
            } : type == Generator.FullName ? target.Name switch
            {
                "IsQueenDungeonReferenceDataReady" => nameof(Ready),
                "BuildReferenceConfigurationContent" => nameof(RecordScan),
                "WriteReferenceConfigurationFile" => nameof(RecordWrite), _ => null
            } : type == "ZoneSystem" && target.Name == "get_instance" ? nameof(GetWorld)
                : type == "UnityEngine.Time" ? nameof(Clock)
                : type == "UnityEngine.Object" && target.Name == "op_Equality" ? nameof(SameObject)
                : type.StartsWith("SoftReferenceableAssets.SoftReference`1") && target.Name == "get_IsValid" ? nameof(RecordValidity)
                : null;
            if (boundary == null) continue;
            instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
            instruction.Operand = body.Module.ImportReference(typeof(Program).GetMethod(boundary, Static)!);
        }
        return body.Generate();
    }
    private static bool IsServer() => Server;
    private static bool IsAuthority() => Authority;
    private static bool Ready() => true;
    private static float Clock() => 10f;
    private static string GetReferencePath() => ReferencePath;
    private static ZoneSystem GetWorld() => World;
    private static bool SameObject(UnityEngine.Object? a, UnityEngine.Object? b) => ReferenceEquals(a, b);
    private static bool RecordValidity(ref SoftReference<UnityEngine.GameObject> prefab) { ValidityQueries++; return false; }
    private static string RecordScan(out int count, HashSet<AssetID>? exclusions)
    {
        count = 1; Builds++; LastExclusions = exclusions; return "fixture content";
    }
    private static void RecordWrite(string content) => Writes++;
}
