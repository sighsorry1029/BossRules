using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BossRules;

internal static partial class DespawnRulesManager
{
    private const string DefaultMessageDespawnStart = "{name} will despawn in {seconds}s unless someone returns.";
    private const string DefaultMessageDespawnReminder = "{name} will despawn in {seconds}s.";
    private const string DefaultMessageDespawnCanceled = "{name} despawn canceled.";
    private static string _messageDespawnStart = DefaultMessageDespawnStart;
    private static string _messageDespawnReminder = DefaultMessageDespawnReminder;
    private static string _messageDespawnCanceled = DefaultMessageDespawnCanceled;

    internal static void ConfigureMessages(string? despawnStart, string? despawnReminder, string? despawnCanceled)
    {
        _messageDespawnStart = despawnStart ?? DefaultMessageDespawnStart;
        _messageDespawnReminder = despawnReminder ?? DefaultMessageDespawnReminder;
        _messageDespawnCanceled = despawnCanceled ?? DefaultMessageDespawnCanceled;
    }

    private static void SendDespawnMessage(long playerId, string message)
    {
        if (playerId == 0L || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (BossRulesPlugin.IsRuntimeServer())
        {
            if (TrySendServerDespawnMessage(playerId, message))
            {
                return;
            }

            Player? fallbackPlayer = Player.GetPlayer(playerId);
            if (fallbackPlayer == null)
            {
                return;
            }

            fallbackPlayer.Message(MessageHud.MessageType.TopLeft, message);
            return;
        }

        Player? player = Player.GetPlayer(playerId);
        if (player == null)
        {
            return;
        }

        if (player.gameObject == null || player.IsDead())
        {
            return;
        }

        player.Message(MessageHud.MessageType.TopLeft, message);
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

    private static bool TrySendServerDespawnMessage(long recipientId, string message)
    {
        if (ZRoutedRpc.instance == null)
        {
            return false;
        }

        if (IsValidMessageTargetPeerId(recipientId))
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(
                recipientId,
                "ShowMessage",
                (int)MessageHud.MessageType.TopLeft,
                message);
            return true;
        }

        Player? player = Player.GetPlayer(recipientId);
        if (player != null &&
            TryResolveMessageTargetPeerId(player, out long targetPeerId))
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(
                targetPeerId,
                "ShowMessage",
                (int)MessageHud.MessageType.TopLeft,
                message);
            return true;
        }

        return false;
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

    private static string BuildDespawnStartMessage(string displayName, int remainingSeconds)
    {
        return FormatDespawnMessage(_messageDespawnStart, displayName, remainingSeconds);
    }

    private static string BuildDespawnReminderMessage(string displayName, int remainingSeconds)
    {
        return FormatDespawnMessage(_messageDespawnReminder, displayName, remainingSeconds);
    }

    private static string BuildDespawnCanceledMessage(string displayName)
    {
        return FormatDespawnMessage(_messageDespawnCanceled, displayName, 0);
    }

    private static string FormatDespawnMessage(string template, string displayName, int remainingSeconds)
    {
        return (template ?? "")
            .Replace("{name}", displayName ?? "")
            .Replace("{seconds}", remainingSeconds.ToString(CultureInfo.InvariantCulture));
    }

    private static string GetDisplayName(Character? character)
    {
        if (character == null)
        {
            return "Target";
        }

        string hoverName = character.GetHoverName();
        if (!string.IsNullOrWhiteSpace(hoverName))
        {
            return hoverName;
        }

        if (!string.IsNullOrWhiteSpace(character.m_name))
        {
            return Localization.instance != null
                ? Localization.instance.Localize(character.m_name)
                : character.m_name;
        }

        return character.gameObject != null && !string.IsNullOrWhiteSpace(character.gameObject.name)
            ? character.gameObject.name
            : "Target";
    }
}
