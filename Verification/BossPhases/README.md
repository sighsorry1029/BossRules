# Boss phase inheritance verification

2026-09-29. Baseline main `b9d74bc175ae6fa3d6d577235675cffea7c36630`, initially clean.
Implementation was verified at 1.1.2 before the separately requested 1.1.3 release.
The release includes this feature, the version bump and configuration instructions;
no dependency or other mod changes are included.

## Scope and original contracts

The existing `rules-yaml` ServerSync value and file watcher carry `bossPhases`.
`BossRuleConfiguration` validates whole ordered lists before publishing; omitted/null
uses the two shipped chains and explicit `[]` disables them. Existing files are preserved.
Phase behavior is together in `BossPhaseRuntime.cs`; original refund storage stays in
`AltarRuntime.Refunds.cs`. Despawn lookup includes configured non-boss characters but
only altar-marked instances qualify without an explicit despawn rule.

Prepared original Valheim 1.0.16 sources were reused:

- Client build 25527674, snapshot `client-b25527674-windows-x64-20260925T211211Z`.
- Dedicated build 25527701, snapshot
  `dedicated-server-b25527701-windows-x64-20260925T211211Z-depot-restored`.
- Original `Character.OnDeath` creates `m_deathEffects` only on the owner.
  `Ragdoll.DestroyNow` is **private** and creates `m_removeEffect` without a parent
  argument before destruction. Both use public `EffectList.Create`, returning the
  actual created GameObjects. Effect fields and `ZNetView.m_persistent` are public.
- Original `ZDO.GetHashZDOID`, paired `Set`/`GetZDOID` and string/bool fields are used.
  No publicizer, private direct calls or runtime accessibility suppression was added.
- MonsterLabZ_cjayfix 3.0.14 supplied DLL SHA-256:
  `845EEDB89F24FCCD660E8E7E798DA579BB83ABE9E083FF7C968168CCE3A69B09`.
  Previously inspected target assets show Queen1/2 non-boss characters, Queen3 boss,
  and a persistent five-second Ragdoll between Queen2 and Queen3. The mod DLL is not
  executed, referenced by the build or included in the package.

Prefix context is restored by Harmony finalizers on success and exception. A default
scope from a skipped prefix never overwrites an enclosing transition. The shared
effect postfix does only a null/context check outside transitions. It never prevents
native death/spawning/destruction. Only one-hop direct or one persistent Ragdoll path
is supported; branches/unknown spawners are rejected, not guessed. Current effect lists
are inspected per transition with no asset loading, per-frame search or route cache.
Performance is not benchmarked.

Transfer runs synchronously on the current source/target owner, preserves the exact
paid or explicitly empty free payload, and stores one successor ZDOID on the source.
This prevents normal repeated callbacks from giving multiple successors a refund.
It is not a distributed transaction or protection against a modified malicious owner.
No RPC, permission policy, inventory mutation, global key or duplicate-summon policy
was added. Natural/initial boss provenance remains separate.

## Automated checks

- Baseline/final Debug build, final ILRepack and normal `DeployToGame=true` copy.
- 95 source-linked tests execute production configuration, route/context handling,
  refund capture/transfer/resolution and despawn rule selection. Cover both chains
  without knowing the bridge name, empty/free vs natural records, exact amount/point,
  owner checks, dictionary-backed reload, consumed-source replay, nested callbacks,
  unrelated effects, branching/duplicate/nonpersistent targets, runtime effect edits,
  live edge removal and explicit per-phase rules. Game doubles replace Unity/network.
- 18 checks per original role in the game's embedded Mono bind all three production
  Harmony patches and execute merged-plugin transfer code with original ZDO storage.
  Actual Harmony cancellation probes also check skipped-prefix finalizers on both
  native death/removal methods, without running those methods' original bodies.
  Test-only animation hash substitution permits Character/Player static initialization;
  world-dirty notification is suppressed because no Unity world is running. An initial
  harness run failed at missing native Animator initialization before this substitution.
  The successful runs still print native icall lookup notices for unused animation,
  rotation, quaternion and component-lookup code. These are isolated-host limitations, not a game-world
  result or evidence that the installed Mono libraries are mismatched.
- Existing personal-summon suite: 84 checks. Initial encounter suite: 52 checks.

Source-linked metadata tests check original access levels and final merged patch/guard
integration. They do not replace actual Unity execution or actual save/network IO.

## 1.1.3 release validation

On 2026-09-29, after the separately requested patch-version bump:

- Debug with `DeployToGame=true` and normal Release builds passed with zero warnings
  and zero errors. ILRepack completed in both configurations.
- Debug output and the installed plugin matched SHA-256
  `1AD81450312C7B4C6022DB358A804D839AC9B1F4AF9995857BCC588C1FBB2F02`.
- All 95 source-linked checks passed again. The final Release DLL passed all 18 Mono
  checks against each original role, with the same isolated-host limitations above.
- Release assembly, file and BepInPlugin versions match 1.1.3. ServerSync and YamlDotNet
  are merged; neither those libraries nor MonsterLabZ are external assembly references.
- `Thunderstore/BossRules_v1.1.3.zip` passed archive integrity and exact eight-file
  inventory checks. Every entry matches its source bytes, including the final DLL,
  manifest, README and changelog with manual top-level `bossPhases` instructions.
  BepInEx remains `denikson-BepInExPack_Valheim-5.4.2351`.
- Release DLL SHA-256:
  `38DB3314E550DEB88E81E84348B1C4D5881D03F4268283C2ED9D3AC421B75AF4`.
- Release ZIP SHA-256:
  `71C56F4B3483ED5B8E2756121F99E4153B00FC442D5755F6F10C25704BE2FC2A`.

The release did not add actual game/multiplayer validation. Upload queues and site
publication were outside this build request and were not inspected.

## Reproduce

From the repository, build the mod normally:

```powershell
dotnet build BossRules.sln -c Debug -p:DeployToGame=true
dotnet run --project Verification/BossPhases/BossPhases.Tests.csproj -c Debug -- $PWD.Path 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/valheim_Data/Managed/assembly_valheim.dll'
dotnet build Verification/BossPhasesMono/BossPhases.Mono.csproj -c Debug
& 'C:/Users/blizz/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' Verification/BootstrapScan/run_mono.py 'C:/Program Files (x86)/Steam/steamapps/common/Valheim' ./bin/Debug/BossRules.dll 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/core' ./Verification/BossPhasesMono/bin/Debug/net48/BossPhases.Mono.exe
```

For dedicated originals, replace the first Python runner argument with the preserved
dedicated snapshot's `original` directory. The runner does not start Valheim.

## Still requires a real game

No Unity world, local host or dedicated multiplayer session was launched. On a disposable
world with matching updated DLLs on every participant:

1. Pay once and abandon each of Frozen King p1/p2/p3 and ML Queen1/2/3: one original
   offering at the original altar. Complete the final kill: no despawn refund.
2. Repeat with YNW free summons and natural initial bosses: no refund.
3. Transfer owner, disconnect and save/reload during p2 and during the ML bridge.
   Check resumed Queen3 and its original offering, including an unloaded server region.
4. Exercise simultaneous fights, death vs countdown timing and repeated callbacks.
   There must be no cross-assignment, double refund or additional item consumption.
5. Edit YAML/effect data live, disable a phase's refunds/range, and test an unsupported
   custom spawner. Check diagnostics, existing despawn behavior and exception handling.

Generated outputs, unchanged embedded library internals, arbitrary custom spawners,
other mods and unrelated features were not re-audited. Existing later phases with no
record are not migrated or assigned an estimated offering.
