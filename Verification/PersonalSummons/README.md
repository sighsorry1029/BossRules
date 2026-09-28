# Personal progression summons verification

2026-09-29. BossRules baseline `86be3fe17b183b3c6cb9b01073e3e13089830a90`;
YouAreNotWorthy baseline `71bc580682d32d7143bcd700a42246811b93bd4c`.
Both main branches were clean before this change. The initial implementation
was verified using Debug builds without a release or commit. A subsequent
release request packages this work as BossRules 1.1.2 and YouAreNotWorthy 1.0.8.
The game support scope and embedded libraries are unchanged.
YNW's BepInEx manifest requirement is corrected to the prescribed 5.4.2351.

## Scope and trust boundary

The production change adds an optional cached YNW API bridge and a bounded,
short-lived request protocol. Queen completion is `defeated_queen`; Frozen King
requires personal `LastBossGate_Open` and absence of final `defeated_frozenking_p3`.
The outer gate remains on the native paid/key-grant path. Inner bowls with an
item reward, item stands, a global-key grant, or another boss are excluded.

The actual object owner resolves the bowl and supplies its configured offering
item and position. The server verifies its root prefab/owner, the authenticated
requesting character and distance, then queries YNW's character snapshot and
item requirement. Server progress does not mean owner authority, and neither is
inferred from a caller-supplied player name or boolean key result. As with native
Valheim, the object owner remains trusted to execute the actual object behavior;
this is not anti-cheat against a modified owner client. YNW's personal-key
snapshots are also client-owned, as documented by its API.

The owner consumes a matching pending request once, rechecks ownership,
character/death/distance, bowl/item identity, native queue and existing enabled
spawn restrictions, and invokes native spawn under an explicit free context.
Replies expire after ten seconds. No ownership acquisition, automatic retries,
inventory writes or key grants are introduced. Native delayed spawning after
approval retains its existing lifecycle.

## Performed checks

- Baseline and final Debug builds of both mods: zero warnings/errors; final
  ILRepack outputs deployed by their MSBuild targets. A BossRules incremental
  merge once crashed inside the native PDB writer; a full Debug rebuild and
  subsequent final build passed without changing build settings.
- BossRules source-linked tests: **84** policy, simulated owner/server protocol,
  optional bridge and original/final DLL metadata checks. Include Queen and
  Frozen King, local host, dedicated without a local character/bowl instance,
  final completion, outer gate exclusion, unavailable snapshots, item gating,
  wrong root/relay, duplicate/late/forged replies, owner/session changes,
  disconnect, death, vertical separation, queued boss/cooldown and live item
  changes. The test game/network are doubles, not a running Unity scene.
- **9 checks per role** in the actual game's embedded Mono, using original
  Valheim 1.0.16 client build 25527674 and dedicated build 25527701 assemblies:
  private field/method accessors initialize, the cached native spawn delegate
  still enters Harmony, and both final merged plugins bind the real enum/out API.
  Copied production refund-builder/consumer IL executes with only component
  lookup replaced: explicit empty payload stays empty and paid prepared payload
  survives unchanged. Unity spawn is suppressed in the delegate probe.
- Existing initial-encounter suite: **52** policy/metadata checks pass.
- YNW runtime verifier passes against client and dedicated originals, including
  the added API enum/signature/authentication-order/result cases, existing tier
  inference, patch targets, transformed IL and managed JIT checks. Its new item
  result tests substitute the item resolver boundary; they do not initialize
  the whole plugin outside Unity. Original-reference verifier passes **271**
  type/member references with no literal-field load/store.
- Before the release version bump, deployed Debug DLL SHA-256 matched each build output:
  - BossRules: `39EF9456E4405EDCA3BE6E869BEDC5B4605B270D9E21685CFF358F5256A71F3F`
  - YouAreNotWorthy: `FADB521C648FC5141050ED5B7FF062CB1182DB150B150DA8B6EC004D41B18978`

Original DLLs/resources came from the prepared global 1.0.16 snapshots; none
were publicized, recopied over the game originals or broadly re-extracted.
Generated outputs, merged library internals, unrelated mods and other features
were not re-audited. No frame-time improvement is claimed. Warmed key calls use
cached delegates; scene/bowl search occurs only on a summon request, and pending
cleanup uses a reused list at most once per second.

## Release validation

BossRules 1.1.2 and YouAreNotWorthy 1.0.8 were built with each project's normal
`dotnet build <project> -c Release` packaging target, with zero warnings/errors.
Assembly, BepInPlugin and manifest versions match. ServerSync and YamlDotNet are
merged, and YNW's public API v2 survives internalization. All eight BossRules ZIP
entries and all six YNW ZIP entries match their source files byte for byte;
only the intended plugin DLL is packaged, with no verifier/fake API DLLs.

The nine game-Mono checks passed for each original client/server role using
**both Release plugin DLLs**. YNW's managed verifier and 271-reference check also
passed for its Release DLL. Debug builds were refreshed/deployed after the
version bumps and their source/destination hashes matched. These remain managed
checks; no actual world or multiplayer session was run.

| Artifact | SHA-256 |
| --- | --- |
| `bin/Release/BossRules.dll` | `FC17AEDEF55FAD46F21ADA7F60A5D99646D5F5A6C9B60F9F11AC10512CA740DD` |
| `Thunderstore/BossRules_v1.1.2.zip` | `B2E95EA72E130FEAF4ED954EADC9699D5E0CF5125051CEE7C9D33775450B09B0` |
| YNW `bin/Release/YouAreNotWorthy.dll` | `58893AE3BAFFD990980952B8483E2E1DA13FF43CE9E4CE7526F709C592B2EBD1` |
| YNW `Thunderstore/YouAreNotWorthy_v1.0.8.zip` | `11B98BF0A02205908C6858FF3BCC33DE68DB12EE93416D4676CF8883AA95E4F2` |

## Reproduce

Build both projects with `dotnet build <project> -c Debug -p:DeployToGame=true`.
From BossRules:

```powershell
dotnet build Verification/PersonalSummons/PersonalSummons.Tests.csproj -c Debug
dotnet Verification/PersonalSummons/bin/Debug/net8.0/YouAreNotWorthy.dll $PWD.Path 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/valheim_Data/Managed/assembly_valheim.dll'
dotnet build Verification/PersonalSummonsMono/PersonalSummons.Mono.csproj -c Debug
python Verification/BootstrapScan/run_mono.py 'C:/Program Files (x86)/Steam/steamapps/common/Valheim' ./bin/Debug/BossRules.dll 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/core' ./Verification/PersonalSummonsMono/bin/Debug/net48/PersonalSummons.Mono.exe
```

Use an actual Python executable, not the Windows Store alias. The standalone
protocol test deliberately names its assembly `YouAreNotWorthy` to bind its
fake API; do not deploy that test DLL. The Mono fixture loads the deployed real
YNW plugin and checks API version 2. Set `BOSSRULES_TEST_YNW_DLL` to an explicit
YNW DLL path to check another build (for Release validation, set it to YNW's
`bin/Release/YouAreNotWorthy.dll` and pass BossRules' Release DLL to the runner).
To check server originals, replace the
first runner argument with the preserved dedicated-server `original` directory.

## Remaining game checks

No graphical game, host or dedicated multiplayer session was launched.
With both updated DLLs on all participants, verify in a disposable world:

1. Uncompleted Queen character with zero trophies uses E successfully; completed
   character needs the configured offering. Missing item-tier progression blocks E.
2. New Frozen King character pays only the exterior gate's three HatefulBlood,
   then uses E inside with zero remaining blood. First-phase death alone does not
   end eligibility; final p3 completion does. Nearby key-grant behavior stays YNW's.
3. Two clients press E together; only the native queue accepts a summon. Repeat
   with a paid request, an existing boss, and configured cooldowns.
4. Move, die, disconnect or transfer owner while authorization is pending; then
   repeat during the native 12-second spawn delay. Check the game object's normal
   destruction/streaming lifecycle and retry feedback.
5. Despawn a free summon before/after save/reload, owner change and config toggles:
   no refund. Follow with a paid summon and confirm its usual refund count once.
6. Confirm live YAML/item changes, feature Off, absent/old YNW, host and dedicated
   paths. Install BossRules 1.1.2 and YouAreNotWorthy 1.0.8 on all participants;
   the unversioned implementation test builds are not the release pair.
