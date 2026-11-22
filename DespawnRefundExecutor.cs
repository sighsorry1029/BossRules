using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRules;

internal static class DespawnRefundExecutor
{
    internal static bool TryRefund(ZDO source, Vector3 centerPoint, out string error)
    {
        if (!AltarRuntime.TryResolveAltarSummonRefunds(source, out var refunds, out error))
        {
            return false;
        }
        if (refunds.Count == 0) return true;

        ZNetScene scene = ZNetScene.instance;
        if (scene == null)
        {
            error = "The network scene is not ready to create offering drops.";
            return false;
        }

        foreach (DespawnRefundDrop refund in refunds)
        {
            if (scene.GetPrefab(refund.Prefab.name) == null ||
                !refund.Prefab.TryGetComponent(out ZNetView view) || !view.m_persistent ||
                refund.Prefab.GetComponent<ItemDrop>().m_itemData?.m_shared?.m_maxStackSize is not > 0)
            {
                error = $"Offering item '{refund.Prefab.name}' cannot create a persistent stack.";
                return false;
            }
        }

        List<GameObject> spawned = new();
        try
        {
            foreach (DespawnRefundDrop refund in refunds)
            {
                SpawnStackedDrops(refund.Prefab, refund.Amount, refund.DropPointOverride ?? centerPoint, spawned);
            }
            source.SetOwner(ZDOMan.GetSessionID());
            AltarRuntime.ClearPaidAltarRefunds(source);
            return true;
        }
        catch (Exception ex)
        {
            // No player/network tick intervenes in this batch. Remove only our own
            // new objects before retrying the original, still unpaid payload.
            foreach (GameObject item in spawned)
            {
                if (item == null) continue;
                ZDO? zdo = item.GetComponent<ZNetView>()?.GetZDO();
                if (zdo != null)
                {
                    DespawnRulesManager.QueueServerDestruction(zdo);
                }
                else
                {
                    UnityEngine.Object.Destroy(item);
                }
            }
            // Leave valid views registered for the native destruction callback.
            foreach (GameObject item in spawned)
            {
                if (item != null) item.SetActive(false);
            }
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static void SpawnStackedDrops(GameObject itemPrefab, int remaining, Vector3 centerPoint, List<GameObject> batch)
    {
        // ItemData.Save writes each stack count as an unsigned 16-bit value.
        int maxStackSize = Math.Min(ushort.MaxValue, itemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
        if (maxStackSize <= 0) throw new InvalidOperationException($"Invalid stack limit for '{itemPrefab.name}'.");
        while (remaining > 0)
        {
            int stackSize = Math.Min(remaining, maxStackSize);
            remaining -= stackSize;
            GameObject spawned = UnityEngine.Object.Instantiate(itemPrefab, GetDropPosition(centerPoint), UnityEngine.Random.rotation);
            batch.Add(spawned);
            if (!spawned.TryGetComponent(out ItemDrop itemDrop) || !spawned.TryGetComponent(out ZNetView view) ||
                !view.IsValid() || !view.IsOwner() || !view.GetZDO().Persistent)
            {
                throw new InvalidOperationException($"Offering item '{itemPrefab.name}' did not create an owned persistent drop.");
            }
            itemDrop.m_itemData.m_worldLevel = (byte)Mathf.Clamp(Game.m_worldLevel, 0, byte.MaxValue);
            itemDrop.SetStack(stackSize); // Also writes the native item data to the ZDO.
            if (itemDrop.m_itemData.m_stack != stackSize)
            {
                throw new InvalidOperationException($"Offering item '{itemPrefab.name}' did not accept a stack of {stackSize}.");
            }

            Rigidbody? body = spawned.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.up * UnityEngine.Random.Range(4f, 6f) +
                                      UnityEngine.Random.insideUnitSphere * 2f;
            }
        }
    }

    private static Vector3 GetDropPosition(Vector3 centerPoint)
    {
        Vector2 offset = UnityEngine.Random.insideUnitCircle * 1.5f;
        return centerPoint + new Vector3(offset.x, 1f, offset.y);
    }
}
