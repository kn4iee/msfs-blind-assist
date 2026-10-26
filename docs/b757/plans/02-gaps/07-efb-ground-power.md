# BlueBird 757 EFB Ground Power Implementation Plan (plan 07, early: essentials)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** From the 757 EFB window (Shift+T) a pilot can connect and disconnect ground power, and hears ground power come and go from any source (BlueBird's automatic GPU, the EFB, a preset).

**Architecture:** BlueBird's tablet is drawn by its WASM: no text can be read from it. The window taps the Ground Services page's GPU tile (measured: it toggles the cart) through the existing EFB client and agent, only after reading `L:BBS_757_GPU_AC_PWR_CONNECTED` and only when it differs from the wish ([B757-2]'s pattern), then judges the result from that variable. A disconnect is refused while external power is on line.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 1 (#11), 4 (R62); `docs/b757.md` "The 757 EFB window"; rule B757-2.

## Global Constraints

- Owned files (then passed to plan 11): `SimConnect/BBS757/Bbs757EfbLayout.cs`, `CoherentBbs757EfbClient.cs`, `Bbs757WorkFolder.cs`, `Bbs757Loadsheet.cs`, `Bbs757LoadWatch.cs`, `Bbs757GroundPower.cs` (new), `Forms/BBS757/BBS757EfbForm*.cs`, `Resources/coherent-bbs757-efb-agent.js`, `Aircraft/BBS757Definition.Efb.cs` (new), `Aircraft/BBS757Definition.LoadWatch.cs`, `MainForm.BBS757.cs` (after plan 01); tests `Bbs757EfbTests.cs`, `Bbs757LoadWatchTests.cs`, `Bbs757LoadsheetTests.cs`, `Bbs757GroundPowerTests.cs` (new).
- Measured (session 8): Home (19,62), Aircraft (320,190), GROUND SERVICES (128,368), GPU tile (1005,231); one tap toggled `BBS_757_GPU_AC_PWR_CONNECTED`, `_CART_VISIBLE`, `_PWR_NOT_IN_USE` and the EXT PWR AVAIL light, a second tap restored them within 4 s.
- External power on line: `L:B757_OVHD_EXT_PWR_BUTTON_ON` 1, read under this plan's own OnRequest key `BBS757_EFB_EXT_PWR_ON` (plan 06 registers the same L:var as an announced row; the two keys never both stream, [VAR-7]), so this plan does not wait for plan 06.
- Variables added: 2 (`Bbs757SharedKeys.GpuConnected`, `BBS757_EFB_EXT_PWR_ON`).

## Review Focus

- The ground power state cannot be read: nothing is tapped and the window says so (Task 1 test).
- A connect while connected never taps (the tile toggles), Task 1 test.
- A disconnect while EXT PWR is on line is refused before any tap (Task 1 test).
- A run that started but did not report finishing is judged from the variable, never run again (Task 2).
- BlueBird's automatic GPU connecting at flight load is the first reading: silent (Task 3 test).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/07-efb-ground-power.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 07`, with the `Plan: 07` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: Ground power logic, pure

**Files:** Create `SimConnect/BBS757/Bbs757GroundPower.cs`; modify `Bbs757EfbLayout.cs`; test `Bbs757GroundPowerTests.cs`.

**Interfaces:**
- `Bbs757EfbLayout`: `public static readonly EfbPoint GpuTile = new(1005, 231);` and `ToggleGroundPower()` = `Home`, `AircraftIcon`, `AircraftMenuGroundServices`, `GpuTile`, each `NavigateDelayMs` (as `ToggleChocks()`).
  ```csharp
  public static class Bbs757GroundPower
  {
      public const string ConnectedVar = "BBS_757_GPU_AC_PWR_CONNECTED";
      public static string? Refusal(bool connect, double? connected, double? extPwrOn);   // null = tap
      public static string Result(bool connect, double? before, double? after);
      public static string? ChangePhrase(double v);   // 1 "Ground power connected", 0 "Ground power disconnected"
      public static DateTime? LastWindowRunUtc { get; set; }   // Task 3's echo stamp
  }
  ```
  `Refusal` (exact): connected unread → "The ground power cannot be read, so nothing was tapped."; connect at 1 → "Ground power is already connected."; disconnect at 0 → "Ground power is already disconnected."; disconnect with `extPwrOn` 1 → "Ground power is on line. Press EXT PWR on the Electrical panel to take it off, with the APU or the engines powering the aircraft, then try again."; disconnect with `extPwrOn` unread → "External power cannot be read, so nothing was tapped." `Result`: wish met → "Ground power connected. Press EXT PWR on the Electrical panel to put it on line." / "Ground power disconnected."; unchanged → "BlueBird did not connect the ground power." / "… disconnect …"; unread → "The ground power cannot be read now."

- [ ] **Steps 1-5** (tests `Refusals`, `Taps_only_when_different`, `Results`, `Sequence_ends_on_the_gpu_tile`); commit `feat(b757): EFB ground power logic`.

### Task 2: Ground power buttons in today's window

**Files:** `Forms/BBS757/BBS757EfbForm.cs`; test `Bbs757EfbTests.cs`.

- "Connect ground power", "Disconnect ground power" after "Set chocks", run exactly as the chocks buttons run (one run at a time, reachability, read `Bbs757SharedKeys.GpuConnected` and `Bbs757SharedKeys.ExtPwrOn` fresh, `Refusal`, the sequence, read every 500 ms for up to 6 s, `Result` spoken and in the status box; a run that did not report finishing is judged from the variable). Stamp `LastWindowRunUtc` before tapping.
- [ ] **Steps 1-5** (tests `Connect_taps_once_and_reports`, `Connect_when_connected_taps_nothing`, `Disconnect_refused_while_on_line`); commit `feat(b757): connect and disconnect ground power from the 757 EFB window`.

### Task 3: Ground power announced and on the Electrical panel

**Files:** Create `Aircraft/BBS757Definition.Efb.cs` (plan 01's `AddEfbVariables`, `AddEfbPanels`, `ProcessEfbUpdate`, `FormatEfbDisplay`); test `Bbs757GroundPowerTests.cs`.

- `Bbs757SharedKeys.GpuConnected` = LVar `BBS_757_GPU_AC_PWR_CONNECTED`, Continuous, `IsAnnounced`, status row "Ground Power" (Connected / Not connected) in the "Electrical" display, a Ctrl+M row. `ProcessEfbUpdate`: `_changes.Observe(key, ChangePhrase(v), NowMs())`, except within 8 s of `LastWindowRunUtc` (the window's own run: rebaseline silently).
- [ ] **Steps 1-5** (tests `Ground_power_row_on_electrical`, `Change_announced_after_silent_first`, `Window_run_not_said_twice`); commit `feat(b757): ground power announced and on the Electrical panel`.

## In-sim checks (owner)

1. At the gate, APU running, EXT PWR not on line: Shift+T, Disconnect ground power → "Ground power disconnected." (EXT PWR AVAIL goes out); Connect ground power → "Ground power connected. Press EXT PWR…"; Connect again → "Ground power is already connected."
2. Press EXT PWR on the Electrical panel (AC from the GPU), then Disconnect ground power → refused.

## Handoff to plan 16

- **Pilot-facing:** "757 EFB window: Connect and Disconnect ground power (it reads BlueBird's ground power first and refuses to disconnect while external power is on line); ground power coming or going is announced and shown on the Electrical panel." Guide: 757 EFB; Ctrl+M list. Changelog `feature`.
- **Hotkey guide:** the 757 EFB (Shift+T) list.
- **Rule change:** extend B757-2 with the GPU tile: "… and the Ground Services GPU tile (1005,231), which toggles `L:BBS_757_GPU_AC_PWR_CONNECTED` (`Bbs757GroundPower.Refusal`)", and its full text with session 8's measurement.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** not started
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
