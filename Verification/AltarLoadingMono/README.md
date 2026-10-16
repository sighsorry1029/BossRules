# Altar loading verification

2026-10-16. Baseline: `main 22cca43271738a6fff35fdd53dc1171131e164b9` (clean).
The unchanged Debug build/deployment passed before edits. No Release package,
version or dependency changes are part of this work.

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

283 checks passed on the original 1.0.20 client and dedicated-server Mono/DLLs:

- All 264 installed MWL IDs are rejected before loader validity access; unrelated
  locations reach normal validation.
- No MWL retains full automatic generation; completed scans do not repeat.
- New files and the actual startup placeholder permit partial generation;
  existing full files remain untouched; marked partial files permit refresh.
- Missing/broken manifests defer without full fallback or repeated scans.
- Explicit full export bypasses exclusions; runtime server and sync authority
  are required for automatic and explicit generation.

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

## Remaining game checks

- Repeat the same world/character/connection loading comparison with this DLL,
  and record new-zone frame times separately. No speedup is yet measured.
- Check partial/full output contents, Queen nested-room capture and multi-bowl
  paths in Unity. Explicit full export can still pause the host/server.
- Verify existing MWL overrides, live configuration changes and removal, Queen
  and Dragonqueen altars, paid/free summons and refunds on host/dedicated + clients.
- Normal MWL zone loading/cloning costs remain. This does not audit all mods or
  certify all BossRules features against Valheim 1.0.20.
