# Altar loading verification

2026-10-16. Baseline: `main 22cca43271738a6fff35fdd53dc1171131e164b9` (clean).
The unchanged Debug build/deployment passed before edits. The implementation
stage did not change versions or dependencies or create a Release package.

The supplied orgetorix profile uses Valheim 1.0.20, BossRules 1.1.6,
MWL 5.2.2 and Jotunn 2.30.2. The user confirmed the 11:47 and 11:50 profiles
differed only by enabling BossRules: lobby-to-world was 37.020s vs 109.435s.
This is baseline evidence, not a measurement of the patched build.

Original 1.0.20 client/server material from the global Valheim index and selective
installed MWL/Jotunn decompilation confirm that synchronous SoftReference.Load
can trigger Jotunn prefab cloning and recursive FixReferences. Automatic reference
generation now excludes MWL manifest IDs before loader access. Runtime overrides
are not excluded. Existing full references are preserved; the exact empty startup
placeholder can become a marked partial reference. An unreadable manifest defers
automatic generation for that world instead of forcing all assets to load.
`bossrules:reference altar` explicitly scans all locations on the host/server.

## Managed checks

The fixture loads the merged plugin and executes compiled production control flow
using MonoMod, substituting native scene/clock and scan/write boundaries. The real
game manifest parser and production file eligibility check are used. It does not
load bundles, launch Unity, modify a profile, or render/spawn objects. Expected
malformed-manifest diagnostics disable the native Unity logger in this process.

293 checks passed on the original 1.0.20 client and dedicated-server Mono/DLLs:

- All 264 installed MWL IDs are rejected before loader validity access; unrelated
  locations reach normal validation.
- No MWL retains full automatic generation; completed scans do not repeat.
- New files and the actual startup placeholder permit partial generation;
  existing full files remain untouched; marked partial files permit refresh.
- Missing/broken manifests defer without full fallback or repeated scans.
- Explicit full export bypasses exclusions; runtime server and sync authority
  are required for automatic and explicit generation.
- Fresh unconfigured roots skip bowl/stand traversal; configured MWL roots still
  reach the normal path. Once an override begins, removed rules, disabled rules
  and corrected prefab names still enter the existing restoration path.
- Actual offering-count assignment records the cleanup obligation before Unity
  component-state access; stand overrides record it as well. Reload retains the
  obligation and shutdown clears it after existing cleanup.

The runtime optimization uses one conservative flag, not a root cache. After any
override begins, all later roots keep the old traversal behavior until shutdown.
Location registration, name correction, Queen context and refund/RPC hooks are
unchanged. An initially unconfigured root now captures its baseline at its first
override, so later removal restores the values immediately preceding that override
(including any other mod's changes in the meantime), rather than an earlier eager
snapshot. The implementation-stage source and deployed Debug DLL have SHA-256
`F0CCBD46FB58901E12B713E32379807202122A91C00B663A35C691BA4A70BBB2`.

The runtime fixture substitutes scene traversal with empty arrays and observes
restore/mark calls; it proves control-flow preservation, not actual Unity field
restoration, nested-object lifecycle or paid/free summon behavior. The small
offering-count mutation runs until the component-state boundary; no native state
component is created by the fixture.

```powershell
dotnet build BossRules.sln -c Debug -p:DeployToGame=true
dotnet build Verification/AltarLoadingMono/AltarLoading.Mono.csproj -c Debug
$env:BOSSRULES_TEST_MWL_MANIFEST = '<MWL plugin directory>/assetBundleManifest_full'
& '<Python executable>' Verification/BootstrapScan/run_mono.py `
  'C:/Program Files (x86)/Steam/steamapps/common/Valheim' `
  bin/Debug/BossRules.dll `
  'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/core' `
  Verification/AltarLoadingMono/bin/Debug/net48/AltarLoading.Mono.exe
```

For the server check, use the preserved
`dedicated-server-b25909431-windows-x64-20261015T103728Z-depot-restored/original`
as the first runner argument. No publicized assemblies are used.

## Requested 1.1.7 release

2026-10-16. Release preparation started from clean `main 6b26860`, including
implementation commits `a161879` and `6b26860`. Debug with `DeployToGame=true`
and the normal Release build both passed with zero warnings/errors.

- Final merged Release DLL passed the same 293 checks separately on original
  1.0.20 client and dedicated-server Mono/DLLs. These remain isolated checks,
  not Unity world or performance tests.
- Assembly version is `1.1.7.0`; plugin/file/manifest version is `1.1.7`.
  ServerSync and YamlDotNet are merged with no separate assembly references.
- All eight ZIP entries match their source files, including the final Release
  DLL and changelog. The BepInEx package dependency remains `5.4.2351`.
- Debug DLL and the Steam game's deployed plugin match SHA-256
  `4A91F68626F77B15EC4429740C6199F0D43E3DDE501C61FF9F4D13004CCFB2A6`.
- Final Release DLL SHA-256:
  `3F09F84596AA31012474493B482209B24F1A4EFBC167616014EA996234FCFDC8`.
- `Thunderstore/BossRules_v1.1.7.zip` SHA-256:
  `373A51B1915F19D0C4D2B4AB0F39178FD929B8F16C270007D54BC8ADD3F7435F`.

## Remaining game checks

- Repeat the same world/character/connection loading comparison with this DLL,
  and record new-zone frame times separately. No speedup is yet measured.
- Check partial/full output contents, Queen nested-room capture and multi-bowl
  paths in Unity. Explicit full export can still pause the host/server.
- Verify existing MWL overrides, live configuration changes and removal, Queen
  and Dragonqueen altars, paid/free summons and refunds on host/dedicated + clients.
- Normal MWL zone loading/cloning costs remain. This does not audit all mods or
  certify all BossRules features against Valheim 1.0.20.
