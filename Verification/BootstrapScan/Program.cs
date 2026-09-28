using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Mono.Cecil;
using MonoMod.Utils;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            System.Console.Error.WriteLine("Usage: BootstrapScan.Tests <BossRules.dll> <original Managed directory> <BepInEx core>");
            return 2;
        }
        string[] directories = { Path.GetDirectoryName(Path.GetFullPath(args[0]))!, args[1], args[2] };
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            string? path = directories.Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
            return path == null ? null : Assembly.LoadFrom(path);
        };
        try { Run(Path.GetFullPath(args[0])); return 0; }
        catch (Exception e) { System.Console.Error.WriteLine(e); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string plugin) => BootstrapScanTests.Run(Assembly.LoadFrom(plugin));
}

// Execute the compiled intake/tick/cursor bodies and original game's iterative query.
// Substitute scene/config/clock boundaries and observe, rather than execute, despawns.
internal static class BootstrapScanTests
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Type Manager = null!;
    private static readonly Dictionary<MethodInfo, MethodInfo> Copies = new();
    private static readonly Dictionary<ZDOID, ZDO> Zdos = new();
    private static readonly HashSet<ZDOID> Dead = new();
    private static readonly List<(string Prefab, int Cursor)> Queries = new();
    private static readonly ZNetScene Scene = New<ZNetScene>();
    private static readonly ObjectDB Database = New<ObjectDB>();
    private static ZDOMan? World;
    private static IReadOnlyList<string> Prefabs = Array.Empty<string>();
    private static int Version, Checks, ScheduledTicks, Prunes, DetachTicks;
    private static uint NextId;
    private static float Now;
    private static bool Server, Enabled, ThrowQuery;

    internal static void Run(Assembly plugin)
    {
        Manager = plugin.GetType("BossRules.DespawnRulesManager", true)!;
        Check(typeof(ZDOMan).GetMethod("GetAllZDOsWithPrefabIterative")!.IsPublic, "query is public in the original DLL");
        CheckSlicesAndLiveIntake();
        CheckResets();
        CheckMergeAndFailures();
        Call("ResetRuntimeState");
        System.Console.WriteLine($"Bootstrap scan: {Checks} checks passed; compiled production flow and original game query, with scene/config/clock/despawn boundaries substituted. No Unity world or multiplayer session.");
    }

    private static void CheckSlicesAndLiveIntake()
    {
        Reset();
        ZDO first = Add("BossA", 0);
        ZDO boundary = Add("BossA", 400);
        ZDO later = Add("BossA", 904);
        ZDO configured = Add("ConfiguredCreature", 2);
        Tick();
        Check(Queries.Count == 1 && Pending(), "one original query call on the first Update");
        Check(Tracked().Contains(first.m_uid) && Tracked().Contains(boundary.m_uid) && !Tracked().Contains(later.m_uid), "partial results become tracked before scan completion");
        object state = Tracked()[boundary.m_uid]!;
        state.GetType().GetProperty("NoPlayerSince", Instance)!.SetValue(state, 17d);

        ZDO live = Add("ConfiguredCreature", 850);
        Call("QueueCreatedDespawnTarget", live.GetPrefab(), live);
        Call("QueueCreatedDespawnTarget", live.GetPrefab(), live);
        Check(Observations().Count == 1, "duplicate live creation is merged");
        Tick();
        Check(Queries.Count == 2 && Queries[1].Cursor == 400 && Now < 1f, "cursor continues on the next frame, without waiting a second");
        Check(Tracked().Contains(live.m_uid), "live creation is processed while bootstrap is pending");
        Check(ReferenceEquals(state, Tracked()[boundary.m_uid]) && (double)state.GetType().GetProperty("NoPlayerSince", Instance)!.GetValue(state)! == 17d,
            "native repeated boundary sector reuses tracked state without resetting its countdown");
        Check(ScheduledTicks == 2 && DetachTicks == 2 && Prunes == 1, "scheduled checks/detach intake continue; pruning keeps its one-second cadence");
        Tick();
        Check(Tracked().Contains(later.m_uid), "later sectors are not skipped");
        Tick();
        Check(Queries.Count == 4 && Pending() && !Tracked().Contains(configured.m_uid), "finishing one prefab does not search the next in the same Update");
        Tick();
        Check(Queries[4] == ("ConfiguredCreature", 0) && Tracked().Contains(configured.m_uid), "configured non-boss targets retain bootstrap coverage");
        for (int i = 0; i < 3; i++) Tick();
        Check(!Pending() && Queries.Count == 8 && Tracked().Count == 5, "all prefabs finish once; repeated sector-zero results do not duplicate targets");
        Tick();
        Check(Queries.Count == 8 && ScheduledTicks == 9, "no global rescans after completion; tracked checks continue");
        Check(Buffer().Count == 0, "slice buffer releases ZDO references");
    }

    private static void CheckResets()
    {
        Reset(); Tick();
        Prefabs = new[] { "ReplacementBoss" }; Version++;
        Tick();
        Check(Queries.Last() == ("ReplacementBoss", 0), "lookup change discards both old prefab snapshot and cursor");
        Tick(); Call("MarkBootstrapScanDirty"); Tick();
        Check(Queries.Last() == ("ReplacementBoss", 0), "explicit YAML dirty restarts from the first sector");

        Enabled = false; int calls = Queries.Count; Tick();
        Check(Queries.Count == calls && EmptyCursor() && Tracked().Count == 0 && Observations().Count == 0, "Off clears scan and tracking state without querying");
        Enabled = true; Tick();
        Check(Queries.Last() == ("ReplacementBoss", 0), "On starts a fresh scan");

        World = MakeWorld(); Tick();
        Check(Queries.Last() == ("ReplacementBoss", 0), "new ZDO manager restarts even with identical prefab/config version");
        Server = false; calls = Queries.Count; Tick();
        Check(Queries.Count == calls && EmptyCursor() && Get("_bootstrapZdoMan") == null, "client/menu Update makes no query and releases the previous world");
        Server = true; Tick();
        Check(Queries.Last() == ("ReplacementBoss", 0), "return to server resumes with a fresh cursor");

        Call("ResetRuntimeState");
        Check(EmptyCursor() && Tracked().Count == 0 && Buffer().Count == 0, "plugin/runtime reset clears all bootstrap state");
        World = null; calls = Queries.Count; Tick();
        Check(Pending() && Queries.Count == calls, "unavailable ZDO manager leaves bootstrap pending");
        World = MakeWorld(); Tick();
        Check(Queries.Last() == ("ReplacementBoss", 0), "world becoming ready starts the scan");
        Reset(); Prefabs = Array.Empty<string>(); Tick();
        Check(!Pending() && Queries.Count == 0, "empty prefab catalog finishes without a world query");
        Prefabs = new[] { "LatePrefab" }; Version++; Tick();
        Check(Queries.Single() == ("LatePrefab", 0), "later prefab catalog retries a completed empty scan");
    }

    private static void CheckMergeAndFailures()
    {
        Reset(); ZDO boss = Add("BossA", 0);
        Call("ObserveDespawnLookupVersion"); Call("RunPendingBootstrapScan");
        Type observation = Manager.GetNestedType("PendingDespawnObservation", BindingFlags.NonPublic)!;
        Type source = Manager.GetNestedType("DespawnObservationSource", BindingFlags.NonPublic)!;
        object loaded = Activator.CreateInstance(observation, Instance, null,
            new object[] { boss.m_uid, boss.GetPrefab(), "BossA", Enum.Parse(source, "LoadedCharacter"), 0f, 0f }, null)!;
        Call("EnqueueDespawnObservation", loaded);
        Call("QueueCreatedDespawnTarget", boss.GetPrefab(), boss);
        Check(Observations().Count == 1 && observation.GetProperty("Source", Instance)!.GetValue(Observations()[boss.m_uid])!.ToString() == "LoadedCharacter",
            "bootstrap/loaded/created observations preserve existing source precedence and ID deduplication");
        Dead.Add(boss.m_uid); Tick();
        Check(!Tracked().Contains(boss.m_uid), "dead result is rejected before becoming tracked");

        Reset(); boss = Add("BossA", 0); ZDO missing = Add("BossA", 1); Zdos.Remove(missing.m_uid);
        ThrowQuery = true;
        bool threw = false;
        try { Tick(); } catch (TargetInvocationException e) { threw = e.GetBaseException().Message == "fixture query failure"; }
        Check(threw && Pending() && Buffer().Count == 0 && (int)Get("_bootstrapZdoIndex")! == 0,
            "query exception propagates, releases partial results and restores the advanced cursor");
        ThrowQuery = false; Tick();
        Check(Tracked().Contains(boss.m_uid) && !Tracked().Contains(missing.m_uid), "next Update retries; missing ZDO result is rejected");
    }

    private static void Reset()
    {
        Call("ResetRuntimeState");
        Zdos.Clear(); Dead.Clear(); Queries.Clear();
        Prefabs = new[] { "BossA", "ConfiguredCreature" };
        World = MakeWorld(); Now = 0f; Version = 1;
        Server = Enabled = true; ThrowQuery = false;
        ScheduledTicks = Prunes = DetachTicks = 0;
    }
    private static ZDOMan MakeWorld()
    {
        var manager = New<ZDOMan>();
        // Enough occupied sectors for multiple calls, including the native repeated boundary.
        typeof(ZDOMan).GetField("m_objectsBySector", Instance)!.SetValue(manager,
            Enumerable.Range(0, 905).Select(_ => new List<ZDO>()).ToArray());
        return manager;
    }
    private static ZDO Add(string prefab, int sector)
    {
        var zdo = New<ZDO>(); zdo.m_uid = new ZDOID(987654321L, ++NextId);
        typeof(ZDO).GetField("m_prefab", Instance)!.SetValue(zdo, prefab.GetStableHashCode());
        ((List<ZDO>[])typeof(ZDOMan).GetField("m_objectsBySector", Instance)!.GetValue(World)!)[sector].Add(zdo);
        Zdos.Add(zdo.m_uid, zdo);
        return zdo;
    }
    private static void Tick() { Now += 0.01f; Call("ExecuteServerTick"); }
    private static bool Pending() => (bool)Get("_pendingBootstrapScan")!;
    private static bool EmptyCursor() => Get("_bootstrapPrefabs") == null && (int)Get("_bootstrapPrefabIndex")! == 0 && (int)Get("_bootstrapZdoIndex")! == 0;
    private static IDictionary Tracked() => (IDictionary)Get("TrackedDespawnTargets")!;
    private static IDictionary Observations() => (IDictionary)Get("PendingDespawnObservations")!;
    private static List<ZDO> Buffer() => (List<ZDO>)Get("BootstrapScanBuffer")!;
    private static object? Get(string name) => Manager.GetField(name, Static)!.GetValue(null);
    private static T New<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        Checks++;
    }
    private static object? Call(string name, params object?[] args) => Copy(Manager.GetMethod(name, Static)!).Invoke(null, args);

    private static MethodInfo Copy(MethodInfo method)
    {
        if (Copies.TryGetValue(method, out MethodInfo copy)) return copy;
        using var body = new DynamicMethodDefinition(method);
        typeof(DynamicMethodDefinition).GetProperty(nameof(DynamicMethodDefinition.OriginalMethod))!.SetValue(body, null);
        body.OwnerType = typeof(BootstrapScanTests);
        foreach (var instruction in body.Definition.Body.Instructions)
        {
            if (!(instruction.Operand is MethodReference target)) continue;
            string type = target.DeclaringType.FullName;
            string? boundary = type == "BossRules.BossRulesPlugin" && target.Name == "IsRuntimeServer" ? nameof(IsServer)
                : type == "BossRules.BossRulesConfig" && target.Name == "IsDespawnRulesEnabled" ? nameof(IsEnabled)
                : type == "BossRules.BossRulesRuntime" ? target.Name switch
                {
                    "GetDespawnLookupVersion" => nameof(GetVersion), "GetDespawnBootstrapPrefabOrder" => nameof(GetPrefabs),
                    "TryGetCachedDespawnTrackingPrefabHashEligibility" => nameof(Eligible), _ => null
                }
                : type == "ZDOMan" ? target.Name switch
                {
                    "get_instance" => nameof(GetWorld), "GetAllZDOsWithPrefabIterative" => nameof(Query), "GetZDO" => nameof(GetZdo), _ => null
                }
                : type == "ZNetScene" && target.Name == "get_instance" ? nameof(GetScene)
                : type == "ObjectDB" && target.Name == "get_instance" ? nameof(GetDatabase)
                : type == "UnityEngine.Object" && target.Name == "op_Equality" ? nameof(SameObject)
                : type == "UnityEngine.Time" && target.Name == "get_time" ? nameof(Clock)
                : type == Manager.FullName ? target.Name switch
                {
                    "GetCurrentDespawnClockSeconds" => nameof(DespawnClock), "IsDeadZdo" => nameof(IsDead),
                    "PruneTrackedDespawnTargetsAgainstCurrentConfig" => nameof(Prune),
                    "ApplyPendingDespawnDetaches" => nameof(Detach), "ProcessScheduledDespawnChecks" => nameof(Scheduled), _ => null
                } : null;
            MethodInfo? replacement = boundary == null ? null : typeof(BootstrapScanTests).GetMethod(boundary, Static);
            if (replacement == null && type == Manager.FullName)
            {
                MethodInfo original = (MethodInfo)target.ResolveReflection();
                replacement = target.Name == "ApplyObservation" && target.Parameters.Count == 3
                    ? ResolveBoundary(original) : Copy(original);
            }
            if (replacement == null) continue;
            instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
            instruction.Operand = body.Module.ImportReference(replacement);
        }
        return Copies[method] = body.Generate();
    }

    // Adapt the scene-dependent rule resolution boundary to its private production return type.
    private static MethodInfo ResolveBoundary(MethodInfo original)
    {
        var method = new DynamicMethod("ResolveFixture", original.ReturnType, new[] { typeof(ZDO), typeof(int), typeof(string) }, typeof(BootstrapScanTests), true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(BootstrapScanTests).GetMethod(nameof(Resolve), Static)!);
        il.Emit(OpCodes.Castclass, original.ReturnType); il.Emit(OpCodes.Ret);
        return method;
    }
    private static object Resolve(ZDO zdo, int hash, string name)
    {
        object state = Call("GetOrCreateTrackedDespawnState", zdo.m_uid)!;
        Call("ScheduleTrackedDespawnCheck", zdo.m_uid, state, (double)Now);
        return state;
    }
    private static bool Query(ZDOMan world, string prefab, List<ZDO> results, ref int cursor)
    {
        Check(results.Count == 0, "each native query receives an empty scratch buffer");
        Queries.Add((prefab, cursor));
        bool complete = world.GetAllZDOsWithPrefabIterative(prefab, results, ref cursor);
        if (ThrowQuery) throw new InvalidOperationException("fixture query failure");
        return complete;
    }
    private static bool SameObject(UnityEngine.Object? a, UnityEngine.Object? b) => ReferenceEquals(a, b);
    private static bool IsServer() => Server;
    private static bool IsEnabled() => Enabled;
    private static int GetVersion() => Version;
    private static IReadOnlyList<string> GetPrefabs() => Prefabs;
    private static bool Eligible(int hash, out bool eligible) { eligible = true; return true; }
    private static ZDOMan? GetWorld() => World;
    private static ZNetScene GetScene() => Scene;
    private static ObjectDB GetDatabase() => Database;
    private static float Clock() => Now;
    private static double DespawnClock() => Now;
    private static bool IsDead(ZDOID id) => Dead.Contains(id);
    private static ZDO? GetZdo(ZDOMan world, ZDOID id) => Zdos.TryGetValue(id, out ZDO zdo) ? zdo : null;
    private static void Prune() => Prunes++;
    private static void Detach(double now) => DetachTicks++;
    private static void Scheduled(double now) => ScheduledTicks++;
}
