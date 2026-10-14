using AL.H;
using HarmonyLib;
using Character;

namespace AL_HWatchers;

// Never patch a method that returns UniTask (HScene.OnEnd, HScene.End(CancellationToken)): the patch closes
// the game to desktop when H ends. Only Update and Dispose are hooked.
internal static class Hooks
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(HScene), nameof(HScene.Update))]
    public static void HScene_Post_Update(HScene __instance)
    {
        try { Watchers.Update(__instance); }
        catch (Exception e) { Plugin.Log.LogError($"Watcher update failed: {e}"); }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(HScene), nameof(HScene.Dispose))]
    public static void HScene_Pre_Dispose()
    {
        try { Watchers.Clear("H ended"); Watchers.EndScene(); Solo.End(); }
        catch (Exception e) { Plugin.Log.LogError($"Watcher cleanup failed: {e}"); }
    }
}

// After a swap, the H clothes menu is redrawn from her current clothes once a button is pressed, so its marks
// match the girl now in the slot. Patched on its own so a miss can't stop the main hooks.
internal static class MenuHooks
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(AL.H.UI.ClothesSettingMenu.SelectClothesState), "_Initialize_b__17_1")]
    public static void ClothesMenu_Post_Click(AL.H.UI.ClothesSettingMenu.SelectClothesState __instance)
    {
        try { if (Watchers.AnySwapped) __instance.UpdateUI(); }
        catch { }
    }
}
