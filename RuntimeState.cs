using System.Collections.Generic;
using UnityEngine;

namespace BossRules;

internal sealed class OfferingBowlSnapshot
{
    public string BossItem { get; set; } = "";
    public int BossItems { get; set; }
    public string BossPrefab { get; set; } = "";
    public string ItemPrefab { get; set; } = "";
    public string SetGlobalKey { get; set; } = "";
    public bool RenderSpawnAreaGizmos { get; set; }
    public bool AlertOnSpawn { get; set; }
    public float SpawnBossDelay { get; set; }
    public float SpawnBossMaxDistance { get; set; }
    public float SpawnBossMinDistance { get; set; }
    public float SpawnBossMaxYDistance { get; set; }
    public int GetSolidHeightMargin { get; set; }
    public bool EnableSolidHeightCheck { get; set; }
    public float SpawnPointClearingRadius { get; set; }
    public float SpawnYOffset { get; set; }
    public bool UseItemStands { get; set; }
    public string ItemStandPrefix { get; set; } = "";
    public float ItemStandMaxRange { get; set; }
}

internal sealed class ItemStandSnapshot
{
    public bool CanBeRemoved { get; set; }
    public bool AutoAttach { get; set; }
    public string OrientationType { get; set; } = "";
    public List<string> SupportedTypes { get; set; } = new();
    public List<string> SupportedItems { get; set; } = new();
    public List<string> UnsupportedItems { get; set; } = new();
    public float PowerActivationDelay { get; set; }
    public string GuardianPower { get; set; } = "";
}

internal sealed class OfferingBowlRuntimeState : MonoBehaviour
{
    public OfferingBowlSnapshot? Snapshot { get; set; }
    public bool Applied { get; set; }
    public float RespawnMinutes { get; set; }
    public long LocalLastUseTicks { get; set; }
    public string? PendingRefundPayload { get; set; }
}

internal sealed class ItemStandRuntimeState : MonoBehaviour
{
    public ItemStandSnapshot? Snapshot { get; set; }
    public bool Applied { get; set; }
}

internal sealed class BossStoneItemStandRuntimeState : MonoBehaviour
{
    public bool Resolved { get; set; }
    public bool ShouldHandle { get; set; }
    public string GuardianPowerName { get; set; } = "";
    public string PlayerKey { get; set; } = "";
}
