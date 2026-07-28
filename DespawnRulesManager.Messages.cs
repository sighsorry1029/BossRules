using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRules;

internal static partial class DespawnRulesManager
{
    private const string DespawnMessageRpc =
        "sighsorry.BossRules Despawn Message";
    private const int MaximumDespawnMessageNameLength = 256;
    private static ZRoutedRpc? _registeredDespawnMessageRpcInstance;

    private enum DespawnMessageKind
    {
        Start = 1,
        Reminder = 2,
        Canceled = 3
    }

    internal static void EnsureMessageRpcRegistered()
    {
        ZRoutedRpc? rpc = ZRoutedRpc.instance;
        if (rpc == null ||
            ReferenceEquals(rpc, _registeredDespawnMessageRpcInstance))
        {
            return;
        }

        rpc.Register<int, string, string, int>(
            DespawnMessageRpc,
            OnDespawnMessageRpc);
        _registeredDespawnMessageRpcInstance = rpc;
    }

    internal static void ShutdownMessages()
    {
        _registeredDespawnMessageRpcInstance = null;
    }

    private static void SendDespawnMessage(
        long playerId,
        DespawnMessageKind messageKind,
        string? nameLocalizationKey,
        string? prefabName,
        int remainingSeconds)
    {
        if (playerId == 0L)
        {
            return;
        }

        string safeNameLocalizationKey =
            LimitMessageValue(nameLocalizationKey);
        string safePrefabName = LimitMessageValue(prefabName);
        int safeRemainingSeconds = Math.Max(0, remainingSeconds);

        if (BossRulesPlugin.IsRuntimeServer())
        {
            if (TrySendServerDespawnMessage(
                    playerId,
                    messageKind,
                    safeNameLocalizationKey,
                    safePrefabName,
                    safeRemainingSeconds))
            {
                return;
            }

            Player? fallbackPlayer = Player.GetPlayer(playerId);
            if (fallbackPlayer == null ||
                fallbackPlayer != Player.m_localPlayer)
            {
                return;
            }

            ShowLocalizedDespawnMessage(
                messageKind,
                safeNameLocalizationKey,
                safePrefabName,
                safeRemainingSeconds);
            return;
        }

        Player? player = Player.GetPlayer(playerId);
        if (player == null ||
            player != Player.m_localPlayer)
        {
            return;
        }

        ShowLocalizedDespawnMessage(
            messageKind,
            safeNameLocalizationKey,
            safePrefabName,
            safeRemainingSeconds);
    }

    private static bool IsDespawnMessageRecipientAvailable(long recipientId)
    {
        if (recipientId == 0L)
        {
            return false;
        }

        if (BossRulesPlugin.IsRuntimeServer())
        {
            if (IsValidMessageTargetPeerId(recipientId))
            {
                return true;
            }

            Player? player = Player.GetPlayer(recipientId);
            return player != null &&
                   TryResolveMessageTargetPeerId(player, out _);
        }

        Player? localPlayer = Player.GetPlayer(recipientId);
        return localPlayer != null &&
               localPlayer.gameObject != null &&
               !localPlayer.IsDead();
    }

    private static bool TrySendServerDespawnMessage(
        long recipientId,
        DespawnMessageKind messageKind,
        string nameLocalizationKey,
        string prefabName,
        int remainingSeconds)
    {
        EnsureMessageRpcRegistered();
        if (ZRoutedRpc.instance == null)
        {
            return false;
        }

        long targetPeerId;
        if (IsValidMessageTargetPeerId(recipientId))
        {
            targetPeerId = recipientId;
        }
        else
        {
            Player? player = Player.GetPlayer(recipientId);
            if (player == null ||
                !TryResolveMessageTargetPeerId(player, out targetPeerId))
            {
                return false;
            }
        }

        if (targetPeerId == ZNet.GetUID() &&
            Player.m_localPlayer != null)
        {
            ShowLocalizedDespawnMessage(
                messageKind,
                nameLocalizationKey,
                prefabName,
                remainingSeconds);
            return true;
        }

        ZRoutedRpc.instance.InvokeRoutedRPC(
            targetPeerId,
            DespawnMessageRpc,
            (int)messageKind,
            nameLocalizationKey,
            prefabName,
            remainingSeconds);
        return true;
    }

    private static bool TryResolveMessageTargetPeerId(Player player, out long targetPeerId)
    {
        targetPeerId = 0L;
        if (player == null)
        {
            return false;
        }

        ZDOID characterId = player.GetZDOID();
        long candidatePeerId = characterId.UserID;
        if (IsValidMessageTargetPeerId(candidatePeerId))
        {
            targetPeerId = candidatePeerId;
            return true;
        }

        List<ZNetPeer>? peers = ZNet.instance?.GetPeers();
        if (peers != null)
        {
            foreach (ZNetPeer peer in peers)
            {
                if (peer != null &&
                    peer.IsReady() &&
                    peer.m_characterID == characterId)
                {
                    targetPeerId = peer.m_uid;
                    return true;
                }
            }
        }

        string playerName = player.GetPlayerName();
        if (!string.IsNullOrWhiteSpace(playerName))
        {
            ZNetPeer? namedPeer = ZNet.instance?.GetPeerByPlayerName(playerName);
            if (namedPeer != null && namedPeer.IsReady())
            {
                targetPeerId = namedPeer.m_uid;
                return true;
            }
        }

        return false;
    }

    private static bool IsValidMessageTargetPeerId(long peerId)
    {
        if (peerId == 0L)
        {
            return false;
        }

        if (peerId == ZNet.GetUID())
        {
            return true;
        }

        return ZNet.instance?.GetPeer(peerId)?.IsReady() == true;
    }

    private static void OnDespawnMessageRpc(
        long sender,
        int rawMessageKind,
        string nameLocalizationKey,
        string prefabName,
        int remainingSeconds)
    {
        ZRoutedRpc? rpc = ZRoutedRpc.instance;
        if (rpc == null ||
            sender != rpc.GetServerPeerID() ||
            nameLocalizationKey == null ||
            prefabName == null ||
            nameLocalizationKey.Length > MaximumDespawnMessageNameLength ||
            prefabName.Length > MaximumDespawnMessageNameLength ||
            remainingSeconds < 0 ||
            remainingSeconds > 300 ||
            !TryParseDespawnMessageKind(
                rawMessageKind,
                out DespawnMessageKind messageKind))
        {
            return;
        }

        ShowLocalizedDespawnMessage(
            messageKind,
            nameLocalizationKey,
            prefabName,
            remainingSeconds);
    }

    private static void ShowLocalizedDespawnMessage(
        DespawnMessageKind messageKind,
        string nameLocalizationKey,
        string prefabName,
        int remainingSeconds)
    {
        Player? localPlayer = Player.m_localPlayer;
        if (localPlayer == null ||
            localPlayer.gameObject == null ||
            localPlayer.IsDead())
        {
            return;
        }

        string messageKey = messageKind switch
        {
            DespawnMessageKind.Start =>
                BossRulesLocalization.MessageDespawnStartKey,
            DespawnMessageKind.Reminder =>
                BossRulesLocalization.MessageDespawnReminderKey,
            DespawnMessageKind.Canceled =>
                BossRulesLocalization.MessageDespawnCanceledKey,
            _ => ""
        };
        if (messageKey.Length == 0)
        {
            return;
        }

        string message = BossRulesLocalization.FormatDespawnMessage(
            messageKey,
            nameLocalizationKey,
            prefabName,
            remainingSeconds);
        localPlayer.Message(MessageHud.MessageType.TopLeft, message);
    }

    private static bool TryParseDespawnMessageKind(
        int rawMessageKind,
        out DespawnMessageKind messageKind)
    {
        messageKind = (DespawnMessageKind)rawMessageKind;
        return messageKind == DespawnMessageKind.Start ||
               messageKind == DespawnMessageKind.Reminder ||
               messageKind == DespawnMessageKind.Canceled;
    }

    private static string LimitMessageValue(string? value)
    {
        string normalized = (value ?? "").Trim();
        return normalized.Length <= MaximumDespawnMessageNameLength
            ? normalized
            : normalized.Substring(0, MaximumDespawnMessageNameLength);
    }
}
