using HarmonyLib;

namespace BossRules;

[HarmonyPatch(typeof(ItemStand), nameof(ItemStand.UseItem))]
internal static class ItemStandUseItemBossStonePerPlayerPatch
{
    private static bool Prefix(ItemStand __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
    {
        if (BossStonePerPlayerRuntime.TryHandleUseItem(__instance, user, item, out bool result))
        {
            __result = result;
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(ItemStand), nameof(ItemStand.HaveAttachment))]
internal static class ItemStandHaveAttachmentBossStonePerPlayerPatch
{
    private static bool Prefix(ItemStand __instance, ref bool __result)
    {
        if (BossStonePerPlayerRuntime.TryOverrideHaveAttachment(__instance, out bool result))
        {
            __result = result;
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(ItemStand), "UpdateVisual")]
internal static class ItemStandUpdateVisualBossStonePerPlayerPatch
{
    private static bool Prefix(ItemStand __instance)
    {
        return !BossStonePerPlayerRuntime.TryOverrideUpdateVisual(__instance);
    }
}
