using System.Collections.Generic;
using UnityEngine;

namespace BossRules;

internal static class SceneProximityQueries
{
    internal static bool TryFindAnyLivingServerPlayerInRangeXZ(Vector3 point, float range, out long playerId)
    {
        playerId = 0L;
        if (range <= 0f || !BossRulesPlugin.IsRuntimeServer())
        {
            return false;
        }

        float rangeSquared = range * range;
        long localPeerId = ZNet.GetUID();
        if (localPeerId != 0L && IsLocalServerPlayerInRangeXZ(point, rangeSquared))
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
            if (!TryGetLivingServerPeerDistanceSquaredXZ(peer, point, rangeSquared, out _))
            {
                continue;
            }

            playerId = peer.m_uid;
            return true;
        }

        return false;
    }

    internal static bool TryFindNearestLivingServerPlayerInRangeXZ(Vector3 point, float range, out long playerId)
    {
        playerId = 0L;
        if (range <= 0f || !BossRulesPlugin.IsRuntimeServer())
        {
            return false;
        }

        float rangeSquared = range * range;
        float bestDistanceSquared = float.MaxValue;
        Player? localPlayer = Player.m_localPlayer;
        long localPeerId = ZNet.GetUID();
        if (localPeerId != 0L &&
            localPlayer != null &&
            localPlayer.gameObject != null &&
            !localPlayer.IsDead())
        {
            float localDistanceSquared = GetDistanceSquaredXZ(localPlayer.transform.position, point);
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
            if (!TryGetLivingServerPeerDistanceSquaredXZ(peer, point, rangeSquared, out float peerDistanceSquared))
            {
                continue;
            }

            if (peerDistanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            bestDistanceSquared = peerDistanceSquared;
            playerId = peer.m_uid;
        }

        return playerId != 0L;
    }

    private static bool IsLocalServerPlayerInRangeXZ(Vector3 point, float rangeSquared)
    {
        Player? localPlayer = Player.m_localPlayer;
        return localPlayer != null &&
               localPlayer.gameObject != null &&
               !localPlayer.IsDead() &&
               GetDistanceSquaredXZ(localPlayer.transform.position, point) < rangeSquared;
    }

    private static bool TryGetLivingServerPeerDistanceSquaredXZ(
        ZNetPeer? peer,
        Vector3 point,
        float rangeSquared,
        out float distanceSquared)
    {
        distanceSquared = float.MaxValue;
        if (peer == null ||
            peer.m_uid == 0L ||
            !peer.IsReady() ||
            !TryGetServerPeerPosition(peer, out Vector3 position, out Player? loadedPlayer))
        {
            return false;
        }

        distanceSquared = GetDistanceSquaredXZ(position, point);
        return distanceSquared < rangeSquared &&
               (loadedPlayer == null || !loadedPlayer.IsDead());
    }

    private static bool TryGetServerPeerPosition(ZNetPeer peer, out Vector3 position, out Player? loadedPlayer)
    {
        if (TryGetLoadedPeerPlayer(peer, out loadedPlayer) &&
            loadedPlayer != null &&
            loadedPlayer.gameObject != null)
        {
            position = loadedPlayer.transform.position;
            return true;
        }

        loadedPlayer = null;

        if (!peer.m_characterID.IsNone() && ZDOMan.instance != null)
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
        if (peer.m_characterID.IsNone() || ZNetScene.instance == null)
        {
            return false;
        }

        GameObject? instance = ZNetScene.instance.FindInstance(peer.m_characterID);
        return instance != null && instance.TryGetComponent(out player);
    }

    private static float GetDistanceSquaredXZ(Vector3 source, Vector3 target)
    {
        float dx = source.x - target.x;
        float dz = source.z - target.z;
        return dx * dx + dz * dz;
    }
}
