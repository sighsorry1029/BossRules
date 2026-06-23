using System;
using System.Collections.Generic;
using System.IO;
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
    internal const string ModVersion = "1.0.0";
    internal const string Author = "sighsorry";
    internal const string ModGUID = $"{Author}.{ModName}";
    internal const string AltarYamlFileName = $"{ModName}.altar.yml";
    internal const string AltarReferenceYamlFileName = $"{ModName}.altar.reference.yml";
    internal const string RulesYamlFileName = $"{ModName}.yml";
    private const float FileReloadDebounceSeconds = 0.25f;

    internal static BossRulesPlugin? Instance { get; private set; }
    internal static readonly ManualLogSource BossRulesLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

    private static ConfigSync? _configSync;
    private readonly Harmony _harmony = new(ModGUID);
    private CustomSyncedValue<string> _syncedAltarYaml = null!;
    private CustomSyncedValue<string> _syncedRulesYaml = null!;
    private FileSystemWatcher? _watcher;
    private float _reloadDueAt = -1f;
    private ConfigEntry<Toggle> _lockConfiguration = null!;
    private IReadOnlyList<AltarConfigurationEntry> _altarEntries = Array.Empty<AltarConfigurationEntry>();
    private BossRuleConfigurationState _rulesConfiguration = BossRuleConfigurationState.Empty;

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
    internal static bool IsSourceOfTruth => ConfigSync.IsSourceOfTruth;
    internal static IReadOnlyList<AltarConfigurationEntry> AltarEntries =>
        Instance?._altarEntries ?? Array.Empty<AltarConfigurationEntry>();
    internal static BossRuleConfigurationState RulesConfiguration =>
        Instance?._rulesConfiguration ?? BossRuleConfigurationState.Empty;
    internal static bool IsRuntimeServer() => ZNet.instance != null && ZNet.instance.IsServer();

    private void Awake()
    {
        EnsureServerSyncInitialized();
        Instance = this;
        Directory.CreateDirectory(ConfigDirectoryPath);
        AltarConfigurationFiles.EnsureDefaultFiles();
        BossRuleConfigurationFiles.EnsureDefaultFile();
        BindConfiguration();

        _syncedAltarYaml = new CustomSyncedValue<string>(ConfigSync, "altar-yaml", "", priority: 50);
        _syncedAltarYaml.ValueChanged += HandleSyncedAltarYamlChanged;
        _syncedRulesYaml = new CustomSyncedValue<string>(ConfigSync, "rules-yaml", "", priority: 60);
        _syncedRulesYaml.ValueChanged += HandleSyncedRulesYamlChanged;
        ConfigSync.SourceOfTruthChanged += HandleSourceOfTruthChanged;

        LoadLocalAltarYamlAndPublish("startup");
        LoadLocalRulesYamlAndPublish("startup");
        _harmony.PatchAll(typeof(BossRulesPlugin).Assembly);
        BossStonePerPlayerRuntime.Initialize();
        BossRulesConsoleCommands.Register();
        InitializeWatcher();
        Config.Save();
    }

    private void Update()
    {
        ProcessQueuedYamlReload();
        AltarRuntime.ProcessPendingAltarSummonMarkers();
        AltarRuntime.ProcessDeferredReapply();
        AltarReferenceGenerator.TryAutoRefreshReferenceConfigurationFile();
        ForsakenPowerRuntime.ProcessDeferredApply();
        BossStonePerPlayerRuntime.EnsureRpcRegistered();
        BossStonePerPlayerRuntime.ProcessPendingResetRequests();
        DespawnRulesManager.ExecuteServerTick();
        BossTamedPressureRuntime.ExecuteServerTick();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (_configSync != null)
        {
            ConfigSync.SourceOfTruthChanged -= HandleSourceOfTruthChanged;
        }

        _syncedAltarYaml.ValueChanged -= HandleSyncedAltarYamlChanged;
        _syncedRulesYaml.ValueChanged -= HandleSyncedRulesYamlChanged;
        _watcher?.Dispose();
        _watcher = null;
        AltarRuntime.Shutdown();
        BossStonePerPlayerRuntime.Shutdown();
        AltarReferenceGenerator.ResetAutoRefresh();
        BossRulesManager.ClearRuntimeState();
        BossRulesRuntime.Reset();
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
        _watcher.Renamed += QueueYamlReload;
        _watcher.EnableRaisingEvents = true;
    }

    private void QueueYamlReload(object sender, FileSystemEventArgs args)
    {
        if (!IsSourceOfTruth)
        {
            return;
        }

        string fileName = Path.GetFileName(args.FullPath);
        if (!string.Equals(fileName, AltarYamlFileName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fileName, RulesYamlFileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _reloadDueAt = Time.realtimeSinceStartup + FileReloadDebounceSeconds;
    }

    private void ProcessQueuedYamlReload()
    {
        if (_reloadDueAt < 0f || Time.realtimeSinceStartup < _reloadDueAt)
        {
            return;
        }

        _reloadDueAt = -1f;
        LoadLocalAltarYamlAndPublish("file change");
        LoadLocalRulesYamlAndPublish("file change");
    }

    private void HandleSourceOfTruthChanged(bool sourceOfTruth)
    {
        if (sourceOfTruth)
        {
            AltarReferenceGenerator.ResetAutoRefresh();
            LoadLocalAltarYamlAndPublish("authority change");
            LoadLocalRulesYamlAndPublish("authority change");
            return;
        }

        ApplyAltarYaml(_syncedAltarYaml.Value ?? "", "server sync");
        ApplyRulesYaml(_syncedRulesYaml.Value ?? "", "server sync");
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

    private bool ApplyAltarYaml(string yaml, string source)
    {
        string content = yaml ?? "";
        BossRulesDebugLog.Client($"Applying altar YAML source={source} bytes={content.Length}.");
        if (!AltarConfiguration.TryParse(content, source, out IReadOnlyList<AltarConfigurationEntry> entries))
        {
            return false;
        }

        _altarEntries = entries;
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

        _rulesConfiguration = configuration;
        BossRulesRuntime.Reload(configuration);
        return true;
    }
}
