using AL.H;
using AL.User;
using Character;
using UnityEngine;

namespace AL_HWatchers;

// Swapping a watcher into the scene. The H scene can't take a new actor once it has started, so the actor stays
// and her body is rebuilt from the watcher's card (Human.Data + Reload), keeping her pose, outfit type, clothes
// state and marks. Her voice follows the new girl. The game's own bookkeeping (hearts, H counts, results) still
// goes to the girl the scene started with. The girl who was in the scene takes the watcher's spot.
// The second girl's slot (an inactive actor until 3P) can be changed the same way, so a watcher joins the 3P.
internal sealed class SwapSlot
{
    public int Index;
    public HumanData Original;        // the actor's own data object; never written to, put back when H ends
    public int OriginalId;
    public int Personality;
    public float Pitch;
    public HumanData ActorData;
    public HumanDataParameter ActorParameter;
    public HumanDataStatus ActorStatus;
    public HumanData Backup;          // a copy of the girl's save data, to check nothing leaked into it
    public int ShownId;
    public HActor Actor;              // kept here: the scene empties its actor list as it ends
    public Human Human;
    public bool Swapped;              // it showed someone else at some point
    public float CheckBlendAt = -1f;
    public bool Pending;              // the 3P slot is rebuilt when she actually joins (an inactive body loads badly)
    public float ActiveSince = -1f;   // the game dresses her as she joins; the rebuild waits until it's done
    public int Coord = -1;           // the outfit type the scene started with
}

internal static partial class Watchers
{
    private static readonly Dictionary<int, SwapSlot> _slots = new();
    internal static bool AnySwapped => _slots.Values.Any(s => s.Swapped);
    // What each girl was wearing when she last left or joined the scene, and who got tired (H "weakness") in it;
    // both last until the scene ends.
    private static readonly Dictionary<int, int> _outfits = new();
    private static readonly HashSet<int> _tired = new();

    private static void ResetMemory() { _outfits.Clear(); _tired.Clear(); }

    // The girls of the scene the menu can swap with: (actor index, the girl it shows, whether it is the 3P slot).
    public static List<(int index, string name, bool active)> SwapTargets()
    {
        var result = new List<(int, string, bool)>();
        if (_scene == null || !Plugin.Swaps.Value) return result;
        try
        {
            var actors = _scene.Actors;
            for (var i = 0; i < actors.Length; i++)
            {
                var a = actors[i];
                if (a?.Human == null || a.IsManOrFutanari || a.IsPC || a.NPCData == null) continue;
                var npc = FindNpc(ShownId(i, a));
                var name = SafeName(() => npc.HumanData.Parameter.fullname) ?? a.Name;
                result.Add((i, name, a.Active));
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Listing the scene's girls failed: {e.Message}"); }
        return result;
    }

    public static bool CanSwap(Watcher w) =>
        Plugin.Swaps.Value && w?.Human != null && !w.Locked && !_wasPut && w.Data != null;

    public static void StandUp(Watcher w) { if (w.Gathered) EndGather(w); }

    private static int ShownId(int index, HActor a) =>
        _slots.TryGetValue(index, out var s) ? s.ShownId : a.NPCData?.UniqueID ?? 0;

    private static NPCData FindNpc(int id)
    {
        var list = Manager.Game.Instance?.SaveData?.NPCDataList;
        if (list == null) return null;
        for (var i = 0; i < list.Count; i++)
            if (list[i] != null && list[i].UniqueID == id) return list[i];
        return null;
    }

    // The watcher goes into the scene in place of the girl at this actor slot.
    public static void SwapIn(Watcher w, int index)
    {
        if (!CanSwap(w)) return;
        var actors = _scene.Actors;
        if (index < 0 || index >= actors.Length || actors[index]?.Human == null) return;
        var actor = actors[index];
        var outId = ShownId(index, actor);
        var outNpc = FindNpc(outId);
        if (outNpc == null) { Plugin.Log.LogWarning($"Swap: no save data for girl {outId}."); return; }
        var inNpc = w.Data;
        var outName = SafeName(() => outNpc.HumanData.Parameter.fullname) ?? "her";

        if (w.Gathered) EndGather(w);
        if (w.Lover != null) EndLove(w);
        if (w.Tok != null) StopTouching(w);

        // Each keeps the outfit she has on: the watcher's goes into the scene, the girl leaving keeps hers.
        var inOutfit = Outfit(w);
        if (inOutfit >= 0) _outfits[inNpc.UniqueID] = inOutfit;
        if (actor.Active) try { _outfits[outId] = (int)actor.Human.FileStatus.coordinateType; } catch { }

        // The same screen fade the game uses when the position changes, so the girls trade places behind it.
        if (actor.Active) try { HScene.CrossFadeStart(); } catch { }
        if (!Show(index, inNpc)) return;

        if (actor.Active)
        {
            // She was the girl in the scene: the girl who comes in leaves her spot to her. If the girl coming in
            // was waiting as the 3P girl, the girl going out takes that slot instead, so nobody is in it twice.
            var wasSecond = w.SecondGirl;
            for (var i = 0; i < actors.Length; i++)
                if (i != index && actors[i]?.Human != null && !actors[i].IsManOrFutanari && ShownId(i, actors[i]) == inNpc.UniqueID)
                    Show(i, outNpc);
            // The entry keeping the 3P girl's spot now stands for the girl who came in, so the one going out
            // isn't brought back a second time when the 3P ends.
            foreach (var o in _list)
                if (o.Away && o.Id == outNpc.UniqueID) Retarget(o, inNpc);
            Dispose(w);
            w.Data = outNpc;
            w.Id = outNpc.UniqueID;
            w.Partner = outNpc.Action?.LinkPartnerUniqueID ?? 0;
            w.Name = outName;
            w.SecondGirl = wasSecond;
            w.Excitement = Mathf.Max(w.Excitement, 0.5f);
            Spawn(_scene, w);
            Plugin.Debug($"{SafeName(() => inNpc.HumanData.Parameter.fullname)} swapped in for {outName}.");
        }
        else
        {
            // The 3P slot: she will be the one who joins, and keeps watching until then.
            foreach (var o in _list) o.SecondGirl = false;
            w.SecondGirl = true;
            Plugin.Debug($"{w.Name} is the second girl now (instead of {outName}).");
        }
    }

    private static void Retarget(Watcher o, NPCData npc)
    {
        o.Data = npc;
        o.Id = npc.UniqueID;
        o.Partner = npc.Action?.LinkPartnerUniqueID ?? 0;
        o.Name = SafeName(() => npc.HumanData.Parameter.fullname) ?? npc.CharaFileName;
    }

    // The 3P slot waiting to be rebuilt: done the moment she joins.
    public static void WatchSlots()
    {
        if (_slots.Count == 0 || _scene == null) return;
        foreach (var slot in _slots.Values.ToList())
        {
            if (slot.CheckBlendAt > 0f && Time.realtimeSinceStartup >= slot.CheckBlendAt)
            {
                slot.CheckBlendAt = -1f;
                try
                {
                    var actors = _scene.Actors;
                    for (var i = 0; i < actors.Length; i++)
                        if (actors[i] != null && actors[i].Active)
                            Plugin.Debug($"Swap check: actor {i} ({actors[i].Name}) height {SafeF(() => actors[i].Height):0.###}, animator floats {AnimFloats(actors[i].Human?.Body?.animBody)}.");
                }
                catch { }
            }
            if (!slot.Pending) continue;
            bool active;
            try { active = slot.Actor != null && slot.Actor.Active && slot.Human?.GameObject != null && slot.Human.GameObject.activeInHierarchy; }
            catch { continue; }
            if (!active) { slot.ActiveSince = -1f; continue; }
            var now = Time.realtimeSinceStartup;
            if (slot.ActiveSince < 0f) slot.ActiveSince = now;
            if (now - slot.ActiveSince < 0.6f) continue;
            slot.Pending = false;
            var npc = FindNpc(slot.ShownId);
            if (npc != null) Apply(slot, npc);
        }
    }

    // Rebuilds the actor at this slot as the given girl (later, if she isn't in the scene yet).
    private static bool Show(int index, NPCData npc)
    {
        var actor = _scene.Actors[index];
        var h = actor.Human;
        if (!_slots.TryGetValue(index, out var slot))
        {
            slot = new SwapSlot
            {
                Index = index, Actor = actor, Human = h, Original = h.Data, OriginalId = actor.NPCData.UniqueID, ShownId = actor.NPCData.UniqueID,
                Personality = actor.Personality, Pitch = actor.VoicePitch,
                ActorData = actor.Data, ActorParameter = actor.Parameter, ActorStatus = actor.Status
            };
            try
            {
                slot.Backup = new HumanData();
                slot.Backup.CopyWithoutPngData(actor.NPCData.HumanData);
            }
            catch { slot.Backup = null; }
            _slots[index] = slot;
            Plugin.Debug($"Swap slot {index} ({actor.Name}, active {actor.Active}): human data is the save's own " +
                               $"{(h.Data?.Pointer == actor.NPCData.HumanData?.Pointer)}, actor data is human data {(actor.Data?.Pointer == h.Data?.Pointer)}, " +
                               $"backup is human data {(actor.BackupData?.Pointer == h.Data?.Pointer)}, personality {actor.Personality}, pitch {actor.VoicePitch:0.00}.");
        }
        if (!actor.Active)
        {
            slot.ShownId = npc.UniqueID;
            slot.Pending = true;
            Plugin.Debug($"Swap slot {index} will show {SafeName(() => npc.HumanData.Parameter.fullname)} when she joins.");
            return true;
        }
        slot.Pending = false;
        return Apply(slot, npc);
    }

    private static bool Apply(SwapSlot slot, NPCData npc)
    {
        var index = slot.Index;
        var actor = slot.Actor;
        var h = slot.Human;
        var t0 = Time.realtimeSinceStartup;

        HumanData data;
        if (npc.UniqueID == slot.OriginalId) data = slot.Original;
        else
        {
            data = new HumanData();
            data.CopyWithoutPngData(npc.HumanData);
        }

        // What the scene has done to her so far, to carry over to the new body.
        if (slot.Coord < 0)
            try { slot.Coord = (int)h.FileStatus.coordinateType; } catch { }
        var coord = !_ending && _outfits.TryGetValue(npc.UniqueID, out var worn) && worn >= 0 ? worn : slot.Coord;
        if (coord >= 0 && data != slot.Original)
            try { data.Status.coordinateType = coord; } catch { }
        var clothes = new Dictionary<HumanCloth.Define.ClothesKind, HumanCloth.Define.ClothesState>();
        try
        {
            foreach (HumanCloth.Define.ClothesKind kind in Enum.GetValues(typeof(HumanCloth.Define.ClothesKind)))
                if (h.Cloth.IsClothesStateKind(kind)) clothes[kind] = h.Cloth.GetClothesStateType(kind);
        }
        catch { }
        var siru = new Dictionary<Human.Define.SiruParts, Human.SiruLv>();
        try
        {
            foreach (Human.Define.SiruParts part in Enum.GetValues(typeof(Human.Define.SiruParts)))
                siru[part] = h.GetSiruFlags(part);
        }
        catch { }
        var anim = h.Body?.animBody;
        RuntimeAnimatorController controller = null;
        int state = 0;
        float time = 0f, speed = 1f;
        if (anim != null)
        {
            controller = anim.runtimeAnimatorController;
            speed = anim.speed;
            try { var info = anim.GetCurrentAnimatorStateInfo(0); state = info.fullPathHash; time = info.normalizedTime; } catch { }
        }
        var layer = -1;
        foreach (var r in h.GameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.enabled) { layer = r.gameObject.layer; break; }
        var pos = h.Transform.position;
        var rot = h.Transform.rotation;

        var looksBefore = Looks(h);
        var oldCloth = h.Cloth;
        Plugin.Debug($"Swap slot {index}: clothes state before: {StateLinks(actor, h)}.");
        var heightBefore = (actor._realHeight, SafeF(() => actor.Height), SafeF(() => actor.RealTimeHeight));
        // A girl who got tired stays tired for the rest of the scene, as a watcher too.
        if (actor.Active && !_ending && SafeB(() => actor.IsWeakness)) _tired.Add(slot.ShownId);
        try { Rebuild(h, data, coord); }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Swap: rebuilding the girl failed: {e}");
            try { Rebuild(h, slot.Original, coord); } catch { }
            return false;
        }
        var looksAfter = Looks(h);
        Plugin.Debug($"Swap slot {index}: {looksAfter.Count(n => !looksBefore.Contains(n))} of {looksAfter.Count} body, hair and clothes pieces are new.");
        // As H ends she gets dressed again, the way the game leaves her: nothing from the scene carries over.
        if (_ending)
        {
            foreach (var k in clothes.Keys.ToList()) clothes[k] = HumanCloth.Define.ClothesState.Clothing;
            foreach (var k in siru.Keys.ToList()) siru[k] = Human.SiruLv.None;
        }
        foreach (var kv in clothes.ToList())
        {
            try
            {
                if (!h.Cloth.IsExist(kv.Key)) continue;
                var st = kv.Value;
                // Not every piece has a half-off look; one without it shows a hole instead, so it comes off.
                var kinds = h.Cloth.GetClothesStateKind(kv.Key);
                if (st != HumanCloth.Define.ClothesState.Clothing && kinds != null && !kinds.ContainsKey(st))
                    st = kinds.ContainsKey(HumanCloth.Define.ClothesState.Naked) ? HumanCloth.Define.ClothesState.Naked : HumanCloth.Define.ClothesState.Clothing;
                clothes[kv.Key] = st;
                h.Cloth.SetClothesState(kv.Key, st, true);
            }
            catch { }
        }
        // Pieces the old girl didn't have keep whatever the clothes kept for that slot (a missing piece's state),
        // so the menu couldn't put them on. She starts wearing them, the way she'd come into the scene.
        var fresh = new List<string>();
        foreach (HumanCloth.Define.ClothesKind kind in Enum.GetValues(typeof(HumanCloth.Define.ClothesKind)))
        {
            try
            {
                if (clothes.ContainsKey(kind) || !h.Cloth.IsExist(kind)) continue;
                h.Cloth.SetClothesState(kind, HumanCloth.Define.ClothesState.Clothing, true);
                fresh.Add($"{kind}={h.Cloth.GetClothesStateType(kind)}");
            }
            catch { }
        }
        if (fresh.Count > 0) Plugin.Debug($"Swap slot {index}: pieces the old girl didn't have, now worn: {string.Join(", ", fresh)}.");
        // The body hides the skin under her clothes with alpha masks; make them follow the new clothes.
        try
        {
            h.Cloth.SetUpdateClothesStateAll();
            foreach (HumanCloth.Define.ClothesKind kind in Enum.GetValues(typeof(HumanCloth.Define.ClothesKind)))
                try { h.Cloth.UpdateAlphaMask(kind, HumanCloth.UpdateAlphaMaskFlags.ALL); } catch { }
            h.Cloth.UpdateVisible();
            h.UpdateVisible();
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Swap: alpha masks: {e.Message}"); }
        var hidden = new List<string>();
        try
        {
            foreach (var r in h.GameObject.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("o_") && (!r.enabled || !r.gameObject.activeInHierarchy)) hidden.Add(r.name);
        }
        catch { }
        Plugin.Debug($"Swap slot {index}: clothes {string.Join(", ", clothes.Select(kv => $"{kv.Key}={kv.Value}"))}; hidden pieces: {string.Join(", ", hidden)}.");
        try
        {
            foreach (var kv in siru) h.SetSiruFlags(kv.Key, kv.Value);
            h.Body?.UpdateSiru();
            h.Face?.UpdateSiru();
        }
        catch { }
        h.Transform.SetPositionAndRotation(pos, rot);
        var newAnim = h.Body?.animBody;
        if (newAnim != null)
        {
            if (controller != null && newAnim.runtimeAnimatorController != controller) newAnim.runtimeAnimatorController = controller;
            newAnim.speed = speed;
            if (state != 0) newAnim.Play(state, 0, time);
            if (actor.Animator == null || actor.Animator.Pointer != newAnim.Pointer)
            {
                Plugin.Debug("Swap: the animator changed with the new body.");
                actor.Animator = newAnim;
            }
        }
        if (layer >= 0)
            foreach (var r in h.GameObject.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.layer != layer) r.gameObject.layer = layer;

        if (npc.UniqueID == slot.OriginalId)
        {
            actor.Data = slot.ActorData;
            actor.Parameter = slot.ActorParameter;
            actor.Status = slot.ActorStatus;
            actor.Personality = slot.Personality;
            actor.VoicePitch = slot.Pitch;
        }
        else
        {
            actor.Data = data;
            actor.Parameter = data.Parameter;
            actor.Status = data.Status;
            try { actor.Personality = data.Parameter.personality; } catch { }
            try { actor.VoicePitch = data.Parameter.voicePitch; } catch { }
        }
        slot.ShownId = npc.UniqueID;
        if (npc.UniqueID != slot.OriginalId) slot.Swapped = true;
        LinkClothesState(actor, h);
        Plugin.Debug($"Swap slot {index}: clothes state after: {StateLinks(actor, h)}.");
        if (!_ending) RebindClothesMenu(actor, oldCloth, h.Cloth);
        // Her height is measured once when the scene loads; measure the new body.
        try
        {
            var live = actor.RealTimeHeight;
            if (live > 0f && Mathf.Abs(live - actor._realHeight) > 0.0001f) actor._realHeight = live;
        }
        catch { }
        Plugin.Debug($"Swap slot {index}: height (stored, height, live) before {heightBefore}, after ({actor._realHeight:0.###}, {SafeF(() => actor.Height):0.###}, {SafeF(() => actor.RealTimeHeight):0.###}); " +
                           $"animator floats {AnimFloats(h.Body?.animBody)}.");
        Revoice(actor);
        if (actor.Active && !_ending) { SetTired(actor, _tired.Contains(npc.UniqueID)); RefitPosture(); Blend(slot); }
        Plugin.Debug($"Swap slot {index} now shows {SafeName(() => npc.HumanData.Parameter.fullname)} " +
                           $"(personality {actor.Personality}, pitch {actor.VoicePitch:0.00}, {(Time.realtimeSinceStartup - t0) * 1000:0} ms).");
        return true;
    }

    // Her H voice: the scene's voice player for this actor keeps its own personality and pitch, and loads the
    // voice lists for them when the position is set up.
    private static void Revoice(HActor actor)
    {
        try
        {
            var hv = _scene.VoiceCtrl;
            var vc = hv?.GetVoiceCtrl(actor);
            if (vc == null) { Plugin.Debug("Swap: no voice player for this girl."); return; }
            var was = vc.Personality;
            vc.Stop();
            vc.Personality = actor.Personality;
            vc.VoicePitch = actor.VoicePitch;
            // Her lines were loaded for the old personality when the scene started; load them again.
            var lists = was == vc.Personality ? "same personality" : ReloadVoiceLists(vc);
            hv.Setup(hv.Category, hv.SubCategory, hv.VoiceCategory, hv._isWeakness, hv._isReverse);
            Plugin.Debug($"Swap: voice personality {was} -> {vc.Personality} ({lists}).");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Swap: voice: {e.Message}"); }
    }

    // The lists live in h/list/sound/voice/{voice,breath,short}/*.unity3d as TextAssets named voice[007] etc.
    // Which path string the loaders want isn't known, so a few are tried on fresh tables; if none loads anything
    // the old tables are put back and she keeps the old voice.
    private static string ReloadVoiceLists(AL.H.Sound.VoiceCtrl vc)
    {
        var p = vc.Personality;
        var parts = new List<string>();

        var oldVoice = vc._voiceData;
        string used = null;
        foreach (var path in new[] { "h/list/sound/voice/voice/", "h/list/sound/voice/voice", $"voice[{p:000}]" })
        {
            vc._voiceData = new Il2CppSystem.Collections.Generic.Dictionary<AL.H.List.Voice.VoiceData.Data.GroupEnum, ILLGAMES.Collections.Generic.IntKeyDictionary<AL.H.List.Voice.VoiceData.Data>>();
            var x = path;
            try { vc.LoadVoiceData(ref x); } catch (Exception e) { parts.Add($"voice '{path}' failed: {e.Message}"); }
            if (vc._voiceData != null && vc._voiceData.Count > 0) { used = path; break; }
        }
        if (used == null) vc._voiceData = oldVoice;
        parts.Add($"voice {(used != null ? $"'{used}' {vc._voiceData.Count} groups" : "kept old")}");

        var oldShort = vc._shortBreathData;
        used = null;
        foreach (var path in new[] { "h/list/sound/voice/short/", "h/list/sound/voice/short", $"short[{p:000}]" })
        {
            vc._shortBreathData = new Il2CppSystem.Collections.Generic.Dictionary<AL.H.List.Voice.ShortBreathData.Data.GroupEnum, ILLGAMES.Collections.Generic.IntKeyDictionary<AL.H.List.Voice.ShortBreathData.Data>>();
            var x = path;
            try { vc.LoadShortBreathData(ref x); } catch (Exception e) { parts.Add($"short '{path}' failed: {e.Message}"); }
            if (vc._shortBreathData != null && vc._shortBreathData.Count > 0) { used = path; break; }
        }
        if (used == null) vc._shortBreathData = oldShort;
        parts.Add($"short {(used != null ? $"'{used}'" : "kept old")}");

        var breath = vc._breathData;
        if (breath != null)
        {
            var old = new List<Il2CppSystem.Collections.Generic.Dictionary<AL.H.List.Voice.BreathData.Data.GroupEnum, ILLGAMES.Collections.Generic.IntKeyDictionary<AL.H.List.Voice.BreathData.Data>>>();
            for (var i = 0; i < breath.Length; i++) old.Add(breath[i]);
            used = null;
            foreach (var path in new[] { "h/list/sound/voice/breath/", "h/list/sound/voice/breath", $"breath[{p:000}]" })
            {
                for (var i = 0; i < breath.Length; i++)
                    breath[i] = new Il2CppSystem.Collections.Generic.Dictionary<AL.H.List.Voice.BreathData.Data.GroupEnum, ILLGAMES.Collections.Generic.IntKeyDictionary<AL.H.List.Voice.BreathData.Data>>();
                var x = path;
                try { vc.LoadBreathData(ref x); } catch (Exception e) { parts.Add($"breath '{path}' failed: {e.Message}"); }
                var n = 0;
                for (var i = 0; i < breath.Length; i++) if (breath[i] != null) n += breath[i].Count;
                if (n > 0) { used = path; break; }
            }
            if (used == null) for (var i = 0; i < breath.Length; i++) breath[i] = old[i];
            parts.Add($"breath {(used != null ? $"'{used}'" : "kept old")}");
        }
        try { vc._voiceHistory?.Clear(); vc._breathHistory?.Clear(); vc._shortHistory?.Clear(); } catch { }
        return string.Join(", ", parts);
    }

    // The position again, so the motions are the ones for her height (the game picks them when it sets one up).
    private static void RefitPosture()
    {
        try
        {
            var info = _scene.NowAnimationInfo;
            if (info == null) return;
            var (id, mode) = (info.ID, info.Mode);
            _scene.ChangePosture(mode, id, _scene.IsWeakness, false);
            var after = _scene.NowAnimationInfo;
            var actors = _scene.Actors;
            for (var i = 0; i < actors.Length; i++)
                if (actors[i] != null && actors[i].Active)
                    Plugin.Debug($"Swap: actor {i} ({actors[i].Name}) height {SafeF(() => actors[i].Height):0.###}, animator floats {AnimFloats(actors[i].Human?.Body?.animBody)}.");
            Plugin.Debug($"Swap: position set up again: {info.NameAnimation} (mode {mode}, id {id}) -> {after?.NameAnimation} (mode {after?.Mode ?? -1}, id {after?.ID ?? -1}).");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Swap: setting the position up again failed: {e.Message}"); }
    }

    private static bool _ending;

    public static void EndScene() { RestoreSlots(false); ResetMemory(); }

    // The girl coming in is tired only if she got tired earlier in this scene. Tiredness is kept per girl
    // (hStatus) and, for the main girl, in the scene's counter that picks the tired positions.
    private static void SetTired(HActor actor, bool tired)
    {
        try
        {
            var before = $"{SafeB(() => actor.IsWeakness)}/{SafeB(() => _scene.IsWeakness)}";
            actor.hStatus?.SetWeakness(tired);
            var main = false;
            var actors = _scene.Actors;
            for (var i = 0; i < actors.Length; i++)
                if (actors[i] != null && actors[i].Active && !actors[i].IsManOrFutanari) { main = actors[i].Pointer == actor.Pointer; break; }
            if (main) { var c = _scene._Counter_k__BackingField; if (c != null) c.IsWeakness = tired; }
            Plugin.Debug($"Swap: tired (her/scene) {before} -> {SafeB(() => actor.IsWeakness)}/{SafeB(() => _scene.IsWeakness)}{(main ? " (main girl)" : "")}.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Swap: tiredness: {e.Message}"); }
    }

    internal static bool WasTired(int id) => _tired.Contains(id);

    internal static int RememberedOutfit(int id) => _outfits.TryGetValue(id, out var o) ? o : -1;

    // Night H after the bath: the girl wears her after-bath clothes, and so does everyone watching.
    internal static bool SceneWearsBath()
    {
        try
        {
            var actors = _scene?.Actors;
            if (actors == null) return false;
            for (var i = 0; i < actors.Length; i++)
                if (actors[i]?.Human != null && actors[i].Active && !actors[i].IsManOrFutanari &&
                    actors[i].Human.FileStatus.coordinateType == (int)HumanCoordinate.Define.CoordinateType.AfterBath) return true;
        }
        catch { }
        return false;
    }

    // The game reads the girls for what follows H (the talk after it, who is busy on the map) while it fades out,
    // before the scene is disposed, so everyone gets her own body back as soon as the scene starts ending.
    public static void WatchEnd()
    {
        if (_slots.Count == 0 || _scene == null) return;
        bool ended;
        try { ended = _scene.IsEnded; } catch { return; }
        if (!ended) return;
        Plugin.Debug("H is ending: the girls in the scene get their own bodies back.");
        _ending = true;
        try { RestoreSlots(true); } finally { _ending = false; }
    }

    // Data first (for the girl the slot started with, her own data object), then the outfit she is wearing, then everything is
    // rebuilt: Reload() alone only redoes the body and keeps the old hair, clothes and accessories.
    private static void Rebuild(Human h, HumanData data, int coord)
    {
        h.Data = data;
        if (coord >= 0)
        {
            // The outfit she wears is a separate copy (Coorde.Now), filled from a throwaway copy of the card so
            // nothing the scene does to the clothes can reach the girl's own save data.
            try
            {
                var copy = new HumanData();
                copy.CopyWithoutPngData(data);
                h.Coorde.SetNowCoordinate(copy.Coordinates[coord]);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Swap: outfit: {e.Message}"); }
        }
        var all = Human.ReloadParameter.All;
        h.Reload(ref all);
    }

    // The game's H clothes menu keeps the girl's clothes object and listens to it; after the rebuild it is pointed
    // at the new one, or its buttons act on a stale object (wrong state marks, missing half-off, and a crash).
    private static void RebindClothesMenu(HActor actor, HumanCloth oldCloth, HumanCloth newCloth)
    {
        try
        {
            var cs = _scene.Option?._clothesSetting;
            if (cs == null) { Plugin.Debug("Swap: no clothes menu to update."); return; }
            var changed = oldCloth == null || newCloth == null || oldCloth.Pointer != newCloth.Pointer;
            // The menu listens to the clothes object's change events; log whether the swap replaced them.
            var oldSub = oldCloth?._clothesStateUpdate;
            var newSub = newCloth?._clothesStateUpdate;
            Plugin.Debug($"Swap: clothes object {(changed ? "replaced" : "the same")}, change events " +
                               $"{(oldSub == null || newSub == null ? "missing" : oldSub.Pointer == newSub.Pointer ? "the same" : "replaced")}" +
                               $"{(oldSub != null && SafeB(() => oldSub.IsCompletedOrDisposed) ? " (old ones closed)" : "")}.");
            var rebound = new List<string>();
            var sc = cs._selectClothesState;
            if (sc?._actor != null && sc._actor.Pointer == actor.Pointer) { sc.SetActor(actor); sc.UpdateUI(); rebound.Add("clothes"); }
            var sa = cs._selectAccessoryState;
            if (sa?._actor != null && sa._actor.Pointer == actor.Pointer) { sa.SetActor(actor); sa.UpdateUI(); rebound.Add("accessories"); }
            var sk = cs._selectCoodinateCard;
            if (sk?._actor != null && sk._actor.Pointer == actor.Pointer) { sk.SetActor(actor); sk.UpdateUI(); rebound.Add("outfit cards"); }
            Plugin.Debug($"Swap: clothes menu panels set again: {(rebound.Count > 0 ? string.Join(", ", rebound) : "none open for her")}.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Swap: updating the clothes menu failed: {e.Message}"); }
    }

    // The clothes keep their states in one byte array that the girl's status shares; the H menu reads the
    // status. A rebuilt girl's status had its own array (all "full"), so the menu showed every piece as full and
    // the all-clothes button always went to half. Point her status at the clothes' array again.
    private static void LinkClothesState(HActor actor, Human h)
    {
        try
        {
            var arr = h.Cloth?._clothesState;
            if (arr == null) return;
            foreach (var st in new[] { actor.Status, h.Data?.Status })
                if (st != null && (st.clothesState == null || st.clothesState.Pointer != arr.Pointer)) st.clothesState = arr;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Swap: linking clothes state: {e.Message}"); }
    }

    private static string StateLinks(HActor actor, Human h)
    {
        string P(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase o) => o == null ? "-" : (o.Pointer.ToInt64() & 0xFFFFFF).ToString("X");
        try
        {
            var arr = h.Cloth?._clothesState;
            var a = actor.Status; var d = h.Data?.Status;
            string Vals(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> x) => x == null ? "-" : string.Join("", x.Select(b => b.ToString()));
            return $"clothes array {P(arr)} [{Vals(arr)}], actor status {P(a)} array {P(a?.clothesState)} [{Vals(a?.clothesState)}], " +
                   $"body status {P(d)} array {P(d?.clothesState)}";
        }
        catch (Exception e) { return $"failed: {e.Message}"; }
    }

    private static bool SafeB(Func<bool> f) { try { return f(); } catch { return false; } }

    // What the H clothes menu sees for a girl: each piece's state and which states the piece has.
    internal static string ClothesReport(HActor actor)
    {
        try
        {
            var c = actor?.Human?.Cloth;
            if (c == null) return "no clothes object";
            var parts = new List<string>();
            foreach (HumanCloth.Define.ClothesKind kind in Enum.GetValues(typeof(HumanCloth.Define.ClothesKind)))
            {
                if (!SafeB(() => c.IsClothesStateKind(kind))) continue;
                var kinds = c.GetClothesStateKind(kind);
                var has = new List<string>();
                foreach (HumanCloth.Define.ClothesState st in Enum.GetValues(typeof(HumanCloth.Define.ClothesState)))
                    if (kinds != null && SafeB(() => kinds.ContainsKey(st))) has.Add(st.ToString());
                parts.Add($"{kind}={c.GetClothesStateType(kind)} [{string.Join("/", has)}]");
            }
            return $"{SafeName(() => actor.Human.Data.Parameter.fullname)} cloth {c.Pointer.ToInt64():X}: {string.Join(", ", parts)}";
        }
        catch (Exception e) { return $"report failed: {e.Message}"; }
    }

    private static bool SameBytes(byte[] x, byte[] y)
    {
        if (x == null || y == null || x.Length != y.Length) return false;
        for (var i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
        return true;
    }

    // The animations blend by the girl's height and bust ("Height", "Breast" on the animator). The game sets them
    // when the scene loads and the man's animator takes the main girl's values, so they're set for the new girl.
    private static void Blend(SwapSlot slot)
    {
        var actor = slot.Actor;
        var height = SafeF(() => actor.Height);
        var breast = SafeF(() => actor.Breast);
        SetFloats(actor.Human?.Body?.animBody, height, breast);
        var actors = _scene.Actors;
        var firstGirl = -1;
        for (var i = 0; i < actors.Length; i++)
            if (actors[i] != null && actors[i].Active && !actors[i].IsManOrFutanari) { firstGirl = i; break; }
        if (firstGirl == slot.Index)
            for (var i = 0; i < actors.Length; i++)
                if (actors[i] != null && actors[i].Active && actors[i].IsManOrFutanari) SetFloats(actors[i].Human?.Body?.animBody, height, breast);
        slot.CheckBlendAt = Time.realtimeSinceStartup + 1.5f;
    }

    private static void SetFloats(Animator a, float height, float breast)
    {
        if (a == null) return;
        try
        {
            foreach (var p in a.parameters)
            {
                if (p.type != AnimatorControllerParameterType.Float) continue;
                if (p.name == "Height" && height >= 0f) a.SetFloat(p.nameHash, height);
                else if (p.name == "Breast" && breast >= 0f) a.SetFloat(p.nameHash, breast);
            }
        }
        catch { }
    }

    private static float SafeF(Func<float> f) { try { return f(); } catch { return -1f; } }

    private static string AnimFloats(Animator a)
    {
        try
        {
            if (a == null) return "none";
            var parts = new List<string>();
            foreach (var p in a.parameters)
                if (p.type == AnimatorControllerParameterType.Float) parts.Add($"{p.name}={a.GetFloat(p.nameHash):0.###}");
            return string.Join(" ", parts);
        }
        catch (Exception e) { return e.Message; }
    }

    private static HashSet<string> Looks(Human h)
    {
        var names = new HashSet<string>();
        try
        {
            foreach (var r in h.GameObject.GetComponentsInChildren<Renderer>(false))
                names.Add(r.name + "|" + (r.sharedMaterial != null ? r.sharedMaterial.name : ""));
        }
        catch { }
        return names;
    }

    // When H ends every actor gets her own data back, and her save data is checked.
    private static void RestoreSlots(bool rebuild)
    {
        if (_slots.Count == 0) return;
        try
        {
            foreach (var slot in _slots.Values.ToList())
            {
                // A body never rebuilt (the 3P slot before she joined) still is her own.
                var rebuilt = !slot.Pending || slot.Human?.Data?.Pointer != slot.Original?.Pointer;
                if (rebuild && rebuilt && slot.ShownId != slot.OriginalId && slot.Human?.GameObject != null)
                {
                    var own = FindNpc(slot.OriginalId);
                    try { if (own != null) Apply(slot, own); }
                    catch (Exception e) { Plugin.Log.LogWarning($"Swap: giving slot {slot.Index} its girl back failed: {e.Message}"); }
                }
                slot.Pending = false;
                var actor = slot.Actor;
                if (actor != null)
                {
                    try { if (slot.Human != null) slot.Human.Data = slot.Original; } catch { }
                    actor.Data = slot.ActorData;
                    actor.Parameter = slot.ActorParameter;
                    actor.Status = slot.ActorStatus;
                    actor.Personality = slot.Personality;
                    actor.VoicePitch = slot.Pitch;
                }
                var npc = FindNpc(slot.OriginalId);
                if (npc?.HumanData != null && slot.Backup != null && !SameLook(npc.HumanData, slot.Backup))
                {
                    Plugin.Log.LogWarning($"Swap: {SafeName(() => npc.HumanData.Parameter.fullname)}'s save data changed during the swap; putting it back.");
                    npc.HumanData.CopyWithoutPngData(slot.Backup);
                }
                else if (npc?.HumanData != null && slot.Backup != null && slot.Swapped)
                {
                    // The game's clothes menu edits the outfits of the girl the slot belongs to, even while it
                    // shows someone else: her outfits go back to what they were.
                    try
                    {
                        var coords = npc.HumanData.Coordinates;
                        if (!SameBytes(npc.HumanData.GetCoordinateBytes(), slot.Backup.GetCoordinateBytes()))
                        {
                            Plugin.Log.LogWarning($"Swap: {SafeName(() => npc.HumanData.Parameter.fullname)}'s outfits changed while someone else was in her place; putting them back.");
                            for (var j = 0; j < coords.Length && j < slot.Backup.Coordinates.Length; j++)
                                npc.HumanData.AssignCoordinate((HumanCoordinate.Define.CoordinateType)j, slot.Backup.Coordinates[j]);
                        }
                    }
                    catch (Exception e) { Plugin.Log.LogWarning($"Swap: checking outfits failed: {e.Message}"); }
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"Swap: restoring the scene's girls failed: {e}"); }
        _slots.Clear();
    }

    private static bool SameLook(HumanData a, HumanData b)
    {
        try
        {
            var x = a.GetCustomBytes();
            var y = b.GetCustomBytes();
            if (x.Length != y.Length) return false;
            for (var i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
            return a.Parameter.fullname == b.Parameter.fullname;
        }
        catch { return true; }
    }
}
