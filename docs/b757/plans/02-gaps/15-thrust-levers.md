# BlueBird 757 Thrust Levers Implementation Plan (plan 15, later: optional)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Optional and last (ruling R67).** The owner flies with a hardware throttle, which moves BlueBird's levers already. This plan is for pilots who have none: BlueBird ignores the simulator's throttle keys (F1-F4 and the throttle events, measured in session 7), so without hardware a keyboard pilot cannot add taxi thrust or pull the levers to idle by hand. Build it when such a pilot needs it.

**Goal:** A pilot without a hardware throttle can set BlueBird's thrust levers from the keyboard (taxi breakaway, manual thrust, idle), since the 757 ignores the simulator's own throttle keys.

**Architecture:** `L:BBS757_THRUST_LEVER_L` / `_R` (0 forward idle to 100 full) take a direct write and drive the engines (measured: 20 written held, the engine's throttle followed at 15.67 %). A Pedestal "Thrust levers" panel gets a percent field for both levers, Idle and step buttons, and the two levers' positions as status rows. With the autothrottle engaged the A/T moves the levers itself; the panel says so rather than fighting it.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 2, 3.8, 4 (R67); the owner's flights (reverse).

## Global Constraints

- Owned files: `Aircraft/BBS757Definition.Thrust.cs` (new), `Aircraft/BBS757/Bbs757Thrust.cs` (new), `tests/.../Bbs757ThrustTests.cs` (new), `docs/b757/evidence/flight-15.md`.
- Writes: `{p} (>L:BBS757_THRUST_LEVER_L) {p} (>L:BBS757_THRUST_LEVER_R)`, p an integer 0-100, invariant. Never write the `_REVERSER` variables (BlueBird: a direct write does not command reverse).
- A/T engaged = the MCP's THR or SPD light on (`Bbs757McpLights` keys in the cache): refuse with "The autothrottle is moving the thrust levers. Disconnect it first." (A/T Disconnect is on the Control stand panel and in Ctrl+P.)
- Spoken: "Thrust levers 20 percent" from the read-back; when the two differ, "Thrust levers left 20, right 18 percent".
- Variables added: 6 (field, three buttons, two lever rows).

## Review Focus

- A percent typed with a decimal comma under de-DE ("12,5") is 13, written invariant (Task 1 test).
- A hardware throttle moving the levers after the app's write: the status rows show the hardware's values; nothing is announced (the rows are not announced, Task 2).
- The A/T engaged: no write at all (Task 2 test).

---

### Task 1: Thrust logic, pure

**Files:** Create `MSFSBlindAssist/Aircraft/BBS757/Bbs757Thrust.cs`; test `tests/MSFSBlindAssist.Tests/Bbs757ThrustTests.cs`.

**Interfaces:**
```csharp
public static class Bbs757Thrust
{
    public const string LeftVar = "BBS757_THRUST_LEVER_L", RightVar = "BBS757_THRUST_LEVER_R";
    public const string SetKey = "BBS757_THRUST_SET", IdleKey = "BBS757_THRUST_IDLE",
        UpKey = "BBS757_THRUST_UP", DownKey = "BBS757_THRUST_DOWN",
        LeftKey = "BBS757_THRUST_LEFT", RightKey = "BBS757_THRUST_RIGHT";
    public const int Step = 5;
    public static bool TryParseText(string? text, out int percent, out string error);   // 0-100, rounded
    public static string Write(int percent);
    public static int Stepped(double? current, int delta);                               // clamped 0-100; unread → refuse in the caller
    public static string Phrase(double? left, double? right);
}
```
Errors (exact): empty → "Type a thrust lever position from 0 to 100 percent first."; else → "Thrust lever position must be from 0 to 100 percent."

- [ ] **Step 1: Write the failing tests:** parse and round ("12,5" under de-DE → 13), refusals, `Write(20)` exact string, `Stepped` clamps (98 + 5 → 100, 3 − 5 → 0), `Phrase` (equal and differing levers, unread → "Thrust levers unavailable").
- [ ] **Step 2: Run** with `--filter Bbs757ThrustTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(b757): thrust lever logic`.

### Task 2: The Thrust levers panel

**Files:** Modify `MSFSBlindAssist/Aircraft/BBS757Definition.Thrust.cs` (implements plan 01's `AddThrustVariables`, `AddThrustPanels`, `HandleThrustSet`, `HandleThrustTypedText`, `FormatThrustDisplay`, `ResetThrust`); test `Bbs757ThrustTests.cs`.

**Interfaces:**
- Panel "Thrust levers" at the end of the Pedestal section: the field ("Thrust Levers Percent", the squawk field's pattern: a key containing `_SET`, an Event registration never sent, `UnparseableTextAsNaN`), buttons "Thrust Idle", "Thrust Up 5", "Thrust Down 5", and the status rows "Left Thrust Lever", "Right Thrust Lever" (OnRequest, read while shown, not announced, `ExcludeFromMonitorManager`), shown through `FormatThrustDisplay` as "20 percent".
- Each action: `CanWrite` false → "Thrust levers unavailable"; A/T engaged → the refusal; else write, read both levers after `ReadBackDelayMs`, speak `Phrase`.

- [ ] **Step 1: Write the failing tests:** the panel's place and order; `Idle_writes_zero_and_reads_back` (fake io); `Up_steps_from_the_read_value` (read 20 → writes 25); `Autothrottle_engaged_refuses` (THR light 1 → no write).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the full suite.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(b757): thrust levers from the keyboard`.

### Task 3 [in flight]: reverse thrust note

After a landing (owner, in flight), try the simulator's reverser key with the levers at idle and record `BBS757_THRUST_LEVER_L_REVERSER`, `BBS_757_TR_1` and N1 in `docs/b757/evidence/flight-15.md`. If the keys work, the handoff says so for the guide; if they do not, the handoff lists reverse thrust as a known gap. No code.

## In-sim checks (owner)

1. Engines running, parking brake set, A/T off: Thrust levers panel, type 25 → "Thrust levers 25 percent" (EPR/N1 rise on the Engines status display); Thrust Idle → "Thrust levers 0 percent".
2. With THR engaged at the runway: Thrust Up 5 → refused.

## Handoff to plan 16

- **Pilot-facing:** "757: a Thrust levers panel on the Pedestal sets both thrust levers by percent, with Idle and 5 % steps, for pilots without a hardware throttle (the 757 ignores the simulator's throttle keys). It refuses while the autothrottle is moving the levers." Guide: 757 flying notes; hotkey guide's Notes (replace "a hardware throttle moves them"). Changelog `feature`.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Claim the plan by filling in **Claimed by** and
setting **Status** to claimed, and push that one change before starting; if the push stops on a conflict in these
lines, someone claimed it first. Fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** not started (optional)
- **Claimed by:** none
- **Worked by:** (people; add "with subagents" if run through superpowers:subagent-driven-development)
- **Started:** (date; the `B757` commit you branched from)
- **Landed:** (date; the last commit of this plan on `B757`)
- **Tests at landing:** (full suite passed / failed, after the last rebase)
- **Tasks:** (done; left, and why)
- **Waiting on the owner:** ([owner] and [in flight] tasks and in-sim checks not run yet)
- **In-sim results:** (each check: passed or failed, date; or "none run")
- **Changed from the plan:** (a name, a value or a task changed, dropped or added, and why)
- **Rulings made while working:** (question, answer, who decided)
