# Plan 23: the OIS options on and off (A350 1.2.8, 2026-11-03)

The ledger is `docs/a350/ledger/23-options.md`: one line per option, its variable, what it changes, what in MSFSBA it
touches, and both states as read back.

## How

- **Both states, through MSFSBA's own client.** A scratch script (session scratchpad, `opt_toggle.py`) read each OPTIONS
  tab with `A350Probe ois cpt <TAB>` (the agent the OIS window runs), set each select, field, slider and box to another
  value (`NAME=VALUE`, typed as key events [A350-6]), read it back, set the owner's value again and read that back. 91
  options on 11 tabs; every one came back to the owner's value. MASTER VOLUME, at its maximum 100, took 101 as 100, so it
  was stepped to 99 and back by hand. Not toggled: RESET DATA COLLECTION, the throttle calibration buttons, RECOMMENDED
  VALUES (actions, not settings), CABIN LIGHTING (the sim's own), and the 3RD PARTY tab (credentials).
- **The variables.** The OIS script is obfuscated, so each option's variable was measured: a second script
  (`opt_vars.py`) dumped every `INI_` name the OIS script holds (250) through an `eval:` step before the set and on the next
  command after it (seconds later), and kept what changed. Values that drift on their own (the APU's hours, battery
  charge, fuel, hydraulic reservoirs, VAPP) were left out. THR RED writes `INI_ACCEL_HEIGHT_OPTIONS` 4 s after the field
  (a `setTimeout` of 0xfa0 in the script), so its change showed on the next option's row. The volumes, weathering, GPS
  jamming, the deadzone, AUTO TILLER DISCO and DEFAULT PANEL STATE moved no named variable within those seconds: kept in
  the OIS's own storage (`DataStore` `useropt_a350_*`) or written later.
- **What hides controls.** The 1.2.8 behaviour file's option conditions read five variables: the four in `A350Options`
  (fuel jettison, brake fans, the standby navigation display, the HUD) and `INI_OPTION_DISPLAY_OLD`, which swaps the
  displays' bezel models and hides no control. `INI_HUD_COVER_LEFT/RIGHT` (the HUD COVERS option, and the cover rows) is a
  cover's state, read by the HUD's own code, not a visibility condition.

## Findings

- **The HUD's glareshield keys could not be reached** (fixed, 79637e529). The Displays section's "Captain HUD" and "First
  Officer HUD" panels carried the same names as the glareshield panels with the HUD brightness, crosswind and declutter
  keys, and panels are found by name, so the display panels replaced them: six rows gone. Found writing
  `A350OptionsTests.Each_option_not_fitted_is_said_for_each_hidden_control`, whose HUD cases found no rows. The display
  panels are now "Captain HUD display" and "First Officer HUD display", as the FCU's; a test keeps every panel name unique.
- **DISPLAY TYPE's variable is inverted**: NEW writes `INI_OPTION_DISPLAY_OLD` 1, OLD writes 0. With OLD the Captain PFD
  and ND read as with NEW (the draw code is the same); restored to NEW.
- **Every hidden control refuses aloud** while its option is off, whatever its row (a set, a press, a typed value, the
  SND knob's increase and decrease): 21 controls, pinned by `A350OptionsTests`.

## Not done here

- An option that only acts on a new flight (DEFAULT PANEL STATE) was set and read back, not loaded: a flight load is never
  made on the A350 by the session (`msfs_load_flight` wrecks its state); plan 17's flight starts from the owner's state.
