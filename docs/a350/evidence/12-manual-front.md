# Plan 12: the manual's front pages (PDF 1-30)

2026-11-03, 1.2.8. The pages' text from `pdftotext -layout` page by page; the cockpit layout's numbered pictures (PDF 23-25) carry
their legends in the text layer, so no page needed rendering for its items. The ledger is `docs/a350/ledger/12-front.md`.

- Most items are controls a panel sweep already proved (`docs/inibuilds-a350.md` "Panels", 1.2.7), and the 1.2.8 control map
  differs from 1.2.7's only in the oxygen masks (plan 11 Task 1), re-proven on 1.2.8 today; the split panels were re-swept on 1.2.8
  (plan 11 Task 2).
- New "not modelled": the sidestick priority lights. The behaviour file's lamp rules read `INI_CPT_PRIORITY_UPPER_LIGHT` and its
  kin, nothing writes them and the module never names them; only the ANN LT test lights them, which MSFSBA shows.
- The limitations, specifications and concepts pages are reference (ruling: text to read, nothing to operate): the PFD's VFE next is
  read, the VMAX band is not (it is drawn only).
- To fly: the FMA's 10-second box and a reversion's flashing box and triple click (plan 07), the trims moving (plan 11), the SND keys
  with a route (plan 11), ELT EMITTING (plan 11).
- For the owner: load the A350-1000 and the -900ULR once (the sim's menu) and sweep a panel on each; the native EFB's checklists
  (outside the aircraft; the same checklists are the MFD's C/L MENU and the First Officer's).
