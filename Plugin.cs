using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace BossRules;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public sealed class BossRulesPlugin : BaseUnityPlugin
{
    internal const string ModName = "BossRules";
    internal const string ModVersion = "1.0.6";
    internal const string Author = "sighsorry";
    internal const string ModGUID = $"{Author}.{ModName}";
    internal const string AltarYamlFileName = $"{ModName}.altar.yml";
    internal const string AltarReferenceYamlFileName = $"{ModName}.altar.reference.yml";
    internal const string RulesYamlFileName = $"{ModName}.yml";
    internal const string ForsakenPowersYamlFileName = $"{ModName}.forsakenPowers.yml";
    private const float FileReloadDebounceSeconds = 0.25f;

    internal static readonly ManualLogSource BossRulesLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

    private static ConfigSync? _configSync;
    private readonly Harmony _harmony = new(ModGUID);
    private CustomSyncedValue<string> _syncedAltarYaml = null!;
    private CustomSyncedValue<string> _syncedRulesYaml = null!;
    private CustomSyncedValue<string> _syncedForsakenPowersYaml = null!;
    private FileSystemWatcher? _watcher;
    private float _reloadDueAt = -1f;
    private int _altarYamlReloadRequested;
    private int _rulesYamlReloadRequested;
    private int _forsakenPowersYamlReloadRequested;
    private bool _reloadAltarYaml;
    private bool _reloadRulesYaml;
    private bool _reloadForsakenPowersYaml;
    private ConfigEntry<Toggle> _lockConfiguration = null!;

    public enum Toggle
    {
        On = 1,
        Off = 0
    }

    internal static ConfigSync ConfigSync =>
        _configSync ?? throw new InvalidOperationException("ServerSync has not been initialized yet.");

    internal static string ConfigDirectoryPath => Path.Combine(Paths.ConfigPath, ModName);
    internal static string AltarYamlFilePath => Path.Combine(ConfigDirectoryPath, AltarYamlFileName);
    internal static string AltarReferenceYamlFilePath => Path.Combine(ConfigDirectoryPath, AltarReferenceYamlFileName);
    internal static string RulesYamlFilePath => Path.Combine(ConfigDirectoryPath, RulesYamlFileName);
    internal static string ForsakenPowersYamlFilePath => Path.Combine(ConfigDirectoryPath, ForsakenPowersYamlFileName);
    internal static bool IsSourceOfTruth => ConfigSync.IsSourceOfTruth;
    internal static bool IsRuntimeServer() => ZNet.instance != null && ZNet.instance.IsServer();

    private void Awake()
    {
        EnsureServerSyncInitialized();
        Directory.CreateDirectory(ConfigDirectoryPath);
        AltarConfigurationFiles.EnsureDefaultFiles();
        BossRuleConfigurationFiles.EnsureDefaultFile();
        ForsakenPowerConfigurationFiles.EnsureDefaultFile();
        BindConfiguration();

        _syncedAltarYaml = new CustomSyncedValue<string>(ConfigSync, "altar-yaml", "", priority: 50);
        _syncedAltarYaml.ValueChanged += HandleSyncedAltarYamlChanged;
        _syncedRulesYaml = new CustomSyncedValue<string>(ConfigSync, "rules-yaml", "", priority: 60);
        _syncedRulesYaml.ValueChanged += HandleSyncedRulesYamlChanged;
        _syncedForsakenPowersYaml = new CustomSyncedValue<string>(ConfigSync, "forsaken-powers-yaml", "", priority: 65);
        _syncedForsakenPowersYaml.ValueChanged += HandleSyncedForsakenPowersYamlChanged;
        ConfigSync.SourceOfTruthChanged += HandleSourceOfTruthChanged;

        LoadLocalAltarYamlAndPublish("startup");
        LoadLocalRulesYamlAndPublish("startup");
        LoadLocalForsakenPowersYamlAndPublish("startup");
        _harmony.PatchAll(typeof(BossRulesPlugin).Assembly);
        Localizer.Initialize(this);
        BossStonePerPlayerRuntime.EnsureRpcRegistered();
        DespawnRulesManager.EnsureMessageRpcRegistered();
        BossRulesConsoleCommands.Register();
        InitializeWatcher();
        Config.Save();
    }

    private void Update()
    {
        ProcessQueuedYamlReload();
        Localizer.ProcessDeferredLoad();
        DataForgeStatusEffectBridge.ProcessDeferredSubscription();
        AltarRuntime.ProcessPendingAltarSummonMarkers();
        AltarRuntime.ProcessPendingQueenDungeonRooms();
        AltarRuntime.ProcessDeferredReapply();
        AltarReferenceGenerator.TryAutoRefreshReferenceConfigurationFile();
        ForsakenPowerRuntime.ProcessDeferredApply();
        BossStonePerPlayerRuntime.EnsureRpcRegistered();
        BossStonePerPlayerRuntime.ProcessPendingSacrificeRequests();
        BossStonePerPlayerRuntime.ProcessPendingResetRequests();
        DespawnRulesManager.EnsureMessageRpcRegistered();
        DespawnRulesManager.ExecuteServerTick();
        BossTamedPressureRuntime.ExecuteServerTick();
    }

    private void OnDestroy()
    {
        if (_configSync != null)
        {
            ConfigSync.SourceOfTruthChanged -= HandleSourceOfTruthChanged;
        }

        _syncedAltarYaml.ValueChanged -= HandleSyncedAltarYamlChanged;
        _syncedRulesYaml.ValueChanged -= HandleSyncedRulesYamlChanged;
        _syncedForsakenPowersYaml.ValueChanged -= HandleSyncedForsakenPowersYamlChanged;
        _watcher?.Dispose();
        _watcher = null;
        Interlocked.Exchange(ref _altarYamlReloadRequested, 0);
        Interlocked.Exchange(ref _rulesYamlReloadRequested, 0);
        Interlocked.Exchange(ref _forsakenPowersYamlReloadRequested, 0);
        ClearPendingYamlReloads();
        AltarRuntime.Shutdown();
        BossStonePerPlayerRuntime.Shutdown();
        DespawnRulesManager.ShutdownMessages();
        ForsakenPowerSelectionRuntime.Shutdown();
        AltarReferenceGenerator.ShutdownAutoRefresh();
        BossRulesManager.ClearRuntimeState();
        BossRulesRuntime.Reset();
        ForsakenPowerRuntime.Reset();
        DataForgeStatusEffectBridge.Shutdown();
        Localizer.Shutdown();
        _harmony.UnpatchSelf();
        Config.Save();
    }

    private static void EnsureServerSyncInitialized()
    {
        if (_configSync != null)
        {
            return;
        }

        _configSync = new ConfigSync(ModGUID)
        {
            DisplayName = ModName,
            CurrentVersion = ModVersion,
            MinimumRequiredVersion = ModVersion
        };
    }

    private void BindConfiguration()
    {
        bool saveOnSet = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;
        try
        {
            _lockConfiguration = BindConfigEntry(
                "1 - General",
                "Lock Configuration",
                Toggle.On,
                "If on, synced configuration can be changed by server admins only.",
                synchronizedSetting: true,
                configManagerOrder: 200);
            BossRulesConfig.Bind(this);

            _ = ConfigSync.AddLockingConfigEntry(_lockConfiguration);
        }
        finally
        {
            Config.SaveOnConfigSet = saveOnSet;
        }
    }

    internal ConfigEntry<T> BindConfigEntry<T>(
        string group,
        string name,
        T value,
        string description,
        bool synchronizedSetting = true,
        int? configManagerOrder = null)
    {
        ConfigDescription extendedDescription = new(
            description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
            null,
            BuildConfigDescriptionTags(configManagerOrder));
        ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
        SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;
        return configEntry;
    }

    private static object[] BuildConfigDescriptionTags(int? configManagerOrder)
    {
        if (!configManagerOrder.HasValue)
        {
            return Array.Empty<object>();
        }

        return new object[]
        {
            new ConfigurationManagerAttributes
            {
                Order = configManagerOrder.Value
            }
        };
    }

    private sealed class ConfigurationManagerAttributes
    {
        public int? Order = null;
    }

    private void InitializeWatcher()
    {
        _watcher = new FileSystemWatcher(ConfigDirectoryPath, "*.yml")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
        };
        _watcher.Changed += QueueYamlReload;
        _watcher.Created += QueueYamlReload;
        _watcher.Deleted += QueueYamlReload;
        _watcher.Renamed += QueueYamlReload;
        _watcher.EnableRaisingEvents = true;
    }

    private void QueueYamlReload(object sender, FileSystemEventArgs args)
    {
        QueueYamlReloadForFile(Path.GetFileName(args.FullPath));
        if (args is RenamedEventArgs renamedArgs)
        {
            QueueYamlReloadForFile(Path.GetFileName(renamedArgs.OldFullPath));
        }
    }

    private void QueueYamlReloadForFile(string fileName)
    {
        if (string.Equals(fileName, AltarYamlFileName, StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Exchange(ref _altarYamlReloadRequested, 1);
            return;
        }

        if (string.Equals(fileName, RulesYamlFileName, StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Exchange(ref _rulesYamlReloadRequested, 1);
            return;
        }

        if (string.Equals(fileName, ForsakenPowersYamlFileName, StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Exchange(ref _forsakenPowersYamlReloadRequested, 1);
        }
    }

    private void ProcessQueuedYamlReload()
    {
        bool reloadRequested = false;
        if (Interlocked.Exchange(ref _altarYamlReloadRequested, 0) != 0)
        {
            _reloadAltarYaml = true;
            reloadRequested = true;
        }

        if (Interlocked.Exchange(ref _rulesYamlReloadRequested, 0) != 0)
        {
            _reloadRulesYaml = true;
            reloadRequested = true;
        }

        if (Interlocked.Exchange(ref _forsakenPowersYamlReloadRequested, 0) != 0)
        {
            _reloadForsakenPowersYaml = true;
            reloadRequested = true;
        }

        if (reloadRequested)
        {
            _reloadDueAt = IsSourceOfTruth
                ? Time.realtimeSinceStartup + FileReloadDebounceSeconds
                : -1f;
        }

        if (!IsSourceOfTruth)
        {
            _reloadDueAt = -1f;
            ClearPendingYamlReloads();
            return;
        }

        if (_reloadDueAt < 0f || Time.realtimeSinceStartup < _reloadDueAt)
        {
            return;
        }

        _reloadDueAt = -1f;
        bool reloadAltarYaml = _reloadAltarYaml;
        bool reloadRulesYaml = _reloadRulesYaml;
        bool reloadForsakenPowersYaml = _reloadForsakenPowersYaml;
        ClearPendingYamlReloads();
        if (reloadAltarYaml)
        {
            LoadLocalAltarYamlAndPublish("file change");
        }

        if (reloadRulesYaml)
        {
            LoadLocalRulesYamlAndPublish("file change");
        }

        if (reloadForsakenPowersYaml)
        {
            LoadLocalForsakenPowersYamlAndPublish("file change");
        }
    }

    private void ClearPendingYamlReloads()
    {
        _reloadAltarYaml = false;
        _reloadRulesYaml = false;
        _reloadForsakenPowersYaml = false;
    }

    private void HandleSourceOfTruthChanged(bool sourceOfTruth)
    {
        if (sourceOfTruth)
        {
            AltarReferenceGenerator.ResetAutoRefresh();
            LoadLocalAltarYamlAndPublish("authority change");
            LoadLocalRulesYamlAndPublish("authority change");
            LoadLocalForsakenPowersYamlAndPublish("authority change");
            return;
        }

        ApplyAltarYaml(_syncedAltarYaml.Value ?? "", "server sync");
        ApplyRulesYaml(_syncedRulesYaml.Value ?? "", "server sync");
        ApplyForsakenPowersYaml(_syncedForsakenPowersYaml.Value ?? "", "server sync");
    }

    private void HandleSyncedAltarYamlChanged()
    {
        if (IsSourceOfTruth)
        {
            return;
        }

        ApplyAltarYaml(_syncedAltarYaml.Value ?? "", "server sync");
    }

    private void HandleSyncedRulesYamlChanged()
    {
        if (IsSourceOfTruth)
        {
            return;
        }

        ApplyRulesYaml(_syncedRulesYaml.Value ?? "", "server sync");
    }

    private void HandleSyncedForsakenPowersYamlChanged()
    {
        if (IsSourceOfTruth)
        {
            return;
        }

        ApplyForsakenPowersYaml(_syncedForsakenPowersYaml.Value ?? "", "server sync");
    }

    private void LoadLocalAltarYamlAndPublish(string source)
    {
        AltarConfigurationFiles.EnsureDefaultFiles();
        string yaml;
        try
        {
            yaml = File.ReadAllText(AltarYamlFilePath);
        }
        catch (Exception ex)
        {
            BossRulesLogger.LogError($"Failed to read {AltarYamlFilePath}. {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (!ApplyAltarYaml(yaml, source))
        {
            return;
        }

        if (IsSourceOfTruth)
        {
            _syncedAltarYaml.AssignLocalValue(yaml);
        }
    }

    private void LoadLocalRulesYamlAndPublish(string source)
    {
        BossRuleConfigurationFiles.EnsureDefaultFile();
        string yaml;
        try
        {
            yaml = File.ReadAllText(RulesYamlFilePath);
        }
        catch (Exception ex)
        {
            BossRulesLogger.LogError($"Failed to read {RulesYamlFilePath}. {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (!ApplyRulesYaml(yaml, source))
        {
            return;
        }

        if (IsSourceOfTruth)
        {
            _syncedRulesYaml.AssignLocalValue(yaml);
        }
    }

    private void LoadLocalForsakenPowersYamlAndPublish(string source)
    {
        ForsakenPowerConfigurationFiles.EnsureDefaultFile();
        string yaml;
        try
        {
            yaml = File.ReadAllText(ForsakenPowersYamlFilePath);
        }
        catch (Exception ex)
        {
            BossRulesLogger.LogError($"Failed to read {ForsakenPowersYamlFilePath}. {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (!ApplyForsakenPowersYaml(yaml, source))
        {
            return;
        }

        if (IsSourceOfTruth)
        {
            _syncedForsakenPowersYaml.AssignLocalValue(yaml);
        }
    }

    private bool ApplyAltarYaml(string yaml, string source)
    {
        string content = yaml ?? "";
        BossRulesDebugLog.Client($"Applying altar YAML source={source} bytes={content.Length}.");
        if (!AltarConfiguration.TryParse(content, source, out IReadOnlyList<AltarConfigurationEntry> entries))
        {
            return false;
        }

        BossRulesDebugLog.Client($"Parsed altar YAML source={source} entries={entries.Count}.");
        AltarRuntime.Reload(entries);
        return true;
    }

    private bool ApplyRulesYaml(string yaml, string source)
    {
        if (!BossRuleConfiguration.TryParse(yaml, source, out BossRuleConfigurationState configuration))
        {
            return false;
        }

        BossRulesRuntime.Reload(configuration);
        return true;
    }

    private bool ApplyForsakenPowersYaml(string yaml, string source)
    {
        if (!ForsakenPowerConfiguration.TryParse(yaml, source, out IReadOnlyList<ForsakenPowerDefinition> entries))
        {
            return false;
        }

        ForsakenPowerRuntime.Configure(entries);
        return true;
    }
}
