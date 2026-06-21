using System.Collections.Generic;
using UnityEngine;

namespace BossRules;

internal static partial class ForsakenPowerRuntime
{
    private static readonly Dictionary<int, ForsakenPowerSnapshot> SnapshotsByHash = new();

    private sealed class ForsakenPowerSnapshot
    {
        public StatusEffect Effect { get; set; } = null!;
        public float Ttl { get; set; }
        public float Cooldown { get; set; }
        public float RunStaminaDrainModifier { get; set; }
        public float JumpStaminaUseModifier { get; set; }
        public float AttackStaminaUseModifier { get; set; }
        public float BlockStaminaUseModifier { get; set; }
        public float BlockStaminaUseFlatValue { get; set; }
        public float DodgeStaminaUseModifier { get; set; }
        public float SwimStaminaUseModifier { get; set; }
        public float SneakStaminaUseModifier { get; set; }
        public float HealthRegenMultiplier { get; set; }
        public float StaminaRegenMultiplier { get; set; }
        public float EitrRegenMultiplier { get; set; }
        public float AddArmor { get; set; }
        public float ArmorMultiplier { get; set; }
        public Skills.SkillType SkillLevel { get; set; }
        public float SkillLevelModifier { get; set; }
        public Skills.SkillType SkillLevel2 { get; set; }
        public float SkillLevelModifier2 { get; set; }
        public List<HitData.DamageModPair> Mods { get; set; } = new();
        public HitData.DamageTypes PercentigeDamageModifiers { get; set; }
        public float AddMaxCarryWeight { get; set; }
        public float SpeedModifier { get; set; }
        public float SwimSpeedModifier { get; set; }
        public Vector3 JumpModifier { get; set; }
        public float AdrenalineModifier { get; set; }
        public float StaggerModifier { get; set; }
    }

    private static void EnsureSnapshotLocked(StatusEffect effect)
    {
        int hash = effect.NameHash();
        if (SnapshotsByHash.ContainsKey(hash))
        {
            return;
        }

        ForsakenPowerSnapshot snapshot = new()
        {
            Effect = effect,
            Ttl = effect.m_ttl,
            Cooldown = effect.m_cooldown
        };

        if (effect is SE_Stats stats)
        {
            snapshot = new ForsakenPowerSnapshot
            {
                Effect = effect,
                Ttl = effect.m_ttl,
                Cooldown = effect.m_cooldown,
                RunStaminaDrainModifier = stats.m_runStaminaDrainModifier,
                JumpStaminaUseModifier = stats.m_jumpStaminaUseModifier,
                AttackStaminaUseModifier = stats.m_attackStaminaUseModifier,
                BlockStaminaUseModifier = stats.m_blockStaminaUseModifier,
                BlockStaminaUseFlatValue = stats.m_blockStaminaUseFlatValue,
                DodgeStaminaUseModifier = stats.m_dodgeStaminaUseModifier,
                SwimStaminaUseModifier = stats.m_swimStaminaUseModifier,
                SneakStaminaUseModifier = stats.m_sneakStaminaUseModifier,
                HealthRegenMultiplier = stats.m_healthRegenMultiplier,
                StaminaRegenMultiplier = stats.m_staminaRegenMultiplier,
                EitrRegenMultiplier = stats.m_eitrRegenMultiplier,
                AddArmor = stats.m_addArmor,
                ArmorMultiplier = stats.m_armorMultiplier,
                SkillLevel = stats.m_skillLevel,
                SkillLevelModifier = stats.m_skillLevelModifier,
                SkillLevel2 = stats.m_skillLevel2,
                SkillLevelModifier2 = stats.m_skillLevelModifier2,
                Mods = stats.m_mods != null
                    ? new List<HitData.DamageModPair>(stats.m_mods)
                    : new List<HitData.DamageModPair>(),
                PercentigeDamageModifiers = stats.m_percentigeDamageModifiers,
                AddMaxCarryWeight = stats.m_addMaxCarryWeight,
                SpeedModifier = stats.m_speedModifier,
                SwimSpeedModifier = stats.m_swimSpeedModifier,
                JumpModifier = stats.m_jumpModifier,
                AdrenalineModifier = stats.m_adrenalineModifier,
                StaggerModifier = stats.m_staggerModifier
            };
        }

        SnapshotsByHash[hash] = snapshot;
    }

    private static void RestoreAllSnapshotsLocked()
    {
        foreach (ForsakenPowerSnapshot snapshot in SnapshotsByHash.Values)
        {
            RestoreSnapshot(snapshot);
        }
    }

    private static void RestoreSnapshot(ForsakenPowerSnapshot snapshot)
    {
        StatusEffect effect = snapshot.Effect;
        if (effect == null)
        {
            return;
        }

        effect.m_ttl = snapshot.Ttl;
        effect.m_cooldown = snapshot.Cooldown;
        if (effect is not SE_Stats stats)
        {
            return;
        }

        stats.m_runStaminaDrainModifier = snapshot.RunStaminaDrainModifier;
        stats.m_jumpStaminaUseModifier = snapshot.JumpStaminaUseModifier;
        stats.m_attackStaminaUseModifier = snapshot.AttackStaminaUseModifier;
        stats.m_blockStaminaUseModifier = snapshot.BlockStaminaUseModifier;
        stats.m_blockStaminaUseFlatValue = snapshot.BlockStaminaUseFlatValue;
        stats.m_dodgeStaminaUseModifier = snapshot.DodgeStaminaUseModifier;
        stats.m_swimStaminaUseModifier = snapshot.SwimStaminaUseModifier;
        stats.m_sneakStaminaUseModifier = snapshot.SneakStaminaUseModifier;
        stats.m_healthRegenMultiplier = snapshot.HealthRegenMultiplier;
        stats.m_staminaRegenMultiplier = snapshot.StaminaRegenMultiplier;
        stats.m_eitrRegenMultiplier = snapshot.EitrRegenMultiplier;
        stats.m_addArmor = snapshot.AddArmor;
        stats.m_armorMultiplier = snapshot.ArmorMultiplier;
        stats.m_skillLevel = snapshot.SkillLevel;
        stats.m_skillLevelModifier = snapshot.SkillLevelModifier;
        stats.m_skillLevel2 = snapshot.SkillLevel2;
        stats.m_skillLevelModifier2 = snapshot.SkillLevelModifier2;
        stats.m_mods = new List<HitData.DamageModPair>(snapshot.Mods);
        stats.m_percentigeDamageModifiers = snapshot.PercentigeDamageModifiers;
        stats.m_addMaxCarryWeight = snapshot.AddMaxCarryWeight;
        stats.m_speedModifier = snapshot.SpeedModifier;
        stats.m_swimSpeedModifier = snapshot.SwimSpeedModifier;
        stats.m_jumpModifier = snapshot.JumpModifier;
        stats.m_adrenalineModifier = snapshot.AdrenalineModifier;
        stats.m_staggerModifier = snapshot.StaggerModifier;
    }

    private static void ClearSupportedStats(SE_Stats stats)
    {
        stats.m_runStaminaDrainModifier = 0f;
        stats.m_jumpStaminaUseModifier = 0f;
        stats.m_attackStaminaUseModifier = 0f;
        stats.m_blockStaminaUseModifier = 0f;
        stats.m_blockStaminaUseFlatValue = 0f;
        stats.m_dodgeStaminaUseModifier = 0f;
        stats.m_swimStaminaUseModifier = 0f;
        stats.m_sneakStaminaUseModifier = 0f;
        stats.m_healthRegenMultiplier = 1f;
        stats.m_staminaRegenMultiplier = 1f;
        stats.m_eitrRegenMultiplier = 1f;
        stats.m_addArmor = 0f;
        stats.m_armorMultiplier = 0f;
        stats.m_skillLevel = Skills.SkillType.None;
        stats.m_skillLevelModifier = 0f;
        stats.m_skillLevel2 = Skills.SkillType.None;
        stats.m_skillLevelModifier2 = 0f;
        stats.m_mods = new List<HitData.DamageModPair>();
        stats.m_percentigeDamageModifiers = default;
        stats.m_addMaxCarryWeight = 0f;
        stats.m_speedModifier = 0f;
        stats.m_swimSpeedModifier = 0f;
        stats.m_jumpModifier = Vector3.zero;
        stats.m_adrenalineModifier = 0f;
        stats.m_staggerModifier = 0f;
    }
}
