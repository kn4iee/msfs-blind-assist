# A350: what is left for you (2026-11-03)

Everything the plans could do or script is done and pushed to Gus's `feat/inibuilds-a350`. This is what only you can do: a sign-in,
a flight that cannot be scripted safely, a judgement by ear, or a decision. Each item names its plan, so its record says more.

No pull request is open. Plan 21's last two tasks (the regression flight and the pull request) wait for you.

## 1. Sign-ins (yours alone: I never type a credential)

1. **OIS OPTIONS > 3RD PARTY:** your SimBrief ID, your Hoppie logon (or SayIntentions), and Navigraph if it has signed out. Plans 13,
   17 and 04 need them.
2. **A company message (plan 04, COMPANY MSG):** with the datalink signed in, have a telex sent to the aircraft's callsign from another
   Hoppie client (or fly with SayIntentions running). Tell me when it is on the way: I run `A350Probe ecamwatch` beside it to settle
   what raises the COMPANY MSG memo.

## 2. Flights

### 2.1 The SimBrief A-to-B flight (plans 17 and 21: the regression flight)

On the build from `feat/inibuilds-a350` (the all-PRs build), A350-900, cold and dark at a stand:

1. Plan a short SimBrief flight (any pair of airports with an ILS at the destination). OIS FLT OPS: request the plan (the OIS's own
   SimBrief request is plan 13's unproven line), load the loadsheet and the fuel.
2. Fly it end to end with MSFSBA as a pilot would: the First Officer's flows (Shift+C), MFD INIT and F-PLN import (Shift+M, Alt+I),
   T.O PERF on the OIS (Shift+T), take-off with FLEX, the climb, cruise, a STAR and the ILS, autoland or hand flown, the rollout with
   BTV armed from the ND window (Ctrl+Shift+B), taxi to a stand, Securing.
3. Plan 17's ledger lists 23 "to fly" steps (`docs/a350/ledger/17-walkthrough.md`): each should work as written there. Tell me any that
   did not, or send me `%APPDATA%\MSFSBlindAssist\logs` (debug.log, a350_speech.log, a350_display.log) and I go through them.
4. Plan 24's in-flight checks were flown on the third flight of 2026-11-03 (scripted): the ND's symbols, the SND with a navaid tuned and
   WHAT IF in flight. Nothing is left for you there (`docs/a350/evidence/flight-2026-11-03.md`, the third flight).

### 2.2 The released aircraft's ILS flights (plan 18, the shared visual-guidance fix)

On the released MSFSBA (`main`), not the A350 branch:

1. A coupled ILS to a runway with a displaced threshold (OMDB 12L) and one without (VCBI 04), on any released aircraft, visual guidance
   on. Note whether the glidepath tone is centred on the glideslope down to the minimums.
2. On the fixed guidance's build: the OMDB 12L approach again, listening to the tone over the displaced half of the runway.

### 2.3 The other Airbus types (plan 19, before its pull request off `main`)

On each of the FBW A380, the FBW A32NX and the Headwind A330, with the First Officer's automatic flaps on:

1. A managed approach: does a flap step wait while the speed holds at about its target (green dot, S, F)? Note the speeds, the times
   and the heights.
2. On the same approach, a climb with the levers at CL (a level-off, a step climb): is it read as a go-around (the flaps going up)?
3. An aircraft without the symptom gets no change. Then tell me "go" for the pull request (one for the three, no A350 anywhere).

### 2.4 Things a script cannot ask the aircraft for

1. **Windshear and stall (plan 07):** neither the 1.2.8 OIS nor ActiveSky offers them. If you meet either, the PFD's call-out and its
   Ctrl+M row should speak.
2. **RWY TOO SHORT and MAX REVERSE (plan 07):** BTV armed for an exit too short for your weight on a shorter runway than VCBI's; on
   2026-11-03 BTV gave way to BRK MED instead of drawing them.
3. **The automatic emergency descent (plan 16):** only a depressurisation brings it (OIS SITUATIONS, irreversible): your call. The manual
   one was flown three times on 2026-11-03.
4. **NOISE take-off scenario 2 (plan 15):** ACCEL above THR RED: the same FMA reader as scenarios 1 and 3, which were flown (scenario 3,
   the noise N1 of 88 %, on the third flight of 2026-11-03: "NOISE, N1, 88%" said at THR RED, THR DCLB above NOISE END).
5. **A deferred procedure (plan 16):** none of the 1.2.8 OIS failures makes one; if one appears in a flight, the DEFRD page reads it.

## 3. By ear (your judgement)

1. **The OIS window (plan 06, Task 2 Step 3):** MISSION, PERFORMANCE, UTILITIES and OPTIONS through Shift+T: does each page read in an
   order and with words you want? A verdict per page.
2. **BTV on an approach and the taxi (plan 08):** the BTV rollout's call-outs, and the taxi turn lead (1.7 s now, against 1.35 s).
3. **The master caution's aural (plan 16):** cancelled by its key (the keys and lights are proven; the sound is yours to hear).
4. **The trim call-out while the autopilot trims (cross-aircraft):** it says every change as on the FBW A380 (Shift+T toggles it). Keep,
   or quieter with the AP on?

## 4. Decisions

1. **STBY RAD NAV row (plan 10, question 9):** keep it on the RMP panels, or drop it?
2. **MFD pages that read too long (plan 09):** which ones, if any; and whether the F-PLN grid's airway column grows to 7 cells for a
   six-letter SID or STAR (as on the A380).
3. **The SND keys' names (plan 11):** placard words now; rename once their function in flight is known (2.1 step 4 helps).
4. **Plan 01's rulings:** the push-once ruling, and M12 (a design point for the First Officer).
5. **The flight phase table (plan 04):** for window titles, a call-out, or neither.
6. **The charts (plan 13):** the charts are pictures; the AI display read (Gemini or Claude) could describe one on request: want it?
7. **Reduced engine taxi out and in (plan 16):** a First Officer checklist for it, or leave it to you?
8. **The display design question:** keep MSFSBA's own A350 windows, or an Alt+key that opens the display itself to click anything?
9. **A symbol I could not name for sure (plan 24):** four small circles under the ailerons' columns on F/CTL. If you know them, I read
   them. (The ND's circled 1 and 2 are named now: the flap 1 and flap 2 points of the CDA function, settled on the third flight.)

## 5. Things outside the A350

1. **PMDG 737 NAV radios dialog (plan 01):** it speaks `Nav1FreqMHz` in the PC's culture (a comma decimal under de-DE): its own fix.
2. **A flaky test:** `A350FoWritePathTests.A_flipping_button_is_judged_from_what_was_just_commanded` failed once under load and passed
   three times alone: a timing margin to widen.

## 6. The pull request (plan 21, Task 5: only when you say)

1. You say "open it". It goes to `oasis1701/msfs-blind-assist` from `feat/inibuilds-a350`, opening with the disclaimer that credits
   Gus Pacleb (@kn4iee) and Tyler Wood (@Wolfman1360), the plans' records compiled into its description, its "User guide:" line and
   its in-sim test plan.
2. Once the number is known: the A350's changelog fragment, written for a pilot.
3. The last commit holding `docs/a350/` is named in the description, the folder is deleted, and it merges when you say.
