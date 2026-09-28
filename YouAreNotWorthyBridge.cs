using System;
using System.Linq.Expressions;
using System.Reflection;

namespace BossRules;

// No assembly reference: YNW remains optional. Resolve once after its soft dependency
// has loaded; warmed key queries do not allocate reflection argument arrays.
internal static class YouAreNotWorthyBridge
{
    internal const string PluginGuid = "sighsorry.YouAreNotWorthy";
    private delegate int LocalItemQuery(string item, out string key);
    private delegate int PeerItemQuery(ZNetPeer peer, string item, out string key);
    private static Func<string, int>? _localKey;
    private static Func<ZNetPeer, string, int>? _peerKey;
    private static LocalItemQuery? _localItem;
    private static PeerItemQuery? _peerItem;
    private static Func<string, bool>? _showMissing;
    private static bool _resolved;

    internal static bool Available
    {
        get
        {
            if (!_resolved) Resolve();
            return _localKey != null && _peerKey != null && _localItem != null && _peerItem != null;
        }
    }

    private static void Resolve()
    {
        _resolved = true;
        Type? api = Type.GetType("YouAreNotWorthy.YouAreNotWorthyApi, YouAreNotWorthy", false);
        if (api == null) return;
        try
        {
            _localKey = Bind<Func<string, int>>(api, "QueryLocal", typeof(string));
            _peerKey = Bind<Func<ZNetPeer, string, int>>(api, "QueryPeer", typeof(ZNetPeer), typeof(string));
            _localItem = Bind<LocalItemQuery>(api, "QueryLocalItemUse", typeof(string), typeof(string).MakeByRefType());
            _peerItem = Bind<PeerItemQuery>(api, "QueryPeerItemUse", typeof(ZNetPeer), typeof(string), typeof(string).MakeByRefType());
            _showMissing = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>),
                api.GetMethod("TryShowLocalMissingRequirement")!);
        }
        catch (Exception ex)
        {
            _localKey = null;
            BossRulesPlugin.BossRulesLogger.LogWarning("Personal progression summons require YNW's item-use API: " + ex.Message);
        }
    }

    private static T Bind<T>(Type api, string name, params Type[] types) where T : Delegate
    {
        MethodInfo method = api.GetMethod(name, types) ?? throw new MissingMethodException(api.FullName, name);
        ParameterExpression[] parameters = Array.ConvertAll(types, type => Expression.Parameter(type));
        return Expression.Lambda<T>(Expression.Convert(Expression.Call(method, parameters), typeof(int)), parameters).Compile();
    }

    internal static int Key(string key, ZNetPeer? peer = null) => !Available ? 1
        : peer == null ? _localKey!(key) : _peerKey!(peer, key);

    internal static bool CanUseItem(string item, out string missingKey, ZNetPeer? peer = null)
    {
        missingKey = "";
        return Available && (peer == null ? _localItem!(item, out missingKey)
            : _peerItem!(peer, item, out missingKey)) == 2;
    }

    internal static void ShowMissing(string key)
    {
        if (Available && key.Length > 0) _showMissing?.Invoke(key);
    }
}
