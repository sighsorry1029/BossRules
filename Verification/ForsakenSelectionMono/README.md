# Forsaken Power radial selection verification

2026-10-08. Baseline main `1ac5766c7054228e3ddbcef0bb8498d5bd1b51a3`, initially clean.
The radial implementation and fixes below are included in the requested 1.1.5 release.
Earlier implementation checks used 1.1.4 Debug builds without creating a Release ZIP.

## Original game contracts and scope

The installed Windows client is Valheim 1.0.17, Steam build 25730771.
Original `valheim_Data/Managed/assembly_valheim.dll` SHA-256:
`25A0A107DCE4D834C44C2B72D0EAFD5CB7793933BDA81816ACCFA1EA9543DACE`.
The build references the original installed game assemblies, with no publicizer.
The global Valheim index and existing 1.0.16 reference material were consulted;
only the input, player power-selection and native radial types needed here were
additionally read from the installed originals. This is not a full 1.0.17 mod audit
or dedicated-server compatibility certification.

- `Plugin.LateUpdate` polls once after native `RadialBase.Update` has sampled the
  pointer. The old per-Player Update patch is removed. A shortcut press opens the
  menu immediately and another press cancels. Releasing the key or chord modifiers
  leaves the menu open. A left-click begun after opening confirms on mouse-up,
  matching native mouse radial interaction; there is no hold timer or cycle action.
  Right-click cancels on mouse-up as well, avoiding a held attack/block after closing.
- The existing remote-selection setting, client shortcut/default, boss-stone
  personal unlock keys and `Player.SetGuardianPower` selection path are preserved.
  Current unlocks and a valid status effect are checked again before selection.
  No power activation, cooldown mutation, RPC, inventory or server policy is added.
- The HUD's shared renderer supplies camera/input blocking. An `OpenRadialConfig`
  root preserves native reset/control initialization and death subscription;
  explicit `IRadialConfig` dispatch opens the BossRules content. Native successful
  interaction is bypassed so the last-used item and radial preferences are retained.
  The initially inactive native radial must be allowed to activate before checking
  `RadialData.SO`: its `RadialDataInitializer.Awake` supplies this data. The GroupElement
  template is validated in the content initialization callback after native Open.
- Original `RadialBase.Close(bool)` and `m_highlighter` are private. Production uses
  a cached Harmony method delegate and field accessor, not direct private access.
  The shared highlighter is moved out of our entries before destroying them.
  Session-owned elements/materials/root are released; input overrides are restored
  only where another mod has not replaced them. Focus loss, disable and destruction
  cancel immediately; player/HUD replacement and feature/settings changes cancel
  on the next input poll.
- Unlocked-power lists refresh on open or cache invalidation/player/ObjectDB
  replacement. UI entries are created on open and released on close, not each frame.
  Reflection accessors are cached. Performance and allocation rates are not measured.
- The icon-side description panel clones the HUD piece-icon tooltip presentation
  once per radial session, without using `UITooltip`'s global hover instance.
  `StatusEffect.GetTooltipString()` is public virtual on the original DLL; vanilla
  boss stones use it too. Descriptions read current ObjectDB effects and localization
  when the highlighted power changes or is pointed at again after centering. This
  preserves the game's available effect lines, not an exhaustive list of every mod's
  hidden mechanic. Names stay in the radial center; the side panel removes only
  the exact leading localized `m_tooltip` introduction, then retains the remaining
  tooltip output. No shared effect fields are modified or cloned. Graphic raycasts
  are disabled on the clone. The actual icon's
  bounds anchor the panel; screen edges flip/clamp its position and oversized text
  panels scale to fit. Corners reuse a buffer; text/layout rebuild only on selection
  changes. Closing/hiding/cleanup follows the radial session.

## Reported first-open and missing-description fixes

The corrected `fearnospear` profile log contains repeated BossRules warnings:
`Could not display Forsaken Power description: HUD tooltip title is unavailable.`
Its plugin SHA-256 before these fixes was
`B206AEE034B97489ACF84406287DF0450C3ED483F6776E805BAF5AB93C2223DA`.
The earlier `mids` log is not evidence for this report.

Focused read-only UnityPy 1.25.3 inspection of installed original bundles `d59cfac`
and `c4210710` traced `Assets/UI/prefabs/IngameGui/IngameGui_HUD.prefab`:

- `Hud.m_radialMenu` points at initially inactive `ValheimRadial`, carrying the
  initializer's `_dataObject` and the renderer. Original `RadialBase.Open` activates
  it before invoking the custom config; the custom root's nested Open also restores
  activation after native Awake's initial deactivation. Requiring SO before Open
  prevented this lifecycle until vanilla G/T had initialized the renderer.
- `Hud.m_pieceIconPrefab` → `UITooltip.m_tooltipPrefab` points at `Tooltip/Text`,
  with no `Topic`. Native UITooltip treats Topic as optional. BossRules no longer
  requires it; the subsequent effects-only change puts effects in Text and hides
  Topic on clones of custom templates that provide one.
- That compact template has fixed 24-unit text height, a layout group with child
  height control disabled, and a horizontally fitting ContentSizeFitter. On the
  titleless clone only, enable preferred child/root height and rebuild the root
  layout so multiple effect lines contribute to placement bounds. Source prefabs
  are not modified. This is asset/code inspection, not rendered visual validation.

## Effects-only panel

Original 1.0.17 `SE_Stats.GetTooltipString` prefixes `m_tooltip` and two newlines
before generated stat/duration lines. The formatter localizes both strings, strips
only an exact leading introduction ending at a newline/end boundary, and trims the
remaining outer whitespace. A custom override's unmatched or interior text remains;
there is no sentence heuristic, blank-line split or numeric-only filter. An empty
result hides the panel. The existing icon positioning and screen fitting remain.

Native stat tooltips omit attributes. Public `HaveAttribute` checks add concise
English/Korean lines for three confirmed original consumers: SailingPower in
`Ship.IsWindControllActive`, TamingBoost in `Tameable.DecreaseRemainingTime`, and
DoubleImpactDamage on the **hit target** in `ImpactEffect.OnCollisionEnter`.
Taming uses a creature-specific multiplier, so its line promises no fixed percentage.
ColdResistance is not described solely from its enum name: no consumer was found
in the current original game assembly, and an external mod may assign its own meaning.
This does not change or remove any attribute. Other custom effects absent from the
game/mod's own tooltip are not inferred or synthesized by this change.

## Reproduce the automated checks

```powershell
dotnet build BossRules.sln -c Debug -p:DeployToGame=true
dotnet build Verification/ForsakenSelectionMono/ForsakenSelection.Mono.csproj -c Debug
python Verification/BootstrapScan/run_mono.py `
  'C:/Program Files (x86)/Steam/steamapps/common/Valheim' `
  './bin/Debug/BossRules.dll' `
  'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/core' `
  './Verification/ForsakenSelectionMono/bin/Debug/net48/ForsakenSelection.Mono.exe'
```

The runner executes the actual merged plugin in the installed game's Mono with
original DLLs. Its 96 checks cover immediate opening, persistence after key release, a second
press closing, mouse-up confirmation, ignoring opening-frame/pre-existing clicks,
short complete clicks, duplicate confirmation, cancellation winning over selection,
held-key blocking and a fresh press after cancellation.
The common eligibility gate is exercised with synthetic booleans; this does not
simulate each actual Unity GUI or input device.
The additional description checks verify the original public virtual tooltip API
and placement on both sides, edge flipping, vertical clamping and a screen-fitted
large panel on a small display. They exercise production placement calculations;
they do not render the actual prefab or measure font readability.

The cold-open regression calls the merged production `TryOpen` with null SO and
asserts it reaches native Open. The native boundary is recorded and stopped before
Unity activation; this is not an Awake execution test. This check fails against the
reported profile's pre-fix DLL and passes against the fixed DLL. Three presentation
checks exercise production Show for multiline effects, empty content hiding, and
showing content again. Twenty English/Korean checks run the original SE_Stats and
Localization string methods with fixture game translations and the plugin's actual
embedded attribute translations. They cover stat/duration preservation, unchanged
source fields/ordinary tooltip, attributes and their removal, already-localized
custom overrides, unmatched/interior text, whole-prefix boundaries, introduction-only
results and nonnumeric effects. The constructor and layout rendering are covered
only by focused asset/code inspection, not by these managed checks.

Managed contract probes bind both new Harmony patches to the original game,
exercise explicit root-interface dispatch, access the original private highlighter
field, invoke the cached private close delegate and check conditional input delegate
restoration. Native UI method bodies are suppressed in these probes. Animation hash
IDs, an unused Unity object memory-offset query, data-directory lookup and profiler markers are substituted
solely to allow static initialization without the engine. Object liveness, object
creation, text setters, localization singleton lookup, activation and layout calls
are also substituted in the new checks; these fixtures never dereference a native object. Initial fixture
runs failed on missing engine initialization, then passed after these substitutions.
Mono still prints internal-call lookup notices for the suppressed Unity bodies.
Those notices are not game-world results or evidence that the installation is mismatched.

Baseline and final Debug builds, ILRepack, game-plugin copy and SHA-256 equality are
checked separately. No game scene, mouse device, save, multiplayer session or dedicated
server is executed by this harness.

The 1.1.5 release passed Debug deployment and the Release build with zero warnings
or errors. All 96 checks also passed against the final merged Release DLL. The ZIP's
eight expected files match their sources byte-for-byte, including the 256×256 PNG
icon, 1.1.5 manifest and changelog; its DLL assembly version is 1.1.5.0. The installed
default game plugin matches the 1.1.5 Debug DLL by SHA-256. No new game session or
multiplayer run was performed for release verification.

## Remaining actual-game checks

1. Restart the game and enter a world without ever opening vanilla G/T. Open directly
   with the BossRules shortcut; repeat after logout/rejoin creates a new HUD.
   With 0, 1 and several personally unlocked powers, open with one press, release
   the key/chord modifiers, and select with a left-click; verify
   icons/names, selected power, unchanged active effect/cooldown and no accidental
   selection when clicking in the 24-pixel center dead zone.
   Verify the same powers in English/Korean: center-only names, no introductory lore,
   preserved numerical effects/duration and Moder's sailing line only while its
   SailingPower attribute is configured. Check long modded effect lists
   and UI scaling at multiple resolutions; center/close must hide the description
   and repeated hover must reuse one panel without blocking selection input.
2. While open, exercise the shortcut again, Escape, right-click, chat/inventory/map, focus loss,
   death/teleport/sleep, component disable, feature off, shortcut change and logout.
   Verify the mouse/camera recover and no action occurs until a fresh press.
3. Repeat open/close, then use vanilla G/T and last-used actions, including rebound
   keys and radial animation/back-button settings. Verify native highlights remain
   alive, no unwanted last-used entry is retained and Korean/English hints fit the HUD.
4. Remove an unlock/status effect while open and replace/reload ObjectDB/HUD with the
   relevant mod combination. Verify revalidation/cancellation and no stale selection.
5. Verify local host and remote-client selection, dedicated-server startup and
   modded extra powers. This feature intentionally supplies no gamepad radial input.
