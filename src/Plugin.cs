using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace AL_HWatchers;

// Girls from the current map gather around the H scene and watch, reacting as it goes on.
[BepInProcess("AmanatsuLocation")]
[BepInPlugin(GUID, "AL_HWatchers", Version)]
public class Plugin : BasePlugin
{
    public const string GUID = "AL_HWatchers";
    public const string Version = "1.0.1";

    internal static new ManualLogSource Log;
    internal static ConfigEntry<bool> AutoBring;
    internal static ConfigEntry<int> MaxWatchers;
    internal static ConfigEntry<float> Distance;
    internal static ConfigEntry<KeyCode> WatcherKey;
    internal static ConfigEntry<float> ExcitementSpeed;
    internal static ConfigEntry<bool> DebugLog;
    internal static ConfigEntry<float> VoiceVolume;
    internal static ConfigEntry<bool> Masturbate;
    internal static ConfigEntry<string> AicomiFolder;
    internal static ConfigEntry<bool> GirlPairs;
    internal static ConfigEntry<bool> Bukkake;
    internal static ConfigEntry<bool> Swaps;
    internal static ConfigEntry<bool> SoloSecondGirl;

    internal static void Debug(string message)
    {
        if (DebugLog != null && DebugLog.Value) Log.LogInfo(message);
    }

    public override void Load()
    {
        Log = base.Log;
        AutoBring = Config.Bind("Watchers", "Bring Watchers Automatically", true,
            "When H starts, girls from the current map come to watch (the second girl first).");
        MaxWatchers = Config.Bind("Watchers", "Maximum Watchers", 3,
            new ConfigDescription("How many girls can watch at once. The second girl's spot counts even while she is in a 3P scene.",
                new AcceptableValueRange<int>(1, 5)));
        Distance = Config.Bind("Watchers", "Distance (m)", 1.8f,
            new ConfigDescription("How far from the couple the watchers stand, in meters.", new AcceptableValueRange<float>(0.8f, 5f)));
        WatcherKey = Config.Bind("Keys", "Watcher Key", KeyCode.F9,
            "Bring one more watcher. Shift + this key sends all of them back.");
        ExcitementSpeed = Config.Bind("Watchers", "Excitement Speed", 1f,
            new ConfigDescription("How quickly watchers get worked up as the scene goes on.", new AcceptableValueRange<float>(0.25f, 4f)));
        VoiceVolume = Config.Bind("Watchers", "Voice Volume", 0.4f,
            new ConfigDescription("Watcher voice volume, relative to the game's voice volume. 0 silences them; the girls in the scene are not affected.",
                new AcceptableValueRange<float>(0f, 1f)));
        Masturbate = Config.Bind("Watchers", "Masturbation", true,
            "Worked-up watchers touch themselves. The animations are taken from your own Aicomi install the first time the game starts; without Aicomi they only watch. Never for 0-heart girls.");
        AicomiFolder = Config.Bind("Watchers", "Aicomi Folder", "",
            "Where Aicomi is installed (the folder that holds its 'lib' folder). Leave empty to look for it automatically. " +
            "The converted animations are kept in BepInEx/cache/AL_HWatchers_aicomi.bin; delete that file to convert again.");
        GirlPairs = Config.Bind("Watchers", "Couples Make Love", true,
            "Two watchers who are a linked couple make love with each other once both are worked up (scissoring or 69). Never for 0-heart girls.");
        Bukkake = Config.Bind("Watchers", "Join the Bukkake", true,
            "When he finishes over the girl, the watchers kneel beside her and get covered too. Couples making love and 0-heart girls stay where they are.");
        Swaps = Config.Bind("Watchers", "Swap Girls", true,
            "A watcher's menu can swap her with a girl in the scene, or make her the second girl who joins in 3P. The game still counts the H for the girl the scene started with. Never for 0-heart girls.");
        SoloSecondGirl = Config.Bind("Watchers", "Second Girl in Solo H", true,
            "When H starts with a girl who has no partner, another girl from the map joins as the second girl so 3P is available. Turn off if H fails to start.");
        DebugLog = Config.Bind("Debug", "Detailed Log", false, "Write every pose change and scene event to the BepInEx log.");
        Harmony.CreateAndPatchAll(typeof(Hooks), GUID);
        try { Harmony.CreateAndPatchAll(typeof(MenuHooks), GUID + ".menu"); }
        catch (Exception e) { Log.LogWarning($"Clothes menu redraw not hooked: {e.Message}"); }
        try { Harmony.CreateAndPatchAll(typeof(SoloHooks), GUID + ".solo"); }
        catch (Exception e) { Log.LogWarning($"Solo H second girl not hooked: {e.Message}"); }
        WatcherMenu.Create();
        TokLibrary.Prepare();
        Log.LogInfo($"AL_HWatchers {Version} loaded. {WatcherKey.Value} brings a watcher, Shift+{WatcherKey.Value} sends them back, Ctrl + right-click a watcher for her menu.");
    }
}
