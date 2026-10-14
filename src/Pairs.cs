using AL.H;
using Character;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace AL_HWatchers;

// Two watchers who are a linked couple make love with each other once both are worked up, using the game's own
// Les animations (h/animator/female/000_01/rez.unity3d: rez_f1_XX and rez_f2_XX share one origin). The H scene
// can't run a second Les scene, so the states are played directly: weak loops, strong loops, her climax and the
// afterglow, each in the size variant (S/M/L) that matches the girl's height.
internal static partial class Watchers
{
    private const string LesBundle = "h/animator/female/000_01/rez.unity3d";
    private static readonly string[] LoveStates = { "WLoop1", "WLoop2", "SLoop1", "SLoop2", "OrgasmF", "OrgasmF_ST", "Idle_OF_A" };
    private static readonly float[] LoveSpeeds = { 1f, 1.1f, 1.2f, 1.35f, 1f, 1f, 1f };
    private const int LoveOrgasm = 4;
    public static readonly string[] LoveNames = { "scissoring", "69" };

    public static bool IsCouple(Watcher a, Watcher b) => a != b && (a.Partner == b.Id || b.Partner == a.Id);

    // Her linked partner, if she is watching too.
    public static Watcher CoupleOf(Watcher w) => Present.FirstOrDefault(o => IsCouple(w, o));

    public static bool CanLove(Watcher a, Watcher b) =>
        Plugin.GirlPairs.Value && a != null && b != null && a != b && a.Human != null && b.Human != null && !a.Tired && !b.Tired &&
        !a.Locked && !b.Locked && a.Lover == null && b.Lover == null && !a.Gathered && !b.Gathered;

    // Girls she could make love with right now, her linked partner first.
    public static List<Watcher> LoveChoices(Watcher w) =>
        w.Tired ? new List<Watcher>() : Present.Where(o => o != w && !o.Locked && !o.Gathered && !o.Tired).OrderBy(o => IsCouple(w, o) ? 0 : 1).ToList();

    // From the menu: she leaves whoever she is with (and so does the other girl) and pairs with the one picked.
    public static void SwitchLove(Watcher w, Watcher other, string set)
    {
        if (w.Lover == other) { if (w.LoveSet == set) return; EndLove(w); }
        if (w.Lover != null) EndLove(w);
        if (other.Lover != null) EndLove(other);
        StartLove(w, other, set);
    }

    // Called when a worked-up watcher picks her next pose: if her partner is worked up too, they may pair up.
    private static bool TryPairUp(Watcher w)
    {
        var other = CoupleOf(w);
        if (other == null || !CanLove(w, other) || other.Excitement < 0.55f || Random.value > 0.5f) return false;
        StartLove(w, other, null);
        return w.Lover != null;
    }

    public static void StartLove(Watcher a, Watcher b, string set)
    {
        if (_scene == null || !CanLove(a, b)) return;
        set ??= Random.value < 0.5f ? "00" : "01";
        var ca = LoadController(LesBundle, $"rez_f1_{set}");
        var cb = LoadController(LesBundle, $"rez_f2_{set}");
        if (ca == null || cb == null) return;
        foreach (var w in new[] { a, b })
        {
            if (w.Tok != null) StopTouching(w);
            if (w.TokFade != null) { w.TokFade.End(); w.TokFade = null; }
            w.ReactAt = -1f;
            w.ReactionUntil = 0f;
            w.TokClothes = new();
            w.Pose = null;
        }
        a.Lover = b; b.Lover = a;
        a.LoverLeads = true; b.LoverLeads = false;
        a.LoveSet = b.LoveSet = set;
        a.Human.Body.animBody.runtimeAnimatorController = ca;
        b.Human.Body.animBody.runtimeAnimatorController = cb;
        if (!PlaceCouple(_scene, a))
        {
            a.Lover = b.Lover = null;
            a.LoverLeads = false;
            BackToWatching(a);
            BackToWatching(b);
            return;
        }
        // Both animations are cut for one body size, so both play the leading girl's size variant.
        Gaze(a, Head(b));
        Gaze(b, Head(a));
        LoveStage(a, 0);
        Plugin.Debug($"{a.Name} and {b.Name} make love ({LoveNames[set == "01" ? 1 : 0]}).");
    }

    private static void LoveStage(Watcher a, int stage)
    {
        var b = a.Lover;
        a.LoveStage = stage;
        var lewd = stage >= LoveOrgasm && IsLewd(_scene);
        foreach (var w in new[] { a, b })
        {
            var anim = w.Human.Body?.animBody;
            if (anim == null) continue;
            var hash = Animator.StringToHash($"Base Layer.{(lewd ? "S_" : "")}{LoveStates[stage]}_{Size(a)}");
            if (!anim.HasState(0, hash)) hash = Animator.StringToHash($"Base Layer.{LoveStates[stage]}_M");
            anim.speed = LoveSpeeds[stage];
            // Both start the new state at the same moment, so the two stay in step.
            if (stage == 0) anim.Play(hash, 0, 0f);
            else anim.CrossFadeInFixedTime(hash, 0.35f, 0, 0f);
            UndressFor(w, stage >= 2 ? 1 : 0);
            switch (stage)
            {
                case < 2: Faces.Set(w, Mood.Aroused); break;
                case < LoveOrgasm: Faces.Set(w, Mood.Hot); break;
                case LoveOrgasm: Faces.Set(w, Mood.Climax); break;
                default: Faces.Set(w, Mood.Afterglow); break;
            }
        }
        var now = Time.realtimeSinceStartup;
        a.LoveStageUntil = now + stage switch
        {
            0 => Random.Range(10f, 16f),
            1 => Random.Range(8f, 12f),
            2 => Random.Range(8f, 12f),
            3 => Random.Range(6f, 10f),
            4 => Random.Range(3f, 4.5f),
            5 => 1.3f,
            _ => Random.Range(5f, 8f)
        };
        if (stage == LoveOrgasm) { Voices.Gasp(a, true); Voices.Gasp(b, true); }
        a.NextGaspAt = now + Random.Range(1f, 3f);
    }

    private static void LoveTick(Watcher a, float now)
    {
        var b = a.Lover;
        if (b?.Human == null || a.Human.Body?.animBody?.runtimeAnimatorController == null ||
            b.Human.Body?.animBody?.runtimeAnimatorController == null)
        {
            EndLove(a);
            return;
        }
        b.ReactAt = -1f;
        // He finishes: if they are close, they finish with him.
        if (a.ReactAt >= 0 && now >= a.ReactAt)
        {
            a.ReactAt = -1f;
            if (a.LoveStage is 2 or 3) { LoveStage(a, LoveOrgasm); return; }
            Voices.Gasp(Random.value < 0.5f ? a : b, true);
        }
        if (now >= a.NextGaspAt)
        {
            Voices.Gasp(Random.value < 0.5f ? a : b, a.LoveStage >= 2);
            a.NextGaspAt = now + Random.Range(2.5f, 6f) / (1f + 0.4f * Mathf.Min(a.LoveStage, 3));
        }
        if (now < a.LoveStageUntil) return;
        if (a.LoveStage + 1 < LoveStates.Length) LoveStage(a, a.LoveStage + 1);
        else EndLove(a);
    }

    public static void EndLove(Watcher w)
    {
        var other = w.Lover;
        if (other == null) return;
        w.Lover = null; other.Lover = null;
        w.LoverLeads = false; other.LoverLeads = false;
        BackToWatching(w);
        BackToWatching(other);
        Plugin.Debug($"{w.Name} and {other.Name} go back to watching.");
    }

    private static void BackToWatching(Watcher w)
    {
        if (w.Human == null) return;
        var anim = w.Human.Body?.animBody;
        if (anim != null)
        {
            if (_mapAnimator == null) _mapAnimator = LoadController(MapBundle, "f_00");
            if (_mapAnimator != null) anim.runtimeAnimatorController = _mapAnimator;
            anim.speed = 1f;
        }
        RestoreClothes(w);
        w.Pose = null;
        w.NextPoseAt = 0f;
        w.ReactAt = -1f;
        w.Excitement = Mathf.Min(w.Excitement, 0.3f);
        Faces.Set(w, Mood.Shy);
        if (_scene == null) return;
        Place(_scene, w);
        LookAtScene(_scene, w);
    }

    // Both girls stand on one spot (their animations hold them in place around it), between their two places
    // in the ring, where there is room for two people lying down.
    private static bool PlaceCouple(HScene scene, Watcher a)
    {
        var b = a.Lover;
        a.Pinned = b.Pinned = false;
        var center = SceneCenter(scene);
        if (_ringDir == Vector3.zero)
        {
            var cam = scene.MainCamera != null ? scene.MainCamera.transform.position : center + Vector3.back;
            _ringDir = cam - center; _ringDir.y = 0;
            if (_ringDir.sqrMagnitude < 0.01f) _ringDir = Vector3.back;
            _ringDir.Normalize();
        }
        var k = WorldScale(a.Human);
        center.y = SceneFloor(scene, center, k);
        if (!_anchored) { _anchor = center; _anchored = true; }
        // H on a bed (or anything raised): they lie on the room's floor, not on the sofa or table next to it.
        var floor = RoomFloor(center, k);
        if (floor < center.y) center.y = floor;
        var bodies = ActorBones(scene);
        var baseAngle = (SlotAngles[a.Slot % SlotAngles.Length] + SlotAngles[b.Slot % SlotAngles.Length]) * 0.5f;
        Vector3? first = null, chosen = null, cramped = null;
        foreach (var dist in new[] { 1.2f, 1.5f, 1.85f, 2.3f })
        {
            foreach (var off in AngleTries)
            {
                var dir = Quaternion.Euler(0, baseAngle + off, 0) * _ringDir;
                // They lie down or kneel, so only on dry floor.
                if (!Usable(center, center + dir * Plugin.Distance.Value * dist * k, k, out var pos, out var wet) || wet) continue;
                first ??= pos;
                if (!IsClear(a, pos, bodies, k) || BlocksView(pos, k)) continue;
                // Room to lie down: no furniture where their bodies go.
                if (!HasRoom(pos, k)) { cramped ??= pos; continue; }
                chosen = pos;
                break;
            }
            if (chosen != null) break;
        }
        if (chosen == null && first == null)
        {
            Plugin.Debug($"No dry spot on the couple's level for {a.Name} and {b.Name} to lie down.");
            return false;
        }
        var spot = chosen ?? cramped ?? first.Value;
        if (chosen == null) Plugin.Debug($"{a.Name} and {b.Name}: no roomy spot, using {(cramped != null ? "a tight one" : "the nearest one")}.");
        LogGround(spot, k, $"{a.Name} and {b.Name}");
        var face = center - spot; face.y = 0;
        var rot = Quaternion.LookRotation(face.sqrMagnitude > 0.001f ? face : Vector3.forward);
        a.Human.Transform.SetPositionAndRotation(spot, rot);
        b.Human.Transform.SetPositionAndRotation(spot, rot);
        a.Wet = b.Wet = false;
        return true;
    }

    // The game's H animations come in three sizes, picked by the girl's height (0 to 1 on her card).
    private static string Size(Watcher w)
    {
        try
        {
            var height = w.Human.Data.Custom.Body.shapeValueBody[0];
            return height < 0.25f ? "S" : height > 0.75f ? "L" : "M";
        }
        catch { return "M"; }
    }

    // The floor of the room around a raised scene (a bed): the walkable floor nearest to it, when it is clearly lower.
    private static float RoomFloor(Vector3 center, float k)
    {
        try
        {
            if (NavMesh.SamplePosition(center, out var hit, 1.5f * k, NavMesh.AllAreas) && center.y - hit.position.y > 0.3f * k)
                return hit.position.y;
            // No walkable floor mapped there: the lowest solid floor under the scene, at most 0.9 m down (a bed,
            // not the ground under the house).
            var lowest = center.y;
            var hits = Physics.RaycastAll(center + Vector3.up * 0.2f * k, Vector3.down, 1.1f * k, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || IsCharacter(h.collider.transform) || IsWater(h.collider) || NotFloor(h.collider) || h.normal.y < 0.6f) continue;
                if (h.point.y < lowest) lowest = h.point.y;
            }
            if (center.y - lowest > 0.25f * k) return lowest;
        }
        catch { }
        return center.y;
    }

    // Nothing solid from shin to chest height around the spot (furniture), where two girls lie.
    private static bool HasRoom(Vector3 pos, float k)
    {
        try
        {
            // Walls and big furniture often have only camera colliders, so those count too.
            var cols = Physics.OverlapBox(pos + Vector3.up * 0.45f * k, new Vector3(0.8f, 0.3f, 0.8f) * k, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            foreach (var c in cols)
                if (c != null && !IsCharacter(c.transform) && !IsSound(c)) return false;
            // Not by an edge (of a bed or a step): the floor is level all around where they lie.
            for (var a = 0; a < 360; a += 45)
            {
                var at = pos + Quaternion.Euler(0, a, 0) * Vector3.forward * 0.75f * k;
                if (!Ground(at, k, out var g) || Mathf.Abs(g.y - pos.y) > 0.15f * k) return false;
            }
            // And no wall within reach at head height when sitting up.
            for (var a = 0; a < 360; a += 45)
            {
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                var hits = Physics.RaycastAll(pos + Vector3.up * 0.6f * k, dir, 1.0f * k, ~0, QueryTriggerInteraction.Ignore);
                for (var i = 0; i < hits.Length; i++)
                    if (hits[i].collider != null && !IsCharacter(hits[i].collider.transform) && !IsSound(hits[i].collider)) return false;
            }
        }
        catch { }
        return true;
    }

    // Between the camera and the scene, or right by the camera: the girls would block the view.
    private static bool BlocksView(Vector3 pos, float k)
    {
        try
        {
            var cam = _scene?.MainCamera;
            if (cam == null) return false;
            var c = cam.transform.position;
            var t = SceneCenter(_scene) + Vector3.up * 0.6f * k;
            var p = pos + Vector3.up * 0.4f * k;
            var flat = p - c; flat.y = 0;
            if (flat.magnitude < 1.3f * k) return true;
            var d = t - c;
            var len2 = d.sqrMagnitude;
            if (len2 < 1e-4f) return false;
            var u = Vector3.Dot(p - c, d) / len2;
            if (u <= 0f || u >= 1f) return false;
            return (c + d * u - p).magnitude < 0.9f * k;
        }
        catch { return false; }
    }

    private static float _viewCheckUntil, _nextViewCheck;

    // The camera moves for the bukkake; girls lying in its way move elsewhere.
    private static void KeepOutOfView(HScene scene, float now)
    {
        if (now >= _viewCheckUntil || now < _nextViewCheck) return;
        _nextViewCheck = now + 0.5f;
        foreach (var w in Present.Where(x => (x.Lover != null && x.LoverLeads) || x.Tired).ToList())
        {
            if (w.Human == null || w.Pinned || !BlocksView(w.Human.Transform.position, WorldScale(w.Human))) continue;
            Plugin.Debug($"{w.Name}{(w.Lover != null ? $" and {w.Lover.Name}" : "")} were in the camera's way; moving.");
            if (w.Lover != null) { if (!PlaceCouple(scene, w)) EndLove(w); }
            else { Place(scene, w); LieDown(w); }
        }
    }

    // A girl who got tired in the scene lies on the floor, worn out, and does nothing else.
    internal static void LieDown(Watcher w)
    {
        w.Pose = "tired";
        if (w.Tok != null) StopTouching(w);
        var anim = w.Human?.Body?.animBody;
        if (anim == null) return;
        var c = LoadController(LesBundle, "rez_f1_00");
        if (c == null) return;
        anim.runtimeAnimatorController = c;
        anim.speed = 1f;
        var hash = Animator.StringToHash($"Base Layer.Idle_OF_A_{Size(w)}");
        if (!anim.HasState(0, hash)) hash = Animator.StringToHash("Base Layer.Idle_OF_A_M");
        anim.Play(hash, 0, 0f);
        Faces.Set(w, Mood.Tired);
    }

    private static void TiredTick(Watcher w, float now)
    {
        w.ReactAt = -1f;
        var anim = w.Human.Body?.animBody;
        if (w.Pose != "tired" || anim == null || anim.runtimeAnimatorController == null) LieDown(w);
        if (now >= w.NextGaspAt)
        {
            Voices.Gasp(w, false);
            w.NextGaspAt = now + Random.Range(15f, 30f);
        }
    }

    private static bool IsLewd(HScene scene)
    {
        try { return scene != null && scene.parameter.State == AL.H.Parameter.HState.Lewdness; }
        catch { return false; }
    }

    private static Transform Head(Watcher w) => w.Human.Face?.objHeadBone?.transform ?? w.Human.Transform;
}
