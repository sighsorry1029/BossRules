using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BossRules;

[HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.SpawnLocation), new Type[]
{
    typeof(ZoneSystem.ZoneLocation),
    typeof(int),
    typeof(Vector3),
    typeof(Quaternion),
    typeof(ZoneSystem.SpawnMode),
    typeof(List<GameObject>)
})]
[HarmonyAfter("expand_world_data")]
internal static class ZoneSystemSpawnLocationAltarPatch
{
    private sealed class SpawnLocationState
    {
        public string PrefabName { get; set; } = "";
    }

    private static void Prefix(ZoneSystem.ZoneLocation location, ref SpawnLocationState? __state)
    {
        string prefabName = AltarLocationResolver.GetLocationSpawnContextPrefabName(location);
        if (prefabName.Length == 0)
        {
            return;
        }

        __state = new SpawnLocationState
        {
            PrefabName = prefabName
        };
    }

    private static void Postfix(GameObject __result, SpawnLocationState? __state)
    {
        if (__result == null || __state == null)
        {
            return;
        }

        AltarRuntime.ReconcileSpawnedLocationRoot(__result, __state.PrefabName);
    }
}

[HarmonyPatch(typeof(Location), nameof(Location.Awake))]
internal static class LocationAwakeAltarPatch
{
    private static void Postfix(Location __instance)
    {
        AltarRuntime.RegisterLocation(__instance);
    }
}

[HarmonyPatch(typeof(Location), "OnDestroy")]
internal static class LocationOnDestroyAltarPatch
{
    private static void Prefix(Location __instance)
    {
        AltarRuntime.UnregisterLocation(__instance);
    }
}

[HarmonyPatch(typeof(LocationProxy), "SpawnLocation")]
internal static class LocationProxySpawnLocationAltarPatch
{
    private static readonly AccessTools.FieldRef<LocationProxy, GameObject> InstanceRef =
        AccessTools.FieldRefAccess<LocationProxy, GameObject>("m_instance");

    private static void Postfix(LocationProxy __instance, bool __result)
    {
        if (!__result || __instance == null)
        {
            return;
        }

        if (!AltarLocationResolver.TryResolveLocationProxyPrefabName(__instance, out string prefabName) &&
            !AltarLocationResolver.TryResolveZoneLocationPrefabName(__instance.transform.position, out prefabName))
        {
            return;
        }

        AltarLocationResolver.RecordLocationProxyResolvedPrefab(__instance, prefabName);
        AltarRuntime.ReconcileSpawnedLocationRoot(InstanceRef(__instance), prefabName);
    }
}

[HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.Awake))]
internal static class OfferingBowlAwakeAltarPatch
{
    private static void Postfix(OfferingBowl __instance)
    {
        OfferingBowlHoverInfoFormatter.RegisterOfferingBowl(__instance);
        AltarRuntime.ReconcileLooseOfferingBowl(__instance);
    }
}

[HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.GetHoverText))]
internal static class OfferingBowlGetHoverTextAltarPatch
{
    private static void Postfix(OfferingBowl __instance, ref string __result)
    {
        if (!BossRulesConfig.ShouldShowOfferingBowlHoverInfo())
        {
            return;
        }

        AltarRuntime.ReconcileLooseOfferingBowl(__instance);
        __result = OfferingBowlHoverInfoFormatter.AppendInfo(__result, __instance);
    }
}

[HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.Interact))]
internal static class OfferingBowlInteractAltarPatch
{
    private static bool Prefix(OfferingBowl __instance, Humanoid user, bool hold, ref bool __result)
    {
        if (!BossRulesConfig.IsAltarRulesEnabled() || hold || !__instance.m_useItemStands)
        {
            return true;
        }

        AltarRuntime.ReconcileLooseOfferingBowl(__instance);
        AltarRuntime.OfferingBowlBlockResult blockResult = AltarRuntime.EvaluateOfferingBowlBlock(__instance);
        if (!blockResult.Blocked)
        {
            return true;
        }

        __result = true;
        AltarRuntime.NotifyOfferingBowlBlocked(__instance, user, blockResult);
        return false;
    }
}

[HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.UseItem))]
internal static class OfferingBowlUseItemAltarPatch
{
    private static bool Prefix(OfferingBowl __instance, Humanoid user, ref bool __result)
    {
        if (!BossRulesConfig.IsAltarRulesEnabled() || __instance.m_useItemStands)
        {
            return true;
        }

        AltarRuntime.ReconcileLooseOfferingBowl(__instance);
        AltarRuntime.OfferingBowlBlockResult blockResult = AltarRuntime.EvaluateOfferingBowlBlock(__instance);
        if (!blockResult.Blocked)
        {
            return true;
        }

        __result = true;
        AltarRuntime.NotifyOfferingBowlBlocked(__instance, user, blockResult);
        return false;
    }
}

[HarmonyPatch(typeof(OfferingBowl), "RPC_SpawnBoss")]
internal static class OfferingBowlRpcSpawnBossAltarPatch
{
    private static bool Prefix(OfferingBowl __instance)
    {
        if (ZNet.instance == null)
        {
            return true;
        }

        if (ZNet.instance.IsServer() &&
            BossRulesConfig.IsAltarRulesEnabled() &&
            AltarRuntime.EvaluateOfferingBowlBlock(__instance).Blocked)
        {
            return false;
        }

        if (BossRulesConfig.ShouldCaptureAltarSpawnRefunds())
        {
            AltarRuntime.PrepareOfferingBowlRefundPayload(__instance);
        }

        return true;
    }
}

[HarmonyPatch(typeof(OfferingBowl), "DelayedSpawnBoss")]
internal static class OfferingBowlDelayedSpawnBossAltarPatch
{
    private static readonly AccessTools.FieldRef<OfferingBowl, Vector3> BossSpawnPointRef =
        AccessTools.FieldRefAccess<OfferingBowl, Vector3>("m_bossSpawnPoint");

    private static void Prefix(OfferingBowl __instance)
    {
        if (ZNet.instance != null && BossRulesConfig.ShouldCaptureAltarSpawnRefunds())
        {
            AltarRuntime.BeginOfferingBowlBossSpawnAttempt(__instance, BossSpawnPointRef(__instance));
        }
    }

    private static void Postfix(OfferingBowl __instance)
    {
        if (ZNet.instance != null && BossRulesConfig.ShouldCaptureAltarSpawnRefunds())
        {
            AltarRuntime.FinalizeOfferingBowlBossSpawnAttempt(__instance, BossSpawnPointRef(__instance));
        }
    }
}

[HarmonyPatch(typeof(OfferingBowl), "SpawnBoss")]
internal static class OfferingBowlSpawnBossAltarPatch
{
    private static void Prefix(OfferingBowl __instance, Vector3 spawnPoint)
    {
        if (ZNet.instance != null && BossRulesConfig.ShouldCaptureAltarSpawnRefunds())
        {
            AltarRuntime.PrepareAndQueueOfferingBowlRefundPayload(__instance, spawnPoint);
        }
    }

    private static void Postfix(OfferingBowl __instance)
    {
        if (BossRulesConfig.IsAltarRulesEnabled())
        {
            AltarRuntime.MarkOfferingBowlUsed(__instance);
        }
    }
}

[HarmonyPatch(typeof(OfferingBowl), "SpawnItem")]
internal static class OfferingBowlSpawnItemAltarPatch
{
    private static void Postfix(OfferingBowl __instance, bool __result)
    {
        if (BossRulesConfig.IsAltarRulesEnabled() && __result)
        {
            AltarRuntime.MarkOfferingBowlUsed(__instance);
        }
    }
}

[HarmonyPatch(typeof(ItemStand), nameof(ItemStand.Awake))]
internal static class ItemStandAwakeAltarPatch
{
    private static void Postfix(ItemStand __instance)
    {
        AltarItemStandHoverInfoFormatter.RegisterItemStand(__instance);
        AltarRuntime.ReconcileLooseItemStand(__instance);
    }
}

[HarmonyPatch(typeof(ItemStand), nameof(ItemStand.GetHoverText))]
internal static class ItemStandGetHoverTextAltarPatch
{
    private static void Postfix(ItemStand __instance, ref string __result)
    {
        if (!BossRulesConfig.ShouldShowOfferingBowlHoverInfo())
        {
            return;
        }

        __result = AltarItemStandHoverInfoFormatter.AppendInfo(__result, __instance);
    }
}

[HarmonyPatch(typeof(ItemStand), nameof(ItemStand.Interact))]
internal static class ItemStandInteractAltarPatch
{
    private static void Prefix(ItemStand __instance)
    {
        AltarRuntime.ReconcileLooseItemStand(__instance);
    }
}

[HarmonyPatch(typeof(ItemStand), nameof(ItemStand.UseItem))]
internal static class ItemStandUseItemAltarPatch
{
    private static void Prefix(ItemStand __instance)
    {
        AltarRuntime.ReconcileLooseItemStand(__instance);
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.Awake))]
internal static class CharacterAwakeBossRulesPatch
{
    private static void Postfix(Character __instance)
    {
        AltarRuntime.TryMarkAltarSummonedCharacter(__instance);
        BossRulesManager.TrackBossCharacter(__instance);
        DespawnRulesManager.TryTrackLoadedDespawnTarget(__instance);
    }
}

[HarmonyPatch(typeof(Character), "OnDestroy")]
internal static class CharacterOnDestroyBossRulesPatch
{
    private static void Prefix(Character __instance)
    {
        BossRulesManager.UntrackBossCharacter(__instance);
    }
}

[HarmonyPatch(typeof(CreatureSpawner), "UpdateSpawner")]
internal static class CreatureSpawnerUpdateSpawnerDuplicateBlockPatch
{
    private static bool Prefix(CreatureSpawner __instance)
    {
        return !CreatureSpawnerDuplicateBlockRuntime.ShouldBlockUpdate(__instance);
    }
}

[HarmonyPatch(typeof(CreatureSpawner), "OnDestroy")]
internal static class CreatureSpawnerOnDestroyDuplicateBlockPatch
{
    private static void Prefix(CreatureSpawner __instance)
    {
        CreatureSpawnerDuplicateBlockRuntime.RemoveSpawner(__instance);
    }
}

[HarmonyPatch(typeof(Character), "RPC_Damage")]
internal static class CharacterRpcDamageBossTamedPressurePatch
{
    private static void Prefix(Character __instance, HitData hit)
    {
        BossTamedPressureRuntime.ApplyDamageMultipliers(__instance, hit);
    }
}

[HarmonyPatch]
internal static class ZDOManCreateNewZdoDespawnPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method(typeof(ZDOMan), nameof(ZDOMan.CreateNewZDO), new[] { typeof(ZDOID), typeof(Vector3), typeof(int) });
    }

    private static void Postfix(int prefabHashIn, ZDO __result)
    {
        AltarRuntime.TryMarkCreatedAltarSummonZdo(prefabHashIn, __result);
        DespawnRulesManager.QueueCreatedDespawnTarget(prefabHashIn, __result);
    }
}

[HarmonyPatch(typeof(ZNetView), nameof(ZNetView.ResetZDO))]
internal static class ZNetViewResetZdoDespawnPatch
{
    private static void Prefix(ZNetView __instance)
    {
        DespawnRulesManager.TryPersistDespawnCountdownBeforeResetZdo(__instance);
    }
}
