# Monster Details — Faithful Port Rebuild (Phase 4)

> Spec — 2026-05-18. Status: **iteration 1 (faithful port)**, awaiting in-game test.
> Scope: Monster Details (Phase 4) only. Skill Details (Phase 1/2/3) is NOT touched.

## Context

Monster Details translation went through ~6 rounds of churn across two agents
(per-line plates → one-block → manual regex-engine). Each approach traded one
bug for another and never converged. Decision: discard the churn and rebuild
Phase 4 as a faithful port of the proven original mod (`PriconneTLFixup`), then
iterate from in-game evidence rather than from assumption.

## Iteration 1 — faithful port + 10-standard quality ONLY

Port the original `PriconneTLFixup` mod's 4 monster patches with their mechanism
copied **1:1**. Apply only code-quality upgrades (the project's 10-standard
audit): defensive guards, `[HarmonyWrapSafe]`, `FLog` logging, XML docs,
zero-allocation. **No behavioural additions** — no fragment-merge, no long-line
split, no bullet normalisation. Those are deferred (see below).

### The 4 patches

Source: `_decomp/tlfixup/PriconneTLFixup.decompiled.cs` lines 1104–1213.

| # | Hook | Original mechanism (copied 1:1) | 10-standard additions |
|---|---|---|---|
| P1 | `PartsMonsterDetailTextController.Initialize` — Prefix | Join `_monsterDetailTextList` fragments with `\n` into ONE string; replace the list with that single entry. | `IsSafe`/null guards, `[HarmonyWrapSafe]`, `FLog.Debug`, XML doc, allocation-conscious join. |
| P2 | `PartsDialogMonsterDetail.InitializeParam` — Postfix | Enable `monsterDetailTextController.curUIScrollContent.curUIScrollView`. | Full `IsSafe` guard chain (original would NPE on any null link), `[HarmonyWrapSafe]`, XML doc. |
| P3 | `PartsMonsterDetailTextPlate.SetText` — Postfix | Set `detailText.overflowMethod = ResizeHeight`; stop the previous watch coroutine and start a new one. Coroutine: find the lowest-Y sibling; loop while `detailText` is alive; whenever the label text changes → reposition the plate below the lowest sibling → press the surrounding `UIDragScrollView`s to refresh scroll bounds. | `IsSafe` guards, `[HarmonyWrapSafe]`, `FLog`, XML doc, coroutine-handle field documented. |
| P4 | `PartsDialogMonsterDetail.OnDestroy` — Postfix | Stop the P3 coroutine. | `IsSafe`, `[HarmonyWrapSafe]`, XML doc. |

The mechanism is copied verbatim — no logic changes. Whatever the original did
(including the reposition step, which is likely vestigial now that P1 produces a
single plate) is kept as-is. Behaviour changes only via the deferred items.

## Implementation mechanic

- Rewrite the `Phase 4` region of `Patches/SkillEffectMergePatch.cs` in place:
  remove the current monster code (the regex-engine), write the 4 ported
  patches. The skill `Phase 1/2/3` code in the same file is NOT touched.
- No `git revert` — reverting the file would also revert the skill code, which
  is confirmed working and out of scope.
- `UILayoutPatch.cs` has no monster code (its REGION 7 was already removed).

## Test plan — iteration 1

Build → deploy → restart game → open Monster Details on several bosses. Observe
and report: translates fully or partially? garbled lines? run-on walls? missing
bullets? hang? freeze?

## Deferred — decided ONLY after the iteration-1 in-game test

- **Fragment-merge** (join U+3000-continuation fragments into logical lines
  before the `\n` join). Likely needed for a live-MT endpoint (Sugoi) because
  the game display-wraps mid-word; the original mod avoided this by shipping a
  complete translation pack. **Unconfirmed — test first.**
- **Long-line split** of the boss phase-transition block.
- **Bullet normalisation** → `►`.

Each deferred item is added in a later iteration only if the in-game test shows
it is needed.
