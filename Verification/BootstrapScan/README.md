# Incremental despawn bootstrap regression checks

2026-09-28, based on `main` at `ee1252aa384b7f172e8cfa264b4b4adc9b60a15c`.

## Change and scope

`Plugin.Update -> DespawnRulesManager.ExecuteServerTick` previously exhausted
`ZDOMan.GetAllZDOsWithPrefabIterative` for every bootstrap prefab in one Update.
The lookup covers auto-detected bosses **and configured despawn targets**. It is
separate from `BossRulesManager`'s loaded-boss duplicate summon prevention.

`DespawnRulesManager.Observations.cs` now retains the immutable runtime prefab
list and the game's sector cursor. One server Update performs at most one game
query, queues that slice's results, then applies them through the existing
observation pipeline. The obsolete full-prefab scanning helper is removed.
Existing creation/load observations, detach persistence and scheduled countdown
checks continue while the scan is incomplete. Config pruning retains its
one-second interval.

The cursor/list are discarded on lookup-version changes, explicit YAML dirty
notifications, Off/On, runtime reset, leaving the server role, or replacement of
the ZDO manager. Manager identity matters because a new world can have the same
prefab names and therefore the same game-data signature. The scratch ZDO list is
cleared in `finally`, including query failures. On failure the current slice's
cursor is restored for retry, so partially collected results are not skipped;
exceptions are not swallowed.

The intended tradeoff is lower concentrated bootstrap work, with recovery of
some saved/unloaded targets spread over subsequent Updates. Total scan work is
similar; no frame-time or FPS improvement has been measured. The original 1.0.16
query yields after 401 occupied sector lists and repeats its boundary sector.
Its final call also scans sector zero and portal lists. A single call therefore
does **not** provide a fixed millisecond or object-count budget. Existing ID-based
observation merging and tracked-state reuse handle repeated results.

No new production file, config, dependency, Harmony target, private game access,
RPC, storage format, refund rule, initial-boss protection rule or ownership rule
is introduced. Version remains 1.1.0. No Release ZIP or push was requested.

## Reproduction

From the repository root, using the restored local BepInEx installation:

```powershell
dotnet build BossRules.sln -c Debug -p:DeployToGame=true
dotnet build Verification/BootstrapScan/BootstrapScan.Tests.csproj -c Debug
python Verification/BootstrapScan/run_mono.py 'C:/Program Files (x86)/Steam/steamapps/common/Valheim' ./bin/Debug/BossRules.dll 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/core'
```

The Python runner embeds the selected game's Mono; it does not launch Unity or
load a world. Pass a preserved dedicated-server root as the first argument to
run the same checks against server originals. The harness resolves original
Managed assemblies and the installed BepInEx core. It never publicizes or edits
either input. Production state/cursor/intake/tick method bodies are copied from
the final merged DLL with MonoMod; they are not reimplemented in the fixture.
The real game's public iterative query runs against a synthetic sector table.

Substituted boundaries: scene singletons, Unity null equality, server/config
state, clocks, ZDO lookup/death state, scene-dependent rule resolution and the
scheduled-despawn/detach/prune consumers. The existing production ID merge,
tracked-state creation and scheduling methods execute. Thus the checks prove
incremental discovery and continued dispatch, **not** actual refund delivery,
player proximity, scene activation, networking or world serialization.

## Completed verification

- Baseline and final Debug builds: zero warnings/errors; final merged plugin
  copied by the existing MSBuild deployment target to
  `C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/plugins/BossRules.dll`.
  Build/deployed SHA-256 both
  `a7fd5dcbd5c73c305f773b44d98d9dfdda97631324a4b3d403fad08292848a9b`.
- Bootstrap fixture: **51 assertions per role**, using original Windows x64
  Valheim 1.0.16 client build 25527674 and dedicated build 25527701. Covers
  incomplete slices, cursor continuation, immediate results, configured non-boss
  targets, native repeated-sector deduplication, unchanged existing countdown
  state, live creation while pending, loaded/created/bootstrap source priority,
  scheduled/detach dispatch, pruning cadence, completion without rescanning,
  config/catalog/session resets, empty/unavailable worlds, missing/dead results
  and exception buffer cleanup.
- Existing `InitialBossEncounter` suite: **52** policy/original/final DLL metadata
  checks pass before and after the change, including merge presence, original
  private creation target and initial encounter protection before countdowns.
- Existing global BossRules metadata verifier: **13,781** direct reference
  occurrences, **52** decoded Harmony patch methods and **32** known reflection
  targets pass against each role's original Managed set. Public/protected API
  matches the baseline exactly. Reports are local generated files under
  `Verification/BootstrapScan/bin/compatibility.*`, not package inputs.
- Canonical IL instruction comparison with the baseline merged DLL: only
  `RunPendingBootstrapScan`, `ExecuteServerTick`, `MarkBootstrapScanDirty`,
  `ObserveDespawnLookupVersion`, and `ResetRuntimeState` changed; the old helper
  was removed. All other **3,728** method bodies match, including countdown,
  initial encounter, refund execution, owner acquisition, destruction, patch
  methods and merged dependency bodies. This is evidence of a limited change,
  not a proof of identical timing or whole-game behavior.

The metadata verifier retains existing limitations: dynamic creation targeting,
generic reflection candidates, reflective readonly writes in ServerSync/config
metadata and a protected receiver in YamlDotNet. The creation target was also
checked by the existing suite/source review. Other unchanged library internals,
native code, full resource data and optional mod combinations were not newly
audited. No new game snapshots/extractions or support-version changes were made.

The first desktop .NET Framework fixture launch was blocked by the downloaded
MonoMod DLL's zone marking; library files were left intact. The documented game
Mono runner then passed on both roles. This was a harness-host issue, not a mod
build or game-world failure.

## Remaining actual game checks

No host, dedicated server process, graphical client world or multiplayer
session was launched for this patch. Before release, use a disposable world:

1. Load a large world with saved bosses and explicit non-boss despawn rules.
   Confirm recovery completes and compare frame times during the initial scan.
2. While recovery is pending, summon/load/kill bosses and leave/re-enter their
   range. Existing timers and messages should continue; a repeated discovery
   must not restart a countdown or produce duplicate refunds.
3. Reload YAML, toggle despawn Off/On, quit and enter a different world with the
   same mods. Check recovery restarts and no old cursor skips saved targets.
4. On host and dedicated server with a remote client, verify initial unvisited
   Queen/FrozenKing protection, owner changes, persisted countdowns after
   reconnect, and exact-once offering refunds on actual despawn.
