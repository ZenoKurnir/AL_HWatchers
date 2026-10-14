using Character;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Audio;
using Random = UnityEngine.Random;

namespace AL_HWatchers;

// Watchers aren't H actors, so the H voice system never speaks for them. They get short gasps from their own
// personality's H voice bank, played on their own audio source at the watcher volume, so the girls in the
// scene are never cut off or made quieter. The source goes through the game's voice mixer group, so the
// master and voice volume settings apply to it like to any other voice.
internal static class Voices
{
    private static readonly Dictionary<string, AudioClip> _clips = new();

    // Short gasps: sets 000 and 001 are soft, 002 and 003 stronger (going by the game's own volume table).
    public static void Gasp(Watcher w, bool strong)
    {
        if (Plugin.VoiceVolume.Value <= 0f || w.Human == null) return;
        var p = Personality(w.Human);
        var set = strong ? Random.Range(2, 4) : Random.Range(0, 2);
        var bundle = $"sd/dt/pcm/c{p:00}/h/000_01.unity3d";
        var asset = $"al_hl_{p:00}_00_{set:000}_{Random.Range(0, 10):00}";
        var clip = Load(bundle, asset);
        if (clip == null) return;

        try
        {
            if (w.Voice == null)
            {
                var head = w.Human.Face?.objHeadBone?.transform ?? w.Human.Transform;
                var go = new GameObject("AL_HWatchers voice");
                go.transform.SetParent(head, false);
                w.Voice = go.AddComponent<AudioSource>();
                w.Voice.playOnAwake = false;
                w.Voice.spatialBlend = 0f;
                try { Manager.Voice.RegisterLipSyncTarget(w.Voice, w.Human); } catch { }
            }
            if (w.Voice.isPlaying) return;
            if (w.Voice.outputAudioMixerGroup == null) w.Voice.outputAudioMixerGroup = VoiceGroup();
            w.Voice.clip = clip;
            w.Voice.pitch = VoicePitch(w.Human);
            w.Voice.volume = Plugin.VoiceVolume.Value * GameVoiceVolume(p);
            w.Voice.Play();
        }
        catch (Exception e) { Plugin.Log.LogWarning($"{w.Name}: voice failed: {e.Message}"); }
    }

    private static AudioMixerGroup _group;
    private static float _nextGroupSearch;

    // The mixer group the game's own voices play through (found on its voice setting templates), or failing
    // that the mixer's voice or master group.
    private static AudioMixerGroup VoiceGroup()
    {
        if (_group != null) return _group;
        if (Time.realtimeSinceStartup < _nextGroupSearch) return null;
        _nextGroupSearch = Time.realtimeSinceStartup + 10f;
        string from = null;
        try
        {
            var root = Manager.Voice.RootSetting;
            if (root != null)
                foreach (var src in root.GetComponentsInChildren<AudioSource>(true))
                    if (src.outputAudioMixerGroup != null) { _group = src.outputAudioMixerGroup; from = "voice settings"; break; }
        }
        catch { }
        if (_group == null) _group = FindGroup(SafeMixer(true), "Voice", ref from) ?? FindGroup(SafeMixer(false), "Voice", ref from)
                                     ?? FindGroup(SafeMixer(true), "Master", ref from) ?? FindGroup(SafeMixer(false), "Master", ref from);
        if (_group != null) Plugin.Debug($"Watcher voices play through the \"{_group.name}\" mixer group ({from}).");
        else Plugin.Log.LogWarning("Found no voice mixer group; watcher voices won't follow the master volume.");
        return _group;
    }

    private static AudioMixer SafeMixer(bool voice)
    {
        try { return voice ? Manager.Voice.Mixer : Manager.Sound.Mixer; } catch { return null; }
    }

    private static AudioMixerGroup FindGroup(AudioMixer mixer, string name, ref string from)
    {
        if (mixer == null) return null;
        try
        {
            var groups = mixer.FindMatchingGroups(name);
            if (groups == null || groups.Length == 0) return null;
            if (Plugin.DebugLog.Value)
            {
                var names = new List<string>();
                foreach (var g in mixer.FindMatchingGroups("")) names.Add(g.name);
                Plugin.Debug($"Mixer {mixer.name} groups: {string.Join(", ", names)}");
            }
            from = $"{mixer.name} mixer";
            foreach (var g in groups) if (g.name == name) return g;
            return groups[0];
        }
        catch { return null; }
    }

    private static AudioClip Load(string bundle, string asset)
    {
        var key = bundle + "|" + asset;
        if (_clips.TryGetValue(key, out var cached) && cached != null) return cached;
        try
        {
            var obj = AssetBundleManager.LoadAssetSimple(Il2CppType.Of<AudioClip>(), bundle, asset, null);
            var clip = obj?.TryCast<AudioClip>();
            if (clip != null) _clips[key] = clip;
            else Plugin.Debug($"Voice {asset} not found in {bundle}.");
            return clip;
        }
        catch (Exception e)
        {
            if (Plugin.DebugLog.Value) Plugin.Log.LogWarning($"Voice {asset} failed: {e.Message}");
            return null;
        }
    }

    // The game's own volume for this personality's voice (the sound settings), so the watcher slider is relative to it.
    private static float GameVoiceVolume(int personality)
    {
        try { var v = Manager.Voice.GetVolume(personality); return v > 0f && v <= 1f ? v : 1f; } catch { return 1f; }
    }

    private static float VoicePitch(Human h)
    {
        try { var p = h.Data.Parameter.voicePitch; return p > 0.5f && p < 2f ? p : 1f; } catch { return 1f; }
    }

    private static int Personality(Human h)
    {
        try { var p = h.Data.Parameter.personality; return p is >= 0 and <= 11 ? p : 0; } catch { return 0; }
    }
}
