using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace BossRules;

internal static class BossRulesDebugLog
{
    internal static bool IsClientEnabled => BossRulesConfig.IsClientDebugLogEnabled();

    internal static void Client(string message)
    {
        if (IsClientEnabled)
        {
            BossRulesPlugin.BossRulesLogger.LogInfo($"[ClientDebug] {message}");
        }
    }

    internal static string FormatVector3(Vector3 position)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.##},{1:0.##},{2:0.##}",
            position.x,
            position.y,
            position.z);
    }

    internal static string FormatRefunds(IReadOnlyCollection<DespawnRefundDrop>? refunds)
    {
        if (refunds == null || refunds.Count == 0)
        {
            return "<none>";
        }

        return string.Join(
            ", ",
            refunds
                .Where(refund => refund?.Prefab != null && refund.Amount > 0)
                .Select(refund =>
                    refund.DropPointOverride.HasValue
                        ? $"{refund.Prefab.name}:{refund.Amount}@{FormatVector3(refund.DropPointOverride.Value)}"
                        : $"{refund.Prefab.name}:{refund.Amount}"));
    }
}
