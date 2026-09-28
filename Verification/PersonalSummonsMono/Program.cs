using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using HarmonyLib;

internal static class Program
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static object State = null!;
    private static int Checks;
    private static bool SawSpawnPatch;
    private static bool ObserveSpawn() { SawSpawnPatch = true; return false; }
    private static void Check(bool result, string name)
    {
        if (!result) throw new Exception(name);
        Checks++;
    }
    private static int Main(string[] args)
    {
        string[] directories = { Path.GetDirectoryName(Path.GetFullPath(args[0]))!, args[1], args[2] };
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string? path = directories.Select(dir => Path.Combine(dir, new AssemblyName(e.Name).Name + ".dll")).FirstOrDefault(File.Exists);
            return path == null ? null : Assembly.LoadFrom(path);
        };
        try { Run(args); return 0; }
        catch (Exception ex) { System.Console.Error.WriteLine(ex); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string[] args)
    {
        string ynwPath = Environment.GetEnvironmentVariable("BOSSRULES_TEST_YNW_DLL")
            ?? Path.Combine(args[2], "../plugins/YouAreNotWorthy.dll");
        Assembly ynw = Assembly.LoadFrom(Path.GetFullPath(ynwPath));
        Assembly plugin = Assembly.LoadFrom(args[0]);
        Type runtime = plugin.GetType("BossRules.PersonalSummonRuntime", true)!;
        RuntimeHelpers.RunClassConstructor(runtime.TypeHandle);
        Check(runtime.GetField("SpawnBoss", Static)!.GetValue(null) != null, "native private SpawnBoss accessor");
        Check(runtime.GetField("CanSpawnBoss", Static)!.GetValue(null) != null, "native private out-vector accessor");
        // The production cached delegate must still pass through Harmony; otherwise
        // the normal SpawnBoss refund/cooldown prefixes would be bypassed on Mono.
        Harmony probe = new("bossrules.verification.personal-spawn");
        MethodInfo nativeSpawn = typeof(OfferingBowl).GetMethod("SpawnBoss", BindingFlags.NonPublic | BindingFlags.Instance)!;
        probe.Patch(nativeSpawn, prefix: new HarmonyMethod(typeof(Program), nameof(ObserveSpawn)));
        try
        {
            ((Action<OfferingBowl, UnityEngine.Vector3>)runtime.GetField("SpawnBoss", Static)!.GetValue(null)!)(null!, default);
            Check(SawSpawnPatch, "cached private delegate enters Harmony on game Mono");
        }
        finally { probe.UnpatchSelf(); }
        Type bridge = plugin.GetType("BossRules.YouAreNotWorthyBridge", true)!;
        Check((bool)bridge.GetProperty("Available", Static)!.GetValue(null)!, "real merged YNW enum/out delegate binding on game Mono");
        Check((int)ynw.GetType("YouAreNotWorthy.YouAreNotWorthyApi", true)!.GetField("ApiVersion")!.GetRawConstantValue()! == 2, "deployed YNW API version");

        Type stateType = plugin.GetType("BossRules.OfferingBowlRuntimeState", true)!;
        State = FormatterServices.GetUninitializedObject(stateType);
        stateType.GetProperty("FreeSummonQueued")!.SetValue(State, true);
        using DynamicMethodDefinition lookup = new("FixtureState", stateType, new[] { typeof(UnityEngine.Component) });
        ILProcessor il = lookup.Definition.Body.GetILProcessor();
        il.Emit(OpCodes.Ldsfld, lookup.Module.ImportReference(typeof(Program).GetField(nameof(State), Static)!));
        il.Emit(OpCodes.Castclass, lookup.Module.ImportReference(stateType));
        il.Emit(OpCodes.Ret);
        MethodInfo lookupMethod = lookup.Generate();
        Type altar = plugin.GetType("BossRules.AltarRuntime", true)!;
        using DynamicMethodDefinition builder = CopyWithState(altar.GetMethod("BuildOfferingRefundPayload", Static)!, lookupMethod);
        MethodInfo build = builder.Generate();
        Check((string)build.Invoke(null, new object?[] { null })! == "", "compiled free refund builder returns empty before item lookup");
        using DynamicMethodDefinition consumer = CopyWithState(altar.GetMethod("ConsumePreparedOfferingRefundPayload", Static)!, lookupMethod);
        foreach (Instruction instruction in consumer.Definition.Body.Instructions)
            if (instruction.Operand is MethodReference method && method.Name == "BuildOfferingRefundPayload")
                instruction.Operand = consumer.Module.ImportReference(build);
        MethodInfo consume = consumer.Generate();
        PropertyInfo payload = stateType.GetProperty("PendingRefundPayload")!;
        payload.SetValue(State, "");
        Check((string)consume.Invoke(null, new object?[] { null })! == "" && payload.GetValue(State) == null,
            "compiled delayed consumption preserves and consumes explicit empty payload");
        Check((string)consume.Invoke(null, new object?[] { null })! == "", "free fallback cannot invent refunds");
        payload.SetValue(State, "HatefulBlood:3");
        Check((string)consume.Invoke(null, new object?[] { null })! == "HatefulBlood:3", "paid prepared payload remains unchanged");
        System.Console.WriteLine($"PASS: {Checks} checks using game Mono, original game DLL and both final merged plugins; only component lookup substituted for refund bodies. No Unity world/network execution.");
    }
    private static DynamicMethodDefinition CopyWithState(MethodInfo method, MethodInfo lookup)
    {
        DynamicMethodDefinition copy = new(method);
        foreach (Instruction instruction in copy.Definition.Body.Instructions)
            if (instruction.Operand is GenericInstanceMethod called && called.Name == "GetComponent"
                && called.GenericArguments.Any(type => type.Name == "OfferingBowlRuntimeState"))
            {
                instruction.OpCode = OpCodes.Call;
                instruction.Operand = copy.Module.ImportReference(lookup);
            }
        return copy;
    }
}
