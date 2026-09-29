using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BossRules;
using Mono.Cecil;
using UnityEngine;

internal static class Program
{
    private static int Checks;
    private static uint Id;
    private static readonly Vector3 AltarPoint = new(10, 20, 30);
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        Checks++;
    }
    private static BossRuleConfigurationState Parse(string yaml)
    {
        Check(BossRuleConfiguration.TryParse(yaml, "test", out var state), "parse " + yaml);
        return state;
    }
    private static void Reset(string yaml = "")
    {
        ZNetScene.instance = new(); ObjectDB.instance = new();
        BossRulesPlugin.BossRulesLogger.Warnings.Clear();
        BossRulesRuntime.Reset(); BossPhaseRuntime.Restore(null);
        BossRulesRuntime.Reload(Parse(yaml));
    }
    private static GameObject Prefab(string name, bool? boss = true)
    {
        GameObject go = new() { name = name };
        go.Add(new ZNetView());
        if (boss.HasValue) go.Add(new Character { Boss = boss.Value });
        else go.Add(new Ragdoll());
        ZNetScene.instance.m_prefabs.Add(go);
        return go;
    }
    private static EffectList Effects(params GameObject[] prefabs) => new()
    {
        m_effectPrefabs = prefabs.Select(p => new EffectList.EffectData { m_prefab = p }).ToArray()
    };
    private static GameObject Spawn(GameObject prefab)
    {
        GameObject go = new() { name = prefab.name + "(Clone)" };
        go.Add(new ZNetView { m_persistent = prefab.GetComponent<ZNetView>().m_persistent,
            Data = new ZDO { m_uid = new(10, ++Id), Prefab = prefab.name.GetStableHashCode() } });
        if (prefab.TryGetComponent(out Character character))
            go.Add(new Character { Boss = character.Boss, m_deathEffects = character.m_deathEffects });
        if (prefab.TryGetComponent(out Ragdoll ragdoll))
            go.Add(new Ragdoll { m_removeEffect = ragdoll.m_removeEffect });
        return go;
    }
    private static ZDO Data(GameObject go) => go.GetComponent<ZNetView>().Data;
    private static GameObject Summon(GameObject prefab, bool free = false)
    {
        GameObject item = new() { name = "HatefulBlood" }; item.Add(new ItemDrop());
        ObjectDB.instance.m_items.Add(item);
        GameObject altar = new() { name = "Altar" }; altar.transform.position = AltarPoint;
        var bowl = altar.Add(new OfferingBowl { m_bossPrefab = prefab, m_bossItem = item.GetComponent<ItemDrop>(), m_bossItems = 3 });
        altar.Add(new OfferingBowlRuntimeState { FreeSummonQueued = free });
        AltarRuntime.PrepareAndQueueOfferingBowlRefundPayload(bowl, default);
        GameObject go = Spawn(prefab);
        AltarRuntime.TryMarkCreatedAltarSummonZdo(Data(go).GetPrefab(), Data(go));
        Check(AltarRuntime.IsAltarSummoned(Data(go)), "initial altar provenance");
        return go;
    }
    private static void Complete(GameObject source, params GameObject[] created)
    {
        Character? character = source.GetComponent<Character>();
        Ragdoll? ragdoll = source.GetComponent<Ragdoll>();
        BossPhaseRuntime.Transition? previous = character != null
            ? BossPhaseRuntime.Enter(character) : BossPhaseRuntime.Enter(ragdoll!);
        try { BossPhaseRuntime.AfterEffects(character != null ? character.m_deathEffects : ragdoll!.m_removeEffect, created); }
        finally { BossPhaseRuntime.Restore(previous); }
    }
    private static GameObject Advance(GameObject source, GameObject prefab)
    {
        GameObject next = Spawn(prefab); Complete(source, next); return next;
    }
    private static bool Refunds(GameObject go, out IReadOnlyCollection<DespawnRefundDrop> refunds) =>
        AltarRuntime.TryResolveAltarSummonRefunds(Data(go), out refunds);
    private static bool Tracked(GameObject go, out IReadOnlyCollection<DespawnRefundDrop> refunds) =>
        BossRulesRuntime.TryResolveDespawnTrackingRule(Data(go), Data(go).GetPrefab(), Utils.GetPrefabName(go),
            out _, out _, out _, out refunds);

    private static void Configuration()
    {
        Reset();
        Check(Parse("").BossPhases.Count == 2, "missing section uses shipped chains");
        Check(Parse("bossPhases: []").BossPhases.Count == 0, "explicit empty disables chains");
        Check(Parse("bossPhases:\n  - [A, B]").BossPhases.Single().SequenceEqual(new[] { "A", "B" }), "custom replaces defaults");
        foreach (string yaml in new[] { "bossPhases: [ [A] ]", "bossPhases: [ [A, A] ]",
            "bossPhases: [ [A, B], [B, C] ]", "bossPhases: [ [A, ''] ]", "bossPhases: [ null ]" })
            Check(!BossRuleConfiguration.TryParse(yaml, "bad test", out _), "reject malformed/ambiguous chains");
        Check(Parse("despawn:\n  defaults: 32, 50\n  rules: [ 'Fader, 0, 20, false' ]").DespawnRules.Single().Refunds == false,
            "existing despawn format preserved");
    }
    private static (GameObject First, GameObject Second, GameObject Third) FrozenPrefabs()
    {
        var a = Prefab("FrozenKing"); var b = Prefab("FrozenKing_p2"); var c = Prefab("FrozenKing_p3");
        a.GetComponent<Character>().m_deathEffects = Effects(b);
        b.GetComponent<Character>().m_deathEffects = Effects(c);
        return (a,b,c);
    }
    private static void DirectChain()
    {
        Reset(); var (a,b,c) = FrozenPrefabs();
        GameObject first = Summon(a);
        Check(Refunds(first, out var before) && before.Single().Amount == 3, "paid initial refund");
        GameObject second = Advance(first,b);
        Check(AltarRuntime.HasTransferredAltarSummon(Data(first)) && !Refunds(first, out _), "source consumed");
        Check(Refunds(second, out var middle) && middle.Single().Amount == 3, "p2 original cost");
        Check(middle.Single().DropPointOverride!.Value.Equals(AltarPoint), "original altar point");
        Data(second).Position = new Vector3(900,1000,500);
        second.GetComponent<ZNetView>().Data = Data(second).Reload();
        BossRulesRuntime.Reload(Parse(""));
        GameObject third = Advance(second,c);
        Check(Refunds(third, out var last) && !Refunds(second, out _) && last.Single().Amount == 3, "p3 after simulated persistence/config reload");
        Check(last.Single().DropPointOverride!.Value.Equals(AltarPoint), "p3 retains altar rather than current position");
        var duplicate = Advance(first,b);
        Check(!AltarRuntime.IsAltarSummoned(Data(duplicate)), "repeated source callback grants no second record");
        first.GetComponent<ZNetView>().Data = Data(first).Reload();
        Check(!AltarRuntime.IsAltarSummoned(Data(Advance(first,b))), "consumed marker survives reload");
        var natural = Advance(Spawn(a), b);
        Check(!AltarRuntime.IsAltarSummoned(Data(natural)) && !Refunds(natural, out _), "natural phases never invent offerings");
        var free = Advance(Advance(Summon(a,true),b),c);
        Check(AltarRuntime.IsAltarSummoned(Data(free)) && !Refunds(free, out _), "free provenance and empty cost through all phases");
        Complete(third, Spawn(Prefab("Minion",false)));
        Check(!AltarRuntime.HasTransferredAltarSummon(Data(third)), "terminal phase has no successor");
    }
    private static void RagdollChain()
    {
        Reset();
        var a = Prefab("ML_AshHuldraQueen1",false); var b = Prefab("ML_AshHuldraQueen2",false);
        var bridge = Prefab("InternalTransformUnknownToUser",null); var c = Prefab("ML_AshHuldraQueen3");
        a.GetComponent<Character>().m_deathEffects = Effects(b);
        b.GetComponent<Character>().m_deathEffects = Effects(bridge);
        bridge.GetComponent<Ragdoll>().m_removeEffect = Effects(c);
        var first = Summon(a);
        Check(Tracked(first,out var refunds) && refunds.Single().Amount == 3, "non-boss altar first phase is tracked");
        Check(!Tracked(Spawn(a),out _), "natural non-boss first phase excluded");
        var second = Advance(first,b);
        Check(Tracked(second,out _) && !second.GetComponent<Character>().IsBoss(), "non-boss next phase tracked without changing game flag");
        var intermediate = Advance(second,bridge);
        Check(AltarRuntime.IsAltarSummoned(Data(intermediate)), "hidden bridge inherits record");
        Check(!Tracked(intermediate,out _), "bridge is not a despawn target");
        intermediate.GetComponent<ZNetView>().Data = Data(intermediate).Reload();
        Data(intermediate).Owner = false;
        Check(!AltarRuntime.IsAltarSummoned(Data(Advance(intermediate,c))), "nonowner cannot hand off bridge");
        Data(intermediate).Owner = true; // New owner resumes the persisted bridge.
        var third = Advance(intermediate,c);
        Check(Refunds(third,out var final) && final.Single().Amount == 3, "bridge resumes after simulated owner change/reload");
        Check(!Refunds(second,out _) && !Refunds(intermediate,out _), "both earlier stages consumed");
        Check(Tracked(third,out _) && !AltarRuntime.IsAltarSummoned(Data(Advance(intermediate,c))), "queen3 eligible and bridge cannot duplicate");
        var free = Advance(Advance(Advance(Summon(a,true),b),bridge),c);
        Check(AltarRuntime.IsAltarSummoned(Data(free)) && !Refunds(free,out _), "free cost through hidden bridge");
        BossRulesRuntime.Reload(Parse("despawn:\n  rules: ['ML_AshHuldraQueen3, 0, 15, false']"));
        Check(BossRulesRuntime.TryResolveDespawnTrackingRule(Data(third),Data(third).GetPrefab(),c.name,out _,out float? range,out _,out var denied)
            && range == 0 && denied.Count == 0, "explicit phase range/refund overrides preserved");
    }
    private static void RejectionsAndLiveChanges()
    {
        Reset("bossPhases: [[A, B]]"); var a = Prefab("A"); var b = Prefab("B"); var minion = Prefab("Minion",false);
        var rag = Prefab("Bridge",null); rag.GetComponent<Ragdoll>().m_removeEffect = Effects(b);
        bool Path(EffectList effects, bool allow = true) => BossPhaseRuntime.TryFindSuccessor(effects,"B",allow,out _,out _);
        Check(Path(Effects(b)), "one direct candidate");
        Check(Path(Effects(rag)), "one hidden bridge candidate");
        Check(!Path(Effects(rag),false), "no unbounded ragdoll recursion");
        Check(!Path(Effects(b,minion)), "reject direct branch");
        Check(!Path(Effects(b,rag)), "reject direct plus bridge branch");
        Check(!Path(Effects(rag,rag)), "reject duplicate bridge effects");
        rag.GetComponent<Ragdoll>().m_removeEffect = Effects(b,minion);
        Check(!Path(Effects(rag)), "reject branch inside bridge");
        rag.GetComponent<Ragdoll>().m_removeEffect = Effects(b);
        rag.GetComponent<ZNetView>().m_persistent = false;
        Check(!Path(Effects(rag)), "reject nonpersistent bridge");
        rag.GetComponent<ZNetView>().m_persistent = true;
        var disabled = Effects(b,minion); disabled.m_effectPrefabs[1].m_enabled = false;
        Check(Path(disabled), "disabled spawn does not create a branch");
        b.GetComponent<ZNetView>().m_persistent = false;
        Check(!Path(Effects(b)), "reject nonpersistent target"); b.GetComponent<ZNetView>().m_persistent = true;
        a.GetComponent<Character>().m_deathEffects = Effects(b);
        var first = Summon(a); var second = Spawn(b); var other = Spawn(b);
        Complete(first,second,other);
        Check(!AltarRuntime.IsAltarSummoned(Data(second)) && !AltarRuntime.IsAltarSummoned(Data(other)), "multiple actual successors never duplicate cost");
        Check(BossRulesPlugin.BossRulesLogger.Warnings.Any(w=>w.Contains("Multiple successors")), "ambiguity diagnostic");
        Data(first).Owner = false; Complete(first,second);
        Check(!AltarRuntime.IsAltarSummoned(Data(second)), "source ownership required"); Data(first).Owner = true;
        Data(second).Owner = false; Complete(first,second);
        Check(!AltarRuntime.IsAltarSummoned(Data(second)), "target ownership required"); Data(second).Owner = true;
        Data(second).Prefab = minion.name.GetStableHashCode(); Complete(first,second);
        Check(!AltarRuntime.IsAltarSummoned(Data(second)), "actual ZDO prefab must match the effect result name");
        Data(second).Prefab = b.name.GetStableHashCode(); Data(second).Persistent = false; Complete(first,second);
        Check(!AltarRuntime.IsAltarSummoned(Data(second)), "actual ZDO persistence required"); Data(second).Persistent = true;
        var already = Summon(b,true); Complete(first,already);
        Check(!Refunds(already,out _), "existing free target record cannot be overwritten with paid source");
        Complete(first,second);
        Check(Refunds(second,out _), "failed attempts leave record available for a valid successor");

        var current = Summon(a); var pending = BossPhaseRuntime.Enter(current.GetComponent<Character>());
        try
        {
            current.GetComponent<Character>().m_deathEffects.m_effectPrefabs = Effects(b,minion).m_effectPrefabs;
            var result = Spawn(b);
            BossPhaseRuntime.AfterEffects(current.GetComponent<Character>().m_deathEffects,new[]{result});
            Check(!AltarRuntime.IsAltarSummoned(Data(result)), "recheck in-place live effect edits");
        }
        finally { BossPhaseRuntime.Restore(pending); }
        a.GetComponent<Character>().m_deathEffects = Effects(rag);
        var waiting = Advance(Summon(a),rag);
        BossRulesRuntime.Reload(Parse("bossPhases: []"));
        Check(!AltarRuntime.IsAltarSummoned(Data(Advance(waiting,b))), "live removal cancels persisted bridge edge");

        BossRulesRuntime.Reload(Parse("bossPhases: [[A, B]]"));
        a.GetComponent<Character>().m_deathEffects = Effects(b);
        var one = Summon(a); var two = Summon(a);
        var outer = BossPhaseRuntime.Enter(one.GetComponent<Character>());
        foreach(Type patch in new[]{typeof(CharacterDeathBossPhasePatch),typeof(RagdollRemovalBossPhasePatch)})
            patch.GetMethod("Finalizer",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!
                .Invoke(null,new object[]{default(BossPhaseRuntime.Scope)});
        var inner = BossPhaseRuntime.Enter(two.GetComponent<Character>());
        var secondTwo = Spawn(b);
        BossPhaseRuntime.AfterEffects(two.GetComponent<Character>().m_deathEffects,new[]{secondTwo});
        BossPhaseRuntime.Restore(inner); // Also models a nested finalizer after an exception.
        var secondOne = Spawn(b);
        BossPhaseRuntime.AfterEffects(one.GetComponent<Character>().m_deathEffects,new[]{secondOne});
        BossPhaseRuntime.Restore(outer);
        Check(Refunds(secondOne,out _) && Refunds(secondTwo,out _), "nested/skipped-prefix finalizers keep distinct sources");
        var unrelated = Spawn(b);
        BossPhaseRuntime.AfterEffects(a.GetComponent<Character>().m_deathEffects,new[]{unrelated});
        Check(!AltarRuntime.IsAltarSummoned(Data(unrelated)), "restored context does not leak into other effects");
    }
    private static void Metadata(string repo, string gameDll)
    {
        using var game = AssemblyDefinition.ReadAssembly(gameDll);
        TypeDefinition Original(string name) => game.MainModule.Types.Single(t=>t.Name==name);
        Check(Original("Character").Methods.Single(m=>m.Name=="OnDeath").IsPublic, "original death target public");
        Check(Original("Ragdoll").Methods.Single(m=>m.Name=="DestroyNow").IsPrivate, "original ragdoll target private");
        foreach (var pair in new[]{("Character","m_deathEffects"),("Ragdoll","m_removeEffect"),("ZNetView","m_persistent")})
            Check(Original(pair.Item1).Fields.Single(f=>f.Name==pair.Item2).IsPublic, "original public field " + pair);
        var create = Original("EffectList").Methods.Single(m=>m.Name=="Create");
        Check(create.Parameters.Count == 6 && create.ReturnType.FullName == "UnityEngine.GameObject[]", "original effect result contract");
        foreach(string name in new[]{"GetHashZDOID","GetZDOID","Set","IsOwner"})
            Check(Original("ZDO").Methods.Any(m=>m.Name==name && m.IsPublic), "original public ZDO."+name);
        using var mod = AssemblyDefinition.ReadAssembly(Path.Combine(repo,"bin","Debug","BossRules.dll"));
        TypeDefinition Product(string name) => mod.MainModule.Types.Single(t=>t.Name==name);
        bool Calls(MethodDefinition method,string name) => method.Body.Instructions.Any(i=>i.Operand is MethodReference called && called.Name==name);
        foreach(string name in new[]{"CharacterDeathBossPhasePatch","RagdollRemovalBossPhasePatch"})
        {
            var patch = Product(name);
            Check(Calls(patch.Methods.Single(m=>m.Name=="Prefix"),"Enter"), name+" enters context");
            Check(Calls(patch.Methods.Single(m=>m.Name=="Finalizer"),"Restore"), name+" restores on exception");
            Check(patch.Methods.Single(m=>m.Name=="Finalizer").ReturnType.FullName=="System.Void", name+" preserves original exception");
        }
        Check(Calls(Product("EffectListBossPhasePatch").Methods.Single(m=>m.Name=="Postfix"),"AfterEffects"), "actual result forwarding");
        Check(Calls(Product("DespawnRulesManager").Methods.Single(m=>m.Name=="ProcessTrackedDespawnTarget"),"HasTransferredAltarSummon"), "server skips consumed source even unloaded");
        Check(Calls(Product("BossRulesRuntime").Methods.Single(m=>m.Name=="Reload"),"Configure"), "synced config reaches phase runtime");
        Check(!mod.MainModule.AssemblyReferences.Any(r=>r.Name is "YamlDotNet" or "ServerSync" or "MonsterLabZ"), "merged libraries and no new optional hard dependency");
    }
    private static void Main(string[] args)
    {
        Configuration(); DirectChain(); RagdollChain(); RejectionsAndLiveChanges();
        if(args.Length != 2) throw new ArgumentException("repository root, original assembly_valheim.dll");
        Metadata(args[0],args[1]);
        Console.WriteLine($"PASS: {Checks} source-linked phase/config/refund and original/final DLL metadata checks. No Unity world/network execution.");
    }
}
