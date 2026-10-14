using System.Text;
using Character;
using UnityEngine;

namespace AL_HWatchers;

// Masturbation poses converted from the player's own Aicomi install (tok_f_00 lying, tok_f_01 crouching,
// tok_f_03 standing). Amanatsu has no such animations, so the converted bone samples are played directly on
// the watcher's skeleton while her animator is paused. Both games share the same skeleton (bone rest rotations
// differ by under 1 degree); Amanatsu is built 10 times larger, which only matters for the hips position.
internal sealed class TokClip
{
    public string Name;
    public int Frames;
    public float Length;
    public string[] Bones;
    public Quaternion[][] Rot;   // [bone][frame], null when the bone has no rotation samples
    public Vector3[] HipsPos;    // [frame], Aicomi scale
}

internal static class TokLibrary
{
    private static readonly Dictionary<string, TokClip> _clips = new();
    public static bool Available => _clips.Count > 0;
    private static bool _importPending;

    // Uses the animations converted earlier, or converts them from Aicomi once the game is up.
    public static void Prepare()
    {
        if (File.Exists(AicomiImport.OutputPath)) Load(AicomiImport.OutputPath);
        if (!Available) _importPending = true;
    }

    public static void ImportIfNeeded()
    {
        if (!_importPending || Time.realtimeSinceStartup < 5f) return;
        _importPending = false;
        if (!Plugin.Masturbate.Value) return;
        var aicomi = AicomiImport.FindAicomi();
        if (aicomi == null)
        {
            Plugin.Log.LogInfo("Aicomi was not found, so watchers won't masturbate. If it is installed, set its folder in the config (Watchers > Aicomi Folder).");
            return;
        }
        if (AicomiImport.Convert(aicomi, AicomiImport.OutputPath)) Load(AicomiImport.OutputPath);
    }

    public static void Load(string path)
    {
        if (!File.Exists(path))
        {
            Plugin.Log.LogInfo($"No masturbation animations ({Path.GetFileName(path)} is missing), so watchers won't masturbate.");
            return;
        }
        try
        {
            using var r = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
            if (new string(r.ReadChars(4)) != "ALHW" || r.ReadInt32() != 1) throw new InvalidDataException("unknown format");
            var count = r.ReadInt32();
            for (var c = 0; c < count; c++)
            {
                var clip = new TokClip { Name = Str(r), Frames = r.ReadInt32(), Length = r.ReadSingle() };
                var boneCount = r.ReadInt32();
                clip.Bones = new string[boneCount];
                clip.Rot = new Quaternion[boneCount][];
                for (var b = 0; b < boneCount; b++)
                {
                    clip.Bones[b] = Str(r);
                    var flags = r.ReadByte();
                    if ((flags & 1) != 0)
                    {
                        var rot = clip.Rot[b] = new Quaternion[clip.Frames];
                        for (var f = 0; f < clip.Frames; f++) rot[f] = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    }
                    if ((flags & 2) != 0)
                    {
                        var pos = new Vector3[clip.Frames];
                        for (var f = 0; f < clip.Frames; f++) pos[f] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        if (clip.Bones[b] == "cf_j_hips") clip.HipsPos = pos;
                    }
                }
                if (boneCount > 0) _clips[clip.Name] = clip;
            }
            Plugin.Log.LogInfo($"Loaded {_clips.Count} masturbation animations.");
        }
        catch (Exception e)
        {
            _clips.Clear();
            Plugin.Log.LogError($"Could not read {Path.GetFileName(path)}: {e.Message}");
        }
    }

    public static TokClip Get(string set, string state) => _clips.TryGetValue($"{set}/{state}", out var c) ? c : null;

    private static string Str(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
}

// Plays TokClips on one girl's body skeleton. Her animator keeps running underneath (so nothing snaps to the
// bind pose); the clip is written over its result in LateUpdate and blended in and out against it.
internal sealed class TokPlayer
{
    private const float AicomiHipsHeight = 1.0785f;
    private readonly Dictionary<string, Transform> _bones = new();
    private readonly string _name;
    private Transform _hips, _hipsLength;
    private TokClip _clip;
    private Transform[] _targets;
    private float _time, _blend, _blendTime, _fade = 1f, _fadeTime;
    public bool Loop;
    public string State;
    public bool Finished => !Loop && _clip != null && _time >= _clip.Length;
    public bool Released => _fadeTime > 0f && _fade <= 0f;

    private readonly Hand[] _hands;
    private BodySurface _surface;
    private readonly Human _human;
    private float _measureAt = -1f, _reportAt;
    private readonly float _scale;
    private readonly HumanHand _handShapes;
    private readonly bool[] _handShapesWere = new bool[2];
    private readonly Dictionary<string, Quaternion> _last = new();
    private static readonly string[] FingerNames = { "thumb", "index", "middle", "ring", "little" };
    private readonly Transform[] _fingerShapes;
    private Quaternion?[] _from;
    private Vector3? _lastHips, _hipsFrom;

    public TokPlayer(Human h, string name, float worldScale)
    {
        _name = name;
        _scale = worldScale;
        // Only the body skeleton: clothes and the body mesh carry their own unused copies of the cf_j_ bones.
        var root = BodyBoneRoot(h);
        var fingerShapes = new List<Transform>();
        foreach (var t in (root ?? h.Transform).GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("cf_j_") && !_bones.ContainsKey(t.name)) _bones[t.name] = t;
            else if (t.name.StartsWith("cf_s_") && FingerNames.Any(f => t.name.Contains(f))) fingerShapes.Add(t);
        }
        _fingerShapes = fingerShapes.ToArray();
        _bones.TryGetValue("cf_j_hips", out _hips);
        _bones.TryGetValue("cf_j_hips_length", out _hipsLength);
        Plugin.Debug($"{name}: masturbation skeleton {(root != null ? Path(root) : "not found, using whole character")}, {_bones.Count} bones, hips {(_hips != null ? "found" : "missing")}.");
        _hands = new[] { Hand.Find(_bones, "L"), Hand.Find(_bones, "R") }.Where(x => x != null).ToArray();
        _human = h;
        Measure();
        // The game's hand shapes would bend her fingers again on top of the clip.
        try
        {
            _handShapes = h.Body?.Hand;
            for (var lr = 0; _handShapes != null && lr < 2; lr++)
            {
                _handShapesWere[lr] = _handShapes.GetEnableShapeHand(lr);
                _handShapes.SetEnableShapeHand(lr, false);
            }
        }
        catch { _handShapes = null; }
        // The game's pose correction (per-bone processes and finger corrections, normally fed by her own
        // animations) would keep bending bones for the pose she was in; it twisted her ring finger.
        try
        {
            foreach (var c in h.Transform.GetComponentsInChildren<ILLGAMES.Unity.Correct.Process.BaseProcess>(true))
                if (c.enabled) { c.enabled = false; _corrections.Add(c); }
            foreach (var c in h.Transform.GetComponentsInChildren<ILLGAMES.Unity.Correct.FingerCorrect>(true))
                if (c.enabled) { c.enabled = false; _corrections.Add(c); }
            foreach (var c in h.Transform.GetComponentsInChildren<ILLGAMES.Unity.Correct.BaseCorrect>(true))
                if (c.enabled) { c.enabled = false; _corrections.Add(c); }
            Plugin.Debug($"{name}: paused {_corrections.Count} pose corrections.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"{name}: could not pause the pose corrections: {e.Message}"); }
    }

    private readonly List<Behaviour> _corrections = new();

    private static Transform BodyBoneRoot(Human h)
    {
        try { var t = h.Body?._trfBodyBone; if (t != null) return t; } catch { }
        try { var go = h.Body?.GetObjBone(); if (go != null) return go.transform; } catch { }
        foreach (var t in h.Transform.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("p_cf_") && t.name.Contains("body_bone")) return t;
        return null;
    }

    private static string Path(Transform t)
    {
        var s = t.name;
        for (var p = t.parent; p != null && s.Length < 200; p = p.parent) s = p.name + "/" + s;
        return s;
    }

    public void Play(TokClip clip, string state, bool loop, float blendTime = 0.4f)
    {
        _clip = clip;
        State = state;
        Loop = loop;
        _time = 0f;
        _blend = 0f;
        _blendTime = blendTime;
        _targets = new Transform[clip.Bones.Length];
        _from = new Quaternion?[clip.Bones.Length];
        _hipsFrom = _lastHips;
        var mapped = 0;
        for (var b = 0; b < clip.Bones.Length; b++)
            if (clip.Rot[b] != null && _bones.TryGetValue(clip.Bones[b], out var t))
            {
                _targets[b] = t;
                // From the second clip on, blend from where the last one left her rather than from her idle pose.
                if (_last.TryGetValue(clip.Bones[b], out var q)) _from[b] = q;
                mapped++;
            }
        Plugin.Debug($"{_name}: playing {clip.Name}, {mapped}/{clip.Bones.Length} bones.");
    }

    private void Measure()
    {
        try { _surface = BodySurface.Build(_human, _bones, _name, _scale); }
        catch (Exception e) { _surface = null; Plugin.Log.LogWarning($"{_name}: could not measure her body, hands may pass through it: {e.Message}"); }
    }

    // Her clothes changed: measure again once the new pieces are showing.
    public void Remeasure(float delay = 0.25f) => _measureAt = Time.realtimeSinceStartup + delay;

    // Lets go of the body over a moment, back to whatever her animator is doing.
    public void Release(float time = 0.6f) => _fadeTime = time;

    public void Apply(float dt)
    {
        if (_clip == null) return;
        _time += dt;
        if (_blend < 1f) _blend = _blendTime > 0f ? Mathf.Min(1f, _blend + dt / _blendTime) : 1f;
        if (_fadeTime > 0f) _fade = Mathf.Max(0f, _fade - dt / _fadeTime);
        var bw = Mathf.SmoothStep(0f, 1f, _blend);
        var w = Mathf.SmoothStep(0f, 1f, _fade);
        if (w <= 0f) return;
        var t = Loop ? Mathf.Repeat(_time, _clip.Length) : Mathf.Min(_time, _clip.Length);
        var pos = _clip.Length > 0f ? t / _clip.Length * (_clip.Frames - 1) : 0f;
        var f0 = Mathf.Clamp((int)pos, 0, _clip.Frames - 1);
        var f1 = Mathf.Min(f0 + 1, _clip.Frames - 1);
        var a = pos - f0;
        for (var b = 0; b < _targets.Length; b++)
        {
            var target = _targets[b];
            if (target == null) continue;
            var rot = _clip.Rot[b];
            var q = Quaternion.Slerp(rot[f0], rot[f1], a);
            if (bw < 1f) q = Quaternion.Slerp(_from[b] ?? target.localRotation, q, bw);
            _last[_clip.Bones[b]] = q;
            target.localRotation = w < 1f ? Quaternion.Slerp(target.localRotation, q, w) : q;
        }
        if (_hips != null && _clip.HipsPos != null)
        {
            // Aicomi's hips sit straight under the height node; Amanatsu adds cf_j_hips_length (10x larger) in between.
            var lengthOffset = _hipsLength != null ? _hipsLength.localPosition : Vector3.zero;
            var k = lengthOffset.y > 0.01f ? lengthOffset.y / AicomiHipsHeight : 10f;
            var p = Vector3.Lerp(_clip.HipsPos[f0], _clip.HipsPos[f1], a) * k - lengthOffset;
            if (bw < 1f) p = Vector3.Lerp(_hipsFrom ?? _hips.localPosition, p, bw);
            _lastHips = p;
            _hips.localPosition = w < 1f ? Vector3.Lerp(_hips.localPosition, p, w) : p;
        }
        // The game poses fingers through these shape bones too; whatever bend they hold would add to the clip's.
        foreach (var f in _fingerShapes)
            if (f != null) f.localRotation = w < 1f ? Quaternion.Slerp(f.localRotation, Quaternion.identity, w) : Quaternion.identity;
        if (_measureAt > 0f && Time.realtimeSinceStartup >= _measureAt) { _measureAt = -1f; Measure(); }
        if (_surface != null)
        {
            _surface.Update();
            foreach (var hand in _hands) hand.KeepOut(_surface, _scale, w, dt);
            if (Time.realtimeSinceStartup >= _reportAt)
            {
                _reportAt = Time.realtimeSinceStartup + 6f;
                Plugin.Debug($"{_name} ({State}): overlap in cm: {string.Join("; ", _hands.Select(x => x.Report()))}.");
            }
        }
    }

    // Gives her hands back to the game once she has let go.
    public void End()
    {
        try
        {
            for (var lr = 0; _handShapes != null && lr < 2; lr++)
                if (_handShapesWere[lr]) _handShapes.SetEnableShapeHand(lr, true);
        }
        catch { }
        foreach (var c in _corrections)
            try { if (c != null) c.enabled = true; } catch { }
        _corrections.Clear();
    }
}


// The parts of her body her hands touch (breasts and crotch), measured once from her own body mesh: surface points
// with their normals, each kept in the space of the bone nearest to it so they follow her pose, size and bounce.
internal sealed class BodySurface
{
    private Transform[] _bones;
    private int[] _bone;
    private Vector3[] _local, _localN;
    private Vector3[] _world, _worldN;
    private BreastShape[] _breasts = Array.Empty<BreastShape>();

    private static readonly string[] Anchors =
    {
        "cf_j_spine02", "cf_j_spine03", "cf_j_bust01_L", "cf_j_bust01_R", "cf_j_bust02_L", "cf_j_bust02_R", "cf_j_bust03_L", "cf_j_bust03_R",
        "cf_j_hips", "cf_j_waist01", "cf_j_waist02", "cf_j_kokan", "cf_j_thigh00_L", "cf_j_thigh00_R", "cf_j_leg01_L", "cf_j_leg01_R"
    };

    public static BodySurface Build(Human h, Dictionary<string, Transform> skeleton, string name, float k)
    {
        var meshes = FindBodyMeshes(h);
        if (meshes.Count == 0) throw new InvalidOperationException("no body mesh found");
        var anchors = new List<Transform>(); var anchorNames = new List<string>();
        foreach (var n in Anchors)
            if (skeleton.TryGetValue(n, out var t)) { anchors.Add(t); anchorNames.Add(n); }
        skeleton.TryGetValue("cf_j_kokan", out var kokan);
        if (anchors.Count < 4) throw new InvalidOperationException("skeleton bones missing");
        var anchorPos = anchors.Select(b => b.position).ToArray();
        var crotch = kokan != null ? kokan.position : Vector3.zero;
        var crotchReach = 0.16f * k;

        var boneList = new List<Transform>(); var boneIndex = new Dictionary<IntPtr, int>();
        var bone = new List<int>(); var local = new List<Vector3>(); var localN = new List<Vector3>();
        var report = new List<string>();
        var breastPoints = new[] { new List<Vector3>(), new List<Vector3>() };
        skeleton.TryGetValue("cf_j_bust02_L", out var bustL);
        skeleton.TryGetValue("cf_j_bust02_R", out var bustR);
        foreach (var (body, skinPiece) in meshes)
        {
            var baked = new Mesh();
            body.BakeMesh(baked);
            var verts = baked.vertices;
            var norms = baked.normals;
            UnityEngine.Object.Destroy(baked);
            var toWorld = BakedSpace(body, verts, skeleton, k, name, out var rotate);

            // Skin weights tie each point to the bone that really moves it; without them, the nearest skeleton bone.
            Transform[] skin = null; int[] dominant = null;
            try
            {
                var mesh = body.sharedMesh;
                if (mesh.isReadable)
                {
                    var weights = mesh.boneWeights;
                    if (weights != null && weights.Length == verts.Length)
                    {
                        skin = body.bones.ToArray();
                        dominant = new int[weights.Length];
                        for (var v = 0; v < weights.Length; v++)
                        {
                            var w = weights[v];
                            int bi = w.boneIndex0; var bw = w.weight0;
                            if (w.weight1 > bw) { bi = w.boneIndex1; bw = w.weight1; }
                            if (w.weight2 > bw) { bi = w.boneIndex2; bw = w.weight2; }
                            if (w.weight3 > bw) { bi = w.boneIndex3; }
                            dominant[v] = bi;
                        }
                    }
                }
            }
            catch { skin = null; dominant = null; }

            int breastCount = 0, crotchCount = 0;
            for (var v = 0; v < verts.Length; v++)
            {
                var p = toWorld(verts[v]);
                var best = float.MaxValue; var ai = 0;
                for (var i = 0; i < anchorPos.Length; i++) { var d = (anchorPos[i] - p).sqrMagnitude; if (d < best) { best = d; ai = i; } }
                Transform attach = anchors[ai];
                bool breast;
                if (dominant != null && dominant[v] < skin.Length && skin[dominant[v]] != null)
                {
                    attach = skin[dominant[v]];
                    var bn = attach.name;
                    breast = (bn.Contains("bust") || bn.Contains("bnip")) && !bn.Contains("bust00");
                }
                else breast = anchorNames[ai].StartsWith("cf_j_bust02") || anchorNames[ai].StartsWith("cf_j_bust03");
                // Clothes only count over the breasts: around the crotch a skirt would push her hand out from under it.
                var nearCrotch = skinPiece && kokan != null && (p - crotch).magnitude < crotchReach;
                if (!breast && !nearCrotch) continue;
                var n = norms != null && v < norms.Length ? rotate(norms[v]) : Vector3.zero;
                if (n.sqrMagnitude < 1e-8f) continue;
                if (breast)
                {
                    // Breasts get their own shape (below); the point-by-point surface is for the crotch.
                    breastCount++;
                    var right = attach.name.EndsWith("_R") || (!attach.name.EndsWith("_L") && bustL != null && bustR != null
                        && (p - bustR.position).sqrMagnitude < (p - bustL.position).sqrMagnitude);
                    breastPoints[right ? 1 : 0].Add(p);
                    continue;
                }
                crotchCount++;
                if (!boneIndex.TryGetValue(attach.Pointer, out var idx)) { idx = boneList.Count; boneList.Add(attach); boneIndex[attach.Pointer] = idx; }
                bone.Add(idx);
                local.Add(attach.InverseTransformPoint(p));
                localN.Add(attach.InverseTransformDirection(n.normalized));
            }
            report.Add($"{body.name} ({verts.Length} vertices, {(dominant != null ? "weights" : "nearest bones")}): {breastCount} breast, {crotchCount} crotch");
        }
        var breasts = new List<BreastShape>();
        var shapes = new[] { BreastShape.Build(bustL, breastPoints[0], Forward(skeleton, "L")), BreastShape.Build(bustR, breastPoints[1], Forward(skeleton, "R")) };
        foreach (var b in shapes) if (b != null) breasts.Add(b);
        Plugin.Debug($"{name}: body measured from {string.Join("; ", report)}. Breasts: {string.Join(", ", breasts.Select(b => b.Describe(k)))}.");
        if (bone.Count == 0 && breasts.Count == 0) return null;
        return new BodySurface
        {
            _bones = boneList.ToArray(), _bone = bone.ToArray(), _local = local.ToArray(), _localN = localN.ToArray(),
            _world = new Vector3[bone.Count], _worldN = new Vector3[bone.Count], _breasts = breasts.ToArray()
        };
    }

    // Which way a breast points: from its root on the chest to the nipple.
    private static Vector3 Forward(Dictionary<string, Transform> skeleton, string side) =>
        skeleton.TryGetValue("cf_j_bust01_" + side, out var a) && skeleton.TryGetValue("cf_j_bust03_" + side, out var b)
            ? b.position - a.position : Vector3.zero;

    // Which space BakeMesh left the points in: whichever reading puts her surface closest to her own bones.
    private static Func<Vector3, Vector3> BakedSpace(SkinnedMeshRenderer smr, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> verts,
        Dictionary<string, Transform> skeleton, float k, string name, out Func<Vector3, Vector3> rotate)
    {
        var tr = smr.transform;
        var pos = tr.position; var rot = tr.rotation; var m = tr.localToWorldMatrix;
        var root = smr.rootBone; var rm = root != null ? root.localToWorldMatrix : m; var rr = root != null ? root.rotation : rot;
        var options = new (string name, Func<Vector3, Vector3> map, Func<Vector3, Vector3> rot)[]
        {
            ("renderer, unscaled", v => pos + rot * v, v => rot * v),
            ("renderer", v => m.MultiplyPoint3x4(v), v => m.MultiplyVector(v)),
            ("root bone", v => rm.MultiplyPoint3x4(v), v => rm.MultiplyVector(v)),
            ("root bone, unscaled", v => (root != null ? root.position : pos) + rr * v, v => rr * v),
            ("world", v => v, v => v),
        };
        var bones = skeleton.Values.Where(b => b != null).Select(b => b.position).ToArray();
        var step = Math.Max(1, verts.Length / 300);
        var bestScore = float.MaxValue; var best = 0;
        var report = new List<string>();
        for (var o = 0; o < options.Length; o++)
        {
            double sum = 0; var count = 0;
            for (var v = 0; v < verts.Length; v += step)
            {
                var p = options[o].map(verts[v]);
                var d = float.MaxValue;
                foreach (var b in bones) d = Mathf.Min(d, (b - p).sqrMagnitude);
                sum += Math.Sqrt(d); count++;
            }
            var score = (float)(sum / Math.Max(1, count));
            report.Add($"{options[o].name} {score / k * 100f:0.0} cm");
            if (score < bestScore) { bestScore = score; best = o; }
        }
        Plugin.Debug($"{name}: {smr.name} mesh space {options[best].name} (average distance to bones: {string.Join(", ", report)}).");
        rotate = options[best].rot;
        return options[best].map;
    }

    // What her hands can touch right now: the body pieces that are showing (the torso comes in several variants,
    // only one of them active), and the clothes she has on, which count over the breasts only.
    private static List<(SkinnedMeshRenderer smr, bool skin)> FindBodyMeshes(Human h)
    {
        Transform bodyObj = null;
        try { bodyObj = h.Body?._objBody?.transform; } catch { }
        var list = new List<(SkinnedMeshRenderer, bool)>();
        foreach (var smr in h.Transform.GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (smr.sharedMesh == null || !smr.enabled || !smr.gameObject.activeInHierarchy) continue;
            var n = smr.name;
            if (n.Contains("silhouette") || n.StartsWith("o_add")) continue;
            var skin = bodyObj != null ? smr.transform.IsChildOf(bodyObj) : n.StartsWith("o_body") || n.StartsWith("o_upper") || n.StartsWith("o_onepi");
            if (!skin)
            {
                var bones = smr.bones; var bust = false;
                for (var i = 0; bones != null && i < bones.Length && !bust; i++) bust = bones[i] != null && bones[i].name.Contains("bust");
                if (!bust) continue;
            }
            list.Add((smr, skin));
        }
        return list;
    }

    public void Update()
    {
        foreach (var b in _breasts) b.Update();
        var m = new Matrix4x4[_bones.Length];
        for (var i = 0; i < _bones.Length; i++) m[i] = _bones[i].localToWorldMatrix;
        for (var i = 0; i < _bone.Length; i++)
        {
            var mm = m[_bone[i]];
            _world[i] = mm.MultiplyPoint3x4(_local[i]);
            _worldN[i] = mm.MultiplyVector(_localN[i]).normalized;
        }
    }

    // How far a point (with some thickness of its own) is inside her, and which way is out.
    public bool Depth(Vector3 p, float margin, float reach, out Vector3 push, bool sideways = false)
    {
        var inSurface = SurfaceDepth(p, margin, reach, out push);
        foreach (var b in _breasts)
            if (b.Depth(p, margin, sideways, out var bp) && bp.sqrMagnitude > push.sqrMagnitude) { push = bp; inSurface = true; }
        return inSurface;
    }

    private bool SurfaceDepth(Vector3 p, float margin, float reach, out Vector3 push)
    {
        push = Vector3.zero;
        var r2 = reach * reach;
        int n0 = -1, n1 = -1, n2 = -1; float d0 = r2, d1 = r2, d2 = r2;
        for (var i = 0; i < _world.Length; i++)
        {
            var d = (_world[i] - p).sqrMagnitude;
            if (d >= d2) continue;
            if (d < d0) { n2 = n1; d2 = d1; n1 = n0; d1 = d0; n0 = i; d0 = d; }
            else if (d < d1) { n2 = n1; d2 = d1; n1 = i; d1 = d; }
            else { n2 = i; d2 = d; }
        }
        if (n0 < 0) return false;
        Vector3 sp = Vector3.zero, sn = Vector3.zero; var c = 0;
        foreach (var i in new[] { n0, n1, n2 })
        {
            if (i < 0) continue;
            sp += _world[i]; sn += _worldN[i]; c++;
        }
        sp /= c; sn = sn.normalized;
        var depth = margin - Vector3.Dot(p - sp, sn);
        if (depth <= 0f) return false;
        push = sn * depth;
        return true;
    }
}

// A breast's shape as seen from its middle: for every direction, how far out her skin (or her bra) reaches, kept
// in the breast bone's space so it follows her size and the bounce. Works however deep a hand has gone in.
internal sealed class BreastShape
{
    private const int Rows = 24, Cols = 48;
    private Transform _bone;
    private Vector3 _center;
    private float[] _reach;
    private Matrix4x4 _toLocal, _toWorld;
    private float _scale;
    private int _count;
    private Vector3 _axis;      // bone space, towards the nipple
    private float _maxReach;

    public static BreastShape Build(Transform bone, List<Vector3> world, Vector3 forward)
    {
        if (bone == null || world.Count < 30 || forward.sqrMagnitude < 1e-10f) return null;
        var pts = world.Select(bone.InverseTransformPoint).ToList();
        var c = Vector3.zero;
        foreach (var q in pts) c += q;
        c /= pts.Count;
        var reach = new float[Rows * Cols];
        foreach (var q in pts)
        {
            var d = q - c; var r = d.magnitude;
            if (r < 1e-6f) continue;
            var i = Bin(d / r);
            if (r > reach[i]) reach[i] = r;
        }
        // Fill the small gaps between sampled directions from their neighbours.
        for (var pass = 0; pass < 2; pass++)
        {
            var copy = (float[])reach.Clone();
            for (var row = 0; row < Rows; row++)
                for (var col = 0; col < Cols; col++)
                {
                    if (copy[row * Cols + col] > 0f) continue;
                    float sum = 0; var n = 0;
                    for (var dr = -1; dr <= 1; dr++)
                        for (var dc = -1; dc <= 1; dc++)
                        {
                            var r2 = row + dr; if (r2 < 0 || r2 >= Rows) continue;
                            var v = copy[r2 * Cols + (col + dc + Cols) % Cols];
                            if (v > 0f) { sum += v; n++; }
                        }
                    if (n >= 3) reach[row * Cols + col] = sum / n;
                }
        }
        return new BreastShape
        {
            _bone = bone, _center = c, _reach = reach, _count = pts.Count, _maxReach = reach.Max(),
            _axis = bone.InverseTransformDirection(forward).normalized
        };
    }

    private static int Bin(Vector3 u)
    {
        var row = Mathf.Clamp((int)(Mathf.Acos(Mathf.Clamp(u.y, -1f, 1f)) / Mathf.PI * Rows), 0, Rows - 1);
        var col = Mathf.Clamp((int)((Mathf.Atan2(u.z, u.x) + Mathf.PI) / (2f * Mathf.PI) * Cols), 0, Cols - 1);
        return row * Cols + col;
    }

    public string Describe(float k) => $"{_bone.name} {_count} points, up to {_reach.Max() * _bone.lossyScale.x / k * 100f:0} cm from its middle";

    public void Update()
    {
        _toLocal = _bone.worldToLocalMatrix;
        _toWorld = _bone.localToWorldMatrix;
        _scale = Mathf.Max(1e-6f, _bone.lossyScale.x);
    }

    private bool Inside(Vector3 local, float margin)
    {
        var d = local - _center;
        var r = d.magnitude;
        if (r < 1e-6f) return true;
        var reach = _reach[Bin(d / r)];
        return reach > 0f && r < reach + margin;
    }

    // How far a point inside has to move to get out. Hands and forearms come out the front, the way the breast
    // points (going round her side would send them towards her armpit); upper arms come out to the side.
    public bool Depth(Vector3 p, float margin, bool sideways, out Vector3 push)
    {
        push = Vector3.zero;
        var q = _toLocal.MultiplyPoint3x4(p);
        var m = margin / _scale;
        if (!Inside(q, m)) return false;
        var dir = _axis;
        if (sideways)
        {
            var side = q - _center - Vector3.Project(q - _center, _axis);
            if (side.sqrMagnitude > 1e-8f) dir = side.normalized;
        }
        var step = _maxReach / 24f;
        var t = step;
        for (; t < _maxReach * 2.5f; t += step)
            if (!Inside(q + dir * t, m)) break;
        push = _toWorld.MultiplyVector(dir * t);
        return true;
    }
}

// One arm. When the clip puts her hand, forearm or upper arm inside her body (her proportions differ from the
// ones the animation was made for, most of all with big breasts), the arm is moved out: the hand goes to the
// surface, a forearm lying in a breast lifts hand and elbow together, an upper arm pressing in swings the elbow
// out, and the arm is bent to fit.
internal sealed class Hand
{
    private Transform _upper, _lower, _hand, _palm;
    private Transform[] _tips;
    private Vector3 _handPush, _elbowPush;
    private string _side;
    private float _seenHand, _seenForearm, _seenUpper, _seenApplied, _seenLeft;

    public static Hand Find(Dictionary<string, Transform> bones, string side)
    {
        if (!bones.TryGetValue("cf_j_arm00_" + side, out var u) || !bones.TryGetValue("cf_j_forearm01_" + side, out var l)
            || !bones.TryGetValue("cf_j_hand_" + side, out var h)) return null;
        var tips = new List<Transform>();
        foreach (var f in new[] { "index03", "middle03", "ring03" })
            if (bones.TryGetValue($"cf_j_{f}_{side}", out var t)) tips.Add(t);
        bones.TryGetValue("cf_j_middle01_" + side, out var palm);
        return new Hand { _upper = u, _lower = l, _hand = h, _tips = tips.ToArray(), _palm = palm, _side = side };
    }

    // Smooth rather than exact: the correction follows slowly (about a third of a second) so the arm glides
    // instead of shaking from frame to frame, and leaves about a centimeter of overlap, however big she is.
    private const float Follow = 2f, RiseRate = 6f, Allowed = 0.004f;
    private static readonly float[] ForearmAt = { 0.2f, 0.45f, 0.7f, 0.9f };
    private static readonly float[] UpperArmAt = { 0.5f, 0.75f, 1f };

    public void KeepOut(BodySurface body, float k, float weight, float dt)
    {
        var reach = 0.16f * k; // deep enough to find the way out of big breasts
        Vector3 shoulder = _upper.position, elbow = _lower.position, wrist = _hand.position;

        // The hand moves as one piece: find how far it has to go for the palm and the fingertips to all be out.
        // Fingertips may press in about a centimeter, as fingers squeezing would.
        var handPts = new List<(Vector3 p, float margin)>();
        if (_palm != null) handPts.Add((Vector3.Lerp(wrist, _palm.position, 0.6f), 0.012f * k));
        foreach (var t in _tips) handPts.Add((t.position, -0.01f * k));
        // Arm thickness around the bone: 3.5 cm at the elbow down to 2.5 cm at the wrist, 4 cm for the upper arm.
        var forearmPts = ForearmAt.Select(t => (Vector3.Lerp(elbow, wrist, t), Mathf.Lerp(0.035f, 0.025f, t) * k)).ToList();
        var upperPts = UpperArmAt.Select(t => (Vector3.Lerp(shoulder, elbow, t), 0.04f * k)).ToList();

        var hand = Solve(body, handPts, reach, out var handDepth);
        var forearm = Solve(body, forearmPts, reach, out var forearmDepth);
        var upperArm = Solve(body, upperPts, reach, out var upperDepth, true);
        _seenHand = Mathf.Max(_seenHand, handDepth / k);
        _seenForearm = Mathf.Max(_seenForearm, forearmDepth / k);
        _seenUpper = Mathf.Max(_seenUpper, upperDepth / k);
        hand = Trim(hand, k); forearm = Trim(forearm, k); upperArm = Trim(upperArm, k);
        // A forearm inside her moves the whole forearm: the hand takes whatever part of it its own push lacks.
        if (forearm != Vector3.zero)
        {
            var dir = forearm.normalized;
            hand += dir * Mathf.Max(0f, forearm.magnitude - Vector3.Dot(hand, dir));
        }
        var elbowPush = forearm + upperArm;

        // Moves out briskly and settles back slowly, so it neither lags inside her nor shakes.
        _handPush = Ease(_handPush, hand, dt);
        _elbowPush = Ease(_elbowPush, elbowPush, dt);
        _seenApplied = Mathf.Max(_seenApplied, _handPush.magnitude * weight / k);
        if (_handPush.sqrMagnitude < 1e-8f && _elbowPush.sqrMagnitude < 1e-8f) return;
        Reach(wrist + _handPush * weight, elbow + _elbowPush * weight);

        // What is still inside after the move, for the log.
        var left = 0f;
        var moved = _hand.position - wrist;
        foreach (var (p, m) in handPts)
            if (body.Depth(p + moved, m, reach, out var push)) left = Mathf.Max(left, push.magnitude);
        _seenLeft = Mathf.Max(_seenLeft, left / k);
    }

    // How far a rigid piece has to move for all its points to be out: a few steps, each pushing out the point
    // that is deepest from where the previous steps left it.
    private static Vector3 Solve(BodySurface body, List<(Vector3 p, float margin)> pts, float reach, out float firstDepth, bool sideways = false)
    {
        var total = Vector3.zero;
        firstDepth = 0f;
        for (var step = 0; step < 4; step++)
        {
            var best = Vector3.zero;
            foreach (var (p, m) in pts)
                if (body.Depth(p + total, m, reach, out var push, sideways) && push.sqrMagnitude > best.sqrMagnitude) best = push;
            if (step == 0) firstDepth = best.magnitude;
            if (best.sqrMagnitude < 1e-10f) break;
            total += best;
        }
        return total;
    }

    private static Vector3 Ease(Vector3 current, Vector3 target, float dt)
    {
        var rate = target.sqrMagnitude > current.sqrMagnitude ? RiseRate : Follow;
        return Vector3.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));
    }

    // What the arm ran into since the last report (centimeters), for the log.
    public string Report()
    {
        var r = $"{_side} hand {_seenHand * 100f:0.0}, forearm {_seenForearm * 100f:0.0}, upper arm {_seenUpper * 100f:0.0}, hand moved {_seenApplied * 100f:0.0}, still in {_seenLeft * 100f:0.0}";
        _seenHand = _seenForearm = _seenUpper = _seenApplied = _seenLeft = 0f;
        return r;
    }

    private static Vector3 Trim(Vector3 push, float k)
    {
        var depth = push.magnitude;
        return depth > Allowed * k ? push * ((depth - Allowed * k) / depth) : Vector3.zero;
    }

    private static void Deepest(BodySurface body, Vector3 p, float margin, float reach, ref Vector3 best)
    {
        if (body.Depth(p, margin, reach, out var push) && push.sqrMagnitude > best.sqrMagnitude) best = push;
    }

    // Two-bone reach: the hand to its target with its own rotation kept, the elbow bending towards where it
    // should be (where the clip had it, unless it was pushed out).
    private void Reach(Vector3 target, Vector3 elbowHint)
    {
        var handRot = _hand.rotation;
        Vector3 a = _upper.position, b = _lower.position, c = _hand.position;
        float l1 = (b - a).magnitude, l2 = (c - b).magnitude;
        var at = target - a;
        var d = Mathf.Clamp(at.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
        var dir = at.normalized;
        var pole = (elbowHint - a) - Vector3.Project(elbowHint - a, dir);
        if (pole.sqrMagnitude < 1e-8f) pole = (b - a) - Vector3.Project(b - a, dir);
        if (pole.sqrMagnitude < 1e-8f) return;
        pole.Normalize();
        var cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
        var elbow = a + (dir * cosA + pole * Mathf.Sqrt(1f - cosA * cosA)) * l1;
        _upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * _upper.rotation;
        var b1 = _lower.position;
        _lower.rotation = Quaternion.FromToRotation(_hand.position - b1, a + dir * d - b1) * _lower.rotation;
        _hand.rotation = handRot;
    }
}
