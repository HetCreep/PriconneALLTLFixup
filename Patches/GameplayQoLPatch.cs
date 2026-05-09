#nullable enable
using Elements;
using HarmonyLib;
using System;
using System.Runtime.CompilerServices;

namespace PriconneALLTLFixup.Patches;

/// <summary>
/// Gameplay Quality-of-Life patch module providing minor but impactful player experience improvements.
/// <para>Module A: Birthday Everyday — overrides the player's stored birth-date with today's date,
/// ensuring Birthday Voice lines play on every login.</para>
/// <para>Module B: Auto-Focus Search — automatically focuses the search input when the unit
/// search panel opens, removing the need for an extra tap/click.</para>
/// </summary>
[HarmonyPatch]
public static class GameplayQoLPatch
{
    // =========================================================================
    // MODULE A: Birthday Everyday (LoadIndexReceiveParam::ParseLoadIndexReceiveParam)
    // =========================================================================

    #region 1. Module A — Hook
    /// <summary>
    /// Postfix on the login-data parser: overrides <c>UserBirth</c> and <c>Voice.Birthday</c>
    /// with the current date, so the game treats every day as the player's birthday.
    /// Controlled by <see cref="ConfigManager.Gameplay.BirthdayEveryday"/>.
    /// </summary>
    [HarmonyPatch(typeof(LoadIndexReceiveParam), "ParseLoadIndexReceiveParam")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixBirthdayEveryday(ref LoadIndexReceiveParam __instance)
    {
        if (!ConfigManager.Gameplay.BirthdayEveryday.Value) return;

        // Format today as YYYYMMDD for UserBirth and MMDD for Voice.Birthday
        var now = DateTime.Now;
        __instance.UserInfo.UserBirth = int.Parse(now.ToString("yyyyMMdd"));
        __instance.Voice.Birthday     = int.Parse(now.ToString("MMdd"));

        FLog.Debug($"[QoL] Birthday override applied: UserBirth={__instance.UserInfo.UserBirth}");
    }
    #endregion

    // =========================================================================
    // MODULE B: Auto-Focus Search (SearchUnitNamePlate::Initialize)
    // =========================================================================

    #region 2. Module B — Hook
    /// <summary>
    /// Postfix on the unit-search panel initializer: selects the search input field
    /// automatically, bypassing the need for a manual tap to begin typing.
    /// Controlled by <see cref="ConfigManager.Gameplay.AutoFocusSearch"/>.
    /// </summary>
    [HarmonyPatch(typeof(SearchUnitNamePlate), "Initialize")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixAutoFocusSearch(SearchUnitNamePlate __instance)
    {
        if (!ConfigManager.Gameplay.AutoFocusSearch.Value) return;
        if (!__instance.IsSafe() || __instance.input == null) return;

        __instance.input.OnSelect(true);
        FLog.Debug("[QoL] Search input auto-focused.");
    }
    #endregion
}
