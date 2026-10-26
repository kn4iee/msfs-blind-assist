---
paths:
  - "tools/a350-probe/**"
---
# iniBuilds A350 rules

Loaded when Claude reads matching code. Background: docs/inibuilds-a350.md. Full text of each rule: docs/invariants/inibuilds-a350.md.

- [A350-1] Never write into the sim's memory: read the A350's displays from a read-only copy run through its own draw code, and operate them only through the sim's inputs (L:var writes, input events, the Coherent WASM_MOUSE_* route). Full: docs/invariants/inibuilds-a350.md#a350-1
- [A350-2] Copy the A350's memory by listing only its own allocation's regions, then reading them back-to-back into one buffer (about 8 ms); walking the whole process while reading tears the copy across frames. Full: docs/invariants/inibuilds-a350.md#a350-2
- [A350-3] Press an ECP or KCCU key the aircraft resets itself with one native SimConnect write of 1; two calculator writes of 1 then 0 land in one frame and do nothing. Full: docs/invariants/inibuilds-a350.md#a350-3
- [A350-4] The ND takes mouse input only while the KCCU cursor screen is 1 and the MFD only while it is 2: move the cursor with the KCCU ND or MFD key before a Coherent mouse event. Full: docs/invariants/inibuilds-a350.md#a350-4
- [A350-5] Tooltip position strings are numbered from 1 (SIGNS_SEAT_BELTS.1 is value 0); map placard names by that offset. Full: docs/invariants/inibuilds-a350.md#a350-5
