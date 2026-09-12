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
        RestoreGuardianPowerAdrenaline(__instance, ref __state);
    }

    private static Exception? Finalizer(Player __instance, ref float? __state, Exception? __exception)
    {
        RestoreGuardianPowerAdrenaline(__instance, ref __state);
        return __exception;
    }

    private static void RestoreGuardianPowerAdrenaline(Player player, ref float? state)
    {
        if (state.HasValue)
        {
            ForsakenPowerRuntime.GuardianPowerAdrenalineRef(player) = state.Value;
            state = null;
        }
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
