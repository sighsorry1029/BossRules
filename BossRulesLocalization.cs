using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BossRules;

internal static class BossRulesLocalization
{
    internal const string MessageDespawnStartKey = "bossrules_message_despawn_start";
    internal const string MessageDespawnReminderKey = "bossrules_message_despawn_reminder";
    internal const string MessageDespawnCanceledKey = "bossrules_message_despawn_canceled";
    internal const string MessageBossTamedPressureKey = "bossrules_message_boss_tamed_pressure";
    internal const string MessageForsakenPowerOpenKey = "bossrules_message_forsaken_power_open";
    internal const string MessageForsakenPowerMenuKey = "bossrules_message_forsaken_power_menu";
    internal const string MessageForsakenPowerControlsKey = "bossrules_message_forsaken_power_controls";
    internal const string MessageForsakenPowerSelectedKey = "bossrules_message_forsaken_power_selected";
    internal const string MessageForsakenPowerNoneKey = "bossrules_message_forsaken_power_none";
    internal const string EffectSailingKey = "bossrules_effect_sailing";
    internal const string EffectTamingKey = "bossrules_effect_taming";
    internal const string EffectCollisionKey = "bossrules_effect_collision";

    private static readonly Dictionary<string, string> EnglishDefaults =
        new(StringComparer.Ordinal)
        {
            [MessageDespawnStartKey] =
                "{name} will despawn in {seconds}s unless someone returns.",
            [MessageDespawnReminderKey] =
                "{name} will despawn in {seconds}s.",
            [MessageDespawnCanceledKey] =
                "{name} despawn canceled.",
            [MessageBossTamedPressureKey] =
                "Tamed creatures near a boss are weakened.",
            [MessageForsakenPowerOpenKey] = "Choose power",
            [MessageForsakenPowerMenuKey] = "Forsaken Powers",
            [MessageForsakenPowerControlsKey] = "LMB: select\n{key} / RMB / Esc: close",
            [MessageForsakenPowerSelectedKey] = "Forsaken Power: {name}",
            [MessageForsakenPowerNoneKey] = "No unlocked Forsaken Powers available.",
            [EffectSailingKey] = "Tailwind while sailing",
            [EffectTamingKey] = "Faster taming of nearby creatures",
            [EffectCollisionKey] = "Incoming collision damage: <color=orange>×2</color>"
        };

    private static readonly Dictionary<string, string> EnglishNameCache =
        new(StringComparer.Ordinal);
    private static readonly HashSet<string> MissingEnglishNameCache =
        new(StringComparer.Ordinal);

    internal static string Text(string key)
    {
        string normalizedKey = Localizer.NormalizeKey(key);
        Localization? localization = Localization.instance;
        if (localization != null &&
            Localizer.TranslationsRef(localization).TryGetValue(
                normalizedKey,
                out string currentText) &&
            !string.IsNullOrWhiteSpace(currentText))
        {
            return currentText;
        }

        if (Localizer.TryGetEnglishText(normalizedKey, out string englishText))
        {
            return englishText;
        }

        return EnglishDefaults.TryGetValue(normalizedKey, out string fallback)
            ? fallback
            : normalizedKey;
    }

    internal static string FormatDespawnMessage(
        string messageKey,
        string? nameLocalizationKey,
        string? prefabName,
        int remainingSeconds)
    {
        string displayName = ResolveCharacterName(
            nameLocalizationKey,
            prefabName);
        return Text(messageKey)
            .Replace("{name}", displayName)
            .Replace(
                "{seconds}",
                Math.Max(0, remainingSeconds)
                    .ToString(CultureInfo.InvariantCulture));
    }

    internal static string ResolveCharacterName(
        string? nameLocalizationKey,
        string? prefabName)
    {
        string nameToken = (nameLocalizationKey ?? "").Trim();
        if (nameToken.Length == 0)
        {
            nameToken = ResolveNameTokenFromPrefab(prefabName);
        }

        if (nameToken.Length > 0 && !nameToken.StartsWith("$", StringComparison.Ordinal))
        {
            string literalOrLocalized =
                Localization.instance?.Localize(nameToken) ?? nameToken;
            if (IsUsableTranslation(literalOrLocalized, nameToken))
            {
                return literalOrLocalized;
            }
        }

        string normalizedKey = Localizer.NormalizeKey(nameToken);
        if (normalizedKey.Length > 0)
        {
            Localization? localization = Localization.instance;
            if (localization != null &&
                Localizer.TranslationsRef(localization).TryGetValue(
                    normalizedKey,
                    out string currentText) &&
                IsUsableTranslation(currentText, normalizedKey))
            {
                return currentText;
            }

            if (TryGetEnglishCharacterName(
                    localization,
                    normalizedKey,
                    out string englishText))
            {
                return englishText;
            }
        }

        string normalizedPrefab = Utils.GetPrefabName(prefabName ?? "").Trim();
        return normalizedPrefab.Length > 0 ? normalizedPrefab : "Target";
    }

    private static string ResolveNameTokenFromPrefab(string? prefabName)
    {
        string normalizedPrefab = Utils.GetPrefabName(prefabName ?? "").Trim();
        if (normalizedPrefab.Length == 0)
        {
            return "";
        }

        GameObject? prefab = ZNetScene.instance?.GetPrefab(normalizedPrefab);
        Character? character = prefab?.GetComponent<Character>();
        return character?.m_name?.Trim() ?? "";
    }

    private static bool TryGetEnglishCharacterName(
        Localization? localization,
        string key,
        out string englishText)
    {
        if (EnglishNameCache.TryGetValue(key, out englishText))
        {
            return true;
        }

        englishText = "";
        if (localization == null || MissingEnglishNameCache.Contains(key))
        {
            return false;
        }

        if (Localizer.TryGetLoadedAssemblyEnglishText(
                key,
                out string embeddedEnglish))
        {
            EnglishNameCache[key] = embeddedEnglish;
            englishText = embeddedEnglish;
            return true;
        }

        try
        {
            string translated =
                localization.TranslateSingleId(key, "English") ?? "";
            if (!IsUsableTranslation(translated, key))
            {
                MissingEnglishNameCache.Add(key);
                return false;
            }

            EnglishNameCache[key] = translated;
            englishText = translated;
            return true;
        }
        catch (Exception exception)
        {
            MissingEnglishNameCache.Add(key);
            BossRulesPlugin.BossRulesLogger.LogDebug(
                $"English localization lookup failed for '${key}'. " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static bool IsUsableTranslation(string? text, string key)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text!.Trim();
        string normalizedKey = Localizer.NormalizeKey(key);
        return !string.Equals(value, "$" + normalizedKey, StringComparison.Ordinal) &&
               !string.Equals(value, "[" + normalizedKey + "]", StringComparison.Ordinal) &&
               value.IndexOf("MISSING KEY", StringComparison.OrdinalIgnoreCase) < 0;
    }
}
