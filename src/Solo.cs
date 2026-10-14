using AL;
using AL.H;
using AL.User;
using Character;
using HarmonyLib;
using UnityEngine;

namespace AL_HWatchers;

// H with a girl who has no partner has no second girl, so the game offers no 3P. The game takes the second girl
// from the main girl's partner link (NPCData.Action.LinkPartnerUniqueID), so when H is agreed or started with a
// girl who has none, she is linked to another girl on the map for the scene, and the links are put back when H
// ends. Any watcher can then be swapped in as the 3P girl.
internal static class Solo
{
    private static readonly List<(NPCData npc, int was)> _links = new();

    internal static void Before(EventManager.Parameter param, string where)
    {
        try
        {
            var her = param?.Heroine;
            var data = her?.Data;
            if (data?.Action == null) { Plugin.Debug($"Solo H ({where}): no girl."); return; }
            var name = Name(data);
            var link = data.Action.LinkPartnerUniqueID;
            var game = Manager.Game.Instance;
            var partner = link != 0 && link != -1 ? game?.GetHeroin_UniqueID(link) : null;
            Plugin.Debug($"Solo H ({where}): {name}, partner link {link}{(partner != null ? $" ({Name(partner.Data)}, here)" : " (none here)")}.");
            if (!Plugin.SoloSecondGirl.Value || partner != null || _links.Count > 0) return;

            var pick = Pick(her);
            if (pick == null) { Plugin.Debug("Solo H: no other girl with hearts to pair her with."); return; }
            _links.Add((data, link));
            _links.Add((pick.Data, pick.Data.Action.LinkPartnerUniqueID));
            data.Action.LinkPartnerUniqueID = pick.Data.UniqueID;
            pick.Data.Action.LinkPartnerUniqueID = data.UniqueID;
            Plugin.Debug($"Solo H: {name} is paired with {Name(pick.Data)} for this H.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Solo H ({where}): {e.Message}"); }
    }

    // A girl with hearts and no partner of her own here: on this map and nearest first. Every girl looked at is
    // logged with the reason she was passed over.
    private static Heroine Pick(Heroine main)
    {
        var game = Manager.Game.Instance;
        var all = game?._heroines;
        if (all == null) { Plugin.Debug("Solo H: the game has no list of girls."); return null; }
        int mapId = -1;
        try { mapId = Manager.MapManager.Instance._mapID; } catch { }
        Vector3 at;
        try { at = main.Human.GameObject.transform.position; } catch { at = Vector3.zero; }
        Heroine best = null;
        var bestScore = float.MaxValue;
        var seen = new List<string>();
        for (var i = 0; i < all.Count; i++)
        {
            var h = all[i];
            var d = h?.Data;
            if (d?.Action == null || d.UniqueID == main.Data.UniqueID) continue;
            int hearts;
            try { hearts = d.GameParameter.Favorability.LV; } catch { hearts = -1; }
            var l = d.Action.LinkPartnerUniqueID;
            var hasHuman = false;
            try { hasHuman = h.Human != null && h.Human.GameObject != null; } catch { }
            var why = hearts == 0 ? "0 hearts" : l != 0 && l != -1 && game.GetHeroin_UniqueID(l) != null ? "has a partner" : "";
            seen.Add($"{Name(d)} (map {d.Action.MapID}, hearts {hearts}, body {(hasHuman ? "yes" : "no")}{(why != "" ? ", " + why : "")})");
            if (why != "") continue;
            var score = d.Action.MapID == mapId ? 0f : 1e6f;
            try { if (hasHuman) score += (h.Human.GameObject.transform.position - at).sqrMagnitude; else score += 1e5f; } catch { score += 1e5f; }
            if (score < bestScore) { bestScore = score; best = h; }
        }
        Plugin.Debug($"Solo H: map {mapId}, girls: {(seen.Count > 0 ? string.Join("; ", seen) : "none")}.");
        return best;
    }

    private static string Name(NPCData d) { try { return d?.HumanData?.Parameter?.fullname ?? "?"; } catch { return "?"; } }

    internal static void AtStart(HScene scene)
    {
        try
        {
            var hs = scene._parameter_k__BackingField.Humans;
            var names = new List<string>();
            if (hs != null)
                for (var i = 0; i < hs.Length; i++)
                    names.Add(hs[i] == null ? "-" : $"{(hs[i].Sex == 0 ? "M" : "F")}:{hs[i].Data?.Parameter?.fullname}");
            Plugin.Debug($"H starts with [{string.Join(", ", names)}].");
        }
        catch { }
    }

    internal static void End()
    {
        if (_links.Count == 0) return;
        foreach (var (npc, was) in _links)
            try { npc.Action.LinkPartnerUniqueID = was; } catch { }
        Plugin.Debug("Solo H: the girls' partner links are back as they were.");
        _links.Clear();
    }
}

internal static class SoloHooks
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(EventManager), nameof(EventManager.GetHAgreeAsset))]
    public static void Pre_HAgree(EventManager.Parameter param) => Solo.Before(param, "agree");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EventManager), nameof(EventManager.GetHStartAsset))]
    public static void Pre_HStart(EventManager.Parameter param) => Solo.Before(param, "start");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(HScene), nameof(HScene.Start))]
    public static void Pre_Start(HScene __instance) => Solo.AtStart(__instance);
}
