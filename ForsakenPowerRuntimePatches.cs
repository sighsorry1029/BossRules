using HarmonyLib;

namespace BossRules;

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
