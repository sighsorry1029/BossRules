using BepInEx.Configuration;
using UnityEngine;

namespace BossRules;

internal static class BossRulesConfig
{
    private const float SameBossDuplicateBlockRadius = 64f;
    private static ConfigEntry<BossRulesPlugin.Toggle> _clientDebugLog = null!;
    private static ConfigEntry<BossRulesPlugin.Toggle> _enableAltarRules = null!;
    private static ConfigEntry<BossRulesPlugin.Toggle> _showOfferingBowlHoverInfo = null!;
    private static ConfigEntry<BossRulesPlugin.Toggle> _enableSameBossDuplicateBlock = null!;
    private static ConfigEntry<BossRulesPlugin.Toggle> _enableDespawnRules = null!;
    private static ConfigEntry<float> _defaultDespawnDelaySeconds = null!;
    private static ConfigEntry<float> _defaultDespawnRange = null!;
    private static ConfigEntry<BossRulesPlugin.Toggle> _enableBossTamedPressure = null!;
    private static ConfigEntry<BossRulesPlugin.Toggle> _enableForsakenPowerRules = null!;
    private static ConfigEntry<BossRulesPlugin.Toggle> _perPlayerBossStones = null!;
    private static ConfigEntry<BossRulesPlugin.Toggle> _remoteForsakenPowerSelection = null!;
    private static ConfigEntry<KeyboardShortcut> _rotateForsakenPowerShortcut = null!;

    internal static void Bind(BossRulesPlugin plugin)
    {
        _clientDebugLog = plugin.BindConfigEntry(
            "1 - General",
            "Client Debug Log",
            BossRulesPlugin.Toggle.Off,
            "If on, writes local BossRules diagnostic logs for this machine. Useful for altar refund and runtime tracing.",
            synchronizedSetting: false,
            configManagerOrder: 100);
        _rotateForsakenPowerShortcut = plugin.BindConfigEntry(
            "2 - Forsaken & Altars",
            "Rotate Forsaken Power Shortcut",
            new KeyboardShortcut(KeyCode.G),
            "Shortcut used to rotate through unlocked Forsaken Powers when Remote Forsaken Power Selection is enabled. This setting is client-side only.",
            synchronizedSetting: false,
            configManagerOrder: 600);
        _enableAltarRules = plugin.BindConfigEntry(
            "2 - Forsaken & Altars",
            "Enable Altar Rules",
            BossRulesPlugin.Toggle.On,
            $"If on, {BossRulesPlugin.AltarYamlFileName} altar entries can override OfferingBowl and boss ItemStand behavior.",
            synchronizedSetting: true,
            configManagerOrder: 100);
        _showOfferingBowlHoverInfo = plugin.BindConfigEntry(
            "2 - Forsaken & Altars",
            "Show OfferingBowl Hover Info",
            BossRulesPlugin.Toggle.On,
            "If on, looking at an OfferingBowl shows simplified offering info with the spawned boss/item and required offering item. Matching altar ItemStands also show their required supported item names.",
            synchronizedSetting: true,
            configManagerOrder: 200);
        _enableSameBossDuplicateBlock = plugin.BindConfigEntry(
            "3 - Boss Rules",
            "Enable Same Boss Duplicate Block",
            BossRulesPlugin.Toggle.On,
            $"If on, OfferingBowls and CreatureSpawners block new boss spawns when the same boss prefab already exists within {SameBossDuplicateBlockRadius:0} horizontal XZ meters. CreatureSpawner respawn timing starts after the duplicate boss is gone.",
            synchronizedSetting: true,
            configManagerOrder: 550);
        _enableBossTamedPressure = plugin.BindConfigEntry(
            "3 - Boss Rules",
            "Enable Boss Tamed Pressure",
            BossRulesPlugin.Toggle.Off,
            $"If on, {BossRulesPlugin.RulesYamlFileName} bossTamedPressure entries can damage and weaken tamed MonsterAI creatures near configured boss prefabs.",
            synchronizedSetting: true,
            configManagerOrder: 400);
        _perPlayerBossStones = plugin.BindConfigEntry(
            "2 - Forsaken & Altars",
            "Personalized Boss Stones",
            BossRulesPlugin.Toggle.On,
            "If on, each player has their own boss stone unlock state. Players inside the boss stone location when a trophy is sacrificed unlock that Forsaken Power for themselves.",
            synchronizedSetting: true,
            configManagerOrder: 300);
        _enableForsakenPowerRules = plugin.BindConfigEntry(
            "2 - Forsaken & Altars",
            "Forsaken Power Overhaul",
            BossRulesPlugin.Toggle.On,
            $"If on, {BossRulesPlugin.RulesYamlFileName} forsakenPowers entries can rebalance Forsaken Power duration, cooldown, stats, resistances, and supported special effects.",
            synchronizedSetting: true,
            configManagerOrder: 400);
        _remoteForsakenPowerSelection = plugin.BindConfigEntry(
            "2 - Forsaken & Altars",
            "Remote Forsaken Power Selection",
            BossRulesPlugin.Toggle.On,
            "If on, players can rotate through Forsaken Powers they have unlocked through per-player boss stones without returning to the Start Temple.",
            synchronizedSetting: true,
            configManagerOrder: 500);
        _enableDespawnRules = plugin.BindConfigEntry(
            "3 - Boss Rules",
            "Enable Despawn Rules",
            BossRulesPlugin.Toggle.On,
            $"If on, {BossRulesPlugin.RulesYamlFileName} despawn entries and auto-detected boss despawn tracking can remove bosses when no living player is nearby.",
            synchronizedSetting: true,
            configManagerOrder: 300);
        _defaultDespawnDelaySeconds = plugin.BindConfigEntry(
            "3 - Boss Rules",
            "default despawn delay seconds",
            90f,
            "Default seconds to wait after no living player is within range before a tracked boss despawns.",
            synchronizedSetting: true,
            configManagerOrder: 100);
        _defaultDespawnRange = plugin.BindConfigEntry(
            "3 - Boss Rules",
            "default despawn range",
            64f,
            "Default horizontal XZ range used to decide whether any living player is near a tracked boss.",
            synchronizedSetting: true,
            configManagerOrder: 200);
    }

    internal static bool IsClientDebugLogEnabled() => _clientDebugLog?.Value == BossRulesPlugin.Toggle.On;

    internal static bool IsAltarRulesEnabled() => _enableAltarRules?.Value != BossRulesPlugin.Toggle.Off;

    internal static bool ShouldShowOfferingBowlHoverInfo() => _showOfferingBowlHoverInfo?.Value != BossRulesPlugin.Toggle.Off;

    internal static float GetSameBossDuplicateBlockRadius()
    {
        return _enableSameBossDuplicateBlock?.Value == BossRulesPlugin.Toggle.Off
            ? 0f
            : SameBossDuplicateBlockRadius;
    }

    internal static bool IsDespawnRulesEnabled() => _enableDespawnRules?.Value != BossRulesPlugin.Toggle.Off;

    internal static bool ShouldCaptureAltarSpawnRefunds() => IsDespawnRulesEnabled();

    internal static float GetDefaultDespawnDelaySeconds() => _defaultDespawnDelaySeconds?.Value ?? 90f;

    internal static float GetDefaultDespawnRange() => _defaultDespawnRange?.Value ?? 64f;

    internal static bool IsBossTamedPressureEnabled() => _enableBossTamedPressure?.Value != BossRulesPlugin.Toggle.Off;

    internal static bool IsForsakenPowerRulesEnabled() => _enableForsakenPowerRules?.Value != BossRulesPlugin.Toggle.Off;

    internal static bool IsPerPlayerBossStonesEnabled() => _perPlayerBossStones?.Value != BossRulesPlugin.Toggle.Off;

    internal static bool IsRemoteForsakenPowerSelectionEnabled() => _remoteForsakenPowerSelection?.Value != BossRulesPlugin.Toggle.Off;

    internal static KeyboardShortcut GetRotateForsakenPowerShortcut() =>
        _rotateForsakenPowerShortcut?.Value ?? default;
}
