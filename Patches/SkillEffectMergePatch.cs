#nullable enable
using Elements;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.AutoTranslator.Plugin.Core.Extensions;

namespace PriconneALLTLFixup.Patches;

/// <summary>
/// Skill-effect translation fix — a faithful port of the original two-mod
/// pipeline (PriconneTLFixup by Dakari + PriconneSkillTLFixup by Olegase),
/// merged into one patch class so a single config toggle controls every part.
///
/// <para><b>The problem.</b> The Skill Details dialog runs every skill string
/// through <see cref="PartsDialogUnitSkillDetail.stringToLineStringList"/>,
/// which pre-wraps it into <c>(ePlateType, string)</c> entries — one entry per
/// display LINE. <see cref="PartsUnitSkillDetailTextController.Initialize"/>
/// then builds one recycled plate per entry, so a single logical effect such as
/// <c>12544ダメージ分の…バリアを展開する</c> is spread across several plates and
/// XUAT polls each fragment separately — its anchored <c>^…$</c> regex templates
/// can never match a half-sentence fragment.</para>
///
/// <para><b>Phase 1 — store &amp; thin</b> (<see cref="PrefixStoreAndThin"/>):
/// at <c>Initialize</c>, a running position counter keeps the first two entries
/// of each group as their own plates and merges every later entry's text into
/// the second one — exactly the heuristic PriconneTLFixup's
/// <c>SkillTextStorePatch</c> used. The counter resets at the literal
/// <c>スキル効果</c> section header. Merged-away entries are dropped from the
/// list with <see cref="System.Collections.IList.RemoveAt"/> only — the IL2CPP
/// <c>ValueTuple</c> entries are never modified in place (doing so corrupts the
/// list). One stored string is kept per SURVIVING entry so Phase 2 stays aligned
/// 1:1 with the plates the game builds.</para>
///
/// <para><b>Phase 2 — apply &amp; relayout</b> (<see cref="PostfixApplySkillUi"/>):
/// at <c>display</c>, walks the plates, dequeues one stored string per active
/// plate, writes each section's merged text onto its surviving detail label,
/// then re-stacks the plates so the now-taller labels do not overlap and
/// re-enables the scroll view — a faithful port of <c>SkillPopupPatch</c>.</para>
///
/// <para><b>Phase 3 — re-queue</b> (<see cref="PrefixRequeueEmptyPoll"/>): a port
/// of PriconneSkillTLFixup's <c>SkillTranslationPatch</c>. When XUAT polls a
/// label with empty text it reads the real text off the component and re-queues
/// it (newlines stripped) so XUAT's combined <c>r:"…"</c> regex patterns match.
/// The Prefix declares the EXACT internal XUAT parameter types — patching
/// <c>TranslateOrQueueWebJobImmediate</c> with <c>object</c>-typed parameters
/// made MonoMod's JIT crash with fatal CLR error 0x80131506. The csproj
/// publicizes <c>XUnity.AutoTranslator.Plugin.Core</c> so these internal types
/// compile as exact parameter types and MonoMod emits a clean trampoline.</para>
///
/// <para><b>Phase 4 — Monster Details</b> (<see cref="PrefixMonsterMerge"/> and
/// friends): the scroll/overflow skeleton is a faithful port of PriconneTLFixup's
/// four monster patches — <c>SkillDescriptionPatch</c> (P1: collapse the fragment
/// list to one entry so the recycling controller builds exactly one plate),
/// <c>MonsterDetailScrollContainerPatch</c> (P2: enable the scroll view),
/// <c>MonsterDetailOverflowPatch</c> (P3: <c>ResizeHeight</c> + a watcher that
/// repositions the plate and refreshes scroll bounds on each text change), and
/// <c>MonsterDetailScrollContainerPatch2</c> (P4: stop that watcher on destroy).
/// The original mod shipped a complete translation pack; against a live MT
/// endpoint P1 instead hands the plate a non-Japanese placeholder and P3 starts
/// <see cref="TranslateMonsterLines"/>, which translates every logical line as
/// its own request — XUAT can no longer batch the uncached boss-phase block into
/// one over-long Sugoi job that comes back as a structure-less run-on.</para>
/// </summary>
[HarmonyPatch]
public static class SkillEffectTranslationPatch
{
    /// <summary>One text entry per SURVIVING list entry captured at
    /// <c>Initialize</c> (Phase 1) and consumed at <c>display</c> (Phase 2).
    /// Titles are included so Phase 2 can dequeue exactly one per plate.</summary>
    private static readonly List<string> _stored = new(8);

    /// <summary>Vertical gap inserted between plates while re-stacking.</summary>
    private const float PlateSpacing = 7f;

    /// <summary>Font size forced onto the merged detail label (old-mod recipe).</summary>
    private const int DetailFontSize = 17;

    /// <summary>Labels whose GetInstanceID() is tracked here will be bypassed in TranslateOrQueueWebJobImmediate.</summary>
    private static readonly System.Collections.Generic.HashSet<int> _ignoredLabels = new();

    #region Phase 1 — store & thin (PartsUnitSkillDetailTextController.Initialize)
    [HarmonyPatch(typeof(PartsUnitSkillDetailTextController), "Initialize")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixStoreAndThin(
        Il2CppSystem.Collections.Generic.List<
            Il2CppSystem.ValueTuple<PartsUnitSkillDetailTextPlate.ePlateType, string>> _detailTextList)
    {
        _stored.Clear();
        if (_detailTextList == null || _detailTextList.Count == 0) return;

        try
        {
            // Snapshot once for reading — reading IL2CPP ValueTuple entries is
            // safe; the only safe mutation is RemoveAt, deferred to the end.
            var arr = _detailTextList.ToArray();
            var removeIndices = new List<int>(arr.Length);

            // Faithful port of PriconneTLFixup.SkillTextStorePatch: a running
            // position counter keeps the first two entries of each group as
            // their own plates and merges every later entry into the second
            // one. The counter resets at the literal スキル効果 section header.
            int pos = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                pos++;
                string text = arr[i].Item2 ?? string.Empty;

                if (text == "スキル効果")
                    pos = 1;

                if (pos > 2)
                {
                    // Continuation fragment — merge into the last kept entry,
                    // drop its plate.
                    if (_stored.Count > 0)
                        _stored[_stored.Count - 1] += text;
                    removeIndices.Add(i);
                }
                else
                {
                    // Kept entry — its plate survives; store text 1:1.
                    _stored.Add(text);
                }
            }

            // Remove continuation entries highest-index-first so earlier
            // indices stay valid.
            for (int k = removeIndices.Count - 1; k >= 0; k--)
                _detailTextList.RemoveAt(removeIndices[k]);

            if (FLog.IsDeveloperContext)
            {
                int chars = 0;
                foreach (var s in _stored) chars += s?.Length ?? 0;
                FLog.Info($"[SkillMerge] P1 fired: in={arr.Length} stored={_stored.Count} " +
                          $"removed={removeIndices.Count} chars={chars}");
            }
        }
        catch (Exception ex)
        {
            FLog.Error("[SkillMerge] P1 failed", ex);
        }
    }
    #endregion

    #region Phase 2 — apply & relayout (PartsDialogUnitSkillDetail.display)
    [HarmonyPatch(typeof(PartsDialogUnitSkillDetail), "display")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixApplySkillUi(PartsDialogUnitSkillDetail __instance)
    {
        if (!__instance.IsSafe() || _stored.Count == 0) return;

        var wrap = __instance.transform.Find("ScrollContent/ScrollView/WrapContent");
        if (!wrap.IsSafe())
        {
            if (FLog.IsDeveloperContext)
                FLog.Info("[SkillMerge] P2: WrapContent not found.");
            return;
        }

        try
        {
            int childCount = wrap!.childCount;
            var queue = new Queue<string>(_stored);
            var sb = new StringBuilder(256);
            CustomUILabel? acc = null;   // surviving detail label accumulating a section
            int applied = 0;
            int resolved = 0;

            // ---- Pass 1: merge stored text onto each surviving detail label ----
            for (int i = 0; i < childCount; i++)
            {
                var child = wrap.GetChild(i);
                if (!child.IsSafe()) continue;

                var titleT = child!.Find("TitleLabel");
                bool hasTitle = titleT.IsSafe()
                             && titleT!.gameObject.activeSelf
                             && titleT.GetComponent<CustomUILabel>().IsSafe();

                var detailT = child.Find("DetailLabel");
                CustomUILabel? detailLabel = null;
                if (detailT.IsSafe() && detailT!.gameObject.activeSelf)
                    detailLabel = detailT.GetComponent<CustomUILabel>();
                bool hasDetail = detailLabel.IsSafe();

                // One stored entry is consumed per active plate (title OR
                // detail) so the queue stays aligned 1:1 with the thinned list.
                string? item = null;
                if ((hasTitle || hasDetail) && queue.Count > 0)
                    item = queue.Dequeue();

                if (hasTitle)
                {
                    // Section boundary — flush the previous section's text.
                    if (acc.IsSafe() && sb.Length > 0)
                    {
                        string jp = sb.ToString(), shown = ResolveCached(jp);
                        acc!.text = shown;
                        applied++;
                        if (!ReferenceEquals(shown, jp)) resolved++;
                    }
                    sb.Clear();
                    acc = null;
                }
                else if (hasDetail)
                {
                    if (item == null) continue;   // no stored text — leave as-is
                    if (!acc.IsSafe())
                    {
                        acc = detailLabel;
                        detailLabel!.overflowMethod = UILabel.Overflow.ResizeHeight;
                        detailLabel.pivot           = UIWidget.Pivot.Top;
                        detailLabel.fontSize        = DetailFontSize;
                    }
                    sb.Append(item.Replace("　", string.Empty));
                }
            }
            // Flush the final section.
            if (acc.IsSafe() && sb.Length > 0)
            {
                string jp = sb.ToString(), shown = ResolveCached(jp);
                acc!.text = shown;
                applied++;
                if (!ReferenceEquals(shown, jp)) resolved++;
            }

            // ---- Pass 2: re-stack every active plate top-to-bottom ----
            float y = 0f;
            for (int i = 0; i < childCount; i++)
            {
                var child = wrap.GetChild(i);
                if (!child.IsSafe() || !child!.gameObject.activeSelf) continue;

                var p = child.localPosition;
                child.localPosition = new Vector3(p.x, y, p.z);

                float height = 0f;
                float extra  = 0f;
                CustomUILabel? measure = null;

                var titleT = child.Find("TitleLabel");
                if (titleT.IsSafe() && titleT!.gameObject.activeSelf)
                    measure = titleT.GetComponent<CustomUILabel>();

                if (!measure.IsSafe())
                {
                    var detailT = child.Find("DetailLabel");
                    if (detailT.IsSafe() && detailT!.gameObject.activeSelf)
                    {
                        measure = detailT.GetComponent<CustomUILabel>();
                        if (measure.IsSafe())
                        {
                            var lp = measure!.transform.localPosition;
                            measure.transform.localPosition = new Vector3(lp.x, 12f, lp.z);
                            extra = 15f;
                        }
                    }
                }

                if (measure.IsSafe())
                    height = measure!.localSize.y;

                y -= height + PlateSpacing + extra;
            }

            // ---- Re-enable the scroll view/bar for the now-taller content ----
            var ctrl = __instance.textController;
            if (ctrl.IsSafe())
            {
                var content = ctrl!.curUIScrollContent;
                if (content.IsSafe())
                {
                    if (content!.curUIScrollView.IsSafe()) content.curUIScrollView!.enabled = true;
                    if (content.curUIScrollBar.IsSafe())   content.curUIScrollBar!.enabled  = true;
                }
            }

            if (FLog.IsDeveloperContext)
                FLog.Info($"[SkillMerge] P2 fired: children={childCount} stored={_stored.Count} " +
                          $"applied={applied} cached={resolved} queueLeft={queue.Count}");
        }
        catch (Exception ex)
        {
            FLog.Error("[SkillMerge] P2 failed", ex);
        }
    }
    #endregion

    #region Phase 3 — re-queue empty poll (AutoTranslationPlugin.TranslateOrQueueWebJobImmediate)
    [HarmonyPatch(typeof(AutoTranslationPlugin), "TranslateOrQueueWebJobImmediate")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixRequeueEmptyPoll(
        AutoTranslationPlugin         __instance,
        object                        ui,
        string                        text,
        int                           scope,
        TextTranslationInfo           info,
        bool                          allowStabilizationOnTextComponent,
        bool                          ignoreComponentState,
        bool                          allowStartTranslationImmediate,
        bool                          allowStartTranslationLater,
        IReadOnlyTextTranslationCache tc,
        UntranslatedTextInfo          untranslatedTextContext,
        ParserTranslationContext      context)
    {
        if (ui is UnityEngine.Object obj && _ignoredLabels.Contains(obj.GetInstanceID()))
            return false;

        if (!string.IsNullOrWhiteSpace(text) || (info != null && info.IsCurrentlySettingText))
            return true;

        text = ui.GetText(info);
        if (string.IsNullOrWhiteSpace(text)) return true;

        if (text.Contains('※') || text.Contains('[') || !ContainsJapanese(text))
            return true;

        var probe = new UntranslatedText(text, false, false, true, false, false);
        if (__instance.TextCache.TryGetTranslation(probe, false, false, -1, out _))
            return true;

        var probeRegex = new UntranslatedText(text, false, false, true, true, true);
        if (__instance.TextCache.TryGetTranslation(probeRegex, false, true, -1, out _))
            return true;

        string flat = text.Replace("\n", string.Empty);
        __instance.TranslateOrQueueWebJobImmediate(
            ui, flat, scope, info,
            allowStabilizationOnTextComponent, ignoreComponentState,
            false, false, tc, untranslatedTextContext, context);
            
        return true;
    }
    #endregion

    #region Phase 4 — Monster Details (faithful port of PriconneTLFixup's 4 monster patches)
    /// <summary>Handle of the P3 plate-watcher coroutine for the open Monster
    /// Details dialog; stopped by P4 when the dialog is destroyed.</summary>
    private static UnityEngine.Coroutine? _monsterCoroutine;

    /// <summary>Handle of the P3 self-translate coroutine
    /// (<see cref="TranslateMonsterLines"/>); stopped by P4.</summary>
    private static UnityEngine.Coroutine? _monsterTranslateCoroutine;

    /// <summary>Logical monster-detail lines parked by P1 for P3 to pick up and
    /// self-translate per line; <c>null</c> when no self-translate is pending.</summary>
    private static List<string>? _pendingMonsterLines;

    /// <summary>Placeholder shown in the monster plate while the per-line
    /// self-translation runs. Contains no Japanese, so XUAT leaves it alone and
    /// does not race the coroutine for the label.</summary>
    private const string MonsterLoadingPlaceholder = "Loading...";

    /// <summary>
    /// P1 — port of <c>SkillDescriptionPatch</c>'s fragment-collapse, adapted for
    /// a live-MT endpoint. Three steps:
    /// <list type="bullet">
    /// <item><b>merge</b> — the game display-wraps a logical line across several
    /// fragments, indenting continuations with U+3000; those are joined back so
    /// each translation request is a whole line, not a mid-word fragment (which
    /// came back as garbage such as <c>る。</c> =&gt; "Ru.").</item>
    /// <item><b>long-line split</b> — an unusually long logical line is broken at
    /// its bullets by <see cref="SplitLongMonsterLine"/> so no single request is
    /// long enough for Sugoi to return as a structure-less run-on.</item>
    /// <item><b>placeholder</b> — the merged lines are parked in
    /// <see cref="_pendingMonsterLines"/> and the controller is handed a single
    /// <see cref="MonsterLoadingPlaceholder"/> entry. It still builds exactly one
    /// plate; P3 then self-translates the parked lines line-by-line. Handing XUAT
    /// the raw multi-line Japanese instead let it batch the whole uncached
    /// boss-phase block into one garbled Sugoi job.</item>
    /// </list>
    /// </summary>
    [HarmonyPatch(typeof(PartsMonsterDetailTextController), "Initialize")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixMonsterMerge(
        ref Il2CppSystem.Collections.Generic.List<string> _monsterDetailTextList)
    {
        if (_monsterDetailTextList == null || _monsterDetailTextList.Count == 0) return;

        try
        {
            var parts = _monsterDetailTextList.ToArray();

            // merge — re-join U+3000-indented continuation fragments so the
            // game's mid-word display wrapping is undone.
            var logical = new List<string>(parts.Length);
            var cur = new StringBuilder(160);
            for (int i = 0; i < parts.Length; i++)
            {
                string frag = parts[i] ?? string.Empty;
                if (cur.Length > 0 && frag.Length > 0 && frag[0] == '　')
                {
                    cur.Append(frag.TrimStart('　'));
                }
                else if (frag.Length == 0)
                {
                    // Blank fragment — the source's paragraph break between
                    // 【Phase】 blocks. Flush the current logical line and emit
                    // an empty line so the gap survives the merge.
                    if (cur.Length > 0) { logical.Add(cur.ToString()); cur.Clear(); }
                    logical.Add(string.Empty);
                }
                else
                {
                    if (cur.Length > 0) logical.Add(cur.ToString());
                    cur.Clear();
                    cur.Append(frag);
                }
            }
            if (cur.Length > 0) logical.Add(cur.ToString());

            // long-line split — break only an over-long line at its bullets.
            var lines = new List<string>(logical.Count + 8);
            for (int i = 0; i < logical.Count; i++)
                SplitLongMonsterLine(logical[i], lines);
            if (lines.Count == 0) return;

            // Park the logical lines for P3's per-line self-translate and hand
            // the controller a placeholder — see the method summary.
            _pendingMonsterLines = lines;
            var single = new Il2CppSystem.Collections.Generic.List<string>();
            single.Add(MonsterLoadingPlaceholder);
            _monsterDetailTextList = single;

            if (FLog.IsDeveloperContext)
            {
                FLog.Info($"[SkillMerge] Monster P1: {parts.Length} fragments -> {logical.Count} logical " +
                          $"-> {lines.Count} lines parked for self-translate.");
                // Per-fragment dump (dev only). Reveals whether the source
                // sends "" separators where JP layout shows no gap, and lets
                // us trace any extra blank lines back to a specific fragment
                // or to a translation result.
                for (int i = 0; i < parts.Length; i++)
                {
                    string f = parts[i] ?? string.Empty;
                    FLog.Info($"[SkillMerge]   frag[{i}] len={f.Length}: {f}");
                }
            }
        }
        catch (Exception ex)
        {
            FLog.Error("[SkillMerge] Monster P1 (merge) failed", ex);
        }
    }

    /// <summary>
    /// Appends <paramref name="line"/> to <paramref name="outList"/>. A short
    /// line passes through whole — its XUAT cache key must not change, and it may
    /// hold a name-internal '・' (e.g. 六凶・魔蜘) or an inline term (【蒐輝】,
    /// 【月の祝福】) that must stay intact. An over-long line — the boss
    /// phase-transition block is one ~300-char run-on with many '・' and no '。'
    /// — is broken at each bullet '・' so Sugoi translates each point as a short
    /// unit. '・' is the only split char: '。' is absent from these blocks and
    /// '【' also delimits inline terms.
    /// </summary>
    private static void SplitLongMonsterLine(string line, List<string> outList)
    {
        const int LongLineThreshold = 150;
        if (line.Length <= LongLineThreshold)
        {
            outList.Add(line);
            return;
        }

        var seg = new StringBuilder(line.Length);
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '・' && seg.Length > 0)
            {
                outList.Add(seg.ToString());
                seg.Clear();
            }
            seg.Append(c);
        }
        if (seg.Length > 0) outList.Add(seg.ToString());
    }

    /// <summary>
    /// P2 — port of <c>MonsterDetailScrollContainerPatch</c>. At
    /// <c>PartsDialogMonsterDetail.InitializeParam</c>, re-enables the monster
    /// detail controller's scroll view so the tall single plate can scroll.
    /// </summary>
    [HarmonyPatch(typeof(PartsDialogMonsterDetail), "InitializeParam")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixMonsterScroll(PartsDialogMonsterDetail __instance)
    {
        if (!__instance.IsSafe()) return;

        var ctrl = __instance.monsterDetailTextController;
        if (!ctrl.IsSafe()) return;

        var content = ctrl!.curUIScrollContent;
        if (content.IsSafe() && content!.curUIScrollView.IsSafe())
            content.curUIScrollView!.enabled = true;
    }

    /// <summary>
    /// P3 — port of <c>MonsterDetailOverflowPatch</c>. At
    /// <c>PartsMonsterDetailTextPlate.SetText</c>, switches the detail label to
    /// <c>ResizeHeight</c> overflow and (re)starts the
    /// <see cref="UpdateMonsterDetailPlate"/> watcher.
    /// </summary>
    [HarmonyPatch(typeof(PartsMonsterDetailTextPlate), "SetText")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixMonsterOverflow(PartsMonsterDetailTextPlate __instance)
    {
        if (!__instance.IsSafe()) return;

        try
        {
            var label = __instance.detailText;
            if (label.IsSafe())
                label!.overflowMethod = UILabel.Overflow.ResizeHeight;

            if (_monsterCoroutine != null) CoroutineStarter.Stop(_monsterCoroutine);
            _monsterCoroutine = CoroutineStarter.Run(UpdateMonsterDetailPlate(__instance!));

            // If P1 parked lines for self-translation, start it now against this
            // plate. _pendingMonsterLines is cleared so a repeat SetText cannot
            // launch a second translate over the same plate.
            if (_pendingMonsterLines != null)
            {
                var pending = _pendingMonsterLines;
                _pendingMonsterLines = null;
                if (_monsterTranslateCoroutine != null) CoroutineStarter.Stop(_monsterTranslateCoroutine);
                _monsterTranslateCoroutine = CoroutineStarter.Run(TranslateMonsterLines(__instance!, pending));
            }
        }
        catch (Exception ex)
        {
            FLog.Error("[SkillMerge] Monster P3 (overflow) failed", ex);
        }
    }

    /// <summary>
    /// Watcher coroutine for the monster detail plate. On every label-text change
    /// (i.e. when XUAT swaps in the translation) it waits for NGUI to lay out the
    /// resized label, repositions the plate below its lowest sibling, then presses
    /// the surrounding <see cref="UIDragScrollView"/>s to refresh the scroll
    /// bounds. Loops until the label is destroyed. Faithful port of
    /// <c>MonsterDetailOverflowPatch.UpdateDetailTextPlate</c>.
    /// </summary>
    private static System.Collections.IEnumerator UpdateMonsterDetailPlate(
        PartsMonsterDetailTextPlate textPlate)
    {
        if (!textPlate.IsSafe()) yield break;

        var go     = textPlate!.gameObject;
        var parent = go.transform.parent;

        // Lowest-Y sibling, excluding the plate itself.
        Transform? lowestChild = null;
        if (parent.IsSafe())
        {
            for (int i = 0; i < parent!.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!child.IsSafe() || child == go.transform) continue;
                if (!lowestChild.IsSafe() || child!.position.y < lowestChild!.position.y)
                    lowestChild = child;
            }
        }

        string last = string.Empty;
        while (textPlate.IsSafe() && textPlate!.detailText.IsSafe())
        {
            string now = textPlate.detailText!.text ?? string.Empty;
            if (now != last)
            {
                last = now;
                yield return null;   // let NGUI lay out the resized label

                if (lowestChild.IsSafe())
                {
                    int height = textPlate.detailText!.height;
                    var lp = go.transform.localPosition;
                    go.transform.localPosition =
                        new Vector3(lp.x, lowestChild!.localPosition.y - height / 2f, lp.z);
                }

                yield return null;
                yield return null;
                yield return null;

                PressDragScrollView(textPlate.transform);
                PressDragScrollView(textPlate.transform.IsSafe() ? textPlate.transform!.parent : null);
                var self = textPlate.transform.IsSafe()
                    ? textPlate.transform!.GetComponent<UIDragScrollView>() : null;
                if (self.IsSafe()) self!.CallOnPress();
            }
            yield return null;
        }
    }

    /// <summary>Finds a <see cref="UIDragScrollView"/> under <paramref name="root"/>
    /// and presses it so the scroll view recomputes its content bounds.</summary>
    private static void PressDragScrollView(Transform? root)
    {
        if (!root.IsSafe()) return;
        var drag = root!.GetComponentInChildren<UIDragScrollView>();
        if (drag.IsSafe()) drag!.CallOnPress();
    }

    /// <summary>
    /// P3 self-translate. Each parked logical line is translated as its own
    /// XUAT request, so the endpoint never sees the multi-line Japanese block
    /// it would otherwise batch into one over-long, garbled job. A leading
    /// <c>・</c> is split off before the request so the cache key stays
    /// <c>・</c>-less, matching the shipped translation pack's convention
    /// (<c>全属性…=…</c>, not <c>・全属性…=・…</c>). A <c>►</c> bullet is then
    /// re-attached to the result for display — matching the pack's
    /// <c>\n・=\n►</c> rule, which XUAT runs during lookup but not on text we
    /// set directly on the label. Non-Japanese lines (separators) pass
    /// through untouched. Bracket form (<c>【】</c> vs <c>[]</c>) is left
    /// verbatim — the shipped pack does not lock either, so we match its
    /// pass-through behaviour. When every line is resolved — or a bounded
    /// wait elapses — the assembled text replaces the
    /// <see cref="MonsterLoadingPlaceholder"/> directly on the label, which
    /// the <see cref="UpdateMonsterDetailPlate"/> watcher then lays out.
    /// </summary>
    private static System.Collections.IEnumerator TranslateMonsterLines(
        PartsMonsterDetailTextPlate plate, List<string> lines)
    {
        int count = lines.Count;
        var results = new string[count];
        int remaining = count;

        for (int i = 0; i < count; i++)
        {
            string line = lines[i] ?? string.Empty;

            // Non-Japanese lines (the separator rule) pass through untouched.
            if (!ContainsJapanese(line))
            {
                results[i] = line;
                remaining--;
                continue;
            }

            // Split a leading '・' off the cache key so we never write
            // '・xxx=・eng' entries — the shipped pack stores 'xxx=eng'. A
            // '►' is re-attached to the result for display, matching the
            // shipped pack's '\n・=\n►' substitution that runs during XUAT
            // lookup but not on text we set directly on the label.
            string bullet = (line.Length > 0 && line[0] == '・') ? "►" : string.Empty;
            string toTranslate = line.Substring((line.Length > 0 && line[0] == '・') ? 1 : 0);

            if (toTranslate.Length == 0 || !ContainsJapanese(toTranslate))
            {
                results[i] = bullet + toTranslate;
                remaining--;
                continue;
            }

            int idx = i;
            string lead = bullet;
            string original = line;
            try
            {
                XUnity.AutoTranslator.Plugin.Core.AutoTranslator.Default.TranslateAsync(
                    toTranslate,
                    result =>
                    {
                        // Trim newlines that some endpoints (Sugoi) append, so
                        // a one-line translation does not introduce an extra
                        // blank line when the per-line results are joined.
                        // Brackets and other glyphs pass through verbatim —
                        // the upstream pack does not lock 【】 vs [] either.
                        results[idx] = (result != null && result.Succeeded
                                        && !string.IsNullOrEmpty(result.TranslatedText))
                            ? lead + result.TranslatedText.Trim('\r', '\n')
                            : original;
                        remaining--;
                    });
            }
            catch (Exception ex)
            {
                FLog.Error("[SkillMerge] Monster self-translate request failed", ex);
                results[idx] = original;
                remaining--;
            }
        }

        // Bounded wait — a stalled endpoint must not hang the coroutine forever.
        const int MaxWaitFrames = 1800;
        int waited = 0;
        while (remaining > 0 && waited < MaxWaitFrames)
        {
            waited++;
            yield return null;
        }

        if (remaining > 0)
        {
            for (int i = 0; i < count; i++)
                if (results[i] == null) results[i] = lines[i] ?? string.Empty;
            FLog.Warn($"[SkillMerge] Monster self-translate timed out, {remaining} line(s) unresolved.");
        }

        if (plate.IsSafe() && plate!.detailText.IsSafe())
        {
            plate.detailText!.text = string.Join("\n", results);
            if (FLog.IsDeveloperContext)
                FLog.Info($"[SkillMerge] Monster self-translate applied ({count} lines).");
        }

        _monsterTranslateCoroutine = null;
    }

    /// <summary>
    /// P4 — port of <c>MonsterDetailScrollContainerPatch2</c>. At
    /// <c>PartsDialogMonsterDetail.OnDestroy</c>, stops the P3 watcher and
    /// self-translate coroutines so neither keeps running against a destroyed
    /// plate, and drops any pending self-translate lines.
    /// </summary>
    [HarmonyPatch(typeof(PartsDialogMonsterDetail), "OnDestroy")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixMonsterDestroy()
    {
        if (_monsterCoroutine != null)
        {
            CoroutineStarter.Stop(_monsterCoroutine);
            _monsterCoroutine = null;
        }
        if (_monsterTranslateCoroutine != null)
        {
            CoroutineStarter.Stop(_monsterTranslateCoroutine);
            _monsterTranslateCoroutine = null;
        }
        _pendingMonsterLines = null;
    }
    #endregion
    private static string ResolveCached(string japanese, bool allowRegex = true)
    {
        try
        {
            if (string.IsNullOrEmpty(japanese)) return japanese;
            
            if (XUnity.AutoTranslator.Plugin.Core.AutoTranslator.Default.TryTranslate(japanese, out string translated, out string _))
            {
                if (!string.IsNullOrEmpty(translated) && translated != japanese)
                    return translated;
            }
        }
        catch (Exception ex)
        {
            FLog.Error("[SkillMerge] ResolveCached failed", ex);
        }
        return japanese;
    }

    private static bool ContainsJapanese(string text)
    {
        foreach (char c in text)
        {
            if ((c >= '぀' && c <= 'ヿ') ||
                (c >= '一' && c <= '鿿'))
                return true;
        }
        return false;
    }

}
