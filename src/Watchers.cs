using AL;
using AL.H;
using AL.User;
using Character;
using ILLGAMES.Unity.Animations;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace AL_HWatchers;

internal sealed class Watcher
{
    public NPCData Data;
    public int Id;
    public int Partner;          // her linked partner's id
    public string Name;
    public int Slot;
    public bool SecondGirl;
    public Human Human;          // a copy built from her card; the map's own girl stays hidden by the game
    public bool Away;            // she is in the scene as the 3P girl; her spot waits for her

    public float Excitement;     // 0..1, grows as the scene goes on
    public float Temper;         // how quickly this girl gets worked up
    public float NextPoseAt;
    public float ReactAt = -1f;  // a pending reaction to a climax
    public float ReactionUntil;
    public string Pose;
    public float NextGaspAt;
    public AudioSource Voice;
    public int Hearts = -1;      // favorability level; 0 means she only watches and every option is locked
    public bool Locked => Hearts == 0;

    public TokPlayer Tok;        // set while she is masturbating
    public TokPlayer TokFade;    // the last one, letting go of her body
    public string TokSet;
    public Dictionary<HumanCloth.Define.ClothesKind, (HumanCloth.Define.ClothesState was, HumanCloth.Define.ClothesState set)> TokClothes;
    public bool Hidden;          // hidden while the camera is inside her
    public int TokStage;
    public float TokStageUntil;

    public Watcher Lover;        // her partner while the two of them make love
    public bool LoverLeads;      // the one of the two that runs their timing
    public bool Pinned;          // moved by hand with the game's move tool: she stays where she was put
    public bool Tired;           // she got tired in the scene: she lies on the floor and does nothing else
    public int LoveStage;
    public float LoveStageUntil;
    public string LoveSet;       // "00" scissoring, "01" 69

    public bool Gathered;        // kneeling beside the girl for the bukkake
    public Vector3 GatherTarget; // where her hips should be
    public float GatherAt;
    public int GatherFixes;
    public float MarkAt;
    public Transform Hips;
    public string ResumeTok;     // what she was doing before the bukkake, to go back to it
    public int ResumeStage;
    public Mood Mood;
    public bool Wet;             // standing in water: no lying down, kneeling or crouching there
    public float NextFaceAt;
}

internal static partial class Watchers
{
    private static readonly List<Watcher> _list = new();
    private static readonly Queue<(NPCData data, bool second)> _pending = new();
    private static HScene _scene;
    private static float _autoAt;
    private static bool _autoDone;
    private static float _nextSpawnAt;
    private static float _scale;
    private static Vector3 _ringDir;
    private static RuntimeAnimatorController _mapAnimator;
    private static bool _wasFinished, _wasPut;
    private static string _lastState;
    private static Vector3 _anchor;
    private static bool _anchored;
    private static float _nextArrangeAt;
    private static readonly Dictionary<IntPtr, Transform[]> _actorBones = new();

    private const string MapBundle = "ani/mot/1/000_00.unity3d";
    private static readonly float[] SlotAngles = { 35f, -35f, 65f, -65f, 95f };

    private static IEnumerable<Watcher> Present => _list.Where(w => !w.Away && w.Human != null);
    public static Watcher Selected;

    public static void Update(HScene scene)
    {
        if (_scene == null || _scene.Pointer != scene.Pointer)
        {
            Clear("new H scene");
            RestoreSlots(false);
            ResetMemory();
            _scene = scene;
            _autoAt = Time.realtimeSinceStartup + 1.5f;
            _autoDone = false;
            _ringDir = Vector3.zero;
            _anchored = false;
            _actorBones.Clear();
        }
        if (!scene.Loaded) return;

        var now = Time.realtimeSinceStartup;
        if (!_autoDone && now > _autoAt)
        {
            _autoDone = true;
            if (Plugin.AutoBring.Value) QueueWatchers(scene, Plugin.MaxWatchers.Value);
        }

        if (Input.GetKeyDown(Plugin.WatcherKey.Value))
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) Clear("sent back");
            else QueueWatchers(scene, 1);
        }

        if (_pending.Count > 0 && now >= _nextSpawnAt)
        {
            // One girl at a time, so building them doesn't freeze the game in one go.
            var (data, second) = _pending.Dequeue();
            Spawn(scene, NewWatcher(data, second));
            _nextSpawnAt = now + 0.3f;
        }

        try { Moving.Tick(scene); } catch { }
        if (_list.Count == 0) return;
        if (Time.frameCount % 15 == 0) SyncWithActors(scene);
        if (now >= _nextArrangeAt)
        {
            _nextArrangeAt = now + 0.4f;
            try { Arrange(scene); } catch (Exception e) { if (Plugin.DebugLog.Value) Plugin.Log.LogWarning($"Arranging watchers failed: {e.Message}"); }
        }
        var intensity = ReadScene(scene, now);
        foreach (var w in Present) Tick(scene, w, now, intensity);
    }

    // ---------- who watches ----------

    private static void QueueWatchers(HScene scene, int count)
    {
        var room = Plugin.MaxWatchers.Value - _list.Count - _pending.Count;
        if (room <= 0)
        {
            Plugin.Debug($"All {Plugin.MaxWatchers.Value} watcher spots are taken.");
            return;
        }
        count = Math.Min(count, room);

        var active = ActiveActorIds(scene);
        var partner = PartnerOfActiveGirl(scene);
        var taken = new HashSet<int>(_list.Select(w => w.Id).Concat(_pending.Select(p => p.data.UniqueID)));
        var list = Manager.Game.Instance?.SaveData?.NPCDataList;
        if (list == null) return;
        int mapId = -1;
        try { mapId = Manager.MapManager.Instance._mapID; } catch { }

        var candidates = new List<(NPCData, bool)>();
        for (var i = 0; i < list.Count; i++)
        {
            var npc = list[i];
            if (npc == null || active.Contains(npc.UniqueID) || taken.Contains(npc.UniqueID)) continue;
            if (npc.UniqueID == partner) candidates.Insert(0, (npc, true));
            else if (npc.Action?.MapID == mapId) candidates.Add((npc, false));
        }
        if (candidates.Count == 0)
        {
            Plugin.Debug("No other girl on this map can come to watch.");
            return;
        }
        foreach (var c in candidates.Take(count)) _pending.Enqueue(c);
    }

    private static Watcher NewWatcher(NPCData data, bool second)
    {
        var w = new Watcher
        {
            Data = data, Id = data.UniqueID, Partner = data.Action?.LinkPartnerUniqueID ?? 0, SecondGirl = second,
            Name = SafeName(() => data.HumanData.Parameter.fullname) ?? data.CharaFileName,
            Excitement = Random.Range(0.05f, 0.2f), Temper = Random.Range(0.7f, 1.3f)
        };
        w.Slot = Enumerable.Range(0, SlotAngles.Length).First(i => _list.All(x => x.Slot != i));
        if (_list.Count == 0) _ringDir = Vector3.zero; // a new group: aim the ring at the current camera
        _list.Add(w);
        return w;
    }

    private static void Spawn(HScene scene, Watcher w)
    {
        var t0 = Time.realtimeSinceStartup;
        try
        {
            // Her own copy of the card, so nothing done to the watcher (outfits, clothes) reaches the save.
            var copy = new HumanData();
            copy.CopyWithoutPngData(w.Data.HumanData);
            w.Human = Human.Create(copy);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not build {w.Name} from her card: {e.Message}");
            _list.Remove(w);
            return;
        }
        w.Away = false;
        w.Hidden = false;
        w.Voice = null;
        try { w.Hearts = w.Data.GameParameter.Favorability.LV; } catch { w.Hearts = -1; }
        MatchOutfit(w);
        var anim = w.Human.Body?.animBody;
        if (anim != null)
        {
            if (_mapAnimator == null) _mapAnimator = LoadController(MapBundle, "f_00");
            if (_mapAnimator != null) anim.runtimeAnimatorController = _mapAnimator;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        w.Human.GameObject.SetActive(true);
        w.Tired = WasTired(w.Id);
        Place(scene, w);
        LookAtScene(scene, w);
        w.Pose = null;
        w.NextPoseAt = 0f;
        w.ReactionUntil = 0f;
        w.NextGaspAt = Time.realtimeSinceStartup + Random.Range(10f, 25f);
        if (w.Tired) LieDown(w);
        Plugin.Debug($"{w.Name} came to watch{(w.Tired ? " (tired)" : "")}, outfit {Outfit(w)}{(SceneWearsBath() ? " (night H, after-bath clothes)" : "")}{(w.SecondGirl ? " (the second girl)" : "")}{(w.Locked ? ", 0 hearts so her options are locked" : "")} ({(Time.realtimeSinceStartup - t0) * 1000:0} ms).");
    }

    // When the scene switches to 3P the second girl becomes an actor: her copy goes away but her spot stays
    // reserved, and she comes back to it when the scene returns to two people.
    private static void SyncWithActors(HScene scene)
    {
        var active = ActiveActorIds(scene);
        foreach (var w in _list.ToList())
        {
            if (!w.Away && active.Contains(w.Id))
            {
                Dispose(w);
                w.Away = true;
                Plugin.Debug($"{w.Name} joined the scene; her spot waits for her.");
            }
            else if (w.Away && !active.Contains(w.Id))
            {
                // After a swap the girl may already be watching in another spot: no second copy of her.
                if (_list.Any(o => o != w && !o.Away && o.Id == w.Id)) { _list.Remove(w); continue; }
                Spawn(scene, w);
            }
        }
    }

    private static bool _clearing;

    public static void Clear(string why)
    {
        Moving.Release();
        _pending.Clear();
        _wasPut = false;
        _putEndedAt = -1f;
        if (_list.Count == 0) return;
        _clearing = true;
        foreach (var w in _list) Dispose(w);
        _clearing = false;
        Plugin.Debug($"Watchers left ({why}): {string.Join(", ", _list.Select(w => w.Name))}");
        _list.Clear();
    }

    private static void Dispose(Watcher w)
    {
        Moving.Forget(w);
        if (Selected == w) Selected = null;
        if (w.Lover != null)
        {
            // Her partner is left on her own and goes back to watching.
            var other = w.Lover;
            w.Lover = null;
            other.Lover = null;
            other.LoverLeads = false;
            if (!_clearing && other.Human != null) BackToWatching(other);
        }
        w.Gathered = false;
        w.Hips = null;
        w.Tok = null;
        w.TokFade = null;
        try { if (w.Human?.GameObject != null) w.Human.Dispose(); }
        catch (Exception e) { Plugin.Log.LogWarning($"{w.Name}: unloading failed: {e.Message}"); }
        w.Human = null;
    }

    // ---------- reacting to the scene ----------

    // How fast excitement grows right now, and a reaction for everyone when he finishes.
    private static float ReadScene(HScene scene, float now)
    {
        var intensity = 1f;
        try
        {
            var c = scene.Controller;
            if (c == null) return intensity;
            if (c.IsSLoop) intensity = 2.5f;
            var finished = c.IsFinished;
            var put = c.IsPutNow;
            if (put != _wasPut) Plugin.Debug($"Bukkake {(put ? "started" : "ended")}.");
            if (put && !_wasPut) { _viewCheckUntil = now + 6f; Gather(scene, now); }
            KeepOutOfView(scene, now);
            if (!put && _wasPut) _putEndedAt = now;
            if ((finished && !_wasFinished) || (put && !_wasPut))
                foreach (var w in Present)
                    if (w.ReactAt < 0 && !w.Gathered) w.ReactAt = now + Random.Range(0.1f, 0.8f);
            _wasFinished = finished;
            _wasPut = put;
            if (!put) Disperse(scene, now);
            if (Plugin.DebugLog.Value)
            {
                var state = c.State?.GetIl2CppType()?.Name;
                if (state != _lastState) Plugin.Debug($"H state: {state} (strong loop {c.IsSLoop}, finished {finished}, put {put})");
                _lastState = state;
            }
        }
        catch { }
        return intensity;
    }

    private static void Tick(HScene scene, Watcher w, float now, float intensity)
    {
        Faces.Tick(w, now);
        if (w.Tired) { TiredTick(w, now); return; }
        w.Excitement = Mathf.Min(1f, w.Excitement + Time.deltaTime * 0.006f * intensity * w.Temper * Plugin.ExcitementSpeed.Value);

        if (w.Gathered)
        {
            GatherTick(scene, w, now);
            return;
        }
        if (w.Lover != null)
        {
            if (w.LoverLeads) LoveTick(w, now);
            else w.ReactAt = -1f;
            return;
        }
        if (w.Tok != null)
        {
            TokTick(w, now);
            return;
        }
        if (w.ReactAt >= 0 && now >= w.ReactAt)
        {
            w.ReactAt = -1f;
            w.Excitement = Mathf.Min(1f, w.Excitement + 0.15f);
            SetPose(w, $"Base Layer.water_Reaction.00_tachi.Pose_D_{Personality(w):00}_Reaction");
            Faces.Set(w, Mood.Hit); // surprised
            Voices.Gasp(w, true);
            w.ReactionUntil = now + 3.5f;
            return;
        }
        if (now >= w.NextGaspAt)
        {
            if (w.Excitement > 0.45f) Voices.Gasp(w, false);
            w.NextGaspAt = now + Random.Range(12f, 35f) / (0.5f + w.Excitement);
        }
        if (now < w.ReactionUntil) return;
        if (w.ReactionUntil > 0f) { w.ReactionUntil = 0f; w.NextPoseAt = 0f; }
        if (now < w.NextPoseAt) return;

        w.NextPoseAt = now + Random.Range(10f, 22f);
        var e = w.Excitement;
        var idle = $"Base Layer.Idle.chara.Pose_D_{Personality(w):00}_Loop";
        const string shy = "Base Layer.Idle.chara.Pose_D_08_Loop";
        const string fidget = "Base Layer.Idle.chara.Pose_D_04_Loop";
        const string lips = "Base Layer.Idle.chara.Pose_D_10_Loop";
        string pose;
        if (e < 0.35f)
        {
            pose = Random.value < 0.5f ? idle : shy;
            Faces.Set(w, Mood.Shy);
        }
        else if (e < 0.7f)
        {
            var r = Random.value;
            pose = r < 0.35f ? shy : r < 0.7f ? fidget : lips;
            Faces.Set(w, Mood.Curious);
        }
        else
        {
            if (TryPairUp(w)) return;
            if (CanTouch(w) && Random.value < 0.45f)
            {
                StartTouching(w, null);
                return;
            }
            pose = Random.value < 0.4f ? fidget : lips;
            Faces.Set(w, Mood.Aroused);
        }
        SetPose(w, pose);
        Plugin.Debug($"{w.Name}: excitement {e:0.00}, pose {pose}");
    }

    private static void SetPose(Watcher w, string state)
    {
        if (state == w.Pose) return;
        var anim = w.Human.Body?.animBody;
        if (anim == null) return;
        var hash = Animator.StringToHash(state);
        if (!anim.HasState(0, hash)) return;
        anim.CrossFadeInFixedTime(hash, 0.4f, 0, 0f);
        w.Pose = state;
    }

    // ---------- masturbation ----------

    private static readonly string[] TokStages = { "WLoop", "MLoop", "SLoop", "OLoop", "Orgasm", "Orgasm_A" };
    private const int TokOrgasm = 4;

    public static bool CanTouch(Watcher w) =>
        Plugin.Masturbate.Value && TokLibrary.Available && !w.Locked && !w.Tired && w.Human != null && w.Lover == null && !w.Gathered;

    public static void StartTouching(Watcher w, string set)
    {
        if (!CanTouch(w) || w.Tok != null) return;
        set ??= Random.value < 0.6f ? "standing" : "crouching";
        if (w.Wet) set = "standing";
        if (TokLibrary.Get(set, TokStages[0]) == null) return;
        if (w.TokFade != null) { w.TokFade.End(); w.TokFade = null; }
        w.Tok = new TokPlayer(w.Human, w.Name, WorldScale(w.Human));
        w.TokClothes = new();
        w.TokSet = set;
        TokStage(w, 0);
        Plugin.Debug($"{w.Name} started touching herself ({set}).");
    }

    public static void StopTouching(Watcher w)
    {
        if (w.Tok == null) return;
        w.Tok.Release();
        w.TokFade = w.Tok;
        w.Tok = null;
        RestoreClothes(w);
        w.Pose = null;
        w.NextPoseAt = 0f;
        w.Excitement = Mathf.Min(w.Excitement, 0.35f);
        Faces.Set(w, Mood.Shy);
    }

    private static void TokStage(Watcher w, int stage)
    {
        var clip = TokLibrary.Get(w.TokSet, TokStages[stage]);
        if (clip == null) { StopTouching(w); return; }
        w.TokStage = stage;
        w.Tok.Play(clip, TokStages[stage], stage != TokOrgasm, stage == 0 ? 0.6f : 0.3f);
        var now = Time.realtimeSinceStartup;
        w.TokStageUntil = now + stage switch
        {
            0 => Random.Range(12f, 20f),
            1 => Random.Range(10f, 16f),
            2 => Random.Range(8f, 14f),
            3 => Random.Range(5f, 8f),
            4 => clip.Length,
            _ => Random.Range(4f, 7f)
        };
        if (UndressFor(w, stage)) w.Tok.Remeasure();
        switch (stage)
        {
            case < 2: Faces.Set(w, Mood.Aroused); break;
            case < 4: Faces.Set(w, Mood.Hot); break;
            case 4: Faces.Set(w, Mood.Climax); Voices.Gasp(w, true); break;
            default: Faces.Set(w, Mood.Afterglow); break;
        }
        w.NextGaspAt = now + Random.Range(1f, 3f);
    }

    // She moves her clothes aside as she goes: panties, skirt and pantyhose first, then her top and bra.
    private static readonly HumanCloth.Define.ClothesKind[] LowerClothes =
        { HumanCloth.Define.ClothesKind.Shorts, HumanCloth.Define.ClothesKind.Bot, HumanCloth.Define.ClothesKind.Panst };
    private static readonly HumanCloth.Define.ClothesKind[] UpperClothes =
        { HumanCloth.Define.ClothesKind.Top, HumanCloth.Define.ClothesKind.Bra };

    private static bool UndressFor(Watcher w, int stage)
    {
        var changed = HalfUndress(w, LowerClothes);
        if (stage >= 1) changed |= HalfUndress(w, UpperClothes);
        return changed;
    }

    private static bool HalfUndress(Watcher w, HumanCloth.Define.ClothesKind[] kinds)
    {
        var cloth = w.Human?.Cloth;
        var changed = false;
        if (cloth == null || w.TokClothes == null) return false;
        foreach (var kind in kinds)
            try
            {
                if (w.TokClothes.ContainsKey(kind) || !cloth.IsExist(kind)) continue;
                var state = cloth.GetClothesStateType(kind);
                if (state != HumanCloth.Define.ClothesState.Clothing) continue;
                if (!cloth.IsClothesStateType(kind, HumanCloth.Define.ClothesState.HalfUndress)) continue;
                cloth.SetClothesState(kind, HumanCloth.Define.ClothesState.HalfUndress, true);
                w.TokClothes[kind] = (state, HumanCloth.Define.ClothesState.HalfUndress);
                changed = true;
            }
            catch { }
        return changed;
    }

    // When she stops she puts back what she moved, unless someone changed it in the meantime.
    private static void RestoreClothes(Watcher w)
    {
        var cloth = w.Human?.Cloth;
        if (cloth == null || w.TokClothes == null) return;
        foreach (var (kind, (was, set)) in w.TokClothes)
            try { if (cloth.GetClothesStateType(kind) == set) cloth.SetClothesState(kind, was, true); }
            catch { }
        w.TokClothes = null;
    }

    private static void TokTick(Watcher w, float now)
    {
        // He finishes: if she is close, she finishes with him; otherwise she just gasps.
        if (w.ReactAt >= 0 && now >= w.ReactAt)
        {
            w.ReactAt = -1f;
            if (w.TokStage >= 2 && w.TokStage < TokOrgasm) { TokStage(w, TokOrgasm); return; }
            Voices.Gasp(w, true);
        }
        if (now >= w.NextGaspAt)
        {
            Voices.Gasp(w, w.TokStage >= 2);
            w.NextGaspAt = now + Random.Range(3f, 7f) / (1f + 0.4f * w.TokStage);
        }
        var done = w.TokStage == TokOrgasm ? w.Tok.Finished : now >= w.TokStageUntil;
        if (!done) return;
        if (w.TokStage + 1 < TokStages.Length) TokStage(w, w.TokStage + 1);
        else StopTouching(w);
    }

    // Called every frame from LateUpdate, after her animator has posed her.
    public static void Animate(float dt)
    {
        foreach (var w in Present)
        {
            if (w.Lover != null && !w.LoverLeads && w.Lover.Pinned && w.Lover.Human != null)
                w.Human.Transform.SetPositionAndRotation(w.Lover.Human.Transform.position, w.Lover.Human.Transform.rotation);
            if (w.Gathered) FixGatherSpot(w);
            if (w.TokFade != null)
            {
                try { w.TokFade.Apply(dt); } catch { w.TokFade = null; }
                if (w.TokFade?.Released == true) { w.TokFade.End(); w.TokFade = null; }
            }
            if (w.Tok != null)
                try { w.Tok.Apply(dt); }
                catch (Exception e) { Plugin.Log.LogWarning($"{w.Name}: animation failed, stopping: {e.Message}"); StopTouching(w); }
        }
    }

    // ---------- placement ----------

    // A ring around the couple on the side the camera looked from when the group arrived, facing them. Spots
    // that touch anyone in the scene (where their bodies actually are in this pose, the man included) or
    // another watcher are skipped: first a little to the side, then further out.
    private static readonly float[] AngleTries = { 0f, 20f, -20f, 40f, -40f, 60f, -60f, 90f, -90f };
    private static readonly float[] DistanceTries = { 1f, 1.3f, 1.65f, 2.1f };

    private static void Place(HScene scene, Watcher w)
    {
        if (w.Gathered) return;
        w.Pinned = false;
        if (w.Lover != null)
        {
            if (!PlaceCouple(scene, w.LoverLeads ? w : w.Lover)) EndLove(w);
            return;
        }
        var center = SceneCenter(scene);
        if (_ringDir == Vector3.zero)
        {
            var cam = scene.MainCamera != null ? scene.MainCamera.transform.position : center + Vector3.back;
            _ringDir = cam - center; _ringDir.y = 0;
            if (_ringDir.sqrMagnitude < 0.01f) _ringDir = Vector3.back;
            _ringDir.Normalize();
        }
        var k = WorldScale(w.Human);
        center.y = SceneFloor(scene, center, k);
        if (!_anchored) { _anchor = center; _anchored = true; LogGround(center, k, "the scene"); }
        // In H on a bed (or anything raised) they stand on the room's floor around it, not on the bed: a tired girl
        // always (and with room around her, as couples), the others unless the floor has no spot in sight.
        var raised = center.y;
        var floor = RoomFloor(center, k);
        var onFloor = floor < center.y;
        if (onFloor) center.y = floor;
        var bodies = ActorBones(scene);
        var baseAngle = SlotAngles[w.Slot % SlotAngles.Length];
        Vector3? first = null, chosen = null, cramped = null;
        bool firstWet = false, chosenWet = false;
        for (var pass = 0; pass < 2 && chosen == null; pass++)
        {
            if (pass == 1)
            {
                if (!onFloor || w.Tired) break;
                Plugin.Debug($"{w.Name}: no free spot on the floor around the bed, trying the bed's level.");
                center.y = raised; first = null; cramped = null;
            }
            foreach (var dist in DistanceTries)
            {
                foreach (var off in AngleTries)
                {
                    var dir = Quaternion.Euler(0, baseAngle + off, 0) * _ringDir;
                    if (!Usable(center, center + dir * Plugin.Distance.Value * dist * k, k, out var pos, out var wet)) continue;
                    if (w.Tired && wet) continue; // she lies down
                    if (first == null) { first = pos; firstWet = wet; }
                    if (!IsClear(w, pos, bodies, k)) continue;
                    if (w.Tired && !HasRoom(pos, k)) { cramped ??= pos; continue; }
                    if (!w.Tired && pass == 0 && onFloor && !Standable(pos, k)) continue; // in or under the bed
                    chosen = pos; chosenWet = wet;
                    break;
                }
                if (chosen != null) break;
            }
            chosen ??= cramped;
        }
        if (onFloor && Plugin.DebugLog.Value) Plugin.Debug($"{w.Name}: raised scene, placed {(chosen != null && chosen.Value.y < raised - 0.2f * k ? "on the floor" : "at the bed's level")}.");
        var spot = chosen ?? first ?? center + Quaternion.Euler(0, baseAngle, 0) * _ringDir * Plugin.Distance.Value * k;
        w.Wet = chosen != null ? chosenWet : firstWet;
        if (chosen == null) Plugin.Debug($"{w.Name}: no free spot around the couple, using {(first != null ? "the nearest one" : "a spot on their level")}.");
        var face = center - spot; face.y = 0;
        // Lying worn out, she would look away from the scene; she lies the other way round.
        if (w.Tired) face = -face;
        w.Human.Transform.SetPositionAndRotation(spot, Quaternion.LookRotation(face.sqrMagnitude > 0.001f ? face : Vector3.forward));
    }

    // Room to stand: nothing solid from knee to head height (the bed or a table she would stand in).
    private static bool Standable(Vector3 pos, float k)
    {
        try
        {
            var cols = Physics.OverlapCapsule(pos + Vector3.up * 0.45f * k, pos + Vector3.up * 1.4f * k, 0.2f * k, ~0, QueryTriggerInteraction.Ignore);
            foreach (var c in cols)
                if (c != null && !IsCharacter(c.transform) && !IsSound(c)) return false;
        }
        catch { }
        return true;
    }

    private static bool Snap(Vector3 pos, float k, out Vector3 result)
    {
        if (Ground(pos, k, out result)) return true;
        if (NavMesh.SamplePosition(pos, out var hit, 1.5f * k, NavMesh.AllAreas)) { result = hit.position; return true; }
        result = pos;
        return false;
    }

    // A spot she can stand on: solid floor at the same level as the couple (not the ground below a deck), with
    // nothing like a wall or rock between her and them.
    private static bool Usable(Vector3 center, Vector3 pos, float k, out Vector3 spot) => Usable(center, pos, k, out spot, out _);

    private static bool Usable(Vector3 center, Vector3 pos, float k, out Vector3 spot, out bool wet)
    {
        pos.y = center.y;
        wet = false;
        if (!Ground(pos, k, out spot, out var water)) return false;
        if (Mathf.Abs(spot.y - center.y) > 0.3f * k) return false;
        wet = water - spot.y > 0.2f * k;
        return InSight(center, spot, k);
    }

    private static bool Ground(Vector3 pos, float k, out Vector3 result) => Ground(pos, k, out result, out _);

    private static float Floor(Vector3 pos, float k) => Ground(pos, k, out var g) ? g.y : pos.y;

    // Where the people in the scene stand: the floor under the lowest foot of anyone in it (the man's feet are
    // on the floor even when she sits on a chair or he sits on a bench), or under their middle.
    private static float SceneFloor(HScene scene, Vector3 center, float k)
    {
        var lowest = float.PositiveInfinity;
        Vector3 at = center;
        try
        {
            var actors = scene.Actors;
            for (var i = 0; i < actors.Length; i++)
            {
                var a = actors[i];
                if (a?.Human?.GameObject == null || !a.Active) continue;
                foreach (var name in new[] { "cf_j_toes_L", "cf_j_toes_R", "cf_j_foot_L", "cf_j_foot_R" })
                {
                    var t = FindBone(a.Human, name);
                    if (t != null && t.position.y < lowest) { lowest = t.position.y; at = t.position; }
                }
            }
        }
        catch { }
        if (float.IsPositiveInfinity(lowest)) return Floor(center, k);
        at.y = lowest + 0.1f * k;
        return Ground(at, k, out var g) ? g.y : lowest;
    }

    // The highest solid surface under this point, within a step of it (decks and stairs aren't always on the
    // navmesh). Railings, roofs and people don't count.
    private static bool Ground(Vector3 pos, float k, out Vector3 result, out float water)
    {
        result = pos;
        water = float.NegativeInfinity;
        var best = float.NegativeInfinity;
        var fallback = float.NegativeInfinity;
        try
        {
            var hits = Physics.RaycastAll(pos + Vector3.up * 0.4f * k, Vector3.down, 2f * k, ~0, QueryTriggerInteraction.Collide);
            for (var i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || IsCharacter(h.collider.transform)) continue;
                if (IsWater(h.collider)) { water = Mathf.Max(water, h.point.y); continue; }
                if (h.collider.isTrigger) continue;
                if (h.normal.y < 0.6f) continue; // a wall, not a floor
                // Footstep-sound volumes and camera colliders sit above the real floor; use them only if
                // nothing else is there.
                if (NotFloor(h.collider)) { if (h.point.y > fallback) fallback = h.point.y; continue; }
                if (h.point.y > best) best = h.point.y;
            }
        }
        catch { }
        if (float.IsNegativeInfinity(best) && !NavMesh.SamplePosition(pos, out _, 0.3f * k, NavMesh.AllAreas)) best = fallback;
        if (float.IsNegativeInfinity(best))
        {
            if (!NavMesh.SamplePosition(pos, out var nh, 0.3f * k, NavMesh.AllAreas)) return false;
            result = nh.position;
            return true;
        }
        result.y = best;
        return true;
    }

    // Nothing solid between the two spots at chest or head height (a railing blocks only the lower line).
    private static bool InSight(Vector3 from, Vector3 to, float k) =>
        Clear(from + Vector3.up * 0.9f * k, to + Vector3.up * 0.9f * k) || Clear(from + Vector3.up * 1.45f * k, to + Vector3.up * 1.45f * k);

    private static bool Clear(Vector3 a, Vector3 b)
    {
        var d = b - a;
        var len = d.magnitude;
        if (len < 0.01f) return true;
        try
        {
            var hits = Physics.RaycastAll(a, d / len, len, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hits.Length; i++)
                if (hits[i].collider != null && !IsCharacter(hits[i].collider.transform) && !IsSound(hits[i].collider)) return false;
        }
        catch { }
        return true;
    }

    private static readonly Dictionary<int, bool> _notFloor = new();

    // Colliders that aren't the floor itself: footstep-sound volumes (layer "SE", e.g. se_wood) and the camera's
    // collision boxes (layer "CameraCollision"), which on Amanatsu's decks sit up to ~20 cm above the planks.
    private static bool NotFloor(Collider c)
    {
        var id = c.GetInstanceID();
        if (_notFloor.TryGetValue(id, out var known)) return known;
        var result = false;
        try
        {
            var layer = LayerMask.LayerToName(c.gameObject.layer) ?? "";
            result = layer == "SE" || layer.IndexOf("camera", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (c.name ?? "").StartsWith("se_", StringComparison.OrdinalIgnoreCase);
        }
        catch { }
        _notFloor[id] = result;
        return result;
    }

    private static bool IsSound(Collider c)
    {
        try { return LayerMask.LayerToName(c.gameObject.layer) == "SE" || (c.name ?? "").StartsWith("se_", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static readonly Dictionary<int, bool> _water = new();

    // Water surfaces: the "Water" layer, or anything named like water.
    private static bool IsWater(Collider c)
    {
        var id = c.GetInstanceID();
        if (_water.TryGetValue(id, out var known)) return known;
        var result = false;
        try
        {
            var layer = LayerMask.LayerToName(c.gameObject.layer) ?? "";
            var name = (c.name ?? "").ToLowerInvariant();
            result = layer.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     new[] { "water", "mizu", "umi", "sea", "pool", "suimen", "ocean", "river", "lake" }.Any(name.Contains);
        }
        catch { }
        _water[id] = result;
        return result;
    }

    // A look at what is under the scene, so the log shows how a map's floors and water are built.
    private static void LogGround(Vector3 pos, float k, string what)
    {
        try
        {
            var hits = Physics.RaycastAll(pos + Vector3.up * 1f * k, Vector3.down, 4f * k, ~0, QueryTriggerInteraction.Collide);
            var parts = new List<string>();
            for (var i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || IsCharacter(h.collider.transform)) continue;
                parts.Add($"{h.collider.name} (layer {LayerMask.LayerToName(h.collider.gameObject.layer)}, {(h.collider.isTrigger ? "trigger, " : "")}" +
                          $"{(h.point.y - pos.y) / k:+0.00;-0.00} m{(IsWater(h.collider) ? ", water" : "")})");
            }
            Plugin.Debug($"Ground under {what}: {(parts.Count > 0 ? string.Join("; ", parts) : "nothing")}");
        }
        catch { }
    }

    private static bool IsCharacter(Transform t)
    {
        for (var i = 0; t != null && i < 40; i++, t = t.parent)
        {
            var n = t.name;
            if (n.StartsWith("chaF") || n.StartsWith("chaM") || n.StartsWith("p_cf_") || n.StartsWith("p_cm_")) return true;
        }
        return false;
    }

    // Nobody in the scene within reach of her, and no other watcher right next to her.
    private static bool IsClear(Watcher w, Vector3 pos, List<Vector3> bodies, float k)
    {
        var room = (w.Lover != null || w.Tired ? 1.0f : 0.55f) * k;
        foreach (var b in bodies)
        {
            if (b.y - pos.y > 2.2f * k) continue;
            var dx = b.x - pos.x; var dz = b.z - pos.z;
            if (dx * dx + dz * dz < room * room) return false;
        }
        foreach (var o in Present)
        {
            if (o == w || o == w.Lover) continue;
            var d = o.Human.Transform.position - pos; d.y = 0;
            // A couple lying together takes far more room than a girl standing.
            var need = o.Lover != null || w.Lover != null || o.Tired || w.Tired ? 1.3f : 0.7f;
            if (d.magnitude < need * k) return false;
        }
        return true;
    }

    // Where the people in the scene are right now: every main bone of every active actor.
    private static List<Vector3> ActorBones(HScene scene)
    {
        var list = new List<Vector3>();
        var actors = scene.Actors;
        for (var i = 0; i < actors.Length; i++)
        {
            var h = actors[i]?.Human;
            if (h?.GameObject == null || !actors[i].Active) continue;
            if (!_actorBones.TryGetValue(h.Pointer, out var bones) || bones.Length == 0 || bones[0] == null)
            {
                Transform root = null;
                try { root = h.Body?._trfBodyBone; } catch { }
                var found = new List<Transform>();
                foreach (var t in (root ?? h.Transform).GetComponentsInChildren<Transform>(true))
                    if (t.name.Contains("_j_")) found.Add(t);
                _actorBones[h.Pointer] = bones = found.ToArray();
            }
            foreach (var t in bones) if (t != null) list.Add(t.position);
        }
        return list;
    }

    // Keeps the watchers with the couple: if the scene moves to another spot they follow, and if a new pose puts
    // someone where a watcher stands, she steps aside. A watcher the camera ends up inside is hidden meanwhile.
    private static void Arrange(HScene scene)
    {
        var present = Present.ToList();
        if (present.Count == 0) return;
        var k = WorldScale(present[0].Human);
        var center = SceneCenter(scene);
        if (_anchored && (center - _anchor).magnitude > 1.2f * k)
        {
            Plugin.Debug("The scene moved; the watchers follow.");
            _anchor = center;
            _ringDir = Vector3.zero;
            foreach (var w in present)
            {
                w.Pinned = false;
                if (!w.Gathered) w.Human.Transform.position = center + Vector3.down * 100f * k; // out of the way while they re-place
            }
            foreach (var w in present)
            {
                if (w.Gathered || (w.Lover != null && !w.LoverLeads)) continue;
                Place(scene, w);
                if (w.Lover == null) LookAtScene(scene, w);
            }
        }
        else
        {
            var bodies = ActorBones(scene);
            foreach (var w in present)
                if (!w.Gathered && !w.Pinned && (w.Lover == null || w.LoverLeads) && !IsClear(w, w.Human.Transform.position, bodies, k))
                {
                    Plugin.Debug($"{w.Name} is in the way; she steps aside.");
                    Place(scene, w);
                }
        }

        var cam = scene.MainCamera != null ? scene.MainCamera.transform.position : Vector3.zero;
        foreach (var w in present)
        {
            var p = w.Human.Transform.position;
            var d = cam - p; var up = d.y; d.y = 0;
            var inside = scene.MainCamera != null && d.magnitude < 0.45f * k && up > -0.2f * k && up < 1.9f * k;
            if (inside == w.Hidden) continue;
            w.Hidden = inside;
            try { w.Human.VisibleAll = !inside; w.Human.UpdateVisible(); } catch { }
        }
    }

    // The game world isn't in meters (a girl is about 15 units tall), so measure it from a standing girl once.
    internal static float WorldScale(Human h)
    {
        if (_scale > 0) return _scale;
        try
        {
            var head = h.Face?.objHeadBone?.transform;
            var height = head != null ? head.position.y - h.Transform.position.y : 0f;
            if (height > 0.5f && height < 50f)
            {
                _scale = height / 1.45f; // head bone of a girl about 1.6 m tall
                return _scale;
            }
        }
        catch { }
        return 10f;
    }

    private static Vector3 SceneCenter(HScene scene)
    {
        var sum = Vector3.zero; var n = 0;
        var actors = scene.Actors;
        for (var i = 0; i < actors.Length; i++)
        {
            var a = actors[i];
            if (a?.Human?.GameObject == null || !a.Active) continue;
            sum += a.Human.Transform.position; n++;
        }
        return n > 0 ? sum / n : Vector3.zero;
    }

    private static void LookAtScene(HScene scene, Watcher w)
    {
        Transform target = null;
        var actors = scene.Actors;
        for (var i = 0; i < actors.Length && target == null; i++)
        {
            var a = actors[i];
            if (a?.Human == null || !a.Active || a.IsManOrFutanari) continue;
            target = a.Human.Face?.objHeadBone?.transform ?? a.Human.Transform;
        }
        if (target != null) Gaze(w, target);
    }

    private static void Gaze(Watcher w, Transform target)
    {
        try
        {
            var face = w.Human.Face;
            var eyes = face.eyeLookCtrl;
            if (eyes != null)
            {
                eyes.Target = target;
                var states = eyes.EyeLookScript?.EyeTypeStates;
                for (var i = 0; states != null && i < states.Length; i++)
                    if (states[i] != null && states[i].LookType == EYE_LOOK_TYPE.TARGET) { eyes.PtnNo = i; break; }
            }
            var neck = face.neckLookCtrl;
            if (neck != null)
            {
                neck.Target = target;
                var states = neck.NeckLookScript?.neckTypeStates;
                for (var i = 0; states != null && i < states.Length; i++)
                    if (states[i] != null && states[i].lookType == NECK_LOOK_TYPE_VER2.TARGET) { neck.PtnNo = i; break; }
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"{w.Name}: gaze setup failed: {e.Message}"); }
    }

    // ---------- click menu ----------

    // Right-click on a watcher opens her menu: the girl whose on-screen body contains the mouse, nearest first.
    public static void PickAt(Vector2 mouse)
    {
        var cam = _scene?.MainCamera;
        if (cam == null) return;
        Watcher best = null;
        var bestDepth = float.MaxValue;
        foreach (var w in Present)
        {
            try
            {
                var k = WorldScale(w.Human);
                var feet = cam.WorldToScreenPoint(w.Human.Transform.position);
                var head = cam.WorldToScreenPoint(w.Human.Transform.position + Vector3.up * 1.7f * k);
                if (feet.z <= 0 || head.z <= 0) continue;
                var height = Mathf.Abs(head.y - feet.y);
                var half = Mathf.Max(height * 0.2f, 20f);
                var cx = (feet.x + head.x) * 0.5f;
                if (mouse.x < cx - half || mouse.x > cx + half) continue;
                if (mouse.y < Mathf.Min(feet.y, head.y) || mouse.y > Mathf.Max(feet.y, head.y)) continue;
                if (feet.z < bestDepth) { bestDepth = feet.z; best = w; }
            }
            catch { }
        }
        if (best != null) Selected = Selected == best ? null : best;
    }

    public static void Dismiss(Watcher w)
    {
        Dispose(w);
        _list.Remove(w);
        Plugin.Debug($"{w.Name} was sent back.");
    }

    public static void Redress(Watcher w, HumanCloth.Define.ClothesState state)
    {
        w.Tok?.Remeasure(0.5f);
        var cloth = w.Human?.Cloth;
        if (cloth == null) return;
        foreach (HumanCloth.Define.ClothesKind kind in Enum.GetValues(typeof(HumanCloth.Define.ClothesKind)))
            try
            {
                if (cloth.IsClothesStateKind(kind) && cloth.IsExist(kind) && cloth.IsClothesStateType(kind, state))
                    cloth.SetClothesState(kind, state, true);
            }
            catch { }
    }

    public static void CyclePart(Watcher w, HumanCloth.Define.ClothesKind kind)
    {
        w.Tok?.Remeasure(0.5f);
        var cloth = w.Human?.Cloth;
        if (cloth == null) return;
        try
        {
            var state = cloth.GetClothesStateType(kind);
            for (var i = 1; i <= 3; i++)
            {
                var next = (HumanCloth.Define.ClothesState)(((int)state + i) % 3);
                if (!cloth.IsClothesStateType(kind, next)) continue;
                cloth.SetClothesState(kind, next, true);
                return;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"{w.Name}: clothes change failed: {e.Message}"); }
    }

    public static int Outfit(Watcher w)
    {
        try { return (int)w.Human.FileStatus.coordinateType; } catch { return -1; }
    }

    public static void SetOutfit(Watcher w, int type)
    {
        w.Tok?.Remeasure(0.5f);
        try
        {
            if (Outfit(w) == type) return;
            w.Human.Coorde.ChangeCoordinateTypeAndReload((HumanCoordinate.Define.CoordinateType)type, true);
            w.Pose = null;          // the reload can reset her animator; pick her pose again
            w.NextPoseAt = 0f;
            if (_scene != null) LookAtScene(_scene, w);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"{w.Name}: outfit change failed: {e.Message}"); }
    }

    // The copy starts in her card's default outfit; dress her like the girl walking the map.
    private static void MatchOutfit(Watcher w)
    {
        try
        {
            // What she wore when she left the scene; else the after-bath clothes in night H; else what she wears
            // on the map.
            var type = RememberedOutfit(w.Id);
            if (type < 0 && SceneWearsBath()) type = (int)HumanCoordinate.Define.CoordinateType.AfterBath;
            if (type < 0)
            {
                var original = w.Data.NPCInstance?.Human;
                if (original == null) return;
                type = (int)original.FileStatus.coordinateType;
            }
            if (Outfit(w) != type) w.Human.Coorde.ChangeCoordinateTypeAndReload((HumanCoordinate.Define.CoordinateType)type, true);
        }
        catch { }
    }

    // ---------- helpers ----------

    private static HashSet<int> ActiveActorIds(HScene scene)
    {
        var ids = new HashSet<int>();
        var actors = scene.Actors;
        for (var i = 0; i < actors.Length; i++)
            if (actors[i] != null && actors[i].Active && actors[i].NPCData != null) ids.Add(ShownId(i, actors[i]));
        return ids;
    }

    // In a two-person scene the second girl is already loaded as an inactive actor, so only an active girl's partner counts.
    private static int PartnerOfActiveGirl(HScene scene)
    {
        var actors = scene.Actors;
        for (var i = 0; i < actors.Length; i++)
        {
            var a = actors[i];
            if (a != null && a.Active && !a.IsManOrFutanari && a.NPCData?.Action != null && a.NPCData.Action.LinkPartnerUniqueID != 0)
                return a.NPCData.Action.LinkPartnerUniqueID;
        }
        return 0;
    }

    private static readonly Dictionary<string, RuntimeAnimatorController> _controllers = new();

    // Cached, but loaded again if the game has unloaded its bundle in the meantime.
    private static RuntimeAnimatorController LoadController(string bundle, string asset)
    {
        try
        {
            if (_controllers.TryGetValue(asset, out var cached) && cached != null) return cached;
            var obj = AssetBundleManager.LoadAssetSimple(Il2CppType.Of<RuntimeAnimatorController>(), bundle, asset, null);
            var rac = obj?.TryCast<RuntimeAnimatorController>();
            if (rac != null) _controllers[asset] = rac;
            return rac;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not load the {asset} animator: {e.Message}");
            return null;
        }
    }

    private static void Expression(Human h, int brow, int eyes, int mouth, float blush)
    {
        try
        {
            var face = h.Face;
            face.ChangeEyebrowPtn(brow, true);
            face.ChangeEyesPtn(eyes, true);
            face.ChangeMouthPtn(mouth, true);
            face.ChangeHohoAkaRate(new Il2CppSystem.Nullable<float>(Mathf.Clamp01(blush)));
        }
        catch { }
    }

    private static int Personality(Watcher w)
    {
        try { var p = w.Human.Data.Parameter.personality; return p is >= 0 and <= 11 ? p : 0; } catch { return 0; }
    }

    private static string SafeName(Func<string> f)
    {
        try { return f(); } catch { return null; }
    }
}
