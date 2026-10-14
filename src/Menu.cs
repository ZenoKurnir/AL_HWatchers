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

    private static float _scroll;       // how far the menu is scrolled down, in pixels
    private static bool _blocked;
    private static Rect _box;
    private static float _contentHeight; // measured on the last repaint; 0 until known
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
            // The mouse wheel over the menu scrolls it.
            if (Watchers.Selected != null && _box.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
                _scroll -= Input.mouseScrollDelta.y * 40f;
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
        var areas = 0;
        try { Draw(w, ref areas); }
        catch (Exception e) { Plugin.Log.LogWarning($"Menu failed: {e.Message}"); Watchers.Selected = null; }
        finally
        {
            // Close any area still open after an error, so Unity's GUI clips stay balanced.
            for (; areas > 0; areas--) try { GUILayout.EndArea(); } catch { }
        }
    }

    // Scrolling is done by hand (an inner area moved up inside the outer one, driven by the mouse wheel):
    // GUILayout.BeginScrollView is stripped from the game and can't be restored on every BepInEx build.
    private static void Draw(Watcher w, ref int areas)
    {
        const float width = 270f, closeHeight = 30f, pad = 6f;
        // It ends well above the bottom right corner, where the game's move tool opens; long menus scroll.
        var height = Mathf.Max(300f, Screen.height * 0.58f - 120f);
        var box = new Rect(Screen.width - width - 20f, 120f, width, height);
        GUI.Box(box, "");
        var view = height - closeHeight - pad * 3f;
        var max = Mathf.Max(0f, _contentHeight - view);
        var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        _box = box;
        _scroll = Mathf.Clamp(_scroll, 0f, max);

        var viewRect = new Rect(box.x + pad, box.y + pad, width - pad * 2f, view);
        // Buttons scrolled out of sight must not catch clicks meant for the Close button or the game.
        var e = Event.current.type;
        _blocked = (e == EventType.MouseDown || e == EventType.MouseUp) && !viewRect.Contains(mouse);
        GUI.enabled = !_blocked;
        GUILayout.BeginArea(viewRect); areas++;
        GUILayout.BeginArea(new Rect(0f, -_scroll, width - pad * 2f - (max > 0f ? 8f : 0f), Mathf.Max(view, _contentHeight) + 2000f)); areas++;
        GUILayout.Label($"<b>{w.Name}</b>{(w.SecondGirl ? "  (second girl)" : "")}", None);
        GUILayout.Label(w.Hearts >= 0 ? $"Hearts: {w.Hearts}" : "Hearts: ?", None);
        if (w.Locked) GUILayout.Label("She only watches: no options with 0 hearts.", None);
        if (w.Tired) GUILayout.Label("She's worn out from H and stays lying down.", None);
        var moving = Moving.Who;
        GUILayout.Label(moving != null && (moving == w || moving == w.Lover)
            ? "<i>The move tool (bottom right) now moves her.</i>"
            : w.Gathered ? "" : "<i>Open the move tool (bottom right) to move her.</i>", None);

        GUI.enabled = !w.Locked && !_blocked;

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
        // Where the content ends, measured on repaint, sets how far it can scroll.
        // (GetRect, not GetLastRect: that one is stripped from the game too.)
        var end = GUILayoutUtility.GetRect(0f, 0f, GUI.skin.label, None);
        if (Event.current.type == EventType.Repaint) _contentHeight = end.y;
        GUILayout.EndArea(); areas--;
        GUILayout.EndArea(); areas--;
        if (max > 0f)
        {
            // A thin bar showing where in the menu she is.
            var track = new Rect(box.xMax - pad - 6f, box.y + pad, 4f, view);
            var thumb = Mathf.Max(20f, view * view / _contentHeight);
            GUI.Box(new Rect(track.x, track.y + (view - thumb) * (_scroll / max), track.width, thumb), "");
        }
        if (GUI.Button(new Rect(box.x + pad, box.yMax - pad - closeHeight, width - pad * 2f, closeHeight), "Close")) Watchers.Selected = null;
    }
}
