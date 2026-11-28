# Plan 04 evidence: the A330's input-event catalogue and the panel gap

## Capture, 2026-11-27

Simulator: MSFS 2024 1.9.24.0. Aircraft: iniBuilds A330-200 VIP (GE). 649 input events, captured from the app's own catalogue log (`tools/a330-gen/data/input_events.tsv`).

The aircraft's own names for the controls (`control_names.json`) have not been exported yet, so the tables have no title column: every event would read "no name". Where "In today's map" says a key, the control is in the map; "left out" is followed by the reason, in short; "not offered" means no control and no left-out entry (plan 04 Task 5 works through these).

Event names are written as the simulator lists them, including its spellings (GAURD).

## Part 1: every event, by group

The group is the word after `AIRLINER_` (the first word of the name for the few events without that prefix). Groups with a single event are in one table, last.

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
| AIRLINER_APU_EXTING_RESET | not offered |
| AIRLINER_APU_FIRE_HANDLE | A330_APU_FIRE |
| AIRLINER_APU_FIRE_HANDLE_COVER | A330_APU_FIRE_COVER |
| AIRLINER_APU_MASTER | A330_APU_MASTER |
| AIRLINER_APU_START | A330_APU_START |

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
| AIRLINER_CARGO_AIR_TEMP_MODE | not offered |
| AIRLINER_CARGO_SMOKE_TEST | A330_CARGO_SMOKE_TEST |
| AIRLINER_CARGO_TEMP_PRESEL | not offered |

### CLOCK (5 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_CLOCK_CHR | A330_CLOCK_CHR |
| AIRLINER_CLOCK_GPS | not offered |
| AIRLINER_CLOCK_RST | A330_CLOCK_RST |
| AIRLINER_CLOCK_RUN | A330_CLOCK_ET |
| AIRLINER_CLOCK_SET | not offered |

### CMC1 (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_CMC1 | not offered |
| AIRLINER_CMC1_COVER | not offered |

### COCKPIT (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_COCKPIT_DOOR_LOCK | not offered |
| AIRLINER_COCKPIT_TEMP | A330_CKPT_TEMP |

### COM3 (59 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_COM3_ADF1_VOL | not offered |
| AIRLINER_COM3_ADF1_VOL_BUTTON | not offered |
| AIRLINER_COM3_ADF2_VOL | not offered |
| AIRLINER_COM3_ADF2_VOL_BUTTON | not offered |
| AIRLINER_COM3_BTN_CAB | not offered |
| AIRLINER_COM3_BTN_HF1 | not offered |
| AIRLINER_COM3_BTN_HF2 | not offered |
| AIRLINER_COM3_BTN_INT | not offered |
| AIRLINER_COM3_BTN_PA | not offered |
| AIRLINER_COM3_BTN_SAT1 | not offered |
| AIRLINER_COM3_BTN_SAT2 | not offered |
| AIRLINER_COM3_BTN_TRANSFER | not offered |
| AIRLINER_COM3_BTN_VHF1 | not offered |
| AIRLINER_COM3_BTN_VHF2 | not offered |
| AIRLINER_COM3_BTN_VHF3 | not offered |
| AIRLINER_COM3_CAB_VOL | not offered |
| AIRLINER_COM3_CAB_VOL_BUTTON | not offered |
| AIRLINER_COM3_COMM_KHZ | not offered |
| AIRLINER_COM3_COMM_MHZ | not offered |
| AIRLINER_COM3_COM_ADF | not offered |
| AIRLINER_COM3_COM_AM | not offered |
| AIRLINER_COM3_COM_BFO | not offered |
| AIRLINER_COM3_COM_HF1 | not offered |
| AIRLINER_COM3_COM_HF2 | not offered |
| AIRLINER_COM3_COM_LS | not offered |
| AIRLINER_COM3_COM_NAV | not offered |
| AIRLINER_COM3_COM_NAV_COVER | not offered |
| AIRLINER_COM3_COM_VHF1 | not offered |
| AIRLINER_COM3_COM_VHF2 | not offered |
| AIRLINER_COM3_COM_VHF3 | not offered |
| AIRLINER_COM3_COM_VOR | not offered |
| AIRLINER_COM3_HF1_VOL | not offered |
| AIRLINER_COM3_HF1_VOL_BUTTON | not offered |
| AIRLINER_COM3_HF2_VOL | not offered |
| AIRLINER_COM3_HF2_VOL_BUTTON | not offered |
| AIRLINER_COM3_INTRAD | not offered |
| AIRLINER_COM3_INT_VOL | not offered |
| AIRLINER_COM3_INT_VOL_BUTTON | not offered |
| AIRLINER_COM3_LS_VOL | not offered |
| AIRLINER_COM3_LS_VOL_BUTTON | not offered |
| AIRLINER_COM3_MKR_VOL | not offered |
| AIRLINER_COM3_MKR_VOL_BUTTON | not offered |
| AIRLINER_COM3_PA_VOL | not offered |
| AIRLINER_COM3_PA_VOL_BUTTON | not offered |
| AIRLINER_COM3_SAT1_VOL | not offered |
| AIRLINER_COM3_SAT1_VOL_BUTTON | not offered |
| AIRLINER_COM3_SAT2_VOL | not offered |
| AIRLINER_COM3_SAT2_VOL_BUTTON | not offered |
| AIRLINER_COM3_VHF1_VOL | not offered |
| AIRLINER_COM3_VHF1_VOL_BUTTON | not offered |
| AIRLINER_COM3_VHF2_VOL | not offered |
| AIRLINER_COM3_VHF2_VOL_BUTTON | not offered |
| AIRLINER_COM3_VHF3_VOL | not offered |
| AIRLINER_COM3_VHF3_VOL_BUTTON | not offered |
| AIRLINER_COM3_VHF_ONOFF | not offered |
| AIRLINER_COM3_VOR1_VOL | not offered |
| AIRLINER_COM3_VOR1_VOL_BUTTON | not offered |
| AIRLINER_COM3_VOR2_VOL | not offered |
| AIRLINER_COM3_VOR2_VOL_BUTTON | not offered |

### CPT (103 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_CPT_ADF1_VOL | not offered |
| AIRLINER_CPT_ADF1_VOL_BUTTON | not offered |
| AIRLINER_CPT_ADF2_VOL | not offered |
| AIRLINER_CPT_ADF2_VOL_BUTTON | not offered |
| AIRLINER_CPT_AIRVENT_SLIDER | not offered |
| AIRLINER_CPT_ATC_MSG | A330_ATC_MSG |
| AIRLINER_CPT_AUTOLAND | not offered |
| AIRLINER_CPT_BARO_SET | not offered |
| AIRLINER_CPT_BARO_SET_PULL | left out: pressed by baro knob (the captain's knob to STD) |
| AIRLINER_CPT_BARO_SET_PUSH | left out: pressed by baro knob (the captain's knob to QNH) |
| AIRLINER_CPT_BARO_TOGGLE | A330_CPT_BARO_UNIT |
| AIRLINER_CPT_BLIND_REAR_CLIP | not offered |
| AIRLINER_CPT_BTN_CAB | A330_CPT_BTN_CAB |
| AIRLINER_CPT_BTN_HF1 | A330_CPT_BTN_HF1 |
| AIRLINER_CPT_BTN_HF2 | A330_CPT_BTN_HF2 |
| AIRLINER_CPT_BTN_INT | A330_CPT_BTN_INT |
| AIRLINER_CPT_BTN_PA | A330_CPT_BTN_PA |
| AIRLINER_CPT_BTN_SAT1 | not offered |
| AIRLINER_CPT_BTN_SAT2 | not offered |
| AIRLINER_CPT_BTN_TRANSFER | A330_CPT_BTN_TRANSFER |
| AIRLINER_CPT_BTN_VHF1 | A330_CPT_BTN_VHF1 |
| AIRLINER_CPT_BTN_VHF2 | A330_CPT_BTN_VHF2 |
| AIRLINER_CPT_BTN_VHF3 | A330_CPT_BTN_VHF3 |
| AIRLINER_CPT_CAB_VOL | not offered |
| AIRLINER_CPT_CAB_VOL_BUTTON | not offered |
| AIRLINER_CPT_CHRONO | A330_CHRONO |
| AIRLINER_CPT_COMM_KHZ | not offered |
| AIRLINER_CPT_COMM_MHZ | not offered |
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
| AIRLINER_CPT_DCDU_BRT | not offered |
| AIRLINER_CPT_EFB_BRT | not offered |
| AIRLINER_CPT_EFB_HOME | not offered |
| AIRLINER_CPT_EFIS_DMC_NORM | A330_EFIS_DMC |
| AIRLINER_CPT_FD | A330_CPT_FD |
| AIRLINER_CPT_FOOTREST_CLICK | not offered |
| AIRLINER_CPT_FOOT_WARMER_TOGGLE | not offered |
| AIRLINER_CPT_FRONTBLIND_CLICK | not offered |
| AIRLINER_CPT_HF1_VOL | not offered |
| AIRLINER_CPT_HF1_VOL_BUTTON | not offered |
| AIRLINER_CPT_HF2_VOL | not offered |
| AIRLINER_CPT_HF2_VOL_BUTTON | not offered |
| AIRLINER_CPT_INTRAD | not offered |
| AIRLINER_CPT_INT_VOL | not offered |
| AIRLINER_CPT_INT_VOL_BUTTON | not offered |
| AIRLINER_CPT_JOY_DISCO | not offered |
| AIRLINER_CPT_LEFT_ARMREST | not offered |
| AIRLINER_CPT_LOUDSPEAKER | not offered |
| AIRLINER_CPT_LS | A330_CPT_LS |
| AIRLINER_CPT_LS_VOL | not offered |
| AIRLINER_CPT_LS_VOL_BUTTON | not offered |
| AIRLINER_CPT_LT_TOGGLE | not offered |
| AIRLINER_CPT_MAP_TOGGLE | not offered |
| AIRLINER_CPT_MASTER_CAUT | A330_MASTER_CAUT |
| AIRLINER_CPT_MASTER_WARN | A330_MASTER_WARN |
| AIRLINER_CPT_MCDU_BRT | A330_CPT_MCDU_BRT |
| AIRLINER_CPT_MKR_VOL | not offered |
| AIRLINER_CPT_MKR_VOL_BUTTON | not offered |
| AIRLINER_CPT_ND_BRT | A330_CPT_ND_BRT |
| AIRLINER_CPT_OXY_COVER | not offered |
| AIRLINER_CPT_OXY_TEST | not offered |
| AIRLINER_CPT_PA_VOL | not offered |
| AIRLINER_CPT_PA_VOL_BUTTON | not offered |
| AIRLINER_CPT_PFD_BRT | A330_CPT_PFD_BRT |
| AIRLINER_CPT_PFD_ND | A330_CPT_PFD_ND_XFR |
| AIRLINER_CPT_RAIN_RPL | A330_CPT_RAIN_RPL |
| AIRLINER_CPT_READING_LT_KNOB | not offered |
| AIRLINER_CPT_REARBLIND_CLICK | not offered |
| AIRLINER_CPT_RIGHT_ARMREST | not offered |
| AIRLINER_CPT_SAT1_VOL | not offered |
| AIRLINER_CPT_SAT1_VOL_BUTTON | not offered |
| AIRLINER_CPT_SAT2_VOL | not offered |
| AIRLINER_CPT_SAT2_VOL_BUTTON | not offered |
| AIRLINER_CPT_TERR_ON_ND | A330_CPT_TERR_ON_ND |
| AIRLINER_CPT_TER_BRT | not offered |
| AIRLINER_CPT_TILLER_DISCO | not offered |
| AIRLINER_CPT_TRAY_CLICK | not offered |
| AIRLINER_CPT_VHF1_VOL | not offered |
| AIRLINER_CPT_VHF1_VOL_BUTTON | not offered |
| AIRLINER_CPT_VHF2_VOL | not offered |
| AIRLINER_CPT_VHF2_VOL_BUTTON | not offered |
| AIRLINER_CPT_VHF3_VOL | not offered |
| AIRLINER_CPT_VHF3_VOL_BUTTON | not offered |
| AIRLINER_CPT_VHF_ONOFF | A330_RMP1_POWER |
| AIRLINER_CPT_VISOR_CLICK | not offered |
| AIRLINER_CPT_VOR1_VOL | not offered |
| AIRLINER_CPT_VOR1_VOL_BUTTON | not offered |
| AIRLINER_CPT_VOR2_VOL | not offered |
| AIRLINER_CPT_VOR2_VOL_BUTTON | not offered |
| AIRLINER_CPT_WINDOW_HANDLE | not offered |
| AIRLINER_CPT_WINDOW_PIN | not offered |
| AIRLINER_CPT_WIPER_SWITCH | A330_CPT_WIPER |

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
| AIRLINER_EMER_CALL | not offered |
| AIRLINER_EMER_CALL_COVER | not offered |
| AIRLINER_EMER_EXIT_TOGGLE | A330_EMER_EXIT_LT |
| AIRLINER_EMER_GEN_TEST_BUTTON | not offered |
| AIRLINER_EMER_GEN_TEST_GAURD | not offered |

### ENG1 (8 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_ENG1_AGENT1 | A330_ENG1_AGENT1 |
| AIRLINER_ENG1_AGENT2 | A330_ENG1_AGENT2 |
| AIRLINER_ENG1_ATDISCO | not offered |
| AIRLINER_ENG1_BLEED | A330_ENG1_BLEED |
| AIRLINER_ENG1_CUTOFF | A330_ENG1_MASTER |
| AIRLINER_ENG1_FIRE_HANDLE | A330_ENG1_FIRE |
| AIRLINER_ENG1_FIRE_HANDLE_COVER | A330_ENG1_FIRE_COVER |
| AIRLINER_ENG1_ICE | A330_ENG1_ANTI_ICE |

### ENG2 (8 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_ENG2_AGENT1 | A330_ENG2_AGENT1 |
| AIRLINER_ENG2_AGENT2 | A330_ENG2_AGENT2 |
| AIRLINER_ENG2_ATDISCO | not offered |
| AIRLINER_ENG2_BLEED | A330_ENG2_BLEED |
| AIRLINER_ENG2_CUTOFF | A330_ENG2_MASTER |
| AIRLINER_ENG2_FIRE_HANDLE | A330_ENG2_FIRE |
| AIRLINER_ENG2_FIRE_HANDLE_COVER | A330_ENG2_FIRE_COVER |
| AIRLINER_ENG2_ICE | A330_ENG2_ANTI_ICE |

### ENGINE (3 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_ENGINE_FIRE_TEST | A330_ENG_FIRE_TEST |
| ENGINE_THROTTLE_1 | not offered |
| ENGINE_THROTTLE_2 | not offered |

### EVAC (4 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_EVAC_COMMAND | not offered |
| AIRLINER_EVAC_COMMAND_COVER | not offered |
| AIRLINER_EVAC_CPT_TOGGLE | not offered |
| AIRLINER_EVAC_HORN | not offered |

### FADEC (4 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_FADEC_ENG1 | A330_FADEC1 |
| AIRLINER_FADEC_ENG1_COVER | A330_FADEC1_COVER |
| AIRLINER_FADEC_ENG2 | A330_FADEC2 |
| AIRLINER_FADEC_ENG2_COVER | A330_FADEC2_COVER |

### FLOOD (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_FLOOD_LT_PED_KNOB | not offered |
| AIRLINER_FLOOD_MAIN_PNL_KNOB | not offered |

### FLTCTL (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_FLTCTL_PRIM1 | A330_PRIM1 |
| AIRLINER_FLTCTL_SEC1 | A330_SEC1 |

### FO (103 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_FO_ADF1_VOL | not offered |
| AIRLINER_FO_ADF1_VOL_BUTTON | not offered |
| AIRLINER_FO_ADF2_VOL | not offered |
| AIRLINER_FO_ADF2_VOL_BUTTON | not offered |
| AIRLINER_FO_AIRVENT_SLIDER | not offered |
| AIRLINER_FO_ATC_MSG | not offered |
| AIRLINER_FO_AUTOLAND | not offered |
| AIRLINER_FO_BARO_SET | not offered |
| AIRLINER_FO_BARO_SET_PULL | left out: pressed by baro knob (the first officer's knob to STD) |
| AIRLINER_FO_BARO_SET_PUSH | left out: pressed by baro knob (the first officer's knob to QNH) |
| AIRLINER_FO_BARO_TOGGLE | A330_FO_BARO_UNIT |
| AIRLINER_FO_BLIND_REAR_CLIP | not offered |
| AIRLINER_FO_BTN_CAB | not offered |
| AIRLINER_FO_BTN_HF1 | not offered |
| AIRLINER_FO_BTN_HF2 | not offered |
| AIRLINER_FO_BTN_INT | not offered |
| AIRLINER_FO_BTN_PA | not offered |
| AIRLINER_FO_BTN_SAT1 | not offered |
| AIRLINER_FO_BTN_SAT2 | not offered |
| AIRLINER_FO_BTN_TRANSFER | not offered |
| AIRLINER_FO_BTN_VHF1 | not offered |
| AIRLINER_FO_BTN_VHF2 | not offered |
| AIRLINER_FO_BTN_VHF3 | not offered |
| AIRLINER_FO_CAB_VOL | not offered |
| AIRLINER_FO_CAB_VOL_BUTTON | not offered |
| AIRLINER_FO_CHRONO | not offered |
| AIRLINER_FO_COMM_KHZ | not offered |
| AIRLINER_FO_COMM_MHZ | not offered |
| AIRLINER_FO_COM_ADF | not offered |
| AIRLINER_FO_COM_AM | not offered |
| AIRLINER_FO_COM_BFO | not offered |
| AIRLINER_FO_COM_HF1 | not offered |
| AIRLINER_FO_COM_HF2 | not offered |
| AIRLINER_FO_COM_LS | not offered |
| AIRLINER_FO_COM_NAV | not offered |
| AIRLINER_FO_COM_NAV_COVER | not offered |
| AIRLINER_FO_COM_VHF1 | not offered |
| AIRLINER_FO_COM_VHF2 | not offered |
| AIRLINER_FO_COM_VHF3 | not offered |
| AIRLINER_FO_COM_VOR | not offered |
| AIRLINER_FO_CONSOLE_TOGGLE | A330_FO_CONSOLE_LT |
| AIRLINER_FO_DCDU_BRT | not offered |
| AIRLINER_FO_EFB_BRT | not offered |
| AIRLINER_FO_EFB_HOME | not offered |
| AIRLINER_FO_EFIS_DMC_NORM | not offered |
| AIRLINER_FO_FD | A330_FO_FD |
| AIRLINER_FO_FOOTREST_CLICK | not offered |
| AIRLINER_FO_FOOT_WARMER_TOGGLE | not offered |
| AIRLINER_FO_FRONTBLIND_CLICK | not offered |
| AIRLINER_FO_HF1_VOL | not offered |
| AIRLINER_FO_HF1_VOL_BUTTON | not offered |
| AIRLINER_FO_HF2_VOL | not offered |
| AIRLINER_FO_HF2_VOL_BUTTON | not offered |
| AIRLINER_FO_INTRAD | not offered |
| AIRLINER_FO_INT_VOL | not offered |
| AIRLINER_FO_INT_VOL_BUTTON | not offered |
| AIRLINER_FO_JOY_DISCO | not offered |
| AIRLINER_FO_LEFT_ARMREST | not offered |
| AIRLINER_FO_LOUDSPEAKER | not offered |
| AIRLINER_FO_LS | A330_FO_LS |
| AIRLINER_FO_LS_VOL | not offered |
| AIRLINER_FO_LS_VOL_BUTTON | not offered |
| AIRLINER_FO_LT_CEILING_TOGGLE | not offered |
| AIRLINER_FO_MAP_TOGGLE | not offered |
| AIRLINER_FO_MASTER_CAUT | not offered |
| AIRLINER_FO_MASTER_WARN | not offered |
| AIRLINER_FO_MCDU_BRT | A330_FO_MCDU_BRT |
| AIRLINER_FO_MKR_VOL | not offered |
| AIRLINER_FO_MKR_VOL_BUTTON | not offered |
| AIRLINER_FO_ND_BRT | A330_FO_ND_BRT |
| AIRLINER_FO_OXY_COVER | not offered |
| AIRLINER_FO_OXY_TEST | not offered |
| AIRLINER_FO_PA_VOL | not offered |
| AIRLINER_FO_PA_VOL_BUTTON | not offered |
| AIRLINER_FO_PFD_BRT | A330_FO_PFD_BRT |
| AIRLINER_FO_PFD_ND | A330_FO_PFD_ND_XFR |
| AIRLINER_FO_RAIN_RPL | not offered |
| AIRLINER_FO_READING_LT_KNOB | not offered |
| AIRLINER_FO_REARBLIND_CLICK | not offered |
| AIRLINER_FO_RIGHT_ARMREST | not offered |
| AIRLINER_FO_SAT1_VOL | not offered |
| AIRLINER_FO_SAT1_VOL_BUTTON | not offered |
| AIRLINER_FO_SAT2_VOL | not offered |
| AIRLINER_FO_SAT2_VOL_BUTTON | not offered |
| AIRLINER_FO_TERR_ON_ND | A330_FO_TERR_ON_ND |
| AIRLINER_FO_TER_BRT | not offered |
| AIRLINER_FO_TILLER_DISCO | not offered |
| AIRLINER_FO_TRAY_CLICK | not offered |
| AIRLINER_FO_VHF1_VOL | not offered |
| AIRLINER_FO_VHF1_VOL_BUTTON | not offered |
| AIRLINER_FO_VHF2_VOL | not offered |
| AIRLINER_FO_VHF2_VOL_BUTTON | not offered |
| AIRLINER_FO_VHF3_VOL | not offered |
| AIRLINER_FO_VHF3_VOL_BUTTON | not offered |
| AIRLINER_FO_VHF_ONOFF | not offered |
| AIRLINER_FO_VISOR_CLICK | not offered |
| AIRLINER_FO_VOR1_VOL | not offered |
| AIRLINER_FO_VOR1_VOL_BUTTON | not offered |
| AIRLINER_FO_VOR2_VOL | not offered |
| AIRLINER_FO_VOR2_VOL_BUTTON | not offered |
| AIRLINER_FO_WINDOW_HANDLE | not offered |
| AIRLINER_FO_WINDOW_PIN | not offered |
| AIRLINER_FO_WIPER_SWITCH | A330_FO_WIPER |

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
| AIRLINER_FUEL_FEED_TOGGLE | not offered |
| AIRLINER_FUEL_FEED_TOGGLE_GAURD | not offered |
| AIRLINER_FUEL_MODE | A330_FUEL_TRIM_MODE |
| AIRLINER_FUEL_TK_XFR | A330_FUEL_TK_XFR |
| AIRLINER_FUEL_TK_XFR_COVER | A330_FUEL_TK_XFR_COVER |
| AIRLINER_FUEL_XFEED | A330_FUEL_XFEED |

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
| AIRLINER_GND_HF_DATA | not offered |

### GPWS (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_GPWS_SYS | A330_GPWS_SYS |
| AIRLINER_GPWS_TERR | A330_GPWS_TERR |

### GRAVITY (4 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_GRAVITY_GEAR_LEFT_GAURD | not offered |
| AIRLINER_GRAVITY_GEAR_OUTER_GAURD | not offered |
| AIRLINER_GRAVITY_GEAR_RIGHT_GAURD | not offered |
| AIRLINER_GRAVITY_GEAR_TOGGLE | not offered |

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
| AIRLINER_ISIS_BARO_INNER | not offered |
| AIRLINER_ISIS_BARO_OUTER | not offered |
| AIRLINER_ISIS_BUGS | A330_ISIS_BUGS |
| AIRLINER_ISIS_LS | A330_ISIS_LS |
| AIRLINER_ISIS_MINUS | not offered |
| AIRLINER_ISIS_PLUS | not offered |
| AIRLINER_ISIS_RST | A330_ISIS_RST |

### LDG (3 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_LDG_ELEV | A330_LDG_ELEV |
| AIRLINER_LDG_SYS_TOGGLE | not offered |
| AIRLINER_LDG_SYS_TOGGLE_GAURD | not offered |

### MAN (6 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_MAN_ON | not offered |
| AIRLINER_MAN_ON_COVER | not offered |
| AIRLINER_MAN_START_1 | A330_MAN_START1 |
| AIRLINER_MAN_START_1_COVER | A330_MAN_START1_COVER |
| AIRLINER_MAN_START_2 | A330_MAN_START2 |
| AIRLINER_MAN_START_2_COVER | A330_MAN_START2_COVER |

### MASK (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_MASK_MAN_BUTTON | not offered |
| AIRLINER_MASK_MAN_GAURD | not offered |

### MCU (15 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_MCU_ALT | not offered |
| AIRLINER_MCU_ALTSELECT | left out: set by FCU window (altitude knob step, 100 or 1000 feet) |
| AIRLINER_MCU_ALT_PULL | not offered |
| AIRLINER_MCU_ALT_PUSH | not offered |
| AIRLINER_MCU_BRT_KNOB | not offered |
| AIRLINER_MCU_HDG | not offered |
| AIRLINER_MCU_HDG_PULL | not offered |
| AIRLINER_MCU_HDG_PUSH | not offered |
| AIRLINER_MCU_INT_KNOB | not offered |
| AIRLINER_MCU_SPEED | not offered |
| AIRLINER_MCU_SPEED_PULL | not offered |
| AIRLINER_MCU_SPEED_PUSH | not offered |
| AIRLINER_MCU_VS | not offered |
| AIRLINER_MCU_VS_PULL | left out: pressed by FCU window (V/S knob pull) |
| AIRLINER_MCU_VS_PUSH | left out: pressed by FCU window (V/S knob push, with its own push action) |

### N1 (4 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_N1_MODE_1 | not offered |
| AIRLINER_N1_MODE_1_COVER | not offered |
| AIRLINER_N1_MODE_2 | not offered |
| AIRLINER_N1_MODE_2_COVER | not offered |

### NWS (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_NWS_BUTTON | not offered |
| AIRLINER_NWS_BUTTON_COVER | not offered |

### OXY (6 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_OXY_TEST_BLACK | not offered |
| AIRLINER_OXY_TEST_BLACK_CPT | not offered |
| AIRLINER_OXY_TEST_BLACK_FO | not offered |
| AIRLINER_OXY_TEST_RED | not offered |
| AIRLINER_OXY_TEST_RED_CPT | not offered |
| AIRLINER_OXY_TEST_RED_FO | not offered |

### PRINTER (3 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_PRINTER_OFF | not offered |
| AIRLINER_PRINTER_SLEW | not offered |
| AIRLINER_PRINTER_TEST | not offered |

### RAM (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_RAM_AIR | A330_RAM_AIR |
| AIRLINER_RAM_AIR_COVER | A330_RAM_AIR_COVER |

### SEAT (4 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_SEAT_ARMREST_HEIGHT_CAPT | not offered |
| AIRLINER_SEAT_ARMREST_HEIGHT_FO | not offered |
| AIRLINER_SEAT_ARMREST_PITCH_CAPT | not offered |
| AIRLINER_SEAT_ARMREST_PITCH_FO | not offered |

### TCAS (15 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_TCAS_1 | not offered |
| AIRLINER_TCAS_2 | not offered |
| AIRLINER_TCAS_3 | not offered |
| AIRLINER_TCAS_4 | not offered |
| AIRLINER_TCAS_5 | not offered |
| AIRLINER_TCAS_6 | not offered |
| AIRLINER_TCAS_7 | not offered |
| AIRLINER_TCAS_ABV | A330_TCAS_TRAFFIC |
| AIRLINER_TCAS_ALT | A330_XPDR_ALT_RPTG |
| AIRLINER_TCAS_ATC | A330_XPDR_SYSTEM |
| AIRLINER_TCAS_CLR | not offered |
| AIRLINER_TCAS_IDENT | A330_XPDR_IDENT |
| AIRLINER_TCAS_MODE | A330_XPDR_MODE |
| AIRLINER_TCAS_STBY | A330_TCAS_MODE |
| AIRLINER_TCAS_ZERO | not offered |

### VALVE (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_VALVE_SEL | not offered |
| AIRLINER_VALVE_SEL_GAURD | not offered |

### VENT (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_VENT_CAB | A330_CAB_FANS |
| AIRLINER_VENT_EXTRACT | A330_EXTRACT |

### VOR (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_VOR_TOGGLE1 | not offered |
| AIRLINER_VOR_TOGGLE2 | not offered |

### WING (2 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_WING_ICE | A330_WING_ANTI_ICE |
| AIRLINER_WING_TOGGLE | A330_WING_LT |

### WX (6 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_WX_CTRL | not offered |
| AIRLINER_WX_GAIN | A330_WX_GAIN |
| AIRLINER_WX_MODE | A330_WX_MODE |
| AIRLINER_WX_PWS | A330_WX_PWS |
| AIRLINER_WX_SYS | A330_WX_SYS |
| AIRLINER_WX_TILT | A330_WX_TILT |

### DOOR (5 events)

| Event | In today's map |
| --- | --- |
| DOOR_LOCK_BACK | not offered |
| DOOR_LOCK_BULK | not offered |
| DOOR_LOCK_FRONT | not offered |
| DOOR_LOCK_LEFT | not offered |
| DOOR_LOCK_RIGHT | not offered |

### UNKNOWN (30 events)

| Event | In today's map |
| --- | --- |
| UNKNOWN_A330_GEAR_FRONT_MAIN | not offered |
| UNKNOWN_A330_GEAR_MAIN_STRUT_LEFT_1 | not offered |
| UNKNOWN_A330_GEAR_MAIN_STRUT_RIGHT_1 | not offered |
| UNKNOWN_A330_GEAR_M_TIRE_LF | not offered |
| UNKNOWN_A330_GEAR_M_TIRE_LR | not offered |
| UNKNOWN_A330_GEAR_M_TIRE_RF | not offered |
| UNKNOWN_A330_GEAR_M_TIRE_RR | not offered |
| UNKNOWN_A330_GEAR_SHOCKABSORBER_FRONT | not offered |
| UNKNOWN_A330_GEAR_SHOCKABSORBER_ROD_LEFT_1 | not offered |
| UNKNOWN_A330_GEAR_SHOCKABSORBER_ROD_RIGHT_1 | not offered |
| UNKNOWN_A330_WING_INNER_FLAP_LEFT | not offered |
| UNKNOWN_A330_WING_INNER_FLAP_RIGHT | not offered |
| UNKNOWN_CHOCKS | not offered |
| UNKNOWN_COVER_LEFT | not offered |
| UNKNOWN_COVER_RIGHT | not offered |
| UNKNOWN_DOOR1L | not offered |
| UNKNOWN_DOOR1R | not offered |
| UNKNOWN_DOOR2L | not offered |
| UNKNOWN_DOOR2R | not offered |
| UNKNOWN_PITOT_COVERS | not offered |
| UNKNOWN_SKEL_ELEVATOR_LEFT | not offered |
| UNKNOWN_SKEL_ELEVATOR_RIGHT | not offered |
| UNKNOWN_SKEL_RUDDER | not offered |
| UNKNOWN_TIRE_NOSE_STILL | not offered |
| UNKNOWN_WING_LEFT_CHECKBOX_AILERON_INNER | not offered |
| UNKNOWN_WING_LEFT_CHECKBOX_AILERON_OUTER | not offered |
| UNKNOWN_WING_LEFT_CHECKBOX_INNER | not offered |
| UNKNOWN_WING_RIGHT_CHECKBOX_AILERON_INNER | not offered |
| UNKNOWN_WING_RIGHT_CHECKBOX_AILERON_OUTER | not offered |
| UNKNOWN_WING_RIGHT_CHECKBOX_INNER | not offered |

### Groups with one event (76 events)

| Event | In today's map |
| --- | --- |
| AIRLINER_ACMS_PRINT | not offered |
| AIRLINER_ADIR1 | A330_ADR1 |
| AIRLINER_ADIR2 | A330_ADR2 |
| AIRLINER_ADIR3 | A330_ADR3 |
| AIRLINER_ADIRS1_MODE | A330_IRS1_MODE |
| AIRLINER_ADIRS2_MODE | A330_IRS2_MODE |
| AIRLINER_ADIRS3_MODE | A330_IRS3_MODE |
| AIRLINER_AIRCOND_RESET | not offered |
| AIRLINER_ALT_PUSH | left out: pressed by FCU window (ALT hold) |
| AIRLINER_ANNLT_TOGGLE | A330_ANN_LT |
| AIRLINER_ANTISKD | A330_ANTISKID |
| AIRLINER_AP1_PUSH | not offered |
| AIRLINER_AP2_PUSH | not offered |
| AIRLINER_APPR_PUSH | not offered |
| AIRLINER_ATHR_PUSH | left out: pressed by FCU window (A/THR) |
| AIRLINER_ATT_HDG_SELECT | A330_ATT_HDG |
| AIRLINER_AUDIO_SELECT | A330_AUDIO_SW |
| AIRLINER_BEACON_TOGGLE | A330_BEACON |
| AIRLINER_BRK_FAN | A330_BRAKE_FAN |
| AIRLINER_BULK_ISO_VALVE | A330_BULK_ISO |
| AIRLINER_CABIN_TEMP | A330_CABIN_TEMP |
| AIRLINER_CAB_REST | not offered |
| AIRLINER_CKPT_DOOR_VIDEO | not offered |
| AIRLINER_COCKPITDOOR | not offered |
| AIRLINER_CTL_TOGGLE | A330_DOME |
| AIRLINER_CTR_VISOR_BASE | not offered |
| AIRLINER_DFDR_EVENT | A330_DFDR_EVENT |
| AIRLINER_ELEVATORTRIM | not offered |
| AIRLINER_ENG_START | A330_ENG_MODE |
| AIRLINER_FLAPS | left out: the Flaps and Speedbrake panel's flap row drives the lever through K:FLAPS_SET (IniA330Definition.Panels.cs, HandlePanelSet) |
| AIRLINER_FLAP_MODE | A330_GPWS_FLAP |
| AIRLINER_FLT_RESET | not offered |
| AIRLINER_FM_SELECT | A330_FM_SW |
| AIRLINER_GEAR_LEVER | left out: the Landing Gear panel's gear row drives the lever through K:GEAR_DOWN / K:GEAR_UP (IniA330Definition.Panels.cs, HandlePanelSet) |
| AIRLINER_GS_MODE | A330_GPWS_GS |
| AIRLINER_HDVS | not offered |
| AIRLINER_IFEC | not offered |
| AIRLINER_INTEG_LT_KNOB | not offered |
| AIRLINER_IR1 | A330_IR1 |
| AIRLINER_IR2 | A330_IR2 |
| AIRLINER_IR3 | A330_IR3 |
| AIRLINER_LAND_RECOVERY | A330_LAND_RECOVERY |
| AIRLINER_LDGLIGHT_TOGGLE | A330_LANDING_LT |
| AIRLINER_LOC_PUSH | not offered |
| AIRLINER_MECH_CALL | A330_MECH_CALL |
| AIRLINER_METRIC | left out: set by FCU window (metric altitude toggle) |
| AIRLINER_MODE_SEL | A330_PRESS_MODE |
| AIRLINER_NAVLOGO_TOGGLE | A330_NAV_LOGO |
| AIRLINER_NORTH_REF | not offered |
| AIRLINER_NOSMOKING_TOGGLE | A330_NO_SMOKING |
| AIRLINER_OXYGEN_CREW | A330_CREW_OXY |
| AIRLINER_PACK1 | A330_PACK1 |
| AIRLINER_PACK2 | A330_PACK2 |
| AIRLINER_PACK_FLOW | A330_PACK_FLOW |
| AIRLINER_PAPER_ALARM | not offered |
| AIRLINER_PARKINGBRAKE | left out: set by parking brake control |
| AIRLINER_PAX_SYS | not offered |
| AIRLINER_PROBE_HEAT | A330_PROBE_HEAT |
| AIRLINER_RUDDER_TRIM | not offered |
| AIRLINER_RWYTURN_TOGGLE | A330_RWY_TURNOFF |
| AIRLINER_SEATBELTS_TOGGLE | A330_SEAT_BELTS |
| AIRLINER_SPDMACH | left out: set by FCU window (speed/Mach toggle) |
| AIRLINER_SPOILER_LEVER | left out: the Flaps and Speedbrake panel's speedbrake row drives the lever through K:SPOILERS_SET, and its ground spoilers row through K:SPOILERS_ARM_ON / K:SPOILERS_ARM_OFF (IniA330Definition.Panels.cs, HandlePanelSet) |
| AIRLINER_STBYCOMPASS_LIGHT | A330_STBY_COMPASS_LT |
| AIRLINER_STBY_OXY_TEST | not offered |
| AIRLINER_STORM_TOGGLE | not offered |
| AIRLINER_STROBE_TOGGLE | A330_STROBE |
| AIRLINER_SVCE_OVRD | not offered |
| AIRLINER_TAXILIGHT_TOGGLE | A330_NOSE_LT |
| AIRLINER_TMR_RESET | not offered |
| AIRLINER_TRIM_RESET | not offered |
| AIRLINER_TURB_DAMP | not offered |
| AIRLINER_VIDEO_BUTTON | not offered |
| AIRLINER_VS_CTL | not offered |
| AIRLINER_WASTE_FO | not offered |
| AIRLINER_XBLEED | A330_XBLEED |

## Part 2: counts by group

| Group | Events | In map | Left out | Not offered |
| --- | --- | --- | --- | --- |
| CPT | 103 | 38 | 2 | 63 |
| FO | 103 | 10 | 2 | 91 |
| COM3 | 59 | 0 | 0 | 59 |
| UNKNOWN | 30 | 0 | 0 | 30 |
| ECAM | 25 | 25 | 0 | 0 |
| HYD | 19 | 19 | 0 | 0 |
| EFIS | 18 | 18 | 0 | 0 |
| ELEC | 18 | 18 | 0 | 0 |
| FUEL | 15 | 13 | 0 | 2 |
| MCU | 15 | 0 | 3 | 12 |
| TCAS | 15 | 6 | 0 | 9 |
| ENG1 | 8 | 7 | 0 | 1 |
| ENG2 | 8 | 7 | 0 | 1 |
| APU | 7 | 6 | 0 | 1 |
| ISIS | 7 | 3 | 0 | 4 |
| CALL | 6 | 6 | 0 | 0 |
| MAN | 6 | 4 | 0 | 2 |
| OXY | 6 | 0 | 0 | 6 |
| WX | 6 | 5 | 0 | 1 |
| CARGO | 5 | 3 | 0 | 2 |
| CLOCK | 5 | 3 | 0 | 2 |
| DOOR | 5 | 0 | 0 | 5 |
| EMER | 5 | 1 | 0 | 4 |
| EVAC | 4 | 0 | 0 | 4 |
| FADEC | 4 | 4 | 0 | 0 |
| GRAVITY | 4 | 0 | 0 | 4 |
| INR | 4 | 4 | 0 | 0 |
| N1 | 4 | 0 | 0 | 4 |
| SEAT | 4 | 0 | 0 | 4 |
| AFT | 3 | 3 | 0 | 0 |
| AUTOBRK | 3 | 0 | 3 | 0 |
| ENGINE | 3 | 1 | 0 | 2 |
| FTL | 3 | 3 | 0 | 0 |
| FWD | 3 | 3 | 0 | 0 |
| GND | 3 | 2 | 0 | 1 |
| HOT | 3 | 3 | 0 | 0 |
| LDG | 3 | 1 | 0 | 2 |
| PRINTER | 3 | 0 | 0 | 3 |
| AIR | 2 | 2 | 0 | 0 |
| CMC1 | 2 | 0 | 0 | 2 |
| COCKPIT | 2 | 1 | 0 | 1 |
| DITCHING | 2 | 2 | 0 | 0 |
| FLOOD | 2 | 0 | 0 | 2 |
| FLTCTL | 2 | 2 | 0 | 0 |
| GPWS | 2 | 2 | 0 | 0 |
| MASK | 2 | 0 | 0 | 2 |
| NWS | 2 | 0 | 0 | 2 |
| RAM | 2 | 2 | 0 | 0 |
| VALVE | 2 | 0 | 0 | 2 |
| VENT | 2 | 2 | 0 | 0 |
| VOR | 2 | 0 | 0 | 2 |
| WING | 2 | 2 | 0 | 0 |
| ACMS | 1 | 0 | 0 | 1 |
| ADIR1 | 1 | 1 | 0 | 0 |
| ADIR2 | 1 | 1 | 0 | 0 |
| ADIR3 | 1 | 1 | 0 | 0 |
| ADIRS1 | 1 | 1 | 0 | 0 |
| ADIRS2 | 1 | 1 | 0 | 0 |
| ADIRS3 | 1 | 1 | 0 | 0 |
| AIRCOND | 1 | 0 | 0 | 1 |
| ALT | 1 | 0 | 1 | 0 |
| ANNLT | 1 | 1 | 0 | 0 |
| ANTISKD | 1 | 1 | 0 | 0 |
| AP1 | 1 | 0 | 0 | 1 |
| AP2 | 1 | 0 | 0 | 1 |
| APPR | 1 | 0 | 0 | 1 |
| ATHR | 1 | 0 | 1 | 0 |
| ATT | 1 | 1 | 0 | 0 |
| AUDIO | 1 | 1 | 0 | 0 |
| BEACON | 1 | 1 | 0 | 0 |
| BRK | 1 | 1 | 0 | 0 |
| BULK | 1 | 1 | 0 | 0 |
| CAB | 1 | 0 | 0 | 1 |
| CABIN | 1 | 1 | 0 | 0 |
| CKPT | 1 | 0 | 0 | 1 |
| COCKPITDOOR | 1 | 0 | 0 | 1 |
| CTL | 1 | 1 | 0 | 0 |
| CTR | 1 | 0 | 0 | 1 |
| DFDR | 1 | 1 | 0 | 0 |
| ELEVATORTRIM | 1 | 0 | 0 | 1 |
| ENG | 1 | 1 | 0 | 0 |
| FLAP | 1 | 1 | 0 | 0 |
| FLAPS | 1 | 0 | 1 | 0 |
| FLT | 1 | 0 | 0 | 1 |
| FM | 1 | 1 | 0 | 0 |
| GEAR | 1 | 0 | 1 | 0 |
| GS | 1 | 1 | 0 | 0 |
| HDVS | 1 | 0 | 0 | 1 |
| IFEC | 1 | 0 | 0 | 1 |
| INTEG | 1 | 0 | 0 | 1 |
| IR1 | 1 | 1 | 0 | 0 |
| IR2 | 1 | 1 | 0 | 0 |
| IR3 | 1 | 1 | 0 | 0 |
| LAND | 1 | 1 | 0 | 0 |
| LDGLIGHT | 1 | 1 | 0 | 0 |
| LOC | 1 | 0 | 0 | 1 |
| MECH | 1 | 1 | 0 | 0 |
| METRIC | 1 | 0 | 1 | 0 |
| MODE | 1 | 1 | 0 | 0 |
| NAVLOGO | 1 | 1 | 0 | 0 |
| NORTH | 1 | 0 | 0 | 1 |
| NOSMOKING | 1 | 1 | 0 | 0 |
| OXYGEN | 1 | 1 | 0 | 0 |
| PACK | 1 | 1 | 0 | 0 |
| PACK1 | 1 | 1 | 0 | 0 |
| PACK2 | 1 | 1 | 0 | 0 |
| PAPER | 1 | 0 | 0 | 1 |
| PARKINGBRAKE | 1 | 0 | 1 | 0 |
| PAX | 1 | 0 | 0 | 1 |
| PROBE | 1 | 1 | 0 | 0 |
| RUDDER | 1 | 0 | 0 | 1 |
| RWYTURN | 1 | 1 | 0 | 0 |
| SEATBELTS | 1 | 1 | 0 | 0 |
| SPDMACH | 1 | 0 | 1 | 0 |
| SPOILER | 1 | 0 | 1 | 0 |
| STBY | 1 | 0 | 0 | 1 |
| STBYCOMPASS | 1 | 1 | 0 | 0 |
| STORM | 1 | 0 | 0 | 1 |
| STROBE | 1 | 1 | 0 | 0 |
| SVCE | 1 | 0 | 0 | 1 |
| TAXILIGHT | 1 | 1 | 0 | 0 |
| TMR | 1 | 0 | 0 | 1 |
| TRIM | 1 | 0 | 0 | 1 |
| TURB | 1 | 0 | 0 | 1 |
| VIDEO | 1 | 0 | 0 | 1 |
| VS | 1 | 0 | 0 | 1 |
| WASTE | 1 | 0 | 0 | 1 |
| XBLEED | 1 | 1 | 0 | 0 |
| Total | 649 | 271 | 18 | 360 |

## Part 3: the app's A32NX and Headwind A330 panel rows with no A330 match

This is the list plan 04 Task 5 works through; it is not a list of things to add now. It compares by function, not by label (the A32NX's "Strobe" matches the A330's `A330_STROBE`). "No event" means the catalogue above has no event for the function. "Stock event" marks a control those aircraft drive through a simulator event that is not an input event, so no input-event count can find it; the A330 may offer it the same way. "Event not offered" names a catalogued event that looks like the same function but is in the not-offered list; whether it is the same function is for Task 5 to measure.

The Headwind A330's panels are the A32NX's: the app's Headwind A330 definition inherits the A32NX panel list and changes only three rows (the second and third landing-light rows, and the two glareshield brightness knobs, which it drops). So the lists below hold for both aircraft. Those three rows all have an A330 match.

Panels the A330 reaches only through its own definition (the FCU, gear, autobrake, parking brake, flaps and speedbrake) are in the map's own panels, not compared here row by row, except where the A32NX has a row the A330 does not offer.

### Overhead

- ELEC: emergency generator 1 line (no event); emergency generator test (event not offered: `AIRLINER_EMER_GEN_TEST_BUTTON`, with its guard); external power available (a read-out; the A330 map has none).
- APU: autoexiting test (no event); autoexiting reset (event not offered: `AIRLINER_APU_EXTING_RESET`).
- Oxygen: masks deployed, passenger oxygen light (read-outs, no A330 row); oxygen timer reset (no event). Event not offered: `AIRLINER_MASK_MAN_BUTTON` with its guard, the `AIRLINER_OXY_TEST_*` events.
- Hydraulics: PTU and its fault light (no event); leak measurement, green, blue and yellow (no event, counted as one row below); the pump FAULT lights (read-outs; the A330 map has the pump rows only).
- Fuel: fuel mode select, manual (no event). Event not offered: `AIRLINER_FUEL_FEED_TOGGLE` with its guard.
- Pressurization: manual cabin vertical-speed control (event not offered: `AIRLINER_VS_CTL`, and `AIRLINER_VALVE_SEL` with its guard may be related; not shown to be).
- Ventilation: blower toggle (no event).
- Interior lighting: the pedestal, main panel, glareshield and overhead integral brightness knobs. Events not offered: `AIRLINER_FLOOD_LT_PED_KNOB`, `AIRLINER_FLOOD_MAIN_PNL_KNOB`, `AIRLINER_INTEG_LT_KNOB`, `AIRLINER_MCU_BRT_KNOB`, `AIRLINER_MCU_INT_KNOB`. These are pots on the A32NX, driven through L-variables, not input events.
- Recorder and miscellaneous: crew head set, recorder test, ELT on, ELT test and reset, DLS on (no event); avionics complete (no match found; `AIRLINER_CMC1` is not offered and is not shown to be the same).
- Flight Control Computers: FAC 1 and FAC 2 (no event); rudder trim reset and rudder trim left and right (stock events on those aircraft; events not offered: `AIRLINER_RUDDER_TRIM`, `AIRLINER_TRIM_RESET`).
- Wipers: first officer rain repellent (event not offered: `AIRLINER_FO_RAIN_RPL`).
- GPWS: flaps 3 mode and the GPWS test (no event).
- Calls: emergency call (event not offered: `AIRLINER_EMER_CALL`, with its cover).
- Cockpit: cockpit door locked, door video (events not offered: `AIRLINER_COCKPIT_DOOR_LOCK`, `AIRLINER_COCKPITDOOR`, `AIRLINER_CKPT_DOOR_VIDEO`); armrests and seat back (events not offered: the `AIRLINER_SEAT_ARMREST_*` and `AIRLINER_CPT_LEFT_ARMREST`, `_RIGHT_ARMREST` events and their first officer twins).
- Evacuation: evacuation command, captain toggle and horn shut-off (events not offered: `AIRLINER_EVAC_COMMAND` with its cover, `AIRLINER_EVAC_CPT_TOGGLE`, `AIRLINER_EVAC_HORN`).

### Glareshield

- FCU: expedite (no event); autopilot disconnect (no event); autothrust disconnect (events not offered: `AIRLINER_ENG1_ATDISCO`, `AIRLINER_ENG2_ATDISCO`). The FCU knobs and buttons themselves are the definition's own panel (plan 05); the not-offered list holds the knob events the definition does not press (`AIRLINER_MCU_*`, `AIRLINER_AP1_PUSH`, `AIRLINER_AP2_PUSH`, `AIRLINER_APPR_PUSH`, `AIRLINER_LOC_PUSH`).
- EFIS, both sides: none missing.
- Warnings: none missing.

### Main instrument panel

- Autobrake: none missing (brake fans have a row).
- ISIS: baro mode (events not offered: `AIRLINER_ISIS_BARO_INNER`, `AIRLINER_ISIS_BARO_OUTER`, `AIRLINER_ISIS_PLUS`, `AIRLINER_ISIS_MINUS`).
- Source switching: true or magnetic north reference (event not offered: `AIRLINER_NORTH_REF`).
- Clock, system display, gear: none missing.

### Pedestal

- Flight controls and speed brake: none missing for the lever, ground spoilers and flaps (the definition drives them through stock events, see the left-out reasons). Speed brake slider and the on and off events are the A32NX's own helpers; no A330 match is needed.
- Thrust levers: all-engine and per-engine detent rows (stock events on those aircraft; events not offered: `ENGINE_THROTTLE_1`, `ENGINE_THROTTLE_2`).
- Engines: none missing.
- ECAM control panel: none missing.
- Weather radar: multiscan auto and GCS auto (no event); event not offered: `AIRLINER_WX_CTRL`, `AIRLINER_TURB_DAMP`.
- Transponder: transponder code (stock event on those aircraft; events not offered: `AIRLINER_TCAS_1` to `AIRLINER_TCAS_7`, `AIRLINER_TCAS_CLR`, `AIRLINER_TCAS_ZERO`, which may be the transponder's keypad); the A32NX's datalink acknowledge is the A330's ATC message row.
- RMP: standby frequency, active frequency and swap for COM 1, 2 and 3 (stock events on those aircraft; events not offered: `AIRLINER_CPT_COMM_MHZ`, `AIRLINER_CPT_COMM_KHZ`, their first officer twins and the whole `AIRLINER_COM3_*` group, 59 events).

### Size by panel

Rows with no A330 match: ELEC 3, APU 2, Oxygen 3, Hydraulics 4, Fuel 1, Pressurization 1, Ventilation 1, Interior lighting 4, Recorder and miscellaneous 6, Flight Control Computers 5, Wipers 1, GPWS 2, Calls 1, Cockpit 6, Evacuation 3, FCU 3, ISIS 1, Source switching 1, Thrust levers 3, Weather radar 2, Transponder 1, RMP 9. Of these, the stock-event rows (the ones no input-event count shows) are: rudder trim reset, rudder trim left and right, the three thrust lever detent rows, the transponder code and the nine RMP frequency rows.
