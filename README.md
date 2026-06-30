# BossRules

Forsaken powers overhaul, despawn abandoned bosses with offering refunds, hover an altar to see the offering and boss. <br>
Rotate Forsaken Powers remotely, block duplicate summons, personalize boss stones, and pressure tames near bosses.

![](https://i.ibb.co/C3tz6LBW/eikthyrdespawn.gif) <br>
You can change the boss despawn range and delay in the config.

![](https://i.ibb.co/p6B05TMs/moderdespawn.gif) <br>
Getting away from the boss with also count for despawn.

![](https://i.ibb.co/kZX4X5J/faderdespawn.gif) <br>
If you get back in time, despawn would be canceled. If the boss does despawn offerings would be refunded.

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
- `BossRules.yml`: boss despawn, boss tamed pressure, and localization rules.
- `BossRules.forsakenPowers.yml`: Forsaken Power stat edits in a DataForge-compatible row format.
- `sighsorry.BossRules.cfg`: synced BepInEx feature toggles.

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

- `despawn`: default range/delay plus compact rows in `prefab, despawnRange, despawnDelay, refunds` format.
- `bossTamedPressure`: a global rule for tamed creatures near bosses.
- `localization`: boss despawn, tame pressure, and remote power selection messages.

`BossRules.forsakenPowers.yml` controls selected Forsaken Power stat edits. Its top-level list is intentionally compatible with DataForge `effects.yml` rows.

Despawn rows use `despawn.defaults` when range or delay is omitted. Set `despawnRange` to `0` to disable despawn for one boss prefab.

```yaml
despawn:
  defaults: 64, 90
  rules:
  - Eikthyr
  - Bonemass, 96, 120
  - Dragon, , 180
  - GoblinKing, 0
  - Fader, 128, 180, false
```

Refund values:

- omit the fourth value for `true`
- `true`: refund the actual altar offering when the boss was marked as altar-summoned
- `false`: no refund

Refunds drop at the original `OfferingBowl` position when possible. Bosses from `CreatureSpawner`, `SpawnSystem`, or other world sources do not receive altar refunds just because they died near an altar.

## Forsaken Powers

BossRules can rebalance selected `SE_Stats` fields for Forsaken Powers:

- duration and cooldown
- guardian power activation adrenaline gain through the synced config
- stamina costs
- block stamina flat cost and timed block bonus
- outgoing damage
- incoming damage modifiers
- health, stamina, and eitr regen
- carry weight
- flat and percent armor
- movement speed and jump height
- skill levels
- adrenaline and stagger gauge
- vanilla status attributes such as `SailingPower`

`BossRules.forsakenPowers.yml` uses the same compact style as DataForge status effects:

```yaml
- effect: GP_Eikthyr
  time: 18, 60
  staminaDrainModifier:
    run: -0.5
    jump: -0.5

- effect: GP_Moder
  time: 18, 60
  attributes: SailingPower
  stats:
    speedModifier: 0.1
    jumpModifier: 0, 0.2, 0
```

Modifier values use Valheim/DataForge-style factors. For example, `-0.5` means 50% lower cost, `0.1` means +10%, and `regenMultiplier: 2, 1, 1` means health regen x2 while stamina and eitr stay unchanged.

### Vanilla vs BossRules Forsaken Powers

The table below compares vanilla `GP_` effect rows from DataForge `effects.reference.yml` with the BossRules preset in `BossRules.forsakenPowers.yml`.

| Effect | Vanilla effect | BossRules effect |
| --- | --- | --- |
| `GP_Eikthyr` | `time: 300, 1200`<br>Stamina drain: run/jump/swim `-0.6` | `time: 16, 60`<br>Pickaxe damage `0.5`<br>Stamina drain: run/dodge `-0.5`<br>Speed `0.1`<br>Blunt: `SlightlyResistant` |
| `GP_TheElder` | `time: 300, 1200`<br>Regen `1.3, 1, 1`<br>Chop/pickaxe damage `0.6` | `time: 16, 60`<br>Chop damage `0.5`<br>Stamina drain: swim/sneak `-0.5`<br>Regen `2, 1, 1`<br>Poison: `SlightlyResistant` |
| `GP_Bonemass` | `time: 300, 1200`<br>Block `0, -5`<br>Block stamina drain `-1`<br>Blunt/slash/pierce: `SlightlyResistant` | `time: 16, 60`<br>Block stamina drain `-0.5`<br>Block `0, -5`<br>Armor `20, 0.2`<br>Frost: `SlightlyResistant` |
| `GP_Moder` | `time: 300, 1200`<br>`SailingPower`<br>Speed `0.1`<br>Carry weight `300`<br>Frost: `Resistant` | `time: 16, 60`<br>`SailingPower`<br>Stamina drain: jump `-0.5`<br>Carry weight `300`<br>Jump `0, 0.2, 0`<br>Farming/Fishing skill `25`<br>Fire: `SlightlyResistant` |
| `GP_Yagluth` | `time: 300, 1200`<br>Farming skill `25`<br>Lightning: `Resistant`<br>Blunt/slash/pierce/chop/pickaxe/fire/frost/lightning/poison/spirit damage `0.1` | `time: 16, 60`<br>Fire/poison/frost/lightning/spirit damage `0.1`<br>Regen `1, 1, 2`<br>Pierce: `SlightlyResistant` |
| `GP_Queen` | `time: 300, 1200`<br>Regen `1, 1, 2`<br>Sneak stamina drain `-1`<br>Poison: `Resistant` | `time: 16, 60`<br>Pierce/blunt/slash damage `0.1`<br>Attack stamina drain `-0.1`<br>Slash: `SlightlyResistant` |
| `GP_Fader` | `time: 300, 1200`<br>Adrenaline `1`<br>Stagger `-0.5`<br>Attack damage `None, 0`<br>Fire: `Resistant` | `time: 16, 60`<br>Adrenaline `1`<br>Stagger `-0.5`<br>Lightning: `SlightlyResistant` |

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
https://github.com/sighsorry1029/BossRules
