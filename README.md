# BossRules

Standalone boss altar and boss-rule mod split from DropNSpawn.

This standalone slice owns server-synced boss altar edits, same boss duplicate blocking for altars and `CreatureSpawner`, personalized boss stones, remote Forsaken Power selection, boss despawn rules, and boss tamed pressure.

## Files

- `BepInEx/config/BossRules/BossRules.altar.yml`: altar override source.
- `BepInEx/config/BossRules/BossRules.altar.reference.yml`: generated altar reference target.
- `BepInEx/config/BossRules/BossRules.yml`: boss despawn and boss tamed pressure rules.

## Schema Direction

`BossRules.altar.yml` keeps only boss altar behavior fields:

- `prefab`
- `enabled`
- `offeringBowl`
- `itemStands`

Location identity, runestone pins, vegvisir effects, and general DropNSpawn spawn/drop domains are intentionally outside this mod.

## Boss Rules

`BossRules.yml` supports:

- `despawn:` with compact rows: `prefab, despawnRange, despawnDelay, refunds`.
- `bossTamedPressure:` as a global pressure rule for tamed creatures near bosses.
- `forsakenPowers:` to rebalance selected Forsaken Power status effects.
- `localization:` for despawn messages, boss tamed pressure messages, and the remote Forsaken Power rotate label.

Boss prefabs are auto-detected and tracked for despawn using the default despawn config. A compact despawn row can override range/delay; omitted or empty `despawnRange` and `despawnDelay` use the BepInEx config defaults. Set `despawnRange: 0` to disable despawn for that prefab.

The generated `BossRules.yml` exposes the supported `bossTamedPressure` fields with inline comments. Scan, damage tick, and message intervals are fixed internally; use the BepInEx config option to turn the feature off globally.

`Enable Same Boss Duplicate Block` blocks duplicate boss spawns from both `OfferingBowl` and `CreatureSpawner`. For `CreatureSpawner`, the respawn timer is kept fresh while the duplicate boss is alive, so `respawnMinutes` starts counting after the previous boss is gone instead of completing in the background.

Refund behavior:

- omit the fourth compact value for `true`
- use `true` to refund the actual altar offering only when the boss ZDO was marked as altar-summoned
- use `false` for no refund
- altar refunds are dropped at the original `OfferingBowl` position, falling back to the boss despawn position only when the stored altar position is missing

Localization despawn message templates support `{name}` and `{seconds}` placeholders. Empty despawn message values disable that message. `messageForsakenPowerRotate` changes the `[shortcut] Rotate` HUD label and falls back to `Rotate` when empty.

Spawner or SpawnSystem bosses near an altar do not receive altar refunds because only `OfferingBowl` boss spawns are marked.

## Forsaken Powers

`BossRules.yml` can rebalance selected Forsaken Power `SE_Stats` effects without becoming a general status-effect editor.

Supported fields:

- `defaults.durationSeconds`, `defaults.cooldownSeconds`
- `defaults.adrenalineGain`: guardian power activation adrenaline gain for every power, including mod-added powers; omit to keep vanilla 10. Negative values clamp to 0.
- `staminaCostPercent`: `run`, `jump`, `sneak`, `dodge`, `swim`, `block`, `attack`
- `blockStaminaReturn`: flat block stamina return, using the same SE_Stats field as vanilla Bonemass.
- `outgoingDamagePercent`: `Blunt`, `Slash`, `Pierce`, `Chop`, `Pickaxe`, `Fire`, `Frost`, `Lightning`, `Poison`, `Spirit`
- `incomingDamageModifiers`: individual `DamageType: DamageModifier` rows, such as `Blunt: SlightlyResistant`
- `regenPercent`: `health`, `stamina`, `eitr`
- `carryWeight`
- `armor.flat`, `armor.percent`
- `movement.speedPercent`, `movement.jumpHeightPercent`
- `skillLevels`
- `adrenalinePercent`, `staggerGaugePercent`
- `tailwind`

Percent values are written as readable percent numbers: `-50` means 50% lower cost, `100` means 100% more regen, and `10` means 10% more damage.

Configured Forsaken Power tooltips are reordered by BossRules so related effects stay grouped: damage, incoming damage modifiers, defense, movement, stamina, regen/resources, utility/skills, then duration.

## Boss Stones

BossRules owns the Start Temple and Deep North personalized boss stone behavior that was previously in DropNSpawn.

- `Personalized Boss Stones`: each player stores their own unlocked boss stone powers.
- `Remote Forsaken Power Selection`: lets players rotate through unlocked Forsaken Powers without returning to the Start Temple.
- `Rotate Forsaken Power Shortcut`: client-only shortcut used for remote rotation.

Console commands:

- `bossrules:inspect bossstone`: shows personalized boss stone state for the aimed target.
- `bossrules:bossstone reset <exactPlayerName>`: admin command that resets one player's personalized boss stone unlocks.
