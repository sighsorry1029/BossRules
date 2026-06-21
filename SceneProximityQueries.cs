using System.Collections.Generic;
using UnityEngine;

namespace BossRules;

internal static class SceneProximityQueries
{
    internal static bool TryFindAnyLivingPlayerInRangeXZ(Vector3 point, float range, out long playerId)
    {
        playerId = 0L;
        if (range <= 0f)
        {
            return false;
        }

        float rangeSquared = range * range;
        if (BossRulesPlugin.IsRuntimeServer())
        {
            return TryFindAnyServerPlayerInRangeXZ(point, rangeSquared, out playerId);
        }

        foreach (Player player in Player.GetAllPlayers())
        {
            if (player == null ||
                player.gameObject == null ||
                player.IsDead())
            {
                continue;
            }

            long candidatePlayerId = player.GetPlayerID();
            if (candidatePlayerId == 0L || !IsWithinRangeXZ(player.transform.position, point, rangeSquared))
            {
                continue;
            }

            playerId = candidatePlayerId;
            return true;
        }

        return false;
    }

    internal static bool TryFindNearestLivingPlayerInRangeXZ(Vector3 point, float range, out long playerId)
    {
        playerId = 0L;
        if (range <= 0f)
        {
            return false;
        }

        float rangeSquared = range * range;
        if (BossRulesPlugin.IsRuntimeServer())
        {
            return TryFindNearestServerPlayerInRangeXZ(point, rangeSquared, out playerId);
        }

        float bestDistanceSquared = float.MaxValue;
        foreach (Player player in Player.GetAllPlayers())
        {
            if (player == null ||
                player.gameObject == null ||
                player.IsDead())
            {
                continue;
            }

            long candidatePlayerId = player.GetPlayerID();
            if (candidatePlayerId == 0L)
            {
                continue;
            }

            Vector3 offset = player.transform.position - point;
            offset.y = 0f;
            float distanceSquared = offset.sqrMagnitude;
            if (distanceSquared >= rangeSquared || distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            bestDistanceSquared = distanceSquared;
            playerId = candidatePlayerId;
        }

        return playerId != 0L;
    }

    private static bool TryFindAnyServerPlayerInRangeXZ(Vector3 point, float rangeSquared, out long playerId)
    {
        playerId = 0L;
        long localPeerId = ZNet.GetUID();
        if (localPeerId != 0L &&
            IsLocalServerPlayerInRangeXZ(point, rangeSquared, livingPlayersOnly: true))
        {
            playerId = localPeerId;
            return true;
        }

        List<ZNetPeer>? peers = ZNet.instance?.GetPeers();
        if (peers == null)
        {
            return false;
        }

        foreach (ZNetPeer peer in peers)
        {
            if (peer == null ||
                peer.m_uid == 0L ||
                !IsServerPeerInRangeXZ(peer, point, rangeSquared, livingPlayersOnly: true))
            {
                continue;
            }

            playerId = peer.m_uid;
            return true;
        }

        return false;
    }

    private static bool TryFindNearestServerPlayerInRangeXZ(Vector3 point, float rangeSquared, out long playerId)
    {
        playerId = 0L;
        float bestDistanceSquared = float.MaxValue;

        Player? localPlayer = Player.m_localPlayer;
        long localPeerId = ZNet.GetUID();
        if (localPeerId != 0L &&
            localPlayer != null &&
            localPlayer.gameObject != null &&
            !localPlayer.IsDead())
        {
            Vector3 localOffset = localPlayer.transform.position - point;
            localOffset.y = 0f;
            float localDistanceSquared = localOffset.sqrMagnitude;
            if (localDistanceSquared < rangeSquared)
            {
                bestDistanceSquared = localDistanceSquared;
                playerId = localPeerId;
            }
        }

        List<ZNetPeer>? peers = ZNet.instance?.GetPeers();
        if (peers == null)
        {
            return playerId != 0L;
        }

        foreach (ZNetPeer peer in peers)
        {
            if (peer == null ||
                peer.m_uid == 0L ||
                !IsServerPeerInRangeXZ(peer, point, rangeSquared, livingPlayersOnly: true))
            {
                continue;
            }

            if (!TryGetServerPeerPosition(peer, out Vector3 peerPosition))
            {
                continue;
            }

            Vector3 peerOffset = peerPosition - point;
            peerOffset.y = 0f;
            float peerDistanceSquared = peerOffset.sqrMagnitude;
            if (peerDistanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            bestDistanceSquared = peerDistanceSquared;
            playerId = peer.m_uid;
        }

        return playerId != 0L;
    }

    private static bool IsLocalServerPlayerInRangeXZ(Vector3 point, float rangeSquared, bool livingPlayersOnly)
    {
        Player? localPlayer = Player.m_localPlayer;
        return localPlayer != null &&
               localPlayer.gameObject != null &&
               (!livingPlayersOnly || !localPlayer.IsDead()) &&
               IsWithinRangeXZ(localPlayer.transform.position, point, rangeSquared);
    }

    private static bool IsServerPeerInRangeXZ(ZNetPeer? peer, Vector3 point, float rangeSquared, bool livingPlayersOnly)
    {
        if (peer == null ||
            !peer.IsReady() ||
            !TryGetServerPeerPosition(peer, out Vector3 peerPosition) ||
            !IsWithinRangeXZ(peerPosition, point, rangeSquared))
        {
            return false;
        }

        if (!livingPlayersOnly)
        {
            return true;
        }

        if (TryGetLoadedPeerPlayer(peer, out Player? player))
        {
            return player != null && !player.IsDead();
        }

        return true;
    }

    private static bool TryGetServerPeerPosition(ZNetPeer peer, out Vector3 position)
    {
        position = default;
        if (peer == null)
        {
            return false;
        }

        if (TryGetLoadedPeerPlayer(peer, out Player? player) &&
            player != null &&
            player.gameObject != null)
        {
            position = player.transform.position;
            return true;
        }

        if (!peer.m_characterID.IsNone() &&
            ZDOMan.instance != null)
        {
            ZDO? characterZdo = ZDOMan.instance.GetZDO(peer.m_characterID);
            if (characterZdo != null)
            {
                position = characterZdo.GetPosition();
                return true;
            }
        }

        position = peer.GetRefPos();
        return true;
    }

    private static bool TryGetLoadedPeerPlayer(ZNetPeer peer, out Player? player)
    {
        player = null;
        if (peer == null ||
            peer.m_characterID.IsNone() ||
            ZNetScene.instance == null)
        {
            return false;
        }

        GameObject? instance = ZNetScene.instance.FindInstance(peer.m_characterID);
        return instance != null && instance.TryGetComponent(out player);
    }

    private static bool IsWithinRangeXZ(Vector3 source, Vector3 target, float rangeSquared)
    {
        Vector3 offset = source - target;
        offset.y = 0f;
        return offset.sqrMagnitude < rangeSquared;
    }
}
