using System;
using System.Reflection;
using UnityEngine;

namespace BossRules;

internal static class DataForgeStatusEffectBridge
{
    private const string ApiTypeName = "DataForge.DataForgeStatusEffectOwnership, DataForge";
    private const float ResolveRetrySeconds = 2f;

    private static MethodInfo? HasActiveStatusEffectOverrideMethod;
    private static EventInfo? StatusEffectOverridesWillApplyEvent;
    private static EventInfo? StatusEffectOverridesAppliedEvent;
    private static Action? StatusEffectOverridesWillApplyHandler;
    private static Action? StatusEffectOverridesAppliedHandler;
    private static float NextResolveAt;
    private static bool Subscribed;
    private static bool InvokeWarningLogged;
    private static bool SubscriptionWarningLogged;

    internal static void ProcessDeferredSubscription()
    {
        if (Subscribed || Time.realtimeSinceStartup < NextResolveAt)
        {
            return;
        }

        NextResolveAt = Time.realtimeSinceStartup + ResolveRetrySeconds;
        TryResolveAndSubscribe();
    }

    internal static bool IsStatusEffectOwnedByDataForge(string effectName)
    {
        if (string.IsNullOrWhiteSpace(effectName))
        {
            return false;
        }

        ProcessDeferredSubscription();
        MethodInfo? method = HasActiveStatusEffectOverrideMethod;
        if (method == null)
        {
            return false;
        }

        try
        {
            return method.Invoke(null, new object[] { effectName }) is true;
        }
        catch (Exception ex)
        {
            if (!InvokeWarningLogged)
            {
                InvokeWarningLogged = true;
                BossRulesPlugin.BossRulesLogger.LogWarning(
                    $"Could not query DataForge status effect ownership. BossRules will ignore DataForge ownership until it can resolve again. {ex.GetType().Name}: {ex.Message}");
            }

            HasActiveStatusEffectOverrideMethod = null;
            Subscribed = false;
            return false;
        }
    }

    internal static void Shutdown()
    {
        if (StatusEffectOverridesWillApplyEvent != null && StatusEffectOverridesWillApplyHandler != null)
        {
            try
            {
                StatusEffectOverridesWillApplyEvent.RemoveEventHandler(null, StatusEffectOverridesWillApplyHandler);
            }
            catch
            {
                // Best effort cleanup for an optional integration.
            }
        }

        if (StatusEffectOverridesAppliedEvent != null && StatusEffectOverridesAppliedHandler != null)
        {
            try
            {
                StatusEffectOverridesAppliedEvent.RemoveEventHandler(null, StatusEffectOverridesAppliedHandler);
            }
            catch
            {
                // Best effort cleanup for an optional integration.
            }
        }

        HasActiveStatusEffectOverrideMethod = null;
        StatusEffectOverridesWillApplyEvent = null;
        StatusEffectOverridesAppliedEvent = null;
        StatusEffectOverridesWillApplyHandler = null;
        StatusEffectOverridesAppliedHandler = null;
        Subscribed = false;
        NextResolveAt = 0f;
        InvokeWarningLogged = false;
        SubscriptionWarningLogged = false;
    }

    private static void TryResolveAndSubscribe()
    {
        Type? apiType = Type.GetType(ApiTypeName, throwOnError: false);
        if (apiType == null)
        {
            return;
        }

        HasActiveStatusEffectOverrideMethod = apiType.GetMethod(
            "HasActiveStatusEffectOverride",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null);

        StatusEffectOverridesWillApplyEvent = GetActionEvent(apiType, "StatusEffectOverridesWillApply");
        StatusEffectOverridesAppliedEvent = GetActionEvent(apiType, "StatusEffectOverridesApplied");

        if (HasActiveStatusEffectOverrideMethod == null)
        {
            LogSubscriptionWarningOnce("DataForge status effect ownership API was found, but the query method is missing.");
            return;
        }

        if (StatusEffectOverridesWillApplyEvent == null || StatusEffectOverridesAppliedEvent == null)
        {
            LogSubscriptionWarningOnce("DataForge status effect ownership API was found, but ownership events are missing or have unsupported signatures.");
            Subscribed = true;
            return;
        }

        StatusEffectOverridesWillApplyHandler = ForsakenPowerRuntime.ReleaseDataForgeOwnedSnapshots;
        StatusEffectOverridesAppliedHandler = ForsakenPowerRuntime.RequestReapply;
        StatusEffectOverridesWillApplyEvent.AddEventHandler(null, StatusEffectOverridesWillApplyHandler);
        StatusEffectOverridesAppliedEvent.AddEventHandler(null, StatusEffectOverridesAppliedHandler);
        Subscribed = true;
        BossRulesPlugin.BossRulesLogger.LogDebug("Connected to DataForge status effect ownership API.");
    }

    private static EventInfo? GetActionEvent(Type apiType, string eventName)
    {
        EventInfo? eventInfo = apiType.GetEvent(eventName, BindingFlags.Public | BindingFlags.Static);
        return eventInfo?.EventHandlerType == typeof(Action) ? eventInfo : null;
    }

    private static void LogSubscriptionWarningOnce(string message)
    {
        if (SubscriptionWarningLogged)
        {
            return;
        }

        SubscriptionWarningLogged = true;
        BossRulesPlugin.BossRulesLogger.LogWarning(message);
    }
}
