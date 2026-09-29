using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using HarmonyLib;

internal static class Program
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int Checks;
    private static uint Id;
    // Only revision/world-dirty notification is suppressed. Production transfer and
    // original ZDO/ZDOExtraData storage code run unchanged in the game's Mono.
    private static bool SkipWorldNotification() => false;
    // Character/Player static animation IDs require Unity's native player. Their
    // values are irrelevant to phase patch binding, so provide managed fixture IDs.
    private static bool AnimationHash(string __0, ref int __result)
    {
        __result = __0.GetStableHashCode();
        return false;
    }
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        Checks++;
    }
    private static int Main(string[] args)
    {
        string[] dirs = { Path.GetDirectoryName(Path.GetFullPath(args[0]))!, args[1], args[2] };
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string? path = dirs.Select(d=>Path.Combine(d,new AssemblyName(e.Name).Name+".dll")).FirstOrDefault(File.Exists);
            return path == null ? null : Assembly.LoadFrom(path);
        };
        try { Run(args); return 0; }
        catch(Exception ex) { System.Console.Error.WriteLine(ex); return 1; }
    }
    private static ZDO New(bool owner = true)
    {
        ZDO zdo = (ZDO)FormatterServices.GetUninitializedObject(typeof(ZDO));
        zdo.m_uid = new ZDOID(834756219, ++Id);
        typeof(ZDO).GetProperty("Owner",Instance)!.SetValue(zdo,owner);
        return zdo;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string[] args)
    {
        Assembly plugin = Assembly.LoadFrom(args[0]);
        Type altar = plugin.GetType("BossRules.AltarRuntime",true)!;
        Type runtime = plugin.GetType("BossRules.BossPhaseRuntime",true)!;
        RuntimeHelpers.RunClassConstructor(runtime.TypeHandle);
        Harmony harness = new("bossrules.verification.phases");
        Type animator = Assembly.Load("UnityEngine.AnimationModule").GetType("UnityEngine.Animator",true)!;
        harness.Patch(animator.GetMethod("StringToHash",Static),
            prefix:new HarmonyMethod(typeof(Program),nameof(AnimationHash)));
        harness.Patch(typeof(ZDO).GetMethod("IncreaseDataRevision",Instance),
            prefix:new HarmonyMethod(typeof(Program),nameof(SkipWorldNotification)));
        try
        {
            foreach(string name in new[]{"CharacterDeathBossPhasePatch","RagdollRemovalBossPhasePatch","EffectListBossPhasePatch"})
            {
                var patched = harness.CreateClassProcessor(plugin.GetType("BossRules."+name,true)!).Patch();
                Check(patched.Count == 1, "bind production " + name);
            }
            Check(new EffectList().Create(default,default).Length == 0, "native empty effect call with production postfix");
            FieldInfo current = runtime.GetField("_current",Static)!;
            object enclosing = Activator.CreateInstance(runtime.GetNestedType("Transition",BindingFlags.NonPublic)!,true)!;
            foreach(var target in new[]{typeof(Character).GetMethod("OnDeath",Instance)!,typeof(Ragdoll).GetMethod("DestroyNow",Instance)!})
            {
                // An earlier mod cancels a nested death/removal. Harmony skips our
                // stateful prefix, but still runs its finalizer with default state.
                MethodInfo skip = typeof(Program).GetMethod(nameof(SkipWorldNotification),Static)!;
                harness.Patch(target,prefix:new HarmonyMethod(skip) { priority = Priority.First });
                current.SetValue(null,enclosing);
                target.Invoke(FormatterServices.GetUninitializedObject(target.DeclaringType!),null);
                Check(ReferenceEquals(current.GetValue(null),enclosing), "skipped prefix preserves enclosing context: " + target.Name);
                current.SetValue(null,null);
                harness.Unpatch(target,skip);
            }
            bool Transfer(ZDO from,ZDO to) => (bool)altar.GetMethod("TryTransferAltarSummon",Static)!.Invoke(null,new object[]{from,to})!;
            bool Consumed(ZDO zdo) => (bool)altar.GetMethod("HasTransferredAltarSummon",Static)!.Invoke(null,new object[]{zdo})!;
            ZDO Paid(string payload)
            {
                ZDO source = New();
                source.Set("BossRules.altar_summon",true);
                source.Set("BossRules.altar_refunds",payload);
                source.Set("BossRules.altar_refund_point","12,5000,-34");
                return source;
            }
            ZDO first = Paid("HatefulBlood:3"); ZDO second = New(); ZDO third = New();
            Check(Transfer(first,second), "actual managed transfer");
            Check(second.GetString("BossRules.altar_refunds")=="HatefulBlood:3" &&
                second.GetString("BossRules.altar_refund_point")=="12,5000,-34", "original ZDO string fields preserve exact cost/point");
            Check(first.GetZDOID("BossRules.altar_successor")==second.m_uid && Consumed(first), "original paired ZDOID storage");
            Check(!Transfer(first,New()), "source replay rejected");
            Check(Transfer(second,third) && Consumed(second), "second hop on original ZDO storage");
            Check(third.GetBool("BossRules.altar_summon") && third.GetString("BossRules.altar_refunds")=="HatefulBlood:3", "terminal provenance");
            ZDO free = Paid(""); ZDO freeNext = New();
            Check(Transfer(free,freeNext) && freeNext.GetBool("BossRules.altar_summon") &&
                freeNext.GetString("BossRules.altar_refunds","sentinel")=="", "explicit empty remains free");
            Check(!Transfer(Paid("HatefulBlood:3"),freeNext), "paid cannot overwrite free target");
            Check(!Transfer(New(),New()), "natural source rejected");
            ZDO nonowner = Paid("HatefulBlood:3");
            typeof(ZDO).GetProperty("Owner",Instance)!.SetValue(nonowner,false);
            Check(!Transfer(nonowner,New()), "source owner required");
            Check(!Transfer(Paid("HatefulBlood:3"),New(false)), "target owner required");
            Check(!Transfer(third,third), "self transfer rejected");
        }
        finally { harness.UnpatchSelf(); }
        System.Console.WriteLine($"PASS: {Checks} original-game Mono phase patch/storage checks; animation IDs and world-dirty notification substituted, no Unity scene/network/save execution.");
    }
}
