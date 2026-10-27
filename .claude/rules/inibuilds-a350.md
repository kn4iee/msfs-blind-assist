---
paths:
  - "tools/a350-probe/**"
  - "tools/a350-gen/**"
  - "tools/A350Probe/**"
  - "MSFSBlindAssist/Aircraft/A350/**"
  - "MSFSBlindAssist/Aircraft/IniBuildsA350*.cs"
  - "tests/MSFSBlindAssist.Tests/A350/**"
  - "MSFSBlindAssist/Resources/coherent-a350-ois-agent.js"
---
# iniBuilds A350 rules

Loaded when Claude reads matching code. Background: docs/inibuilds-a350.md. Full text of each rule: docs/invariants/inibuilds-a350.md.

- [A350-1] Never write into the sim's memory: read the A350's displays from a read-only copy run through its own draw code, and operate them only through the sim's inputs (L:var writes, input events, the Coherent WASM_MOUSE_* route). Full: docs/invariants/inibuilds-a350.md#a350-1
- [A350-2] Copy the A350's memory by listing only its own allocation's regions, then reading them back-to-back into one buffer (about 8 ms); walking the whole process while reading tears the copy across frames. Full: docs/invariants/inibuilds-a350.md#a350-2
- [A350-3] Press an ECP or KCCU key the aircraft resets itself with one native SimConnect write of 1; two calculator writes of 1 then 0 land in one frame and do nothing. Full: docs/invariants/inibuilds-a350.md#a350-3
- [A350-4] The ND takes mouse input only while the KCCU cursor screen is 1 and the MFD only while it is 2: move the cursor with the KCCU ND or MFD key before a Coherent mouse event. Full: docs/invariants/inibuilds-a350.md#a350-4
- [A350-5] Name a control's positions from its own tooltip expression (which value selects which INI.TOOLTIPS key), never from an assumed numbering: ANN LT counts .1 TEST from value 0, BAT SELECTOR counts .0 EMER 1 from value 0. Full: docs/invariants/inibuilds-a350.md#a350-5
- [A350-6] Type into an OIS field as key events (keydown, keypress, input, keyup per character, then Enter); a bare value set plus input/change shows in the field but COMPUTE ignores it. Full: docs/invariants/inibuilds-a350.md#a350-6
- [A350-7] A key whose input event only animates it while its click code flips the state (the HUD declutter keys) is a selector on that state, written as the click code writes it (`ClickWrites`); its `_Set` moves nothing, so never take a `_Set` that animates for a working press. Full: docs/invariants/inibuilds-a350.md#a350-7
