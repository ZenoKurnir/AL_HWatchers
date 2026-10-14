using AL.H;
using UnityEngine;

namespace AL_HWatchers;

// The game's move tool (bottom right in H) moves whatever its root transform is. While a watcher's menu is open
// and the tool is on, the tool's root is pointed at her (or the couple she is in), so the game's own arrows
// move and turn her; she then stays where she was put. The scene's root is put back when the menu or the tool
// closes.
internal static class Moving
{
    private static AL.H.UI.MoveSetting _ms;
    private static bool _wasOn;
    private static Transform _root;
    private static Vector3 _backupPos;
    private static Quaternion _backupRot;
    private static Watcher _who;
    private static Vector3 _startPos;
    private static Quaternion _startRot;

    internal static Watcher Who => _who;

    internal static void Tick(HScene scene)
    {
        AL.H.UI.MoveSetting ms = null;
        var on = false;
        try { ms = scene?.Option?._moveSetting; on = ms != null && ms.Active; } catch { }
        if (on != _wasOn)
        {
            _wasOn = on;
            bool a = false, b = false;
            try { a = scene.Option.IsMove; b = ms.IsMove; } catch { }
            Plugin.Debug($"Move tool {(on ? "open" : "closed")} (option moving {a}, tool moving {b}).");
        }
        var w = Watchers.Selected;
        var target = w == null ? null : w.Lover != null && !w.LoverLeads ? w.Lover : w;
        if (!on || target?.Human == null || target.Gathered) { Release(); return; }
        if (_who == target && _ms != null && _ms.Pointer == ms.Pointer)
        {
            var t = target.Human.Transform;
            if (!target.Pinned && ((t.position - _startPos).sqrMagnitude > 1e-6f || Quaternion.Angle(t.rotation, _startRot) > 0.1f))
            {
                target.Pinned = true;
                Plugin.Debug($"{target.Name} was moved by hand; she stays there.");
            }
            return;
        }
        Release();
        try
        {
            var t = target.Human.Transform;
            _ms = ms; _who = target;
            _root = ms._transformRoot; _backupPos = ms._positionBackup; _backupRot = ms._rotationBackup;
            ms._transformRoot = t;
            ms._positionBackup = t.position;
            ms._rotationBackup = t.rotation;
            _startPos = t.position; _startRot = t.rotation;
            Plugin.Debug($"Move tool now moves {target.Name}{(target.Lover != null ? $" and {target.Lover.Name}" : "")} (scene root was {(_root != null ? _root.name : "none")}).");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Move tool: {e.Message}"); _ms = null; _who = null; }
    }

    internal static void Release()
    {
        if (_ms == null) return;
        try
        {
            _ms._transformRoot = _root;
            _ms._positionBackup = _backupPos;
            _ms._rotationBackup = _backupRot;
            Plugin.Debug("Move tool back on the scene.");
        }
        catch { }
        _ms = null; _who = null; _root = null;
    }

    internal static void Forget(Watcher w) { if (_who == w) Release(); }
}
