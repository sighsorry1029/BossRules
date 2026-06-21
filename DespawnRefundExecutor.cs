using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRules;

internal static class DespawnRefundExecutor
{
    internal static bool TryExecuteRefunds(Vector3 centerPoint, IReadOnlyCollection<DespawnRefundDrop> refunds)
    {
        if (refunds == null || refunds.Count == 0)
        {
            BossRulesDebugLog.Client($"Despawn refund skipped at {BossRulesDebugLog.FormatVector3(centerPoint)}: no refunds.");
            return true;
        }

        BossRulesDebugLog.Client($"Despawn refund executing fallback={BossRulesDebugLog.FormatVector3(centerPoint)}: {BossRulesDebugLog.FormatRefunds(refunds)}.");
        bool anyDropped = false;
        foreach (DespawnRefundDrop refund in refunds)
        {
            if (refund == null || refund.Prefab == null || refund.Amount <= 0)
            {
                continue;
            }

            Vector3 dropPoint = refund.DropPointOverride ?? centerPoint;
            SpawnStackedDrops(refund.Prefab, refund.Amount, dropPoint);
            anyDropped = true;
        }

        return anyDropped;
    }

    private static void SpawnStackedDrops(GameObject itemPrefab, int amount, Vector3 centerPoint)
    {
        int remaining = Math.Max(1, amount);
        int maxStackSize = GetMaxStackSize(itemPrefab);
        while (remaining > 0)
        {
            int stackSize = Math.Min(remaining, maxStackSize);
            remaining -= stackSize;
            GameObject spawned = UnityEngine.Object.Instantiate(itemPrefab, GetDropPosition(centerPoint), UnityEngine.Random.rotation);
            if (spawned.TryGetComponent(out ItemDrop itemDrop))
            {
                itemDrop.SetStack(stackSize);
                itemDrop.m_itemData.m_worldLevel = (byte)Mathf.Clamp(Game.m_worldLevel, 0, byte.MaxValue);
            }

            Rigidbody? body = spawned.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.up * UnityEngine.Random.Range(4f, 6f) +
                                      UnityEngine.Random.insideUnitSphere * 2f;
            }
        }
    }

    private static int GetMaxStackSize(GameObject itemPrefab)
    {
        if (itemPrefab.TryGetComponent(out ItemDrop itemDrop))
        {
            return Mathf.Max(1, itemDrop.m_itemData.m_shared.m_maxStackSize);
        }

        return 1;
    }

    private static Vector3 GetDropPosition(Vector3 centerPoint)
    {
        Vector2 offset = UnityEngine.Random.insideUnitCircle * 1.5f;
        return centerPoint + new Vector3(offset.x, 1f, offset.y);
    }
}
