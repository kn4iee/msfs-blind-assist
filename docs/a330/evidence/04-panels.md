# Plan 04 evidence: the A330's input-event catalogue and the panel gap

## Capture, 2026-11-27

Simulator: MSFS 2024 1.9.24.0. Aircraft: iniBuilds A330-200 VIP (GE). 649 input events, captured from the app's own catalogue log (`tools/a330-gen/data/input_events.tsv`).

The aircraft's own names for the controls (`control_names.json`) have not been exported yet, so the tables have no title column: every event would read "no name". Where "In today's map" says a key, the control is in the map; "left out" is followed by the reason, in short. Events with no control and no left-out entry are listed as "not offered" (plan 04 Task 5 works through these).

Event names are written as the simulator lists them, including its spellings (GAURD).

## Part 1: every event, by group

The group is the word after `AIRLINER_` (the first word of the name for the few events without that prefix). For each group, a short table of the events in the map or left out, then the events not offered. Groups with a single event are together, last.

### AFT (3 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_AFT_AGENT | A330_AFT_CARGO_AGENT |
| AIRLINER_AFT_AGENT_COVER | A330_AFT_CARGO_AGENT_COVER |
| AIRLINER_AFT_ISO_VALVE | A330_AFT_ISO |

### AIR (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_AIR_APU_BLEED | A330_APU_BLEED |
| AIRLINER_AIR_DATA_SELECT | A330_AIR_DATA |

### APU (7 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_APU_AGENT | A330_APU_AGENT |
| AIRLINER_APU_AGENT_TEST | A330_APU_FIRE_TEST |
| AIRLINER_APU_FIRE_HANDLE | A330_APU_FIRE |
| AIRLINER_APU_FIRE_HANDLE_COVER | A330_APU_FIRE_COVER |
| AIRLINER_APU_MASTER | A330_APU_MASTER |
| AIRLINER_APU_START | A330_APU_START |

Not offered (1):

- AIRLINER_APU_EXTING_RESET

### AUTOBRK (3 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_AUTOBRK_HI | left out: pressed by autobrake level (HI) |
| AIRLINER_AUTOBRK_LO | left out: pressed by autobrake level (LO) |
| AIRLINER_AUTOBRK_MED | left out: pressed by autobrake level (MED) |

### CALL (6 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_CALL_AFT | A330_CALL_AFT |
| AIRLINER_CALL_ALL | A330_CALL_ALL |
| AIRLINER_CALL_EXIT | A330_CALL_EXIT |
| AIRLINER_CALL_FWD | A330_CALL_FWD |
| AIRLINER_CALL_MID | A330_CALL_MID |
| AIRLINER_CALL_PURS | A330_CALL_PURS |

### CARGO (5 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_CARGO_AIR_TEMP | A330_BULK_TEMP |
| AIRLINER_CARGO_AIR_TEMP1 | A330_FWD_CARGO_TEMP |
| AIRLINER_CARGO_SMOKE_TEST | A330_CARGO_SMOKE_TEST |

Not offered (2):

- AIRLINER_CARGO_AIR_TEMP_MODE
- AIRLINER_CARGO_TEMP_PRESEL

### CLOCK (5 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_CLOCK_CHR | A330_CLOCK_CHR |
| AIRLINER_CLOCK_RST | A330_CLOCK_RST |
| AIRLINER_CLOCK_RUN | A330_CLOCK_ET |

Not offered (2):

- AIRLINER_CLOCK_GPS
- AIRLINER_CLOCK_SET

### CMC1 (2 events)

Not offered (2):

- AIRLINER_CMC1
- AIRLINER_CMC1_COVER

### COCKPIT (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_COCKPIT_TEMP | A330_CKPT_TEMP |

Not offered (1):

- AIRLINER_COCKPIT_DOOR_LOCK

### COM3 (59 events)

Not offered (59): AIRLINER_COM3_ADF1_VOL, AIRLINER_COM3_ADF1_VOL_BUTTON, AIRLINER_COM3_ADF2_VOL, AIRLINER_COM3_ADF2_VOL_BUTTON, AIRLINER_COM3_BTN_CAB, AIRLINER_COM3_BTN_HF1, AIRLINER_COM3_BTN_HF2, AIRLINER_COM3_BTN_INT, AIRLINER_COM3_BTN_PA, AIRLINER_COM3_BTN_SAT1, AIRLINER_COM3_BTN_SAT2, AIRLINER_COM3_BTN_TRANSFER, AIRLINER_COM3_BTN_VHF1, AIRLINER_COM3_BTN_VHF2, AIRLINER_COM3_BTN_VHF3, AIRLINER_COM3_CAB_VOL, AIRLINER_COM3_CAB_VOL_BUTTON, AIRLINER_COM3_COMM_KHZ, AIRLINER_COM3_COMM_MHZ, AIRLINER_COM3_COM_ADF, AIRLINER_COM3_COM_AM, AIRLINER_COM3_COM_BFO, AIRLINER_COM3_COM_HF1, AIRLINER_COM3_COM_HF2, AIRLINER_COM3_COM_LS, AIRLINER_COM3_COM_NAV, AIRLINER_COM3_COM_NAV_COVER, AIRLINER_COM3_COM_VHF1, AIRLINER_COM3_COM_VHF2, AIRLINER_COM3_COM_VHF3, AIRLINER_COM3_COM_VOR, AIRLINER_COM3_HF1_VOL, AIRLINER_COM3_HF1_VOL_BUTTON, AIRLINER_COM3_HF2_VOL, AIRLINER_COM3_HF2_VOL_BUTTON, AIRLINER_COM3_INTRAD, AIRLINER_COM3_INT_VOL, AIRLINER_COM3_INT_VOL_BUTTON, AIRLINER_COM3_LS_VOL, AIRLINER_COM3_LS_VOL_BUTTON, AIRLINER_COM3_MKR_VOL, AIRLINER_COM3_MKR_VOL_BUTTON, AIRLINER_COM3_PA_VOL, AIRLINER_COM3_PA_VOL_BUTTON, AIRLINER_COM3_SAT1_VOL, AIRLINER_COM3_SAT1_VOL_BUTTON, AIRLINER_COM3_SAT2_VOL, AIRLINER_COM3_SAT2_VOL_BUTTON, AIRLINER_COM3_VHF1_VOL, AIRLINER_COM3_VHF1_VOL_BUTTON, AIRLINER_COM3_VHF2_VOL, AIRLINER_COM3_VHF2_VOL_BUTTON, AIRLINER_COM3_VHF3_VOL, AIRLINER_COM3_VHF3_VOL_BUTTON, AIRLINER_COM3_VHF_ONOFF, AIRLINER_COM3_VOR1_VOL, AIRLINER_COM3_VOR1_VOL_BUTTON, AIRLINER_COM3_VOR2_VOL, AIRLINER_COM3_VOR2_VOL_BUTTON.

### CPT (103 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_CPT_ATC_MSG | A330_ATC_MSG |
| AIRLINER_CPT_BARO_SET_PULL | left out: pressed by baro knob (the captain's knob to STD) |
| AIRLINER_CPT_BARO_SET_PUSH | left out: pressed by baro knob (the captain's knob to QNH) |
| AIRLINER_CPT_BARO_TOGGLE | A330_CPT_BARO_UNIT |
| AIRLINER_CPT_BTN_CAB | A330_CPT_BTN_CAB |
| AIRLINER_CPT_BTN_HF1 | A330_CPT_BTN_HF1 |
| AIRLINER_CPT_BTN_HF2 | A330_CPT_BTN_HF2 |
| AIRLINER_CPT_BTN_INT | A330_CPT_BTN_INT |
| AIRLINER_CPT_BTN_PA | A330_CPT_BTN_PA |
| AIRLINER_CPT_BTN_TRANSFER | A330_CPT_BTN_TRANSFER |
| AIRLINER_CPT_BTN_VHF1 | A330_CPT_BTN_VHF1 |
| AIRLINER_CPT_BTN_VHF2 | A330_CPT_BTN_VHF2 |
| AIRLINER_CPT_BTN_VHF3 | A330_CPT_BTN_VHF3 |
| AIRLINER_CPT_CHRONO | A330_CHRONO |
| AIRLINER_CPT_COM_ADF | A330_CPT_COM_ADF |
| AIRLINER_CPT_COM_AM | A330_CPT_COM_AM |
| AIRLINER_CPT_COM_BFO | A330_CPT_COM_BFO |
| AIRLINER_CPT_COM_HF1 | A330_CPT_COM_HF1 |
| AIRLINER_CPT_COM_HF2 | A330_CPT_COM_HF2 |
| AIRLINER_CPT_COM_LS | A330_CPT_COM_LS |
| AIRLINER_CPT_COM_NAV | A330_RMP1_NAV |
| AIRLINER_CPT_COM_NAV_COVER | A330_RMP1_NAV_COVER |
| AIRLINER_CPT_COM_VHF1 | A330_CPT_COM_VHF1 |
| AIRLINER_CPT_COM_VHF2 | A330_CPT_COM_VHF2 |
| AIRLINER_CPT_COM_VHF3 | A330_CPT_COM_VHF3 |
| AIRLINER_CPT_COM_VOR | A330_CPT_COM_VOR |
| AIRLINER_CPT_CONSOLE_TOGGLE | A330_CPT_CONSOLE_LT |
| AIRLINER_CPT_EFIS_DMC_NORM | A330_EFIS_DMC |
| AIRLINER_CPT_FD | A330_CPT_FD |
| AIRLINER_CPT_LS | A330_CPT_LS |
| AIRLINER_CPT_MASTER_CAUT | A330_MASTER_CAUT |
| AIRLINER_CPT_MASTER_WARN | A330_MASTER_WARN |
| AIRLINER_CPT_MCDU_BRT | A330_CPT_MCDU_BRT |
| AIRLINER_CPT_ND_BRT | A330_CPT_ND_BRT |
| AIRLINER_CPT_PFD_BRT | A330_CPT_PFD_BRT |
| AIRLINER_CPT_PFD_ND | A330_CPT_PFD_ND_XFR |
| AIRLINER_CPT_RAIN_RPL | A330_CPT_RAIN_RPL |
| AIRLINER_CPT_TERR_ON_ND | A330_CPT_TERR_ON_ND |
| AIRLINER_CPT_VHF_ONOFF | A330_RMP1_POWER |
| AIRLINER_CPT_WIPER_SWITCH | A330_CPT_WIPER |

Not offered (63): AIRLINER_CPT_ADF1_VOL, AIRLINER_CPT_ADF1_VOL_BUTTON, AIRLINER_CPT_ADF2_VOL, AIRLINER_CPT_ADF2_VOL_BUTTON, AIRLINER_CPT_AIRVENT_SLIDER, AIRLINER_CPT_AUTOLAND, AIRLINER_CPT_BARO_SET, AIRLINER_CPT_BLIND_REAR_CLIP, AIRLINER_CPT_BTN_SAT1, AIRLINER_CPT_BTN_SAT2, AIRLINER_CPT_CAB_VOL, AIRLINER_CPT_CAB_VOL_BUTTON, AIRLINER_CPT_COMM_KHZ, AIRLINER_CPT_COMM_MHZ, AIRLINER_CPT_DCDU_BRT, AIRLINER_CPT_EFB_BRT, AIRLINER_CPT_EFB_HOME, AIRLINER_CPT_FOOTREST_CLICK, AIRLINER_CPT_FOOT_WARMER_TOGGLE, AIRLINER_CPT_FRONTBLIND_CLICK, AIRLINER_CPT_HF1_VOL, AIRLINER_CPT_HF1_VOL_BUTTON, AIRLINER_CPT_HF2_VOL, AIRLINER_CPT_HF2_VOL_BUTTON, AIRLINER_CPT_INTRAD, AIRLINER_CPT_INT_VOL, AIRLINER_CPT_INT_VOL_BUTTON, AIRLINER_CPT_JOY_DISCO, AIRLINER_CPT_LEFT_ARMREST, AIRLINER_CPT_LOUDSPEAKER, AIRLINER_CPT_LS_VOL, AIRLINER_CPT_LS_VOL_BUTTON, AIRLINER_CPT_LT_TOGGLE, AIRLINER_CPT_MAP_TOGGLE, AIRLINER_CPT_MKR_VOL, AIRLINER_CPT_MKR_VOL_BUTTON, AIRLINER_CPT_OXY_COVER, AIRLINER_CPT_OXY_TEST, AIRLINER_CPT_PA_VOL, AIRLINER_CPT_PA_VOL_BUTTON, AIRLINER_CPT_READING_LT_KNOB, AIRLINER_CPT_REARBLIND_CLICK, AIRLINER_CPT_RIGHT_ARMREST, AIRLINER_CPT_SAT1_VOL, AIRLINER_CPT_SAT1_VOL_BUTTON, AIRLINER_CPT_SAT2_VOL, AIRLINER_CPT_SAT2_VOL_BUTTON, AIRLINER_CPT_TER_BRT, AIRLINER_CPT_TILLER_DISCO, AIRLINER_CPT_TRAY_CLICK, AIRLINER_CPT_VHF1_VOL, AIRLINER_CPT_VHF1_VOL_BUTTON, AIRLINER_CPT_VHF2_VOL, AIRLINER_CPT_VHF2_VOL_BUTTON, AIRLINER_CPT_VHF3_VOL, AIRLINER_CPT_VHF3_VOL_BUTTON, AIRLINER_CPT_VISOR_CLICK, AIRLINER_CPT_VOR1_VOL, AIRLINER_CPT_VOR1_VOL_BUTTON, AIRLINER_CPT_VOR2_VOL, AIRLINER_CPT_VOR2_VOL_BUTTON, AIRLINER_CPT_WINDOW_HANDLE, AIRLINER_CPT_WINDOW_PIN.

### DITCHING (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_DITCHING | A330_DITCHING |
| AIRLINER_DITCHING_COVER | A330_DITCHING_COVER |

### ECAM (25 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_ECAM_ALL | A330_ECAM_ALL |
| AIRLINER_ECAM_APU | A330_ECAM_APU |
| AIRLINER_ECAM_BLEED | A330_ECAM_BLEED |
| AIRLINER_ECAM_CB | A330_ECAM_CB |
| AIRLINER_ECAM_CLR | A330_ECAM_CLR |
| AIRLINER_ECAM_CLR1 | A330_ECAM_CLR1 |
| AIRLINER_ECAM_COND | A330_ECAM_COND |
| AIRLINER_ECAM_DMC_AUTO | A330_ECAM_DMC |
| AIRLINER_ECAM_DOOR | A330_ECAM_DOOR |
| AIRLINER_ECAM_ELAC | A330_ECAM_ELAC |
| AIRLINER_ECAM_ELDC | A330_ECAM_ELDC |
| AIRLINER_ECAM_EMERCANC | A330_ECAM_EMER_CANC |
| AIRLINER_ECAM_EMERCANC_GAURD | A330_ECAM_EMER_CANC_COVER |
| AIRLINER_ECAM_ENG | A330_ECAM_ENG |
| AIRLINER_ECAM_FCTL | A330_ECAM_FCTL |
| AIRLINER_ECAM_FUEL | A330_ECAM_FUEL |
| AIRLINER_ECAM_HYD | A330_ECAM_HYD |
| AIRLINER_ECAM_LOWDISP | A330_ECAM_LOWER_BRT |
| AIRLINER_ECAM_PRESS | A330_ECAM_PRESS |
| AIRLINER_ECAM_RCL | A330_ECAM_RCL |
| AIRLINER_ECAM_STS | A330_ECAM_STS |
| AIRLINER_ECAM_SWITCH | A330_ECAM_ND_XFR |
| AIRLINER_ECAM_TOCONFIG | A330_TO_CONFIG |
| AIRLINER_ECAM_UPDISP | A330_ECAM_UPPER_BRT |
| AIRLINER_ECAM_WHEEL | A330_ECAM_WHEEL |

### EFIS (18 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_EFIS_CPT_ARPT | A330_CPT_ND_ARPT |
| AIRLINER_EFIS_CPT_CSTR | A330_CPT_ND_CSTR |
| AIRLINER_EFIS_CPT_MODE | A330_CPT_ND_MODE |
| AIRLINER_EFIS_CPT_NDB | A330_CPT_ND_NDB |
| AIRLINER_EFIS_CPT_RANGE | A330_CPT_ND_RANGE |
| AIRLINER_EFIS_CPT_VOR | A330_CPT_ND_VOR |
| AIRLINER_EFIS_CPT_VOR1 | A330_CPT_ND_NAV1 |
| AIRLINER_EFIS_CPT_VOR2 | A330_CPT_ND_NAV2 |
| AIRLINER_EFIS_CPT_WPT | A330_CPT_ND_WPT |
| AIRLINER_EFIS_FO_ARPT | A330_FO_ND_ARPT |
| AIRLINER_EFIS_FO_CSTR | A330_FO_ND_CSTR |
| AIRLINER_EFIS_FO_MODE | A330_FO_ND_MODE |
| AIRLINER_EFIS_FO_NDB | A330_FO_ND_NDB |
| AIRLINER_EFIS_FO_RANGE | A330_FO_ND_RANGE |
| AIRLINER_EFIS_FO_VOR | A330_FO_ND_VOR |
| AIRLINER_EFIS_FO_VOR1 | A330_FO_ND_NAV1 |
| AIRLINER_EFIS_FO_VOR2 | A330_FO_ND_NAV2 |
| AIRLINER_EFIS_FO_WPT | A330_FO_ND_WPT |

### ELEC (18 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_ELEC_AC_ESS | A330_AC_ESS |
| AIRLINER_ELEC_AC_ESS_COVER | A330_AC_ESS_COVER |
| AIRLINER_ELEC_APU_BAT | A330_APU_BAT |
| AIRLINER_ELEC_APU_GEN | A330_APU_GEN |
| AIRLINER_ELEC_BAT1 | A330_BAT1 |
| AIRLINER_ELEC_BAT2 | A330_BAT2 |
| AIRLINER_ELEC_BATT_TOGGLE | A330_BAT_DISPLAY |
| AIRLINER_ELEC_BUS_TIE | A330_BUS_TIE |
| AIRLINER_ELEC_COMMERCIAL | A330_COMMERCIAL |
| AIRLINER_ELEC_EXTA | A330_EXT_A |
| AIRLINER_ELEC_EXTB | A330_EXT_B |
| AIRLINER_ELEC_GALLEY | A330_GALLEY |
| AIRLINER_ELEC_GEN1 | A330_GEN1 |
| AIRLINER_ELEC_GEN2 | A330_GEN2 |
| AIRLINER_ELEC_IDG1 | A330_IDG1 |
| AIRLINER_ELEC_IDG1_COVER | A330_IDG1_COVER |
| AIRLINER_ELEC_IDG2 | A330_IDG2 |
| AIRLINER_ELEC_IDG2_COVER | A330_IDG2_COVER |

### EMER (5 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_EMER_EXIT_TOGGLE | A330_EMER_EXIT_LT |

Not offered (4):

- AIRLINER_EMER_CALL
- AIRLINER_EMER_CALL_COVER
- AIRLINER_EMER_GEN_TEST_BUTTON
- AIRLINER_EMER_GEN_TEST_GAURD

### ENG1 (8 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_ENG1_AGENT1 | A330_ENG1_AGENT1 |
| AIRLINER_ENG1_AGENT2 | A330_ENG1_AGENT2 |
| AIRLINER_ENG1_BLEED | A330_ENG1_BLEED |
| AIRLINER_ENG1_CUTOFF | A330_ENG1_MASTER |
| AIRLINER_ENG1_FIRE_HANDLE | A330_ENG1_FIRE |
| AIRLINER_ENG1_FIRE_HANDLE_COVER | A330_ENG1_FIRE_COVER |
| AIRLINER_ENG1_ICE | A330_ENG1_ANTI_ICE |

Not offered (1):

- AIRLINER_ENG1_ATDISCO

### ENG2 (8 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_ENG2_AGENT1 | A330_ENG2_AGENT1 |
| AIRLINER_ENG2_AGENT2 | A330_ENG2_AGENT2 |
| AIRLINER_ENG2_BLEED | A330_ENG2_BLEED |
| AIRLINER_ENG2_CUTOFF | A330_ENG2_MASTER |
| AIRLINER_ENG2_FIRE_HANDLE | A330_ENG2_FIRE |
| AIRLINER_ENG2_FIRE_HANDLE_COVER | A330_ENG2_FIRE_COVER |
| AIRLINER_ENG2_ICE | A330_ENG2_ANTI_ICE |

Not offered (1):

- AIRLINER_ENG2_ATDISCO

### ENGINE (3 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_ENGINE_FIRE_TEST | A330_ENG_FIRE_TEST |

Not offered (2):

- ENGINE_THROTTLE_1
- ENGINE_THROTTLE_2

### EVAC (4 events)

Not offered (4):

- AIRLINER_EVAC_COMMAND
- AIRLINER_EVAC_COMMAND_COVER
- AIRLINER_EVAC_CPT_TOGGLE
- AIRLINER_EVAC_HORN

### FADEC (4 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_FADEC_ENG1 | A330_FADEC1 |
| AIRLINER_FADEC_ENG1_COVER | A330_FADEC1_COVER |
| AIRLINER_FADEC_ENG2 | A330_FADEC2 |
| AIRLINER_FADEC_ENG2_COVER | A330_FADEC2_COVER |

### FLOOD (2 events)

Not offered (2):

- AIRLINER_FLOOD_LT_PED_KNOB
- AIRLINER_FLOOD_MAIN_PNL_KNOB

### FLTCTL (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_FLTCTL_PRIM1 | A330_PRIM1 |
| AIRLINER_FLTCTL_SEC1 | A330_SEC1 |

### FO (103 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_FO_BARO_SET_PULL | left out: pressed by baro knob (the first officer's knob to STD) |
| AIRLINER_FO_BARO_SET_PUSH | left out: pressed by baro knob (the first officer's knob to QNH) |
| AIRLINER_FO_BARO_TOGGLE | A330_FO_BARO_UNIT |
| AIRLINER_FO_CONSOLE_TOGGLE | A330_FO_CONSOLE_LT |
| AIRLINER_FO_FD | A330_FO_FD |
| AIRLINER_FO_LS | A330_FO_LS |
| AIRLINER_FO_MCDU_BRT | A330_FO_MCDU_BRT |
| AIRLINER_FO_ND_BRT | A330_FO_ND_BRT |
| AIRLINER_FO_PFD_BRT | A330_FO_PFD_BRT |
| AIRLINER_FO_PFD_ND | A330_FO_PFD_ND_XFR |
| AIRLINER_FO_TERR_ON_ND | A330_FO_TERR_ON_ND |
| AIRLINER_FO_WIPER_SWITCH | A330_FO_WIPER |

Not offered (91): AIRLINER_FO_ADF1_VOL, AIRLINER_FO_ADF1_VOL_BUTTON, AIRLINER_FO_ADF2_VOL, AIRLINER_FO_ADF2_VOL_BUTTON, AIRLINER_FO_AIRVENT_SLIDER, AIRLINER_FO_ATC_MSG, AIRLINER_FO_AUTOLAND, AIRLINER_FO_BARO_SET, AIRLINER_FO_BLIND_REAR_CLIP, AIRLINER_FO_BTN_CAB, AIRLINER_FO_BTN_HF1, AIRLINER_FO_BTN_HF2, AIRLINER_FO_BTN_INT, AIRLINER_FO_BTN_PA, AIRLINER_FO_BTN_SAT1, AIRLINER_FO_BTN_SAT2, AIRLINER_FO_BTN_TRANSFER, AIRLINER_FO_BTN_VHF1, AIRLINER_FO_BTN_VHF2, AIRLINER_FO_BTN_VHF3, AIRLINER_FO_CAB_VOL, AIRLINER_FO_CAB_VOL_BUTTON, AIRLINER_FO_CHRONO, AIRLINER_FO_COMM_KHZ, AIRLINER_FO_COMM_MHZ, AIRLINER_FO_COM_ADF, AIRLINER_FO_COM_AM, AIRLINER_FO_COM_BFO, AIRLINER_FO_COM_HF1, AIRLINER_FO_COM_HF2, AIRLINER_FO_COM_LS, AIRLINER_FO_COM_NAV, AIRLINER_FO_COM_NAV_COVER, AIRLINER_FO_COM_VHF1, AIRLINER_FO_COM_VHF2, AIRLINER_FO_COM_VHF3, AIRLINER_FO_COM_VOR, AIRLINER_FO_DCDU_BRT, AIRLINER_FO_EFB_BRT, AIRLINER_FO_EFB_HOME, AIRLINER_FO_EFIS_DMC_NORM, AIRLINER_FO_FOOTREST_CLICK, AIRLINER_FO_FOOT_WARMER_TOGGLE, AIRLINER_FO_FRONTBLIND_CLICK, AIRLINER_FO_HF1_VOL, AIRLINER_FO_HF1_VOL_BUTTON, AIRLINER_FO_HF2_VOL, AIRLINER_FO_HF2_VOL_BUTTON, AIRLINER_FO_INTRAD, AIRLINER_FO_INT_VOL, AIRLINER_FO_INT_VOL_BUTTON, AIRLINER_FO_JOY_DISCO, AIRLINER_FO_LEFT_ARMREST, AIRLINER_FO_LOUDSPEAKER, AIRLINER_FO_LS_VOL, AIRLINER_FO_LS_VOL_BUTTON, AIRLINER_FO_LT_CEILING_TOGGLE, AIRLINER_FO_MAP_TOGGLE, AIRLINER_FO_MASTER_CAUT, AIRLINER_FO_MASTER_WARN, AIRLINER_FO_MKR_VOL, AIRLINER_FO_MKR_VOL_BUTTON, AIRLINER_FO_OXY_COVER, AIRLINER_FO_OXY_TEST, AIRLINER_FO_PA_VOL, AIRLINER_FO_PA_VOL_BUTTON, AIRLINER_FO_RAIN_RPL, AIRLINER_FO_READING_LT_KNOB, AIRLINER_FO_REARBLIND_CLICK, AIRLINER_FO_RIGHT_ARMREST, AIRLINER_FO_SAT1_VOL, AIRLINER_FO_SAT1_VOL_BUTTON, AIRLINER_FO_SAT2_VOL, AIRLINER_FO_SAT2_VOL_BUTTON, AIRLINER_FO_TER_BRT, AIRLINER_FO_TILLER_DISCO, AIRLINER_FO_TRAY_CLICK, AIRLINER_FO_VHF1_VOL, AIRLINER_FO_VHF1_VOL_BUTTON, AIRLINER_FO_VHF2_VOL, AIRLINER_FO_VHF2_VOL_BUTTON, AIRLINER_FO_VHF3_VOL, AIRLINER_FO_VHF3_VOL_BUTTON, AIRLINER_FO_VHF_ONOFF, AIRLINER_FO_VISOR_CLICK, AIRLINER_FO_VOR1_VOL, AIRLINER_FO_VOR1_VOL_BUTTON, AIRLINER_FO_VOR2_VOL, AIRLINER_FO_VOR2_VOL_BUTTON, AIRLINER_FO_WINDOW_HANDLE, AIRLINER_FO_WINDOW_PIN.

### FTL (3 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_FTL_CTL_PRIM2 | A330_PRIM2 |
| AIRLINER_FTL_CTL_PRIM3 | A330_PRIM3 |
| AIRLINER_FTL_CTL_SEC2 | A330_SEC2 |

### FUEL (15 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_FUEL_CTR_L | A330_FUEL_CTR_L |
| AIRLINER_FUEL_CTR_R | A330_FUEL_CTR_R |
| AIRLINER_FUEL_CTR_XFR | A330_FUEL_CTR_XFR |
| AIRLINER_FUEL_ENG1_L1 | A330_FUEL1L1 |
| AIRLINER_FUEL_ENG1_L2 | A330_FUEL1L2 |
| AIRLINER_FUEL_ENG1_LSTBY | A330_FUEL1LSTBY |
| AIRLINER_FUEL_ENG2_R1 | A330_FUEL2R1 |
| AIRLINER_FUEL_ENG2_R2 | A330_FUEL2R2 |
| AIRLINER_FUEL_ENG2_RSTBY | A330_FUEL2RSTBY |
| AIRLINER_FUEL_MODE | A330_FUEL_TRIM_MODE |
| AIRLINER_FUEL_TK_XFR | A330_FUEL_TK_XFR |
| AIRLINER_FUEL_TK_XFR_COVER | A330_FUEL_TK_XFR_COVER |
| AIRLINER_FUEL_XFEED | A330_FUEL_XFEED |

Not offered (2):

- AIRLINER_FUEL_FEED_TOGGLE
- AIRLINER_FUEL_FEED_TOGGLE_GAURD

### FWD (3 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_FWD_AGENT | A330_FWD_CARGO_AGENT |
| AIRLINER_FWD_AGENT_COVER | A330_FWD_CARGO_AGENT_COVER |
| AIRLINER_FWD_ISO_VALVE | A330_FWD_ISO |

### GND (3 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_GND_COOL | A330_GND_COOL |
| AIRLINER_GND_CTL | A330_CVR_GND_CTL |

Not offered (1):

- AIRLINER_GND_HF_DATA

### GPWS (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_GPWS_SYS | A330_GPWS_SYS |
| AIRLINER_GPWS_TERR | A330_GPWS_TERR |

### GRAVITY (4 events)

Not offered (4):

- AIRLINER_GRAVITY_GEAR_LEFT_GAURD
- AIRLINER_GRAVITY_GEAR_OUTER_GAURD
- AIRLINER_GRAVITY_GEAR_RIGHT_GAURD
- AIRLINER_GRAVITY_GEAR_TOGGLE

### HOT (3 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_HOT_AIR | A330_CARGO_HOT_AIR |
| AIRLINER_HOT_AIR1 | A330_HOT_AIR1 |
| AIRLINER_HOT_AIR2 | A330_HOT_AIR2 |

### HYD (19 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_HYD_BLUE | A330_HYD_BLUE |
| AIRLINER_HYD_BLUE_COVER | A330_HYD_BLUE_COVER |
| AIRLINER_HYD_BLUE_ELEC | A330_HYD_BLUE_ELEC_ON |
| AIRLINER_HYD_BLUE_ELEC_COVER | A330_HYD_BLUE_ELEC_COVER |
| AIRLINER_HYD_BLUE_OFF | A330_HYD_BLUE_ELEC_OFF |
| AIRLINER_HYD_GREEN | A330_HYD_GREEN |
| AIRLINER_HYD_GREEN_COVER | A330_HYD_GREEN_COVER |
| AIRLINER_HYD_GREEN_ELEC | A330_HYD_GREEN_ELEC_ON |
| AIRLINER_HYD_GREEN_ELEC_COVER | A330_HYD_GREEN_ELEC_COVER |
| AIRLINER_HYD_GREEN_ENG2 | A330_HYD_GREEN_ENG2 |
| AIRLINER_HYD_GREEN_ENG2_COVER | A330_HYD_GREEN_ENG2_COVER |
| AIRLINER_HYD_GREEN_OFF | A330_HYD_GREEN_ELEC_OFF |
| AIRLINER_HYD_RAT_MAN | A330_RAT |
| AIRLINER_HYD_RAT_MAN_COVER | A330_RAT_COVER |
| AIRLINER_HYD_YELLOW | A330_HYD_YELLOW |
| AIRLINER_HYD_YELLOW_COVER | A330_HYD_YELLOW_COVER |
| AIRLINER_HYD_YELLOW_ELEC | A330_HYD_YELLOW_ELEC_ON |
| AIRLINER_HYD_YELLOW_ELEC_COVER | A330_HYD_YELLOW_ELEC_COVER |
| AIRLINER_HYD_YELLOW_OFF | A330_HYD_YELLOW_ELEC_OFF |

### INR (4 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_INR_TK_L | A330_INR_TK_L |
| AIRLINER_INR_TK_L_COVER | A330_INR_TK_L_COVER |
| AIRLINER_INR_TK_R | A330_INR_TK_R |
| AIRLINER_INR_TK_R_COVER | A330_INR_TK_R_COVER |

### ISIS (7 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_ISIS_BUGS | A330_ISIS_BUGS |
| AIRLINER_ISIS_LS | A330_ISIS_LS |
| AIRLINER_ISIS_RST | A330_ISIS_RST |

Not offered (4):

- AIRLINER_ISIS_BARO_INNER
- AIRLINER_ISIS_BARO_OUTER
- AIRLINER_ISIS_MINUS
- AIRLINER_ISIS_PLUS

### LDG (3 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_LDG_ELEV | A330_LDG_ELEV |

Not offered (2):

- AIRLINER_LDG_SYS_TOGGLE
- AIRLINER_LDG_SYS_TOGGLE_GAURD

### MAN (6 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_MAN_START_1 | A330_MAN_START1 |
| AIRLINER_MAN_START_1_COVER | A330_MAN_START1_COVER |
| AIRLINER_MAN_START_2 | A330_MAN_START2 |
| AIRLINER_MAN_START_2_COVER | A330_MAN_START2_COVER |

Not offered (2):

- AIRLINER_MAN_ON
- AIRLINER_MAN_ON_COVER

### MASK (2 events)

Not offered (2):

- AIRLINER_MASK_MAN_BUTTON
- AIRLINER_MASK_MAN_GAURD

### MCU (15 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_MCU_ALT | left out: the FCU window sets the altitude through the INI_ALTITUDE_DIAL variable |
| AIRLINER_MCU_ALTSELECT | left out: set by FCU window (altitude knob step, 100 or 1000 feet) |
| AIRLINER_MCU_ALT_PULL | left out: the FCU window's altitude knob pull is offered through the INI_FCU_ALTITUDE_PULL_COMMAND press flag |
| AIRLINER_MCU_ALT_PUSH | left out: the FCU window's altitude knob push is offered through the INI_FCU_ALTITUDE_PUSH_COMMAND press flag |
| AIRLINER_MCU_HDG | left out: the FCU window sets the heading through the INI_HEADING_DIAL variable |
| AIRLINER_MCU_HDG_PULL | left out: the FCU window's heading knob pull is offered through the INI_FCU_SELECTED_HEADING_BUTTON press flag |
| AIRLINER_MCU_HDG_PUSH | left out: the FCU window's heading knob push is offered through the INI_FCU_MANAGED_HEADING_BUTTON press flag |
| AIRLINER_MCU_SPEED | left out: the FCU window sets the speed or Mach through the INI_AIRSPEED_DIAL variable |
| AIRLINER_MCU_SPEED_PULL | left out: the FCU window's speed knob pull is offered through the INI_FCU_SELECTED_SPEED_BUTTON press flag |
| AIRLINER_MCU_SPEED_PUSH | left out: the FCU window's speed knob push is offered through the INI_FCU_MANAGED_SPEED_BUTTON press flag |
| AIRLINER_MCU_VS | left out: the FCU window sets the vertical speed through the INI_VVI_DIAL variable |
| AIRLINER_MCU_VS_PULL | left out: pressed by FCU window (V/S knob pull) |
| AIRLINER_MCU_VS_PUSH | left out: pressed by FCU window (V/S knob push, with its own push action) |

Not offered (2):

- AIRLINER_MCU_BRT_KNOB
- AIRLINER_MCU_INT_KNOB

### N1 (4 events)

Not offered (4):

- AIRLINER_N1_MODE_1
- AIRLINER_N1_MODE_1_COVER
- AIRLINER_N1_MODE_2
- AIRLINER_N1_MODE_2_COVER

### NWS (2 events)

Not offered (2):

- AIRLINER_NWS_BUTTON
- AIRLINER_NWS_BUTTON_COVER

### OXY (6 events)

Not offered (6):

- AIRLINER_OXY_TEST_BLACK
- AIRLINER_OXY_TEST_BLACK_CPT
- AIRLINER_OXY_TEST_BLACK_FO
- AIRLINER_OXY_TEST_RED
- AIRLINER_OXY_TEST_RED_CPT
- AIRLINER_OXY_TEST_RED_FO

### PRINTER (3 events)

Not offered (3):

- AIRLINER_PRINTER_OFF
- AIRLINER_PRINTER_SLEW
- AIRLINER_PRINTER_TEST

### RAM (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_RAM_AIR | A330_RAM_AIR |
| AIRLINER_RAM_AIR_COVER | A330_RAM_AIR_COVER |

### SEAT (4 events)

Not offered (4):

- AIRLINER_SEAT_ARMREST_HEIGHT_CAPT
- AIRLINER_SEAT_ARMREST_HEIGHT_FO
- AIRLINER_SEAT_ARMREST_PITCH_CAPT
- AIRLINER_SEAT_ARMREST_PITCH_FO

### TCAS (15 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_TCAS_ABV | A330_TCAS_TRAFFIC |
| AIRLINER_TCAS_ALT | A330_XPDR_ALT_RPTG |
| AIRLINER_TCAS_ATC | A330_XPDR_SYSTEM |
| AIRLINER_TCAS_IDENT | A330_XPDR_IDENT |
| AIRLINER_TCAS_MODE | A330_XPDR_MODE |
| AIRLINER_TCAS_STBY | A330_TCAS_MODE |

Not offered (9):

- AIRLINER_TCAS_1
- AIRLINER_TCAS_2
- AIRLINER_TCAS_3
- AIRLINER_TCAS_4
- AIRLINER_TCAS_5
- AIRLINER_TCAS_6
- AIRLINER_TCAS_7
- AIRLINER_TCAS_CLR
- AIRLINER_TCAS_ZERO

### VALVE (2 events)

Not offered (2):

- AIRLINER_VALVE_SEL
- AIRLINER_VALVE_SEL_GAURD

### VENT (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_VENT_CAB | A330_CAB_FANS |
| AIRLINER_VENT_EXTRACT | A330_EXTRACT |

### VOR (2 events)

Not offered (2):

- AIRLINER_VOR_TOGGLE1
- AIRLINER_VOR_TOGGLE2

### WING (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_WING_ICE | A330_WING_ANTI_ICE |
| AIRLINER_WING_TOGGLE | A330_WING_LT |

### WX (6 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_WX_GAIN | A330_WX_GAIN |
| AIRLINER_WX_MODE | A330_WX_MODE |
| AIRLINER_WX_PWS | A330_WX_PWS |
| AIRLINER_WX_SYS | A330_WX_SYS |
| AIRLINER_WX_TILT | A330_WX_TILT |

Not offered (1):

- AIRLINER_WX_CTRL

### DOOR (5 events)

Not offered (5):

- DOOR_LOCK_BACK
- DOOR_LOCK_BULK
- DOOR_LOCK_FRONT
- DOOR_LOCK_LEFT
- DOOR_LOCK_RIGHT

### UNKNOWN (30 events)

Not offered (30): UNKNOWN_A330_GEAR_FRONT_MAIN, UNKNOWN_A330_GEAR_MAIN_STRUT_LEFT_1, UNKNOWN_A330_GEAR_MAIN_STRUT_RIGHT_1, UNKNOWN_A330_GEAR_M_TIRE_LF, UNKNOWN_A330_GEAR_M_TIRE_LR, UNKNOWN_A330_GEAR_M_TIRE_RF, UNKNOWN_A330_GEAR_M_TIRE_RR, UNKNOWN_A330_GEAR_SHOCKABSORBER_FRONT, UNKNOWN_A330_GEAR_SHOCKABSORBER_ROD_LEFT_1, UNKNOWN_A330_GEAR_SHOCKABSORBER_ROD_RIGHT_1, UNKNOWN_A330_WING_INNER_FLAP_LEFT, UNKNOWN_A330_WING_INNER_FLAP_RIGHT, UNKNOWN_CHOCKS, UNKNOWN_COVER_LEFT, UNKNOWN_COVER_RIGHT, UNKNOWN_DOOR1L, UNKNOWN_DOOR1R, UNKNOWN_DOOR2L, UNKNOWN_DOOR2R, UNKNOWN_PITOT_COVERS, UNKNOWN_SKEL_ELEVATOR_LEFT, UNKNOWN_SKEL_ELEVATOR_RIGHT, UNKNOWN_SKEL_RUDDER, UNKNOWN_TIRE_NOSE_STILL, UNKNOWN_WING_LEFT_CHECKBOX_AILERON_INNER, UNKNOWN_WING_LEFT_CHECKBOX_AILERON_OUTER, UNKNOWN_WING_LEFT_CHECKBOX_INNER, UNKNOWN_WING_RIGHT_CHECKBOX_AILERON_INNER, UNKNOWN_WING_RIGHT_CHECKBOX_AILERON_OUTER, UNKNOWN_WING_RIGHT_CHECKBOX_INNER.

### Groups with one event (76 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_ADIR1 | A330_ADR1 |
| AIRLINER_ADIR2 | A330_ADR2 |
| AIRLINER_ADIR3 | A330_ADR3 |
| AIRLINER_ADIRS1_MODE | A330_IRS1_MODE |
| AIRLINER_ADIRS2_MODE | A330_IRS2_MODE |
| AIRLINER_ADIRS3_MODE | A330_IRS3_MODE |
| AIRLINER_ALT_PUSH | left out: pressed by FCU window (ALT hold) |
| AIRLINER_ANNLT_TOGGLE | A330_ANN_LT |
| AIRLINER_ANTISKD | A330_ANTISKID |
| AIRLINER_AP1_PUSH | left out: the FCU window's AP1 button is offered through the INI_AP1_BUTTON press flag |
| AIRLINER_AP2_PUSH | left out: the FCU window's AP2 button is offered through the INI_AP2_BUTTON press flag |
| AIRLINER_APPR_PUSH | left out: the FCU window's APPR button is offered through the INI_APPROACH_BUTTON press flag |
| AIRLINER_ATHR_PUSH | left out: pressed by FCU window (A/THR) |
| AIRLINER_ATT_HDG_SELECT | A330_ATT_HDG |
| AIRLINER_AUDIO_SELECT | A330_AUDIO_SW |
| AIRLINER_BEACON_TOGGLE | A330_BEACON |
| AIRLINER_BRK_FAN | A330_BRAKE_FAN |
| AIRLINER_BULK_ISO_VALVE | A330_BULK_ISO |
| AIRLINER_CABIN_TEMP | A330_CABIN_TEMP |
| AIRLINER_CTL_TOGGLE | A330_DOME |
| AIRLINER_DFDR_EVENT | A330_DFDR_EVENT |
| AIRLINER_ENG_START | A330_ENG_MODE |
| AIRLINER_FLAPS | left out: the Flaps and Speedbrake panel's flap row drives the lever through K:FLAPS_SET |
| AIRLINER_FLAP_MODE | A330_GPWS_FLAP |
| AIRLINER_FM_SELECT | A330_FM_SW |
| AIRLINER_GEAR_LEVER | left out: the Landing Gear panel's gear row drives the lever through K:GEAR_DOWN / K:GEAR_UP |
| AIRLINER_GS_MODE | A330_GPWS_GS |
| AIRLINER_IR1 | A330_IR1 |
| AIRLINER_IR2 | A330_IR2 |
| AIRLINER_IR3 | A330_IR3 |
| AIRLINER_LAND_RECOVERY | A330_LAND_RECOVERY |
| AIRLINER_LDGLIGHT_TOGGLE | A330_LANDING_LT |
| AIRLINER_LOC_PUSH | left out: the FCU window's LOC button is offered through the INI_LOCALIZER_BUTTON press flag |
| AIRLINER_MECH_CALL | A330_MECH_CALL |
| AIRLINER_METRIC | left out: set by FCU window (metric altitude toggle) |
| AIRLINER_MODE_SEL | A330_PRESS_MODE |
| AIRLINER_NAVLOGO_TOGGLE | A330_NAV_LOGO |
| AIRLINER_NOSMOKING_TOGGLE | A330_NO_SMOKING |
| AIRLINER_OXYGEN_CREW | A330_CREW_OXY |
| AIRLINER_PACK1 | A330_PACK1 |
| AIRLINER_PACK2 | A330_PACK2 |
| AIRLINER_PACK_FLOW | A330_PACK_FLOW |
| AIRLINER_PARKINGBRAKE | left out: set by parking brake control |
| AIRLINER_PROBE_HEAT | A330_PROBE_HEAT |
| AIRLINER_RWYTURN_TOGGLE | A330_RWY_TURNOFF |
| AIRLINER_SEATBELTS_TOGGLE | A330_SEAT_BELTS |
| AIRLINER_SPDMACH | left out: set by FCU window (speed/Mach toggle) |
| AIRLINER_SPOILER_LEVER | left out: the Flaps and Speedbrake panel's speedbrake row drives the lever through K:SPOILERS_SET, and its ground spoilers row through K:SPOILERS_ARM_ON / K:SPOILERS_ARM_OFF |
| AIRLINER_STBYCOMPASS_LIGHT | A330_STBY_COMPASS_LT |
| AIRLINER_STROBE_TOGGLE | A330_STROBE |
| AIRLINER_TAXILIGHT_TOGGLE | A330_NOSE_LT |
| AIRLINER_XBLEED | A330_XBLEED |

Not offered (24): AIRLINER_ACMS_PRINT, AIRLINER_AIRCOND_RESET, AIRLINER_CAB_REST, AIRLINER_CKPT_DOOR_VIDEO, AIRLINER_COCKPITDOOR, AIRLINER_CTR_VISOR_BASE, AIRLINER_ELEVATORTRIM, AIRLINER_FLT_RESET, AIRLINER_HDVS, AIRLINER_IFEC, AIRLINER_INTEG_LT_KNOB, AIRLINER_NORTH_REF, AIRLINER_PAPER_ALARM, AIRLINER_PAX_SYS, AIRLINER_RUDDER_TRIM, AIRLINER_STBY_OXY_TEST, AIRLINER_STORM_TOGGLE, AIRLINER_SVCE_OVRD, AIRLINER_TMR_RESET, AIRLINER_TRIM_RESET, AIRLINER_TURB_DAMP, AIRLINER_VIDEO_BUTTON, AIRLINER_VS_CTL, AIRLINER_WASTE_FO.

## Part 2: counts by group

Groups with more than one event and something not offered. Every other group of that size is fully in the map or left out.

| Group | Events | Not offered |
| --- | --- | --- |
| FO | 103 | 91 |
| CPT | 103 | 63 |
| COM3 | 59 | 59 |
| UNKNOWN | 30 | 30 |
| TCAS | 15 | 9 |
| OXY | 6 | 6 |
| DOOR | 5 | 5 |
| EMER | 5 | 4 |
| EVAC | 4 | 4 |
| GRAVITY | 4 | 4 |
| ISIS | 7 | 4 |
| N1 | 4 | 4 |
| SEAT | 4 | 4 |
| PRINTER | 3 | 3 |
| CARGO | 5 | 2 |
| CLOCK | 5 | 2 |
| CMC1 | 2 | 2 |
| ENGINE | 3 | 2 |
| FLOOD | 2 | 2 |
| FUEL | 15 | 2 |
| LDG | 3 | 2 |
| MAN | 6 | 2 |
| MASK | 2 | 2 |
| MCU | 15 | 2 |
| NWS | 2 | 2 |
| VALVE | 2 | 2 |
| VOR | 2 | 2 |
| APU | 7 | 1 |
| COCKPIT | 2 | 1 |
| ENG1 | 8 | 1 |
| ENG2 | 8 | 1 |
| GND | 3 | 1 |
| WX | 6 | 1 |

Groups of one event that is not offered (24): ACMS, AIRCOND, CAB, CKPT, COCKPITDOOR, CTR, ELEVATORTRIM, FLT, HDVS, IFEC, INTEG, NORTH, PAPER, PAX, RUDDER, STBY, STORM, SVCE, TMR, TRIM, TURB, VIDEO, VS, WASTE.

Totals: 649 events, 271 in the map, 32 left out, 346 not offered.

## Part 3: the app's A32NX and Headwind A330 panel rows with no A330 match

This is the list plan 04 Task 5 works through; it is not a list of things to add now. It compares by function, not by label (the A32NX's "Strobe" matches the A330's `A330_STROBE`). "No event" means the catalogue above has no event for the function. "Stock event" marks a control those aircraft drive through a simulator event that is not an input event, so no input-event count can find it; the A330 may offer it the same way. A "candidate event" is a catalogued event in the not-offered list whose name suggests the same function. It is a guess from the name only, nothing is measured; Task 5 measures it or drops it.

The Headwind A330's panels are the A32NX's: the app's Headwind A330 definition inherits the A32NX panel list and changes only three rows (the second and third landing-light rows, and the two glareshield brightness knobs, which it drops). So the lists below hold for both aircraft. Those three rows all have an A330 match.

Panels the A330 reaches only through its own definition (the FCU, gear, autobrake, parking brake, flaps and speedbrake) are in the map's own panels, not compared here row by row, except where the A32NX has a row the A330 does not offer.

### Overhead

- ELEC: emergency generator 1 line (no event); emergency generator test (candidate event, a guess from its name: `AIRLINER_EMER_GEN_TEST_BUTTON`, with its guard); external power available (a read-out; the A330 map has none).
- APU: autoexiting test (no event); autoexiting reset (candidate event, a guess from its name: `AIRLINER_APU_EXTING_RESET`).
- Oxygen: masks deployed, passenger oxygen light (read-outs, no A330 row); oxygen timer reset (no event). Candidate event, a guess from its name: `AIRLINER_MASK_MAN_BUTTON` with its guard, the `AIRLINER_OXY_TEST_*` events.
- Hydraulics: PTU and its fault light (no event); leak measurement, green, blue and yellow (no event, counted as one row below); the pump FAULT lights (read-outs; the A330 map has the pump rows only).
- Fuel: fuel mode select, manual (no event). Candidate event, a guess from its name: `AIRLINER_FUEL_FEED_TOGGLE` with its guard.
- Pressurization: manual cabin vertical-speed control (candidate event, a guess from its name: `AIRLINER_VS_CTL`, and `AIRLINER_VALVE_SEL` with its guard may be related; not shown to be).
- Ventilation: blower toggle (no event).
- Interior lighting: the pedestal, main panel, glareshield and overhead integral brightness knobs. Candidate events, a guess from their names: `AIRLINER_FLOOD_LT_PED_KNOB`, `AIRLINER_FLOOD_MAIN_PNL_KNOB`, `AIRLINER_INTEG_LT_KNOB`, `AIRLINER_MCU_BRT_KNOB`, `AIRLINER_MCU_INT_KNOB`. Whether the A32NX rows for these knobs are driven the same way is a guess to measure in Task 5.
- Recorder and miscellaneous: crew head set, recorder test, ELT on, ELT test and reset, DLS on (no event); avionics complete (no match found; `AIRLINER_CMC1` is not offered and is not shown to be the same).
- Flight Control Computers: FAC 1 and FAC 2 (no event); rudder trim reset and rudder trim left and right (stock events on those aircraft; candidate events, a guess from their names: `AIRLINER_RUDDER_TRIM`, `AIRLINER_TRIM_RESET`).
- Wipers: first officer rain repellent (candidate event, a guess from its name: `AIRLINER_FO_RAIN_RPL`).
- GPWS: flaps 3 mode and the GPWS test (no event).
- Calls: emergency call (candidate event, a guess from its name: `AIRLINER_EMER_CALL`, with its cover).
- Cockpit: cockpit door locked, door video (candidate events, a guess from their names: `AIRLINER_COCKPIT_DOOR_LOCK`, `AIRLINER_COCKPITDOOR`, `AIRLINER_CKPT_DOOR_VIDEO`); armrests and seat back (candidate events, a guess from their names: the `AIRLINER_SEAT_ARMREST_*` and `AIRLINER_CPT_LEFT_ARMREST`, `_RIGHT_ARMREST` events and their first officer twins).
- Evacuation: evacuation command, captain toggle and horn shut-off (candidate events, a guess from their names: `AIRLINER_EVAC_COMMAND` with its cover, `AIRLINER_EVAC_CPT_TOGGLE`, `AIRLINER_EVAC_HORN`).

### Glareshield

- FCU: expedite (no event); autopilot disconnect (no event); autothrust disconnect (candidate events, a guess from their names: `AIRLINER_ENG1_ATDISCO`, `AIRLINER_ENG2_ATDISCO`). The FCU knobs and buttons themselves are the definition's own panel (plan 05); their events are in the left-out list, offered through the FCU window's own flags and dials. Still not offered: `AIRLINER_MCU_BRT_KNOB` and `AIRLINER_MCU_INT_KNOB` (guess from the names: display brightness and integral light).
- EFIS, both sides: none missing.
- Warnings: none missing.

### Main instrument panel

- Autobrake: none missing (brake fans have a row).
- ISIS: baro mode (candidate events, a guess from their names: `AIRLINER_ISIS_BARO_INNER`, `AIRLINER_ISIS_BARO_OUTER`, `AIRLINER_ISIS_PLUS`, `AIRLINER_ISIS_MINUS`).
- Source switching: true or magnetic north reference (candidate event, a guess from its name: `AIRLINER_NORTH_REF`).
- Clock, system display, gear: none missing.

### Pedestal

- Flight controls and speed brake: none missing for the lever, ground spoilers and flaps (the definition drives them through stock events, see the left-out reasons). Speed brake slider and the on and off events are the app's A32NX panels' own helpers; no A330 match is needed.
- Thrust levers: all-engine and per-engine detent rows (stock events on those aircraft; candidate events, a guess from their names: `ENGINE_THROTTLE_1`, `ENGINE_THROTTLE_2`).
- Engines: none missing.
- ECAM control panel: none missing.
- Weather radar: multiscan auto and GCS auto (no event); candidate event, a guess from its name: `AIRLINER_WX_CTRL`, `AIRLINER_TURB_DAMP`.
- Transponder: transponder code (stock event on those aircraft; candidate events, a guess from their names: `AIRLINER_TCAS_1` to `AIRLINER_TCAS_7`, `AIRLINER_TCAS_CLR`, `AIRLINER_TCAS_ZERO`, which may be the transponder's keypad); the A32NX's datalink acknowledge is the A330's ATC message row.
- RMP: standby frequency, active frequency and swap for COM 1, 2 and 3 (stock events on those aircraft; candidate events, a guess from their names: `AIRLINER_CPT_COMM_MHZ`, `AIRLINER_CPT_COMM_KHZ`, their first officer twins and the whole `AIRLINER_COM3_*` group, 59 events).

### Size by panel

Rows with no A330 match: ELEC 3, APU 2, Oxygen 3, Hydraulics 4, Fuel 1, Pressurization 1, Ventilation 1, Interior lighting 4, Recorder and miscellaneous 6, Flight Control Computers 5, Wipers 1, GPWS 2, Calls 1, Cockpit 6, Evacuation 3, FCU 3, ISIS 1, Source switching 1, Thrust levers 3, Weather radar 2, Transponder 1, RMP 9. Of these, the stock-event rows (the ones no input-event count shows) are: rudder trim reset, rudder trim left and right, the three thrust lever detent rows, the transponder code and the nine RMP frequency rows.

## Panel sweep, 2026-11-27

### Conditions

Simulator: MSFS 2024 1.9.24.0. Aircraft: iniBuilds A330-200 VIP (GE), on the ground, engines off, stopped, parking brake set, external power on. Before the sweep the two batteries and the APU battery were switched to AUTO and external power A on, by calculator writes (main bus 28 V): ONE write each, and each came on, which fits the model below (one write, one press). The tool is `tools/a330-gen/PanelSweep` (its README): it writes through the app's own routes (SimConnect `SetInputEvent` by hash), reads each control back until it settles, and journals every control's starting value before its first write. The full run started at 21:29 and took about 13 minutes: 151 units (149 controls and 2 key families), none not reached, no SimConnect exception. Its files (the sweep table, its journal and the two restores) are kept outside the repository.

### Counts by verdict

- Read-only: 172 rows.
- Toggle: 73 controls.
- Dead: 10 controls.
- Already there: 2 controls.
- Lagged: 0. No settle took longer than 1.5 s.
- SimConnect exceptions: 0.

### The push-buttons that toggle

Finding, in plain words: 73 push-buttons are toggles under `SetInputEvent`, the route the app uses. Writing the same value twice flipped the read back. Every one started with its input event's own value at 0, whatever the switch showed.

Two patterns were measured.

The 9 controls where `SetInputEvent(0)` with the event at 0 flipped the read, and again flipped it back: `A330_FUEL1L1`, `A330_FUEL1L2`, `A330_FUEL1LSTBY`, `A330_FUEL2R1`, `A330_FUEL2R2`, `A330_FUEL2RSTBY` (the six engine-feed fuel pumps), `A330_CREW_OXY`, `A330_CPT_FD`, `A330_FO_FD`. Nothing else was sent to them, so a set of 1 from 0, and of 0 from 1, is not measured on these 9.

The other 64 controls: `SetInputEvent(1)` with the event at 0 flipped the read (in about 100 ms), and `SetInputEvent(1)` again, with the event at 1, flipped it back. The put-back then sent `SetInputEvent(0)` twice, and after the second the read was at its start. The put-back sends a second write only when the first did not leave the control at its start (its event's own value and its read both back); which of the two was not back is not recorded. The 64 are: A330_AC_ESS, A330_APU_BLEED, A330_APU_GEN, A330_BRAKE_FAN, A330_BULK_ISO, A330_BUS_TIE, A330_CAB_FANS, A330_CARGO_HOT_AIR, A330_COMMERCIAL, A330_CPT_LS, A330_CPT_ND_ARPT, A330_CPT_ND_CSTR, A330_CPT_ND_NDB, A330_CPT_ND_VOR, A330_CPT_ND_WPT, A330_CPT_TERR_ON_ND, A330_ENG1_ANTI_ICE, A330_ENG1_BLEED, A330_ENG2_ANTI_ICE, A330_ENG2_BLEED, A330_EXTRACT, A330_FADEC1, A330_FADEC2, A330_FO_LS, A330_FO_ND_ARPT, A330_FO_ND_CSTR, A330_FO_ND_NDB, A330_FO_ND_VOR, A330_FO_ND_WPT, A330_FO_TERR_ON_ND, A330_FUEL_TK_XFR, A330_FUEL_TRIM_MODE, A330_FUEL_XFEED, A330_GALLEY, A330_GEN1, A330_GEN2, A330_GPWS_FLAP, A330_GPWS_GS, A330_GPWS_SYS, A330_GPWS_TERR, A330_HOT_AIR1, A330_HOT_AIR2, A330_HYD_BLUE, A330_HYD_BLUE_ELEC_OFF, A330_HYD_BLUE_ELEC_ON, A330_HYD_GREEN, A330_HYD_GREEN_ELEC_OFF, A330_HYD_GREEN_ELEC_ON, A330_HYD_GREEN_ENG2, A330_HYD_YELLOW, A330_HYD_YELLOW_ELEC_OFF, A330_HYD_YELLOW_ELEC_ON, A330_INR_TK_L, A330_INR_TK_R, A330_LAND_RECOVERY, A330_PACK1, A330_PACK2, A330_PRIM1, A330_PRIM2, A330_PRIM3, A330_RAM_AIR, A330_SEC1, A330_SEC2, A330_WING_ANTI_ICE.

The trial at 23:05 (`--only A330_CPT_FD,A330_PACK1`, the same flight): the press by value built after the full run (`SetInputEvent(1)`, a 150 ms hold, `SetInputEvent(0)`) left both reads where they were. Both rows read `dead`: pack 1, one of the 64, and the captain's flight director, one of the 9. An earlier attempt of the same trial crashed inside SimConnect's own message handling before it wrote a row; the retry a minute later ran clean. The cause that fits the crash is the tool's own (a snapshot block shorter than its struct, read past the message's end), fixed since; the tool's README has the stack.

The one model that fits every result: every `SetInputEvent` to these events is a press, one flip, whatever the value. It reads each pair of writes above as two flips: the two sets of 1 on the 64, the two sets of 0 on the 9, the 64's put-back of two sets of 0, and the 23:05 press of a set of 1 then a set of 0, which cancelled. An earlier reading of the 64's put-back, that its first set of 0 changed nothing, overlooked the put-back's rule; the 23:05 trial rules it out. Only the parts above were measured; the rest is the model.

The app and the tool now press these 73 with ONE `SetInputEvent` of 1 and nothing after it, only when the control's read is known and differs from the pick. That press has not met the simulator yet; the sweep's next run presses each once and back, which measures it.

### Dead controls

Ten controls whose read did not move when written. The reasons are guesses; none was measured.

- `A330_ADR1`, `A330_ADR2`, `A330_ADR3`: the ADIRS selectors were off (cold IRS), so the ADR buttons may have had nothing to act on.
- `A330_PRESS_MODE` (pressurization MODE SEL): its read, `INI_CABIN_PRESS_MODE`, never moves. Plan 06 saw the same. A wrong read is the likeliest reason.
- `A330_MASTER_WARN`, `A330_MASTER_CAUT`: no warning or caution was active, so there was nothing to cancel.
- `A330_CPT_COM_VHF2`, `A330_CPT_COM_VHF3`, `A330_CPT_COM_HF1`, `A330_CPT_COM_HF2` (the radio panel's selection keys): the selected-radio read, `INI_SELECTED_RADIO_CPT`, stayed at VHF 1. Either the radio panel's state blocked them or the read is wrong.

Already there (the control was at the position being tried): `A330_CPT_COM_VHF1` and `A330_LDG_ELEV`.

### Read-only positions stay unproven

Every read-only position remains unproven. The tool checks the control's own read and, where a lamp or a simulator variable exists, that one. It cannot yet check a position by a display (an E/WD memo, an ECAM page), which needs the display reader. No position got an independent proof, so predictive windshear is still the only proven one (`tools/a330-gen/unproven.txt`).

### What was not put back afterwards

- Pack 2 (`A330_PACK2`): it started on (`INI_PACK2_BUTTON` 1) and reads off (0). One press by value from the restore (set 1, a hold, set 0: by the model above, two presses) did not bring it back, and it ignored calculator writes too. Switch it on by hand.
- Engine 1 bleed (`A330_ENG1_BLEED`): it started on (`INI_ENGINE1_BLEED` 1) and was read off after the sweep. The restore stopped at pack 2 before reaching it, so the tool has not checked it. One calculator write to it changed nothing visible.
- Flap mode (`A330_GPWS_FLAP`) is not a leftover: the restore's own read shows it at its start (1).

The first restore (21:46) called all 165 controls "at start". That was the tool's defect: it judged a push-button by its input event's own value, which rests at 0. The second restore (22:40, after the fix) stopped at pack 2 with 105 of 165 at start and 59 not tried, engine 1 bleed among them. After switching pack 2 on by hand, run `--restore` again with the 21:29 journal for the rest.

### Measurement traps

- The simulator-link MCP's read of an L:var name can return 0 when it is wrong, not only on the first read. Several apparent flips during this session were that, so a check by that route needs a second read.
- The calculator route (the `_Set` form) toggles on every write. By the 23:05 trial the app's input-event route does too on these 73: every set is a press. Only the app's route is what the app uses; a calculator result is a lead, not the app's behaviour.

### Waiting list

What still has to be done, by the owner's session or someone with access:

1. The names file (plan 04 Task 1 Step 1): `control_names.json`, from someone with access to the private reference. Until then the map's labels are the generator's own.
2. The naming rule to hear (Task 3): the Electrical, ADIRS and Captain EFIS panels three ways (capitals as written; sentence case with the aircraft's abbreviations; digits set apart from letters). The owner rules.
3. The sweep, run again (Task 4 Step 4):
   - the 73 push-buttons that toggle, each pressed and pressed back with the one-set press (it has not met the simulator yet);
   - the denied controls by hand, one at a time, each with the owner's go-ahead (item 9 has the push-buttons among them);
   - the proofs the display reader gives (E/WD memos, ECAM pages);
   - the APU-bleed pass (`--pass apu-bleed`, the APU started by hand first).

   Questions the code review raised, by key:
   - `A330_XPDR_MODE` and `A330_TCAS_MODE`: the transponder and TCAS selectors may be crossed.
   - `A330_EFIS_DMC`: Normal at 0.
   - `A330_CLOCK_RST`: the clock's elapsed-time reset, spring-loaded on Airbus clocks.
   - The fuel pumps: left pump 1 reads OUTER, right pump 1 reads INNER.
   - `A330_PRESS_MODE`.
   - The radio panel's selected radio.
   - `A330_CPT_BARO_UNIT` and `A330_FO_BARO_UNIT`: the read is the control's own event.
   - The parking brake's direction.
   - The landing and nose light position words (`A330_NOSE_LT`).
   - `A330_DOME` and the wipers (`A330_CPT_WIPER`, `A330_FO_WIPER`).
   - Every guarded control with its guard closed: does the aircraft refuse it?
4. The ADIRS selectors (measured 2026-11-19: input 1 on each put all three in NAV): the sweep denied them. Move them by hand, then run the ADR buttons again.
5. Put back by hand: pack 2 on, then `--restore` again for the rest (engine 1 bleed among them).
6. The order capture (Task 6 Step 1): the simulator's own procedures in order, with the private reference, to make `procedure_order.json`.
7. Measurement items the code relies on:
   - Each control's settle time: a pick is read back after a fixed 1.5 s until then.
   - The weather radar tilt scale: its read `B:AIRLINER_WX_TILT` and its input share the 0 to 30 scale; whether the label at 0 says "0 degrees" or "Level".
   - The master warning and caution variable names: `INI_MASTER_WARNING_ACTIVE` and `INI_MASTER_CAUTION_ACTIVE` are guesses.
   - The speedbrake lever's travel time: the 3 s quiet is unmeasured.
   - That NVDA reads a focused combo's snap-back once when the aircraft ignored a pick.
   - That the transmit keys (PA, CAB, INT) select audio and ring no call.
   - The temperature and landing elevation ranges: the sliders say 0 to 100 until measured.
8. Owner decision on the deny list: whether to deny FADEC ground power, the generators and crew oxygen (the sweep drove `A330_FADEC1`, `A330_FADEC2`, `A330_GEN1`, `A330_GEN2` and `A330_CREW_OXY`), and the autobrake.
9. The denied push-buttons of the same build. They keep `mode: set` (a pick sets the position's input by value), but every push-button of this shape the sweep reached turned out to be a toggle, and the First Officer switches the batteries and external power. The keys and the L:var each reads:
   - `A330_BAT1` (BAT 1): `INI_OVHD_ELEC_BAT_1_PB_IS_AUTO`.
   - `A330_BAT2` (BAT 2): `INI_OVHD_ELEC_BAT_2_PB_IS_AUTO`.
   - `A330_APU_BAT` (APU BAT): `INI_OVHD_ELEC_BAT_3_PB_IS_AUTO`.
   - `A330_EXT_A` (EXT A): `INI_GEN_EXT_A_ONLINE`.
   - `A330_EXT_B` (EXT B): `INI_GEN_EXT_B_ONLINE`.
   - `A330_IR1`, `A330_IR2`, `A330_IR3` (IR 1 to 3): `INI_IR1_STATE`, `INI_IR2_STATE`, `INI_IR3_STATE`. Switching an IR off loses its alignment.
   - `A330_APU_MASTER` (the APU master): `INI_APU_MASTER_SWITCH`.

   The check, for each one, with the owner's go-ahead:
   - Press it once through the app's route: one pick of its other position in its panel row.
   - Read its L:var, then again after a pause of about 2 s.
   - Press it back the same way, and read the L:var twice again, with the same pause.
   - Each press should leave the L:var at the picked position on both reads; one that does not is a finding.
   - A press each way cannot tell a toggle from a set by value: only writing the same value twice can, as the sweep did on the 73. That is a further step, with its own go-ahead. A key found toggling joins the 73 (`mode: toggle`, `press: 1`).
