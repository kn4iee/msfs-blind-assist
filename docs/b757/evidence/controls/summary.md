# Control map: bluebirdsim-aircraft-757-200

- Title: BlueBirdSim 757-200 · creator: BlueBird Simulations · version: 0.1.0
- Interactive controls: 1231; lights (annunciators and legends reading an L: or a non-lighting A: var): 518; emissive elements driven only by circuits and potentiometers (backlighting, not listed): 673
- Controls by kind: toggle 178, multi-position switch 35, momentary button 163, held button 7, select button 14, rotary knob infinite 49, rotary knob finite 78, lever 30, guard 24, cdu key 138, circuit breaker 511, other 4
- Plain L:var controls (state is an L:var, the click writes L:vars, no event, bitmask, gate or stock template): 582; controls with a label of their own (ANIMTIP or tooltip title beyond DECREASE/INCREASE/ON/OFF): 288
- Panels (banner sections holding controls or lights): 79
- L:vars named by controls and lights: 1311; events fired by clicks: 24 K:, 2 H:, 0 B:
- Package templates defined: 47, expanded 367 times; sim templates used as leaves: 32

## Interior files and variants

| File | Role | Used by (model folders) | Controls | Lights | Own templates |
|---|---|---|---|---|---|
| `SimObjects/Airplanes/bbs-757-200-base/model/BBS_752_BASE_INTERIOR.xml` | interior base | (base of the interiors below) | 1223 | 1183 | 46 |
| `SimObjects/Airplanes/bbs-757-200-pw/model/BBS_752_PW_INTERIOR.xml` | interior | SimObjects/Airplanes/bbs-757-200-pw-nw/model, SimObjects/Airplanes/bbs-757-200-pw/model | 2 | 2 | 1 |
| `SimObjects/Airplanes/bbs-757-200-pw/model.nocab/BBS_752_PW_INTERIOR_NC.xml` | interior | SimObjects/Airplanes/bbs-757-200-pw-NC/model, SimObjects/Airplanes/bbs-757-200-pw-nw/model.nocabNW, SimObjects/Airplanes/bbs-757-200-pw/model.nocab | 2 | 2 | 1 |
| `SimObjects/Airplanes/bbs-757-200-pw-AWE/model/BBS_752_PW_INTERIOR_AWE.xml` | interior | SimObjects/Airplanes/bbs-757-200-pw-AWE/model, SimObjects/Airplanes/bbs-757-200-pw-nw-AWE/model | 2 | 2 | 1 |
| `SimObjects/Airplanes/bbs-757-200-rr/model/BBS_752_RR_INTERIOR.xml` | interior | SimObjects/Airplanes/bbs-757-200-rr-nw/model, SimObjects/Airplanes/bbs-757-200-rr-scimitarw/model, SimObjects/Airplanes/bbs-757-200-rr/model | 0 | 0 | 0 |
| `SimObjects/Airplanes/bbs-757-200-rr/model.nocabRR/BBS_752_RR_INTERIOR_NC.xml` | interior | SimObjects/Airplanes/bbs-757-200-rr/model.nocabRR | 2 | 2 | 1 |
| `SimObjects/Airplanes/bbs-757-200-rr-AWE/model/BBS_752_RR_INTERIOR_AWE.xml` | interior | SimObjects/Airplanes/bbs-757-200-rr-AWE/model, SimObjects/Airplanes/bbs-757-200-rr-nw-AWE/model | 0 | 0 | 0 |
- `SimObjects/Airplanes/bbs-757-200-pw/model/BBS_752_PW_INTERIOR.xml` adds to its base: `BBS_fuelcut_PW1` (multi-position switch, state L:BBS_fuelcut_PW1, writes L:BBS_fuelcut_PW1), `BBS_fuelcut_PW2` (multi-position switch, state L:BBS_fuelcut_PW2, writes L:BBS_fuelcut_PW2); 2 of its nodes are not in the base
- `SimObjects/Airplanes/bbs-757-200-pw/model.nocab/BBS_752_PW_INTERIOR_NC.xml` adds to its base: `BBS_fuelcut_PW1` (multi-position switch, state L:BBS_fuelcut_PW1, writes L:BBS_fuelcut_PW1), `BBS_fuelcut_PW2` (multi-position switch, state L:BBS_fuelcut_PW2, writes L:BBS_fuelcut_PW2); 2 of its nodes are not in the base
- `SimObjects/Airplanes/bbs-757-200-pw-AWE/model/BBS_752_PW_INTERIOR_AWE.xml` adds to its base: `BBS_fuelcut_PW1` (multi-position switch, state L:BBS_fuelcut_PW1, writes L:BBS_fuelcut_PW1), `BBS_fuelcut_PW2` (multi-position switch, state L:BBS_fuelcut_PW2, writes L:BBS_fuelcut_PW2); 2 of its nodes are not in the base
- `SimObjects/Airplanes/bbs-757-200-rr/model.nocabRR/BBS_752_RR_INTERIOR_NC.xml` adds to its base: `BBS_fuelcut_PW1` (multi-position switch, state L:BBS_fuelcut_PW1, writes L:BBS_fuelcut_PW1), `BBS_fuelcut_PW2` (multi-position switch, state L:BBS_fuelcut_PW2, writes L:BBS_fuelcut_PW2); 2 of its nodes are not in the base

## Panels

| Panel | Controls | Lights |
|---|---|---|
| RUDDER TRIM_LIMIT | 3 | 0 |
| YOKES + RUDDER + TILLER SYSTEM | 8 | 0 |
| THROTTLES MAIN | 10 | 0 |
| Updated throttle GA A/T buttons with L vars | 1 | 0 |
| (after Updated throttle GA A/T buttons with L vars) | 1 | 0 |
| TEST ANNUNCIATOR + BUTTONS LIGHTS | 0 | 121 |
| SUB CATEGORY - IRS SYSTEM PANEL | 0 | 12 |
| TEST WARNING LIGHTS PANEL | 3 | 1 |
| BUTTONS AND SWITCHES MAIN | 4 | 4 |
| HYDRAULIC PANEL | 6 | 6 |
| ELECTRICAL PANEL | 12 | 12 |
| APU CONTROL PANEL | 1 | 0 |
| COCKPIT VOICE RECORDER - CVR | 2 | 0 |
| LIGHT OVERRIDE | 10 | 6 |
| FUEL PANEL | 8 | 8 |
| ANTI ICE PANEL BUTTONS | 3 | 3 |
| WINDSHIELD RAIN REPELLENT AND WIPER CONTROL PANEL | 1 | 0 |
| lights | 14 | 12 |
| WINDOW HEAT CONTROL PANEL BUTTONS | 4 | 4 |
| SELCAL AND CABIN CALL BUTTON PANEL | 11 | 11 |
| PASSENGER SIGN CONTROL PANEL | 2 | 0 |
| PRESSURIZATION CONTROL PANEL | 4 | 0 |
| (after PRESSURIZATION CONTROL PANEL) | 2 | 3 |
| OVERHEAD PNEUMATIC PANEL | 14 | 7 |
| IRS SUB-CATEGORY - BUTTON_GLASS | 12 | 0 |
| IRS KNOBS | 5 | 0 |
| PLASTIC BUTTON and SWITCH GUARDS | 8 | 1 |
| MAIN BUTTONS AND CONTROL | 37 | 14 |
| AP MASTER DISCONNECT | 1 | 0 |
| MCP SPEED KNOB | 10 | 0 |
| AUTO THROTTLE ARM/OFF | 1 | 0 |
| WARNING/CAUTION BUTTONS | 2 | 4 |
| VOR/DME PANEL | 8 | 4 |
| FUEL CUT + THROTTLE QUADRANT SWITCHES | 0 | 8 |
| WARNING LIGHTS | 0 | 61 |
| EICAS KEY FUNCTIONS - FORWARD ONLY - EXCLUDE CRT BRIGHTNESS | 8 | 0 |
| EHSI/ADI KEY FUNCTIONS - CAPTAIN's SIDE ONLY - B757_AISLE_STAND_LCDEXCLUDE CRT BRIGHTNESS | 26 | 14 |
| FMC BUTTONS | 0 | 2 |
| PEDESTAL GAURD SWITCH GEAR | 25 | 2 |
| INDIVIDUAL AUDIO/RADIO PANELS | 126 | 23 |
| (after INDIVIDUAL AUDIO/RADIO PANELS) | 4 | 0 |
| LEFT | 0 | 12 |
| CRT screens | 0 | 8 |
| (after CRT screens) | 0 | 14 |
| AUDIO LIGHT KNOBS | 0 | 30 |
| (after OBSERVER ASP) | 0 | 30 |
| FMCS | 0 | 4 |
| FMC 2 decals | 0 | 4 |
| LANDING GEAR | 2 | 5 |
| EMISSIVE GAUGES | 0 | 4 |
| CAPT DDRMI | 4 | 0 |
| F/O DDRMI | 2 | 2 |
| CAPT AUTOLAND STATUS | 3 | 4 |
| F/O AUTOLAND STATUS | 3 | 4 |
| CAPT PRIMARY ALTIMETER | 2 | 0 |
| CAPT CHRONOMETER | 6 | 0 |
| STANDBY ATTITUDE-GYROMETER | 1 | 0 |
| STANDBY ALTIMETER | 1 | 0 |
| STANDBY ALTITUDE ROLLING NUMBER | 1 | 0 |
| FIRST OFFICER MACH/ASI | 4 | 0 |
| 1's | 0 | 1 |
| (after ALTITUDE ROLLING NUMBER) | 2 | 0 |
| FISRT OFFICER CHRONOMETER | 4 | 0 |
| MAIN PANEL SWITCH GEAR + BUTTONS | 52 | 15 |
| (after MAIN PANEL SECTION) | 1 | 0 |
| P61 PANEL SWITCH GEAR | 38 | 19 |
| (after P61 PANEL SWITCH GEAR) | 1 | 0 |
| MISC PARTS | 13 | 0 |
| CHANGE MAJOR CODE and UPDATES BELOW | 19 | 5 |
| COCKPIT CAPTAIN CHAIR | 1 | 0 |
| COCKPIT FIRST OFFICER CHAIR | 1 | 0 |
| L WINDOW WET and DRY LOGIC | 0 | 3 |
| RIGHT WINDOW RAIN EFFECTS | 14 | 3 |
| NEW ECAM COMPUTER SWITCH | 1 | 0 |
| ADDING L VARS for CDU switches | 69 | 0 |
| FO CDU CODE | 69 | 0 |
| CB OVERHEAD PANEL | 511 | 0 |
| (after DOOR CODE) | 1 | 0 |
| (no banner) | 8 | 8 |

## Naming conventions

L:var prefixes (first two underscore-separated words; unique names among state vars, written vars and light vars):

- `B757_OVHD_`: 440
- `B757_PED_`: 153
- `B757_CDU_`: 140
- `B757_MAIN_`: 113
- `B757_MCP_`: 48
- `B757_ADI_`: 22
- `B757_IRS_`: 19
- `B757_P6_`: 19
- `BBS757_CAPT_`: 18
- `BBS757_FO_`: 17
- `BBS_CB_`: 16
- `P61_PRESS_`: 12
- `B757_EICAS_`: 11
- `B757_FLOOD_`: 9
- `B757_FO_`: 9
- `B757_ENG_`: 8
- `B757_ADF_`: 6
- `B757_VHF_`: 6
- `BBS_OVHD_`: 6
- `B757_CAPT_`: 5
- `B757_CABIN_`: 4
- `B757_CAPTAIN_`: 4
- `B757_CARGO_`: 4
- `B757_CDU1_`: 4
- `B757_CDU2_`: 4
- `B757_CHRONO_`: 4
- `B757_DECAL_`: 4
- `B757_FC_`: 4
- `B757_HSI_`: 4
- `B757_JUMPSEAT_`: 4
- `B757_L_`: 4
- `B757_OBS1_`: 4
- `B757_OBS2_`: 4
- `B757_PFD_`: 4
- `B757_R_`: 4
- `B757_SUNVISOR_`: 4
- `B757_VOR_`: 4
- `BBS757_THRUST_`: 4
- `BBS_CHRONO_`: 4
- `BBS_DDRMI_`: 4
- … 104 more prefixes

Suffixes:

- `_BUTTON`: 205
- `_DECAL`: 196
- `_BULB_FAILED`: 163
- `_SWITCH`: 74
- `_KNOB`: 69
- `_ON`: 66
- `_LIGHT`: 34
- `_STATE`: 30
- `_PRESS`: 17
- `_HELD`: 16
- `_SW`: 9
- `_SEL`: 1

Light vars read by the most lights (a shared var is a lamp test, a failure or a power gate, not a legend of its own):

- `L:OVHD_TEST_LTS`: 433
- `L:STAB_TRIM_LIGHTING`: 8
- `L:BBS_RTP_L_LIGHTS`: 6
- `L:BBS_RTP_R_LIGHTS`: 6
- `L:BBS_RTP_C_LIGHTS`: 6
- `L:B757_L_ENG_FIRE_FUEL_CUTOFF_SWITCH`: 4
- `L:B757_R_ENG_FIRE_FUEL_CUTOFF_SWITCH`: 4
- `L:ASA_LIGHTING`: 4

Lights with a `_DECAL` var: 197; with a `_BULB_FAILED` var: 163; gated by `A:CIRCUIT ON:n`: 307; mapped to a control by name: 212 (and 80 guessed, marked '?')

Events fired by clicks (count of controls):

- `H:BBS_757_EICAS_DEBUG`: 1
- `H:COUCOU4`: 1
- `K:AP_ALT_VAR_DEC`: 1
- `K:AP_ALT_VAR_INC`: 1
- `K:AP_SPD_VAR_DEC`: 1
- `K:AP_SPD_VAR_INC`: 1
- `K:AP_VS_VAR_DEC`: 1
- `K:AP_VS_VAR_INC`: 1
- `K:AUTOPILOT_DISENGAGE_SET`: 1
- `K:AUTOPILOT_OFF`: 2
- `K:AUTO_THROTTLE_TO_GA`: 2
- `K:DECREASE_DECISION_HEIGHT`: 2
- `K:HEADING_BUG_DEC`: 1
- `K:HEADING_BUG_INC`: 1
- `K:INCREASE_DECISION_HEIGHT`: 2
- `K:KOHLSMAN_DEC`: 3
- `K:KOHLSMAN_INC`: 3
- `K:LOGO_LIGHTS_SET`: 1
- `K:XPNDR_1000_DEC`: 1
- `K:XPNDR_1000_INC`: 1
- `K:XPNDR_100_DEC`: 1
- `K:XPNDR_100_INC`: 1
- `K:XPNDR_10_DEC`: 1
- `K:XPNDR_10_INC`: 1
- `K:XPNDR_1_DEC`: 1
- `K:XPNDR_1_INC`: 1

## Templates

Package templates expanded (uses): BBS_PushButton_Held_Template (138), BBS_ASP_VOLUME_KNOB (30), BBS757_EFIS_RHEO_KNOB_TEMPLATE (17), BBS_MOMENTARY_ACTION_SWITCH (16), BBS_ROTARY_SELECTOR_KNOB (16), BBS757_MCP_BUTTON (14), BBS_ASP_MIC_BUTTON (14), BBS757_IRS_NUMPAD_BUTTON (12), BBS757_OVHD_PCP_BUTTON_GENERIC (10), BBS_SMALL_DETENT_ROTARY_SELECTOR_KNOB (10), BBS757_SMALL_BLACK_BUTTON (8), BBS_3POS_P61_TEST_SWITCH (8), BBS_PW_FUEL_WORKAROUND_TEMPLATE (8), BBS757_BLACK_PTT_BUTTON (7), BBS757_MAP_LIGHT_KNOB_TEMPLATE (6), BBS757_TMSP_BUTTON (6), BBS_3POS_TRIM_SWITCH_TEMPLATE (5), BBS_OVHD_PASSOXY_AND_RAT_BUTTON (4), BBS_ALTERNATING_ACTION_BUTTON (4), BBS757_ADI_KNOB_TEMPLATE (3), BBS_EXT_PWR_BUTTON (3), BBS_SWITCH_FLIP_GUARD (3), BBS757_ZONE_TEMP_CONTROL_KNOB (3), BBS_IRS_SELECTOR_KNOB (3), BBS_TOGA_PUSHBUTTON (2), BBS757_START_SELECTOR_KNOB (2), BBS_EICAS_SQUARE_BUTTON (2), BBS_METAL_2STATE_TOGGLE_SWITCH (2), BBS_IND_LTS_KNOB (1), BBS_APU_KNOB (1), BBS_LIGHT_OVERRIDE_BUTTON_AND_MODE (1), BBS_METAL_3STATE_EMER_LIGHTS_SWITCH (1), BBS757_OVHD_PCP_BUTTON_ALERT (1), BBS757_AUTOPILOT_DISENGAGE_BAR (1), BBS_Knob_Infinite_Better_Than_Asobo_template (1), BBS_ALTN_GEAR_SWITCH_GUARD (1), BBS_METAL_2state_ALTN_GEAR (1), BBS_AUTOBRAKE_KNOB (1), BBS_3POS_P61_SWITCH (1)

Package templates never used: ASOBO_INSTRUMENT_Needle_ADF_Template, BBS757_EFIS_RHEO_KNOB_TEMPLATE_FMC, BBS_AUTOBRAKE_SOUND_NODE, BBS_AUTOPILOT_Knob_PUSHSYSTEM_SubTemplate, BBS_AUTOPILOT_Knob_PUSHSYSTEM_Template, BBS_CDU_KEY, BBS_Knob_Tooltip, BBS_TURNOFF_LIGHT_SWITCH_TEMPLATE

Sim templates used as leaves (not in the package; uses): ASOBO_GT_Switch_Code (739), ASOBO_LIGHTING_Instruments_Emissive_Template (701), ASOBO_GT_Anim_Code (448), ASOBO_GT_Material_Emissive_Code (241), ASOBO_GT_Push_Button_Held_Simple (212), ASOBO_GT_MouseRect (161), ASOBO_GT_Knob_Finite_Code (79), ASOBO_GT_Update (54), ASOBO_GT_Component_Emissive_Gauge (54), ASOBO_GT_Knob_Infinite (42), ASOBO_GT_Lever_DraggingWheelLeftDrag_Axis (27), ASOBO_GT_Switch_3States (23), ASOBO_GT_Visibility (8), ASOBO_BOEING_Switch_VOR_ADF_Template (4), ASOBO_INSTRUMENT_Needle_MachNumber_Template (3), ASOBO_HANDLING_Yoke_Hideable_Template (2), ASOBO_HANDLING_Yoke_Hider_Template (2), ASOBO_ENGINE_Lever_Throttle_Template (2), ASOBO_GT_Emissive_Gauge (2), ASOBO_LIGHTING_Knob_Potentiometer_Template (2), ASOBO_INSTRUMENT_Dial_Compass_Template (2), ASOBO_INSTRUMENT_Knob_Altimeter_Template (2), ASOBO_HANDLING_RudderPedals_Template (1), ASOBO_HANDLING_Steering_Tiller_Template (1), ASOBO_GT_Interaction_LeftSingle_Code (1), ASOBO_INSTRUMENT_Needle_ADF_SubTemplate (1), ASOBO_INSTRUMENT_Needle_Altimeter_Template (1), ASOBO_INSTRUMENT_Altimeter_SubTemplate (1), ASOBO_INSTRUMENT_Needle_VerticalSpeed_SubTemplate (1), ASOBO_LIGHTING_Knob_Panel_Template (1), ASOBO_LIGHTING_Knob_Cabin_Template (1), ASOBO_HANDLING_Spoilers_Int_Template (1)

Sim behavior libraries included: `ASOBO\Inputs\Helpers.xml`, `ASOBO\Inputs\Templates.xml`, `Asobo\Airliner\Airbus.xml`, `Asobo\Airliner\AirlinerCommon.xml`, `Asobo\Airliner\Boeing.xml`, `Asobo\Airliner\FMC.xml`, `Asobo\Airliner\GlassCockpit.xml`, `Asobo\Common.xml`, `Asobo\Common\Autopilot.xml`, `Asobo\Common\Instrument.xml`, `Asobo\Generic\Complex\Knob.xml`, `Asobo\Generic\Emissive.xml`, `Asobo\Generic\Interactions.xml`, `Asobo\GlassCockpit\AS1000.xml`, `bbs-757\Templates.xml`

## Kinds

- toggle: a click flips one var (`(L:X) ! (>L:X)` or an equivalent if/els), or a click whose new value depends on the var it writes, with at most two labels.
- multi-position switch: three or more labels, a `ASOBO_GT_Switch_3States`, or a click that steps a var between clamps (`1 + 2 min`).
- momentary button: press writes and release resets (a pulse), or a press that only sets a value or fires an event; held button: the template times the press (`FIRE_HELD_EVENT`, `MIN_HELD_DURATION`, a `_HELD` var).
- select button (beyond the brief's list): a press sets its own var to 1 and clears its group (radio-style audio selectors).
- rotary knob infinite / finite: turn codes (or a knob template); infinite when the code wraps (`%`, `d 35 > if{ 0 }`) or the template is an infinite knob.
- lever: a drag or axis template (`DRAG_CODE`); guard: node or var named GUARD, GAURD (the vendor's spelling) or COVER; cdu key: the CDU key templates.
- circuit breaker (beyond the brief's list): a click that XORs one bit of a shared mask var.
- other: flight controls and anything the rules above do not match (see the notes column).

## Irregularities (count of controls)

- bitmask state: 511
- state not an L:var: 77
- writes L:vars unrelated to its state: 55
- Update code writes its vars: 26
- K: event: 19
- stock Asobo template: 14
- selects one of a group: 14
- turn writes +1/-1 to a request var: 13
- press only: 8
- flight control: 4
- click gated: 3
- node has several control rows: 2
- H: event: 2
- click writes only O: 2

## What a generator can derive, and what needs curation

- Mechanical: panel grouping (banner), node, kind, positions, state var and the RPN to replay for each click, for every row; for the 582 plain L:var controls that is the whole definition (read the state var, replay the click code over the calculator path): toggle 168, multi-position switch 34, momentary button 110, select button 14, rotary knob infinite 6, rotary knob finite 74, lever 18, guard 20, cdu key 138.
- Mechanical with a rule: circuit breakers (511) share bitmask vars, one bit each: the bit is the constant in the click code; momentary and held buttons (163 + 7) need a press, a hold time and a release; CDU keys (138) follow one var per key.
- Curation: wording for the 943 controls with no label of their own (the node name is all there is); position names where ANIMTIP describes a direction (knobs) rather than positions; controls driven by K:/H:/B: events whose real state is a stock simvar or lives in the aircraft's WASM; controls whose state is not an L:var; guarded switches (the guard var must be open first); variant-specific controls (see the variant table); lights mapped to controls by name only ('?' is a guess).

## Assumptions about the sim's templates (not in the package)

- `ASOBO_GT_Switch_Code`: ANIM_CODE is the position (0..100 per step); LEFT_SINGLE_CODE runs on a left click; ANIMTIP_n is the tooltip while the control is in position n (the vendor's text may name the state or the action). Without any click code it is an animated part (an inscription), not a control.
- `ASOBO_GT_Push_Button_Held_Simple`: LEFT_SINGLE_CODE on press, LEFT_LEAVE_CODE on release (or when the mouse leaves).
- `ASOBO_GT_Knob_Infinite` / `ASOBO_GT_Knob_Finite_Code`: CLOCKWISE_CODE and ANTICLOCKWISE_CODE per detent; ANIMTIP_0 is the anticlockwise (decrease) hint and ANIMTIP_1 the clockwise one; VALUE is the tooltip's value code (it often names where the real value lives).
- `ASOBO_GT_Switch_3States`: the template itself writes SWITCH_POSITION_VAR (of SWITCH_POSITION_TYPE, L here) to 0, 1 or 2 and then runs CODE_POS_n; STATEn_TEST reads the position back.
- `ASOBO_GT_MouseRect`: CALLBACKCODE runs on every event in MOUSEFLAGS; branches test (M:Event) 'LeftSingle', 'LeftRelease', 'WheelUp' ...; code outside a branch runs on every listed event.
- `ASOBO_GT_Lever_DraggingWheelLeftDrag_Axis`: DRAG_CODE runs while dragging with (M:DragPercent); POSITIVE_AXIS_CODE and NEGATIVE_AXIS_CODE run on wheel or axis steps.
- `ASOBO_INSTRUMENT_Base_Template` (the vendor's tooltip and CDU-key wrappers): it builds an input event named IE_NAME whose interaction runs the inherited CLOCKWISE/ANTICLOCKWISE/LEFT_SINGLE codes.
- `ASOBO_GT_Helper_Suffix_ID_Appender`: calls TEMPLATE_TO_CALL; when that is a package template it is expanded directly (the ID suffixing is not modeled).
- Template logic: <Parameters> without Type and <DefaultTemplateParameters> set a value only when it is not already set; <Condition Check> means defined, <Condition Valid> means defined and not False/0/empty; a UseTemplate's children and every enclosing value are visible inside the template.
- High-level stock templates (ASOBO_AUTOPILOT_Knob_Altitude_Template, ASOBO_AUTOPILOT_Knob_VerticalSpeed_Template, ASOBO_AUTOPILOT_Push_Disconnect_Template, ASOBO_AUTOPILOT_Push_FlightDirector_Template, ASOBO_BOEING_Switch_VOR_ADF_Template, ASOBO_ENGINE_Lever_Throttle_Template, ASOBO_HANDLING_Lever_Flaps_Template, ASOBO_HANDLING_RudderPedals_Template, ASOBO_HANDLING_Spoilers_Int_Template, ASOBO_HANDLING_Steering_Tiller_Template, ASOBO_HANDLING_Switch_ElevatorTrim_Template, ASOBO_HANDLING_Yoke_Hideable_Template, ASOBO_INSTRUMENT_Knob_Altimeter_Template, ASOBO_LANDING_GEAR_Lever_Gear_Template, ASOBO_LIGHTING_Knob_Cabin_Template, ASOBO_LIGHTING_Knob_Panel_Template, ASOBO_LIGHTING_Knob_Potentiometer_Template, ASOBO_LIGHTING_Switch_Light_Landing_Template, ASOBO_LIGHTING_Switch_Light_Recognition_Template) keep their logic and state in the sim; the state var shown for them is the stock simvar such a template is assumed to drive.
