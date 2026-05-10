---
name: systematic-debugging
description: Use when encountering any bug, test failure, or unexpected behavior, before proposing fixes
---

## Overview
Random fixes waste time and create new bugs. Quick patches mask underlying issues.

**Core principle:** ALWAYS find root cause before attempting fixes. Symptom fixes are failure.

## The Iron Law
```
NO FIXES WITHOUT ROOT CAUSE INVESTIGATION FIRST
```

## The Four Phases

### Phase 1: Root Cause Investigation
**BEFORE attempting ANY fix:**
1. Read error messages carefully (stack traces, line numbers, error codes)
2. Reproduce consistently
3. Check recent changes (git diff, recent commits, new dependencies)
4. Gather evidence in multi-component systems — add diagnostic instrumentation per boundary
5. Trace data flow — where does bad value originate? Keep tracing up until source found

### Phase 2: Pattern Analysis
1. Find working examples in same codebase
2. Compare against references — read completely
3. Identify differences between working and broken
4. Understand dependencies and assumptions

### Phase 3: Hypothesis and Testing
1. Form single hypothesis: "I think X is root cause because Y"
2. Test minimally — smallest possible change, one variable at a time
3. Verify before continuing — didn't work? Form NEW hypothesis, don't stack fixes

### Phase 4: Implementation
1. Create failing test case first (use test-driven-development skill)
2. Implement single fix addressing root cause
3. Verify fix — test passes, no other tests broken
4. If fix doesn't work: if ≥ 3 fixes failed → STOP, question architecture

## Red Flags — STOP and Follow Process
- "Quick fix for now, investigate later"
- "Just try changing X and see"
- "Add multiple changes, run tests"
- "It's probably X, let me fix that"
- "One more fix attempt" (when already tried 2+)

**ALL = STOP. Return to Phase 1.**

## Quick Reference
| Phase | Success Criteria |
|-------|-----------------|
| 1. Root Cause | Understand WHAT and WHY |
| 2. Pattern | Identify differences |
| 3. Hypothesis | Confirmed or new hypothesis |
| 4. Implementation | Bug resolved, tests pass |
