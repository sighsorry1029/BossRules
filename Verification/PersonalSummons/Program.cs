using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BossRules;
using HarmonyLib;
using Mono.Cecil;
using UnityEngine;
using YouAreNotWorthy;

internal static class Program
{
    private static int _checks;
    private static OfferingBowl Bowl = null!;
    private static ZNetView View = null!;
    private static ZDO Character = null!;
    private static void Check(bool result, string name)
    {
        if (!result) throw new Exception("FAIL: " + name);
        _checks++;
    }
    private static void Call(string name, params object[] args) => typeof(PersonalSummonRuntime)
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
    private static void Reset()
    {
        PersonalSummonRuntime.Shutdown();
        Time.realtimeSinceStartup = 0;
        BossRulesConfig.Enabled = true;
        AltarRuntime.Block = false;
        ZRoutedRpc.instance = new();
        ZNet.instance = new();
        ZDOMan.instance = new();
        ZNetScene.instance = new();
        YouAreNotWorthyApi.Keys.Clear();
        YouAreNotWorthyApi.Keys["LastBossGate_Open"] = 3;
        YouAreNotWorthyApi.ItemResult = ItemUseQueryResult.Allowed;
        YouAreNotWorthyApi.ItemRequirement = "";
        GameObject root = new() { name = "offeraltar_FrozenKing_bossroom" };
        View = root.Add(new ZNetView());
        View.Data = new ZDO { Owner = 10, m_uid = new ZDOID { UserID = 10, ID = 2 }, Prefab = root.name.GetStableHashCode() };
        Bowl = root.Add(new OfferingBowl(View));
        Bowl.m_bossPrefab.name = "FrozenKing";
        Bowl.m_bossItem.gameObject.name = "HatefulBlood";
        Character = new ZDO { Owner = 20, m_uid = new ZDOID { UserID = 20, ID = 1 }, Prefab = "Player".GetStableHashCode() };
        Player.m_localPlayer = new Player { Id = Character.m_uid };
        ZDOMan.instance.Objects.Add(View.Data.m_uid, View.Data);
        ZDOMan.instance.Objects.Add(Character.m_uid, Character);
        ZNetScene.instance.Objects.Add(View.Data.m_uid, root);
        ZNet.instance.Peers.Add(20, new ZNetPeer { m_uid = 20, m_characterID = Character.m_uid });
        PersonalSummonRuntime.Update();
    }
    private static ZPackage Begin(long requester = 20, string path = ".")
    {
        ZPackage package = new();
        package.Write(View.Data.m_uid); package.Write(path); package.Write(Character.m_uid);
        Call("OnBegin", requester, package);
        return (ZPackage)ZRoutedRpc.instance.Sent.Last().Args[0];
    }
    private static (string Token, bool Allowed) Authorize(ZPackage package, long owner = 10)
    {
        ZNet.instance.Server = true;
        ZRoutedRpc.instance.Id = 1;
        ZNetScene ownerScene = ZNetScene.instance;
        Player? ownerPlayer = Player.m_localPlayer;
        Player.m_localPlayer = null;
        ZNetScene.instance = new(); // A dedicated server has ZDOs, but no remote bowl objects.
        Call("OnAuthorize", owner, package);
        ZNetScene.instance = ownerScene;
        Player.m_localPlayer = ownerPlayer;
        var decision = ZRoutedRpc.instance.Sent.Last(message => message.Method.Contains("Decision"));
        ZNet.instance.Server = false;
        ZRoutedRpc.instance.Id = 10;
        return ((string)decision.Args[0], (bool)decision.Args[1]);
    }
    private static void Finish((string Token, bool Allowed) decision, long sender = 1) =>
        Call("OnDecision", sender, decision.Token, decision.Allowed);

    private static void PolicyTests()
    {
        using HarmonyScope scope = new();
        foreach (int completion in Enumerable.Range(0, 6))
        {
            var queen = PersonalSummonRuntime.Evaluate(1, _ => completion);
            Check(queen == (completion < 2 ? PersonalSummonRuntime.Eligibility.Unavailable
                : completion == 2 ? PersonalSummonRuntime.Eligibility.Free : PersonalSummonRuntime.Eligibility.Paid),
                "Queen explicit personal state " + completion);
            for (int gate = 0; gate < 6; gate++)
            {
                var north = PersonalSummonRuntime.Evaluate(2, key => key == "LastBossGate_Open" ? gate : completion);
                Check((north == PersonalSummonRuntime.Eligibility.Free) == (completion == 2 && gate == 3),
                    $"North gate={gate}, final={completion}");
            }
        }
        Check(PersonalSummonRuntime.Evaluate(0, _ => throw new Exception()) == PersonalSummonRuntime.Eligibility.Paid, "unrelated bowl bypasses key API");
        Check(YouAreNotWorthyBridge.Available, "optional bridge binds enum and out-string methods");
        YouAreNotWorthyApi.ItemResult = ItemUseQueryResult.MissingRequirement;
        YouAreNotWorthyApi.ItemRequirement = "required_key";
        Check(!YouAreNotWorthyBridge.CanUseItem("item", out string required) && required == "required_key", "bridge missing requirement out-string");
        YouAreNotWorthyApi.ItemResult = ItemUseQueryResult.Allowed;
        Check(YouAreNotWorthyBridge.CanUseItem("item", out _, new ZNetPeer()), "bridge server item enum");

        Reset();
        var decision = Authorize(Begin());
        Check(decision.Allowed, "dedicated approval without local bowl resolution on server");
        Finish(decision);
        Check(Bowl.Spawns == 1 && Bowl.FreeContextAtSpawn && HarmonyScope.PrefixWasFree, "native spawn and patched delegate use free context");
        Check(!PersonalSummonRuntime.IsFreeSpawn(Bowl), "free context restored after call");
        Finish(decision);
        Check(Bowl.Spawns == 1, "approval replay cannot spawn twice");

        Reset(); decision = Authorize(Begin()); Finish(decision, 55);
        Check(Bowl.Spawns == 0, "non-server approval rejected");
        Finish(decision); Check(Bowl.Spawns == 1, "forged approval did not consume legitimate pending");
        Reset(); ZPackage authorization = Begin(); Begin();
        Check(ZRoutedRpc.instance.Sent.Count == 1, "one pending request per bowl");
        Reset(); decision = Authorize(Begin()); Time.realtimeSinceStartup = 11; Finish(decision);
        Check(Bowl.Spawns == 0, "late approval expires");
        Reset(); decision = Authorize(Begin()); View.Data.Owner = 30; Finish(decision);
        Check(Bowl.Spawns == 0, "owner transfer cancels approval");
        Reset(); decision = Authorize(Begin()); Character.Position = new Vector3(0, 100, 0); Finish(decision);
        Check(Bowl.Spawns == 0, "same XZ different floor rejected at execution");
        Reset(); decision = Authorize(Begin()); Character.Dead = true; Finish(decision);
        Check(Bowl.Spawns == 0, "death before approval cancels");
        Reset(); decision = Authorize(Begin()); Bowl.Queued = true; Finish(decision);
        Check(Bowl.Spawns == 0, "paid/other queued spawn wins without duplicate");
        Reset(); decision = Authorize(Begin()); AltarRuntime.Block = true; Finish(decision);
        Check(Bowl.Spawns == 0, "boss/cooldown block rechecked after approval");
        Reset(); decision = Authorize(Begin()); Bowl.m_bossItem.gameObject.name = "new_item"; Finish(decision);
        Check(Bowl.Spawns == 0, "live offering item change invalidates approval");
        Reset(); decision = Authorize(Begin()); Bowl.SpawnPossible = false; Finish(decision);
        Check(Bowl.Spawns == 0, "failed native spawn search has no side effect");
        Reset(); decision = Authorize(Begin()); PersonalSummonRuntime.Shutdown(); Finish(decision);
        Check(Bowl.Spawns == 0, "world/plugin shutdown clears approval");
        Reset(); decision = Authorize(Begin()); ZRoutedRpc.instance = new(); Finish(decision);
        Check(Bowl.Spawns == 0, "RPC session replacement before next Update rejects old approval");
        Reset(); authorization = Begin(); ZNet.instance.Peers[20].Authenticated = false; decision = Authorize(authorization); Finish(decision);
        Check(!decision.Allowed && Bowl.Spawns == 0, "unauthenticated peer fails closed");
        Reset(); authorization = Begin(); ZNet.instance.Peers.Remove(20); decision = Authorize(authorization);
        Check(!decision.Allowed, "disconnected requester never falls back to host keys");
        Reset(); authorization = Begin(); YouAreNotWorthyApi.Keys["defeated_frozenking_p3"] = 3; decision = Authorize(authorization);
        Check(!decision.Allowed, "completion between request and approval denies free attempt");
        Reset(); authorization = Begin(); YouAreNotWorthyApi.ItemResult = ItemUseQueryResult.MissingRequirement; decision = Authorize(authorization);
        Check(!decision.Allowed, "free cost retains item progression requirement");
        Reset(); authorization = Begin(); YouAreNotWorthyApi.Keys["defeated_frozenking_p3"] = 1; decision = Authorize(authorization);
        Check(!decision.Allowed, "missing snapshot is not missing completion");
        Reset(); YouAreNotWorthyApi.Keys["defeated_frozenking_p3"] = 1;
        Check(PersonalSummonRuntime.TryInteract(Bowl, Player.m_localPlayer!) && ZRoutedRpc.instance.Sent.Count == 0,
            "unavailable local snapshot handled without paid fallthrough");
        Reset(); YouAreNotWorthyApi.Keys["defeated_frozenking_p3"] = 3;
        Check(!PersonalSummonRuntime.TryInteract(Bowl, Player.m_localPlayer!), "completed player retains paid native path");
        Reset(); Bowl.m_setGlobalKey = "LastBossGate_Open"; Bowl.m_bossPrefab.name = "vfx_LastBossGate_destroyed";
        Check(!PersonalSummonRuntime.TryInteract(Bowl, Player.m_localPlayer!), "outer gate remains paid");
        Reset(); BossRulesConfig.Enabled = false;
        Check(!PersonalSummonRuntime.TryInteract(Bowl, Player.m_localPlayer!), "feature off preserves native path");
        Reset(); PersonalSummonRuntime.TryInteract(Bowl, Player.m_localPlayer!);
        Check(ZRoutedRpc.instance.Sent.Single().Target == 10, "E sends only to native object owner, no offering inventory required");

        Reset(); Bowl.gameObject.name = "offeraltar_queen"; Bowl.gameObject.Add(new DungeonGenerator());
        Bowl.m_bossPrefab.name = "SeekerQueen"; View.Data.Prefab = "DG_DvergrBoss".GetStableHashCode();
        Bowl.m_bossItem.gameObject.name = "TrophySeekerBrute";
        decision = Authorize(Begin()); Finish(decision);
        Check(Bowl.Spawns == 1 && Bowl.FreeContextAtSpawn, "Queen shared generator view follows the same authorized path");

        Reset(); authorization = Begin(); View.Data.Prefab = "unrelated".GetStableHashCode();
        ZNet.instance.Server = true; ZRoutedRpc.instance.Id = 1; Call("OnAuthorize", 10L, authorization);
        Check(!ZRoutedRpc.instance.Sent.Any(message => message.Method.Contains("Decision")), "wrong root prefab rejected by server");
        Reset(); authorization = Begin(); ZNet.instance.Server = true; ZRoutedRpc.instance.Id = 1; Call("OnAuthorize", 99L, authorization);
        Check(!ZRoutedRpc.instance.Sent.Any(message => message.Method.Contains("Decision")), "non-owner relay rejected");

        Reset(); View.Data.Owner = 1; Character.Owner = 1; ZRoutedRpc.instance.Id = 1; ZNet.instance.Server = true;
        authorization = Begin(1);
        Call("OnAuthorize", 1L, authorization);
        var localDecision = ZRoutedRpc.instance.Sent.Single(message => message.Method.Contains("Decision"));
        Call("OnDecision", 1L, localDecision.Args[0], localDecision.Args[1]);
        Check(Bowl.Spawns == 1, "local host uses own character with the same server authorization");
    }

    private static void Metadata(string repo, string game)
    {
        using AssemblyDefinition original = AssemblyDefinition.ReadAssembly(game);
        TypeDefinition bowl = original.MainModule.Types.Single(type => type.Name == "OfferingBowl");
        foreach (string name in new[] { "GetSpawnPosition", "CanSpawnBoss", "SpawnBoss" })
            Check(bowl.Methods.Single(method => method.Name == name).IsPrivate, "original private method " + name);
        Check(bowl.Methods.Single(method => method.Name == "CanSpawnBoss").Parameters[1].ParameterType.FullName == "UnityEngine.Vector3&", "original out spawn point");
        using AssemblyDefinition plugin = AssemblyDefinition.ReadAssembly(Path.Combine(repo, "bin/Debug/BossRules.dll"));
        Check(!plugin.MainModule.AssemblyReferences.Any(reference => reference.Name == "YouAreNotWorthy"), "no hard YNW assembly dependency");
        TypeDefinition runtime = plugin.MainModule.Types.Single(type => type.Name == "PersonalSummonRuntime");
        Check(!runtime.Methods.SelectMany(method => method.HasBody ? method.Body.Instructions : Enumerable.Empty<Mono.Cecil.Cil.Instruction>())
            .Any(instruction => instruction.Operand is MethodReference method && (method.Name == "RemoveItem" || method.Name == "SetGlobalKey")), "free path has no inventory/key mutation");
        TypeDefinition refunds = plugin.MainModule.Types.Single(type => type.Name == "AltarRuntime");
        var build = refunds.Methods.Single(method => method.Name == "BuildOfferingRefundPayload");
        Check(build.Body.Instructions.Any(instruction => instruction.Operand is MethodReference method && method.Name == "get_FreeSummonQueued"), "real refund builder checks free marker");
    }
    private sealed class HarmonyScope : IDisposable
    {
        private readonly Harmony _harmony = new("bossrules.personal.tests");
        internal static bool PrefixWasFree;
        internal HarmonyScope() => _harmony.Patch(AccessTools.Method(typeof(OfferingBowl), "SpawnBoss"),
            prefix: new HarmonyMethod(typeof(HarmonyScope), nameof(Prefix)));
        private static void Prefix(OfferingBowl __instance) => PrefixWasFree = PersonalSummonRuntime.IsFreeSpawn(__instance);
        public void Dispose() => _harmony.UnpatchAll(_harmony.Id);
    }
    private static int Main(string[] args)
    {
        try
        {
            PolicyTests();
            if (args.Length == 2) Metadata(args[0], args[1]);
            Console.WriteLine($"PASS: {_checks} policy/protocol/bridge/metadata checks; simulated game/network, not a Unity play test.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
