using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BossRules;

// Configuration identifies the character phases. Only the two native effect
// paths below may transport the offering record; proximity is never evidence.
internal static class BossPhaseRuntime
{
    private static readonly int BridgeFromKey = "BossRules.phase_from".GetStableHashCode();
    private static readonly int BridgeNextKey = "BossRules.phase_next".GetStableHashCode();
    private static readonly Dictionary<int, string> NextPhases = new();
    private static readonly HashSet<string> Warnings = new(StringComparer.Ordinal);
    [ThreadStatic] private static Transition? _current;

    internal sealed class Transition
    {
        internal ZDO Source = null!;
        internal EffectList Effects = null!;
        internal GameObject Successor = null!;
        internal int From;
        internal string Next = "";
        internal bool ThroughRagdoll;
        internal bool AllowRagdoll;
    }

    internal readonly struct Scope
    {
        internal Scope(Transition? previous) { Previous = previous; Entered = true; }
        internal readonly Transition? Previous;
        internal readonly bool Entered;
    }

    internal static void Configure(List<List<string>> chains)
    {
        NextPhases.Clear();
        Warnings.Clear();
        foreach (List<string> chain in chains)
        {
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                NextPhases.Add(chain[i].GetStableHashCode(), chain[i + 1]);
            }
        }
    }

    internal static Transition? Enter(Character character)
    {
        Transition? previous = _current;
        _current = null;
        try
        {
            ZDO? source = GetOwnedRecord(character);
            if (source != null && NextPhases.TryGetValue(source.GetPrefab(), out string? next))
            {
                _current = Prepare(source, character.m_deathEffects, source.GetPrefab(), next, true);
            }
        }
        catch (Exception ex) { WarnException(ex); }
        return previous;
    }

    internal static Transition? Enter(Ragdoll ragdoll)
    {
        Transition? previous = _current;
        _current = null;
        try
        {
            ZDO? source = GetOwnedRecord(ragdoll);
            if (source != null)
            {
                int from = source.GetInt(BridgeFromKey);
                string next = source.GetString(BridgeNextKey, "");
                // A live configuration change can cancel or replace an edge. Do not
                // follow a stale bridge into a newly configured, unrelated fight.
                if (next.Length > 0 && NextPhases.TryGetValue(from, out string? configured) && configured == next)
                {
                    _current = Prepare(source, ragdoll.m_removeEffect, from, next, false);
                }
            }
        }
        catch (Exception ex) { WarnException(ex); }
        return previous;
    }

    internal static void Restore(Transition? previous) => _current = previous;

    private static ZDO? GetOwnedRecord(Component component)
    {
        ZNetView? view = component.GetComponent<ZNetView>();
        if (view == null || !view.IsValid() || !view.IsOwner())
        {
            return null;
        }
        ZDO zdo = view.GetZDO();
        return AltarRuntime.IsAltarSummoned(zdo) && !AltarRuntime.HasTransferredAltarSummon(zdo) ? zdo : null;
    }

    private static Transition? Prepare(ZDO source, EffectList effects, int from, string next, bool allowRagdoll)
    {
        if (!TryFindSuccessor(effects, next, allowRagdoll, out GameObject? successor, out bool throughRagdoll))
        {
            Warn(from, next, "No unique supported path. Expected one character, directly or through one persistent Ragdoll; offering inheritance was skipped.");
            return null;
        }
        return new Transition
        {
            Source = source, Effects = effects, Successor = successor!, From = from,
            Next = next, ThroughRagdoll = throughRagdoll, AllowRagdoll = allowRagdoll
        };
    }

    // Re-read only the source's current effects at the transition. No asset loading,
    // global graph scan or stale cache when another mod edits an existing prefab.
    internal static bool TryFindSuccessor(EffectList effects, string next, bool allowRagdoll,
        out GameObject? successor, out bool throughRagdoll)
    {
        successor = null;
        throughRagdoll = false;
        int characterCount = 0;
        foreach (EffectList.EffectData effect in effects.m_effectPrefabs)
        {
            GameObject? prefab = effect?.m_prefab;
            if (effect == null || !effect.m_enabled || prefab == null)
            {
                continue;
            }
            if (prefab.TryGetComponent(out Character _))
            {
                characterCount++;
                if (IsPersistent(prefab) && Utils.GetPrefabName(prefab) == next)
                {
                    successor = prefab;
                    throughRagdoll = false;
                }
            }
            else if (allowRagdoll && prefab.TryGetComponent(out Ragdoll ragdoll))
            {
                foreach (EffectList.EffectData removal in ragdoll.m_removeEffect.m_effectPrefabs)
                {
                    GameObject? child = removal?.m_prefab;
                    if (removal == null || !removal.m_enabled || child == null || !child.TryGetComponent(out Character _))
                    {
                        continue;
                    }
                    characterCount++;
                    if (IsPersistent(prefab) && IsPersistent(child) && Utils.GetPrefabName(child) == next)
                    {
                        successor = prefab;
                        throughRagdoll = true;
                    }
                }
            }
        }
        return characterCount == 1 && successor != null;
    }

    private static bool IsPersistent(GameObject prefab) =>
        prefab.TryGetComponent(out ZNetView view) && view.m_persistent;

    internal static void AfterEffects(EffectList effects, GameObject[] created)
    {
        Transition? transition = _current;
        if (transition == null || !ReferenceEquals(effects, transition.Effects))
        {
            return;
        }
        // Consume this callback context before any nested callbacks can reuse it.
        _current = null;
        try
        {
            Complete(transition, created);
        }
        catch (Exception ex) { WarnException(ex); }
    }

    private static void Complete(Transition transition, GameObject[] created)
    {
        if (!transition.Source.IsOwner() || AltarRuntime.HasTransferredAltarSummon(transition.Source) ||
            !NextPhases.TryGetValue(transition.From, out string? next) || next != transition.Next)
        {
            return;
        }
        // Other prefixes may have changed the effects after our context was entered.
        if (!TryFindSuccessor(transition.Effects, next, transition.AllowRagdoll,
                out GameObject? expected, out bool throughRagdoll) ||
            expected != transition.Successor || throughRagdoll != transition.ThroughRagdoll)
        {
            Warn(transition.From, next, "Effect path changed during spawning; offering inheritance was skipped.");
            return;
        }

        GameObject? target = null;
        string expectedName = Utils.GetPrefabName(transition.Successor);
        foreach (GameObject instance in created ?? Array.Empty<GameObject>())
        {
            if (instance == null || Utils.GetPrefabName(instance) != expectedName)
            {
                continue;
            }
            if (target != null)
            {
                Warn(transition.From, next, "Multiple successors were created; offering inheritance was skipped to prevent duplicate refunds.");
                return;
            }
            target = instance;
        }
        ZNetView? view = target != null ? target.GetComponent<ZNetView>() : null;
        if (view == null || !view.IsValid() || !view.IsOwner() || !view.m_persistent ||
            (throughRagdoll ? target!.GetComponent<Ragdoll>() == null : target!.GetComponent<Character>() == null))
        {
            Warn(transition.From, next, "The successor has no valid owned persistent ZDO; offering inheritance was skipped.");
            return;
        }
        ZDO destination = view.GetZDO();
        if (destination.GetPrefab() != expectedName.GetStableHashCode() || !destination.Persistent)
        {
            Warn(transition.From, next, "The successor ZDO has a different prefab or is not persistent; offering inheritance was skipped.");
            return;
        }
        if (AltarRuntime.IsAltarSummoned(destination))
        {
            Warn(transition.From, next, "The successor already belongs to an altar summon; its record was preserved.");
            return;
        }
        if (throughRagdoll)
        {
            destination.Set(BridgeFromKey, transition.From);
            destination.Set(BridgeNextKey, next);
        }
        if (AltarRuntime.TryTransferAltarSummon(transition.Source, destination) && !throughRagdoll)
        {
            DespawnRulesManager.TryTrackLoadedDespawnTarget(target!.GetComponent<Character>());
        }
    }

    private static void Warn(int from, string next, string reason)
    {
        string sourceName = ZNetScene.instance?.GetPrefab(from)?.name ?? from.ToString();
        string message = $"bossPhases '{sourceName}' -> '{next}': {reason}";
        if (Warnings.Add(message)) BossRulesPlugin.BossRulesLogger.LogWarning(message);
    }

    private static void WarnException(Exception ex)
    {
        string message = $"bossPhases inheritance failed: {ex.GetType().Name}: {ex.Message}";
        if (Warnings.Add(message)) BossRulesPlugin.BossRulesLogger.LogWarning(message);
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
internal static class CharacterDeathBossPhasePatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(Character __instance, out BossPhaseRuntime.Scope __state) =>
        __state = new BossPhaseRuntime.Scope(BossPhaseRuntime.Enter(__instance));

    private static void Finalizer(BossPhaseRuntime.Scope __state)
    {
        // Harmony can skip our stateful prefix when an earlier prefix returns false.
        // In that case leave an enclosing death/removal context untouched.
        if (__state.Entered) BossPhaseRuntime.Restore(__state.Previous);
    }
}

[HarmonyPatch(typeof(Ragdoll), "DestroyNow")]
internal static class RagdollRemovalBossPhasePatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(Ragdoll __instance, out BossPhaseRuntime.Scope __state) =>
        __state = new BossPhaseRuntime.Scope(BossPhaseRuntime.Enter(__instance));

    private static void Finalizer(BossPhaseRuntime.Scope __state)
    {
        if (__state.Entered) BossPhaseRuntime.Restore(__state.Previous);
    }
}

[HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
internal static class EffectListBossPhasePatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(EffectList __instance, GameObject[] __result) =>
        BossPhaseRuntime.AfterEffects(__instance, __result);
}
