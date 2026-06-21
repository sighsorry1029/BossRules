using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace BossRules;

internal static partial class ForsakenPowerRuntime
{
    // Tooltip formatting mirrors the supported SE_Stats fields owned by forsaken power rules.
    internal static bool TryFormatTooltip(SE_Stats? stats, out string tooltip)
    {
        tooltip = "";
        if (stats == null || !BossRulesConfig.IsForsakenPowerRulesEnabled())
        {
            return false;
        }

        lock (Sync)
        {
            if (!TooltipPowerHashes.Contains(stats.NameHash()))
            {
                return false;
            }
        }

        tooltip = FormatTooltip(stats);
        return true;
    }

    private static string FormatTooltip(SE_Stats stats)
    {
        StringBuilder builder = new(256);
        if (!string.IsNullOrEmpty(stats.m_tooltip))
        {
            builder.AppendFormat("{0}\n\n", stats.m_tooltip);
        }

        AppendDamagePercentLines(builder, stats.m_percentigeDamageModifiers);
        AppendIncomingDamageModifierLines(builder, stats.m_mods);

        AppendPlainValueLine(builder, "$item_armor", stats.m_addArmor);
        AppendPercentLine(builder, "$item_armor", stats.m_armorMultiplier);
        AppendPercentLine(builder, "$se_stagger", 0f - stats.m_staggerModifier);

        AppendPercentLine(builder, "$item_movement_modifier", stats.m_speedModifier);
        AppendPercentLine(builder, "$item_swimspeed_modifier", stats.m_swimSpeedModifier);
        if (stats.m_jumpModifier.y != 0f && stats.m_jumpModifier.y != 1f && stats.m_jumpModifier.y > -1f && stats.m_jumpModifier.x > -1f)
        {
            AppendPercentLine(builder, "$se_jumpheight", stats.m_jumpModifier.y);
        }
        if ((stats.m_jumpModifier.x != 0f || stats.m_jumpModifier.z != 0f) && stats.m_jumpModifier.y > -1f && stats.m_jumpModifier.x > -1f)
        {
            AppendPercentLine(builder, "$se_jumplength", Mathf.Max(stats.m_jumpModifier.x, stats.m_jumpModifier.z));
        }

        AppendPercentLine(builder, "$se_runstamina", stats.m_runStaminaDrainModifier);
        AppendPercentLine(builder, "$se_jumpstamina", stats.m_jumpStaminaUseModifier);
        AppendPercentLine(builder, "$se_attackstamina", stats.m_attackStaminaUseModifier);
        AppendPercentLine(builder, "$se_blockstamina", stats.m_blockStaminaUseModifier);
        AppendBlockStaminaFlatLine(builder, stats.m_blockStaminaUseFlatValue);
        AppendPercentLine(builder, "$se_dodgestamina", stats.m_dodgeStaminaUseModifier);
        AppendPercentLine(builder, "$se_swimstamina", stats.m_swimStaminaUseModifier);
        AppendPercentLine(builder, "$se_sneakstamina", stats.m_sneakStaminaUseModifier);

        AppendRegenPercentLine(builder, "$se_healthregen", stats.m_healthRegenMultiplier);
        AppendRegenPercentLine(builder, "$se_staminaregen", stats.m_staminaRegenMultiplier);
        AppendRegenPercentLine(builder, "$se_eitrregen", stats.m_eitrRegenMultiplier);
        AppendPercentLine(builder, "$se_adrenaline", stats.m_adrenalineModifier);

        AppendPlainValueLine(builder, "$se_max_carryweight", stats.m_addMaxCarryWeight);
        AppendSkillLine(builder, stats.m_skillLevel, stats.m_skillLevelModifier);
        AppendSkillLine(builder, stats.m_skillLevel2, stats.m_skillLevelModifier2);

        if (stats.m_ttl > 1f)
        {
            builder.AppendFormat("$se_ttl: <color=orange>{0}</color>\n", stats.m_ttl.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void AppendDamagePercentLines(StringBuilder builder, HitData.DamageTypes damageTypes)
    {
        AppendPercentLine(builder, "$inventory_blunt", damageTypes.m_blunt);
        AppendPercentLine(builder, "$inventory_slash", damageTypes.m_slash);
        AppendPercentLine(builder, "$inventory_pierce", damageTypes.m_pierce);
        AppendPercentLine(builder, "$inventory_chop", damageTypes.m_chop);
        AppendPercentLine(builder, "$inventory_pickaxe", damageTypes.m_pickaxe);
        AppendPercentLine(builder, "$inventory_fire", damageTypes.m_fire);
        AppendPercentLine(builder, "$inventory_frost", damageTypes.m_frost);
        AppendPercentLine(builder, "$inventory_lightning", damageTypes.m_lightning);
        AppendPercentLine(builder, "$inventory_poison", damageTypes.m_poison);
        AppendPercentLine(builder, "$inventory_spirit", damageTypes.m_spirit);
    }

    private static void AppendIncomingDamageModifierLines(StringBuilder builder, List<HitData.DamageModPair>? mods)
    {
        if (mods == null || mods.Count == 0)
        {
            return;
        }

        string text = SE_Stats.GetDamageModifiersTooltipString(mods);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (text[0] == '\n')
        {
            text = text.Substring(1);
        }

        builder.Append(text);
        if (text[text.Length - 1] != '\n')
        {
            builder.Append('\n');
        }
    }

    private static void AppendPlainValueLine(StringBuilder builder, string labelToken, float value)
    {
        if (value == 0f)
        {
            return;
        }

        builder.AppendFormat(
            "{0}: <color=orange>{1}</color>\n",
            labelToken,
            value.ToString("+0;-0", CultureInfo.InvariantCulture));
    }

    private static void AppendPercentLine(StringBuilder builder, string labelToken, float value)
    {
        if (value == 0f)
        {
            return;
        }

        builder.AppendFormat(
            "{0}: <color=orange>{1}%</color>\n",
            labelToken,
            (value * 100f).ToString("+0;-0", CultureInfo.InvariantCulture));
    }

    private static void AppendRegenPercentLine(StringBuilder builder, string labelToken, float multiplier)
    {
        if (multiplier == 1f)
        {
            return;
        }

        AppendPercentLine(builder, labelToken, multiplier - 1f);
    }

    private static void AppendBlockStaminaFlatLine(StringBuilder builder, float value)
    {
        if (value == 0f)
        {
            return;
        }

        if (value > 0f)
        {
            builder.AppendFormat("$se_blockstaminaflat: <color=orange>{0}</color>\n", value.ToString("+0;-0", CultureInfo.InvariantCulture));
            return;
        }

        builder.AppendFormat("$se_blockstaminaflat_minus: <color=orange>{0}</color>\n", (0f - value).ToString("+0;-0", CultureInfo.InvariantCulture));
    }

    private static void AppendSkillLine(StringBuilder builder, Skills.SkillType skill, float value)
    {
        if (skill == Skills.SkillType.None)
        {
            return;
        }

        string label = $"$skill_{skill.ToString().ToLowerInvariant()}";
        if (Localization.instance != null)
        {
            label = Localization.instance.Localize(label);
        }

        builder.AppendFormat("{0} <color=orange>{1}</color>\n", label, value.ToString("+0;-0", CultureInfo.InvariantCulture));
    }
}
