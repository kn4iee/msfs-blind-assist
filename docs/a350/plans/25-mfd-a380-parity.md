# A350 MFD at A380 Parity Implementation Plan (plan 25)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The A350's MFD (its window, its message call-outs and its Displays entries) is said exactly as the FlyByWire A380's MFD window says its own: no colour words, the A380's role words and keys (owner, 2026-11-12: "full parity with the A380 on how things are announced and all that ... just the MFD").

**Architecture:** `A350MfdPage.Read` keeps producing its lines with colours (its own logic and tests read them: a grey key, a box-less message). A new `A350MfdWording` turns a line into what the pilot hears, colours into the owner's words or nothing; every place an MFD text reaches the pilot speaks through it, and `A350MfdPage.Shown` builds the A380 window's line on it. The window takes the A380 window's list and keys; pure helpers carry each decision so it is tested offline.

**Tech Stack:** C# 13 / .NET 10, WinForms, xUnit; `tools/A350Probe mfdlines` over the capture corpora.

**Spec:** the owner's words of 2026-11-12 (below); the A380 window as built, `Forms/FBWA380/FBWA380MCDUForm.cs` (its list, `RoleWord`, `PerformClear`, `DisplayKeyDown`, `FormKeyDown`, `ApplyScreenMeta`); the colour inventory of 222 captured MFD pages (the 88 fixtures and three flight corpora, 2026-11-12, the table in Task 1).

- "the colours would have to be removed completely from the MFD, and it would have to have full parity with the A380 on how things are announced and all that"
- "instead of must enter colour then required might be a good word to put in there"
- "for control plus up and down, keep them as they are, prev and next page you can make pageup and pagedown"
- Agreed in the same exchange: yellow (the temporary flight plan) "temporary"; magenta (a constraint) "constraint"; cyan, green and white nothing.

## Global Constraints

- The index's "Shared conventions" apply; [A350-1] (never write the sim's memory) above all.
- The MFD only: the PFD, ND, E/WD, SD, HUD, FCU, RMP and standby displays keep their colours (plan 24's "every item said carries its colour" stands for them).
- Colour words never reach the pilot from the MFD: amber on a field "required" (never doubled when the value already says it), yellow "temporary" (never beside the word TMPY), magenta "constraint", every other colour nothing; a grey control "dimmed".
- Ctrl+Up and Ctrl+Down stay the KCCU's arrows; PageUp and PageDown are previous and next page.
- Offline only (no sim this plan): a key sequence the window sends that no live run has pressed (a field cleared, OIS on Alt+W) goes on `docs/a350/owner-steps.md` for one live check.
- Every commit carries `Plan: 25` and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; pushed to `gus` as a plain fast-forward, never a rebase.

## Review Focus

- A value or label holding a colour's name in capitals or as a word of its own ("Green dot", a GREEN waypoint name): said untouched (Task 1 test).
- A torn read or a message read without its box (", black"): still no read, as before (Task 5 test: comparisons stay on the raw line).
- The first officer's MFD (side 1): every rule holds there too (Task 1's sweep reads both sides of every capture).
- A page with no title line (a dark, half-drawn or pop-up-only page): the list and the selection map without one (Task 3 test).
- A scratchpad box holding only spaces: empty for Enter and Backspace alike (Task 4 test).

---

### Task 0 [controller]: Claim this plan

As every plan's Task 0: check the remote, set the record below, commit `docs(a350): claim plan 25` with the `Plan: 25` trailer and push before Task 1.

### Task 1: `A350MfdWording`, the colours into the owner's words

**Files:**
- Create: `MSFSBlindAssist/Aircraft/A350/Display/A350MfdWording.cs`
- Test: `tests/MSFSBlindAssist.Tests/A350/A350MfdWordingTests.cs`

**Interfaces:**
- Produces: `public static class A350MfdWording` with `public static string Speak(MfdLine line)` (the line's text with its colours turned into words, no role word) and `public static string Speak(string kind, string spoken)`; `public static bool IsDimmed(MfdLine line)` (grey, or the reader's ", unavailable"); `public static string Message(string pad)` (an FMS message as the A380 says its footer: the text alone, no "Scratchpad: ", no colour).

The inventory (kind, colour, how it is written by the reader), and what each becomes:

| Reader writes | Example | Becomes |
|---|---|---|
| `, amber` on a field | `CI: required, amber`, `ETA: none, amber` | `CI: required`, `ETA: none, required` |
| `, amber` on any other kind | `ACTIVATE * APPR button, amber`, `Scratchpad: CHECK DATABASE CYCLE, amber` | the colour dropped |
| `, yellow` | `VIA: P570, yellow`, `DIRECT check box, checked, yellow`, F-PLN rows | `, temporary` |
| `, yellow` with TMPY in the line | `ACTIVE/F-PLN/AIRWAYS; TMPY, yellow`, `TMPY F-PLN button, yellow` | the colour dropped |
| `, magenta` | `UTC none, EFOB none T, magenta` | `, constraint` |
| a note `NAME magenta` | `..., cyan, overfly, ALT magenta` | `ALT constraint` |
| a note `NAME white` (any colour but magenta) | `..., ident white`, `FPA white` | the note dropped |
| ` (colour)` after a value | `LOCAL ETA AT DEST (white)` | dropped |
| `, white/green/cyan/grey/black/red/blue` | `FUEL PENALTY: +000.0 %, cyan`, `Notepad: HELLO WORLD, cyan, menu open` | dropped (`Notepad: HELLO WORLD, menu open`) |

- [ ] **Step 1: Write the failing tests** (`A350MfdWordingTests`): one `[Theory]` row per table line, `Assert.Equal(expected, A350MfdWording.Speak(kind, spoken))`, with the examples and results above verbatim, plus:
  - `Assert.Equal("Green dot: 190", A350MfdWording.Speak("field", "Green dot: 190, green"))` and a waypoint named in capitals keeps its name;
  - `Assert.Equal("DER22   BASU1D 034° 1, temporary, overfly", ...)` from `"DER22   BASU1D 034° 1, yellow, overfly, ident white"`;
  - `Assert.Equal("CHECK DATABASE CYCLE", A350MfdWording.Message("Scratchpad: CHECK DATABASE CYCLE, amber"))`;
  - `IsDimmed` true for `DELETE ALL * button, grey` and `X check box, not checked, unavailable`, false for `ACTIVATE * APPR button, amber`;
  - `Every_captured_mfd_line_is_said_without_a_colour`: every MFD fixture (`A350Fixtures`, the names holding `mfd` or `MFD`), both sides, every line: `Speak` holds no word of `\b(white|green|cyan|amber|magenta|yellow|grey|black|red|blue)\b`, and the sweep read at least 1,000 lines (else it proves nothing).
- [ ] **Step 2: Run them; they fail** (`A350MfdWording` does not exist).
  Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~A350MfdWordingTests"`
- [ ] **Step 3: Implement `A350MfdWording`**: one compiled regex per form in the table, lowercase colour names only (the reader writes them so; a label's "Green" is never one), applied note forms first, then segments, then a clean-up of doubled commas.
- [ ] **Step 4: Run them; they pass.**
- [ ] **Step 5: Commit** `feat(a350): the MFD's colours into the owner's words (plan 25)`.

### Task 2: `A350MfdPage.Shown`, the A380 window's line

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/A350/Display/A350MfdPage.cs` (`Shown`, its regexes)
- Test: the tests asserting `Shown` today (`grep -l "A350MfdPage.Shown" tests/`), updated, and new rows in `A350MfdRolesTests`

**Interfaces:**
- Consumes: `A350MfdWording.Speak`, `A350MfdWording.IsDimmed`.
- Produces: `A350MfdPage.Shown(MfdLine)` in the A380 window's form `<text>, <role>[, state][, dimmed]` (`RoleWord` and `RefreshDisplay` of the A380 window).

The forms (spoken by the reader → shown):

| Kind | Reader | Shown |
|---|---|---|
| field, scratchpad | `CI: required, amber` | `CI: required, edit` |
| button, menuitem | `INSERT * TMPY button, amber` | `INSERT * TMPY, button` |
| button, grey | `DELETE ALL * button, grey` | `DELETE ALL *, button, dimmed` |
| opener | `FMS 1 menu, cyan` | `FMS 1, combo box, collapsed` (`expanded` while its list is open) |
| tab | `F-PLN tab, selected` | `F-PLN, tab, selected` |
| checkbox | `FULL check box, checked, cyan` | `FULL, check box, checked` |
| checkbox, grey | `X check box, not checked, unavailable` | `X, check box, not checked, dimmed` |
| option | `ADS-C: OFF, selected, green` | `ADS-C: OFF, radio button, selected` |
| option, unselected | `ADS-C: ON` | `ADS-C: ON, radio button` |
| waypoint | `VCBI22 ..., green` | `VCBI22 ..., button` |
| text, title, message | as spoken | `Speak` alone |

- [ ] **Step 1: Write the failing tests**: a `[Theory]` over the table (`new MfdLine(kind, spoken, 0, 0, Opener: ...)` → `Assert.Equal(shown, A350MfdPage.Shown(line))`), and the highlighted list row keeps `, highlighted` after its role.
- [ ] **Step 2: Run them; they fail on the colours and the role forms.**
- [ ] **Step 3: Rebuild `Shown` on `Speak`**: the reader's own role word at the end of the text ("button", "menu", "tab", "check box") moves behind a comma as the A380's; `IsDimmed` gives ", dimmed".
- [ ] **Step 4: Update every older `Shown` expectation to the new form; run the A350 tests; all pass.**
  Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~A350"`
- [ ] **Step 5: Commit** `feat(a350): the MFD window's lines in the A380 window's words (plan 25)`.

### Task 3: The window's list, title and page keys

**Files:**
- Modify: `MSFSBlindAssist/Forms/A350/A350MfdWindow.cs`
- Test: `tests/MSFSBlindAssist.Tests/A350/A350MfdWindowListTests.cs`

**Interfaces:**
- Produces: `internal static (IReadOnlyList<MfdLine> Lines, IReadOnlyList<MfdLine> Listed, IReadOnlyList<string> Shown) A350MfdWindow.PageLines(A350DrawCapture? cap, int side, bool dark, IReadOnlyList<MfdLine>? before = null)` (`Listed`: the lines without the title, one per `Shown` row); `internal static int? A350MfdWindow.ListedIndex(IReadOnlyList<MfdLine> lines, int? lineIndex)` (a line's row in the list, or null for the title).

- [ ] **Step 1: Write the failing tests**:
  - on `mfd_active_menu_open` (and a fixture with no title line, Review Focus 4): `Shown.Count == Listed.Count`, no `Listed` line has kind `title`, `Shown` holds no colour word;
  - `ListedIndex`: an index after the title is one less, before it unchanged, the title itself null, and with no title unchanged.
- [ ] **Step 2: Run them; they fail** (`PageLines` returns two lists).
- [ ] **Step 3: Implement**, and in `RefreshAsync`: the list shows `Shown`; Enter acts on `Listed[i]`; a new page says `A350MfdWording.Speak("title", title)` once and takes the selection to row 0 (the A380's `_resetSelection`); a menu that just opened takes it to `ListedIndex(...)`; PageUp and PageDown with no list open say "Previous page" and "Next page" (the A380's words) as they scroll; the list's description: "Enter acts on the line; a field takes the scratchpad. Backspace clears, Delete clears a field, Control plus an arrow moves the KCCU cursor, PageUp and PageDown previous and next page, F5 refreshes, Alt plus a letter presses a page key, Alt plus S the scratchpad."
- [ ] **Step 4: Run the A350 tests; all pass.**
- [ ] **Step 5: Commit** `feat(a350): the MFD window's list and title as the A380's (plan 25)`.

### Task 4: The window's keys: Enter, Backspace, Delete, Escape, Alt+S

**Files:**
- Create: `MSFSBlindAssist/Aircraft/A350/Display/A350MfdClear.cs`
- Modify: `MSFSBlindAssist/Aircraft/A350/Display/A350MfdInput.cs`, `MSFSBlindAssist/Forms/A350/A350MfdWindow.cs`
- Test: `tests/MSFSBlindAssist.Tests/A350/A350MfdClearTests.cs`, `tests/MSFSBlindAssist.Tests/A350/A350MfdInputTests.cs`

**Interfaces:**
- Produces: `public enum MfdClearAction { ClearScratchpadBox, ClearMessage, ClearField, CannotBeCleared, Unavailable, KccuClr, Nothing }`; `public static (MfdClearAction Action, string? Say) A350MfdClear.Decide(string scratchpadBox, bool messageShown, MfdLine? selected, bool delete)`; `public Task<MfdAction> A350MfdInput.ClearAsync(int side, float x, float y, CancellationToken ct = default)`.

The A380's `PerformClear` and Delete, on the A350's own keys (a field is cleared with CLR then ENTER in it, as the ND's BTV EXIT AT field, plan 08 evidence; the FMS message with the KCCU's CLR, `docs/inibuilds-a350.md`):

| Key | Box has text | Message shown | Selected line | Action, said |
|---|---|---|---|---|
| Backspace | yes | any | any | box emptied, "Scratchpad cleared" |
| Backspace | no | yes | any | KCCU CLR, "Info cleared" |
| Backspace / Delete | no | no (Delete: any) | a field, not dimmed | click, CLR, ENTER, "Cleared" |
| Backspace / Delete | no | no | a field, dimmed | nothing, "Unavailable" |
| Backspace / Delete | no | no | not a field | nothing, "Cannot be cleared" |
| Backspace | no | no | none | KCCU CLR, nothing said |
| Delete | any | any | none | nothing |

- [ ] **Step 1: Write the failing tests**: `Decide` over the table (a box of spaces is empty, Review Focus 5); `ClearAsync` with the internal constructor's fake click: the cursor key, the click, then `1 (>L:INI_MCDU1_CLR)` and `1 (>L:INI_MCDU1_ENTER)` in that order, and Busy while another action runs; `A350MfdWindow.PageKeys` has no `Keys.S` and maps `Keys.W` to `OIS`.
- [ ] **Step 2: Run them; they fail.**
- [ ] **Step 3: Implement** `Decide` and `ClearAsync`; in the window: Backspace and Delete through `Decide`; Enter on a dimmed line says "Unavailable" and presses nothing; a typed entry is said back as it is sent (the A380's `_announcer.Announce(text)`); Escape in the scratchpad box with text empties it ("Scratchpad cleared"), else closes; Alt+S focuses the box; OIS moves to Alt+W.
- [ ] **Step 4: Run the A350 tests; all pass.**
- [ ] **Step 5: Commit** `feat(a350): the MFD window's keys as the A380's (plan 25)`.

### Task 5: The message call-outs and the Displays entries

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/A350/Display/A350PfdMonitor.cs` (where `ObserveMessages` speaks), `MSFSBlindAssist/Forms/A350/A350MfdWindow.cs` (its message), `MSFSBlindAssist/Aircraft/IniBuildsA350Definition.Displays.cs` (Captain MFD, First Officer MFD)
- Test: the existing FMS message tests (`grep -ln "Scratchpad: " tests/MSFSBlindAssist.Tests/A350`), and `A350MfdWordingTests`

- [ ] **Step 1: Write the failing tests**: the background FMS message call-out of `CHECK DATABASE CYCLE` (amber) says `CHECK DATABASE CYCLE`; `IniBuildsA350Definition.DisplayPanels` "Captain MFD" read of an MFD fixture holds no colour word; `A350FmsMessageAnnouncer.Observe` still refuses a `", black"` read (Review Focus 2).
- [ ] **Step 2: Run them; they fail.**
- [ ] **Step 3: Implement**: compare and keep the raw lines as today; speak `A350MfdWording.Message(...)`; the two Displays entries read `A350MfdWording.Speak`.
- [ ] **Step 4: Run the A350 tests; all pass.**
- [ ] **Step 5: Commit** `feat(a350): the MFD's messages and display reads without colours (plan 25)`.

### Task 6: Rule, docs, guides, owner steps

**Files:** `.claude/rules/inibuilds-a350.md`, `docs/invariants/inibuilds-a350.md` ([A350-16]); `docs/inibuilds-a350.md` (the MFD window); `guide/airbus-aircraft.md` (the A350's MFD window); `MSFSBlindAssist/HotkeyGuides/iniBuilds_A350_Hotkeys.txt`, `guide/hotkey-reference.md` (its keys); `docs/a350/owner-steps.md` (the live check); this plan's record; `docs/a350/plans/00-index.md` (row 25).

- [ ] **Step 1:** [A350-16], at most 400 characters: an MFD text reaches the pilot only through `A350MfdWording` (`Shown`, `Message`): no colour word; amber field "required", yellow "temporary", magenta "constraint"; `A350MfdPage.Read`'s colours stay for the reader's own logic. Its full text with this plan's inventory and the owner's words.
- [ ] **Step 2:** The docs and guides: what the window says and its keys (Alt+S the scratchpad, Alt+W OIS, Backspace and Delete as the A380's, PageUp and PageDown previous and next page).
- [ ] **Step 3:** Owner steps: one live check: Delete and Backspace on a filled field clear it; Alt+W opens OIS; the window says no colour anywhere.
- [ ] **Step 4:** Run the suite with `ClaudeRulesHookTests` and `ClaudeContextBudgetTests`; all pass.
- [ ] **Step 5: Commit** `docs(a350): the MFD at A380 parity (plan 25)`.

### Task 7: The whole plan reviewed

A fresh reviewer on the most capable model reads the plan's range; Critical and Important findings fixed test-first; the corpora read again (`A350Probe mfdlines` over every MFD capture, today's `Shown`) with no colour word; the full suite; push.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** landed
- **Claimed by:** Francesco Tissera, with Claude, 2026-11-12
- **Worked by:** Claude (Opus 5.5), inline (superpowers:executing-plans)
- **Started:** 2026-11-12, from `98e1fa23d`
- **Landed:** 2026-11-13, offline; final review (a fresh reviewer): no critical finding, five important ones fixed test-first
- **Tests at landing:** the suite without `ClaudeRulesHookTests` 22,136/22,136; `ClaudeRulesHookTests` 203/203
- **Tasks:** 1 `4875c28dd` (`A350MfdWording`), 2 `3679521` (`Shown` in the A380 window's words), 3 `3a56b61` (the list without the
  title, the caption, "Previous page"/"Next page"), 4 `2b51467` (Backspace, Delete, Enter, Escape, Alt+S, Alt+W), 5 `a9babc8` (the
  messages and the status display uncoloured), the field clear corrected `c51ac1f68`, 6 the rule [A350-16] and the docs
- **Waiting on the owner:** the live check, `owner-steps.md` 2.4 item 4 (Delete and Backspace on a filled field, Alt+W, no colour)
- **In-sim results:** none: built offline, as the owner asked
- **Changed from the plan:** a field is cleared with the word CLR typed and ENTER, not the KCCU's CLR key and ENTER (the KCCU's CLR only
  empties an entry being typed, live 2026-10-30, `831860a18`); the messages' spoken words are made in the two message observers, whose
  returns are only spoken; the title also stands in the window's caption; a grey field is never refused (grey is a colour read, not
  the A380's disabled flag: Enter and Delete act and the FMS says if it refuses), so `MfdClearAction.Unavailable` is gone; Backspace and
  Delete say the MFD is busy rather than nothing; a title-only page lists "No lines on this page."
- **Rulings made while working:** the ATC mailbox window is no MFD page and keeps its words; Backspace with nothing to clear presses the
  KCCU's CLR silently (the window's old key, not the A380's BACKSPACE); the scratchpad line itself "Cannot be cleared"; a typed entry is
  said back once the MFD took it; the titled-page test uses the TMPY F-PLN (ACTIVE's open menu covers the title bar)
