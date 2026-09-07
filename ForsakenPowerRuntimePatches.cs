using System;
using HarmonyLib;

namespace BossRules;

[HarmonyPatch(typeof(Player), nameof(Player.ActivateGuardianPower))]
internal static class PlayerActivateGuardianPowerForsakenPowerPatch
{
    private static void Prefix(Player __instance, out float? __state)
    {
        __state = null;
        if (ForsakenPowerRuntime.TryOverrideGuardianPowerAdrenalineGain(__instance, out float originalValue) &&
            !float.IsNaN(originalValue))
        {
            __state = originalValue;
        }
    }

    private static void Postfix(Player __instance, ref float? __state)
    {
        if (__state.HasValue)
        {
            __instance.m_adrenalineGuardianPower = __state.Value;
            __state = null;
        }
    }

    private static Exception? Finalizer(Player __instance, ref float? __state, Exception? __exception)
    {
        if (__state.HasValue)
        {
            __instance.m_adrenalineGuardianPower = __state.Value;
            __state = null;
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(Player), "Update")]
internal static class PlayerUpdateForsakenPowerSelectionPatch
{
    private static void Postfix(Player __instance)
    {
        ForsakenPowerSelectionRuntime.TryRotateSelection(__instance);
    }
}

[HarmonyPatch(typeof(Hud), "UpdateGuardianPower")]
internal static class HudUpdateGuardianPowerForsakenPowerSelectionPatch
{
    private static void Postfix(Player player)
    {
        ForsakenPowerSelectionRuntime.UpdateHudHint(Hud.instance, player);
    }
}
