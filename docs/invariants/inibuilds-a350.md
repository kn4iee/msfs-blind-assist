# iniBuilds A350 — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/inibuilds-a350.md`, which Claude Code loads when it reads matching code. Background: [inibuilds-a350.md](../inibuilds-a350.md).

## A350-1

The A350's WASM displays are read from a read-only copy of its linear memory (`OpenProcess` with `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ` only), run through the A350's own `Draw()` in a WASM runtime. They are operated only through the sim's own inputs: L:var writes, input events, and `Coherent.call("WASM_MOUSE_MOVE" | "WASM_MOUSE_DOWN" | "WASM_MOUSE_UP" | "WASM_CLICK", guid, x, y, button)` evaluated in the gauge's Coherent view. The 1.2.6 investigation moved the MFD pointer by writing the aircraft's `RequestMouseChange` slots into the sim's memory; on 1.2.7 the Coherent mouse route moves it exactly (measured 26 October 2026), so no write into another process's memory is needed, and a wrong address there could crash the sim.

## A350-2

Measured 26 October 2026: walking all of the sim's ~25,000 memory regions with `VirtualQueryEx` while reading the A350's (0.5 s) produced torn copies: about half the Warning Display draws came back empty or garbled, and one trapped inside `dlmalloc`. Listing only the A350 allocation's regions first (walk from its base while `AllocationBase` matches, about 10 regions) and then reading them into one preallocated buffer took 8 ms for 37.7 MB and gave 8 of 8 consistent draws. A trap during a draw still means a torn copy: take another, never report it.

## A350-3

The ECP keys (`INI_ECAM_CHECKLIST_LEFT`, `PUSH_ECAM_STS`, `INI_ECAM_ABN`, the `INI_ECAM_WHEEL_*_COMMAND` pair, `INI_MASTER_CAUTION_COMMAND`, …) are consumed and reset to 0 by the aircraft. One native SimConnect write of 1 is a press. Two MobiFlight calculator writes (`1 (>L:X)` then `0 (>L:X)`) executed before the aircraft's next update, so the aircraft saw 0 and nothing happened (measured with STS and ABN). KCCU keys that the aircraft does not reset (`INI_MCDU1_ND`, `INI_MCDU1_MFD`) take 1, a hold of about 180 ms, then 0.

## A350-4

`ND::MouseEvent` returns unless `INI_CPT_CURSOR_SCREEN` (first officer: `INI_FO_CURSOR_SCREEN`) is 1; the MFD follows the mouse while it is 2. The 1.2.6 investigation concluded the Coherent mouse calls "do not reach the A350 gauges" because the cursor was on another screen. Measured 26 October 2026: with the cursor on the ND, `WASM_MOUSE_MOVE` set the captain EFIS gauge's mouse position, and a click on the aircraft symbol opened the ND pop-up.

## A350-5

In `en-US.locPak`, a control's position names are `INI.TOOLTIPS.<CONTROL>.<key>` and the key a value selects is written in that control's tooltip expression (`tt_value` in the behaviour file), not by a fixed rule. Measured 26 October 2026: `SIGNS_SEAT_BELTS` and `LIGHTS_INT_ANN_LT` count from `.1` (value 0 is `.1` ON / `.1` TEST), `MAINT_ELEC_BAT_VOLTAGE_SELECTOR` from `.0` (value 0 is `.0` EMER 1, 2 is `.2` OFF, 3 is `.3` BAT 1). Two-position push buttons use `.OFF` / `.ON`; the placard name is `.TITLE`. Read the expression per control; an assumed offset names every position of half the controls wrong.

## A350-6

The OIS (EFB) is MSFS SDK components with its own keyboard handling (`_onKeyDown` in `ini-efb-a350.js`). Measured 26 October 2026 on T.O PERF: setting WIND, OAT, QNH and TOW with the native value setter plus `input` and `change` events showed the values in the fields and kept them across a page change, but COMPUTE produced nothing. Typing the same values as `keydown`, `keypress`, value append with `input`, and `keyup` per character, then Enter and `change`, made COMPUTE return V1 142, VR 143, V2 148, FLEX 67, flaps 2, THS 26.4 %. The airport field reacted to the plain value set, so a field that seems to work that way is no proof the others do.

## A350-7

Measured 27 October 2026 (1.2.7, HUD fitted and deployed): `INSTRUMENT_FCU_HUD_DECLUTTER_LEFT_IE_ID_Set` writes only the key's `INI_FCU_HUD_DECLUTTER_LEFT_ANIM`, whatever its parameter; the model's update puts the ANIM back to 1 once `INI_FCU_HUD_DECLUTTER_LEFT_TIMER` runs out. Four writes of 1 and four of 0 left `INI_FCU_HUD_DECLUTTER_LEFT` at 0. The mouse rect's LeftSingle code is what declutters: ANIM 0, TIMER 2, and `INI_FCU_HUD_DECLUTTER_LEFT` flipped between 0 and 1. The generator maps such a key as a two-position selector on that variable (`CLICK_SWITCHES`). `A350WritePlan.ClickWrites` writes the same three L:vars, which switched both sides' declutter on and off through MSFSBA's own path. The map first called it a button pressed with 1, which did nothing. A key's `_Set` that moves only an animation is no proof of a working press: read the mouse code.

## A350-8

Measured 27 October 2026 (1.2.7): the two OIS views are `VCockpit10 - iniEfbA350` and `VCockpit11 - iniEfbA350`, and each loads its side's page, `ini-efb-a350-cpt.html` or `ini-efb-a350-fo.html` (the `url` of its `ini-efb-a350` element). `CoherentPmdgEfbClient.ForA350Ois` picks the view by evaluating that page name over each candidate socket (its side probe), as the PMDG tablets do with `getTabletSide()`. A view's number is the order the sim created the instruments in, not a side: picking by number would put the first officer's tablet behind the captain's key on any load that creates them the other way round.
