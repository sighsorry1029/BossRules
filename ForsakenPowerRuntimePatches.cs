using HarmonyLib;

namespace BossRules;

[HarmonyPatch(typeof(Ship), nameof(Ship.GetWindAngleFactor))]
internal static class ShipGetWindAngleFactorForsakenPowerPatch
{
    private static void Postfix(Ship __instance, ref float __result)
    {
        if (__result < 1f && ForsakenPowerRuntime.HasTailwindPower(__instance))
        {
            __result = 1f;
        }
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.ActivateGuardianPower))]
internal static class PlayerActivateGuardianPowerForsakenPowerPatch
{
    private static void Prefix(Player __instance, out float __state)
    {
        if (!ForsakenPowerRuntime.TryOverrideGuardianPowerAdrenalineGain(__instance, out __state))
        {
            __state = float.NaN;
        }
    }

    private static void Postfix(Player __instance, float __state)
    {
        if (!float.IsNaN(__state))
        {
            __instance.m_adrenalineGuardianPower = __state;
        }
    }
}

[HarmonyPatch(typeof(SE_Stats), nameof(SE_Stats.GetTooltipString))]
internal static class SEStatsGetTooltipStringForsakenPowerPatch
{
    private static void Postfix(SE_Stats __instance, ref string __result)
    {
        if (ForsakenPowerRuntime.TryFormatTooltip(__instance, out string tooltip))
        {
            __result = tooltip;
        }
    }
}
