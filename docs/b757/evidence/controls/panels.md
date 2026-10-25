# Panels: bluebirdsim-aircraft-757-200

One section per banner comment that labels controls or lights, in file order (a banner the vendor reused is one section); "(after X)" holds what the vendor placed after section X closed and before the next banner. Kinds, notes and labels as in control-map.tsv; a label that only says DECREASE/INCREASE or ON/OFF counts as no label. A note every control of a kind shares in a panel is given once for the kind.

## File `SimObjects/Airplanes/bbs-757-200-base/model/BBS_752_BASE_INTERIOR.xml`

### RUDDER TRIM_LIMIT

- Controls: 3 (multi-position switch 3)
- Lights: 0
- No label, wording must come from the node name (1):
  - multi-position switch (1): `B757_Pedestal_switch_STAB_TRIM`

### YOKES + RUDDER + TILLER SYSTEM

- Controls: 8 (multi-position switch 2, held button 2, other 4)
- Lights: 0
- Irregular (6):
  - `btn_yoke_autopilot_disengage_pilot` (held button): state not an L:var (O:_AnimProgress); K: event; Update code writes its vars (not only an init)
  - `btn_yoke_autopilot_disengage_copilot` (held button): state not an L:var (O:_AnimProgress); K: event; Update code writes its vars (not only an init)
  - every other (4): state not an L:var; stock Asobo template; flight control
- No label, wording must come from the node name (6):
  - held button (2): `btn_yoke_autopilot_disengage_pilot`, `btn_yoke_autopilot_disengage_copilot`
  - other (4): `HANDLING_RudderPedals_l`, `BBS757_CONTROL_COLUMN_CAPT`, `COCKPIT_pylon_yoke_copilot`, `B757_MAIN_tiller_l`

### THROTTLES MAIN

- Controls: 10 (momentary button 4, lever 6)
- Lights: 0
- Irregular (4):
  - `ENGINE_Switch_ATDisc_1` (momentary button): K: event
  - `ENGINE_Switch_ATDisc_2` (momentary button): K: event
  - `throttle_01` (lever): state not an L:var (A:GENERAL ENG THROTTLE LEVER POSITION:1 (assumed: inside stock template)); stock Asobo template: its logic is not in the package
  - `throttle_02` (lever): state not an L:var (A:GENERAL ENG THROTTLE LEVER POSITION:2 (assumed: inside stock template)); stock Asobo template: its logic is not in the package
- No label, wording must come from the node name (8):
  - lever (4): `BBS757_THRUST_LEVER_L`, `BBS757_THRUST_LEVER_L_REVERSER`, `BBS757_THRUST_LEVER_R`, `BBS757_THRUST_LEVER_R_REVERSER`
  - momentary button (4): `ENGINE_Switch_ATDisc_1`, `ENGINE_Switch_ATDisc_2`, `B757_AT_DISC_1`, `B757_AT_DISC_2`

### Updated throttle GA A/T buttons with L vars

- Controls: 1 (momentary button 1)
- Lights: 0
- Irregular (1):
  - `BBS757_tablet_home_button` (momentary button): state not an L:var (none); writes L:vars unrelated to its state (1 of 1); press only: sets a value, no release code

### (after Updated throttle GA A/T buttons with L vars)

- Controls: 1 (rotary knob finite 1)
- Lights: 0
- No label, wording must come from the node name (1):
  - rotary knob finite (1): `B757_IRS_BACKLIGHT_KNOB`

### TEST ANNUNCIATOR + BUTTONS LIGHTS

- Controls: 0
- Lights: 121

### SUB CATEGORY - IRS SYSTEM PANEL

- Section: TEST ANNUNCIATOR + BUTTONS LIGHTS > SUB CATEGORY - IRS SYSTEM PANEL
- Controls: 0
- Lights: 12

### TEST WARNING LIGHTS PANEL

- Controls: 3 (momentary button 1, rotary knob finite 2)
- Lights: 1
- Irregular (2):
  - `B757_OVHD_WARNING_TEST_BUTTON` (momentary button): Update code writes its vars (not only an init)
  - `B757_OVHD_WARNING_IND_SWITCH` (rotary knob finite): Update code writes its vars (not only an init)
- No label, wording must come from the node name (1):
  - rotary knob finite (1): `B757_OVHD_WARNING_IND_SWITCH`

### BUTTONS AND SWITCHES MAIN

- Controls: 4 (toggle 4)
- Lights: 4

### HYDRAULIC PANEL

- Controls: 6 (toggle 6)
- Lights: 6

### ELECTRICAL PANEL

- Controls: 12 (toggle 8, momentary button 3, rotary knob finite 1)
- Lights: 12
- Irregular (3):
  - `B757_OVHD_EXT_PWR_BUTTON` (momentary button): state not an L:var (O:_AnimProgress)
  - `B757_OVHD_L_GEN_DRIVE_BUTTON` (momentary button): state not an L:var (O:_AnimProgress)
  - `B757_OVHD_R_GEN_DRIVE_BUTTON` (momentary button): state not an L:var (O:_AnimProgress)
- No label, wording must come from the node name (3):
  - momentary button (3): `B757_OVHD_EXT_PWR_BUTTON`, `B757_OVHD_L_GEN_DRIVE_BUTTON`, `B757_OVHD_R_GEN_DRIVE_BUTTON`

### APU CONTROL PANEL

- Controls: 1 (rotary knob finite 1)
- Lights: 0
- No label, wording must come from the node name (1):
  - rotary knob finite (1): `B757_OVHD_APU_SWITCH`

### COCKPIT VOICE RECORDER - CVR

- Controls: 2 (momentary button 2)
- Lights: 0

### LIGHT OVERRIDE

- Controls: 10 (toggle 3, multi-position switch 1, momentary button 2, rotary knob finite 3, guard 1)
- Lights: 6
- Irregular (4):
  - `B757_OVHD_PASS_OXY_Toggle_Switch_CLICK` (multi-position switch): state not an L:var (O:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK_SUBMOVEMENT); click gated by (L:B757_OVHD_SWITCH_GAURD, bool) 1 ==; Update code writes its vars (not only an init)
  - `B757_OVHD_SWITCH_GAURD` (guard): writes L:vars unrelated to its state (1 of 2); Update code writes its vars (not only an init); node has several control rows
  - `B757_OVHD_PASS_OXY_BUTTON` (momentary button): state not an L:var (O:_AnimProgress)
  - `B757_OVHD_RAT_BUTTON` (momentary button): state not an L:var (O:_AnimProgress)
- No label, wording must come from the node name (8):
  - toggle (1): `B757_OVHD_LT_OVRD_BUTTON`
  - multi-position switch (1): `B757_OVHD_PASS_OXY_Toggle_Switch_CLICK`
  - guard (1): `B757_OVHD_SWITCH_GAURD`
  - momentary button (2): `B757_OVHD_PASS_OXY_BUTTON`, `B757_OVHD_RAT_BUTTON`
  - rotary knob finite (3): `B757_OVHD_ENG_START_SWITCH`, `B757_OVHD_L_ENG_START_SWITCH`, `B757_OVHD_R_ENG_START_SWITCH`

### FUEL PANEL

- Controls: 8 (toggle 8)
- Lights: 8

### ANTI ICE PANEL BUTTONS

- Controls: 3 (toggle 3)
- Lights: 3

### WINDSHIELD RAIN REPELLENT AND WIPER CONTROL PANEL

- Controls: 1 (rotary knob finite 1)
- Lights: 0
- No label, wording must come from the node name (1):
  - rotary knob finite (1): `B757_OVHD_WIPER_SWITCH`

### lights

- Controls: 14 (toggle 10, momentary button 1, rotary knob finite 3)
- Lights: 12
- Irregular (1):
  - `B757_GND_SVC_BUTTON` (momentary button): state not an L:var (O:_AnimProgress)
- No label, wording must come from the node name (10):
  - toggle (6): `B757_OVHD_SW_landing_L`, `B757_OVHD_SW_landing_R`, `B757_OVHD_RWY_TURN_L`, `B757_OVHD_RWY_TURN_R`, `B757_OVHD_btn_lights_taxi`, `B757_CABIN_WORK_LT002`
  - rotary knob finite (3): `B757_CABIN_CEILING`, `B757_CABIN_WINDOW`, `B757_CABIN_ENTRY`
  - momentary button (1): `B757_GND_SVC_BUTTON`

### WINDOW HEAT CONTROL PANEL BUTTONS

- Controls: 4 (toggle 4)
- Lights: 4

### SELCAL AND CABIN CALL BUTTON PANEL

- Controls: 11 (momentary button 11)
- Lights: 11
- No label, wording must come from the node name (11):
  - momentary button (11): `B757_OVHD_L_VHF_BUTTON`, `B757_OVHD_C_VHF_BUTTON`, `B757_OVHD_R_VHF_BUTTON`, `B757_OVHD_L_HF_BUTTON`, `B757_OVHD_R_HF_BUTTON`, `B757_OVHD_FWD_CABIN_CALL_BUTTON`, `B757_OVHD_MID_CABIN_CALL_BUTTON`, `B757_OVHD_AFT_CABIN_CALL_BUTTON`, `B757_OVHD_ALERT_CABIN_CALL_BUTTON`, `B757_OVHD_GND_CALL_BUTTON`, `B757_OVHD_FLT_INT_BUTTON`

### PASSENGER SIGN CONTROL PANEL

- Controls: 2 (rotary knob finite 2)
- Lights: 0
- No label, wording must come from the node name (2):
  - rotary knob finite (2): `B757_OVHD_NO_SMOKING_SWITCH`, `B757_OVHD_SEATBELTS_SWITCH`

### PRESSURIZATION CONTROL PANEL

- Controls: 4 (rotary knob infinite 1, rotary knob finite 3)
- Lights: 0
- Irregular (1):
  - `B757_OVHD_LDG_ALT_KNOB` (rotary knob infinite): state not an L:var (none); writes L:vars unrelated to its state (1 of 1); turn writes +1/-1 to a request var
- No label, wording must come from the node name (1):
  - rotary knob finite (1): `B757_OVHD_MODE_SELECT_SWITCH`

### (after PRESSURIZATION CONTROL PANEL)

- Controls: 2 (toggle 2)
- Lights: 3
- Irregular (1):
  - `B757_OVHD_LOGO_BUTTON` (toggle): K: event

### OVERHEAD PNEUMATIC PANEL

- Controls: 14 (toggle 7, held button 2, rotary knob infinite 3, rotary knob finite 2)
- Lights: 7
- Irregular (5):
  - `B757_OVHD_SEL_pack_Cockpt` (rotary knob infinite): writes L:vars unrelated to its state (1 of 1)
  - `B757_OVHD_SEL_pack_FWD_cab` (rotary knob infinite): writes L:vars unrelated to its state (1 of 1)
  - `B757_OVHD_SEL_pack_AFT_cab` (rotary knob infinite): writes L:vars unrelated to its state (1 of 1)
  - `B757_OVHD_L_PACK_BUTTON` (held button): state not an L:var (O:_AnimProgress); Update code writes its vars (not only an init)
  - `B757_OVHD_R_PACK_BUTTON` (held button): state not an L:var (O:_AnimProgress); Update code writes its vars (not only an init)
- No label, wording must come from the node name (9):
  - rotary knob infinite (3): `B757_OVHD_SEL_pack_Cockpt`, `B757_OVHD_SEL_pack_FWD_cab`, `B757_OVHD_SEL_pack_AFT_cab`
  - toggle (2): `B757_OVHD_L_RECIRC_BUTTON`, `B757_OVHD_R_RECIRC_BUTTON`
  - held button (2): `B757_OVHD_L_PACK_BUTTON`, `B757_OVHD_R_PACK_BUTTON`
  - rotary knob finite (2): `B757_OVHD_SEL_L_PACK_SWITCH`, `B757_OVHD_SEL_R_PACK_SWITCH`

### IRS SUB-CATEGORY - BUTTON_GLASS

- Controls: 12 (momentary button 12)
- Lights: 0
- No label, wording must come from the node name (12):
  - momentary button (12): `B757_IRS_NUMBER_1_BUTTON`, `B757_IRS_NUMBER_2_BUTTON`, `B757_IRS_NUMBER_3_BUTTON`, `B757_IRS_NUMBER_4_BUTTON`, `B757_IRS_NUMBER_5_BUTTON`, `B757_IRS_NUMBER_6_BUTTON`, `B757_IRS_NUMBER_7_BUTTON`, `B757_IRS_NUMBER_8_BUTTON`, `B757_IRS_NUMBER_9_BUTTON`, `B757_IRS_NUMBER_0_BUTTON`, `B757_IRS_NUMBER_ENT_BUTTON`, `B757_IRS_NUMBER_DEL_BUTTON`

### IRS KNOBS

- Controls: 5 (rotary knob finite 5)
- Lights: 0
- No label, wording must come from the node name (3):
  - rotary knob finite (3): `B757_IRS_L_SWITCH`, `B757_IRS_C_SWITCH`, `B757_IRS_R_SWITCH`

### PLASTIC BUTTON and SWITCH GUARDS

- Controls: 8 (guard 8)
- Lights: 1

### MAIN BUTTONS AND CONTROL

- Section: MCP MAIN LIGHTS > MAIN BUTTONS AND CONTROL
- Controls: 37 (momentary button 14, rotary knob finite 23)
- Lights: 14
- No label, wording must come from the node name (24):
  - rotary knob finite (10): `B757_MAIN_PANEL_FLOOD_KNOB`, `B757_FLOOD_DOME_KNOB`, `B757_MAIN_PANEL_FLOOD_KNOB2`, `B757_FLOOD_AISLE-STAND_OUTER_KNOB`, `B757_FLOOD_GLARESHIELD_INNER_KNOB`, `B757_FLOOD_AISLE-STAND_INNER_KNOB`, `B757_MAP_LIGHT_KNOB`, `B757_MAP_LIGHT_KNOB2`, `B757_CHART_LIGHT_KNOB`, `B757_CHART_LIGHT_KNOB2`
  - momentary button (14): `B757_MCP_THR_BUTTON`, `B757_MCP_SPD_BUTTON`, `B757_MCP_LNAV_BUTTON`, `B757_MCP_VNAV_BUTTON`, `B757_MCP_FLCH_BUTTON`, `B757_MCP_HOLD_1_BUTTON`, `B757_MCP_VS_BUTTON`, `B757_MCP_HOLD_2_BUTTON`, `B757_MCP_BCRS_1_BUTTON`, `B757_MCP_LOC_BUTTON`, `B757_MCP_APP_BUTTON`, `B757_MCP_CMD_1_BUTTON` … and 2 more

### AP MASTER DISCONNECT

- Section: MCP MAIN LIGHTS > AP MASTER DISCONNECT
- Controls: 1 (toggle 1)
- Lights: 0
- Irregular (1):
  - `B757_MCP_btn_autopilot_disengage` (toggle): K: event
- No label, wording must come from the node name (1):
  - toggle (1): `B757_MCP_btn_autopilot_disengage`

### MCP SPEED KNOB

- Section: MCP MAIN LIGHTS > MCP SPEED KNOB
- Controls: 10 (toggle 2, momentary button 3, rotary knob infinite 4, rotary knob finite 1)
- Lights: 0
- Irregular (4):
  - `B757_MCP_AP_MCHIAS_SEL` (rotary knob infinite): K: event
  - `B757_MCP_AP_HDG_SEL` (rotary knob infinite): K: event
  - `B757_MCP_AP_VSI_SEL` (rotary knob infinite): K: event
  - `B757_MCP_AP_ALT_SEL` (rotary knob infinite): state not an L:var (none); K: event
- No label, wording must come from the node name (6):
  - momentary button (3): `B757_MCP_AP_SPD_MACH_SW`, `B757_MCP_AP_HDG_SEL_SW`, `BBS757_ALTSEL_CLICK`
  - rotary knob infinite (2): `B757_MCP_AP_HDG_SEL`, `B757_MCP_AP_VSI_SEL`
  - rotary knob finite (1): `B757_MCP_AP_HDG_BNKLIM`

### AUTO THROTTLE ARM/OFF

- Section: MCP MAIN LIGHTS > AUTO THROTTLE ARM/OFF
- Controls: 1 (toggle 1)
- Lights: 0
- Irregular (1):
  - `B757_MCP_SW_AP_throttle` (toggle): writes L:vars unrelated to its state (1 of 2)

### WARNING/CAUTION BUTTONS

- Section: MCP MAIN LIGHTS > WARNING/CAUTION BUTTONS
- Controls: 2 (momentary button 2)
- Lights: 4
- Irregular (2):
  - `B757_MCP_btn_master_copilot` (momentary button): state not an L:var (O:_AnimProgress)
  - `B757_MCP_btn_master_pilot` (momentary button): state not an L:var (O:_AnimProgress)
- No label, wording must come from the node name (2):
  - momentary button (2): `B757_MCP_btn_master_copilot`, `B757_MCP_btn_master_pilot`

### VOR/DME PANEL

- Section: MCP MAIN LIGHTS > VOR/DME PANEL
- Controls: 8 (momentary button 2, rotary knob infinite 6)
- Lights: 4
- Irregular (6):
  - `B757_MCP_FREQ_1_UPR_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2); turn writes +1/-1 to a request var
  - `B757_MCP_FREQ_1_LWR_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2); turn writes +1/-1 to a request var
  - `B757_MCP_CRS_1_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2)
  - `B757_MCP_FREQ_2_UPR_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2); turn writes +1/-1 to a request var
  - `B757_MCP_FREQ_2_LWR_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2); turn writes +1/-1 to a request var
  - `B757_MCP_CRS_2_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2)

### FUEL CUT + THROTTLE QUADRANT SWITCHES

- Section: AFT CONTROL AISLE STAND > FUEL CUT + THROTTLE QUADRANT SWITCHES
- Controls: 0
- Lights: 8

### WARNING LIGHTS

- Section: AFT CONTROL AISLE STAND > WARNING LIGHTS
- Controls: 0
- Lights: 61

### EICAS KEY FUNCTIONS - FORWARD ONLY - EXCLUDE CRT BRIGHTNESS

- Section: AFT CONTROL AISLE STAND > EICAS KEY FUNCTIONS - FORWARD ONLY - EXCLUDE CRT BRIGHTNESS
- Controls: 8 (toggle 1, momentary button 4, rotary knob infinite 1, rotary knob finite 2)
- Lights: 0
- Irregular (4):
  - `B757_EICAS_L_STATUS_BUTTON` (momentary button): writes L:vars unrelated to its state (1 of 2)
  - `B757_EICAS_R_STATUS_BUTTON` (momentary button): writes L:vars unrelated to its state (1 of 2)
  - `B757_EICAS_EVENT_BUTTON` (momentary button): H: event
  - `B757_EICAS_UPR_THRUSTREF_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (2 of 3)
- No label, wording must come from the node name (6):
  - momentary button (3): `B757_EICAS_L_STATUS_BUTTON`, `B757_EICAS_R_STATUS_BUTTON`, `B757_EICAS_EVENT_BUTTON`
  - rotary knob finite (1): `B757_EICAS_LWR_THRUST-REF_KNOB`
  - rotary knob infinite (1): `B757_EICAS_UPR_THRUSTREF_KNOB`
  - toggle (1): `BBS757_THRREF_CLICK`

### EHSI/ADI KEY FUNCTIONS - CAPTAIN's SIDE ONLY - B757_AISLE_STAND_LCDEXCLUDE CRT BRIGHTNESS

- Section: AFT CONTROL AISLE STAND > EHSI/ADI KEY FUNCTIONS - CAPTAIN's SIDE ONLY - B757_AISLE_STAND_LCDEXCLUDE CRT BRIGHTNESS
- Controls: 26 (toggle 14, momentary button 6, rotary knob infinite 2, rotary knob finite 4)
- Lights: 14
- Irregular (2):
  - `B757_ADI_1_DH_REF_TUNE_KNOB` (rotary knob infinite): K: event
  - `B757_ADI_2_DH_REF_TUNE_KNOB` (rotary knob infinite): K: event
- No label, wording must come from the node name (3):
  - rotary knob finite (1): `B757_ADI_1_CTR_KNOB`
  - rotary knob infinite (2): `B757_ADI_1_DH_REF_TUNE_KNOB`, `B757_ADI_2_DH_REF_TUNE_KNOB`

### FMC BUTTONS

- Section: AFT CONTROL AISLE STAND > FMC BUTTONS
- Controls: 0
- Lights: 2

### PEDESTAL GAURD SWITCH GEAR

- Section: AFT CONTROL AISLE STAND > PEDESTAL GAURD SWITCH GEAR
- Controls: 25 (toggle 4, multi-position switch 4, momentary button 7, held button 3, rotary knob finite 3, guard 4)
- Lights: 2
- Irregular (13):
  - `B757_AISLE_STAND_STAB_1_GAURD_SWITCH` (guard): writes L:vars unrelated to its state (1 of 2)
  - `B757_AISLE_STAND_STAB_2_GAURD_SWITCH` (guard): writes L:vars unrelated to its state (1 of 2)
  - `B757_Stab_trim_C_toggle_switch_CLICK` (toggle): click gated by (L:B757_AISLE_STAND_STAB_1_GAURD_SWITCH, bool) 1 ==
  - `B757_Stab_trim_R_toggle_switch_CLICK` (toggle): click gated by (L:B757_AISLE_STAND_STAB_2_GAURD_SWITCH, bool) 1 ==
  - `BBS_fuelcut_RR1` (multi-position switch): Update code writes its vars (not only an init)
  - `BBS_fuelcut_RR2` (multi-position switch): Update code writes its vars (not only an init)
  - `BBS757_FIREUNLOCK1` (momentary button): press only: sets a value, no release code; Update code writes its vars (not only an init)
  - `BBS757_FIREUNLOCK2` (momentary button): press only: sets a value, no release code; Update code writes its vars (not only an init)
  - `B757_FIREUNLOCK3` (momentary button): press only: sets a value, no release code; Update code writes its vars (not only an init)
  - `BBS757_FIREHANDLE1_CLICK3` (momentary button): press only: sets a value, no release code
  - `B757_PED_1BTL_CARGOFIRE_BUTTON` (held button): state not an L:var (O:_AnimProgress); Update code writes its vars (not only an init)
  - `B757_PED_2BTL_CARGOFIRE_BUTTON` (held button): state not an L:var (O:_AnimProgress); Update code writes its vars (not only an init)
  - `B757_PED_SYS_FAIL_BUTTON` (held button): state not an L:var (O:_AnimProgress); Update code writes its vars (not only an init)
- No label, wording must come from the node name (15):
  - guard (4): `B757_AISLE_STAND_STAB_1_GAURD_SWITCH`, `B757_AISLE_STAND_STAB_2_GAURD_SWITCH`, `B757_GAURD_11_SWITCH`, `B757_GAURD_12_SWITCH`
  - toggle (2): `B757_Stab_trim_C_toggle_switch_CLICK`, `B757_Stab_trim_R_toggle_switch_CLICK`
  - momentary button (6): `BBS757_FIREUNLOCK1`, `BBS757_FIREUNLOCK2`, `B757_FIREUNLOCK3`, `B757_PED_FIRE_WHLWELL_BUTTON`, `B757_PED_FIRE_ENG_BUTTON`, `B757_PED_FIRE_CARGO_BUTTON`
  - held button (3): `B757_PED_1BTL_CARGOFIRE_BUTTON`, `B757_PED_2BTL_CARGOFIRE_BUTTON`, `B757_PED_SYS_FAIL_BUTTON`

### INDIVIDUAL AUDIO/RADIO PANELS

- Section: AFT CONTROL AISLE STAND > INDIVIDUAL AUDIO/RADIO PANELS
- Controls: 126 (toggle 36, multi-position switch 4, momentary button 44, select button 14, rotary knob infinite 25, rotary knob finite 3)
- Lights: 23
- Irregular (58):
  - `B757_PED_NAV_1_LARGE_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2)
  - `B757_PED_NAV_1_SMALL_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2)
  - `B757_PED_NAV_2_LARGE_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2)
  - `B757_PED_NAV_2_SMALL_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2)
  - `B757_PED_ADF_L_SMALL_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2); turn writes +1/-1 to a request var
  - `B757_PED_ADF_L_MEDIUM_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2); turn writes +1/-1 to a request var
  - `B757_PED_ADF_L_LARGE_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2); turn writes +1/-1 to a request var
  - `B757_PED_ADF_R_SMALL_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2); turn writes +1/-1 to a request var
  - `B757_PED_ADF_R_MEDIUM_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2); turn writes +1/-1 to a request var
  - `B757_PED_ADF_R_LARGE_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2); turn writes +1/-1 to a request var
  - `B757_PED_TCAS_1_SMALL_KNOB` (rotary knob infinite): K: event
  - `B757_PED_TCAS_1_LARGE_KNOB` (rotary knob infinite): K: event
  - `B757_PED_TCAS_2_SMALL_KNOB` (rotary knob infinite): K: event
  - `B757_PED_TCAS_2_LARGE_KNOB` (rotary knob infinite): K: event
  - `B757_PED_NAV_3_LARGE_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2)
  - `B757_PED_NAV_3_SMALL_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2)
  - `B757_PED_FREQ_1_UPR_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2); turn writes +1/-1 to a request var
  - `B757_PED_FREQ_1_LWR_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2); turn writes +1/-1 to a request var
  - `B757_PED_CRS_1_UPR_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2)
  - `B757_PED_CRS_1_LWR_KNOB` (rotary knob infinite): writes L:vars unrelated to its state (1 of 2)
  - `B757_PED_NAV_1_OFF_BUTTON` (momentary button): state not an L:var (O:B757_PED_NAV_1_OFF_BUTTON_POSSITION)
  - `B757_PED_NAV_1_VHFL_BUTTON` (momentary button): state not an L:var (O:B757_PED_NAV_1_VHFL_BUTTON_POSSITION)
  - `B757_PED_NAV_1_VHFC_BUTTON` (momentary button): state not an L:var (O:B757_PED_NAV_1_VHFC_BUTTON_POSSITION)
  - `B757_PED_NAV_1_VHFR_BUTTON` (momentary button): state not an L:var (O:B757_PED_NAV_1_VHFR_BUTTON_POSSITION)
  - `B757_PED_NAV_1_HFL_BUTTON` (momentary button): state not an L:var (O:B757_PED_NAV_1_HFL_BUTTON_POSSITION)
  - every select button (14): selects one of a group
  - … and 19 more (control-map.tsv, notes column)
- No label, wording must come from the node name (86):
  - momentary button (30): `B757_PED_COMHEAD_1_SWAP_BUTTON`, `B757_PED_NAV_1_OFF_BUTTON`, `B757_PED_NAV_1_VHFL_BUTTON`, `B757_PED_NAV_1_VHFC_BUTTON`, `B757_PED_NAV_1_VHFR_BUTTON`, `B757_PED_NAV_1_HFL_BUTTON`, `B757_PED_NAV_1_AM_BUTTON`, `B757_PED_NAV_1_HFR_BUTTON`, `B757_PED_NAV_1_TEST_BUTTON`, `B757_PED_AUDIO_1_PTT_SWITCH`, `B757_PED_COMHEAD_2_SWAP_BUTTON`, `B757_PED_NAV_2_OFF_BUTTON` … and 18 more
  - rotary knob infinite (7): `B757_PED_NAV_1_HFSENSE_KNOB`, `B757_PED_ADF_L_SMALL_KNOB`, `B757_PED_ADF_L_MEDIUM_KNOB`, `B757_PED_ADF_L_LARGE_KNOB`, `B757_PED_ADF_R_SMALL_KNOB`, `B757_PED_ADF_R_MEDIUM_KNOB`, `B757_PED_ADF_R_LARGE_KNOB`
  - select button (14): `B757_PED_AUDIO_1_MIC_L_LIGHT`, `B757_PED_AUDIO_1_MIC_C_LIGHT`, `B757_PED_AUDIO_1_MIC_R_LIGHT`, `B757_PED_AUDIO_1_MIC_LM_LIGHT`, `B757_PED_AUDIO_1_MIC_RR_LIGHT`, `B757_PED_AUDIO_1_MIC_PA_LIGHT`, `B757_PED_AUDIO_1_MIC_INT_LIGHT`, `B757_PED_AUDIO_2_MIC_L_LIGHT`, `B757_PED_AUDIO_2_MIC_C_LIGHT`, `B757_PED_AUDIO_2_MIC_R_LIGHT`, `B757_PED_AUDIO_2_MIC_LM_LIGHT`, `B757_PED_AUDIO_2_MIC_RR_LIGHT` … and 2 more
  - toggle (35): `B757_PED_AUDIO_1_MIC_L_LIGHT_KNOB`, `B757_PED_AUDIO_1_MIC_C_LIGHT_KNOB`, `B757_PED_AUDIO_1_MIC_R_LIGHT_KNOB`, `B757_PED_AUDIO_1_MIC_LM_LIGHT_KNOB`, `B757_PED_AUDIO_1_MIC_RR_LIGHT_KNOB`, `B757_PED_AUDIO_1_MIC_PA_LIGHT_KNOB`, `B757_PED_AUDIO_1_MIC_INT_LIGHT_KNOB`, `B757_PED_AUDIO_1_MIC_LILS_LIGHT_KNOB`, `B757_PED_AUDIO_1_MIC_LVOR_LIGHT_KNOB`, `B757_PED_AUDIO_1_MIC_CLILS_LIGHT_KNOB`, `B757_PED_AUDIO_1_MIC_RVOR_LIGHT_KNOB`, `B757_PED_AUDIO_1_MIC_RLILS_LIGHT_KNOB` … and 23 more

### (after INDIVIDUAL AUDIO/RADIO PANELS)

- Section: AFT CONTROL AISLE STAND > (after INDIVIDUAL AUDIO/RADIO PANELS)
- Controls: 4 (rotary knob finite 4)
- Lights: 0
- Irregular (2):
  - `B757_GRIMES_1_LIGHTS_BUTTON` (rotary knob finite): state not an L:var (A:LIGHT POTENTIOMETER:20 (assumed: inside stock template)); stock Asobo template: its logic is not in the package
  - `B757_GRIMES_2_LIGHTS_BUTTON` (rotary knob finite): state not an L:var (A:LIGHT POTENTIOMETER:21 (assumed: inside stock template)); stock Asobo template: its logic is not in the package
- No label, wording must come from the node name (2):
  - rotary knob finite (2): `B757_ADI_1_LCD_KNOB`, `B757_ADI_2_LCD_KNOB`

### LEFT

- Controls: 0
- Lights: 12

### CRT screens

- Controls: 0
- Lights: 8

### (after CRT screens)

- Controls: 0
- Lights: 14

### AUDIO LIGHT KNOBS

- Controls: 0
- Lights: 30

### (after OBSERVER ASP)

- Section: AUDIO LIGHT KNOBS > (after OBSERVER ASP)
- Controls: 0
- Lights: 30

### FMCS

- Section: AUDIO LIGHT KNOBS > FMCS
- Controls: 0
- Lights: 4

### FMC 2 decals

- Section: AUDIO LIGHT KNOBS > FMC 2 decals
- Controls: 0
- Lights: 4

### LANDING GEAR

- Section: MAIN PANEL SECTION > LANDING GEAR
- Controls: 2 (toggle 1, lever 1)
- Lights: 5
- Irregular (1):
  - `B757_MAIN_Landing_Gear_Y` (lever): writes L:vars unrelated to its state (2 of 3)
- No label, wording must come from the node name (2):
  - toggle (1): `B757_MAIN_LOCK_OVRD_SWITCH`
  - lever (1): `B757_MAIN_Landing_Gear_Y`

### EMISSIVE GAUGES

- Section: MAIN PANEL SECTION > EMISSIVE GAUGES
- Controls: 0
- Lights: 4

### CAPT DDRMI

- Section: MAIN PANEL SECTION > INSTRUMENTS > CAPT DDRMI
- Controls: 4 (toggle 4)
- Lights: 0
- Irregular (2):
  - `switch_afds_vor_adf_R_L` (toggle): state not an L:var ((stock RMI VOR/ADF pointer selection) (assumed: inside stock template)); stock Asobo template: its logic is not in the package
  - `switch_afds_vor_adf_R_R_NCL1` (toggle): state not an L:var ((stock RMI VOR/ADF pointer selection) (assumed: inside stock template)); stock Asobo template: its logic is not in the package
- No label, wording must come from the node name (2):
  - toggle (2): `switch_afds_vor_adf_R_L`, `switch_afds_vor_adf_R_R_NCL1`

### F/O DDRMI

- Section: MAIN PANEL SECTION > INSTRUMENTS > F/O DDRMI
- Controls: 2 (toggle 2)
- Lights: 2

### CAPT AUTOLAND STATUS

- Section: MAIN PANEL SECTION > INSTRUMENTS > CAPT AUTOLAND STATUS
- Controls: 3 (momentary button 3)
- Lights: 4

### F/O AUTOLAND STATUS

- Section: MAIN PANEL SECTION > INSTRUMENTS > F/O AUTOLAND STATUS
- Controls: 3 (momentary button 3)
- Lights: 4

### CAPT PRIMARY ALTIMETER

- Section: MAIN PANEL SECTION > INSTRUMENTS > CAPT PRIMARY ALTIMETER
- Controls: 2 (rotary knob infinite 2)
- Lights: 0
- Irregular (2):
  - `B757_MAIN_L_Gau_STBY_ALT_knob` (rotary knob infinite): state not an L:var (none); K: event
  - `INSTRUMENT_Knob_Altimeter` (rotary knob infinite): state not an L:var (none); writes L:vars unrelated to its state (1 of 1)

### CAPT CHRONOMETER

- Section: MAIN PANEL SECTION > INSTRUMENTS > CAPT CHRONOMETER
- Controls: 6 (momentary button 4, rotary knob finite 2)
- Lights: 0

### STANDBY ATTITUDE-GYROMETER

- Section: MAIN PANEL SECTION > INSTRUMENTS > STANDBY ATTITUDE-GYROMETER
- Controls: 1 (momentary button 1)
- Lights: 0
- No label, wording must come from the node name (1):
  - momentary button (1): `BBS757_gyro_knob`

### STANDBY ALTIMETER

- Section: MAIN PANEL SECTION > INSTRUMENTS > STANDBY ALTIMETER
- Controls: 1 (rotary knob infinite 1)
- Lights: 0
- Irregular (1):
  - `INSTRUMENTS` (rotary knob infinite): state not an L:var (A:KOHLSMAN SETTING MB:#ID# (assumed: inside stock template)); stock Asobo template: its logic is not in the package
- No label, wording must come from the node name (1):
  - rotary knob infinite (1): `INSTRUMENTS`

### STANDBY ALTITUDE ROLLING NUMBER

- Section: MAIN PANEL SECTION > INSTRUMENTS > STANDBY ALTIMETER > STANDBY ALTITUDE ROLLING NUMBER
- Controls: 1 (rotary knob infinite 1)
- Lights: 0
- Irregular (1):
  - `B757_MAIN_M_Gau_STBY_ALT_knob` (rotary knob infinite): state not an L:var (none); K: event

### FIRST OFFICER MACH/ASI

- Section: MAIN PANEL SECTION > INSTRUMENTS > FIRST OFFICER MACH/ASI
- Controls: 4 (lever 4)
- Lights: 0
- Irregular (4):
  - every lever (4): writes L:vars unrelated to its state

### 1's

- Section: MAIN PANEL SECTION > INSTRUMENTS > FIRST OFFICER MACH/ASI > 1's
- Controls: 0
- Lights: 1

### (after ALTITUDE ROLLING NUMBER)

- Section: MAIN PANEL SECTION > INSTRUMENTS > FIRST OFFICER MACH/ASI > FISRT OFFICER PRIMARY ALTIMETER > (after ALTITUDE ROLLING NUMBER)
- Controls: 2 (rotary knob infinite 2)
- Lights: 0
- Irregular (2):
  - `B757_MAIN_R_Gau_STBY_ALT_knob` (rotary knob infinite): state not an L:var (none); H: event; K: event
  - `INSTRUMENT_Knob_Altimeter_FO` (rotary knob infinite): state not an L:var (none); writes L:vars unrelated to its state (1 of 1)

### FISRT OFFICER CHRONOMETER

- Section: MAIN PANEL SECTION > INSTRUMENTS > FIRST OFFICER MACH/ASI > FISRT OFFICER CHRONOMETER
- Controls: 4 (momentary button 2, rotary knob finite 2)
- Lights: 0

### MAIN PANEL SWITCH GEAR + BUTTONS

- Section: MAIN PANEL SECTION > INSTRUMENTS > MAIN PANEL SWITCH GEAR + BUTTONS
- Controls: 52 (toggle 17, multi-position switch 4, momentary button 20, rotary knob infinite 1, rotary knob finite 6, guard 4)
- Lights: 15
- Irregular (6):
  - `BBS757_O2_redbutton_CPT` (momentary button): state not an L:var (none); writes L:vars unrelated to its state (1 of 1)
  - `BBS757_O2_redbutton_FO` (momentary button): state not an L:var (none); writes L:vars unrelated to its state (1 of 1)
  - `BBS757_O2_redbutton_JUMPSEAT` (momentary button): state not an L:var (none); writes L:vars unrelated to its state (1 of 1)
  - `BBS757_O2_redbutton_P61` (momentary button): state not an L:var (none); writes L:vars unrelated to its state (1 of 1)
  - `B757_MAIN_L_TERROVRD_BUTTON` (momentary button): state not an L:var (O:_AnimProgress)
  - `B757_MAIN_PANEL_ALTN_GEAR_SWITCH_GAURD` (guard): writes L:vars unrelated to its state (1 of 2)
- No label, wording must come from the node name (15):
  - momentary button (12): `OXY_CAPT_TEST_SWITCH001`, `OXY_FO_TEST_SWITCH1`, `OXY_OBSV_TEST_SWITCH`, `OXY_P61_TEST_SWITCH`, `BBS757_GS_INHIBIT`, `B757_MAIN_ALTN_GEAR_SWITCH`, `B757_MAIN_TOGA_BUTTON`, `B757_MAIN_CLB_BUTTON`, `B757_MAIN_1_BUTTON`, `B757_MAIN_2_BUTTON`, `B757_MAIN_CON_BUTTON`, `B757_MAIN_CRZ_BUTTON`
  - guard (1): `B757_MAIN_PANEL_ALTN_GEAR_SWITCH_GAURD`
  - toggle (1): `B757_MAIN_STDBY_EICAS_KNOB`
  - rotary knob finite (1): `B757_MAIN_knob_gears_altn_autobrake`

### (after MAIN PANEL SECTION)

- Controls: 1 (rotary knob finite 1)
- Lights: 0
- Irregular (1):
  - `xB757_FLOOD_CB_KNOB` (rotary knob finite): state not an L:var (A:LIGHT PANEL POWER SETTING (assumed: inside stock template)); stock Asobo template: its logic is not in the package

### P61 PANEL SWITCH GEAR

- Controls: 38 (toggle 15, multi-position switch 9, momentary button 7, guard 7)
- Lights: 19
- Irregular (7):
  - `B757_P6_SQUIBS_BUTTON2` (momentary button): state not an L:var (O:_AnimProgress); writes L:vars unrelated to its state (2 of 2)
  - `P61_PRESS_Button_1` (momentary button): state not an L:var (O:_AnimProgress)
  - `P61_PRESS_Button_2` (momentary button): state not an L:var (O:_AnimProgress)
  - `P61_PRESS_Button_3` (momentary button): state not an L:var (O:_AnimProgress)
  - `P61_PRESS_Button_4` (momentary button): state not an L:var (O:_AnimProgress)
  - `P61_PRESS_Button_5` (momentary button): state not an L:var (O:_AnimProgress)
  - `P61_PRESS_Button_6` (momentary button): state not an L:var (O:_AnimProgress)
- No label, wording must come from the node name (34):
  - multi-position switch (9): `B757_VC_P61_SWITCH_3`, `B757_VC_P61_SWITCH_4`, `B757_VC_P61_SWITCH_5`, `B757_VC_P61_SWITCH_6`, `B757_VC_P61_SWITCH_7`, `B757_VC_P61_SWITCH_8`, `B757_VC_P61_SWITCH_9`, `B757_VC_P61_SWITCH_10`, `B757_VC_P61_SWITCH_2`
  - momentary button (7): `B757_P6_SQUIBS_BUTTON2`, `P61_PRESS_Button_1`, `P61_PRESS_Button_2`, `P61_PRESS_Button_3`, `P61_PRESS_Button_4`, `P61_PRESS_Button_5`, `P61_PRESS_Button_6`
  - toggle (15): `B757_P6_DISPLAY_1_EADI_BUTTON`, `B757_P6_DISPLAY_1_EHSI_BUTTON`, `B757_P6_DISPLAY_1_EICAS_BUTTON`, `B757_P6_DISPLAY_2_EICAS_BUTTON`, `B757_P6_DISPLAY_2_EADI_BUTTON`, `B757_P6_DISPLAY_2_EHSI_BUTTON`, `B757_P6_EVENT_AUTO_TEST_SWITCH_BUTTON`, `B757_P6_EVENT_MAN_TEST_SWITCH_BUTTON`, `B757_P6_EVENT_MAIN_TEST_SWITCH_BUTTON`, `B757_P6_EVENT_REC_TEST_SWITCH_BUTTON`, `B757_P6_EVENT_ERASE_TEST_SWITCH_BUTTON`, `B757_P61_PRESS_MAIN_BUTTON_L` … and 3 more
  - guard (3): `BBS_B757_P61_GLASS_GAURD_1`, `BBS_B757_P61_GLASS_GAURD_2`, `BBS_B757_P61_GLASS_GAURD_3`

### (after P61 PANEL SWITCH GEAR)

- Controls: 1 (rotary knob finite 1)
- Lights: 0
- Irregular (1):
  - `xB757_FLOOD_DOME_KNOB` (rotary knob finite): state not an L:var (A:LIGHT CABIN POWER SETTING (assumed: inside stock template)); stock Asobo template: its logic is not in the package

### MISC PARTS

- Controls: 13 (toggle 8, lever 5)
- Lights: 0
- Irregular (1):
  - `B757_JUMP_2_SEAT_BASE` (toggle): writes L:vars unrelated to its state (1 of 2)

### CHANGE MAJOR CODE and UPDATES BELOW

- Controls: 19 (toggle 8, rotary knob finite 1, lever 10)
- Lights: 5
- Irregular (4):
  - `B757_MAIN_MASI_INSTRUMENT_REF1` (lever): writes L:vars unrelated to its state (3 of 4); Update code writes its vars (not only an init)
  - `B757_MAIN_MASI_INSTRUMENT_REF2` (lever): writes L:vars unrelated to its state (3 of 5); Update code writes its vars (not only an init)
  - `B757_MAIN_MASI_INSTRUMENT_REF3` (lever): writes L:vars unrelated to its state (3 of 5); Update code writes its vars (not only an init)
  - `B757_MAIN_MASI_INSTRUMENT_REF4` (lever): writes L:vars unrelated to its state (3 of 4); Update code writes its vars (not only an init)
- No label, wording must come from the node name (1):
  - rotary knob finite (1): `B757_MAIN_M_Gau_STBY_GYRO_SELECTOR`

### COCKPIT CAPTAIN CHAIR

- Section: CHANGE MAJOR CODE and UPDATES BELOW > COCKPIT CAPTAIN CHAIR
- Controls: 1 (toggle 1)
- Lights: 0
- Irregular (1):
  - `B757_CAPT_LEVER_SEAT` (toggle): click writes only O: (component-local) vars; Update code writes its vars (not only an init)

### COCKPIT FIRST OFFICER CHAIR

- Section: CHANGE MAJOR CODE and UPDATES BELOW > COCKPIT FIRST OFFICER CHAIR
- Controls: 1 (toggle 1)
- Lights: 0
- Irregular (1):
  - `B757_FO_LEVER_SEAT` (toggle): click writes only O: (component-local) vars; Update code writes its vars (not only an init)

### L WINDOW WET and DRY LOGIC

- Section: CHANGE MAJOR CODE and UPDATES BELOW > L WINDOW WET and DRY LOGIC
- Controls: 0
- Lights: 3

### RIGHT WINDOW RAIN EFFECTS

- Section: CHANGE MAJOR CODE and UPDATES BELOW > RIGHT WINDOW RAIN EFFECTS
- Controls: 14 (toggle 7, momentary button 3, lever 4)
- Lights: 3
- Irregular (8):
  - `L2win_handle` (toggle): Update code writes its vars (not only an init)
  - `L3_SHADE_GUIDE` (toggle): Update code writes its vars (not only an init)
  - `R3_SHADE_GUIDE` (toggle): Update code writes its vars (not only an init)
  - `R2win_handle` (toggle): Update code writes its vars (not only an init)
  - `BBS757_SPOILERARMED_CLICK` (momentary button): state not an L:var (none); writes L:vars unrelated to its state (1 of 1); press only: sets a value, no release code
  - `BBS757_SPOILERUP_CLICK` (momentary button): state not an L:var (none); writes L:vars unrelated to its state (1 of 1); press only: sets a value, no release code
  - `BBS757_SPOILERDOWN_CLICK` (momentary button): state not an L:var (none); writes L:vars unrelated to its state (1 of 1); press only: sets a value, no release code
  - `lever_speed_brake` (lever): state not an L:var (A:SPOILERS HANDLE POSITION (assumed: inside stock template)); stock Asobo template: its logic is not in the package
- No label, wording must come from the node name (5):
  - momentary button (3): `BBS757_SPOILERARMED_CLICK`, `BBS757_SPOILERUP_CLICK`, `BBS757_SPOILERDOWN_CLICK`
  - lever (2): `BBS757_lever_speed_brake`, `lever_speed_brake`

### NEW ECAM COMPUTER SWITCH

- Section: CHANGE MAJOR CODE and UPDATES BELOW > NEW ECAM COMPUTER SWITCH
- Controls: 1 (rotary knob finite 1)
- Lights: 0
- No label, wording must come from the node name (1):
  - rotary knob finite (1): `B757_ECAM_LRAUTO_KNOB`

### ADDING L VARS for CDU switches

- Section: CHANGE MAJOR CODE and UPDATES BELOW > ADDING L VARS for CDU switches
- Controls: 69 (cdu key 69)
- Lights: 0
- No label, wording must come from the node name (69):
  - cdu key (69): `B757_CDU_1_L1`, `B757_CDU_1_L2`, `B757_CDU_1_L3`, `B757_CDU_1_L4`, `B757_CDU_1_L5`, `B757_CDU_1_L6`, `B757_CDU_1_R1`, `B757_CDU_1_R2`, `B757_CDU_1_R3`, `B757_CDU_1_R4`, `B757_CDU_1_R5`, `B757_CDU_1_R6` … and 57 more

### FO CDU CODE

- Section: CHANGE MAJOR CODE and UPDATES BELOW > FO CDU CODE
- Controls: 69 (cdu key 69)
- Lights: 0
- No label, wording must come from the node name (69):
  - cdu key (69): `B757_CDU_2_L1`, `B757_CDU_2_L2`, `B757_CDU_2_L3`, `B757_CDU_2_L4`, `B757_CDU_2_L5`, `B757_CDU_2_L6`, `B757_CDU_2_R1`, `B757_CDU_2_R2`, `B757_CDU_2_R3`, `B757_CDU_2_R4`, `B757_CDU_2_R5`, `B757_CDU_2_R6` … and 57 more

### CB OVERHEAD PANEL

- Controls: 511 (circuit breaker 511)
- Lights: 0
- Irregular (511):
  - every circuit breaker (511): bitmask state
- No label, wording must come from the node name (511):
  - circuit breaker (511): `A2_STANDBY_POWER_navigation_vor_mkr_left`, `A3_STANDBY_POWER_navigation_ils_center`, `A4_STANDBY_POWER_navigation_adf_right`, `A5_STANDBY_POWER_navigation_stby_att_ind`, `A6_STANDBY_POWER_navigation_rdmi_left`, `A7_STANDBY_POWER_navigation_efis_dspl_sw_left`, `A8_STANDBY_POWER_navigation_stby_altm_vib`, `A9_STANDBY_POWER_navigation_stby_ils_ind`, `A10_STANDBY_POWER_navigation_air_data_cmptr_left`, `A11_STANDBY_POWER_navigation_air_data_aoa_sensor_left`, `A12_STANDBY_POWER_navigation_air_data_baro_correct_left`, `A13_STANDBY_POWER_air_cond_pack_left_auto_cont` … and 499 more

### (after DOOR CODE)

- Controls: 1 (momentary button 1)
- Lights: 0
- No label, wording must come from the node name (1):
  - momentary button (1): `B757_MCP_AP_SPD_HOLD_SW`

## File `SimObjects/Airplanes/bbs-757-200-pw/model/BBS_752_PW_INTERIOR.xml`

### (no banner)

- Controls: 2 (multi-position switch 2)
- Lights: 2
- No label, wording must come from the node name (2):
  - multi-position switch (2): `BBS_fuelcut_PW1`, `BBS_fuelcut_PW2`

## File `SimObjects/Airplanes/bbs-757-200-pw/model.nocab/BBS_752_PW_INTERIOR_NC.xml`

### (no banner)

- Controls: 2 (multi-position switch 2)
- Lights: 2
- No label, wording must come from the node name (2):
  - multi-position switch (2): `BBS_fuelcut_PW1`, `BBS_fuelcut_PW2`

## File `SimObjects/Airplanes/bbs-757-200-pw-AWE/model/BBS_752_PW_INTERIOR_AWE.xml`

### (no banner)

- Controls: 2 (multi-position switch 2)
- Lights: 2
- No label, wording must come from the node name (2):
  - multi-position switch (2): `BBS_fuelcut_PW1`, `BBS_fuelcut_PW2`

## File `SimObjects/Airplanes/bbs-757-200-rr/model.nocabRR/BBS_752_RR_INTERIOR_NC.xml`

### (no banner)

- Controls: 2 (multi-position switch 2)
- Lights: 2
- No label, wording must come from the node name (2):
  - multi-position switch (2): `BBS_fuelcut_PW1`, `BBS_fuelcut_PW2`
