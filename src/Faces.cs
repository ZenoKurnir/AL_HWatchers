using Character;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AL_HWatchers;

internal enum Mood { None, Shy, Curious, Aroused, Hot, Climax, Afterglow, Eager, Hit, Tired }

// Facial expressions by mood. Each mood has a few faces (brow, eyes, mouth pattern, blush, and how far the eyes
// and mouth are held open), and a girl switches between them every few seconds so she never freezes on one.
// Pattern numbers are the game's own (com/lis/exp, the same for every personality): mouth 8 is open, 26 dreamy,
// 27 melting, 2 smiling; eyes 1 closed, 4 happy-closed (no heart eyes).
internal static class Faces
{
    private readonly struct Face
    {
        public readonly int Brow, Eyes, Mouth;
        public readonly float Blush, EyesOpen, MouthOpen; // openness < 0 leaves it to blinking / lip sync
        public Face(int brow, int eyes, int mouth, float blush, float eyesOpen = -1f, float mouthOpen = -1f)
        { Brow = brow; Eyes = eyes; Mouth = mouth; Blush = blush; EyesOpen = eyesOpen; MouthOpen = mouthOpen; }
    }

    private static readonly Dictionary<Mood, Face[]> Pools = new()
    {
        [Mood.Shy] = new Face[]
        {
            new(5, 2, 23, 0.5f), new(5, 2, 23, 0.7f), new(8, 6, 23, 0.45f), new(8, 6, 19, 0.45f), new(1, 0, 19, 0.4f)
        },
        [Mood.Curious] = new Face[]
        {
            new(8, 0, 26, 0.6f, 0.75f), new(4, 0, 23, 0.55f), new(8, 3, 19, 0.55f), new(2, 0, 8, 0.6f, -1f, 0.3f),
            new(5, 2, 8, 0.7f, -1f, 0.25f)
        },
        [Mood.Aroused] = new Face[]
        {
            new(8, 0, 26, 0.75f, 0.65f, 0.3f), new(8, 0, 27, 0.75f, 0.6f, 0.35f), new(5, 2, 8, 0.85f, -1f, 0.45f),
            new(8, 6, 8, 0.8f, -1f, 0.5f), new(8, 2, 27, 0.8f, -1f, 0.35f)
        },
        [Mood.Hot] = new Face[]
        {
            new(8, 0, 27, 0.95f, 0.45f, 0.6f), new(8, 0, 27, 0.95f, 0.4f, 0.55f), new(8, 6, 8, 0.95f, -1f, 0.75f),
            new(8, 1, 8, 1f, -1f, 0.65f), new(8, 0, 8, 1f, 0.35f, 0.8f)
        },
        [Mood.Climax] = new Face[]
        {
            new(8, 1, 8, 1f, -1f, 0.95f), new(2, 0, 8, 1f, 0.3f, 1f), new(8, 1, 27, 1f, -1f, 0.85f)
        },
        [Mood.Afterglow] = new Face[]
        {
            new(2, 4, 2, 0.9f, -1f, 0.3f), new(8, 0, 26, 0.9f, 0.5f, 0.3f), new(8, 0, 27, 0.9f, 0.4f, 0.4f),
            new(8, 1, 27, 0.9f, -1f, 0.3f)
        },
        [Mood.Eager] = new Face[]
        {
            new(2, 3, 2, 0.8f, -1f, 0.65f), new(8, 0, 27, 0.9f, 0.6f, 0.7f), new(8, 0, 26, 0.85f, 0.7f, 0.7f),
            new(2, 0, 8, 0.85f, -1f, 0.8f)
        },
        [Mood.Tired] = new Face[]
        {
            new(8, 0, 8, 0.8f, 0.25f, 0.25f), new(8, 1, 8, 0.75f, -1f, 0.2f), new(2, 0, 26, 0.7f, 0.3f, 0.15f),
            new(8, 1, 27, 0.75f, -1f, 0.3f)
        },
        [Mood.Hit] = new Face[]
        {
            new(2, 0, 8, 1f, -1f, 0.8f), new(8, 1, 8, 1f, -1f, 0.6f), new(2, 4, 2, 1f, -1f, 0.5f)
        },
    };

    public static void Set(Watcher w, Mood mood)
    {
        if (w?.Human == null || mood == Mood.None) return;
        w.Mood = mood;
        var pool = Pools[mood];
        var face = pool[Random.Range(0, pool.Length)];
        w.NextFaceAt = Time.realtimeSinceStartup + Random.Range(4f, 9f);
        try
        {
            var f = w.Human.Face;
            f.ChangeEyebrowPtn(face.Brow, true);
            f.ChangeEyesPtn(face.Eyes, true);
            f.ChangeMouthPtn(face.Mouth, true);
            f.ChangeHohoAkaRate(new Il2CppSystem.Nullable<float>(Mathf.Clamp01(face.Blush)));
            var eyes = f.eyesCtrl;
            if (eyes != null)
            {
                f.ChangeEyesOpenMax(1f);
                eyes.SetFixedRate(face.EyesOpen >= 0f ? face.EyesOpen : -0.1f);
                f.ChangeEyesBlinkFlag(face.EyesOpen < 0f);
            }
            var mouth = f.mouthCtrl;
            if (mouth != null)
            {
                f.ChangeMouthOpenMin(0f);
                f.ChangeMouthOpenMax(1f);
                mouth.SetFixedRate(face.MouthOpen >= 0f ? face.MouthOpen : -0.1f);
            }
        }
        catch { }
    }

    // Every few seconds she changes to another face of the same mood.
    public static void Tick(Watcher w, float now)
    {
        if (w.Mood != Mood.None && now >= w.NextFaceAt) Set(w, w.Mood);
    }
}
