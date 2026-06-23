# 1.0.0

- Promoted BossRules to a stable standalone release.
- Added feature-focused README documentation for Thunderstore and GitHub.
- Release builds now derive package version from the DLL assembly version and package the root README into the Thunderstore zip.
- The Thunderstore package now contains only `BossRules.dll`, `README.md`, `CHANGELOG.md`, `manifest.json`, and `icon.png`.
- Kept BossRules focused on boss altars, duplicate boss protection, boss despawn/refunds, boss tamed pressure, personalized boss stones, and Forsaken Power rules.

# 0.1.0

- Started the standalone BossRules split from DropNSpawn.
- Added a self-contained BepInEx project with ServerSync and YamlDotNet repacked into the output.
- Added server-synced `BossRules.altar.yml` parsing and default file generation.
- Added runtime application for boss altar `OfferingBowl` and boss `ItemStand` edits.
- Added altar hover info and same boss duplicate blocking for both altars and `CreatureSpawner`.
- Moved per-player boss stones and remote Forsaken Power selection from DropNSpawn into BossRules.
- Added `bossrules:inspect bossstone` and `bossrules:bossstone reset <exactPlayerName>` console commands.
- Added `forsakenPowers` rules for duration/cooldown, activation adrenaline gain, supported SE_Stats stat fields, Bonemass-style block stamina return, damage modifiers, and tailwind.
- Added ordered Forsaken Power tooltips so configured effects are grouped consistently.
- Added `messageForsakenPowerRotate` localization for the remote Forsaken Power selection HUD label.
- Added server-synced `BossRules.yml` for boss despawn rules and boss tamed pressure.
- Added automatic altar-offering refund markers for bosses spawned by `OfferingBowl`; spawner/world-spawned bosses near an altar do not refund offerings.
- Changed altar refunds to drop at the original `OfferingBowl` position, with boss despawn position as fallback when no altar point is stored.
- Removed fixed explicit refund lists from boss despawn rules.
- Removed the BossRules.yml `enabled` field; use `despawnRange: 0` to disable despawn for a prefab.
- Switched BossRules.yml despawn rules to compact `prefab, despawnRange, despawnDelay, refunds` rows; omitted range/delay use config defaults and omitted refunds defaults to true.
- Moved despawn and boss tamed pressure text to a `localization` block at the bottom of BossRules.yml.
- Restored the generated `bossTamedPressure` rule as an active block with all supported fields and inline comments.
- Simplified `bossTamedPressure` by fixing scan, damage, and message intervals internally and removing minimum-base-health scaling.
- Removed Expand World Data hard dependency and the altar `data`, `fields`, and `objects` payload fields.
- Removed altar `conditions`; BossRules altar entries now apply by `prefab` only.
- Added auto-generated `BossRules.altar.reference.yml` for loaded boss altar and boss stone prefabs.
- Removed mod-owner grouping from `BossRules.altar.reference.yml` generation to avoid optional loader reflection during dedicated server startup.
- Fixed altar-spawn refund capture for item stand altars without calling unsafe vanilla `ItemStand` attachment helpers.
- Restored authored-path remapping for altar ItemStands so path-based `supportedItems` edits also apply to relevant detached/runtime ItemStands.
- Deferred altar rule application until ObjectDB and ZNetScene are ready so startup-loaded `BossRules.altar.yml` applies without requiring a manual resave.
- Retried altar-summoned boss marker capture briefly after spawn and re-queued despawn tracking when the marker is applied, covering dedicated-server ZNetView/ZDO timing.
- Decoupled altar offering refund capture from `Enable Altar Rules`; despawn refunds now follow `Enable Despawn Rules`.
- Marked altar-summoned boss ZDOs before despawn observation is queued and refreshed refunds at despawn execution time to avoid stale empty refund state.
- Quoted `prefab` values containing `:` in `BossRules.altar.reference.yml` so EWD clone names such as `Dragonqueen:clone` can be copied directly into overrides.
- Restricted created-ZDO altar summon marking to exact boss prefab hashes so unrelated fresh ZDOs near the altar cannot consume the pending refund marker.
