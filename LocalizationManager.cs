using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using YamlDotNet.Serialization;

namespace BossRules;

// Adapted for BossRules from AzumattDev/LocalizationManager 1.4.0 (MIT-0).
// Source: https://github.com/AzumattDev/LocalizationManager
internal static class Localizer
{
    private const string EnglishLanguage = "English";
    private static readonly string[] FileExtensions = { ".json", ".yml" };
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreFields()
        .Build();
    private static readonly Dictionary<string, Dictionary<string, string>?> EmbeddedTranslationCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string>? _loadedAssemblyEnglishTranslations;
    private static BossRulesPlugin? _plugin;
    private static Localization? _appliedLocalization;
    private static string _appliedLanguage = "";
    private static DateTime _nextDeferredLoadAttemptUtc = DateTime.MinValue;
    private static bool _reportedMissingEnglish;

    internal static void Initialize(BossRulesPlugin plugin)
    {
        _plugin = plugin;
        if (ReadEmbeddedTranslations(EnglishLanguage) == null)
        {
            ReportMissingEnglish();
        }
    }

    internal static void Shutdown()
    {
        _plugin = null;
        _appliedLocalization = null;
        _appliedLanguage = "";
        _nextDeferredLoadAttemptUtc = DateTime.MinValue;
        _reportedMissingEnglish = false;
        EmbeddedTranslationCache.Clear();
        _loadedAssemblyEnglishTranslations = null;
    }

    internal static void ProcessDeferredLoad()
    {
        Localization? localization = Localization.m_instance;
        if (_plugin == null ||
            localization == null ||
            localization.m_translations.Count == 0 ||
            DateTime.UtcNow < _nextDeferredLoadAttemptUtc)
        {
            return;
        }

        string selectedLanguage = localization.GetSelectedLanguage();
        if (ReferenceEquals(localization, _appliedLocalization) &&
            string.Equals(
                selectedLanguage,
                _appliedLanguage,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ApplyLanguage(localization, selectedLanguage);
    }

    internal static void ApplyLanguage(Localization localization, string? language)
    {
        if (_plugin == null || localization == null)
        {
            return;
        }

        string selectedLanguage = string.IsNullOrWhiteSpace(language)
            ? EnglishLanguage
            : language!;
        try
        {
            Dictionary<string, string>? embeddedEnglish = ReadEmbeddedTranslations(EnglishLanguage);
            if (embeddedEnglish == null)
            {
                ReportMissingEnglish();
                MarkLanguageApplied(localization, selectedLanguage);
                return;
            }

            Dictionary<string, string> translations =
                new(embeddedEnglish, StringComparer.Ordinal);
            Dictionary<string, string> externalFiles = FindExternalLocalizationFiles();
            bool selectedLanguageLocalizationApplied = false;

            if (!string.Equals(selectedLanguage, EnglishLanguage, StringComparison.OrdinalIgnoreCase))
            {
                if (externalFiles.TryGetValue(selectedLanguage, out string selectedLanguageFile) &&
                    TryReadExternalTranslations(
                        selectedLanguageFile,
                        out Dictionary<string, string>? externalSelectedLanguage) &&
                    externalSelectedLanguage != null)
                {
                    OverlayTranslations(translations, externalSelectedLanguage);
                    selectedLanguageLocalizationApplied = true;
                }

                if (!selectedLanguageLocalizationApplied &&
                    ReadEmbeddedTranslations(selectedLanguage) is { } embeddedSelectedLanguage)
                {
                    OverlayTranslations(translations, embeddedSelectedLanguage);
                    selectedLanguageLocalizationApplied = true;
                }
            }

            if (!selectedLanguageLocalizationApplied &&
                externalFiles.TryGetValue(EnglishLanguage, out string englishFile) &&
                TryReadExternalTranslations(
                    englishFile,
                    out Dictionary<string, string>? externalEnglish) &&
                externalEnglish != null)
            {
                OverlayTranslations(translations, externalEnglish);
            }

            foreach (KeyValuePair<string, string> entry in translations)
            {
                localization.AddWord(entry.Key, entry.Value);
            }

            localization.m_cache.EvictAll();
            MarkLanguageApplied(localization, selectedLanguage);
        }
        catch (Exception exception)
        {
            _nextDeferredLoadAttemptUtc = DateTime.UtcNow.AddSeconds(10);
            BossRulesPlugin.BossRulesLogger.LogError(
                $"Failed to load BossRules localization for '{selectedLanguage}'. " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void MarkLanguageApplied(
        Localization localization,
        string language)
    {
        _appliedLocalization = localization;
        _appliedLanguage = language;
        _nextDeferredLoadAttemptUtc = DateTime.MinValue;
    }

    internal static bool TryGetEnglishText(string key, out string text)
    {
        text = "";
        string normalizedKey = NormalizeKey(key);
        return ReadEmbeddedTranslations(EnglishLanguage) is { } english &&
               english.TryGetValue(normalizedKey, out text) &&
               !string.IsNullOrWhiteSpace(text);
    }

    internal static bool TryGetLoadedAssemblyEnglishText(
        string key,
        out string text)
    {
        string normalizedKey = NormalizeKey(key);
        Dictionary<string, string> englishTranslations =
            _loadedAssemblyEnglishTranslations ??=
                BuildLoadedAssemblyEnglishTranslations();
        return englishTranslations.TryGetValue(normalizedKey, out text) &&
               !string.IsNullOrWhiteSpace(text);
    }

    internal static byte[]? ReadEmbeddedFileBytes(
        string resourceFileName,
        Assembly? containingAssembly = null)
    {
        containingAssembly ??= typeof(BossRulesPlugin).Assembly;
        string? resourceName = containingAssembly
            .GetManifestResourceNames()
            .FirstOrDefault(name =>
                name.EndsWith(resourceFileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName == null)
        {
            return null;
        }

        using Stream? stream = containingAssembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return null;
        }

        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return memory.Length == 0 ? null : memory.ToArray();
    }

    private static Dictionary<string, string> FindExternalLocalizationFiles()
    {
        Dictionary<string, string> localizationFiles =
            new(StringComparer.OrdinalIgnoreCase);
        string pluginName = _plugin?.Info.Metadata.Name ?? BossRulesPlugin.ModName;
        string searchRoot = Path.GetDirectoryName(Paths.PluginPath) ?? Paths.PluginPath;
        if (string.IsNullOrWhiteSpace(searchRoot) || !Directory.Exists(searchRoot))
        {
            return localizationFiles;
        }

        IEnumerable<string> candidates;
        try
        {
            candidates = Directory
                .EnumerateFiles(searchRoot, $"{pluginName}.*", SearchOption.AllDirectories)
                .Where(path => FileExtensions.Contains(
                    Path.GetExtension(path),
                    StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception)
        {
            BossRulesPlugin.BossRulesLogger.LogWarning(
                $"Failed to scan BepInEx for BossRules localization files. " +
                $"{exception.GetType().Name}: {exception.Message}");
            return localizationFiles;
        }

        string filePrefix = pluginName + ".";
        foreach (string file in candidates)
        {
            string nameWithoutExtension = Path.GetFileNameWithoutExtension(file);
            if (!nameWithoutExtension.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string language = nameWithoutExtension.Substring(filePrefix.Length);
            if (string.IsNullOrWhiteSpace(language))
            {
                continue;
            }

            if (localizationFiles.TryGetValue(language, out string existing))
            {
                BossRulesPlugin.BossRulesLogger.LogWarning(
                    $"Duplicate BossRules localization for '{language}' ignored: {file}. " +
                    $"Using {existing}.");
                continue;
            }

            localizationFiles[language] = file;
        }

        return localizationFiles;
    }

    private static bool TryReadExternalTranslations(
        string path,
        out Dictionary<string, string>? translations)
    {
        translations = null;
        try
        {
            string data = File.ReadAllText(path, Encoding.UTF8);
            return TryDeserialize(
                data,
                $"external localization file {path}",
                out translations);
        }
        catch (Exception exception)
        {
            BossRulesPlugin.BossRulesLogger.LogWarning(
                $"Failed to read BossRules localization file {path}. " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static Dictionary<string, string>? ReadEmbeddedTranslations(string language)
    {
        if (EmbeddedTranslationCache.TryGetValue(
                language,
                out Dictionary<string, string>? cached))
        {
            return cached;
        }

        foreach (string extension in FileExtensions)
        {
            byte[]? bytes = ReadEmbeddedFileBytes(
                $"translations.{language}{extension}");
            if (bytes == null)
            {
                continue;
            }

            if (TryDeserialize(
                    Encoding.UTF8.GetString(bytes),
                    $"embedded {language} localization",
                    out Dictionary<string, string>? translations))
            {
                EmbeddedTranslationCache[language] = translations;
                return translations;
            }
        }

        EmbeddedTranslationCache[language] = null;
        return null;
    }

    private static Dictionary<string, string> BuildLoadedAssemblyEnglishTranslations()
    {
        Dictionary<string, string> translations =
            new(StringComparer.Ordinal);
        Assembly[] assemblies = AppDomain.CurrentDomain
            .GetAssemblies()
            .OrderBy(
                assembly => assembly.FullName ?? assembly.GetName().Name,
                StringComparer.Ordinal)
            .ToArray();
        foreach (Assembly assembly in assemblies)
        {
            string[] resourceNames;
            try
            {
                resourceNames = assembly
                    .GetManifestResourceNames()
                    .Where(IsEnglishTranslationResource)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
            }
            catch
            {
                continue;
            }

            foreach (string resourceName in resourceNames)
            {
                try
                {
                    using Stream? stream =
                        assembly.GetManifestResourceStream(resourceName);
                    if (stream == null)
                    {
                        continue;
                    }

                    using StreamReader reader = new(
                        stream,
                        Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: true);
                    Dictionary<string, string>? resourceTranslations =
                        Deserializer.Deserialize<Dictionary<string, string>?>(
                            reader.ReadToEnd());
                    if (resourceTranslations == null)
                    {
                        continue;
                    }

                    foreach (KeyValuePair<string, string> entry in resourceTranslations)
                    {
                        string normalizedKey = NormalizeKey(entry.Key);
                        if (normalizedKey.Length > 0 &&
                            !string.IsNullOrWhiteSpace(entry.Value) &&
                            !translations.ContainsKey(normalizedKey))
                        {
                            translations[normalizedKey] = entry.Value;
                        }
                    }
                }
                catch
                {
                    // Third-party resources are best-effort English fallback data.
                }
            }
        }

        return translations;
    }

    private static bool IsEnglishTranslationResource(string resourceName)
    {
        return FileExtensions.Any(extension =>
            resourceName.EndsWith(
                $"translations.{EnglishLanguage}{extension}",
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryDeserialize(
        string data,
        string source,
        out Dictionary<string, string>? translations)
    {
        translations = null;
        try
        {
            Dictionary<string, string>? parsed =
                Deserializer.Deserialize<Dictionary<string, string>?>(data);
            if (parsed == null)
            {
                throw new InvalidDataException(
                    "The document root must be a translation mapping.");
            }

            translations = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> entry in parsed)
            {
                string key = NormalizeKey(entry.Key);
                if (key.Length == 0 || string.IsNullOrWhiteSpace(entry.Value))
                {
                    continue;
                }

                translations[key] = entry.Value;
            }

            return true;
        }
        catch (Exception exception)
        {
            BossRulesPlugin.BossRulesLogger.LogWarning(
                $"Failed to parse {source}. " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static void OverlayTranslations(
        Dictionary<string, string> target,
        IReadOnlyDictionary<string, string> overlay)
    {
        foreach (KeyValuePair<string, string> entry in overlay)
        {
            if (!string.IsNullOrWhiteSpace(entry.Key) &&
                !string.IsNullOrWhiteSpace(entry.Value))
            {
                target[NormalizeKey(entry.Key)] = entry.Value;
            }
        }
    }

    private static string NormalizeKey(string? key)
    {
        string normalized = (key ?? "").Trim();
        return normalized.StartsWith("$", StringComparison.Ordinal)
            ? normalized.Substring(1)
            : normalized;
    }

    private static void ReportMissingEnglish()
    {
        if (_reportedMissingEnglish)
        {
            return;
        }

        _reportedMissingEnglish = true;
        BossRulesPlugin.BossRulesLogger.LogError(
            "BossRules embedded English localization is missing. " +
            "Expected translations/English.json or translations/English.yml.");
    }
}

[HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
internal static class BossRulesLocalizationSetupLanguagePatch
{
    [HarmonyPostfix]
    private static void Postfix(Localization __instance, string language)
    {
        if (ReferenceEquals(__instance, Localization.m_instance))
        {
            Localizer.ApplyLanguage(__instance, language);
        }
    }
}
