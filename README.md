# BossRules

BossRules is a standalone Valheim server mod for boss progression, boss altars, boss stones, Forsaken Powers, and boss cleanup rules.

It was split from DropNSpawn so boss behavior can be managed without also taking ownership of general creature drops, object loot, spawners, or world spawn tables.

## Highlights

- Edit boss altars without rebuilding locations by hand.
- Block duplicate boss summons from both altars and `CreatureSpawner`.
- Despawn abandoned bosses and optionally refund real altar offerings.
- Pressure tamed creatures near bosses so boss arenas stay dangerous.
- Give each player their own boss stone unlock state.
- Let players rotate unlocked Forsaken Powers remotely.
- Rebalance Forsaken Power duration, cooldown, costs, regen, damage, armor, movement, skills, and tooltips.
- Sync server YAML to clients through ServerSync.
- Reload YAML while the game is running.

## Why Use BossRules

BossRules focuses on the awkward parts of boss management that usually live across several systems:

- Altars can be edited by prefab name with generated reference data.
- Boss duplicate protection keeps respawn timers honest instead of letting cooldowns finish in the background.
- Altar refunds are tied to actual altar-summoned bosses, not nearby world spawns.
- Boss stones and Forsaken Powers can be customized without turning the mod into a general status-effect editor.
- Dedicated servers remain the source of truth for synced rule files.

## Generated Files

BossRules creates its files under:

```text
BepInEx/config/BossRules/
```

Files:

- `BossRules.altar.yml`: boss altar and boss item stand overrides.
- `BossRules.altar.reference.yml`: generated reference for loaded boss altar and boss stone prefabs.
- `BossRules.yml`: boss despawn, boss tamed pressure, Forsaken Power, and localization rules.
- `sighsorry.BossRules.cfg`: synced BepInEx feature toggles and defaults.

Server admins should edit the YAML on the server or host. Synced YAML is pushed to clients automatically.

## Boss Altars

`BossRules.altar.yml` supports compact altar entries:

- `prefab`
- `enabled`
- `offeringBowl`
- `itemStands`

Use the generated `BossRules.altar.reference.yml` to find real prefab names, item stand paths, and current altar values. Copy only the rows you want to override into `BossRules.altar.yml`.

BossRules intentionally does not own general location editing, object drops, runestone pins, or vegvisir rewards.

## Boss Rules

`BossRules.yml` controls runtime boss behavior:

- `despawn`: compact rows in `prefab, despawnRange, despawnDelay, refunds` format.
- `bossTamedPressure`: a global rule for tamed creatures near bosses.
- `forsakenPowers`: selected Forsaken Power stat edits and tooltip ordering.
- `localization`: boss despawn, tame pressure, and remote power selection messages.

Despawn rows use BepInEx defaults when range or delay is omitted. Set `despawnRange` to `0` to disable despawn for one boss prefab.

Refund values:

- omit the fourth value for `true`
- `true`: refund the actual altar offering when the boss was marked as altar-summoned
- `false`: no refund

Refunds drop at the original `OfferingBowl` position when possible. Bosses from `CreatureSpawner`, `SpawnSystem`, or other world sources do not receive altar refunds just because they died near an altar.

## Forsaken Powers

BossRules can rebalance selected `SE_Stats` fields for Forsaken Powers:

- duration and cooldown
- guardian power adrenaline gain
- stamina costs
- block stamina return
- outgoing damage
- incoming damage modifiers
- health, stamina, and eitr regen
- carry weight
- flat and percent armor
- movement speed and jump height
- skill levels
- adrenaline and stagger gauge
- tailwind

Percent values are written as readable percent numbers. For example, `-50` means 50% lower cost and `100` means 100% more regen.

Configured tooltips are reordered so related effects stay grouped in game: damage, resistance, defense, movement, resources, utility, skills, and duration.

## Boss Stones

BossRules owns personalized boss stones and remote Forsaken Power selection.

- `Personalized Boss Stones`: each player keeps their own unlocked boss stone powers.
- `Remote Forsaken Power Selection`: players can rotate through unlocked powers without returning to the Start Temple.
- `Rotate Forsaken Power Shortcut`: client-only shortcut for remote rotation.

Console commands:

- `bossrules:inspect bossstone`
- `bossrules:bossstone reset <exactPlayerName>`

## Compatibility

BossRules is designed to sit beside DropNSpawn:

- Use BossRules for boss altars, boss stones, Forsaken Powers, boss despawn, and boss tame pressure.
- Use DropNSpawn for creature drops, object loot, spawners, and world spawn tables.
- Use UsefulRunestones for pinless RuneStone global pins and Vegvisir rewards.

If another mod owns the same boss system, disable the overlapping BossRules feature in the BepInEx config.

## GitHub

https://github.com/sighsorry1029/DropNSpawn
