# Plan 12 Task 4: the order of the controls inside each panel

Since Task 7 (2026-10-28) the panels are regrouped to follow the cockpit (see [panel-audit-12.md](panel-audit-12.md)); inside each regrouped panel this order rule is kept: by tier, Task 4's order within a tier, each guard just before its switch.

The owner's instruction (2026-10-28): "The panel items should be ordered where most frequently every day items are listed first in tab order." This file is the evidence for every placement: the order of each panel, each control's tier, and the reason with its source, so that each move can be checked. It is generated from the research data (`order_data.py` of the plan 12 workspace); the order itself lives in `tools/b757-gen/curation.json` (`control_order`), and the map `MSFSBlindAssist/Resources/bbs757_control_map.json` is generated from it.

## Method

Only the order of the controls inside a panel moves. The panels of a section keep their order (`panel_order`), and no label, position or control changed. Each control has a tier:

- **Tier 1, every flight.** What the normal procedures move on every flight, in the order the flow first uses it, a left and right pair together.
- **Tier 2, set once.** Set once in preflight, tests, display options, things done on request.
- **Tier 3, rare.** Non-normal, emergency and maintenance controls, with a guard just before the switch it covers. The emergency lights guard is the exception: it stays right after its switch, because the app opens and closes it when the switch is set.

Every panel is listed tier 1 first, then tier 2, then tier 3 (a pair or a guard stays together inside its tier, as the reason says). The rows below are in the order the pilot tabs through.

Sources, cited by PDF page: **UP n** is page n of `BlueBird_757_User_Procedures_v1.01.pdf`, **NC n** is page n of `BlueBird_757_Normal_Checklist_v1.01.pdf`. **(gp)** marks general 757 practice where BlueBird's manuals say nothing. "BlueBird list" and "BlueBird simvar list" are BlueBird's published simvar list (support.bluebirdsim.com/docs/simvars). 18 reasons cite the researcher's own notes ("see method 1.5", "see method 1.6", "(section 3)"); the notes close this file, verbatim, under "Daily-use order research: the notes order_data.py refers to". Every reason is kept as the research wrote it, and the key below says which note each citation means.

Key to the citations of the research notes:

- "method 1.5" in the reason of Overhead miscellaneous / Emergency Lights Guard: judgment call 1 of the notes (the emergency lights guard stays just after its switch, because the app opens and closes it itself).
- "method 1.6" in the reason of Stabilizer trim / Nose Down Stab Trim 1 s (button): judgment call 4 of the notes (the alternate stab trim buttons are tier 2, as the flow only checks those switches NEUTRAL and normal trim is the yoke switches).
- "method 1.6" in the reason of Engines / R Fuel Control (RR): judgment call 5 of the notes (Engines was unchanged, ruling R37, and the notes name an alternative order for the RR switches; the owner's plan 12 Task 10 instruction made engine by engine the order, see the Engines section).
- "(section 3)" in the reasons of 8 rows (Audio panel 1 / Audio 1 Mic Lm Light; Audio panel 1 / Audio 1 Mic Lm Light Knob; Audio panel 1 / Audio 1 Mic RR Light; Audio panel 1 / Audio 1 Mic RR Light Knob; Audio panel 2 / Audio 2 Mic Lm Light; Audio panel 2 / Audio 2 Mic Lm Light Knob; Audio panel 2 / Audio 2 Mic RR Light; Audio panel 2 / Audio 2 Mic RR Light Knob): item 1 of the notes' "Section 3" list, a control whose meaning could not be established.
- "(section 3)" in the reasons of 1 row (SELCAL and cabin call / Flt Int): item 2 of the notes' "Section 3" list, a control whose meaning could not be established.
- "(section 3)" in the reasons of 2 rows (Captain EFIS / INOP; First officer EFIS / INOP): item 3 of the notes' "Section 3" list, a control whose meaning could not be established.
- "(section 3)" in the reasons of 1 row (Cabin lights / Cabin Work Light): item 4 of the notes' "Section 3" list, a control whose meaning could not be established.
- "(section 3)" in the reasons of 1 row (Captain chronometer / 757 Capt Clock): item 5 of the notes' "Section 3" list, a control whose meaning could not be established.
- "(section 3)" in the reasons of 1 row (MCP / ALT SEL Push): item 6 of the notes' "Section 3" list, a control whose meaning could not be established.
- "(section 3)" in the reasons of 1 row (EICAS control / EICAS Event): item 8 of the notes' "Section 3" list, a control whose meaning could not be established.
- Items 7, 9 and 10 of the "Section 3" list (weather radar, Sys Fail, Stdby EICAS) are not cited by any reason's "(section 3)" mark; they stay in the notes below.

App rows are not map nodes: the landing altitude field, the cabin climb and descend buttons and the six trim buttons are placed by the app's own code (Pressurization: the landing altitude field first, the cabin buttons last; Stabilizer trim: the trim buttons first, rudder, aileron, then the alternate stab trim). They are listed where the research places them and marked "(app row, placed by its own code)"; `control_order` leaves them out.

Totals: 55 panels (29 changed, 1 new panel, 25 unchanged), 426 rows (417 map controls and 9 app rows); tier 1: 113, tier 2: 169, tier 3: 144.

## Overhead

### Electrical (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Battery | `B757_OVHD_BATTERY_BUTTON` | 1 | ON first at power-up (UP 35), OFF last at final securing (UP 62) |
| 2 | EXT PWR | `B757_OVHD_EXT_PWR_BUTTON` | 1 | press ON at power-up (UP 35), deselect for APU transfer or securing (UP 35, 62); moved up from 8th because the AUTO/ON items it followed stay set |
| 3 | STBY Power | `B757_OVHD_STBY_POWER_SWITCH` | 2 | AUTO (UP 35); BAT only for the preflight battery test (UP 38, 82) |
| 4 | L Bus Tie | `B757_OVHD_L_BUS_TIE_BUTTON` | 2 | AUTO at power-up (UP 35, 38); no later flow moves it |
| 5 | R Bus Tie | `B757_OVHD_R_BUS_TIE_BUTTON` | 2 | as L |
| 6 | L Gen Cont | `B757_OVHD_L_GEN_CONT_BUTTON` | 2 | ON at power-up (UP 35, 38), left ON |
| 7 | R Gen Cont | `B757_OVHD_R_GEN_CONT_BUTTON` | 2 | as L |
| 8 | APU Gen | `B757_OVHD_APU_GEN_BUTTON` | 2 | ON for the APU transfer (UP 35), left ON |
| 9 | L Util Bus | `B757_OVHD_L_UTIL_BUS_BUTTON` | 2 | ON (UP 38); reset OUT/IN only after ground load shedding (UP 83-84) |
| 10 | R Util Bus | `B757_OVHD_R_UTIL_BUS_BUTTON` | 2 | as L |
| 11 | L Gen Drive | `B757_OVHD_L_GEN_DRIVE_BUTTON` | 3 | generator drive disconnect, guarded: non-normal (gp) |
| 12 | R Gen Drive | `B757_OVHD_R_GEN_DRIVE_BUTTON` | 3 | as L |

### APU (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | APU Selector | `B757_OVHD_APU_SWITCH` | 1 | START then ON (UP 35), OFF after start or at securing (UP 48, 62) |

### IRS (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | IRS L | `B757_IRS_L_SWITCH` | 1 | OFF then NAV (UP 36), OFF at termination (UP 61-62) |
| 2 | IRS C | `B757_IRS_C_SWITCH` | 1 | as L |
| 3 | IRS R | `B757_IRS_R_SWITCH` | 1 | as L |
| 4 | IRS Center Knob | `B757_IRS_CENTRE_KNOB` | 2 | display-unit system select L/C/R; not in the flows (gp) |
| 5 | IRS Large Knob | `B757_IRS_LARGE_KNOB` | 2 | display data select (TK/GS, PPOS, WIND, HDG/STS) (gp) |

### Engine control and yaw damper (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | L Yaw Damper | `B757_OVHD_L_YAW_DAMPER_BUTTON` | 2 | ON in preflight (UP 38, 80); no later flow switches it |
| 2 | R Yaw Damper | `B757_OVHD_R_YAW_DAMPER_BUTTON` | 2 | as L |
| 3 | L ELEC ENG Cont (RR) | `B757_OVHD_L_ELEC-ENG-CONT_BUTTON` | 3 | RR only; checked 'ON, GUARDED' (UP 38, 79); OFF is non-normal |
| 4 | R ELEC ENG Cont (RR) | `B757_OVHD_R_ELEC-ENG-CONT_BUTTON` | 3 | as L |

### Hydraulics (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | R ELEC Pump | `B757_OVHD_R_ENG_ELEC_PUMPS_BUTTON` | 1 | electric pumps OFF in the safety inspection (UP 34), ON before start in the order R, C, L (UP 46, 80, 139), OFF at shutdown L, C, R (UP 61, 194) |
| 2 | C1 ELEC Pump | `B757_OVHD_C_ENG1_HYD_PUMPS_BUTTON` | 1 | C1, same flows |
| 3 | C2 ELEC Pump | `B757_OVHD_C_ENG2_HYD_PUMPS_BUTTON` | 1 | C2, same flows (shed until an engine runs, UP 139) |
| 4 | L ELEC Pump | `B757_OVHD_L_ENG_ELEC_PUMPS_BUTTON` | 1 | L, last ON and first OFF |
| 5 | L ENG HYD Pumps | `B757_OVHD_L_ENG_HYD_PUMPS_BUTTON` | 2 | engine pumps ON in preflight (UP 38, 80); the shutdown turns only the electric pumps off (UP 61, 194), so they stay ON |
| 6 | R ENG HYD Pumps | `B757_OVHD_R_ENG_HYD_PUMPS_BUTTON` | 2 | as L |

### Overhead miscellaneous (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Emergency Lights | `B757_OVHD_PASS_OXY_Toggle_Switch_CLICK` | 1 | Emergency Lights ARMED in preflight (UP 38, 85), OFF for termination (UP 61, 194; NC 2) |
| 2 | Emergency Lights Guard | `B757_OVHD_SWITCH_GAURD` | 1 | its guard; kept right AFTER the switch because the app opens and closes it when the switch is set (curation PLAN 02 TASK 3, Bbs757Guarded): see method 1.5 |
| 3 | Lt Ovrd | `B757_OVHD_LT_OVRD_BUTTON` | 2 | as desired (UP 38, 61, 194) |
| 4 | L ENG Limiter | `B757_OVHD_L_ENG_LIMITER_BUTTON` | 2 | RR only; ON in preflight (UP 38, 86), left ON |
| 5 | R ENG Limiter | `B757_OVHD_R_ENG_LIMITER_BUTTON` | 2 | as L |
| 6 | Passenger Oxygen | `B757_OVHD_PASS_OXY_BUTTON` | 3 | checked guarded and dark, never operated (UP 38, 86) |
| 7 | RAT | `B757_OVHD_RAT_BUTTON` | 3 | checked guarded and dark, never operated (UP 38, 86) |

### Fuel (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | L Pumps FWD | `B757_OVHD_L_PUMPS_FWD_BUTTON` | 1 | OFF in preflight (UP 39), ON before start (UP 46, 140), OFF at shutdown (UP 61, 195) |
| 2 | L Pumps Aft | `B757_OVHD_L_PUMPS_AFT_BUTTON` | 1 | as L FWD |
| 3 | R Pumps FWD | `B757_OVHD_R_PUMPS_FWD_BUTTON` | 1 | as L FWD |
| 4 | R Pumps Aft | `B757_OVHD_R_PUMPS_AFT_BUTTON` | 1 | as L FWD |
| 5 | C Pumps L | `B757_OVHD_C_PUMPS_L_BUTTON` | 1 | ON before start when the centre tank holds fuel (UP 46, 140) |
| 6 | C Pumps R | `B757_OVHD_C_PUMPS_R_BUTTON` | 1 | as C L |
| 7 | Crossfeed FWD | `B757_OVHD_XFEED_PUMPS_FWD_BUTTON` | 3 | checked OFF (UP 39, 46); opened only for fuel balancing / engine-out (UP 213, 228-229) |
| 8 | Crossfeed Aft | `B757_OVHD_XFEED_PUMPS_AFT_BUTTON` | 3 | as FWD |

### Anti-ice (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Anti Ice L ENG | `B757_OVHD_ANTI_ICE_L_ENG_BUTTON` | 1 | engine anti-ice is on the After Start, After Landing and Shutdown checklists (NC 2; UP 48, 60, 61: OFF before shutdown) |
| 2 | Anti Ice R ENG | `B757_OVHD_ANTI_ICE_R_ENG_BUTTON` | 1 | as L |
| 3 | Anti Ice Wing | `B757_OVHD_ANTI_ICE_WING_BUTTON` | 2 | OFF in preflight (UP 39), 'WING OFF' after landing (UP 60); ON only in icing in flight; moved from 1st to last |

### Wipers (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Wiper | `B757_OVHD_WIPER_SWITCH` | 2 | OFF (UP 39, 61, 195); rain only |

### Exterior lights (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Anti-col Red Lights | `B757_OVHD_LOGO` | 1 | Anti-col Red (beacon): ON before pushback (UP 46, 142), OFF at shutdown (UP 61) |
| 2 | Wing Lights | `B757_OVHD_HI_INT` | 1 | Wing: ON for line-up and takeoff (UP 158), OFF after landing (UP 190) |
| 3 | L Runway Turnoff | `B757_OVHD_RWY_TURN_L` | 1 | Runway turnoff: line-up and takeoff (UP 158), off after landing (UP 190), checked OFF at the gate (UP 38, 194) |
| 4 | R Runway Turnoff | `B757_OVHD_RWY_TURN_R` | 1 | as L |
| 5 | Taxi Light | `B757_OVHD_btn_lights_taxi` | 1 | Taxi: runway crossing / line-up (UP 158), taxi-in (UP 60, 190) |
| 6 | Anti-col White Lights | `B757_OVHD_NAV` | 1 | Anti-col White (strobe): takeoff (UP 158), OFF after landing (UP 190) |
| 7 | L Landing Light | `B757_OVHD_SW_landing_L` | 1 | Landing: takeoff (UP 158), off above FL180 (UP 169), approach (UP 174), off after landing (UP 190) |
| 8 | R Landing Light | `B757_OVHD_SW_landing_R` | 1 | as L |
| 9 | Position Lights | `B757_OVHD_BEACON` | 2 | Position: ON in preflight (UP 39) and left ON while parked (UP 195); moved from 2nd |
| 10 | Logo Lights | `B757_OVHD_LOGO_BUTTON` | 2 | Logo: as required (UP 39, 169, 190) |

### Window heat (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | L Side Window Heat | `B757_OVHD_L_SIDE_WINDOW_HEAT_BUTTON` | 1 | all four ON in preflight (UP 39, 88), OFF at shutdown (UP 61, 195; NC 2) |
| 2 | L FWD Window Heat | `B757_OVHD_L_FWD_WINDOW_HEAT_BUTTON` | 1 | same |
| 3 | R Side Window Heat | `B757_OVHD_R_SIDE_WINDOW_HEAT_BUTTON` | 1 | same |
| 4 | R FWD Window Heat | `B757_OVHD_R_FWD_WINDOW_HEAT_BUTTON` | 1 | same |

### Passenger signs (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Seatbelts | `B757_OVHD_SEATBELTS_SWITCH` | 1 | ON before start (UP 46), for descent (UP 55), OFF at shutdown (UP 61) |
| 2 | No Smoking | `B757_OVHD_NO_SMOKING_SWITCH` | 2 | set as required (UP 39); ON in all nine BlueBird presets (curation) |

### Pressurization (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Landing Altitude (field) | (app row, placed by its own code) | 1 | landing elevation set every flight (UP 39, 89-90); stays first |
| 2 | Mode Select | `B757_OVHD_MODE_SELECT_SWITCH` | 1 | AUTO 1 or AUTO 2 (UP 39), alternated between flights by convention (UP 105) |
| 3 | Auto Rate | `B757_OVHD_AUTORATE_SWITCH` | 2 | 'AUTO RATE at index' (UP 39); left there |
| 4 | Equip | `B757_OVHD_EQUIP_BUTTON` | 3 | alternate equipment cooling: checked OFF (UP 39, 90); ALTN for smoke/overheat (gp) |
| 5 | Climb Cabin 1 s (button) | (app row, placed by its own code) | 3 | manual (MAN) mode only: non-normal (gp) |
| 6 | Descend Cabin 1 s (button) | (app row, placed by its own code) | 3 | as Cabin Climb |

### Air conditioning and bleed (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | L Pack Selector | `B757_OVHD_SEL_L_PACK_SWITCH` | 1 | as required (UP 39), OFF for start (UP 47), AUTO after start (UP 48), as required at shutdown (UP 61) |
| 2 | R Pack Selector | `B757_OVHD_SEL_R_PACK_SWITCH` | 1 | as L |
| 3 | Isolation | `B757_OVHD_ISOLATION_BUTTON` | 1 | OPEN (UP 39), CLOSED after start (UP 48), OPEN at shutdown (UP 61) |
| 4 | L ENG Bleed | `B757_OVHD_LENG_BUTTON` | 1 | ON (UP 39), checked for start (UP 47) and after start (UP 48), ON at shutdown (UP 61); L and R now adjacent |
| 5 | R ENG Bleed | `B757_OVHD_RENG_BUTTON` | 1 | as L |
| 6 | APU Bleed | `B757_OVHD_APU_BUTTON` | 1 | APU bleed ON (UP 39) and for engine start (UP 47); moved from between the engine bleeds |
| 7 | Flight Deck Temperature | `B757_OVHD_SEL_pack_Cockpt` | 2 | temperature controls set in the AUTO range (UP 39, 90-91), then left |
| 8 | Forward Cabin Temperature | `B757_OVHD_SEL_pack_FWD_cab` | 2 | same |
| 9 | Aft Cabin Temperature | `B757_OVHD_SEL_pack_AFT_cab` | 2 | same |
| 10 | Trim Air | `B757_OVHD_TRIM_AIR_BUTTON` | 2 | ON (UP 39, 91), left ON |
| 11 | L Recirc | `B757_OVHD_L_RECIRC_BUTTON` | 2 | ON (UP 39, 91), left ON |
| 12 | R Recirc | `B757_OVHD_R_RECIRC_BUTTON` | 2 | as L |
| 13 | L Pack | `B757_OVHD_L_PACK_BUTTON` | 3 | pack reset (BlueBird list: 'pack reset request'): after a pack trip only |
| 14 | R Pack | `B757_OVHD_R_PACK_BUTTON` | 3 | as L |

### Cabin lights (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Cabin Ceiling | `B757_CABIN_CEILING` | 2 | cabin lighting as desired (gp); no flow names it |
| 2 | Cabin Window | `B757_CABIN_WINDOW` | 2 | same |
| 3 | Cabin Entry | `B757_CABIN_ENTRY` | 2 | same |
| 4 | Cabin Work Light | `B757_CABIN_WORK_LT002` | 3 | BlueBird's list calls it the flight-attendant emergency-light override (section 3) |

### IRS keypad (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | IRS Number 1 | `B757_IRS_NUMBER_1_BUTTON` | 3 | keypad entry; the FMS SET IRS POS replaces it (UP 43) (gp) |
| 2 | IRS Number 2 | `B757_IRS_NUMBER_2_BUTTON` | 3 | keypad entry; the FMS SET IRS POS replaces it (UP 43) (gp) |
| 3 | IRS Number 3 | `B757_IRS_NUMBER_3_BUTTON` | 3 | keypad entry; the FMS SET IRS POS replaces it (UP 43) (gp) |
| 4 | IRS Number 4 | `B757_IRS_NUMBER_4_BUTTON` | 3 | keypad entry; the FMS SET IRS POS replaces it (UP 43) (gp) |
| 5 | IRS Number 5 | `B757_IRS_NUMBER_5_BUTTON` | 3 | keypad entry; the FMS SET IRS POS replaces it (UP 43) (gp) |
| 6 | IRS Number 6 | `B757_IRS_NUMBER_6_BUTTON` | 3 | keypad entry; the FMS SET IRS POS replaces it (UP 43) (gp) |
| 7 | IRS Number 7 | `B757_IRS_NUMBER_7_BUTTON` | 3 | keypad entry; the FMS SET IRS POS replaces it (UP 43) (gp) |
| 8 | IRS Number 8 | `B757_IRS_NUMBER_8_BUTTON` | 3 | keypad entry; the FMS SET IRS POS replaces it (UP 43) (gp) |
| 9 | IRS Number 9 | `B757_IRS_NUMBER_9_BUTTON` | 3 | keypad entry; the FMS SET IRS POS replaces it (UP 43) (gp) |
| 10 | IRS Number 0 | `B757_IRS_NUMBER_0_BUTTON` | 3 | keypad entry; the FMS SET IRS POS replaces it (UP 43) (gp) |
| 11 | IRS Number Ent | `B757_IRS_NUMBER_ENT_BUTTON` | 3 | keypad entry; the FMS SET IRS POS replaces it (UP 43) (gp) |
| 12 | IRS Number Del | `B757_IRS_NUMBER_DEL_BUTTON` | 3 | keypad entry; the FMS SET IRS POS replaces it (UP 43) (gp) |

### Switch guards (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Battery Guard | `B757_GAURD_3_SWITCH` | 2 | Battery guard: covers the tier-1 battery switch (UP 35, 62; 'guard opened', UP 82); BlueBird does not gate the battery on it (curation's guarded list names only the emergency lights and stab trims); moved from 3rd to 1st |
| 2 | Elec Eng Cont 1 Guard (RR) | `B757_GAURD_1_SWITCH` | 3 | EEC guard, RR only: 'ON, GUARDED' (UP 38) |
| 3 | Elec Eng Cont 2 Guard (RR) | `B757_GAURD_2_SWITCH` | 3 | as 1 |
| 4 | Passenger Oxygen Guard | `B757_GAURD_6_SWITCH` | 3 | passenger oxygen: 'GUARDED AND DARK' (UP 38) |
| 5 | Ram Air Turbine Guard | `B757_GAURD_7_SWITCH` | 3 | RAT: 'GUARDED AND DARK' (UP 38) |
| 6 | Left Generator Drive Disconnect Guard | `B757_GAURD_4_SWITCH` | 3 | generator drive disconnect guard (gp); not named in the flows |
| 7 | Right Generator Drive Disconnect Guard | `B757_GAURD_5_SWITCH` | 3 | as L |

### Flight deck door (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Flight Deck Door | `B757_OVHD_FLT-DK-DOOR_SWITCH` | 2 | door LOCK checked before start (UP 46, 135); the lever answers entry requests (gp) |

### CVR (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | CVR Test | `B757_OVHD_SW_CVR_TEST` | 2 | preflight test (UP 38, 84) |
| 2 | CVR Erase | `B757_OVHD_SW_CVR_ERASE` | 3 | erase on the ground after flight, rare (gp) |

### SELCAL and cabin call (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | FWD Cabin Call | `B757_OVHD_FWD_CABIN_CALL_BUTTON` | 1 | cabin call for landing preparation in descent: 'one of the blue cabin-call buttons', illustrated FWD (UP 55, 174) |
| 2 | Mid Cabin Call | `B757_OVHD_MID_CABIN_CALL_BUTTON` | 2 | the other blue cabin-call buttons (UP 55) |
| 3 | Aft Cabin Call | `B757_OVHD_AFT_CABIN_CALL_BUTTON` | 2 | same |
| 4 | GND Call | `B757_OVHD_GND_CALL_BUTTON` | 2 | calls the ground crew (BlueBird list: ground horn) (gp) |
| 5 | L VHF | `B757_OVHD_L_VHF_BUTTON` | 2 | SELCAL reset (BlueBird list: 'VHF buttons acknowledge the corresponding SELCAL') (gp) |
| 6 | R VHF | `B757_OVHD_R_VHF_BUTTON` | 2 | same; L/R pair adjacent |
| 7 | C VHF | `B757_OVHD_C_VHF_BUTTON` | 3 | BlueBird list: no SELCAL action bound |
| 8 | L HF | `B757_OVHD_L_HF_BUTTON` | 3 | BlueBird list: no SELCAL action bound; HF |
| 9 | R HF | `B757_OVHD_R_HF_BUTTON` | 3 | same |
| 10 | Flt Int | `B757_OVHD_FLT_INT_BUTTON` | 3 | meaning not established (section 3) |
| 11 | Alert Cabin Call | `B757_OVHD_ALERT_CABIN_CALL_BUTTON` | 3 | emergency alert call to the cabin (gp) |

### Ground service (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | GND Svc | `B757_GND_SVC_BUTTON` | 3 | ground-service bus power for servicing (BlueBird list); not in the flows (gp) |

### Warning lights test (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Lamp Test | `B757_OVHD_WARNING_TEST_BUTTON` | 2 | annunciator lamp test, first flight (gp) |
| 2 | Warning Ind | `B757_OVHD_WARNING_IND_SWITCH` | 2 | DIM/BRT for night (gp) |

## Glareshield

### MCP (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Captain F/D | `B757_MCP_SW_FD_Cap` | 1 | F/D ON in preflight (UP 40, 93), OFF after landing and at shutdown (UP 60, 61) |
| 2 | F/O F/D | `B757_MCP_SW_FD_FO` | 1 | as Captain |
| 3 | A/T Arm | `B757_MCP_SW_AP_throttle` | 1 | A/T ARM in preflight (UP 40, 93) and at shutdown (UP 61, 196) |
| 4 | LNAV | `B757_MCP_LNAV_BUTTON` | 1 | armed before takeoff when the departure needs it (UP 45, 132), at 400 ft (UP 51, 165) |
| 5 | THR | `B757_MCP_THR_BUTTON` | 1 | takeoff thrust is set with THR, not the GA paddles (UP 162-163); moved from 4th to 5th |
| 6 | HDG SEL | `B757_MCP_AP_HDG_SEL_SW` | 1 | HDG SEL or LNAV at 400 ft (UP 51, 165) |
| 7 | VNAV | `B757_MCP_VNAV_BUTTON` | 1 | climb mode at acceleration height (UP 51, 165); T-brief cue (UP 134) |
| 8 | CMD L | `B757_MCP_CMD_1_BUTTON` | 1 | autopilot engage: 'planned autopilot use' (UP 134), 'AUTOPILOT ON' (UP 245); moved up from 15th |
| 9 | CMD C | `B757_MCP_CMD_2_BUTTON` | 1 | second/third autopilot for a multiple-autopilot approach (UP 220) |
| 10 | CMD R | `B757_MCP_CMD_3_BUTTON` | 1 | as CMD C |
| 11 | FLCH | `B757_MCP_FLCH_BUTTON` | 1 | VNAV, FLCH or V/S for climb and descent (UP 59, 165, 187) |
| 12 | V/S | `B757_MCP_VS_BUTTON` | 1 | as FLCH (UP 165) |
| 13 | SPD | `B757_MCP_SPD_BUTTON` | 1 | SPD in thrust (UP 166, 248) |
| 14 | ALT HOLD | `B757_MCP_HOLD_2_BUTTON` | 1 | ALT HOLD (UP 189, 248) |
| 15 | Speed Intervene | `B757_MCP_AP_SPD_HOLD_SW` | 1 | Speed Intervene: speed intervention in VNAV (UP 59, 187) |
| 16 | LOC | `B757_MCP_LOC_BUTTON` | 1 | approach: LOC when cleared, then APP (UP 56, 176, 239) |
| 17 | APP | `B757_MCP_APP_BUTTON` | 1 | as LOC (UP 239) |
| 18 | ALT SEL Push | `BBS757_ALTSEL_CLICK` | 2 | ALT SEL push (altitude intervention, gp; section 3) |
| 19 | IAS/MACH | `B757_MCP_AP_SPD_MACH_SW` | 2 | IAS/MACH changeover (gp) |
| 20 | HDG HOLD | `B757_MCP_HOLD_1_BUTTON` | 2 | HDG HOLD (gp) |
| 21 | Bank Limit | `B757_MCP_AP_HDG_BNKLIM` | 2 | Bank limit AUTO in preflight (UP 40, 93), left there |
| 22 | Autopilot Disengage Bar | `B757_MCP_btn_autopilot_disengage` | 3 | disengage bar: backup disconnect, the yoke button is the normal one (gp) |
| 23 | BCRS 1 | `B757_MCP_BCRS_1_BUTTON` | 3 | back course approach, rare (gp) |

### VOR/DME (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | VOR/DME 1 | `B757_MCP_VORDME_1_BUTTON` | 2 | normally AUTO; manual for raw-data identification (UP 40, 93) |
| 2 | VOR/DME 2 | `B757_MCP_VORDME_2_BUTTON` | 2 | same |

### Master warning and caution (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Captain Master Warning and Caution | `B757_MCP_btn_master_pilot` | 1 | reset acknowledges master WARNING/CAUTION (UP 205); the preflight tests raise them (UP 76, 107-110); captain (left seat, UP 77) now first, as on every other L/R panel |
| 2 | First Officer Master Warning and Caution | `B757_MCP_btn_master_copilot` | 1 | as Captain |

## Forward panel

### Main panel switches (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Autobrake | `B757_MAIN_knob_gears_altn_autobrake` | 1 | RTO in preflight and after start (UP 41, 48), landing setting (UP 55, 57), OFF after landing (UP 60) |
| 2 | Recall | `B757_MAIN_RECALL_BUTTON` | 1 | RECALL on the Before Start, After Start and In Range checklists (NC 2; UP 46, 48, 54, 59) |
| 3 | Cancel | `B757_MAIN_CANCEL_BUTTON` | 1 | CANCEL to page through messages before RECALL (UP 136) |
| 4 | Takeoff Config Check | `B757_MAIN_TOCHK_BUTTON` | 1 | takeoff configuration check, Before Takeoff (UP 50, 157) |
| 5 | L Foot Heaters | `B757_MAIN_L_FOOT_HEATERS_SWITCH` | 2 | as required (UP 40), OFF for termination (UP 62; NC 2) |
| 6 | R Foot Heaters | `B757_MAIN_R_FOOT_HEATERS_SWITCH` | 2 | as L; L/R pair adjacent |
| 7 | L Shoulder Heaters | `B757_MAIN_L_SHOULDER_HEATERS_SWITCH` | 2 | as foot heaters |
| 8 | R Shoulder Heaters | `B757_MAIN_R_SHOULDER_HEATERS_SWITCH` | 2 | as L |
| 9 | L Flt Dir | `B757_MAIN_L_FLT_DIR_SWITCH` | 2 | instrument sources NORMAL (UP 40, 95), left there |
| 10 | R Flt Dir | `B757_MAIN_R_FLT_DIR_SWITCH` | 2 | as L |
| 11 | L Flt Nav | `B757_MAIN_L_FLT_NAV_SWITCH` | 2 | Captain L / FMC L (UP 40, 95) |
| 12 | R Flt Nav | `B757_MAIN_R_FLT_NAV_SWITCH` | 2 | FO R / FMC R (UP 40, 95) |
| 13 | Stdby EICAS | `B757_MAIN_STDBY_EICAS_KNOB` | 2 | standby engine indicator AUTO (UP 41), back to AUTO after start if used (UP 48) |
| 14 | HDG Ref | `B757_MAIN_HDG_REF_SWITCH` | 2 | NORM (UP 41) |
| 15 | TOGA | `B757_MAIN_TOGA_BUTTON` | 2 | thrust reference: verify green TO (UP 41, 100); normally set by the FMC |
| 16 | TMSP 1 | `B757_MAIN_1_BUTTON` | 2 | reduced-thrust 1/2 preselection (UP 100, 134) |
| 17 | TMSP 2 | `B757_MAIN_2_BUTTON` | 2 | as 1 |
| 18 | CLB | `B757_MAIN_CLB_BUTTON` | 2 | verify CLB after takeoff (UP 52, 166) |
| 19 | CRZ | `B757_MAIN_CRZ_BUTTON` | 2 | cruise reference (gp) |
| 20 | G/S Inhibit | `BBS757_GS_INHIBIT` | 2 | GPWS glideslope inhibit when deliberately below the glideslope (gp) |
| 21 | L EFI | `B757_MAIN_L_EFI_BUTTON` | 3 | source reversion; checked OUT (UP 40, 95) |
| 22 | R EFI | `B757_MAIN_R_EFI_BUTTON` | 3 | as L |
| 23 | L IRS | `B757_MAIN_L_IRS_BUTTON` | 3 | as EFI |
| 24 | R IRS | `B757_MAIN_R_IRS_BUTTON` | 3 | as L |
| 25 | L Airdata | `B757_MAIN_L_AIRDATA_BUTTON` | 3 | as EFI |
| 26 | R Airdata | `B757_MAIN_R_AIRDATA_BUTTON` | 3 | as L |
| 27 | Altn Gear Guard | `B757_MAIN_PANEL_ALTN_GEAR_SWITCH_GAURD` | 3 | guard, before its switch; 'alternate extension guarded' (UP 41) |
| 28 | Altn Gear | `B757_MAIN_ALTN_GEAR_SWITCH` | 3 | alternate gear extension: non-normal |
| 29 | Alternate Flaps Selector | `B757_MAIN_knob_gears_altn_flaps` | 3 | ALTN FLAPS NORM (UP 34, 41); other positions non-normal |
| 30 | L Leading Edge | `B757_MAIN_L_LEADINGEDGE_BUTTON` | 3 | ALTN FLAPS LE OFF/OUT (UP 34, 41) |
| 31 | L Trailing Edge | `B757_MAIN_L_TRAILINGEDGE_BUTTON` | 3 | ALTN FLAPS TE OFF/OUT (UP 34, 41) |
| 32 | Flap Override Guard | `B757_GAURD_8_SWITCH` | 3 | guard, before its switch |
| 33 | L Flap Ovrd | `B757_MAIN_L_FLAPOVRD_BUTTON` | 3 | GPWS flap override OFF (UP 41) |
| 34 | Gear Override Guard | `B757_GAURD_9_SWITCH` | 3 | guard, before its switch |
| 35 | L Gear Ovrd | `B757_MAIN_L_GEAROVRD_BUTTON` | 3 | GPWS gear override OFF (UP 41) |
| 36 | Terrain Override Guard | `B757_GAURD_10_SWITCH` | 3 | guard, before its switch |
| 37 | L Terr Ovrd | `B757_MAIN_L_TERROVRD_BUTTON` | 3 | GPWS terrain override OFF (UP 41) |
| 38 | L Brake Reserve | `B757_MAIN_L_BRAKERESERVE_BUTTON` | 3 | reserve brakes: non-normal (gp) |
| 39 | CON | `B757_MAIN_CON_BUTTON` | 3 | maximum continuous: engine out (UP 245) |

### Landing gear (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Gear Lever | `B757_MAIN_Landing_Gear_Y` | 1 | UP after liftoff, OFF after retraction (UP 51-52), DOWN for landing (UP 57) |
| 2 | Gear Lock Override | `B757_MAIN_LOCK_OVRD_SWITCH` | 3 | lever lock override: non-normal (gp) |

### Captain EFIS (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | ND Mode | `B757_ADI_1_CTR_KNOB` | 1 | ND Mode: set in preflight (UP 41, 101); MAP/approach mode changes in flight |
| 2 | ND Range | `B757_ADI_1_TFC_KNOB` | 1 | ND Range: set in preflight (UP 41, 101), changed through every phase |
| 3 | WXR | `B757_ADI_1_WXR_BUTTON` | 1 | WXR/TERR on the Before Takeoff checklist (UP 50, 159; NC 2), OFF after landing (UP 60, 192) |
| 4 | TERR | `B757_ADI_1_TERR_BUTTON` | 1 | same checklist lines as WXR (UP 50, 60) |
| 5 | TFC | `B757_ADI_1_TFC_BUTTON` | 2 | traffic on the ND: a display layer (UP 102: 'MAP layers are up to the individual') |
| 6 | CTR | `B757_ADI_1_CTR_BUTTON` | 2 | map centre: display option (UP 101-102) |
| 7 | NAV AID | `B757_ADI_1_NAV-AID_BUTTON` | 2 | map layer, as desired (UP 102) |
| 8 | APRT | `B757_ADI_1_APRT_BUTTON` | 2 | map layer, as desired (UP 102) |
| 9 | DATA | `B757_ADI_1_DATA_BUTTON` | 2 | map layer, as desired (UP 102) |
| 10 | WPT | `B757_ADI_1_WPT_BUTTON` | 2 | map layer, as desired (UP 102) |
| 11 | DH Reset | `B757_DH_RESET_CPT` | 2 | resets the radio-minimums alert (gp); the minimums themselves are set every approach (UP 56, 175) with a knob the map does not hold |
| 12 | INOP | `B757_ADI_1_INOP_BUTTON` | 3 | no function established (section 3) |

### Yokes (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Captain Yoke A/P Disconnect | `btn_yoke_autopilot_disengage_pilot` | 1 | autopilot disconnect for a manual landing or to leave APP (UP 189) (gp) |
| 2 | First Officer Yoke A/P Disconnect | `btn_yoke_autopilot_disengage_copilot` | 1 | as Captain |

### Captain autoland status (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Autoland Status Test 1 | `B757_MAIN_L_AUTOLAND_1_BUTTON` | 2 | ASA TEST 1 (UP 97-98) |
| 2 | Autoland Status Test 2 | `B757_MAIN_L_AUTOLAND_2_BUTTON` | 2 | ASA TEST 2 (UP 97-98) |
| 3 | Autoland Status Reset | `B757_MAIN_L_AUTOLAND_3_BUTTON` | 2 | ASA P/RST after the tests (UP 98-99) and after an autoland (gp) |

### Standby attitude indicator (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Gyro Cage Knob | `BBS757_gyro_knob` | 2 | pull to erect the standby gyro after a cold-and-dark power-up (UP 96) |
| 2 | Gyro Selector | `B757_MAIN_M_Gau_STBY_GYRO_SELECTOR` | 2 | ILS/BCRS selector (UP 96); backup approach display (gp) |

### Captain DDRMI (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Left Needle VOR/ADF | `B757_MAIN_INSTRUMENT_ADF_Knob_LEFT` | 2 | VOR/ADF needle source; ADF as required (UP 42) |
| 2 | Right Needle VOR/ADF | `B757_MAIN_INSTRUMENT_ADF_Knob_RIGHT` | 2 | same |

### Captain chronometer (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | 757 Capt Clock | `BBS_757_CAPT_CLOCK_BUTTON` | 1 | start the timer with the glareshield CLOCK button as the starter engages (UP 145-146); taxi timer after landing (UP 192); section 3 |
| 2 | 757 FO Clock | `BBS_757_FO_CLOCK_BUTTON` | 2 | FO's clock start/stop (tooltip); kept beside the captain's |
| 3 | Clock Button | `INSTRUMENT_Knob_Clock` | 2 | the clock's own CHR button (gp) |
| 4 | Chrono ET | `B757_MAIN_CHRONO_ET_SWITCH` | 2 | elapsed time RUN/HOLD/RESET (gp) |
| 5 | Chrono Set | `B757_MAIN_CHRONO_SET_SWITCH` | 3 | time setting (gp) |
| 6 | Date Button | `INSTRUMENT_Knob_Date` | 3 | date (gp) |

### Oxygen (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Captain Oxygen Test | `OXY_CAPT_TEST_SWITCH001` | 1 | crew oxygen test every preflight (UP 40, 94); 'Oxygen CHECKED' on the Before Start checklist (NC 2; UP 46) |
| 2 | O2 Redbutton CPT | `BBS757_O2_redbutton_CPT` | 1 | the same test: hold the red emergency control 10 s (UP 94) |
| 3 | First Officer Oxygen Test | `OXY_FO_TEST_SWITCH1` | 1 | as Captain (crew oxygen, UP 40) |
| 4 | O2 Redbutton FO | `BBS757_O2_redbutton_FO` | 1 | as Captain |
| 5 | First Observer Oxygen Test | `OXY_OBSV_TEST_SWITCH` | 3 | observer 1 mask: only when the seat is occupied (gp) |
| 6 | O2 Redbutton Jumpseat | `BBS757_O2_redbutton_JUMPSEAT` | 3 | observer 1 (gp) |
| 7 | Second Observer Oxygen Test | `OXY_P61_TEST_SWITCH` | 3 | observer 2 mask (gp) |
| 8 | O2 Redbutton P61 | `BBS757_O2_redbutton_P61` | 3 | observer 2 (gp) |
| 9 | O2 Ears CPT | `O2_EARS_CPT` | 3 | 'O2 mask grab and wear' (tooltip): smoke/depressurization only |
| 10 | O2 Ears FO | `O2_EARS_FO` | 3 | as Captain |
| 11 | O2 Ears Jumpseat | `O2_EARS_JUMPSEAT` | 3 | as Captain |
| 12 | O2 Ears Observer | `O2_EARS_OBSERVER` | 3 | as Captain |

### Cockpit miscellaneous (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Cockpit Door | `B757_COCKPIT_DOOR` | 2 | doors closed before start (UP 46, 135) |

### First officer EFIS (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | ND Mode | `B757_ADI_2_CTR_KNOB` | 1 | ND Mode: set in preflight (UP 41, 101); MAP/approach mode changes in flight |
| 2 | ND Range | `B757_ADI_2_TFC_KNOB` | 1 | ND Range: set in preflight (UP 41, 101), changed through every phase |
| 3 | WXR | `B757_ADI_2_WXR_BUTTON` | 1 | WXR/TERR on the Before Takeoff checklist (UP 50, 159; NC 2), OFF after landing (UP 60, 192) |
| 4 | TERR | `B757_ADI_2_TERR_BUTTON` | 1 | same checklist lines as WXR (UP 50, 60) |
| 5 | TFC | `B757_ADI_2_TFC_BUTTON` | 2 | traffic on the ND: a display layer (UP 102: 'MAP layers are up to the individual') |
| 6 | CTR | `B757_ADI_2_CTR_BUTTON` | 2 | map centre: display option (UP 101-102) |
| 7 | NAV AID | `B757_ADI_2_NAV-AID_BUTTON` | 2 | map layer, as desired (UP 102) |
| 8 | APRT | `B757_ADI_2_APRT_BUTTON` | 2 | map layer, as desired (UP 102) |
| 9 | DATA | `B757_ADI_2_DATA_BUTTON` | 2 | map layer, as desired (UP 102) |
| 10 | WPT | `B757_ADI_2_WPT_BUTTON` | 2 | map layer, as desired (UP 102) |
| 11 | DH Reset | `B757_DH_RESET_FO` | 2 | resets the radio-minimums alert (gp); the minimums themselves are set every approach (UP 56, 175) with a knob the map does not hold |
| 12 | INOP | `B757_ADI_2_INOP_BUTTON` | 3 | no function established (section 3) |

### First officer autoland status (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Autoland Status Test 1 | `B757_MAIN_R_AUTOLAND_1_BUTTON` | 2 | ASA TEST 1 (UP 97-98) |
| 2 | Autoland Status Test 2 | `B757_MAIN_R_AUTOLAND_2_BUTTON` | 2 | ASA TEST 2 (UP 97-98) |
| 3 | Autoland Status Reset | `B757_MAIN_R_AUTOLAND_3_BUTTON` | 2 | ASA P/RST (UP 98-99) |

### First officer DDRMI (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Left Needle VOR/ADF | `FODMEKNOB1` | 2 | VOR/ADF needle source (UP 42) |
| 2 | Right Needle VOR/ADF | `B757_MAIN_INSTRUMENT_ADF_Knob_RIGHT_FO` | 2 | same |

### First officer chronometer (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Clock Button | `INSTRUMENT_Knob_Clock_FO` | 2 | the clock's CHR button (gp); first, as on the captain's |
| 2 | Chrono ET | `B757_MAIN_CHRONO_ET_SWITCH_FO` | 2 | elapsed time (gp) |
| 3 | Chrono Set | `B757_MAIN_CHRONO_SET_SWITCH_FO` | 3 | time setting (gp) |
| 4 | Date Button | `INSTRUMENT_Knob_Date_FO` | 3 | date (gp) |

### Display brightness (new panel)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Captain PFD | `B757_ADI_1_LCD_KNOB` | 2 | display brightness set for readability in preflight (UP 41, 101-102: ADI brightness on the EFIS panel) |
| 2 | First Officer PFD | `B757_ADI_2_LCD_KNOB` | 2 | same; captain/FO pairs adjacent |
| 3 | Captain ND | `B757_HSI_1_SCREEN_KNOB` | 2 | HSI brightness on the EFIS panel (UP 102) |
| 4 | First Officer ND | `B757_HSI_2_SCREEN_KNOB` | 2 | same |
| 5 | Upper EICAS | `B757_EICAS_1_SCREEN_KNOB` | 2 | EICAS display brightness (UP 101) |
| 6 | Lower EICAS | `B757_EICAS_2_SCREEN_KNOB` | 2 | same |
| 7 | Captain CDU | `B757_FMC_1_SCREEN_KNOB` | 2 | CDU brightness (gp) |
| 8 | First Officer CDU | `B757_FMC_2_SCREEN_KNOB` | 2 | same |

## Pedestal

### Control stand (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Parking Brake | `BBS757_PEDESTAL_Park_Brake_Lever` | 1 | SET in preflight (UP 37), released for pushback (UP 46), SET at the stand (UP 61) |
| 2 | Flap Lever | `BBS757_lever_flaps` | 1 | set after start (UP 48), retracted after takeoff (UP 51-52), extended for landing (UP 57), UP after landing (UP 60); moved up from 5th |
| 3 | Speedbrake Armed | `BBS757_SPOILERARMED_CLICK` | 1 | ARMED before landing (UP 57; NC 2) |
| 4 | Speedbrake Down | `BBS757_SPOILERDOWN_CLICK` | 1 | STOWED/DOWN after start and after landing (UP 48, 60) |
| 5 | Speedbrake Up | `BBS757_SPOILERUP_CLICK` | 2 | in-flight drag (UP 178 'speedbrakes ... are tools') and RTO (UP 213) |
| 6 | TO/GA | `ENGINE_Switch_ATDisc_1` | 2 | TO/GA paddle: go-around only, never for takeoff (UP 59, 162) |
| 7 | A/T Disconnect | `B757_AT_DISC_1` | 2 | A/T disconnect: RTO (UP 213), manual thrust (gp) |

### Engines (changed, plan 12 Task 10)

The owner's instruction (2026-10-28): "I wanted engine 1 start then fuel, then engine 2's controls. Right now they have
the start levers, then the fuel levers." The order below replaces ruling R37's (the start selectors, then the fuel
switches: Ignition, L ENG Start, L Fuel Control (PW), R ENG Start, R Fuel Control (PW), then the RR pair). The loaded
type's pair shows (Task 8), so on an RR the pilot tabs Ignition, L ENG Start, L Fuel Control (RR), R ENG Start,
R Fuel Control (RR).

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Ignition | `B757_OVHD_ENG_START_SWITCH` | 1 | Ignition 1 or 2 (UP 38), alternated at shutdown (UP 61, 194) |
| 2 | L ENG Start | `B757_OVHD_L_ENG_START_SWITCH` | 1 | AUTO (UP 38), GND to start (UP 47), AUTO at shutdown (UP 61) |
| 3 | L Fuel Control (PW) | `BBS_fuelcut_PW1` | 1 | CUTOFF (UP 34, 42), RUN to start (UP 47), CUTOFF at shutdown (UP 61) |
| 4 | L Fuel Control (RR) | `BBS_fuelcut_RR1` | 1 | RR variant of the same; RICH for a cold start, then RUN (UP 47, 146, 149) |
| 5 | R ENG Start | `B757_OVHD_R_ENG_START_SWITCH` | 1 | as L |
| 6 | R Fuel Control (PW) | `BBS_fuelcut_PW2` | 1 | as L |
| 7 | R Fuel Control (RR) | `BBS_fuelcut_RR2` | 1 | as L; the RR alternative method 1.6 named, now the order (the owner's Task 10 instruction) |

### TCAS and transponder (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | TCAS Mode | `B757_PED_TCAS_MODE_KNOB` | 1 | TA/RA before start (UP 45, 133), OFF/as required at the gate (UP 62, 197); the app's Squawk Code field follows it |
| 2 | TCAS Xpndr | `B757_PED_TCAS_XPNDR_SWITCH` | 1 | L or R for the pilot flying, or alternated between flights (UP 105) |
| 3 | TCAS Ident | `B757_PED_TCAS_IDENT_SWITCH` | 2 | on ATC request (gp) |
| 4 | TCAS Test | `B757_PED_TCAS_MODE_KNOB_TEST_CLICK` | 2 | TCAS test (UP 42, 106) |

### EICAS control (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | EICAS R Status | `B757_EICAS_R_STATUS_BUTTON` | 1 | STATUS page (its click requests BBS_EICAS_STATUS_PAGE_REQUEST): preflight quantities (UP 36), flight-control check before takeoff (UP 50, 155) |
| 2 | EICAS L Status | `B757_EICAS_L_STATUS_BUTTON` | 1 | ENGINE page (its click requests BBS_EICAS_ENGINE_PAGE_REQUEST): oil quantities (UP 36), back to ENGINE before takeoff (UP 50, 156) |
| 3 | EICAS Computer Switch | `B757_ECAM_LRAUTO_KNOB` | 2 | EICAS computer AUTO (UP 41, 100), left there |
| 4 | EICAS Lwr Thrust Ref | `B757_EICAS_LWR_THRUST-REF_KNOB` | 2 | thrust reference BOTH (UP 41, 100) |
| 5 | Thrust Ref Pull | `BBS757_THRREF_CLICK` | 3 | inner knob IN; pulled = manual EPR reference (UP 41, 100: no green MAN) |
| 6 | EICAS Max Ind | `B757_EICAS_MAX-IND_BUTTON` | 3 | resets recorded exceedances: maintenance (gp) |
| 7 | EICAS Event | `B757_EICAS_EVENT_BUTTON` | 3 | event record for maintenance (UP 101) (gp); section 3 |

### Radio tuning panel 1 (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Comhead 1 Swap | `B757_PED_COMHEAD_1_SWAP_BUTTON` | 1 | active/standby swap: radios set and changed every flight (UP 42, 104, 130, 134) |
| 2 | Nav 1 VHF L | `B757_PED_NAV_1_VHFL_BUTTON` | 2 | picks the radio this panel tunes; normally left on one VHF (gp) |
| 3 | Nav 1 VHF C | `B757_PED_NAV_1_VHFC_BUTTON` | 2 | same (gp) |
| 4 | Nav 1 VHF R | `B757_PED_NAV_1_VHFR_BUTTON` | 2 | same (gp) |
| 5 | Nav 1 HF L | `B757_PED_NAV_1_HFL_BUTTON` | 3 | HF: oceanic/remote only (gp); HF L and HF R now adjacent (AM was between them) |
| 6 | Nav 1 HF R | `B757_PED_NAV_1_HFR_BUTTON` | 3 | HF (gp) |
| 7 | Nav 1 AM | `B757_PED_NAV_1_AM_BUTTON` | 3 | HF AM, works only with an HF mode selected (BlueBird simvar list) |
| 8 | Radio Test | `B757_PED_NAV_1_TEST_BUTTON` | 3 | radio test (gp) |
| 9 | Nav 1 Off | `B757_PED_NAV_1_OFF_BUTTON` | 3 | turns the tuning panel off (gp); moved from 2nd to last |

### Audio panel 1 (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Audio 1 Mic L Light | `B757_PED_AUDIO_1_MIC_L_LIGHT` | 1 | VHF L transmit select: audio panel set to the intended transmit/receive channels (UP 42, 104) |
| 2 | Audio 1 Mic L Light Knob | `B757_PED_AUDIO_1_MIC_L_LIGHT_KNOB` | 1 | VHF L receive, kept beside its transmit select (UP 104) |
| 3 | Audio 1 Mic R Light | `B757_PED_AUDIO_1_MIC_R_LIGHT` | 1 | VHF R transmit (second radio: company / 121.5, UP 134) |
| 4 | Audio 1 Mic R Light Knob | `B757_PED_AUDIO_1_MIC_R_LIGHT_KNOB` | 1 | VHF R receive (UP 134) |
| 5 | Audio 1 Mic C Light | `B757_PED_AUDIO_1_MIC_C_LIGHT` | 2 | VHF C (gp: usually the data/ACARS radio) |
| 6 | Audio 1 Mic C Light Knob | `B757_PED_AUDIO_1_MIC_C_LIGHT_KNOB` | 2 | VHF C receive (gp) |
| 7 | Audio 1 Mic PA Light | `B757_PED_AUDIO_1_MIC_PA_LIGHT` | 2 | PA announcements (gp) |
| 8 | Audio 1 Mic PA Light Knob | `B757_PED_AUDIO_1_MIC_PA_LIGHT_KNOB` | 2 | PA receive (gp) |
| 9 | Audio 1 Mic Int Light | `B757_PED_AUDIO_1_MIC_INT_LIGHT` | 2 | interphone (gp) |
| 10 | Audio 1 Mic Int Light Knob | `B757_PED_AUDIO_1_MIC_INT_LIGHT_KNOB` | 2 | interphone receive (gp) |
| 11 | Audio 1 Mic Lils Light | `B757_PED_AUDIO_1_MIC_LILS_LIGHT_KNOB` | 2 | navaid audio: identify the ILS on approach (UP 56) |
| 12 | Audio 1 Mic Clils Light | `B757_PED_AUDIO_1_MIC_CLILS_LIGHT_KNOB` | 2 | centre ILS ident (UP 56); L/C/R ILS kept together |
| 13 | Audio 1 Mic Rlils Light | `B757_PED_AUDIO_1_MIC_RLILS_LIGHT_KNOB` | 2 | right ILS ident (UP 56) |
| 14 | Audio 1 Mic Lvor Light | `B757_PED_AUDIO_1_MIC_LVOR_LIGHT_KNOB` | 2 | VOR ident for raw data (UP 93) |
| 15 | Audio 1 Mic Rvor Light | `B757_PED_AUDIO_1_MIC_RVOR_LIGHT_KNOB` | 2 | VOR ident (UP 93); L/R pair adjacent |
| 16 | Audio 1 Mic Ladf Light | `B757_PED_AUDIO_1_MIC_LADF_LIGHT_KNOB` | 2 | ADF ident, ADF as required (UP 42) |
| 17 | Audio 1 Mic Radf Light | `B757_PED_AUDIO_1_MIC_RADF_LIGHT_KNOB` | 2 | ADF ident (UP 42) |
| 18 | Audio 1 Mic Mkr Light | `B757_PED_AUDIO_1_MIC_MKR_LIGHT_KNOB` | 2 | marker audio (gp) |
| 19 | Audio 1 Voice Range | `B757_PED_AUDIO_1_VOICE_RANGE_SWITCH` | 2 | VOICE/BOTH/RANGE ident filter (gp) |
| 20 | Audio 1 Mic Lm Light | `B757_PED_AUDIO_1_MIC_LM_LIGHT` | 3 | meaning not established, likely HF L (section 3) |
| 21 | Audio 1 Mic Lm Light Knob | `B757_PED_AUDIO_1_MIC_LM_LIGHT_KNOB` | 3 | receive for the same (section 3) |
| 22 | Audio 1 Mic RR Light | `B757_PED_AUDIO_1_MIC_RR_LIGHT` | 3 | meaning not established, likely HF R (section 3) |
| 23 | Audio 1 Mic RR Light Knob | `B757_PED_AUDIO_1_MIC_RR_LIGHT_KNOB` | 3 | receive for the same (section 3) |
| 24 | Audio 1 Boom Oxy | `B757_PED_AUDIO_1_BOOM_OXY_SWITCH` | 3 | mic on the oxygen mask: only with the mask on (gp) |
| 25 | Audio 1 PTT | `B757_PED_AUDIO_1_PTT_SWITCH` | 3 | BlueBird list: 'no transmit-command consumer was found' |

### Fire protection (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Wheel Well Fire Test | `B757_PED_FIRE_WHLWELL_BUTTON` | 2 | FIRE/OVHT WHL WELL test, preflight (UP 42, 109) |
| 2 | Engine Fire Test | `B757_PED_FIRE_ENG_BUTTON` | 2 | FIRE/OVHT ENG test (UP 42, 109) |
| 3 | Engine, APU and Cargo Fire Test | `B757_PED_FIRE_CARGO_BUTTON` | 2 | FIRE/OVHT ENG/APU/CARGO test (UP 42, 110) |
| 4 | Engine 1 Fire Handle Unlock | `BBS757_FIREUNLOCK1` | 3 | handles checked IN, not rotated (UP 42, 104); unlock sits before its handle |
| 5 | Engine 1 Fire Handle | `BBS757_FIREHANDLE1_CLICK` | 3 | engine fire: emergency |
| 6 | Engine 1 Fire Handle Rotation | `B757_PED_Fire_DISCH_1` | 3 | bottle discharge: emergency |
| 7 | Engine 2 Fire Handle Unlock | `BBS757_FIREUNLOCK2` | 3 | as engine 1 |
| 8 | Engine 2 Fire Handle | `BBS757_FIREHANDLE1_CLICK2` | 3 | as engine 1 |
| 9 | Engine 2 Fire Handle Rotation | `B757_PED_Fire_DISCH_2` | 3 | as engine 1 |
| 10 | APU Fire Handle Unlock | `B757_FIREUNLOCK3` | 3 | APU, as engine 1 |
| 11 | APU Fire Handle | `BBS757_FIREHANDLE1_CLICK3` | 3 | APU fire: emergency |
| 12 | APU Fire Handle Rotation | `B757_PED_Fire_DISCH_3` | 3 | as engine 1 |
| 13 | FWD Cargo Fire | `B757_PED_FWD_CARGOFIRE_BUTTON` | 3 | cargo fire ARM: checked OUT (UP 42, 105) |
| 14 | AFT Cargo Fire | `B757_PED_AFT_CARGOFIRE_BUTTON` | 3 | as FWD |
| 15 | 1 BTL Cargo Fire Guard | `B757_GAURD_11_SWITCH` | 3 | guard, before its switch; guards closed (UP 42, 105) |
| 16 | 1 BTL Cargo Fire | `B757_PED_1BTL_CARGOFIRE_BUTTON` | 3 | bottle discharge: emergency |
| 17 | 2 BTL Cargo Fire Guard | `B757_GAURD_12_SWITCH` | 3 | guard, before its switch |
| 18 | 2 BTL Cargo Fire | `B757_PED_2BTL_CARGOFIRE_BUTTON` | 3 | as 1 BTL |
| 19 | Sys Fail | `B757_PED_SYS_FAIL_BUTTON` | 3 | fire-system FAIL reset (UP 109) (gp); now right before the tests, which moved to the end of the panel in plan 12 task 12 |

Plan 12 task 12 (2026-10-28, the final review's M1): rows 1 to 3, the three tests, moved after row 19, in their own order (Wheel Well Fire Test, Engine Fire Test, Engine, APU and Cargo Fire Test: the preflight flow's): the option the owner picked ("Keep with fire handles") put them after the handles, so the panel opens with the fire handles in a fire. Rows 4 to 19 keep their order; the tiers and sources above stand as they were.

### Stabilizer trim (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Left Rudder Trim 1 s (button) | (app row, placed by its own code) | 1 | rudder trim zero in preflight, after start, before takeoff and after landing (UP 42, 48, 50, 60, 106); engine-out trim (UP 217) |
| 2 | Right Rudder Trim 1 s (button) | (app row, placed by its own code) | 1 | as Left |
| 3 | Left Aileron Trim 1 s (button) | (app row, placed by its own code) | 1 | aileron trim zero in the same flows (UP 42, 48, 50, 60) |
| 4 | Right Aileron Trim 1 s (button) | (app row, placed by its own code) | 1 | as Left |
| 5 | Nose Down Stab Trim 1 s (button) | (app row, placed by its own code) | 2 | alternate electric stab trim: checked NEUTRAL (UP 41, 100); takeoff and 4.0 trim (UP 48, 60) are normally set with the yoke switches; see method 1.6 |
| 6 | Nose Up Stab Trim 1 s (button) | (app row, placed by its own code) | 2 | as Nose Down |
| 7 | Stab Trim C Guard | `B757_AISLE_STAND_STAB_1_GAURD_SWITCH` | 3 | guard, now right before its cutout (was before both cutouts); 'GUARDED CLOSED' (UP 41, 100) |
| 8 | Stab Trim C Cutout | `B757_Stab_trim_C_toggle_switch_CLICK` | 3 | stab trim cutout: non-normal |
| 9 | Stab Trim R Guard | `B757_AISLE_STAND_STAB_2_GAURD_SWITCH` | 3 | guard, before its cutout |
| 10 | Stab Trim R Cutout | `B757_Stab_trim_R_toggle_switch_CLICK` | 3 | as C |

### Weather radar left (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | WX L WX | `B757_PED_WX_L_WX_BUTTON` | 1 | weather radar OFF in preflight (UP 42), selected as required before takeoff (UP 50, 159), OFF after landing (UP 60, 192) |
| 2 | WX L Wxt | `B757_PED_WX_L_WXT_BUTTON` | 2 | WX + turbulence mode (gp) |
| 3 | WX L Map | `B757_PED_WX_L_MAP_BUTTON` | 2 | ground-mapping mode (gp) |
| 4 | WX L Gcs | `B757_PED_WX_L_GCS_BUTTON` | 2 | ground-clutter suppression (gp) |
| 5 | Weather Radar Test | `B757_PED_WX_L_TEST_BUTTON` | 2 | radar test (gp) |
| 6 | WX L Tfr | `B757_PED_WX_L_TFR_BUTTON` | 3 | transfer of radar control between sides (gp) |

### Radio tuning panel 2 (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Comhead 2 Swap | `B757_PED_COMHEAD_2_SWAP_BUTTON` | 1 | active/standby swap: radios set and changed every flight (UP 42, 104, 130, 134) |
| 2 | Nav 2 VHF L | `B757_PED_NAV_2_VHFL_BUTTON` | 2 | picks the radio this panel tunes; normally left on one VHF (gp) |
| 3 | Nav 2 VHF C | `B757_PED_NAV_2_VHFC_BUTTON` | 2 | same (gp) |
| 4 | Nav 2 VHF R | `B757_PED_NAV_2_VHFR_BUTTON` | 2 | same (gp) |
| 5 | Nav 2 HF L | `B757_PED_NAV_2_HFL_BUTTON` | 3 | HF: oceanic/remote only (gp); HF L and HF R now adjacent (AM was between them) |
| 6 | Nav 2 HF R | `B757_PED_NAV_2_HFR_BUTTON` | 3 | HF (gp) |
| 7 | Nav 2 AM | `B757_PED_NAV_2_AM_BUTTON` | 3 | HF AM, works only with an HF mode selected (BlueBird simvar list) |
| 8 | Radio Test | `B757_PED_NAV_2_TEST_BUTTON` | 3 | radio test (gp) |
| 9 | Nav 2 Off | `B757_PED_NAV_2_OFF_BUTTON` | 3 | turns the tuning panel off (gp); moved from 2nd to last |

### Radio tuning panel 3 (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Comhead 3 Swap | `B757_PED_COMHEAD_3_SWAP_BUTTON` | 1 | active/standby swap: radios set and changed every flight (UP 42, 104, 130, 134) |
| 2 | Nav 3 VHF L | `B757_PED_NAV_3_VHFL_BUTTON` | 2 | picks the radio this panel tunes; normally left on one VHF (gp) |
| 3 | Nav 3 VHF C | `B757_PED_NAV_3_VHFC_BUTTON` | 2 | same (gp) |
| 4 | Nav 3 VHF R | `B757_PED_NAV_3_VHFR_BUTTON` | 2 | same (gp) |
| 5 | Nav 3 HF L | `B757_PED_NAV_3_HFL_BUTTON` | 3 | HF: oceanic/remote only (gp); HF L and HF R now adjacent (AM was between them) |
| 6 | Nav 3 HF R | `B757_PED_NAV_3_HFR_BUTTON` | 3 | HF (gp) |
| 7 | Nav 3 AM | `B757_PED_NAV_3_AM_BUTTON` | 3 | HF AM, works only with an HF mode selected (BlueBird simvar list) |
| 8 | Radio Test | `B757_PED_NAV_3_TEST_BUTTON` | 3 | radio test (gp) |
| 9 | Nav 3 Off | `B757_PED_NAV_3_OFF_BUTTON` | 3 | turns the tuning panel off (gp); moved from 2nd to last |

### Audio panel 2 (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Audio 2 Mic L Light | `B757_PED_AUDIO_2_MIC_L_LIGHT` | 1 | VHF L transmit select: audio panel set to the intended transmit/receive channels (UP 42, 104) |
| 2 | Audio 2 Mic L Light Knob | `B757_PED_AUDIO_2_MIC_L_LIGHT_KNOB` | 1 | VHF L receive, kept beside its transmit select (UP 104) |
| 3 | Audio 2 Mic R Light | `B757_PED_AUDIO_2_MIC_R_LIGHT` | 1 | VHF R transmit (second radio: company / 121.5, UP 134) |
| 4 | Audio 2 Mic R Light Knob | `B757_PED_AUDIO_2_MIC_R_LIGHT_KNOB` | 1 | VHF R receive (UP 134) |
| 5 | Audio 2 Mic C Light | `B757_PED_AUDIO_2_MIC_C_LIGHT` | 2 | VHF C (gp: usually the data/ACARS radio) |
| 6 | Audio 2 Mic C Light Knob | `B757_PED_AUDIO_2_MIC_C_LIGHT_KNOB` | 2 | VHF C receive (gp) |
| 7 | Audio 2 Mic PA Light | `B757_PED_AUDIO_2_MIC_PA_LIGHT` | 2 | PA announcements (gp) |
| 8 | Audio 2 Mic PA Light Knob | `B757_PED_AUDIO_2_MIC_PA_LIGHT_KNOB` | 2 | PA receive (gp) |
| 9 | Audio 2 Mic Int Light | `B757_PED_AUDIO_2_MIC_INT_LIGHT` | 2 | interphone (gp) |
| 10 | Audio 2 Mic Int Light Knob | `B757_PED_AUDIO_2_MIC_INT_LIGHT_KNOB` | 2 | interphone receive (gp) |
| 11 | Audio 2 Mic Lils Light | `B757_PED_AUDIO_2_MIC_LILS_LIGHT_KNOB` | 2 | navaid audio: identify the ILS on approach (UP 56) |
| 12 | Audio 2 Mic Clils Light | `B757_PED_AUDIO_2_MIC_CLILS_LIGHT_KNOB` | 2 | centre ILS ident (UP 56); L/C/R ILS kept together |
| 13 | Audio 2 Mic Rlils Light | `B757_PED_AUDIO_2_MIC_RLILS_LIGHT_KNOB` | 2 | right ILS ident (UP 56) |
| 14 | Audio 2 Mic Lvor Light | `B757_PED_AUDIO_2_MIC_LVOR_LIGHT_KNOB` | 2 | VOR ident for raw data (UP 93) |
| 15 | Audio 2 Mic Rvor Light | `B757_PED_AUDIO_2_MIC_RVOR_LIGHT_KNOB` | 2 | VOR ident (UP 93); L/R pair adjacent |
| 16 | Audio 2 Mic Ladf Light | `B757_PED_AUDIO_2_MIC_LADF_LIGHT_KNOB` | 2 | ADF ident, ADF as required (UP 42) |
| 17 | Audio 2 Mic Radf Light | `B757_PED_AUDIO_2_MIC_RADF_LIGHT_KNOB` | 2 | ADF ident (UP 42) |
| 18 | Audio 2 Mic Mkr Light | `B757_PED_AUDIO_2_MIC_MKR_LIGHT_KNOB` | 2 | marker audio (gp) |
| 19 | Audio 2 Voice Range | `B757_PED_AUDIO_2_VOICE_RANGE_SWITCH` | 2 | VOICE/BOTH/RANGE ident filter (gp) |
| 20 | Audio 2 Mic Lm Light | `B757_PED_AUDIO_2_MIC_LM_LIGHT` | 3 | meaning not established, likely HF L (section 3) |
| 21 | Audio 2 Mic Lm Light Knob | `B757_PED_AUDIO_2_MIC_LM_LIGHT_KNOB` | 3 | receive for the same (section 3) |
| 22 | Audio 2 Mic RR Light | `B757_PED_AUDIO_2_MIC_RR_LIGHT` | 3 | meaning not established, likely HF R (section 3) |
| 23 | Audio 2 Mic RR Light Knob | `B757_PED_AUDIO_2_MIC_RR_LIGHT_KNOB` | 3 | receive for the same (section 3) |
| 24 | Audio 2 Boom Oxy | `B757_PED_AUDIO_2_BOOM_OXY_SWITCH` | 3 | mic on the oxygen mask: only with the mask on (gp) |
| 25 | Audio 2 PTT | `B757_PED_AUDIO_2_PTT_SWITCH` | 3 | BlueBird list: 'no transmit-command consumer was found' |

### Weather radar right (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | WX R WX | `B757_PED_WX_R_WX_BUTTON` | 1 | weather radar OFF in preflight (UP 42), selected as required before takeoff (UP 50, 159), OFF after landing (UP 60, 192) |
| 2 | WX R Wxt | `B757_PED_WX_R_WXT_BUTTON` | 2 | WX + turbulence mode (gp) |
| 3 | WX R Map | `B757_PED_WX_R_MAP_BUTTON` | 2 | ground-mapping mode (gp) |
| 4 | WX R Gcs | `B757_PED_WX_R_GCS_BUTTON` | 2 | ground-clutter suppression (gp) |
| 5 | WX R Tfr | `B757_PED_WX_R_TFR_BUTTON` | 3 | transfer of radar control between sides (gp) |

### Pedestal radio panels (unchanged)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | ILS Test | `B757_PED_ILS_TEST_BUTTON` | 3 | ILS receiver test (gp) |

## Other

### P61 panel (changed)

| # | Control | Node | Tier | Why, and the source |
|---|---|---|---|---|
| 1 | Flight Recorder | `B757_VC_P61_SWITCH_2` | 2 | Flight Recorder: P61 check 'recorder NORM' (UP 36, 75) |
| 2 | Service Interphone | `B757_VC_P61_SWITCH_1` | 2 | Service Interphone: P61 check 'OFF' (UP 36, 75) |
| 3 | Squib Test | `B757_P6_SQUIBS_BUTTON2` | 2 | squib test: hold, nine green lamps (UP 36, 75) |
| 4 | Ground Prox Hyd Gen Test | `B757_VC_P61_SWITCH_7` | 2 | Ground Prox: EGPWS test (UP 36, 75-76) |
| 5 | Air Data Stall Left Test | `B757_VC_P61_SWITCH_10` | 2 | stall-warning test, left first (UP 36, 76) |
| 6 | Air Data Stall Right Test | `B757_VC_P61_SWITCH_9` | 2 | stall-warning test, right (UP 36, 76) |
| 7 | TO and Land Config Test | `B757_VC_P61_SWITCH_8` | 2 | CONFIG T/O and LDG tests (UP 36, 76) |
| 8 | Equip Cooling Test | `B757_VC_P61_SWITCH_3` | 3 | equipment cooling test: outside the crew flow (UP 76) (gp) |
| 9 | Yaw Damp Test | `B757_VC_P61_SWITCH_4` | 3 | yaw damper test: outside the crew flow (gp) |
| 10 | Duct Leak Fuel Qty Test | `B757_VC_P61_SWITCH_5` | 3 | duct leak / fuel quantity test: outside the crew flow (gp) |
| 11 | Wing AI Window Heat Test | `B757_VC_P61_SWITCH_6` | 3 | wing anti-ice test is maintenance only, window/probe heat test outside the crew flow (UP 76) |
| 12 | P61 Glass Guard 1 | `BBS_B757_P61_GLASS_GAURD_1` | 3 | flight-control shutoff valve guard, before its switch; 'guarded ON' (UP 36, 75) |
| 13 | P61 Press Main Button L | `B757_P61_PRESS_MAIN_BUTTON_L` | 3 | flight-control shutoff valve (component P61_Flight_Control_Shutoff_Valves): non-normal |
| 14 | P61 Glass Guard 2 | `BBS_B757_P61_GLASS_GAURD_2` | 3 | guard, before its switch |
| 15 | P61 Press Main Button C | `B757_P61_PRESS_MAIN_BUTTON_C` | 3 | as L |
| 16 | P61 Glass Guard 3 | `BBS_B757_P61_GLASS_GAURD_3` | 3 | guard, before its switch |
| 17 | P61 Press Main Button R | `B757_P61_PRESS_MAIN_BUTTON_R` | 3 | as L |
| 18 | P61 Press Button 1 | `P61_PRESS_Button_1` | 3 | EICAS maintenance page ECS/MSG (component name): maintenance |
| 19 | P61 Press Button 2 | `P61_PRESS_Button_2` | 3 | maintenance page ELEC/HYD |
| 20 | P61 Press Button 3 | `P61_PRESS_Button_3` | 3 | maintenance page PERF/APU |
| 21 | P61 Press Button 4 | `P61_PRESS_Button_4` | 3 | maintenance page CONF/MCDP |
| 22 | P61 Press Button 5 | `P61_PRESS_Button_5` | 3 | maintenance page ENG/EXCD |
| 23 | P61 Press Button 6 | `P61_PRESS_Button_6` | 3 | maintenance page EPCS |

Plan 12 task 5 (2026-10-28): rows 4 to 11, the eight `B757_VC_P61_SWITCH_3` to `_10` test switches, became buttons placed by the app (task 5b) and are no longer map nodes; the tiers and sources above stand as they were.

Plan 12 task 12 (2026-10-28, the final review's I1): row 10's switch has no buttons: nothing in BlueBird reads `L:BBS757_DUCT_LEAK_FUEL_QTY_P61_TEST_SW`, and holding it on the ground with BBS Nest running changed no variable (flight-12.md).

## Daily-use order research: the notes order_data.py refers to

Verbatim from the researcher's report (2026-10-28), the parts its reasons cite as "method 1.x" and "section 3".

### Sources

PDF pages: "UP n" is BlueBird_757_User_Procedures_v1.01.pdf, PDF page n (PDF pages 34-199 are printed page n + 1; the
Complete Flight Guide holds the same pages 6 PDF pages later). "NC 2" is BlueBird_757_Normal_Checklist_v1.01.pdf, PDF
page 2 (printed 201). "(gp)" is general 757 practice, used for tier 2 and 3 placements only (one exception: the yoke A/P
disconnect, where it backs a UP citation). Node meanings come from curation.json, BlueBird's simvar list and the vendor
click code (docs/b757/evidence/controls/control-map.tsv). The flow is the Simplified Procedures (UP 34-62), cold and
dark to final securing, with the Expanded Procedures for detail; abnormal and maneuver pages (UP 205 on) are cited only
to show a control is non-normal.

### Tiers

- Tier 1: moved by the flow during the flight cycle, or named as an action line after the one-time preflight setup;
  per-flight conventions count (AUTO 1/2, ignition 1/2 and transponder L/R alternated between flights, UP 61, 105); a
  test on the checklist counts ("Oxygen CHECKED").
- Tier 2: set or checked once in preflight and not moved later; preflight tests not on the checklist (CVR, squib, EGPWS,
  stall, config, fire, TCAS, ASA); display options, comfort items, "as required" items.
- Tier 3: non-normal or emergency controls, maintenance, tests outside the crew flow (UP 76), guarded switches only
  checked in position, and guards of tier 3 switches.

Inside a tier: tier 1 in the order the flow first uses each control, L/R pairs adjacent; tiers 2 and 3 in the order the
flow first names them, anything unnamed in its current order. A tier 3 guard sits just before its switch (guards on the
Switch guards panel cover switches on other panels, so they are ordered among themselves).

### Judgment calls (method 1.1-1.7)

1. Emergency lights guard: it stays just after its tier 1 switch, because the app opens and closes it itself (curation
   PLAN 02 TASK 3, Bbs757Guarded). Swap the two rows for guard-first here too.
2. Electrical: assumes cold and dark leaves the bus ties and generator controls in AUTO/ON; Dirty Panel States can
   change that (UP 64).
3. Exterior lights: the order follows UP 158 literally. Option: L/R Landing up to 2nd and 3rd, as they have the most
   uses per flight.
4. Alternate stab trim: the buttons are tier 2, as the flow only checks those switches NEUTRAL (UP 41) and normal trim is
   the yoke switches. Raise them to tier 1 if they become the pilot's normal way to set trim in the app.
5. Engines: unchanged (ruling R37). An RR alternative: L Start, L Fuel PW, L Fuel RR, R Start, R Fuel PW, R Fuel RR.
6. Weather radar: future tilt and gain knobs would go right after WX (gp). (Plan 12 leaves them out: nothing reads them.)
7. App rows: the app's Squawk Code field stays right after TCAS Mode.

### Section 3: controls whose meaning could not be established

1. B757_PED_AUDIO_1/2_MIC_LM_LIGHT and _RR_LIGHT, with their knobs: probably HF L and HF R, by elimination. Tier 3.
2. B757_OVHD_FLT_INT_BUTTON: BlueBird calls it only "Flight INT button"; function unknown.
3. B757_ADI_1/2_INOP_BUTTON: a toggle on the EFIS panel with no function found.
4. B757_CABIN_WORK_LT002: labelled Cabin Work Light, but BlueBird's list calls it the flight-attendant emergency-light
   override; the label may be wrong.
5. BBS_757_CAPT_CLOCK_BUTTON: assumed to be the manual's "glareshield CLOCK button" (UP 145); the other candidate is
   INSTRUMENT_Knob_Clock.
6. BBS757_ALTSEL_CLICK: probably altitude intervention; the manuals don't describe it.
7. Weather radar: the map has no OFF control although the flows say "radar OFF"; TFR, GCS, WXT and MAP rest on general
   practice.
8. B757_EICAS_EVENT_BUTTON: its click also fires H:BBS_757_EICAS_DEBUG, which does something unknown.
9. B757_PED_SYS_FAIL_BUTTON: assumed to be the fire panel's FAIL/P-RESET (UP 109).
10. B757_MAIN_STDBY_EICAS_KNOB: assumed to be the "secondary engine indications" selector of UP 48.

Two labels mislead although their meaning is established: EICAS L Status is the ENGINE page button and EICAS R Status
the STATUS page button (from their click code); the vendor component names make "O2 Ears Observer" observer 2, not
observer 1 (it belongs with the P61 test).

## Plan 12 Task 9: the panel lists follow the preflight flow's changes

The owner (2026-10-28) said "reorder", then gave the rule "Actions only". The controller's round 3 and round 4 rulings read it in that spirit: a step counts only if it changes a control on a normal flight. Each section's panels (`panel_order`) are in the order of the first step of BlueBird's Simplified Procedures (UP 34-62, cold and dark to final securing) that changes one of the panel's controls. The key is the PDF page, then the line on that page, as that page's `pdftotext -f n -l n -layout` text numbers it.

Steps that count:
- a press of a test or a button;
- setting a value: a landing altitude, a heading, a speed, a frequency, a flap or trim setting;
- ON, ARM, START, SET, or a selector to a position the control does not rest in cold and dark;
- (round 4) a step that sets a radio, audio or ILS value: a frequency, a course, a receiver or transmitter selection, in words that say SET or TUNE. It counts even when it says AS REQUIRED, because the radios are tuned on every flight.

Steps that do not count:
- a step that only checks or verifies a position: the whole cockpit safety inspection (UP 34), and any CHECK, VERIFY, CONFIRM or CHECKED step elsewhere;
- any other step marked AS REQUIRED or AS DESIRED, which is optional (AS APPROPRIATE is read the same way). A PARK or PARK AS APPROPRIATE step of the radios does not count either;
- a step that sets a control to the position it already rests in cold and dark ("NORMAL", "NORM", "AUTO", "OFF", or an ON or SET of a control already on or set). Such a step confirms a position rather than moves it. The resting positions are BlueBird's own: the named L:var values of its cold and dark panel state, `Data/panel_states/coldanddark_wpb.snap`.

A step counts for a panel when its words name one of the panel's controls as the manual writes them ("BATTERY SWITCH", "AUTOBRAKE SELECTOR"). Panels first changed in the same line keep their Task 7 order. The panels no step changes follow the rest, also in their Task 7 order.

Two starts are pinned whatever the flow gives:
- The Pedestal keeps Control stand, Engines, TCAS and transponder first, which plan 01's frozen `Bbs757DefinitionTests` pins.
- The Forward panel opens with Brakes, the autobrake's panel. This is the owner's pin: "I really want forward to open to the panel with autobrakes first because that's what will be used most". The flow agrees: the autobrake (41:19, OFF to RTO) is the section's first change.

The app's own panels are not in `panel_order` and keep their places by their code: Rudder and aileron trim right after Stabilizer trim, and the COM radios right after TCAS and transponder. Only the order of the panels moved: no control, label or position changed, and the order inside each panel is the one above.

The derivation is `flow_order.py` in the plan 12 workspace. The tables below are its `--markdown` output, unedited. Each panel's first change is given with why it counts and the manual's words. The last column lists the steps before it that do not count: "check", "optional", or "confirms" (already there cold and dark).

How the script reads the steps:

- **Checklist pages** (no dot leaders): the layout prints a response at the right end of the line above its item when a description line comes first. There a response waiting on the line above is the item's ("LOWER EICAS ... STATUS DISPLAY", 50:12). PDF page 57 is read as printed in its two columns.
- **Setting steps:** a step that sets a control is compared with the cold and dark state, control by control, through a table of which L:vars it sets to which values. A setting step with no entry in that table stops the run, so no step before a panel's first change is left unread.

Judgment calls:

- AUTO for the standby engine indicator and NORM for the heading reference are value 0: the value in all nine of BlueBird's panel states. The map names those values only Off and On.
- Hydraulics counts at the Before Start "HYDRAULIC PANEL ... SET / PUMPS SELECTED ON" (46:29), because the engine pumps rest ON. Fuel counts at "FUEL PANEL ... SET" (46:32), whose description selects the pumps ON. Exterior lights counts at "BEACON ... ON" (46:53), because the position lights rest ON cold and dark. Air conditioning counts at "PACKS ... AUTO" (48:13): the bleeds, isolation, trim air and recirculation fans rest where the flow sets them.
- Pressurization counts at 39:27 for the landing elevation, a value. AUTO RATE at index and the mode selector AUTO 1 rest there.
- EICAS control counts at the Before Takeoff STATUS display press (50:12). Its own "EICAS CONTROL PANEL ... SET" (41:33) sets Computer AUTO, thrust reference BOTH and the inner knob IN, which is where they rest.
- Both EFIS panels count at the radio minimums (55:26, a value set on the EFIS control panel's minimums knob, which the map leaves out). Their earlier steps are AS REQUIRED.
- Stabilizer trim counts at 48:29, "stabilizer set from the applicable accepted performance data". In the app the stabilizer is set with this panel's alternate stab trim buttons.
- The flight deck door rests Open cold and dark, so "DOORS ... CLOSED" (46:22) changes it.
- "VHF / AUDIO / ADF ... SET AS REQUIRED" (42:18) counts for all three radio tuning panels and both audio panels, which keep their Task 7 order among themselves. "ILS ... SET OR PARKED AS REQUIRED" (42:27) counts for ILS tuning, because it sets the ILS. The after-landing "ILS CONTROLS ... PARK AS APPROPRIATE" (60:31) would not.
- Steps that only confirm a resting position, or are AS REQUIRED, leave these panels not changed in the flow:
  - Electronic engine control and Yaw dampers (both ON);
  - Panel lighting (as desired);
  - Ram air turbine and engine limiters (ON);
  - Anti-ice, Wipers and Equipment cooling (OFF);
  - VOR/DME (as required);
  - the foot and shoulder heat (as required);
  - the instrument source selectors (NORMAL);
  - Heading reference (NORM), Ground proximity overrides (OFF) and Standby instruments (AUTO);
  - Weather radar (OFF).

### Overhead

| # | Panel | First change (UP page:line) | Why it counts: the manual's words | Not counted before it |
|---|---|---|---|---|
| 1 | Electrical | 35:10 | moves the battery switch from Off (cold and dark) to ON: "BATTERY SWITCH ... ON" |  |
| 2 | APU | 35:29 | moves the APU selector from Off (cold and dark) to START, then ON: "APU SELECTOR ... START, THEN ON" |  |
| 3 | IRS | 36:17 | moves the three IRS selectors from OFF (cold and dark) to NAV: "IRS SELECTORS ... OFF, THEN NAV" |  |
| 4 | CVR | 38:29 | a press of a test or a button: "CVR ... TEST" |  |
| 5 | Emergency lights and passenger oxygen | 38:35 | moves the emergency lights switch from OFF (cold and dark) to ARMED: "EMERGENCY LIGHTS ... ARMED" |  |
| 6 | Window heat | 39:20 | moves the four window heat switches from Off (cold and dark) to ON: "WINDOW HEAT ... ON" |  |
| 7 | Pressurization | 39:27 | sets a value: the landing elevation (with AUTO RATE at index and the mode selector AUTO 1 or 2): "CABIN ALTITUDE CONTROLS ... SET" |  |
| 8 | Hydraulics | 46:29 | moves the electric hydraulic pumps from Off (cold and dark) to ON (R, then C, then L): "HYDRAULIC PANEL ... SET / PUMPS SELECTED ON" | 34:22 check, 38:16 confirms, 38:17 check |
| 9 | Fuel | 46:32 | moves the left and right fuel pumps from Off (cold and dark) to ON: "FUEL PANEL ... SET" | 39:9 confirms, 39:10 confirms, 39:11 check |
| 10 | Passenger signs | 46:36 | moves the seat belt signs from OFF (cold and dark) to ON: "SEAT BELT SIGNS ... ON" | 39:23 optional |
| 11 | Exterior lights | 46:53 | moves the beacon (Anti-col Red Lights) from Off (cold and dark) to ON: "BEACON ... ON" | 34:28 check, 38:33 check, 39:16 confirms, 39:17 confirms, 39:18 confirms, 39:19 optional |
| 12 | Air conditioning and bleed | 48:13 | moves the pack selectors from OFF (cold and dark) to AUTO: "PACKS ... AUTO" | 39:33 confirms, 39:36 confirms, 39:39 optional, 39:42 confirms, 39:43 confirms, 39:44 confirms, 47:12 confirms, 47:15 confirms, 47:18 confirms |
| 13 | SELCAL and cabin call | 55:32 | a press of a test or a button: "Press one of the blue cabin-call buttons to sound the hi-lo chime for landing preparation" |  |
| 14 | Electronic engine control (RR) | not changed in the flow |  | 38:12 confirms |
| 15 | Yaw dampers | not changed in the flow |  | 38:13 confirms |
| 16 | Panel lighting | not changed in the flow |  | 38:33 optional, 61:25 optional |
| 17 | Ram air turbine and engine limiters | not changed in the flow |  | 38:41 check, 38:44 confirms |
| 18 | Anti-ice | not changed in the flow |  | 39:14 confirms, 48:11 optional, 61:17 confirms |
| 19 | Wipers | not changed in the flow |  | 39:15 confirms |
| 20 | Equipment cooling | not changed in the flow |  | 39:30 confirms |
| 21 | Indicator lights and door lock | not changed in the flow |  | 46:24 check |

### Glareshield

| # | Panel | First change (UP page:line) | Why it counts: the manual's words | Not counted before it |
|---|---|---|---|---|
| 1 | MCP | 40:13 | moves the flight director switches from Off (cold and dark) to ON: "FLIGHT DIRECTOR SWITCHES ... ON" |  |
| 2 | VOR/DME | not changed in the flow |  | 40:10 optional |
| 3 | Master warning and caution | not changed in the flow |  |  |

### Forward panel

| # | Panel | First change (UP page:line) | Why it counts: the manual's words | Not counted before it |
|---|---|---|---|---|
| 1 | Brakes (pinned by the owner) | 41:19 | moves the autobrake selector from OFF (cold and dark) to RTO: "AUTOBRAKE SELECTOR ... RTO" |  |
| 2 | Caution, config and G/S inhibit | 50:33 | a press of a test or a button: "Press the CONFIG (T/O CHK) button near RECALL" | 46:42 check, 48:37 check |
| 3 | Landing gear | 51:30 | moves the gear lever from DOWN (cold and dark) to UP: "POSITIVE RATE ... GEAR UP" | 34:14 check, 41:22 check |
| 4 | Captain foot and shoulder heat | not changed in the flow |  | 40:20 optional |
| 5 | Captain instrument source selectors | not changed in the flow |  | 40:21 confirms |
| 6 | Heading reference | not changed in the flow |  | 41:20 confirms |
| 7 | Thrust mode select | not changed in the flow |  | 41:21 check |
| 8 | Alternate flaps | not changed in the flow |  | 34:20 check, 34:21 check, 41:24 check |
| 9 | Ground proximity overrides | not changed in the flow |  | 41:29 confirms |
| 10 | First officer instrument source selectors | not changed in the flow |  | 40:21 confirms |
| 11 | First officer foot and shoulder heat | not changed in the flow |  | 40:20 optional |
| 12 | Yokes | not changed in the flow |  |  |
| 13 | Captain autoland status | not changed in the flow |  | 41:15 check |
| 14 | Standby instruments | not changed in the flow |  | 41:18 confirms |
| 15 | Captain DDRMI | not changed in the flow |  |  |
| 16 | Captain chronometer | not changed in the flow |  |  |
| 17 | First officer autoland status | not changed in the flow |  | 41:15 check |
| 18 | First officer DDRMI | not changed in the flow |  |  |
| 19 | First officer chronometer | not changed in the flow |  |  |

### Pedestal

| # | Panel | First change (UP page:line) | Why it counts: the manual's words | Not counted before it |
|---|---|---|---|---|
| 1 | Control stand (kept first: frozen test) | 48:23 | sets a value: the takeoff flap setting (the flap lever rests at UP cold and dark): "FLAPS ... SET / VERIFY" | 34:17 check, 37:10 confirms, 42:10 confirms, 42:13 check, 46:9 confirms, 46:49 confirms |
| 2 | Engines (kept first: frozen test) | 47:22 | moves an engine start selector from AUTO (cold and dark) to GND: "ENGINE START SELECTOR ... GND" | 34:27 check, 38:47 confirms, 38:50 confirms, 42:14 confirms |
| 3 | TCAS and transponder (kept first: frozen test) | 42:30 | a press of a test or a button: "TCAS TEST ... INITIATE / VERIFY" | 42:24 optional |
| 4 | Radio tuning panel 1 | 42:18 | sets a radio value (frequencies, courses, transmit and receive), every flight: "VHF / AUDIO / ADF ... SET AS REQUIRED" |  |
| 5 | Audio panel 1 | 42:18 | sets a radio value (frequencies, courses, transmit and receive), every flight: "VHF / AUDIO / ADF ... SET AS REQUIRED" |  |
| 6 | Radio tuning panel 2 | 42:18 | sets a radio value (frequencies, courses, transmit and receive), every flight: "VHF / AUDIO / ADF ... SET AS REQUIRED" |  |
| 7 | Radio tuning panel 3 | 42:18 | sets a radio value (frequencies, courses, transmit and receive), every flight: "VHF / AUDIO / ADF ... SET AS REQUIRED" |  |
| 8 | Audio panel 2 | 42:18 | sets a radio value (frequencies, courses, transmit and receive), every flight: "VHF / AUDIO / ADF ... SET AS REQUIRED" |  |
| 9 | ILS tuning | 42:27 | sets a radio value (frequencies, courses, transmit and receive), every flight: "ILS ... SET OR PARKED AS REQUIRED" |  |
| 10 | Fire protection | 42:43 | a press of a test or a button: "Press and hold the WHL WELL test button" | 42:21 check, 42:33 check, 42:42 check |
| 11 | Stabilizer trim | 48:29 | sets a value: the stabilizer trim from the takeoff data (with this panel's alternate stab trim buttons): "stabilizer set from the applicable accepted performance data" | 41:25 check, 41:26 check |
| 12 | EICAS control | 50:12 | presses the STATUS display button (EICAS R Status) for the lower EICAS STATUS page: "LOWER EICAS ... STATUS DISPLAY" | 36:21 check, 36:24 check, 36:27 check, 41:33 confirms |
| 13 | Captain EFIS | 55:26 | sets a value: the radio minimums (the EFIS control panel's minimums knob): "MINIMUMS ... ___ RADIO / ___ BARO" | 41:36 optional, 50:50 optional |
| 14 | First officer EFIS | 55:26 | sets a value: the radio minimums (the EFIS control panel's minimums knob): "MINIMUMS ... ___ RADIO / ___ BARO" | 41:36 optional, 50:50 optional |
| 15 | CDU brightness | not changed in the flow |  |  |
| 16 | Weather radar | not changed in the flow |  | 42:17 confirms, 60:32 confirms |

### Other

| # | Panel | First change (UP page:line) | Why it counts: the manual's words | Not counted before it |
|---|---|---|---|---|
| 1 | P61 panel | 36:38 | a press of a test or a button: "SQUIB TEST ... HOLD / CHECK" | 36:32 check |
| 2 | Oxygen | 40:16 | a press of a test or a button: "CREW OXYGEN EQUIPMENT ... TEST" | 36:23 check |
| 3 | Flight deck door | 46:22 | moves the flight deck door from Open (cold and dark) to CLOSED: "DOORS ... CLOSED" |  |
| 4 | Forward attendant panel | not changed in the flow |  |  |
| 5-6 | Windows and sun visors, Seats and stowage (plan 12 task 11, new) | not changed in the flow |  | at the end of Other, in that order |
