# Valheim 1.0.7 compatibility patch

2026-09-10. Targets Windows x64 client Steam build **25185596** and dedicated
server build **25185644**. This compatibility work is packaged as mod version
**1.0.8**. Earlier game versions are not supported by this patch.

## Changes and preserved behavior

- `Libs/ServerSync.dll`: replace the unmodified baseline
  `166956302a294e224474b26f4c7d58409084ad3f48bd0af1feb7551f229c8f60`
  with fixed `valheim-1.0.7-r1`, SHA-256
  `b4dd786997f4e90d770f09ef3e9d64154754fe7e8edfb4841795751895b35846`.
  `Plugin.Awake -> CustomSyncedValue -> ServerSync` no longer reads the literal
  `ZRoutedRpc.Everybody` as field storage. The fixed source also uses the public
  admin check and preserves PeerInfo/list/time message ordering. The existing
  relative compile reference and ILRepack input remain; no separate runtime
  ServerSync plugin is installed. Provenance/license: `THIRD-PARTY-NOTICES.md`.
- `BossRules.csproj`: compile against original game assemblies, eliminating the
  stale publicized API view. All 13 referenced game DLLs match the preserved
  1.0.7 client originals. The installed game DLLs are not modified.
- `ZoneSystemSpawnLocationAltarPatch`: include the new seventh `bool cheated`
  parameter in the Harmony target. Leave the argument and game policy untouched;
  preserve `HarmonyAfter("expand_world_data")`, Prefix/Postfix state and Finalizer
  restoration. Other private Unity message targets use string names rather than
  inaccessible C# `nameof` expressions.
- `AltarLocationResolver` and `QueenDungeonAltarSupport`: use the game's
  `Vector2s` sector coordinates. `BossTamedPressureRuntime.CollectTargetsNearBoss`
  supplies `SimulationDistance(sectorRange, 0, classic: true)` to retain the old
  square sector scan and subsequent exact horizontal distance filter, independent
  of the player's graphics setting. Tick intervals and target policies remain.
- `BossStonePerPlayerRuntime.TryOverrideUpdateVisual`: call the integer-hash
  `ItemStand.SetVisualItem` overload; empty attachment uses **0**, not the hash
  of an empty string. Variant, quality and orientation arguments remain intact.
  Cached open delegates replace reflection argument arrays/boxing for display
  and orientation; attachment eligibility still calls the original `CanAttach`.
- `AltarRuntime.BuildOfferingRefundPayload -> TryGetAttachedItemName`: read the
  new integer attachment hash and resolve it to a prefab name. The existing
  name/count refund payload, ZDO keys, spawn markers, owner checks and item
  creation/destruction order remain. Unknown/empty attachments do not fabricate
  refunds. This fixes a separate refund omission hidden by the startup failure.
- Private localization, adrenaline, loaded-instance and dead-ZDO access uses
  cached Harmony field accessors/delegates. Localization still calls `AddWord`
  and clears its cache, and does not create the singleton early. Despawn lookup
  and ownership use public `ZNetScene.FindInstance` and `ZDOMan.GetSessionID`.
  The sender check still invokes the original `ZRoutedRpc.GetServerPeerID`.
  Guardian power Prefix/Postfix/Finalizer state restoration is unchanged.
- `Plugin.OnDestroy`: guard event unsubscription when `Awake` failed before
  creating all three synced values. This is failure-path hardening, separate
  from the game API changes; normal event/watcher cleanup is unchanged.

Config keys, YAML file names and formats, synchronization IDs/priorities,
locking/version policy, public plugin API, reset/sacrifice RPC IDs and duplicate
handling remain unchanged. No custom legacy migration is added. The BepInEx
manifest dependency remains the previously corrected `5.4.2350`.

## Validation

- **Build:** `dotnet build BossRules.csproj -c Debug -p:DeployToGame=true` passes
  with zero warnings/errors. The final merged DLL is copied to the local game's
  `BepInEx/plugins/BossRules.dll`; source/deployed SHA-256 match.
- **Dedicated reference build:** same Debug build using preserved server Managed
  originals, separate `bin/DebugServer` and `obj/DebugServer` directories, and
  `DeployToGame=false`; zero warnings/errors. This does not launch a server.
- **Static automated checks:** final merged DLL against both original Managed
  sets: 13,621 direct type/member reference occurrences, 51 Harmony patch methods
  and 32 known reflection targets per target pass. These counts include merged
  dependencies. The dynamic `ZDOMan.CreateNewZDO(ZDOID, Vector3, int)` target and
  `prefabHashIn` injection were additionally checked in source. Public/protected
  API matches the previous Release DLL. No obsolete literal-field storage access
  remains. The checker self-tests pass (9); these are not gameplay tests.
- **Actual execution:** the installed client executable was started headlessly
  with the installed mod set. BossRules loaded all three YAML streams and startup
  completed without the reported exception or another BossRules exception.
  Headless runs report graphics/video shader errors; rendering is not validated.
  No world or character was selected. The test process was stopped after startup;
  graceful Unity destruction was not validated.

## Scope and remaining checks

All 35 compiled C# files, project references, merged dependencies, translations,
manifest and relevant original game methods were included in compile/contract
checks. Manual semantic review focused on the changed paths above, networking
guards, reflection and lifecycle boundaries; this is not a complete gameplay
audit. Generated `bin`/`obj`, old ZIPs, external library internals beyond the
fixed ServerSync contract, native code and full prefab/asset contents are outside
the manual review. Static checking does not prove dynamic reflection safety.

DataForge ownership API/events, Expand World Data location context and optional
location-owner providers retain their discovery, retry and absence handling.
There is no hard Jotunn dependency. Their behavior with every external mod
version is unverified. Multiple embedded ServerSync copies, Steam/PlayFab
crossplay, server authority, graphical UI and frame-time/allocation measurements
need real gameplay verification.

Before publishing, verify in a disposable test world:

1. Client/host startup, language switching, settings reload and graceful exit;
   guardian power activation success/failure restores adrenaline state, and HUD
   selection/rotation stays correct.
2. Existing/new locations and Queen rooms, including Expand World Data when used;
   spawn context is restored after success and exceptions.
3. Per-player trophy offering, visibility for two players, reset/retry/reconnect,
   unauthorized requests and duplicate RPCs: inventory changes only once.
4. Bowl and item-stand offerings, abandoned boss despawn and persisted reload:
   exact offering counts are returned once, with no lost or duplicated items.
5. Remote client to host/dedicated server, admin/non-admin config edits, version
   mismatch, reconnect and large YAML synchronization, including crossplay and
   the intended optional mod combination. Pressure range/ownership must match
   across sector boundaries and simulation-distance settings.

Detailed evidence and reproducible checker source are retained locally under
`C:/Users/blizz/.codex/references/valheim/comparisons/0.221.12--1.0.7-windows-x64/bossrules-verification`.
The original before/after snapshot identifiers and semantic comparisons are
recorded in the adjacent `BossRules.md`.
