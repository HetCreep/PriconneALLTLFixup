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
/// friends): the boss/enemy <c>Monster Details</c> dialog has its own controller
/// fed by a plain <c>List&lt;string&gt;</c> of pre-wrapped display-line
/// fragments. Phase 4 regroups those fragments into logical lines — one plate
/// per line — lets each plate's label grow to fit, and re-enables the scroll
/// view. One plate per logical line keeps every label short: merging the whole
/// description into a single block made XUAT's regex scan block the main thread
/// for ~3 s (the in-game freeze).</para>
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
    public static void PrefixRequeueEmptyPoll(
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
        if (!string.IsNullOrWhiteSpace(text) || (info != null && info.IsCurrentlySettingText))
            return;

        text = ui.GetText(info);
        if (string.IsNullOrWhiteSpace(text)) return;

        if (text.Contains('※') || text.Contains('[') || !ContainsJapanese(text))
            return;

        var probe = new UntranslatedText(text, false, false, true, false, false);
        if (__instance.TextCache.TryGetTranslation(probe, false, false, -1, out _))
            return;

        var probeRegex = new UntranslatedText(text, false, false, true, true, true);
        if (__instance.TextCache.TryGetTranslation(probeRegex, false, true, -1, out _))
            return;

        string flat = text.Replace("\n", string.Empty);
        __instance.TranslateOrQueueWebJobImmediate(
            ui, flat, scope, info,
            allowStabilizationOnTextComponent, ignoreComponentState,
            false, false, tc, untranslatedTextContext, context);
    }
    #endregion

    #region Phase 4 — Monster Details (PartsMonsterDetail* / PartsDialogMonsterDetail)
    /// <summary>Re-stack coroutine for the open Monster Details dialog;
    /// self-terminates after a short settling window.</summary>
    private static UnityEngine.Coroutine? _monsterCoroutine;

    /// <summary>Vertical gap left between monster plates while re-stacking.</summary>
    private const float MonsterPlateGap = 8f;

    /// <summary>
    /// M1 — at <c>PartsMonsterDetailTextController.Initialize</c>, rebuilds the
    /// per-display-line fragments into one block: groups them into logical
    /// lines, translates each via a DIRECT cache lookup (no regex scan), joins
    /// the results, and registers the finished block in XUAT's cache as a
    /// translation of itself. That registration is the freeze fix — XUAT's
    /// empty-poll re-queue (Phase 3) does a direct cache lookup first, hits, and
    /// SKIPS the synchronous re-queue whose regex scan over the long block froze
    /// the game ~3 s (proven by timing logs: <c>P3 requeue → 3.07 s gap</c>).
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
            var arr = _monsterDetailTextList.ToArray();

            // Group the per-display-line fragments into logical lines
            // (continuation fragments are U+3000-indented).
            var logical = new List<string>(arr.Length);
            var cur = new StringBuilder(160);
            for (int i = 0; i < arr.Length; i++)
            {
                string frag = arr[i] ?? string.Empty;
                if (cur.Length > 0 && frag.Length > 0 && frag[0] == '　')
                {
                    cur.Append(frag.TrimStart('　'));
                }
                else
                {
                    if (cur.Length > 0) logical.Add(cur.ToString());
                    cur.Clear();
                    cur.Append(frag);
                }
            }
            if (cur.Length > 0) logical.Add(cur.ToString());

            // Translate each logical line via a DIRECT cache lookup only. The
            // '・' bullet is stripped for the lookup (BossDesc.txt keys are
            // bullet-less) and re-attached as '►' on a hit. Direct lookups are
            // O(1) hash hits — no regex scan, so this never blocks the thread.
            var sb = new StringBuilder(640);
            int hits = 0;
            for (int i = 0; i < logical.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                string line = logical[i];
                if (line.Length > 0 && line[0] == '・')
                {
                    string bare = line.Substring(1);
                    string en   = ResolveCached(bare, allowRegex: false);
                    if (!ReferenceEquals(en, bare)) { sb.Append('►').Append(en); hits++; }
                    else                              sb.Append(line);
                }
                else
                {
                    string en = ResolveCached(line, allowRegex: false);
                    sb.Append(en);
                    if (!ReferenceEquals(en, line)) hits++;
                }
            }
            string block = sb.ToString();

            // Register the finished block as a translation of itself so XUAT's
            // empty-poll probe (Phase 3, an un-templated DIRECT lookup) hits and
            // skips its synchronous re-queue — that re-queue was the ~3 s freeze.
            RegisterSelfTranslation(block);

            var merged = new Il2CppSystem.Collections.Generic.List<string>();
            merged.Add(block);
            _monsterDetailTextList = merged;

            if (FLog.IsDeveloperContext)
                FLog.Info($"[SkillMerge] Monster: {arr.Length} frags -> {logical.Count} lines, {hits} resolved.");
        }
        catch (Exception ex)
        {
            FLog.Error("[SkillMerge] Monster merge failed", ex);
        }
    }

    /// <summary>
    /// M2 — at <c>PartsMonsterDetailTextPlate.SetText</c>, lets each plate's
    /// detail label grow vertically to fit its logical-line text.
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

            // The controller stacks plates assuming one display line each, so a
            // multi-line logical-line plate overlaps the ones below it. Re-stack
            // every plate by its real (resized) height. Restarted per SetText
            // (debounce); the coroutine self-terminates after a short window.
            var container = __instance.transform.parent;
            if (container.IsSafe())
            {
                if (_monsterCoroutine != null) CoroutineStarter.Stop(_monsterCoroutine);
                _monsterCoroutine = CoroutineStarter.Run(MonsterRestack(container!));
            }
        }
        catch (Exception ex)
        {
            FLog.Error("[SkillMerge] Monster overflow failed", ex);
        }
    }

    /// <summary>
    /// M3 — port of <c>MonsterDetailScrollContainerPatch</c>. Re-enables the
    /// monster dialog's scroll view so the taller merged plate can scroll.
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
    /// Re-stacks every monster plate top-to-bottom by its real (resized) label
    /// height so multi-display-line logical lines no longer overlap. Runs a few
    /// passes over a short settling window, then stops — it does not fight the
    /// scroll controller indefinitely.
    /// </summary>
    private static System.Collections.IEnumerator MonsterRestack(Transform container)
    {
        for (int pass = 0; pass < 8 && container.IsSafe(); pass++)
        {
            float y = 0f;
            bool first = true;
            int n = container!.childCount;
            for (int i = 0; i < n; i++)
            {
                var child = container.GetChild(i);
                if (!child.IsSafe() || !child!.gameObject.activeSelf) continue;
                var lbl = MonsterLabelOf(child);
                if (!lbl.IsSafe()) continue;
                if (first) { y = child.localPosition.y; first = false; }
                var p = child.localPosition;
                child.localPosition = new Vector3(p.x, y, p.z);
                y -= lbl!.localSize.y + MonsterPlateGap;
            }
            for (int f = 0; f < 6 && container.IsSafe(); f++)
                yield return null;
        }
    }

    /// <summary>Returns a monster plate's detail label, or <c>null</c>.</summary>
    private static CustomUILabel? MonsterLabelOf(Transform plate)
    {
        var p = plate.GetComponent<PartsMonsterDetailTextPlate>();
        return p.IsSafe() ? p!.detailText : null;
    }
    #endregion

    /// <summary>
    /// Looks <paramref name="japanese"/> up in XUAT's already-loaded translation
    /// cache and returns the finished translation when one exists, so a caller can
    /// paint the final text immediately instead of waiting for XUAT's async
    /// pipeline. The probe is un-templated so a regex <c>Match.Result</c>
    /// substitution yields clean text. On a miss the Japanese is returned
    /// unchanged so XUAT still translates it the normal (delayed) way.
    ///
    /// <para><paramref name="allowRegex"/> MUST be <c>false</c> for long composite
    /// strings such as the whole Monster Details block. XUAT's regex path scans
    /// every loaded <c>r:"…"</c> template; over a 500-char block that scan blocks
    /// the main thread for seconds (the in-game freeze), and a stray substring
    /// match would return a partial string that corrupts the block. Direct-only
    /// lookups are O(1) hash hits and can only ever return a whole-string match.</para>
    /// </summary>
    private static string ResolveCached(string japanese, bool allowRegex = true)
    {
        try
        {
            var plugin = AutoTranslationPlugin.Current;
            if (plugin == null) return japanese;

            var probe = new UntranslatedText(japanese, false, false, true, false, false);
            if (plugin.TextCache.TryGetTranslation(probe, allowRegex, false, -1, out var translated)
                && !string.IsNullOrEmpty(translated)
                && translated != japanese)
                return translated;
        }
        catch (Exception ex)
        {
            FLog.Error("[SkillMerge] ResolveCached failed", ex);
        }
        return japanese;
    }

    /// <summary>
    /// Registers <paramref name="text"/> in XUAT's translation cache as a
    /// translation of itself. XUAT's empty-poll re-queue (Phase 3) does an
    /// un-templated DIRECT cache lookup before re-queuing; a hit there makes it
    /// skip the synchronous re-queue whose regex scan over the long Monster
    /// Details block froze the game ~3 s.
    /// </summary>
    private static void RegisterSelfTranslation(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            var plugin = AutoTranslationPlugin.Current;
            plugin?.TextCache?.AddTranslationToCache(
                text, text, persistToDisk: false, TranslationType.Full, -1);
        }
        catch (Exception ex)
        {
            FLog.Error("[SkillMerge] RegisterSelfTranslation failed", ex);
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="text"/> contains any character
    /// in the Japanese/CJK Unicode blocks. Replaces the original mod's
    /// ASCII-only <c>IsEnglish()</c> check so the patch behaves correctly for
    /// every target locale.
    /// </summary>
    private static bool ContainsJapanese(string text)
    {
        foreach (char c in text)
        {
            if ((c >= '぀' && c <= 'ヿ') ||   // Hiragana + Katakana
                (c >= '一' && c <= '鿿'))      // CJK Unified Ideographs
                return true;
        }
        return false;
    }
}
