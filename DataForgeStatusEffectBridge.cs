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
    private static bool StatusEffectOverridesWillApplyHandlerAttached;
    private static bool StatusEffectOverridesAppliedHandlerAttached;
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
        if (!TryDisconnect())
        {
            return;
        }

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

            TryDisconnect();
            NextResolveAt = 0f;
            return false;
        }
    }

    internal static void Shutdown()
    {
        TryDisconnect();
        NextResolveAt = 0f;
        InvokeWarningLogged = false;
        SubscriptionWarningLogged = false;
    }

    private static bool TryDisconnect()
    {
        HasActiveStatusEffectOverrideMethod = null;
        Subscribed = false;
        bool disconnected = true;
        if (StatusEffectOverridesWillApplyHandlerAttached)
        {
            try
            {
                StatusEffectOverridesWillApplyEvent?.RemoveEventHandler(
                    null,
                    StatusEffectOverridesWillApplyHandler);
                StatusEffectOverridesWillApplyHandlerAttached = false;
            }
            catch
            {
                disconnected = false;
            }
        }

        if (StatusEffectOverridesAppliedHandlerAttached)
        {
            try
            {
                StatusEffectOverridesAppliedEvent?.RemoveEventHandler(
                    null,
                    StatusEffectOverridesAppliedHandler);
                StatusEffectOverridesAppliedHandlerAttached = false;
            }
            catch
            {
                disconnected = false;
            }
        }

        if (!StatusEffectOverridesWillApplyHandlerAttached)
        {
            StatusEffectOverridesWillApplyEvent = null;
            StatusEffectOverridesWillApplyHandler = null;
        }

        if (!StatusEffectOverridesAppliedHandlerAttached)
        {
            StatusEffectOverridesAppliedEvent = null;
            StatusEffectOverridesAppliedHandler = null;
        }

        return disconnected;
    }

    private static void TryResolveAndSubscribe()
    {
        try
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
            if (HasActiveStatusEffectOverrideMethod?.ReturnType != typeof(bool))
            {
                HasActiveStatusEffectOverrideMethod = null;
            }

            StatusEffectOverridesWillApplyEvent = GetActionEvent(apiType, "StatusEffectOverridesWillApply");
            StatusEffectOverridesAppliedEvent = GetActionEvent(apiType, "StatusEffectOverridesApplied");

            if (HasActiveStatusEffectOverrideMethod == null)
            {
                LogSubscriptionWarningOnce("DataForge status effect ownership API was found, but the query method is missing.");
                return;
            }

            if (StatusEffectOverridesWillApplyEvent == null || StatusEffectOverridesAppliedEvent == null)
            {
                StatusEffectOverridesWillApplyEvent = null;
                StatusEffectOverridesAppliedEvent = null;
                LogSubscriptionWarningOnce("DataForge status effect ownership API was found, but ownership events are missing or have unsupported signatures.");
                Subscribed = true;
                return;
            }

            StatusEffectOverridesWillApplyHandler = ForsakenPowerRuntime.ReleaseDataForgeOwnedSnapshots;
            StatusEffectOverridesAppliedHandler = ForsakenPowerRuntime.RequestReapply;
            StatusEffectOverridesWillApplyHandlerAttached = true;
            StatusEffectOverridesWillApplyEvent.AddEventHandler(null, StatusEffectOverridesWillApplyHandler);
            StatusEffectOverridesAppliedHandlerAttached = true;
            StatusEffectOverridesAppliedEvent.AddEventHandler(null, StatusEffectOverridesAppliedHandler);
            Subscribed = true;
            BossRulesPlugin.BossRulesLogger.LogDebug("Connected to DataForge status effect ownership API.");
        }
        catch (Exception ex)
        {
            TryDisconnect();
            LogSubscriptionWarningOnce(
                $"Could not connect to DataForge status effect ownership API. BossRules will retry. {ex.GetType().Name}: {ex.Message}");
        }
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
