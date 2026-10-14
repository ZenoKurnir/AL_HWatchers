using Character;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace AL_HWatchers;

// Ctrl + right-click (without dragging) on a watcher opens her menu on the right side of the screen.
// A girl with 0 hearts only watches: her menu shows, but every option is locked.
public class WatcherMenu : MonoBehaviour
{
    public WatcherMenu(IntPtr pointer) : base(pointer) { }

    private static readonly GUILayoutOption[] None = Array.Empty<GUILayoutOption>();
    private static readonly string[] PartNames = { "Top", "Bottom", "Bra", "Panties", "Gloves", "Pantyhose", "Socks", "Shoes", "Arm extra", "Leg extra", "Other extra" };
    private static readonly string[] StateNames = { "on", "half", "off" };
    private static readonly string[] OutfitNames = { "Swimsuit", "After bath" };

    private static Vector2 _scroll;
    private Vector3 _downAt;
    private bool _down;

    public static void Create()
    {
        ClassInjector.RegisterTypeInIl2Cpp<WatcherMenu>();
        var go = new GameObject("AL_HWatchers menu");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<WatcherMenu>();
    }

    private void LateUpdate() => Watchers.Animate(Time.deltaTime);

    private void Update()
    {
        TokLibrary.ImportIfNeeded();
        try { Watchers.WatchEnd(); Watchers.WatchSlots(); } catch { }
        try
        {
            // Ctrl must be held, so turning the camera with the right button can't open a menu by accident.
            var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (Input.GetMouseButtonDown(1) && ctrl) { _downAt = Input.mousePosition; _down = true; }
            if (Input.GetMouseButtonUp(1) && !ctrl) _down = false;
            if (Input.GetMouseButtonUp(1) && _down && ctrl)
            {
                _down = false;
                // A right-drag turns the camera; only a plain click picks a girl.
                if ((Input.mousePosition - _downAt).magnitude < 6f) Watchers.PickAt(Input.mousePosition);
            }
        }
        catch { }
    }

    private void OnGUI()
    {
        var w = Watchers.Selected;
        if (w?.Human == null) return;
        try { Draw(w); }
        catch (Exception e) { Plugin.Log.LogWarning($"Menu failed: {e.Message}"); Watchers.Selected = null; }
    }

    private static void Draw(Watcher w)
    {
        const float width = 270f;
        // It ends well above the bottom right corner, where the game's move tool opens; long menus scroll.
        var height = Mathf.Max(300f, Screen.height * 0.58f - 120f);
        GUILayout.BeginArea(new Rect(Screen.width - width - 20f, 120f, width, height), GUI.skin.box);
        _scroll = GUILayout.BeginScrollView(_scroll, None);
        GUILayout.Label($"<b>{w.Name}</b>{(w.SecondGirl ? "  (second girl)" : "")}", None);
        GUILayout.Label(w.Hearts >= 0 ? $"Hearts: {w.Hearts}" : "Hearts: ?", None);
        if (w.Locked) GUILayout.Label("She only watches: no options with 0 hearts.", None);
        if (w.Tired) GUILayout.Label("She's worn out from H and stays lying down.", None);
        var moving = Moving.Who;
        GUILayout.Label(moving != null && (moving == w || moving == w.Lover)
            ? "<i>The move tool (bottom right) now moves her.</i>"
            : w.Gathered ? "" : "<i>Open the move tool (bottom right) to move her.</i>", None);

        GUI.enabled = !w.Locked;

        var busy = w.Lover != null || w.Gathered;
        var outfit = Watchers.Outfit(w);
        if (outfit >= 0 && !busy && GUILayout.Button($"Outfit: {OutfitNames[outfit % 2]}  (switch)", None))
            Watchers.SetOutfit(w, 1 - outfit);

        GUILayout.BeginHorizontal(None);
        if (GUILayout.Button("Dressed", None)) Watchers.Redress(w, HumanCloth.Define.ClothesState.Clothing);
        if (GUILayout.Button("Half", None)) Watchers.Redress(w, HumanCloth.Define.ClothesState.HalfUndress);
        if (GUILayout.Button("Naked", None)) Watchers.Redress(w, HumanCloth.Define.ClothesState.Naked);
        GUILayout.EndHorizontal();

        var cloth = w.Human.Cloth;
        if (cloth != null)
            foreach (HumanCloth.Define.ClothesKind kind in Enum.GetValues(typeof(HumanCloth.Define.ClothesKind)))
            {
                bool exists;
                try { exists = cloth.IsClothesStateKind(kind) && cloth.IsExist(kind); } catch { exists = false; }
                if (!exists) continue;
                var state = (int)cloth.GetClothesStateType(kind);
                if (GUILayout.Button($"{PartNames[(int)kind]}: {StateNames[state % 3]}", None)) Watchers.CyclePart(w, kind);
            }

        if (Plugin.GirlPairs.Value && !w.Gathered)
        {
            GUILayout.Space(6f);
            if (w.Lover != null && GUILayout.Button($"Stop making love with {w.Lover.Name}", None)) Watchers.EndLove(w);
            var choices = Watchers.LoveChoices(w);
            if (choices.Count > 0) GUILayout.Label(w.Lover != null ? "Switch to:" : "Make love with:", None);
            foreach (var other in choices)
            {
                GUILayout.BeginHorizontal(None);
                var mark = Watchers.IsCouple(w, other) ? " (partner)" : "";
                GUILayout.Label($"{other.Name}{mark}", None);
                if (GUILayout.Button("Scissoring", None)) Watchers.SwitchLove(w, other, "00");
                if (GUILayout.Button("69", None)) Watchers.SwitchLove(w, other, "01");
                GUILayout.EndHorizontal();
            }
        }

        if (TokLibrary.Available && !busy && !w.Tired)
        {
            GUILayout.Space(6f);
            if (w.Tok == null)
            {
                GUILayout.BeginHorizontal(None);
                if (GUILayout.Button("Touch herself (standing)", None)) Watchers.StartTouching(w, "standing");
                if (GUILayout.Button("(crouching)", None)) Watchers.StartTouching(w, "crouching");
                GUILayout.EndHorizontal();
            }
            else if (GUILayout.Button("Stop touching herself", None)) Watchers.StopTouching(w);
        }

        if (Watchers.CanSwap(w) && !w.Locked)
        {
            var targets = Watchers.SwapTargets();
            if (targets.Count > 0)
            {
                GUILayout.Space(6f);
                foreach (var (index, name, active) in targets)
                {
                    if (active && GUILayout.Button($"Swap with {name}", None)) { Watchers.SwapIn(w, index); break; }
                    if (!active && !w.SecondGirl && GUILayout.Button("Make her the 3P girl", None)) { Watchers.SwapIn(w, index); break; }
                }
            }
        }

        if (w.Gathered && GUILayout.Button("Stand up", None)) Watchers.StandUp(w);

        GUILayout.Space(6f);
        if (GUILayout.Button("Send her back", None)) Watchers.Dismiss(w);

        GUI.enabled = true;
        GUILayout.EndScrollView();
        if (GUILayout.Button("Close", None)) Watchers.Selected = null;
        GUILayout.EndArea();
    }
}
