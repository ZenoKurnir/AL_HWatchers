using System.Text;
using Character;
using UnityEngine;
using NV = System.Numerics.Vector3;
using NQ = System.Numerics.Quaternion;

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
    public NQ[][] Rot;           // [bone][frame], null when the bone has no rotation samples
    public NV[] HipsPos;         // [frame], Aicomi scale
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
                clip.Rot = new NQ[boneCount][];
                for (var b = 0; b < boneCount; b++)
                {
                    clip.Bones[b] = Str(r);
                    var flags = r.ReadByte();
                    if ((flags & 1) != 0)
                    {
                        var rot = clip.Rot[b] = new NQ[clip.Frames];
                        for (var f = 0; f < clip.Frames; f++) rot[f] = new NQ(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    }
                    if ((flags & 2) != 0)
                    {
                        var pos = new NV[clip.Frames];
                        for (var f = 0; f < clip.Frames; f++) pos[f] = new NV(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
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
// All the math runs in managed code (see Fast): only reading and writing her bones goes through the game.
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
    private readonly Dictionary<string, NQ> _last = new();
    private static readonly string[] FingerNames = { "thumb", "index", "middle", "ring", "little" };
    private readonly Transform[] _fingerShapes;
    private NQ?[] _from;
    private NV? _lastHips, _hipsFrom;
    // Measuring a body is the costly part of starting: one girl per frame, so several starting together don't stall it.
    private static int _measuredFrame = -1;

    public TokPlayer(Human h, string name, float worldScale)
    {
        _name = name;
        _scale = worldScale;
        // Only the body skeleton: clothes and the body mesh carry their own unused copies of the cf_j_ bones.
        var root = BodyBoneRoot(h);
        var fingerShapes = new List<Transform>();
        foreach (var t in (root ?? h.Transform).GetComponentsInChildren<Transform>(true))
        {
            var n = t.name;
            if (n.StartsWith("cf_j_") && !_bones.ContainsKey(n)) _bones[n] = t;
            else if (n.StartsWith("cf_s_") && FingerNames.Any(f => n.Contains(f))) fingerShapes.Add(t);
        }
        _fingerShapes = fingerShapes.ToArray();
        _bones.TryGetValue("cf_j_hips", out _hips);
        _bones.TryGetValue("cf_j_hips_length", out _hipsLength);
        Plugin.Debug($"{name}: masturbation skeleton {(root != null ? Path(root) : "not found, using whole character")}, {_bones.Count} bones, hips {(_hips != null ? "found" : "missing")}.");
        _hands = new[] { Hand.Find(_bones, "L"), Hand.Find(_bones, "R") }.Where(x => x != null).ToArray();
        _human = h;
        _measureAt = 0f; // on her first frame
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
        _from = new NQ?[clip.Bones.Length];
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
        var t0 = Time.realtimeSinceStartup;
        try { _surface = BodySurface.Build(_human, _bones, _name, _scale); }
        catch (Exception e) { _surface = null; Plugin.Log.LogWarning($"{_name}: could not measure her body, hands may pass through it: {e.Message}"); }
        Plugin.Debug($"{_name}: body measured in {(Time.realtimeSinceStartup - t0) * 1000f:0} ms.");
    }

    // Her clothes changed: measure again once the new pieces are showing.
    public void Remeasure(float delay = 0.25f) => _measureAt = Time.realtimeSinceStartup + delay;

    // Lets go of the body over a moment, back to whatever her animator is doing.
    public void Release(float time = 0.6f) => _fadeTime = time;

    public void Apply(float dt)
    {
        if (_clip == null) return;
        _time += dt;
        if (_blend < 1f) _blend = _blendTime > 0f ? Math.Min(1f, _blend + dt / _blendTime) : 1f;
        if (_fadeTime > 0f) _fade = Math.Max(0f, _fade - dt / _fadeTime);
        var bw = Fast.SmoothStep(0f, 1f, _blend);
        var w = Fast.SmoothStep(0f, 1f, _fade);
        if (w <= 0f) return;
        var len = _clip.Length;
        var t = Loop ? (len > 0f ? _time - MathF.Floor(_time / len) * len : 0f) : Math.Min(_time, len);
        var pos = len > 0f ? t / len * (_clip.Frames - 1) : 0f;
        var f0 = Math.Clamp((int)pos, 0, _clip.Frames - 1);
        var f1 = Math.Min(f0 + 1, _clip.Frames - 1);
        var a = Fast.Clamp01(pos - f0);
        for (var b = 0; b < _targets.Length; b++)
        {
            var target = _targets[b];
            if (target == null) continue;
            var rot = _clip.Rot[b];
            var q = NQ.Slerp(rot[f0], rot[f1], a);
            // Her current rotation is read from the game only while blending in or letting go.
            if (bw < 1f) q = NQ.Slerp(_from[b] ?? Fast.N(target.localRotation), q, bw);
            _last[_clip.Bones[b]] = q;
            target.localRotation = Fast.U(w < 1f ? NQ.Slerp(Fast.N(target.localRotation), q, w) : q);
        }
        if (_hips != null && _clip.HipsPos != null)
        {
            // Aicomi's hips sit straight under the height node; Amanatsu adds cf_j_hips_length (10x larger) in between.
            var lengthOffset = _hipsLength != null ? Fast.N(_hipsLength.localPosition) : NV.Zero;
            var k = lengthOffset.Y > 0.01f ? lengthOffset.Y / AicomiHipsHeight : 10f;
            var p = NV.Lerp(_clip.HipsPos[f0], _clip.HipsPos[f1], a) * k - lengthOffset;
            if (bw < 1f) p = NV.Lerp(_hipsFrom ?? Fast.N(_hips.localPosition), p, bw);
            _lastHips = p;
            _hips.localPosition = Fast.U(w < 1f ? NV.Lerp(Fast.N(_hips.localPosition), p, w) : p);
        }
        // The game poses fingers through these shape bones too; whatever bend they hold would add to the clip's.
        foreach (var f in _fingerShapes)
            if (f != null) f.localRotation = w < 1f ? Fast.U(NQ.Slerp(Fast.N(f.localRotation), NQ.Identity, w)) : Fast.Identity;
        if (_measureAt >= 0f && Time.frameCount != _measuredFrame && Time.realtimeSinceStartup >= _measureAt)
        {
            _measuredFrame = Time.frameCount;
            _measureAt = -1f;
            Measure();
        }
        if (_surface != null)
        {
            _surface.Update();
            foreach (var hand in _hands) hand.KeepOut(_surface, _scale, w, dt);
            if (Plugin.DebugLog.Value && Time.realtimeSinceStartup >= _reportAt)
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
    private Affine[] _matrices;
    private int[] _bone;
    private NV[] _local, _localN;
    private NV[] _world, _worldN;
    private NV _min, _max;      // the box around the crotch points this frame, to skip the search when far away
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
        var anchorPos = anchors.Select(b => Fast.N(b.position)).ToArray();
        var crotch = kokan != null ? Fast.N(kokan.position) : NV.Zero;
        var crotchReach = 0.16f * k;

        var boneList = new List<Transform>(); var boneIndex = new Dictionary<IntPtr, int>();
        // Each bone's world-to-local transform, read once.
        var inverse = new Dictionary<IntPtr, (Affine point, NQ dir)>();
        (Affine point, NQ dir) InverseOf(Transform t)
        {
            if (!inverse.TryGetValue(t.Pointer, out var inv)) inverse[t.Pointer] = inv = (new Affine(t.worldToLocalMatrix), Fast.InverseRotation(t));
            return inv;
        }
        var bone = new List<int>(); var local = new List<NV>(); var localN = new List<NV>();
        var report = new List<string>();
        var breastPoints = new[] { new List<NV>(), new List<NV>() };
        skeleton.TryGetValue("cf_j_bust02_L", out var bustL);
        skeleton.TryGetValue("cf_j_bust02_R", out var bustR);
        var bustLPos = bustL != null ? Fast.N(bustL.position) : NV.Zero;
        var bustRPos = bustR != null ? Fast.N(bustR.position) : NV.Zero;
        foreach (var (body, skinPiece) in meshes)
        {
            var baked = new Mesh();
            body.BakeMesh(baked);
            var bakedVerts = baked.vertices;
            var bakedNorms = baked.normals;
            UnityEngine.Object.Destroy(baked);
            var verts = new NV[bakedVerts.Length];
            for (var v = 0; v < verts.Length; v++) verts[v] = Fast.N(bakedVerts[v]);
            var norms = new NV[bakedNorms?.Length ?? 0];
            for (var v = 0; v < norms.Length; v++) norms[v] = Fast.N(bakedNorms[v]);
            var toWorld = BakedSpace(body, verts, skeleton, k, name, out var rotate);

            // Skin weights tie each point to the bone that really moves it; without them, the nearest skeleton bone.
            Transform[] skin = null; string[] skinNames = null; int[] dominant = null;
            try
            {
                var mesh = body.sharedMesh;
                if (mesh.isReadable)
                {
                    var weights = mesh.boneWeights;
                    if (weights != null && weights.Length == verts.Length)
                    {
                        skin = body.bones.ToArray();
                        skinNames = skin.Select(t => t != null ? t.name : null).ToArray();
                        dominant = new int[weights.Length];
                        for (var v = 0; v < weights.Length; v++)
                        {
                            var w = weights[v];
                            int bi = w.m_BoneIndex0; var bw = w.m_Weight0;
                            if (w.m_Weight1 > bw) { bi = w.m_BoneIndex1; bw = w.m_Weight1; }
                            if (w.m_Weight2 > bw) { bi = w.m_BoneIndex2; bw = w.m_Weight2; }
                            if (w.m_Weight3 > bw) { bi = w.m_BoneIndex3; }
                            dominant[v] = bi;
                        }
                    }
                }
            }
            catch { skin = null; skinNames = null; dominant = null; }

            int breastCount = 0, crotchCount = 0;
            for (var v = 0; v < verts.Length; v++)
            {
                var p = toWorld(verts[v]);
                var best = float.MaxValue; var ai = 0;
                for (var i = 0; i < anchorPos.Length; i++) { var d = NV.DistanceSquared(anchorPos[i], p); if (d < best) { best = d; ai = i; } }
                Transform attach = anchors[ai];
                string attachName = anchorNames[ai];
                bool breast;
                if (dominant != null && dominant[v] < skin.Length && skin[dominant[v]] != null)
                {
                    attach = skin[dominant[v]];
                    attachName = skinNames[dominant[v]];
                    breast = (attachName.Contains("bust") || attachName.Contains("bnip")) && !attachName.Contains("bust00");
                }
                else breast = attachName.StartsWith("cf_j_bust02") || attachName.StartsWith("cf_j_bust03");
                // Clothes only count over the breasts: around the crotch a skirt would push her hand out from under it.
                var nearCrotch = skinPiece && kokan != null && (p - crotch).Length() < crotchReach;
                if (!breast && !nearCrotch) continue;
                var n = v < norms.Length ? rotate(norms[v]) : NV.Zero;
                if (n.LengthSquared() < 1e-8f) continue;
                if (breast)
                {
                    // Breasts get their own shape (below); the point-by-point surface is for the crotch.
                    breastCount++;
                    var right = attachName.EndsWith("_R") || (!attachName.EndsWith("_L") && bustL != null && bustR != null
                        && NV.DistanceSquared(p, bustRPos) < NV.DistanceSquared(p, bustLPos));
                    breastPoints[right ? 1 : 0].Add(p);
                    continue;
                }
                crotchCount++;
                if (!boneIndex.TryGetValue(attach.Pointer, out var idx)) { idx = boneList.Count; boneList.Add(attach); boneIndex[attach.Pointer] = idx; }
                var inv = InverseOf(attach);
                bone.Add(idx);
                local.Add(inv.point.Point(p));
                localN.Add(NV.Transform(NV.Normalize(n), inv.dir));
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
            _bones = boneList.ToArray(), _matrices = new Affine[boneList.Count], _bone = bone.ToArray(), _local = local.ToArray(), _localN = localN.ToArray(),
            _world = new NV[bone.Count], _worldN = new NV[bone.Count], _breasts = breasts.ToArray()
        };
    }

    // Which way a breast points: from its root on the chest to the nipple.
    private static NV Forward(Dictionary<string, Transform> skeleton, string side) =>
        skeleton.TryGetValue("cf_j_bust01_" + side, out var a) && skeleton.TryGetValue("cf_j_bust03_" + side, out var b)
            ? Fast.N(b.position) - Fast.N(a.position) : NV.Zero;

    // Which space BakeMesh left the points in: whichever reading puts her surface closest to her own bones.
    private static Func<NV, NV> BakedSpace(SkinnedMeshRenderer smr, NV[] verts, Dictionary<string, Transform> skeleton, float k, string name, out Func<NV, NV> rotate)
    {
        var tr = smr.transform;
        var pos = Fast.N(tr.position); var rot = Fast.N(tr.rotation); var m = new Affine(tr.localToWorldMatrix);
        var root = smr.rootBone;
        var rm = root != null ? new Affine(root.localToWorldMatrix) : m;
        var rr = root != null ? Fast.N(root.rotation) : rot;
        var rootPos = root != null ? Fast.N(root.position) : pos;
        var options = new (string name, Func<NV, NV> map, Func<NV, NV> rot)[]
        {
            ("renderer, unscaled", v => pos + NV.Transform(v, rot), v => NV.Transform(v, rot)),
            ("renderer", v => m.Point(v), v => m.Vector(v)),
            ("root bone", v => rm.Point(v), v => rm.Vector(v)),
            ("root bone, unscaled", v => rootPos + NV.Transform(v, rr), v => NV.Transform(v, rr)),
            ("world", v => v, v => v),
        };
        var bones = skeleton.Values.Where(b => b != null).Select(b => Fast.N(b.position)).ToArray();
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
                foreach (var b in bones) d = Math.Min(d, NV.DistanceSquared(b, p));
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
        for (var i = 0; i < _bones.Length; i++) _matrices[i] = new Affine(_bones[i].localToWorldMatrix);
        var min = new NV(float.MaxValue); var max = new NV(float.MinValue);
        for (var i = 0; i < _bone.Length; i++)
        {
            var mm = _matrices[_bone[i]];
            var p = _world[i] = mm.Point(_local[i]);
            _worldN[i] = NV.Normalize(mm.Vector(_localN[i]));
            min = NV.Min(min, p); max = NV.Max(max, p);
        }
        _min = min; _max = max;
    }

    // How far a point (with some thickness of its own) is inside her, and which way is out.
    public bool Depth(NV p, float margin, float reach, out NV push, bool sideways = false)
    {
        // Upper arms only meet her breasts: the crotch search would catch a crouching girl's thighs and wrench her elbow.
        push = NV.Zero;
        var inSurface = !sideways && SurfaceDepth(p, margin, reach, out push);
        foreach (var b in _breasts)
            if (b.Depth(p, margin, sideways, out var bp) && bp.LengthSquared() > push.LengthSquared()) { push = bp; inSurface = true; }
        return inSurface;
    }

    private bool SurfaceDepth(NV p, float margin, float reach, out NV push)
    {
        push = NV.Zero;
        if (_world.Length == 0) return false;
        // Nowhere near the crotch: nothing to search.
        if (p.X < _min.X - reach || p.Y < _min.Y - reach || p.Z < _min.Z - reach ||
            p.X > _max.X + reach || p.Y > _max.Y + reach || p.Z > _max.Z + reach) return false;
        var r2 = reach * reach;
        int n0 = -1, n1 = -1, n2 = -1; float d0 = r2, d1 = r2, d2 = r2;
        for (var i = 0; i < _world.Length; i++)
        {
            var d = NV.DistanceSquared(_world[i], p);
            if (d >= d2) continue;
            if (d < d0) { n2 = n1; d2 = d1; n1 = n0; d1 = d0; n0 = i; d0 = d; }
            else if (d < d1) { n2 = n1; d2 = d1; n1 = i; d1 = d; }
            else { n2 = i; d2 = d; }
        }
        if (n0 < 0) return false;
        NV sp = NV.Zero, sn = NV.Zero; var c = 0;
        if (n0 >= 0) { sp += _world[n0]; sn += _worldN[n0]; c++; }
        if (n1 >= 0) { sp += _world[n1]; sn += _worldN[n1]; c++; }
        if (n2 >= 0) { sp += _world[n2]; sn += _worldN[n2]; c++; }
        sp /= c; sn = Fast.Normalized(sn);
        var depth = margin - NV.Dot(p - sp, sn);
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
    private NV _center;
    private float[] _reach;
    private Affine _toLocal, _toWorld;
    private float _scale;
    private int _count;
    private NV _axis;           // bone space, towards the nipple
    private float _maxReach;

    public static BreastShape Build(Transform bone, List<NV> world, NV forward)
    {
        if (bone == null || world.Count < 30 || forward.LengthSquared() < 1e-10f) return null;
        var toLocal = new Affine(bone.worldToLocalMatrix);
        var pts = world.Select(toLocal.Point).ToList();
        var c = NV.Zero;
        foreach (var q in pts) c += q;
        c /= pts.Count;
        var reach = new float[Rows * Cols];
        foreach (var q in pts)
        {
            var d = q - c; var r = d.Length();
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
            _axis = Fast.Normalized(NV.Transform(forward, Fast.InverseRotation(bone)))
        };
    }

    private static int Bin(NV u)
    {
        var row = Math.Clamp((int)(MathF.Acos(Fast.Clamp(u.Y, -1f, 1f)) / MathF.PI * Rows), 0, Rows - 1);
        var col = Math.Clamp((int)((MathF.Atan2(u.Z, u.X) + MathF.PI) / (2f * MathF.PI) * Cols), 0, Cols - 1);
        return row * Cols + col;
    }

    public string Describe(float k) => $"{_bone.name} {_count} points, up to {_reach.Max() * _bone.lossyScale.x / k * 100f:0} cm from its middle";

    public void Update()
    {
        _toLocal = new Affine(_bone.worldToLocalMatrix);
        _toWorld = new Affine(_bone.localToWorldMatrix);
        _scale = Math.Max(1e-6f, _bone.lossyScale.x);
    }

    private bool Inside(NV local, float margin)
    {
        var d = local - _center;
        var r = d.Length();
        if (r < 1e-6f) return true;
        var reach = _reach[Bin(d / r)];
        return reach > 0f && r < reach + margin;
    }

    // How far a point inside has to move to get out. Hands and forearms come out the front, the way the breast
    // points (going round her side would send them towards her armpit); upper arms come out to the side.
    public bool Depth(NV p, float margin, bool sideways, out NV push)
    {
        push = NV.Zero;
        var q = _toLocal.Point(p);
        var m = margin / _scale;
        // Further from the middle than the breast reaches at all: outside.
        if ((q - _center).Length() >= _maxReach + m) return false;
        if (!Inside(q, m)) return false;
        var dir = _axis;
        if (sideways)
        {
            var side = q - _center - Fast.Project(q - _center, _axis);
            if (side.LengthSquared() > 1e-8f) dir = NV.Normalize(side);
        }
        var step = _maxReach / 24f;
        var t = step;
        for (; t < _maxReach * 2.5f; t += step)
            if (!Inside(q + dir * t, m)) break;
        push = _toWorld.Vector(dir * t);
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
    private NV _handPush, _elbowPush;
    private string _side;
    private float _seenHand, _seenForearm, _seenUpper, _seenApplied, _seenLeft;
    private (NV p, float margin)[] _handPts, _forearmPts, _upperPts;

    public static Hand Find(Dictionary<string, Transform> bones, string side)
    {
        if (!bones.TryGetValue("cf_j_arm00_" + side, out var u) || !bones.TryGetValue("cf_j_forearm01_" + side, out var l)
            || !bones.TryGetValue("cf_j_hand_" + side, out var h)) return null;
        var tips = new List<Transform>();
        foreach (var f in new[] { "index03", "middle03", "ring03" })
            if (bones.TryGetValue($"cf_j_{f}_{side}", out var t)) tips.Add(t);
        bones.TryGetValue("cf_j_middle01_" + side, out var palm);
        return new Hand
        {
            _upper = u, _lower = l, _hand = h, _tips = tips.ToArray(), _palm = palm, _side = side,
            _handPts = new (NV, float)[tips.Count + (palm != null ? 1 : 0)],
            _forearmPts = new (NV, float)[ForearmAt.Length], _upperPts = new (NV, float)[UpperArmAt.Length]
        };
    }

    // Smooth rather than exact: the correction follows slowly (about a third of a second) so the arm glides
    // instead of shaking from frame to frame, and leaves about a centimeter of overlap, however big she is.
    private const float Follow = 2f, RiseRate = 6f, Allowed = 0.004f, MaxElbowPush = 0.06f;
    // How far the wrist may bend to keep the hand as the clip had it while the forearm swings; past this the hand turns with the forearm.
    private const float KeepWristUpTo = 20f, FollowForearmFrom = 50f;
    private static readonly float[] ForearmAt = { 0.2f, 0.45f, 0.7f, 0.9f };
    private static readonly float[] UpperArmAt = { 0.5f, 0.75f, 1f };

    public void KeepOut(BodySurface body, float k, float weight, float dt)
    {
        var reach = 0.16f * k; // deep enough to find the way out of big breasts
        NV shoulder = Fast.N(_upper.position), elbow = Fast.N(_lower.position), wrist = Fast.N(_hand.position);

        // The hand moves as one piece: find how far it has to go for the palm and the fingertips to all be out.
        // Fingertips may press in about a centimeter, as fingers squeezing would.
        var h = 0;
        if (_palm != null) _handPts[h++] = (NV.Lerp(wrist, Fast.N(_palm.position), 0.6f), 0.012f * k);
        foreach (var t in _tips) _handPts[h++] = (Fast.N(t.position), -0.01f * k);
        // Arm thickness around the bone: 3.5 cm at the elbow down to 2.5 cm at the wrist, 4 cm for the upper arm.
        for (var i = 0; i < ForearmAt.Length; i++)
            _forearmPts[i] = (NV.Lerp(elbow, wrist, ForearmAt[i]), (0.035f + (0.025f - 0.035f) * ForearmAt[i]) * k);
        for (var i = 0; i < UpperArmAt.Length; i++)
            _upperPts[i] = (NV.Lerp(shoulder, elbow, UpperArmAt[i]), 0.04f * k);

        var hand = Solve(body, _handPts, reach, out var handDepth);
        var forearm = Solve(body, _forearmPts, reach, out var forearmDepth);
        var upperArm = Solve(body, _upperPts, reach, out var upperDepth, true);
        _seenHand = Math.Max(_seenHand, handDepth / k);
        _seenForearm = Math.Max(_seenForearm, forearmDepth / k);
        _seenUpper = Math.Max(_seenUpper, upperDepth / k);
        hand = Trim(hand, k); forearm = Trim(forearm, k); upperArm = Trim(upperArm, k);
        // A forearm inside her moves the whole forearm: the hand takes whatever part of it its own push lacks.
        if (forearm != NV.Zero)
        {
            var dir = NV.Normalize(forearm);
            hand += dir * Math.Max(0f, forearm.Length() - NV.Dot(hand, dir));
        }
        var elbowPush = forearm + upperArm;
        // An elbow moved much further than this bends the arm out of shape: a little overlap left looks better.
        var elbowLen = elbowPush.Length();
        if (elbowLen > MaxElbowPush * k) elbowPush *= MaxElbowPush * k / elbowLen;

        // Moves out briskly and settles back slowly, so it neither lags inside her nor shakes.
        _handPush = Ease(_handPush, hand, dt);
        _elbowPush = Ease(_elbowPush, elbowPush, dt);
        _seenApplied = Math.Max(_seenApplied, _handPush.Length() * weight / k);
        if (_handPush.LengthSquared() < 1e-8f && _elbowPush.LengthSquared() < 1e-8f) return;
        Reach(wrist + _handPush * weight, elbow + _elbowPush * weight);

        // What is still inside after the move, for the log.
        if (!Plugin.DebugLog.Value) return;
        var left = 0f;
        var moved = Fast.N(_hand.position) - wrist;
        foreach (var (p, m) in _handPts)
            if (body.Depth(p + moved, m, reach, out var push)) left = Math.Max(left, push.Length());
        _seenLeft = Math.Max(_seenLeft, left / k);
    }

    // How far a rigid piece has to move for all its points to be out: a few steps, each pushing out the point
    // that is deepest from where the previous steps left it.
    private static NV Solve(BodySurface body, (NV p, float margin)[] pts, float reach, out float firstDepth, bool sideways = false)
    {
        var total = NV.Zero;
        firstDepth = 0f;
        for (var step = 0; step < 4; step++)
        {
            var best = NV.Zero;
            foreach (var (p, m) in pts)
                if (body.Depth(p + total, m, reach, out var push, sideways) && push.LengthSquared() > best.LengthSquared()) best = push;
            if (step == 0) firstDepth = best.Length();
            if (best.LengthSquared() < 1e-10f) break;
            total += best;
        }
        return total;
    }

    private static NV Ease(NV current, NV target, float dt)
    {
        var rate = target.LengthSquared() > current.LengthSquared() ? RiseRate : Follow;
        return NV.Lerp(current, target, 1f - MathF.Exp(-rate * dt));
    }

    // What the arm ran into since the last report (centimeters), for the log.
    public string Report()
    {
        var r = $"{_side} hand {_seenHand * 100f:0.0}, forearm {_seenForearm * 100f:0.0}, upper arm {_seenUpper * 100f:0.0}, hand moved {_seenApplied * 100f:0.0}, still in {_seenLeft * 100f:0.0}";
        _seenHand = _seenForearm = _seenUpper = _seenApplied = _seenLeft = 0f;
        return r;
    }

    private static NV Trim(NV push, float k)
    {
        var depth = push.Length();
        return depth > Allowed * k ? push * ((depth - Allowed * k) / depth) : NV.Zero;
    }

    // Two-bone reach: the hand to its target with its own rotation kept, the elbow bending towards where it
    // should be (where the clip had it, unless it was pushed out).
    private void Reach(NV target, NV elbowHint)
    {
        var handRot = Fast.N(_hand.rotation);
        var lowerRot = Fast.N(_lower.rotation);
        NV a = Fast.N(_upper.position), b = Fast.N(_lower.position), c = Fast.N(_hand.position);
        float l1 = (b - a).Length(), l2 = (c - b).Length();
        var at = target - a;
        var d = Fast.Clamp(at.Length(), Math.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
        var dir = Fast.Normalized(at);
        var pole = (elbowHint - a) - Fast.Project(elbowHint - a, dir);
        if (pole.LengthSquared() < 1e-8f) pole = (b - a) - Fast.Project(b - a, dir);
        if (pole.LengthSquared() < 1e-8f) return;
        pole = NV.Normalize(pole);
        var cosA = Fast.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
        var elbow = a + (dir * cosA + pole * MathF.Sqrt(1f - cosA * cosA)) * l1;
        _upper.rotation = Fast.U(Fast.FromTo(b - a, elbow - a) * Fast.N(_upper.rotation));
        var b1 = Fast.N(_lower.position);
        var newLower = Fast.FromTo(Fast.N(_hand.position) - b1, a + dir * d - b1) * Fast.N(_lower.rotation);
        _lower.rotation = Fast.U(newLower);
        // Keeping the hand exactly as it was would twist her wrist once the forearm swings far: past a point the
        // hand turns along with the forearm instead.
        var swing = newLower * NQ.Inverse(lowerRot);
        var angle = 2f * MathF.Acos(Math.Min(1f, Math.Abs(swing.W))) * 180f / MathF.PI;
        var follow = Fast.Clamp01((angle - KeepWristUpTo) / (FollowForearmFrom - KeepWristUpTo));
        _hand.rotation = Fast.U(follow > 0f ? Fast.Slerp(handRot, swing * handRot, follow) : handRot);
    }
}
