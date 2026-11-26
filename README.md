# BossRules

Choose Forsaken Powers away from the Sacrificial Stones, customize boss altars, and recover offerings when a fight is abandoned.

![Forsaken Power radial menu with effects beside the highlighted icon](https://i.ibb.co/LdtFvGDQ/Screenshot-2026-10-08-182508.png)

Press **K**, point at a power to see its effects, then left-click to equip it. Switching preserves your cooldown.

![Eikthyr despawn countdown](https://i.ibb.co/C3tz6LBW/eikthyrdespawn.gif)

When the last living player leaves the boss’s range, a countdown starts.

![Player leaving Moder’s encounter area](https://i.ibb.co/p6B05TMs/moderdespawn.gif)

Return before the countdown ends to keep the fight going.

![Fader despawn and offering refund](https://i.ibb.co/kZX4X5J/faderdespawn.gif)

When refunds are enabled, a despawn returns the offering to its altar, even in a later phase of a linked boss fight.

## Boss stones and powers

Hang a trophy at the Sacrificial Stones to unlock its power for everyone present. Each player sees their own unlocked trophies. You can rebind the menu key or hide its HUD hint.

All seven powers are rebalanced. Servers can tune their effects, duration and cooldown, and how much adrenaline activation grants.

## Altars and encounters

- Look at an altar to see what it summons and what it costs. Linked item stands show what they accept. Servers can change offerings, bosses and altar cooldowns.
- Altars and creature spawners can block duplicate bosses nearby. A blocked spawner’s respawn timer starts once the boss is gone.
- Despawn timers and refunds can vary by boss. Under automatic rules, a newly placed dungeon Queen or Frozen King is protected until a player approaches.
- Bosses can optionally weaken nearby tames, making them lose health, take more damage and deal less.

## Setup

Install the same version on the host or dedicated server and every client. The first launch creates `BepInEx/config/sighsorry.BossRules.cfg` and the rule files in `BepInEx/config/BossRules/`. Server rule edits sync while the world is running.

To customize an altar, copy its entry from `BossRules.altar.reference.yml` into `BossRules.altar.yml`. Edit encounters in `BossRules.yml` and powers in `BossRules.forsakenPowers.yml`.

## Compatibility

- **YouAreNotWorthy.** Press **Use** at the Queen’s or Frozen King’s inner altar for free attempts until your first personal victory. Item requirements still apply. For the Frozen King, you need the personal key from his outer gate, which costs its usual offering. Free attempts end when you defeat his final phase.
- **More World Locations AIO.** Automatic discovery skips MWL interiors. Custom altar rules still work there. For a full scan, run `bossrules:reference altar` on the host or server after world load. This can pause the server and use substantial memory.
- **DataForge.** Its overrides take priority for individual status effects.

[Compare the seven power presets with vanilla](https://github.com/sighsorry1029/BossRules/blob/ae28779955a8979a96a43de373504d3a35f6868f/README.md#vanilla-vs-bossrules-forsaken-powers) · [Configure refunds across boss phases](https://github.com/sighsorry1029/BossRules/blob/ae28779955a8979a96a43de373504d3a35f6868f/README.md#boss-phases-and-offering-refunds)
