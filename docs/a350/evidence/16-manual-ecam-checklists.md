# Plan 16: the manual's ECAM and checklist pages (PDF 81-86, 124-128)

2026-11-03, 1.2.8, on the stand (flight phase 1: powered, engines off). The ledger is `docs/a350/ledger/16-ecam-checklists.md`.

- Sensed failure: OIS FAILURES, PRIM 1 FAULT: Active lit PRIM 1's fault light ("PRIM 1 fault light on" said), but the WD drew no alert
  in 18 s; in phase 1 the aircraft shows none (1.2.7's walk was with the engines running). RESET ALL cleared it ("PRIM 1 fault light
  off"). The ECAM walk goes to plan 17's flight.
- Not sensed: ECP ABN listed every procedure with its colour; the tick on SMOKE / FUMES asked "ACTIVATE ?" with YES... and NO... on
  one row and the cyan cursor around YES; the line read "YES or NO, white (selected)" without the answer, fixed in plan 04's
  reader (`fix(a350): an ECAM choice says which answer the cursor is on`). The tick on YES activated FIRE SMOKE / FUMES: ACTIVATE
  (done), the boxed pilot items "(to do)", the sensed items unboxed, CLEAR, DEACTIVATE (grey); ECP CLEAR cleared it.
- The checklists: the manual's printed lists, the MFD's C/L MENU (read only, ten checklists) and the native `A35K.CHECKLISTS` (1.2.8)
  compared. The First Officer's read-backs equal the native ones line for line on 1.2.8 (BEFORESTART, AFTER_START, BEFORE_TAKEOFF,
  AFTER_TAKEOFF, APPROACH, LANDING, AFTER_LANDING, PARKING, SECURING). The manual's print adds SURV: DEFAULT (BOTH) to BEFORE START
  and two non-normal lists, REDUCED ENGINE TAXI OUT and IN, which neither the aircraft nor the native set holds; whether the First
  Officer should carry them is the owner's. The printed REDUCED ENGINE TAXI OUT runs into AFTER START's lines with no heading
  (PDF 125).
- To fly (plan 17): the sensed walk with the engines running, a deferred procedure, the emergency descent (manual arming).
- The owner's: the automatic emergency descent's arming (a depressurisation: OIS SITUATIONS, irreversible).
