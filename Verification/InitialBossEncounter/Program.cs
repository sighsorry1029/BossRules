using System;
using System.IO;
using System.Linq;
using BossRules;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static readonly int Key = "BossRules.initial_boss_encounter".GetStableHashCode();
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        _checks++;
    }
    private static void Reset()
    {
        InitialBossEncounter.ResetRuntimeState();
        Player.Players.Clear();
        Time.time = 0;
        BossRulesRuntime.Range = 64;
        BossRulesRuntime.Ready = true;
    }
    private static void Tick() { Time.time += 1.1f; InitialBossEncounter.CheckApproaches(); }
    private static Character Boss(string name = "SeekerQueen", bool mark = true)
    {
        Character boss = new();
        boss.gameObject.name = name;
        if (mark)
        {
            int previous = name == "SeekerQueen"
                ? InitialBossEncounter.BeginRoomGeneration(true, ZoneSystem.SpawnMode.Full)
                : InitialBossEncounter.BeginLocationGeneration("DN_Bossroom", ZoneSystem.SpawnMode.Ghost);
            InitialBossEncounter.MarkCreatedBoss(name.GetStableHashCode(), boss.View.Data);
            InitialBossEncounter.RestoreGeneration(previous);
        }
        InitialBossEncounter.Track(boss);
        return boss;
    }
    private static Player AddPlayer(float x = 0, float y = 0, float z = 0)
    {
        Player player = new();
        player.transform.position = new Vector3(x, y, z);
        Player.Players.Add(player);
        return player;
    }
    private static bool Waiting(Character boss) => boss.View.Data.GetInt(Key) == 1;
    private static bool Encountered(Character boss) => boss.View.Data.GetInt(Key) == 2;

    private static void PolicyTests()
    {
        Reset();
        foreach (ZoneSystem.SpawnMode mode in new[] { ZoneSystem.SpawnMode.Full, ZoneSystem.SpawnMode.Ghost, ZoneSystem.SpawnMode.Client })
        {
            ZDO queen = new();
            int previous = InitialBossEncounter.BeginRoomGeneration(true, mode);
            InitialBossEncounter.MarkCreatedBoss("SeekerQueen".GetStableHashCode(), queen);
            InitialBossEncounter.RestoreGeneration(previous);
            Check(queen.GetInt(Key) == (mode == ZoneSystem.SpawnMode.Client ? 0 : 1), "Queen mode " + mode);
            ZDO north = new();
            previous = InitialBossEncounter.BeginLocationGeneration("DN_Bossroom", mode);
            InitialBossEncounter.MarkCreatedBoss("FrozenKing".GetStableHashCode(), north);
            InitialBossEncounter.RestoreGeneration(previous);
            Check(north.GetInt(Key) == (mode == ZoneSystem.SpawnMode.Client ? 0 : 1), "North mode " + mode);
        }
        int outer = InitialBossEncounter.BeginLocationGeneration("DN_Bossroom", ZoneSystem.SpawnMode.Ghost);
        int inner = InitialBossEncounter.BeginRoomGeneration(false, ZoneSystem.SpawnMode.Full);
        ZDO nested = new();
        InitialBossEncounter.MarkCreatedBoss("FrozenKing".GetStableHashCode(), nested);
        Check(nested.GetInt(Key) == 0, "unrelated nested room shadows provenance");
        InitialBossEncounter.RestoreGeneration(inner);
        InitialBossEncounter.MarkCreatedBoss("FrozenKing".GetStableHashCode(), nested);
        Check(nested.GetInt(Key) == 1, "outer generation restored");
        ZDO phase = new();
        InitialBossEncounter.MarkCreatedBoss("FrozenKing_p2".GetStableHashCode(), phase);
        Check(phase.GetInt(Key) == 0, "phase is not initial");
        ZDO altar = new() { Altar = true };
        InitialBossEncounter.MarkCreatedBoss("FrozenKing".GetStableHashCode(), altar);
        Check(altar.GetInt(Key) == 0, "altar precedence during creation");
        ZDO nonOwner = new() { Owner = false };
        InitialBossEncounter.MarkCreatedBoss("FrozenKing".GetStableHashCode(), nonOwner);
        Check(nonOwner.GetInt(Key) == 0, "non-owner cannot mark");
        InitialBossEncounter.RestoreGeneration(outer);
        ZDO ordinary = new();
        InitialBossEncounter.MarkCreatedBoss("FrozenKing".GetStableHashCode(), ordinary);
        Check(ordinary.GetInt(Key) == 0, "scope does not leak");

        Reset();
        Character boss = Boss();
        for (int i = 0; i < 100; i++) Tick();
        Check(Waiting(boss), "unvisited boss remains protected beyond 90 seconds");
        Player player = AddPlayer(0, 5000);
        Tick(); Check(Waiting(boss), "same XZ different dungeon height");
        player.transform.position = new Vector3(0, 32.01f, 0);
        Tick(); Check(Waiting(boss), "above height boundary");
        player.transform.position = new Vector3(0, -32.01f, 0);
        Tick(); Check(Waiting(boss), "below height boundary");
        player.transform.position = new Vector3(64, 0, 0);
        Tick(); Check(Waiting(boss), "existing strict XZ boundary");
        player.transform.position = new Vector3(60, 32, 0);
        Tick(); Check(Encountered(boss), "cylinder not sphere; Y boundary included");
        player.transform.position = new Vector3(1000, 5000, 0);
        Tick(); Check(Encountered(boss), "leaving does not restore protection");
        boss.View.Data = boss.View.Data.Reload();
        InitialBossEncounter.Track(boss);
        Tick(); Check(Encountered(boss), "encounter persists across data reload");

        Reset(); boss = Boss("FrozenKing"); player = AddPlayer();
        boss.View.Data = boss.View.Data.Reload();
        boss.View.Data.Owner = false;
        Tick(); Check(Waiting(boss), "non-owner cannot release protection");
        boss.View.Data.Owner = true;
        Tick(); Check(Encountered(boss), "owner migration retains observation");

        Reset(); boss = Boss(); player = AddPlayer();
        player.Health = 0;
        Tick(); Check(Waiting(boss), "dead player ignored");
        player.Health = 100; player.View.Valid = false;
        Tick(); Check(Waiting(boss), "invalid player ignored");
        player.View.Valid = true; player.ID = 0;
        Tick(); Check(Waiting(boss), "uninitialized player ignored");
        player.ID = 1; BossRulesRuntime.Range = 0;
        Tick(); Check(Waiting(boss), "disabled range");
        BossRulesRuntime.Range = 64; BossRulesRuntime.Ready = false;
        Tick(); Check(Waiting(boss), "wait for runtime rule lookup readiness");
        BossRulesRuntime.Ready = true;
        Tick(); Check(Encountered(boss), "valid player activates");

        Reset(); boss = Boss(mark: false); AddPlayer();
        Tick(); Check(boss.View.Data.GetInt(Key) == 0, "existing unknown boss not reclassified");
        boss.View.Data.Set(Key, 1);
        Tick(); Check(Encountered(boss), "late replicated marker is observed");
        Reset(); boss = Boss(); AddPlayer(); boss.View.Data.Altar = true;
        Tick(); Check(!InitialBossEncounter.IsWaitingForPlayer(boss.View.Data), "late altar marker takes precedence");
        Reset(); boss = Boss(); AddPlayer(); InitialBossEncounter.Untrack(boss);
        Tick(); Check(Waiting(boss), "destroyed/untracked character does not activate");
        Reset(); boss = Boss(); AddPlayer(); InitialBossEncounter.ResetRuntimeState();
        Tick(); Check(Waiting(boss), "shutdown clears observations");
        Reset(); boss = Boss();
        InitialBossEncounter.CheckApproaches(); AddPlayer();
        Time.time = 0.5f; InitialBossEncounter.CheckApproaches();
        Check(Waiting(boss), "checks are throttled");
        Time.time = 1; InitialBossEncounter.CheckApproaches();
        Check(Encountered(boss), "one-second check");
    }

    private static bool Calls(MethodDefinition method, string name) =>
        method.Body.Instructions.Any(i => i.Operand is MethodReference call && call.Name == name);
    private static void MetadataTests(string repo, string gameDll)
    {
        using AssemblyDefinition game = AssemblyDefinition.ReadAssembly(gameDll);
        TypeDefinition GameType(string name) => game.MainModule.Types.Single(t => t.Name == name);
        Check(GameType("Player").Methods.Any(m => m.Name == "GetAllPlayers" && m.IsPublic && m.IsStatic), "original Player.GetAllPlayers public");
        Check(GameType("Player").Methods.Any(m => m.Name == "GetPlayerID" && m.IsPublic), "original Player.GetPlayerID public");
        foreach (string name in new[] { "IsOwner", "GetInt", "Set" })
            Check(GameType("ZDO").Methods.Any(m => m.Name == name && m.IsPublic), "original public ZDO." + name);
        MethodDefinition location = GameType("ZoneSystem").Methods.Single(m => m.Name == "SpawnLocation" && m.Parameters.Count == 7);
        MethodDefinition room = GameType("DungeonGenerator").Methods.Single(m => m.Name == "PlaceRoom" && m.Parameters.Count == 5);
        Check(location.Parameters.Any(p => p.Name == "mode" && p.ParameterType.Name == "SpawnMode"), "location Harmony mode parameter");
        Check(room.Parameters.Any(p => p.Name == "mode" && p.ParameterType.Name == "SpawnMode"), "room Harmony mode parameter");
        Check(GameType("ZDOMan").Methods.Any(m => m.Name == "CreateNewZDO" && m.IsPrivate && m.Parameters.Count == 3 &&
            m.Parameters[2].Name == "prefabHashIn"), "original private ZDO creation target");

        using AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(Path.Combine(repo, "bin", "Debug", "BossRules.dll"));
        TypeDefinition ModType(string name) => mod.MainModule.Types.Single(t => t.Name == name);
        foreach (string name in new[] { "ZoneSystemSpawnLocationAltarPatch", "DungeonGeneratorPlaceQueenRoomAltarPatch" })
            Check(Calls(ModType(name).Methods.Single(m => m.Name == "Finalizer"), "RestoreGeneration"), name + " finalizer restores context");
        MethodDefinition creation = ModType("ZDOManCreateNewZdoDespawnPatch").Methods.Single(m => m.Name == "Postfix");
        Check(Calls(creation, "MarkCreatedBoss"), "creation hook marks initial boss");
        Check(Calls(ModType("CharacterAwakeBossRulesPatch").Methods.Single(m => m.Name == "Postfix"), "Track"), "load registers approach observation");
        Check(Calls(ModType("CharacterOnDestroyBossRulesPatch").Methods.Single(m => m.Name == "Prefix"), "Untrack"), "destroy unregisters");
        foreach (string method in new[] { "ProcessTrackedDespawnTarget", "ApplyDetachPersist" })
        {
            var instructions = ModType("DespawnRulesManager").Methods.Single(m => m.Name == method).Body.Instructions;
            int guard = instructions.ToList().FindIndex(i => i.Operand is MethodReference m && m.Name == "ShouldProtectInitialBoss");
            int timer = instructions.ToList().FindIndex(i => i.Operand is MethodReference m && m.Name == "StartDespawnCountdown");
            Check(guard >= 0 && guard < timer, method + " checks protection before timer");
        }
        Check(!mod.MainModule.Types.Any(t => t.Name.StartsWith("InitialBoss") && t.Name.EndsWith("DamagePatch")), "no damage hooks left");
        Check(mod.MainModule.Types.Any(t => t.Namespace == "ServerSync") &&
              !mod.MainModule.AssemblyReferences.Any(r => r.Name == "ServerSync" || r.Name == "YamlDotNet"), "final DLL merge");
    }
    private static int Main(string[] args)
    {
        PolicyTests();
        if (args.Length != 2) throw new ArgumentException("Arguments: repository root, original assembly_valheim.dll");
        MetadataTests(args[0], args[1]);
        Console.WriteLine($"PASS: {_checks} isolated policy and original/final DLL metadata checks. No game/network execution.");
        return 0;
    }
}
