using System.Text;
using BepInEx;
using Il2CppInterop.Runtime;
using Microsoft.Win32;
using UnityEngine;

namespace AL_HWatchers;

// The masturbation poses come from the player's own Aicomi install: Aicomi's bundle is opened with this game's
// Unity, its clips are sampled onto a stand-in skeleton, and the result is saved in BepInEx/cache. Nothing from
// Aicomi ships with the mod; without Aicomi the watchers simply don't masturbate.
internal static class AicomiImport
{
    public const string BundlePath = "lib/h/animator/female/000_01/tok.unity3d";
    private const float Fps = 30f;
    private static readonly (string controller, string set)[] Sets = { ("tok_f_00", "lying"), ("tok_f_01", "crouching"), ("tok_f_03", "standing") };
    private static readonly string[] States = { "Idle", "WLoop", "MLoop", "SLoop", "OLoop", "Orgasm", "Orgasm_A" };
    // Bones the physics, the penis and the tongue drive on their own.
    private static readonly string[] Skip = { "cf_j_dan", "cf_j_tang", "cf_j_bust", "cf_j_bnip", "cf_j_siri", "cf_j_kokan", "cf_j_ana" };

    // Where the clips' bones sit in Aicomi's skeleton (relative to its animator).
    private static readonly string[] BonePaths =
    {
        "cf_j_root",
        "cf_j_root/cf_n_height/cf_j_hips",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_d_bust00/cf_s_bust00_L/cf_t_bust01_L/cf_j_bust01_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_d_bust00/cf_s_bust00_L/cf_t_bust01_L/cf_j_bust01_L/cf_t_bust02_L/cf_j_bust02_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_d_bust00/cf_s_bust00_L/cf_t_bust01_L/cf_j_bust01_L/cf_t_bust02_L/cf_j_bust02_L/cf_t_bust03_L/cf_j_bust03_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_d_bust00/cf_s_bust00_L/cf_t_bust01_L/cf_j_bust01_L/cf_t_bust02_L/cf_j_bust02_L/cf_t_bust03_L/cf_j_bust03_L/cf_t_bnip02_root_L/cf_j_bnip02_root_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_d_bust00/cf_s_bust00_L/cf_t_bust01_L/cf_j_bust01_L/cf_t_bust02_L/cf_j_bust02_L/cf_t_bust03_L/cf_j_bust03_L/cf_t_bnip02_root_L/cf_j_bnip02_root_L/cf_t_bnip02_L/cf_j_bnip02_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_d_bust00/cf_s_bust00_R/cf_t_bust01_R/cf_j_bust01_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_d_bust00/cf_s_bust00_R/cf_t_bust01_R/cf_j_bust01_R/cf_t_bust02_R/cf_j_bust02_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_d_bust00/cf_s_bust00_R/cf_t_bust01_R/cf_j_bust01_R/cf_t_bust02_R/cf_j_bust02_R/cf_t_bust03_R/cf_j_bust03_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_d_bust00/cf_s_bust00_R/cf_t_bust01_R/cf_j_bust01_R/cf_t_bust02_R/cf_j_bust02_R/cf_t_bust03_R/cf_j_bust03_R/cf_t_bnip02_root_R/cf_j_bnip02_root_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_d_bust00/cf_s_bust00_R/cf_t_bust01_R/cf_j_bust01_R/cf_t_bust02_R/cf_j_bust02_R/cf_t_bust03_R/cf_j_bust03_R/cf_t_bnip02_root_R/cf_j_bnip02_root_R/cf_t_bnip02_R/cf_j_bnip02_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_neck",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_neck/cf_j_head",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_neck/cf_j_head/cf_s_head/cf_j_tang_01",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_neck/cf_j_head/cf_s_head/cf_j_tang_01/cf_j_tang_02",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_neck/cf_j_head/cf_s_head/cf_j_tang_01/cf_j_tang_02/cf_j_tang_03",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_neck/cf_j_head/cf_s_head/cf_j_tang_01/cf_j_tang_02/cf_j_tang_03/cf_j_tang_04",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_neck/cf_j_head/cf_s_head/cf_j_tang_01/cf_j_tang_02/cf_j_tang_03/cf_j_tang_04/cf_j_tang_05",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_neck/cf_j_head/cf_s_head/cf_j_tang_01/cf_j_tang_02/cf_j_tang_03/cf_j_tang_04/cf_j_tang_05/cf_j_tang_L_05",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_neck/cf_j_head/cf_s_head/cf_j_tang_01/cf_j_tang_02/cf_j_tang_03/cf_j_tang_04/cf_j_tang_05/cf_j_tang_R_05",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_neck/cf_j_head/cf_s_head/cf_j_tang_01/cf_j_tang_02/cf_j_tang_03/cf_j_tang_04/cf_j_tang_L_04",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_neck/cf_j_head/cf_s_head/cf_j_tang_01/cf_j_tang_02/cf_j_tang_03/cf_j_tang_04/cf_j_tang_R_04",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_neck/cf_j_head/cf_s_head/cf_j_tang_01/cf_j_tang_02/cf_j_tang_03/cf_j_tang_L_03",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_neck/cf_j_head/cf_s_head/cf_j_tang_01/cf_j_tang_02/cf_j_tang_03/cf_j_tang_R_03",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L/cf_s_hand_sub_L/cf_j_index01_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L/cf_s_hand_sub_L/cf_j_index01_L/cf_j_index02_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L/cf_s_hand_sub_L/cf_j_index01_L/cf_j_index02_L/cf_j_index03_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L/cf_s_hand_sub_L/cf_j_little01_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L/cf_s_hand_sub_L/cf_j_little01_L/cf_j_little02_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L/cf_s_hand_sub_L/cf_j_little01_L/cf_j_little02_L/cf_j_little03_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L/cf_s_hand_sub_L/cf_j_middle01_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L/cf_s_hand_sub_L/cf_j_middle01_L/cf_j_middle02_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L/cf_s_hand_sub_L/cf_j_middle01_L/cf_j_middle02_L/cf_j_middle03_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L/cf_s_hand_sub_L/cf_j_ring01_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L/cf_s_hand_sub_L/cf_j_ring01_L/cf_j_ring02_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L/cf_s_hand_sub_L/cf_j_ring01_L/cf_j_ring02_L/cf_j_ring03_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L/cf_s_hand_sub_L/cf_j_thumb01_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L/cf_s_hand_sub_L/cf_j_thumb01_L/cf_j_thumb02_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_arm00_L/cf_j_forearm01_L/cf_j_hand_L/cf_s_hand_sub_L/cf_j_thumb01_L/cf_j_thumb02_L/cf_j_thumb03_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_L/cf_j_shoulder02_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R/cf_s_hand_sub_R/cf_j_index01_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R/cf_s_hand_sub_R/cf_j_index01_R/cf_j_index02_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R/cf_s_hand_sub_R/cf_j_index01_R/cf_j_index02_R/cf_j_index03_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R/cf_s_hand_sub_R/cf_j_little01_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R/cf_s_hand_sub_R/cf_j_little01_R/cf_j_little02_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R/cf_s_hand_sub_R/cf_j_little01_R/cf_j_little02_R/cf_j_little03_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R/cf_s_hand_sub_R/cf_j_middle01_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R/cf_s_hand_sub_R/cf_j_middle01_R/cf_j_middle02_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R/cf_s_hand_sub_R/cf_j_middle01_R/cf_j_middle02_R/cf_j_middle03_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R/cf_s_hand_sub_R/cf_j_ring01_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R/cf_s_hand_sub_R/cf_j_ring01_R/cf_j_ring02_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R/cf_s_hand_sub_R/cf_j_ring01_R/cf_j_ring02_R/cf_j_ring03_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R/cf_s_hand_sub_R/cf_j_thumb01_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R/cf_s_hand_sub_R/cf_j_thumb01_R/cf_j_thumb02_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_arm00_R/cf_j_forearm01_R/cf_j_hand_R/cf_s_hand_sub_R/cf_j_thumb01_R/cf_j_thumb02_R/cf_j_thumb03_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_shoulder_R/cf_j_shoulder02_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_d_ana/cf_j_ana",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_d_siri_L/cf_n_siri01_L/cf_j_siri_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_d_siri_R/cf_n_siri01_R/cf_j_siri_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_j_thigh00_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_j_thigh00_L/cf_j_leg01_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_j_thigh00_L/cf_j_leg01_L/cf_j_leg03_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_j_thigh00_L/cf_j_leg01_L/cf_j_leg03_L/cf_j_foot_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_j_thigh00_L/cf_j_leg01_L/cf_j_leg03_L/cf_j_foot_L/cf_j_toes_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_j_thigh00_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_j_thigh00_R/cf_j_leg01_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_j_thigh00_R/cf_j_leg01_R/cf_j_leg03_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_j_thigh00_R/cf_j_leg01_R/cf_j_leg03_R/cf_j_foot_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_j_thigh00_R/cf_j_leg01_R/cf_j_leg03_R/cf_j_foot_R/cf_j_toes_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_s_kokan/cf_j_dan_top",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_s_kokan/cf_j_dan_top/cf_j_dan100_00",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_s_kokan/cf_j_dan_top/cf_j_dan100_00/cf_j_dan101_00",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_s_kokan/cf_j_dan_top/cf_j_dan100_00/cf_j_dan101_00/cf_j_dan109_00",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_s_kokan/cf_j_dan_top/cf_j_dan_f_top",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_s_kokan/cf_j_dan_top/cf_j_dan_f_top/cf_j_dan_f_L",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_s_kokan/cf_j_dan_top/cf_j_dan_f_top/cf_j_dan_f_R",
        "cf_j_root/cf_n_height/cf_j_hips/cf_j_waist01/cf_j_waist02/cf_s_kokan/cf_j_kokan"
    };

    public static string OutputPath => Path.Combine(Paths.CachePath, "AL_HWatchers_aicomi.bin");

    // ---------- finding Aicomi ----------

    public static string FindAicomi()
    {
        var configured = Plugin.AicomiFolder.Value?.Trim().Trim('"');
        if (!string.IsNullOrEmpty(configured))
        {
            if (HasBundle(configured)) return configured;
            Plugin.Log.LogWarning($"The Aicomi folder in the config ({configured}) has no {BundlePath}; looking elsewhere.");
        }
        foreach (var dir in RegistryCandidates().Concat(NearbyCandidates()))
            if (HasBundle(dir)) return dir;
        return null;
    }

    private static bool HasBundle(string dir)
    {
        try { return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, BundlePath)); } catch { return false; }
    }

    // Installers usually leave the install folder in the registry; the game's own settings key may hold it too.
    private static IEnumerable<string> RegistryCandidates()
    {
        var found = new List<string>();
        if (!OperatingSystem.IsWindows()) return found;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            foreach (var key in new[] { @"Software\ILLGAMES\Aicomi", @"Software\illgames\Aicomi", @"Software\WOW6432Node\ILLGAMES\Aicomi" })
                try
                {
                    using var k = hive.OpenSubKey(key);
                    if (k == null) continue;
                    foreach (var name in k.GetValueNames())
                        if (k.GetValue(name) is string v && v.Length > 3 && v.Length < 400 && (v.Contains('\\') || v.Contains('/')))
                            found.Add(v.TrimEnd('\\', '/'));
                }
                catch { }
        // Uninstall entries carry InstallLocation.
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            foreach (var root in new[] { @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                try
                {
                    using var u = hive.OpenSubKey(root);
                    if (u == null) continue;
                    foreach (var sub in u.GetSubKeyNames())
                    {
                        using var e = u.OpenSubKey(sub);
                        var title = e?.GetValue("DisplayName") as string ?? sub;
                        if (title.IndexOf("Aicomi", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (e?.GetValue("InstallLocation") is string loc && loc.Length > 3) found.Add(loc.TrimEnd('\\', '/'));
                    }
                }
                catch { }
        return found;
    }

    // Next to this game (games are often kept side by side), and the usual install folders.
    private static IEnumerable<string> NearbyCandidates()
    {
        var found = new List<string>();
        try
        {
            var game = Directory.GetParent(Paths.GameRootPath);
            for (var up = game; up != null && found.Count < 50; up = up.Parent)
            {
                foreach (var d in SafeDirs(up.FullName))
                    if (Path.GetFileName(d).IndexOf("Aicomi", StringComparison.OrdinalIgnoreCase) >= 0) found.Add(d);
                if (up.Parent == null) break;
            }
        }
        catch { }
        foreach (var drive in SafeDrives())
            foreach (var rel in new[] { "ILLGAMES\\Aicomi", "illgames\\Aicomi", "Games\\Aicomi", "Program Files\\ILLGAMES\\Aicomi", "Program Files (x86)\\ILLGAMES\\Aicomi" })
                found.Add(Path.Combine(drive, rel));
        return found;
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); } catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeDrives()
    {
        try { return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    // ---------- converting ----------

    public static bool Convert(string aicomi, string output)
    {
        var t0 = Time.realtimeSinceStartup;
        var bundleFile = Path.Combine(aicomi, BundlePath);
        AssetBundle bundle = null;
        GameObject rig = null;
        try
        {
            bundle = AssetBundle.LoadFromFile(bundleFile);
            if (bundle == null)
            {
                Plugin.Log.LogWarning($"Could not open Aicomi's {BundlePath} with this game; watchers won't masturbate.");
                return false;
            }
            rig = new GameObject("AL_HWatchers Aicomi rig");
            rig.transform.position = new Vector3(0f, -10000f, 0f);
            var bones = BuildRig(rig.transform);
            // Outside the editor Unity only samples Mecanim clips onto objects that have an Animator.
            var animator = rig.AddComponent<Animator>();
            animator.applyRootMotion = false;

            var clips = new List<SampledClip>();
            foreach (var (controllerName, set) in Sets)
            {
                var obj = bundle.LoadAsset(controllerName, Il2CppType.Of<RuntimeAnimatorController>());
                var controller = obj?.TryCast<RuntimeAnimatorController>();
                if (controller == null) { Plugin.Log.LogWarning($"Aicomi's {controllerName} was not found in the bundle."); continue; }
                var all = controller.animationClips;
                foreach (var state in States)
                {
                    AnimationClip clip = null;
                    for (var i = 0; all != null && i < all.Length && clip == null; i++)
                        if (all[i] != null && all[i].name == "M_" + state) clip = all[i];
                    if (clip == null) continue;
                    var sampled = Sample(clip, rig, bones, $"{set}/{state}");
                    if (sampled.Bones.Count > 0) clips.Add(sampled);
                    else Plugin.Log.LogWarning($"Aicomi's {set}/{state} gave no bone motion when sampled.");
                }
            }
            if (clips.Count == 0)
            {
                Plugin.Log.LogWarning("Found no masturbation clips in Aicomi's bundle; watchers won't masturbate.");
                return false;
            }
            Write(output, clips);
            Plugin.Log.LogInfo($"Converted {clips.Count} masturbation animations from Aicomi ({aicomi}) in {(Time.realtimeSinceStartup - t0) * 1000:0} ms.");
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Converting Aicomi's animations failed: {e}");
            return false;
        }
        finally
        {
            if (rig != null) UnityEngine.Object.Destroy(rig);
            try { bundle?.Unload(true); } catch { }
        }
    }

    private static Dictionary<string, Transform> BuildRig(Transform root)
    {
        var made = new Dictionary<string, Transform>();
        var bones = new Dictionary<string, Transform>();
        foreach (var path in BonePaths)
        {
            var parent = root;
            var sofar = "";
            foreach (var part in path.Split('/'))
            {
                sofar = sofar.Length == 0 ? part : sofar + "/" + part;
                if (!made.TryGetValue(sofar, out var t))
                {
                    t = new GameObject(part).transform;
                    t.SetParent(parent, false);
                    made[sofar] = t;
                }
                parent = t;
            }
            bones[path.Substring(path.LastIndexOf('/') + 1)] = parent;
        }
        return bones;
    }

    // A value no clip would hold, to tell which bones a clip actually moves.
    private static readonly Quaternion Unset = new(0.1f, 0.2f, 0.3f, 0.927362f);
    private static readonly Vector3 UnsetPos = new(12345f, 12345f, 12345f);

    private sealed class SampledBone { public string Name; public Quaternion[] Rot; public Vector3[] Pos; }
    private sealed class SampledClip { public string Name; public int Frames; public float Length; public List<SampledBone> Bones; }

    private static SampledClip Sample(AnimationClip clip, GameObject rig, Dictionary<string, Transform> bones, string name)
    {
        var length = clip.length;
        var frames = Math.Max(2, (int)Math.Round(length * Fps) + 1);
        foreach (var t in bones.Values) { t.localRotation = Unset; t.localPosition = UnsetPos; }
        var names = bones.Keys.Where(n => !Skip.Any(n.StartsWith)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var rot = new Dictionary<string, Quaternion[]>();
        foreach (var n in names) rot[n] = new Quaternion[frames];
        var pos = new Vector3[frames];
        var hips = bones.TryGetValue("cf_j_hips", out var h) ? h : null;
        for (var f = 0; f < frames; f++)
        {
            clip.SampleAnimation(rig, length * f / (frames - 1));
            foreach (var n in names) rot[n][f] = bones[n].localRotation;
            if (hips != null) pos[f] = hips.localPosition;
        }
        var result = new List<SampledBone>();
        foreach (var n in names)
        {
            var r = rot[n];
            if (r[0] == Unset) continue; // the clip doesn't move this bone
            for (var f = 0; f < frames; f++)
            {
                r[f] = Normalize(r[f]);
                if (f > 0 && Quaternion.Dot(r[f - 1], r[f]) < 0f) r[f] = new Quaternion(-r[f].x, -r[f].y, -r[f].z, -r[f].w);
            }
            result.Add(new SampledBone { Name = n, Rot = r, Pos = n == "cf_j_hips" && pos[0] != UnsetPos ? pos : null });
        }
        return new SampledClip { Name = name, Frames = frames, Length = length, Bones = result };
    }

    private static Quaternion Normalize(Quaternion q)
    {
        var m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        return m > 1e-6f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
    }

    // Same format TokLibrary reads.
    private static void Write(string path, List<SampledClip> clips)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var w = new BinaryWriter(File.Create(path), Encoding.UTF8);
        w.Write(Encoding.ASCII.GetBytes("ALHW"));
        w.Write(1);
        w.Write(clips.Count);
        foreach (var c in clips.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            Str(w, c.Name);
            w.Write(c.Frames);
            w.Write(c.Length);
            w.Write(c.Bones.Count);
            foreach (var b in c.Bones)
            {
                Str(w, b.Name);
                w.Write((byte)(1 | (b.Pos != null ? 2 : 0)));
                foreach (var q in b.Rot) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
                if (b.Pos != null) foreach (var p in b.Pos) { w.Write(p.x); w.Write(p.y); w.Write(p.z); }
            }
        }
    }

    private static void Str(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        w.Write(b.Length);
        w.Write(b);
    }
}
