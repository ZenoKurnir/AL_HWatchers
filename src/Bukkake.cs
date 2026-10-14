using AL.H;
using Character;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AL_HWatchers;

// When he finishes over the girl (the H "put"), the watchers kneel around her facing him, in the game's
// kneeling service bukkake pose (put.unity3d: hou_b_00 normal, hou_bs_00/01 when the scene is in its lewd
// state), and get covered too. They go back to their places when the bukkake is over. Couples making love carry on, and girls
// with 0 hearts only watch.
internal static partial class Watchers
{
    private const string PutBundle = "h/animator/female/000_01/put.unity3d";
    private static readonly float[] GatherFixTimes = { 0.15f, 0.5f, 1f };
    private static float _gatherUntil;

    // Bukkake poses where she kneels or sits upright facing forward (measured from the clips: head 0.5-1 m up,
    // above her hips, face turned forward).
    private static readonly string[] NormalKneel = { "hou_b_00", "hou_b_01", "hou_b_02", "sof_b_03", "som_b_13", "3p_b_f1_00" };
    private static readonly string[] LewdKneel =
    {
        "hou_bs_00", "hou_bs_01", "hou_bs_02", "hou_bs_03", "hou_bs_05", "hou_bs_11", "hou_bs_13", "hou_bs_22",
        "sof_bs_00", "sof_bs_04", "sof_bs_07", "som_bs_23"
    };

    private static void Gather(HScene scene, float now)
    {
        if (!Plugin.Bukkake.Value) return;
        var joiners = Present.Where(w => !w.Locked && !w.Tired && w.Lover == null && !w.Gathered).ToList();
        if (joiners.Count == 0) return;

        _girlAnims.Clear();
        _girlPose.Clear();
        _putEndedAt = -1f;
        // Where the girls in the scene are, and where he stands.
        var girls = new List<(Vector3 hips, Vector3 head)>();
        var girlBones = new List<Vector3>();
        Vector3? man = null;
        Transform manHead = null;
        var actors = scene.Actors;
        for (var i = 0; i < actors.Length; i++)
        {
            var a = actors[i];
            if (a?.Human?.GameObject == null || !a.Active) continue;
            var hips = FindBone(a.Human, "cf_j_hips");
            var head = a.Human.Face?.objHeadBone?.transform;
            if (a.IsManOrFutanari)
            {
                man ??= (hips ?? a.Human.Transform).position;
                manHead ??= head;
            }
            else
            {
                girls.Add(((hips ?? a.Human.Transform).position, (head ?? a.Human.Transform).position));
                girlBones.AddRange(BonesOf(a.Human));
                var anim = a.Human.Body?.animBody;
                if (anim != null) _girlAnims.Add(anim);
            }
        }
        if (girls.Count == 0) return;
        var k = WorldScale(joiners[0].Human);

        var middle = Vector3.zero;
        foreach (var g in girls) middle += (g.hips + g.head) * 0.5f;
        middle /= girls.Count;
        var toMan = man.HasValue ? man.Value - middle : Vector3.zero;
        toMan.y = 0;
        if (toMan.sqrMagnitude < 0.01f * k * k)
        {
            toMan = scene.MainCamera != null ? scene.MainCamera.transform.position - middle : Vector3.forward;
            toMan.y = 0;
        }
        toMan.Normalize();
        var side = Vector3.Cross(Vector3.up, toMan);
        // How far the girls reach sideways and away from him, arms and legs included.
        float width = 0f, back = 0f;
        var extent = new List<Vector3>(girlBones);
        if (extent.Count == 0)
            foreach (var g in girls) { extent.Add(g.hips); extent.Add(g.head); }
        foreach (var p in extent)
        {
            width = Mathf.Max(width, Mathf.Abs(Vector3.Dot(p - middle, side)));
            back = Mathf.Max(back, -Vector3.Dot(p - middle, toMan));
        }

        var lewd = IsLewd(scene);
        var look = scene.MainCamera != null ? scene.MainCamera.transform : manHead;
        var lookAt = look != null ? look.position : man ?? middle + toMan * k;
        // Spots in order of preference: two right behind the girl(s), two at her sides, then the back corners,
        // then further out. Spots on another level or behind a wall are skipped.
        var ground = middle; ground.y = SceneFloor(scene, middle, k);
        var wanted = new List<Vector3>
        {
            -toMan * (back + 0.5f * k) + side * 0.45f * k, -toMan * (back + 0.5f * k) - side * 0.45f * k,
            side * (width + 0.6f * k), -side * (width + 0.6f * k),
            -toMan * (back + 0.4f * k) + side * (width + 0.7f * k), -toMan * (back + 0.4f * k) - side * (width + 0.7f * k),
            -toMan * (back + 1.1f * k), side * (width + 1.3f * k), -side * (width + 1.3f * k),
            -toMan * (back + 1.0f * k) + side * (width + 1.2f * k), -toMan * (back + 1.0f * k) - side * (width + 1.2f * k),
            toMan * 0.4f * k + side * (width + 0.7f * k), toMan * 0.4f * k - side * (width + 0.7f * k)
        };
        var spots = new List<Vector3>();
        foreach (var offset in wanted)
        {
            if (spots.Count >= joiners.Count) break;
            // Kneeling, so only on dry floor, and with room for her (about 0.7 m from the next girl).
            if (!Usable(ground, ground + offset, k, out var spot, out var wet) || wet) continue;
            if (spots.Any(o => (o - spot).magnitude < 0.7f * k)) continue;
            spots.Add(spot);
        }
        // A different kneeling pose for each girl.
        var pool = (lewd ? LewdKneel : NormalKneel).OrderBy(_ => Random.value).ToList();
        pool.AddRange((lewd ? NormalKneel : LewdKneel).OrderBy(_ => Random.value));
        for (var i = 0; i < joiners.Count; i++)
        {
            var w = joiners[i];
            if (i >= spots.Count) break; // no room left for her; she keeps watching
            var target = spots[i];
            var anim = w.Human.Body?.animBody;
            // Kneeling upright and looking up at him (the service bukkake pose), the lewd version when the scene is lewd.
            var asset = pool[i % pool.Count];
            var controller = LoadController(PutBundle, asset);
            if (anim == null || controller == null) continue;
            var lewdPose = asset.Contains("_bs_");

            if (w.Tok != null)
            {
                w.ResumeTok = w.TokSet;
                w.ResumeStage = Math.Min(w.TokStage, TokOrgasm - 1);
                StopTouching(w);
            }
            else w.ResumeTok = null;
            if (w.TokFade != null) { w.TokFade.End(); w.TokFade = null; }
            w.Gathered = true;
            w.GatherTarget = target;
            w.GatherAt = now;
            w.GatherFixes = 0;
            w.MarkAt = now + Random.Range(1.5f, 3.5f);
            w.ReactAt = -1f;
            w.ReactionUntil = 0f;
            w.Pose = null;
            w.Hips ??= FindBone(w.Human, "cf_j_hips");

            var face = lookAt - target; face.y = 0;
            w.Human.Transform.SetPositionAndRotation(target, Quaternion.LookRotation(face.sqrMagnitude > 0.001f ? face : toMan));
            anim.runtimeAnimatorController = controller;
            anim.speed = 1f;
            var hash = Animator.StringToHash($"Base Layer.{(lewdPose ? "S_" : "")}Put_{Size(w)}");
            if (!anim.HasState(0, hash)) hash = Animator.StringToHash($"Base Layer.{(lewdPose ? "S_" : "")}Put_M");
            anim.Play(hash, 0, 0f);
            if (look != null) Gaze(w, look);
            Faces.Set(w, Mood.Eager);
        }
        _gatherUntil = now + 6f;
        var gathered = Present.Where(w => w.Gathered).Select(w => w.Name).ToList();
        if (gathered.Count > 0) Plugin.Debug($"Kneeling for the bukkake ({(lewd ? "lewd" : "normal")}): {string.Join(", ", gathered)}.");
    }

    private static void GatherTick(HScene scene, Watcher w, float now)
    {
        if (w.Human.Body?.animBody?.runtimeAnimatorController == null) { EndGather(w); return; }
        if (w.MarkAt > 0f && now >= w.MarkAt)
        {
            w.MarkAt = -1f;
            Cover(w, IsLewd(scene));
            Faces.Set(w, Mood.Hit);
            Voices.Gasp(w, true);
            w.NextGaspAt = now + Random.Range(2f, 4f);
            return;
        }
        if (w.MarkAt < 0f && now >= w.NextGaspAt)
        {
            Faces.Set(w, Random.value < 0.5f ? Mood.Eager : Mood.Afterglow);
            Voices.Gasp(w, false);
            w.NextGaspAt = now + Random.Range(4f, 9f);
        }
    }

    // The bukkake poses move her hips away from her own origin; shift her so her hips land on her spot.
    private static void FixGatherSpot(Watcher w)
    {
        if (w.GatherFixes >= GatherFixTimes.Length || w.Hips == null) return;
        if (Time.realtimeSinceStartup - w.GatherAt < GatherFixTimes[w.GatherFixes]) return;
        w.GatherFixes++;
        var off = w.GatherTarget - w.Hips.position;
        off.y = 0;
        w.Human.Transform.position += off;
    }

    private static readonly List<Animator> _girlAnims = new();
    private static readonly List<(int controller, int state)> _girlPose = new();
    private static float _putEndedAt = -1f;

    // After the bukkake the girl(s) keep their pose until a new motion is picked; the watchers stay kneeling
    // until then too. Her pose is noted a moment after the bukkake ends, and any change to it sends them back.
    private static bool GirlsStillPosed(float now)
    {
        if (_putEndedAt < 0f || _girlAnims.Count == 0) return false;
        if (now - _putEndedAt > 180f) return false;
        if (now - _putEndedAt < 1f) return true;
        try
        {
            if (_girlPose.Count == 0)
            {
                foreach (var a in _girlAnims)
                    _girlPose.Add((a != null && a.runtimeAnimatorController != null ? a.runtimeAnimatorController.GetInstanceID() : 0, a != null ? a.GetCurrentAnimatorStateInfo(0).fullPathHash : 0));
                return true;
            }
            for (var i = 0; i < _girlAnims.Count; i++)
            {
                var a = _girlAnims[i];
                if (a == null || a.IsInTransition(0)) return false;
                if ((a.runtimeAnimatorController != null ? a.runtimeAnimatorController.GetInstanceID() : 0) != _girlPose[i].controller) return false;
                if (a.GetCurrentAnimatorStateInfo(0).fullPathHash != _girlPose[i].state) return false;
            }
            return true;
        }
        catch { return false; }
    }

    private static void Disperse(HScene scene, float now)
    {
        if (now < _gatherUntil) return;
        if (!_list.Any(w => w.Gathered)) return;
        if (GirlsStillPosed(now)) return;
        List<Watcher> back = null;
        foreach (var w in _list)
            if (w.Gathered && w.Human != null) (back ??= new()).Add(w);
        if (back == null) return;
        foreach (var w in back) EndGather(w);
        Plugin.Debug($"Back to watching after the bukkake: {string.Join(", ", back.Select(w => w.Name))}.");
    }

    private static void EndGather(Watcher w)
    {
        w.Gathered = false;
        w.Excitement = Mathf.Min(1f, w.Excitement + 0.1f);
        var anim = w.Human?.Body?.animBody;
        if (anim != null)
        {
            if (_mapAnimator == null) _mapAnimator = LoadController(MapBundle, "f_00");
            if (_mapAnimator != null) anim.runtimeAnimatorController = _mapAnimator;
            anim.speed = 1f;
        }
        w.Pose = null;
        w.NextPoseAt = 0f;
        Faces.Set(w, Mood.Curious);
        if (_scene == null || w.Human == null) return;
        Place(_scene, w);
        LookAtScene(_scene, w);
        // She goes back to what she was doing: touching herself picks up where she left off.
        if (w.ResumeTok != null && CanTouch(w))
        {
            StartTouching(w, w.ResumeTok);
            if (w.Tok != null && w.ResumeStage > 0) TokStage(w, w.ResumeStage);
        }
        w.ResumeTok = null;
    }

    // Semen on her face and chest (and lower body when the scene is lewd). It stays until she leaves.
    private static void Cover(Watcher w, bool lewd)
    {
        try
        {
            var h = w.Human;
            h.SetSiruFlags(Human.Define.SiruParts.SiruKao, lewd || Random.value < 0.5f ? Human.SiruLv.Many : Human.SiruLv.Few);
            h.SetSiruFlags(Human.Define.SiruParts.SiruFrontUp, lewd ? Human.SiruLv.Many : Human.SiruLv.Few);
            if (lewd) h.SetSiruFlags(Human.Define.SiruParts.SiruFrontDown, Human.SiruLv.Few);
            try { h.Body?.UpdateSiru(); } catch { }
            try { h.Face?.UpdateSiru(); } catch { }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"{w.Name}: covering her failed: {e.Message}"); }
    }

    private static List<Vector3> BonesOf(Human h)
    {
        var list = new List<Vector3>();
        Transform root = null;
        try { root = h.Body?._trfBodyBone; } catch { }
        foreach (var t in (root ?? h.Transform).GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("cf_j_")) list.Add(t.position);
        return list;
    }

    private static Transform FindBone(Human h, string name)
    {
        Transform root = null;
        try { root = h.Body?._trfBodyBone; } catch { }
        foreach (var t in (root ?? h.Transform).GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
