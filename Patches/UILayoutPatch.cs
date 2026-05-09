#nullable enable
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Elements;
using HarmonyLib;
using System.Collections;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class UILayoutPatch
{
    #region 1. Universal Layout
    [HarmonyPatch(typeof(PartsQuestDetail), nameof(PartsQuestDetail.Settings))]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixQuestDetail(PartsQuestDetail __instance)
    {
        if (!ConfigManager.UI.UILayout.Value) return;
        if (!__instance.IsSafe()) return;
        var arrow = __instance.transform.FindDeep("ArrowBlock");
        if (arrow != null) arrow.gameObject.SetActive(false);
    }
    #endregion

    #region 2. Global Repositioning & Boundaries

    [HarmonyPatch(typeof(PartsEventQuestAutoProgressInBattleInfo), "Initialize")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixAutoBattleUI(PartsEventQuestAutoProgressInBattleInfo __instance)
    {
        if (!__instance.IsSafe()) return;

        var autoObj = __instance.transform.FindDeep("questauto_text_auro") ??
                      __instance.transform.FindDeep("questauto_text_auto");

        if (autoObj != null && autoObj.localPosition.x >= 0)
        {
            autoObj.localPosition = new Vector3(autoObj.localPosition.x - 15f, autoObj.localPosition.y, 0f);
        }
    }

    [HarmonyPatch(typeof(PartsDialogUserProfile), "InitializeParam")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixUserProfile(PartsDialogUserProfile __instance)
    {
        if (!__instance.IsSafe()) return;

        var labels = __instance.GetComponentsInChildren<CustomUILabel>();
        foreach (var lb in labels)
        {
            if (!lb.IsSafe()) continue;

            lb.overflowMethod = UILabel.Overflow.ShrinkContent;
            lb.pivot = UIWidget.Pivot.Center;

            if (lb.lineWidth > 0) lb.lineWidth += 20;
        }
    }

    [HarmonyPatch(typeof(PartsDialogAbyssBossResult), "StartShow")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixAbyssResult(PartsDialogAbyssBossResult __instance)
    {
        if (!__instance.IsSafe()) return;

        var rewardLabel = __instance.transform.FindDeep("ToNextRewardLabel");
        if (rewardLabel != null && rewardLabel.localPosition.x > -190f)
        {
            rewardLabel.localPosition = new Vector3(rewardLabel.localPosition.x - 10f, rewardLabel.localPosition.y, 0f);
        }
    }

    [HarmonyPatch(typeof(ViewAlcesTop), "StartView")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixAlcesTop(ViewAlcesTop __instance)
    {
        if (!__instance.IsSafe()) return;

        CoroutineStarter.Run(WaitAndAdjustAlcesUI(__instance));
    }

    private static IEnumerator WaitAndAdjustAlcesUI(ViewAlcesTop instance)
    {
        Transform? gold = null;

        // ใช้ Util.WaitUntilOrTimeoutInstruction แทน manual timeout-loop
        yield return new Util.WaitUntilOrTimeoutInstruction(2f, () =>
        {
            if (!instance.IsSafe()) return true; // abort early
            gold = instance.transform.FindDeep("GUIGroup_Gold");
            return gold != null;
        });

        if (gold != null)
        {
            gold.localPosition += new Vector3(0f, 15f, 0f);

            var pts = instance.transform.FindDeep("GUIGroup_AlcesPt");
            if (pts != null)
                pts.localPosition = gold.localPosition + new Vector3(0f, -40f, 0f);
        }
    }
    #endregion

    #region 3. Shop & Upgrade UI Fixes
    /// <summary>
    /// Fixes the alert label in the Memory Piece deal confirmation dialog.
    /// Sets <c>overflowMethod</c> to <c>ResizeFreely</c> so translated text is never clipped,
    /// and hides all child decorators except the alert icon itself.
    /// </summary>
    [HarmonyPatch(typeof(PartsDialogShopMemoryPieceDealConfirm), "InitializeParam")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixMemoryPieceDeal(PartsDialogShopMemoryPieceDealConfirm __instance)
    {
        if (!__instance.IsSafe() || __instance.alertLabel == null) return;

        __instance.alertLabel.overflowMethod = UILabel.Overflow.ResizeFreely;

        // Hide decorative child objects; keep only the core alert icon
        var alertRoot = __instance.alertObject?.transform;
        if (alertRoot == null) return;

        for (int i = 0; i < alertRoot.childCount; i++)
        {
            var child = alertRoot.GetChild(i);
            if (child.IsSafe() && child.name != "common_icon_alert")
                child.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Corrects label constraints on the Rarity-Up screen:
    /// forces the material-button label to a fixed single-line width of 180px,
    /// and scales the rarity-up button label to 28px to prevent overflow.
    /// </summary>
    [HarmonyPatch(typeof(UnitRarityUp), "Initialize")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixUnitRarityUp(UnitRarityUp __instance)
    {
        if (!__instance.IsSafe()) return;

        var materialLabel = __instance.howtoGetUnitMaterialButton?.GetChildUILabel();
        if (materialLabel.IsSafe())
        {
            materialLabel!.multiLine  = false;
            materialLabel.lineWidth   = 180;
        }

        var rarityLabel = __instance.rarityUpButton?.GetChildUILabel();
        if (rarityLabel.IsSafe())
            rarityLabel!.fontSize = 28;
    }
    #endregion

    #region 4. Battle UI Fixes
    /// <summary>
    /// Reduces the HP label font size on the boss gauge during Special Battle mode.
    /// Special Battle renders longer translated text (e.g. Thai, Arabic) that overflows
    /// at the default size — scaling to 16px prevents clipping without affecting layout.
    /// </summary>
    [HarmonyPatch(typeof(PartsBossGauge), "InitGauge")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixBossGauge(PartsBossGauge __instance)
    {
        if (!__instance.IsSafe()) return;
        if (__instance.battleManager.IsSafe() && __instance.battleManager.IsSpecialBattle)
        {
            if (__instance.hpLabel.IsSafe())
                __instance.hpLabel.fontSize = 16;
        }
    }

    /// <summary>
    /// Scales down the difficulty label inside the Talent Weakness icon panel.
    /// The label path <c>GUIGroup_Difficulty_Normal/Label</c> renders short localised text
    /// (e.g. "Normal") that overflows at default size for some script families.
    /// </summary>
    [HarmonyPatch(typeof(PartsTalentWeaknessIcons), "SetIcon", new[] { typeof(int) })]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixTalentWeakness(PartsTalentWeaknessIcons __instance)
    {
        if (!__instance.IsSafe()) return;

        var labelGo = __instance.transform
            .Find("GUIGroup_Difficulty_Normal/Label")
            ?.gameObject;
        if (labelGo == null) return;

        var label = labelGo.GetComponentInChildren<CustomUILabel>();
        if (label.IsSafe())
            label!.fontSize = 10;
    }
    #endregion

    #region 5. Ranking & Arena UI Fixes
    /// <summary>
    /// Constrains the clan total damage label to 180px in the Clan Battle ranking panel.
    /// Translated damage strings (with thousands separators) are wider than the Japanese original.
    /// </summary>
    [HarmonyPatch(typeof(PartsClanBattleRankingSelf), "SetData")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixClanBattleRanking(PartsClanBattleRankingSelf __instance)
    {
        if (!__instance.IsSafe() || !__instance.clanTotalDamage.IsSafe()) return;
        __instance.clanTotalDamage.lineWidth = 180;
    }

    /// <summary>
    /// Sets <c>ResizeFreely</c> overflow on the Defense Unit and Battle History button labels
    /// in the Grand Arena top screen, preventing text clipping for long translated strings.
    /// </summary>
    [HarmonyPatch(typeof(ViewGrandArenaTop), "StartView")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixGrandArenaRanking(ViewGrandArenaTop __instance)
    {
        if (!__instance.IsSafe() || !__instance.newPlayerInfo.IsSafe()) return;

        var defLabel = __instance.newPlayerInfo.DefenseUnitButton?.GetChildUILabel();
        if (defLabel.IsSafe())
            defLabel!.overflowMethod = UILabel.Overflow.ResizeFreely;

        var histLabel = __instance.newPlayerInfo.BattleHistoryButton?.GetChildUILabel();
        if (histLabel.IsSafe())
            histLabel!.overflowMethod = UILabel.Overflow.ResizeFreely;
    }
    #endregion

    #region 6. Clan Battle & Normal Arena Rank Display
    /// <summary>
    /// Replaces the rank number label on the Clan Battle player ranking plate
    /// (<see cref="PartsClanBattleRankingPlate"/>).
    /// Appends the locale-appropriate ordinal suffix via <see cref="Util.GetRankSuffix"/>
    /// and nudges the label position to -7.5 px to account for the wider localised text.
    /// </summary>
    [HarmonyPatch(typeof(PartsClanBattleRankingPlate), "SetRanking")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixCBRank(
        PartsClanBattleRankingPlate __instance,
        CustomUILabel               _rankNumber,
        int                        _rank)
    {
        if (!__instance.IsSafe() || !_rankNumber.IsSafe()) return;
        _rankNumber.text = $"{_rank}{Util.GetRankSuffix(_rank)}";
        var pos = _rankNumber.transform.localPosition;
        pos.x = -7.5f;
        _rankNumber.transform.localPosition = pos;
    }

    /// <summary>
    /// Replaces the rank number label on the Clan Battle <b>Boss</b> ranking plate
    /// (<see cref="PartsClanBattleBossRankingPlate"/>).
    /// Uses the same ordinal suffix logic but offsets the label to -2.5 px
    /// (narrower layout than the standard ranking plate).
    /// </summary>
    [HarmonyPatch(typeof(PartsClanBattleBossRankingPlate), "setRanking")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixCBBossRank(
        PartsClanBattleBossRankingPlate __instance,
        CustomUILabel                   _rankNumber,
        int                             _rank)
    {
        if (!__instance.IsSafe() || !_rankNumber.IsSafe()) return;
        _rankNumber.text = $"{_rank}{Util.GetRankSuffix(_rank)}";
        var pos = _rankNumber.transform.localPosition;
        pos.x = -2.5f;
        _rankNumber.transform.localPosition = pos;
    }

    /// <summary>
    /// Corrects the rank display on <see cref="ViewClanBattleTop"/> after the rank is set
    /// (<c>setRankNum</c>).  Hides the original numeric-only label nodes and replaces them
    /// with exception-label nodes that show the rank with an ordinal suffix.
    /// Handles both all-clan and within-clan rankings independently.
    /// </summary>
    [HarmonyPatch(typeof(ViewClanBattleTop), "setRankNum")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixCBRankNum(
        ViewClanBattleTop __instance,
        int               _rankInClan,
        int               _rankAllClan,
        bool              _isAggregate)
    {
        if (!__instance.IsSafe() || _isAggregate) return;

        if (_rankAllClan > 0)
        {
            var rankNoAll   = __instance.transform.Find("LeftNode/UIPanel/RankingNode/GUIGroup_Ranking/RankNoAll");
            var exAllLabel  = __instance.transform.Find("LeftNode/UIPanel/RankingNode/GUIGroup_Ranking/GUIGroup_Ranking_All/Label_exceptionAllClan");
            if (rankNoAll.IsSafe() && exAllLabel.IsSafe())
            {
                rankNoAll!.gameObject.SetActive(false);
                exAllLabel!.gameObject.SetActive(true);
                var lbl = exAllLabel.gameObject.GetComponent<CustomUILabel>();
                if (lbl.IsSafe()) lbl!.text = $"{_rankAllClan}{Util.GetRankSuffix(_rankAllClan)}";
            }
        }

        if (_rankInClan > 0)
        {
            var rankNoIn    = __instance.transform.Find("LeftNode/UIPanel/RankingNode/GUIGroup_Ranking/RankNoInClan");
            var exClanLabel = __instance.transform.Find("LeftNode/UIPanel/RankingNode/GUIGroup_Ranking/GUIGroup_Ranking_Clan/Label_exceptionInClan");
            if (rankNoIn.IsSafe() && exClanLabel.IsSafe())
            {
                rankNoIn!.gameObject.SetActive(false);
                exClanLabel!.gameObject.SetActive(true);
                var lbl = exClanLabel.gameObject.GetComponent<CustomUILabel>();
                if (lbl.IsSafe()) lbl!.text = $"{_rankInClan}{Util.GetRankSuffix(_rankInClan)}";
            }
        }
    }

    /// <summary>
    /// Sets <c>ResizeFreely</c> overflow on the Defense Unit and Battle History button labels
    /// in the Normal Arena top screen — the same fix as Grand Arena but targeting
    /// <see cref="ViewNormalArenaTop"/>'s direct field references.
    /// </summary>
    [HarmonyPatch(typeof(ViewNormalArenaTop), "StartView")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixNormalArenaRanking(ViewNormalArenaTop __instance)
    {
        if (!__instance.IsSafe()) return;

        var defLabel = __instance.defenseUnitButton?.GetChildUILabel();
        if (defLabel.IsSafe()) defLabel!.overflowMethod = UILabel.Overflow.ResizeFreely;

        var histLabel = __instance.battleHistoryButton?.GetChildUILabel();
        if (histLabel.IsSafe()) histLabel!.overflowMethod = UILabel.Overflow.ResizeFreely;
    }

    /// <summary>
    /// Postfix on <c>ViewClanBattleRanking.Initialize</c>: walks all
    /// <see cref="PartsClanBattleRankingPlate"/> children and widens their clan-name
    /// and damage labels to prevent translated text from overflowing at initialisation time.
    /// This complements <see cref="PostfixClanBattleRanking"/> (which fires on <c>SetData</c>)
    /// by ensuring the layout is correct before any data is bound.
    /// </summary>
    [HarmonyPatch(typeof(ViewClanBattleRanking), "Initialize")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixCBInitializeRank(ViewClanBattleRanking __instance)
    {
        if (!__instance.IsSafe()) return;

        // Apply overflow/lineWidth corrections to every ranking plate in the list
        var plates = __instance.GetComponentsInChildren<PartsClanBattleRankingPlate>();
        if (plates == null) return;

        foreach (var plate in plates)
        {
            if (!plate.IsSafe()) continue;

            // Probe all labels in the plate and apply overflow fixes based on name heuristics
            var labels = plate.GetComponentsInChildren<CustomUILabel>();
            foreach (var lbl in labels)
            {
                if (!lbl.IsSafe()) continue;

                string n = lbl.name?.ToLowerInvariant() ?? string.Empty;

                if (n.Contains("damage") || n.Contains("score") || n.Contains("total"))
                {
                    // Numeric labels: allow shrink to fit wider translated number strings
                    lbl.overflowMethod = UILabel.Overflow.ShrinkContent;
                    if (lbl.lineWidth > 0) lbl.lineWidth = Mathf.Max(lbl.lineWidth, 200);
                }
                else if (n.Contains("name") || n.Contains("clan"))
                {
                    // Name labels: shrink gracefully for long translated clan/player names
                    lbl.overflowMethod = UILabel.Overflow.ShrinkContent;
                }
            }
        }

        FLog.Debug("[CB] ViewClanBattleRanking initialised — rank plate labels adjusted.");
    }

    /// <summary>
    /// Postfix on <c>PartsClanBattleRankingPlate.Initialize</c>: pre-configures the rank number
    /// label layout before <c>SetRanking</c> populates it with live data.
    /// Ensures the label never clips the ordinal suffix added by <see cref="PostfixCBRank"/>.
    /// </summary>
    [HarmonyPatch(typeof(PartsClanBattleRankingPlate), "Initialize")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixCBRankingPlateInit(PartsClanBattleRankingPlate __instance)
    {
        if (!__instance.IsSafe()) return;

        // Pre-size the rank-number label so it never clips the suffix
        var rankLabel = __instance.GetComponentInChildren<CustomUILabel>();
        if (rankLabel.IsSafe())
        {
            rankLabel!.overflowMethod = UILabel.Overflow.ResizeFreely;
            rankLabel.lineWidth       = 120;
        }
    }
    #endregion

    // =========================================================================
    // REGION 7: Monster Detail Scroll & Overflow
    // =========================================================================

    #region 7. Monster Detail Scroll & Overflow
    /// <summary>Coroutine handle, stopped when the detail dialog is destroyed.</summary>
    private static Coroutine? _monsterDetailCoroutine;

    /// <summary>
    /// Postfix on <c>PartsDialogMonsterDetail.InitializeParam</c>:
    /// enables the scroll view on the monster detail text controller so that
    /// translated descriptions longer than the box height become scrollable.
    /// </summary>
    [HarmonyPatch(typeof(PartsDialogMonsterDetail), "InitializeParam")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixMonsterDetailScrollInit(PartsDialogMonsterDetail __instance)
    {
        if (!__instance.IsSafe()) return;
        var ctrl = __instance.monsterDetailTextController;
        if (!ctrl.IsSafe()) return;
        var scrollContent = ctrl.curUIScrollContent;
        if (!scrollContent.IsSafe()) return;
        var scrollView = scrollContent.curUIScrollView;
        if (scrollView.IsSafe())
        {
            scrollView!.enabled = true;
            FLog.Debug("[MonsterDetail] ScrollView enabled.");
        }
    }

    /// <summary>
    /// Postfix on <c>PartsDialogMonsterDetail.OnDestroy</c>:
    /// stops the overflow-adjustment coroutine to prevent updates on a destroyed object.
    /// </summary>
    [HarmonyPatch(typeof(PartsDialogMonsterDetail), "OnDestroy")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixMonsterDetailOnDestroy()
    {
        if (_monsterDetailCoroutine != null)
        {
            CoroutineStarter.Instance.StopCoroutine(_monsterDetailCoroutine);
            _monsterDetailCoroutine = null;
            FLog.Debug("[MonsterDetail] Overflow coroutine stopped on destroy.");
        }
    }

    /// <summary>
    /// Postfix on <c>PartsMonsterDetailTextPlate.SetText</c>:
    /// switches the detail-text label to <c>ResizeHeight</c> overflow and launches
    /// a coroutine that repositions the plate below its siblings and refreshes
    /// the scroll-view bounds whenever the translated text content changes.
    /// </summary>
    [HarmonyPatch(typeof(PartsMonsterDetailTextPlate), "SetText")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixMonsterDetailOverflow(PartsMonsterDetailTextPlate __instance)
    {
        if (!__instance.IsSafe() || !__instance.detailText.IsSafe()) return;

        __instance.detailText.overflowMethod = UILabel.Overflow.ResizeHeight;

        // Cancel any in-flight coroutine from a previous SetText call
        if (_monsterDetailCoroutine != null)
            CoroutineStarter.Instance.StopCoroutine(_monsterDetailCoroutine);

        _monsterDetailCoroutine = CoroutineStarter.Instance.StartCoroutine(
            BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions.WrapToIl2Cpp(UpdateMonsterDetailPlate(__instance)));
    }

    /// <summary>
    /// Coroutine: waits for NGUI to complete its layout pass, then repositions
    /// <paramref name="textPlate"/> below the lowest sibling and presses any
    /// <see cref="UIDragScrollView"/> components to refresh scroll bounds.
    /// Loops until <c>detailText</c> is destroyed.
    /// </summary>
    private static IEnumerator UpdateMonsterDetailPlate(PartsMonsterDetailTextPlate textPlate)
    {
        var go     = textPlate.gameObject;
        var parent = go.transform.parent;

        // Find the lowest sibling (by Y position) excluding the textPlate itself.
        // Replaces the original goto-based loop with a clean LINQ expression.
        Transform? lowestChild = Enumerable
            .Range(0, parent.childCount)
            .Select(i => parent.GetChild(i))
            .Where(c => c != go.transform)
            .OrderBy(c => c.position.y)
            .FirstOrDefault();

        string lastText = string.Empty;

        while (textPlate.IsSafe() && textPlate.detailText.IsSafe())
        {
            string currentText = textPlate.detailText.text;
            if (currentText != lastText)
            {
                lastText = currentText;
                yield return null; // wait one frame for NGUI layout

                if (lowestChild.IsSafe())
                {
                    int h = textPlate.detailText.height;
                    var lp = go.transform.localPosition;
                    lp.y = lowestChild!.localPosition.y - h * 0.5f;
                    go.transform.localPosition = lp;
                }

                // Three-frame buffer so NGUI finishes repositioning children
                yield return null;
                yield return null;
                yield return null;

                // Refresh scroll-view bounds: press self, parent, and own component
                RefreshDragScrollView(textPlate.transform);
                RefreshDragScrollView(textPlate.transform.parent);
                var selfDrag = go.GetComponent<UIDragScrollView>();
                if (selfDrag.IsSafe()) selfDrag!.CallOnPress();
            }

            yield return null;
        }
    }

    /// <summary>
    /// Finds the first <see cref="UIDragScrollView"/> child of <paramref name="root"/>
    /// and calls <c>CallOnPress()</c> to trigger a scroll-bounds refresh.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RefreshDragScrollView(Transform root)
    {
        if (!root.IsSafe()) return;
        var drag = root!.GetComponentInChildren<UIDragScrollView>();
        if (drag.IsSafe()) drag!.CallOnPress();
    }

    /// <summary>
    /// Prefix on <c>PartsMonsterDetailTextController.Initialize</c>:
    /// consolidates the multi-element description list into a single joined string
    /// so that XUAT receives one contiguous block of text to translate rather than
    /// individual sentence fragments, which would otherwise be cached and translated
    /// separately and never combined into a coherent paragraph.
    /// Skipped when the translation system is inactive.
    /// </summary>
    [HarmonyPatch(typeof(PartsMonsterDetailTextController), "Initialize")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixSkillDescription(
        ref Il2CppSystem.Collections.Generic.List<string> _monsterDetailTextList)
    {
        if (!Util.IsXuatActive()) return;
        if (_monsterDetailTextList == null || _monsterDetailTextList.Count == 0) return;

        // Join all description fragments with newlines into one entry.
        // GeneralExtensions.Join is available from the game assembly; string.Join
        // is used here for IL2CPP interop safety.
        var parts = _monsterDetailTextList.ToArray();
        string joined = string.Join("\n", (object[])parts);

        var single = new Il2CppSystem.Collections.Generic.List<string>();
        single.Add(joined);
        _monsterDetailTextList = single;

        FLog.Debug($"[MonsterDetail] Consolidated {parts.Length} description entries into 1.");
    }
    #endregion

    // =========================================================================
    // REGION 8: Miscellaneous UI Fixes
    // =========================================================================

    #region 8. Miscellaneous UI Fixes

    // ── 8.1 Settings / Menu buttons (ViewMenuTop) ────────────────────────────

    /// <summary>
    /// Postfix on <c>ViewMenuTop.StartView</c>: widens the "System" and "Cartoon"
    /// button labels so that longer translated strings are not clipped by the
    /// default Japanese label widths.
    /// </summary>
    [HarmonyPatch(typeof(ViewMenuTop), "StartView")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixSettingsButton(ViewMenuTop __instance)
    {
        if (!__instance.IsSafe()) return;
        if (__instance.systemButton.IsSafe())
            __instance.systemButton!.GetChildUILabel().lineWidth = 115;
        if (__instance.cartoonButton.IsSafe())
            __instance.cartoonButton!.GetChildUILabel().lineWidth = 190;
    }

    // ── 8.2 Gold Shop jewel-type label (PartsGoldShopPlate) ──────────────────

    /// <summary>
    /// Postfix on <c>PartsGoldShopPlate.SetUseJewel</c>: switches the jewel-type
    /// label to <c>ResizeFreely</c> overflow so that translated names wider than
    /// the original Japanese text are displayed in full.
    /// </summary>
    [HarmonyPatch(typeof(PartsGoldShopPlate), "SetUseJewel")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixGoldShopPlate(PartsGoldShopPlate __instance)
    {
        if (!__instance.IsSafe()) return;
        if (__instance.useJewelTypeLabel.IsSafe())
            __instance.useJewelTypeLabel!.overflowMethod = UILabel.Overflow.ResizeFreely;
    }

    // ── 8.3 Header slide-in anchor refresh (HeaderController) ────────────────

    /// <summary>
    /// Prefix (replaces) on <c>HeaderController.RestoreSlideIn</c>:
    /// tweens the header back to its rest position and, after the tween completes,
    /// broadcasts <c>UpdateAnchors</c> and fires <c>UICamera.onScreenResize</c> so that
    /// all UI panels re-anchor themselves correctly for the translated layout.
    /// Returns <see langword="false"/> to skip the original method.
    /// </summary>
    [HarmonyPatch(typeof(HeaderController), "RestoreSlideIn")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixHeaderRestoreSlideIn(HeaderController __instance, float _time = 0.1f)
    {
        if (!__instance.IsSafe()) return true;
        var tween = TweenPosition.Begin(__instance.slideObjTop, _time, Vector3.zero);
        CoroutineStarter.Instance.StartCoroutine(
            BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions.WrapToIl2Cpp(
                WaitForHeaderTween(tween)));
        return false;
    }

    private static IEnumerator WaitForHeaderTween(TweenPosition tween)
    {
        while (tween.IsSafe() && tween!.enabled)
            yield return null;

        UIRoot.Broadcast("UpdateAnchors");
        UICamera.onScreenResize?.Invoke();
    }

    // ── 8.4 Equipment detail label overflow (PartsEquipmentDetail) ───────────

    /// <summary>
    /// Postfix on <c>PartsEquipmentDetail.setInfoStatusAndUI</c>:
    /// sets all equipment-status labels (identified by <c>eTextId.EQUIP_STATUS_LABEL</c>
    /// or the translated stub text <c>"Equipment Stats"</c>) to <c>ResizeFreely</c>
    /// overflow so that long translated stat names are never truncated.
    /// </summary>
    [HarmonyPatch(typeof(PartsEquipmentDetail), "setInfoStatusAndUI")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixEquipmentDetail(PartsEquipmentDetail __instance)
    {
        if (!__instance.IsSafe()) return;
        foreach (var label in __instance.gameObject.transform
                     .GetComponentsInChildren<CustomUILabel>()
                     .ToArray()
                     .Where(l => l.IsSafe()
                              && (l!.curTextId == eTextId.EQUIP_STATUS_LABEL
                               || l.text == "Equipment Stats")))
        {
            label.overflowMethod = UILabel.Overflow.ResizeFreely;
        }
    }

    // ── 8.5 Profile card tower floor label (PartsProfileCard) ────────────────

    /// <summary>
    /// Postfix on <c>PartsProfileCard.Initialize</c>:
    /// narrows the "Reaching Floor" label and shifts it slightly so that the
    /// translated ordinal text fits within the pink badge area on the profile card.
    /// </summary>
    [HarmonyPatch(typeof(PartsProfileCard), "Initialize")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixProfileCard(PartsProfileCard __instance)
    {
        if (!__instance.IsSafe()) return;
        const string path =
            "_Game(Clone)/UI Root/ViewsArea/View/ViewMyProfileCard(Clone)" +
            "/TopProfileImage/MyProfileCard/ProfileLayer/ProfileNode" +
            "/Profile/Tower/ReachingFloor/common_dt_bg_pink/Label";

        var go = GameObject.Find(path);
        if (!go.IsSafe()) return;

        var label = go!.GetComponent<CustomUILabel>();
        if (!label.IsSafe()) return;

        label!.lineWidth = 180;
        var lp = go.transform.localPosition;
        lp.x = 3.4f;
        go.transform.localPosition = lp;
    }

    // ── 8.6 Header back-button title text (PartsHeaderBackButton) ────────────

    /// <summary>
    /// Prefix (replaces) on <c>PartsHeaderBackButton.SetTitleText</c>:
    /// hides the primary title label, routes the text through <c>titleLabel2nd</c>,
    /// trims redundant newlines, and fits the underline width to the rendered text.
    /// After layout, launches <see cref="WaitForTitleTranslation"/> to re-fit the
    /// underline once XUAT has replaced the text with the translated version.
    /// Returns <see langword="false"/> to skip the original method.
    /// </summary>
    [HarmonyPatch(typeof(PartsHeaderBackButton), "SetTitleText")]
    [HarmonyPrefix]
    [HarmonyPriority(100)]
    [HarmonyWrapSafe]
    public static bool PrefixTitleText(PartsHeaderBackButton __instance, string _setTitleText)
    {
        if (!__instance.IsSafe()) return true;

        if (_setTitleText.IsNullOrEmpty())
        {
            __instance.subTitleLabel.SetActiveWithCheck(false);
            return false;
        }

        bool wasActive = __instance.gameObject.activeSelf;
        __instance.gameObject.SetActive(false);
        __instance.gameObject.SetActive(true);

        string clean = _setTitleText.Replace("\n", " ").Replace("  ", " ");
        __instance.titleLabel.SetText(string.Empty,
            (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>?)null);
        __instance.titleLabel2nd.SetText(clean,
            (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>?)null);
        __instance.titleLabel2nd.SetText(
            __instance.titleLabel2nd.text.Replace("\n", " ").Replace("  ", " "),
            (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>?)null);

        // Toggle labels to force NGUI layout refresh
        __instance.titleLabel.SetActive(false);
        __instance.titleLabel2nd.SetActive(false);
        __instance.titleLabel.SetActive(true);
        __instance.titleLabel2nd.SetActive(true);
        __instance.gameObject.SetActive(wasActive);

        // Fit the underline to the mixed CJK+Latin text width
        FitUnderline(__instance);

        __instance.subTitleLabel.SetActiveWithCheck(false);

        int offset = __instance.backButton == null ? 50 : 0;
        var lp = __instance.titleLabel.transform.localPosition;
        lp.x = __instance.backButton == null ? 22f : 72f;
        __instance.titleLabel.transform.localPosition = lp;

        CoroutineStarter.Instance.StartCoroutine(
            BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions.WrapToIl2Cpp(
                WaitForTitleTranslation(__instance.titleLabel2nd, __instance.underLine,
                    _setTitleText, offset)));
        return false;
    }

    /// <summary>Adjusts underline width to match the rendered title including CJK/Latin mix.</summary>
    private static void FitUnderline(PartsHeaderBackButton btn)
    {
        if (!btn.titleLabel2nd.IsSafe()) return;
        var label = btn.titleLabel2nd!;

        // Use character-range detection: if any character is outside ASCII/Latin-Extended,
        // treat the text as containing CJK or other non-Latin script.
        bool hasNonLatin = label.text.Any(c => c > '\u02AF');

        if (!hasNonLatin)
        {
            label.ProcessText();
            int offset = btn.backButton == null ? 50 : 0;
            float w = label.mCalculatedSize.x + 20f + offset;
            btn.underLine.width = (int)System.Math.Round(w);
            var hdr = SingletonMonoBehaviour<HeaderController>.Instance;
            hdr?.campaignIcons.SetIconPosition(hdr.viewManager.CurrentViewId, w);
            return;
        }

        // CJK mixed: count non-ASCII chars and estimate proportional widths
        var matches = System.Text.RegularExpressions.Regex.Matches(label.text, "[a-zA-Z0-9]");
        int asciiCount    = matches.Count;
        int nonAsciiCount = label.text.Length - asciiCount;
        int fs  = label.fontSize;
        int fsN = (int)System.Math.Ceiling(fs * 0.75);
        int w2  = btn.titleLabel.text.Length * btn.titleLabel.fontSize
                + nonAsciiCount * fs + asciiCount * fsN;
        btn.underLine.width = btn.leftOffset + w2 + btn.rightOffset;
        btn.underLine.gameObject.SetActive(true);
    }

    private static IEnumerator WaitForTitleTranslation(
        UILabel label, UIWidget underline, string original, int offset)
    {
        var wait = new Util.WaitUntilOrTimeoutInstruction(5f, () => label.text != original);
        while (wait.keepWaiting) yield return null;

        if (!label.IsSafe()) yield break;
        bool hasNonLatin = label!.text.Any(c => c > '\u02AF');
        if (hasNonLatin) yield break; // Japanese/CJK — skip resize

        label.ProcessText();
        float w = label.mCalculatedSize.x + 20f + offset;
        underline.width = (int)System.Math.Round(w);
        var hdr = SingletonMonoBehaviour<HeaderController>.Instance;
        hdr?.campaignIcons.SetIconPosition(hdr.viewManager.CurrentViewId, w);
    }

    // ── 8.7 Mirage Alces button label (ViewMirageTop) ─────────────────────────

    /// <summary>
    /// Postfix on <c>ViewMirageTop.StartView</c>:
    /// waits up to 2 s for the Alces button label to appear in the scene hierarchy,
    /// then shrinks its font size and enables <c>ResizeHeight</c> overflow so that
    /// the translated "Alces" label text wraps within the icon area.
    /// </summary>
    [HarmonyPatch(typeof(ViewMirageTop), "StartView")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixMirageAlcesButton(ViewMirageTop __instance)
    {
        if (!__instance.IsSafe()) return;
        const string path =
            "_Game(Clone)/UI Root/ViewsArea/View/ViewMirageTop(Clone)" +
            "/RightNode/PartsMirageTopRightNode/AnchorTopRight/HeaderIconButton/Alces/Label";
        CoroutineStarter.Instance.StartCoroutine(
            BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions.WrapToIl2Cpp(
                WaitAndFixLabel(path, 80, 15, UILabel.Overflow.ResizeHeight)));
    }

    // ── 8.8 Mirage Quest button label (ViewQuestTop) ──────────────────────────

    /// <summary>
    /// Postfix on <c>ViewQuestTop.StartView</c>:
    /// immediately resizes the "Mirage Quest" button label so that the translated
    /// text wraps inside the button area without overflowing horizontally.
    /// </summary>
    [HarmonyPatch(typeof(ViewQuestTop), "StartView")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixMirageButton(ViewQuestTop __instance)
    {
        if (!__instance.IsSafe()) return;
        var btn = __instance.buttonMirageQuest?.Button;
        if (!btn.IsSafe()) return;

        var label = btn!.GetChildUILabel();
        if (!label.IsSafe()) return;

        label!.lineWidth      = 145;
        label.fontSize        = 22;
        label.overflowMethod  = UILabel.Overflow.ResizeHeight;
        var lp = label.transform.localPosition;
        lp.y = -50f;
        lp.x = 0f;
        label.transform.localPosition = lp;
    }

    // ── Shared helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Polls for a <see cref="GameObject"/> at <paramref name="path"/> for up to 2 s,
    /// then applies <paramref name="lineWidth"/>, <paramref name="fontSize"/>, and
    /// <paramref name="overflow"/> to its <see cref="CustomUILabel"/> component.
    /// </summary>
    private static IEnumerator WaitAndFixLabel(
        string path, int lineWidth, int fontSize, UILabel.Overflow overflow, float timeout = 2f)
    {
        float start = UnityEngine.Time.realtimeSinceStartup;
        while (GameObject.Find(path) == null
            && UnityEngine.Time.realtimeSinceStartup - start < timeout)
            yield return null;

        var go = GameObject.Find(path);
        if (!go.IsSafe()) yield break;

        var label = go!.GetComponent<CustomUILabel>();
        if (!label.IsSafe()) yield break;

        label!.lineWidth     = lineWidth;
        label.fontSize       = fontSize;
        label.overflowMethod = overflow;
        FLog.Debug($"[UILayout] Fixed label at {path}: w={lineWidth} fs={fontSize}.");
    }

    // ── 8.9 Skill Detail Popup (PartsDialogUnitSkillDetail) ──────────────────

    /// <summary>
    /// Postfix on <c>PartsDialogUnitSkillDetail.display</c>:
    /// reconstructs the body that the decompiler could not emit (NullReferenceException
    /// in ILAst type-inference).
    /// Applies <c>ResizeHeight</c> overflow and sets a consistent vertical line-spacing
    /// on all <see cref="CustomUILabel"/> components inside the skill detail popup so
    /// that translated descriptions with extra lines do not overflow the dialog frame.
    /// </summary>
    [HarmonyPatch(typeof(PartsDialogUnitSkillDetail), "display")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixSkillPopup(PartsDialogUnitSkillDetail __instance)
    {
        if (!__instance.IsSafe()) return;

        const float VerticalSpacing = 7f;

        // Apply overflow + spacing to every CustomUILabel inside the popup
        foreach (var label in __instance.gameObject
                     .GetComponentsInChildren<CustomUILabel>()
                     .ToArray()
                     .Where(l => l.IsSafe()))
        {
            label!.overflowMethod = UILabel.Overflow.ResizeHeight;
            label.spacingY        = (int)VerticalSpacing;
        }
    }

    #endregion
}