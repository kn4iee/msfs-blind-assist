"""Rules of the BlueBird 757-200 control-map generator, on tiny in-memory rows.

No aircraft package is needed: rows are built by hand in the shape extract_controls.extract() returns, and the
extractor's own new interface (unclipped codes) is checked on a one-control package written to a temp folder.

Run from the repo root, any of:
    python -I tools/b757-gen/test_generate_b757_map.py
    python -I -m unittest discover -s tools/b757-gen -p "test_*.py"
    python -m unittest tools/b757-gen/test_generate_b757_map.py        (without -I: -I leaves the hyphenated
                                                                         folder off the import path)
"""
import contextlib
import copy
import io
import json
import os
import pathlib
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:  # -I puts neither the script's folder nor the current one on the path
    sys.path.insert(0, HERE)

import extract_controls  # noqa: E402
import generate_b757_map as g  # noqa: E402

def row(node="B757_OVHD_TEST_BUTTON", kind="toggle", panel="ELECTRICAL PANEL", line=1, positions="2",
        state=None, tips=None, left=None, release=None, cw=None, ccw=None, other=None, scope=None, file="x.xml"):
    """One extractor row, the parts the generator reads. Click codes are given as plain strings."""
    def items(code):
        return [("", code)] if code else []
    return {
        "file": file, "line": line, "panel": panel, "node": node, "kind": kind, "positions": positions,
        "_state": ["L:" + node] if state is None else state,
        "_tips": tips or [],
        "_clicks": {"left": items(left), "release": items(release), "cw": items(cw), "ccw": items(ccw),
                    "other": list(other or [])},
        "_scope": scope or {},
    }


CURATION = {
    "exclude_banners": ["CB OVERHEAD PANEL"],
    "exclude_nodes": [],
    "panels": {"ELECTRICAL PANEL": "Electrical", "FUEL PANEL": "Fuel"},
    "node_panels": [],
    "labels": {},
    "positions": {},
    "spelling": {"CENTRE": "Center"},
    "status": [],
}


def curation(**changes):
    c = copy.deepcopy(CURATION)
    c.update(changes)
    return c


class Humanize(unittest.TestCase):
    def test_vendor_prefix_area_and_role_suffix_are_dropped(self):
        self.assertEqual(g.humanize("B757_OVHD_BATTERY_BUTTON"), "Battery")

    def test_acronyms_stay_upper(self):
        self.assertEqual(g.humanize("B757_OVHD_APU_GEN_BUTTON"), "APU Gen")
        for word in ("APU", "IRS", "VHF", "HF", "ADF", "VOR", "DME", "EEC", "CVR", "EICAS", "CRT", "TCAS", "ILS",
                     "GPS", "FMC", "CDU", "MCP", "EFIS", "ND", "PFD", "AT", "AP", "FD", "GND", "ENG", "ELEC",
                     "HYD", "L", "R", "C", "BTL"):
            with self.subTest(word=word):
                self.assertEqual(g.humanize(f"B757_OVHD_{word}_FOO_BUTTON"), f"{word} Foo")

    def test_other_words_are_title_case(self):
        self.assertEqual(g.humanize("B757_PED_NAV_1_OFF_BUTTON"), "Nav 1 Off")
        self.assertEqual(g.humanize("B757_OVHD_FLT-DK-DOOR_SWITCH"), "Flt Dk Door")

    def test_several_role_words_at_the_end_are_dropped_but_never_all_words(self):
        self.assertEqual(g.humanize("B757_Stab_trim_C_toggle_switch_CLICK"), "Stab Trim C")
        self.assertEqual(g.humanize("B757_OVHD_SWITCH"), "Switch")

    def test_vendor_spelling_is_repaired(self):
        self.assertEqual(g.humanize("B757_IRS_CENTRE_KNOB", {"CENTRE": "Center"}), "IRS Center")

    def test_title_case_of_a_banner(self):
        self.assertEqual(g.title_case("WINDOW HEAT CONTROL PANEL BUTTONS"), "Window Heat Control Panel Buttons")
        self.assertEqual(g.title_case("APU CONTROL PANEL"), "APU Control Panel")


class Positions(unittest.TestCase):
    def labels(self, r):
        return [p["label"] for p in g.positions_for(r)]

    def test_animtips_become_labels_without_their_common_leading_words(self):
        r = row(kind="toggle", tips=["BATTERY OFF", "BATTERY ON"])
        self.assertEqual(g.positions_for(r), [{"value": 0, "label": "Off"}, {"value": 1, "label": "On"}])

    def test_animtips_may_come_as_index_text_pairs(self):
        r = row(kind="toggle", tips=[(0, "Captain FD OFF"), (1, "Captain FD ON")])
        self.assertEqual(self.labels(r), ["Off", "On"])

    def test_common_trailing_words_go_too_but_never_a_whole_label(self):
        r = row(kind="multi-position switch", positions="3",
                tips=["LEFT SELECTOR", "CENTRE SELECTOR", "RIGHT SELECTOR"])
        self.assertEqual(g.labels_from_tips(["LEFT SELECTOR", "CENTRE SELECTOR", "RIGHT SELECTOR"],
                                            {"CENTRE": "Center"}), ["Left", "Center", "Right"])
        self.assertEqual(self.labels(r), ["Left", "Centre", "Right"])  # no spelling table passed: words as written
        self.assertEqual(g.labels_from_tips(["SAME", "SAME"], None), ["Same", "Same"])

    def test_leading_words_are_kept_when_they_differ(self):
        r = row(kind="multi-position switch", positions="3", tips=["LEFT CDU", "LEFT FMC", "RIGHT FMC"])
        self.assertEqual(self.labels(r), ["Left CDU", "Left FMC", "Right FMC"])

    def test_clamp_without_tips_is_position_n(self):
        r = row(kind="multi-position switch", positions="range 0..2 step 1")
        self.assertEqual(g.positions_for(r), [{"value": 0, "label": "Position 0"},
                                              {"value": 1, "label": "Position 1"},
                                              {"value": 2, "label": "Position 2"}])

    def test_a_count_means_values_from_zero(self):
        r = row(kind="rotary knob finite", positions="3")
        self.assertEqual([p["value"] for p in g.positions_for(r)], [0, 1, 2])

    def test_the_clamps_in_the_turn_code_say_where_the_values_start(self):
        r = row(kind="rotary knob finite", positions="6",
                cw="(L:X, enum) 1 + 6 min (>L:X)", ccw="(L:X, enum) 1 - 1 max (>L:X)")
        self.assertEqual([p["value"] for p in g.positions_for(r)], [1, 2, 3, 4, 5, 6])

    def test_directional_or_mismatched_tips_are_not_positions(self):
        for tips in (["DECREASE", "INCREASE"], ["STANDBY POWER SELECTOR INC", "STANDBY POWER SELECTOR DEC"]):
            with self.subTest(tips=tips):
                self.assertEqual(self.labels(row(kind="rotary knob finite", positions="3", tips=tips)),
                                 ["Position 0", "Position 1", "Position 2"])
        # two tips for three positions: the tips do not name the positions
        self.assertEqual(self.labels(row(kind="rotary knob finite", positions="3", tips=["OFF", "ON"])),
                         ["Position 0", "Position 1", "Position 2"])

    def test_the_clamp_step_gives_the_values(self):
        r = row(kind="multi-position switch", positions="2", left="(L:X) 2 + 2 min (>L:X)",
                other=[("RightSingle", "(L:X) 2 - 0 max (>L:X)")])
        self.assertEqual([p["value"] for p in g.positions_for(r)], [0, 2])

    def test_off_on_tips_whose_wording_drifts_are_still_off_and_on(self):
        r = row(kind="toggle", tips=["L ENG HYD PUMP OFF", "L ENG HYD PUMPS ON"])
        self.assertEqual(self.labels(r), ["Off", "On"])

    def test_localization_keys_are_not_labels(self):
        r = row(kind="toggle", tips=["TT:COCKPIT.TOOLTIPS.A", "TT:COCKPIT.TOOLTIPS.B"])
        self.assertEqual(self.labels(r), ["Off", "On"])

    def test_defaults_per_kind(self):
        self.assertEqual(self.labels(row(kind="toggle")), ["Off", "On"])
        self.assertEqual(self.labels(row(kind="guard")), ["Closed", "Open"])
        self.assertEqual(self.labels(row(kind="select button")), ["Off", "On"])
        self.assertEqual(g.positions_for(row(kind="momentary button", positions="-")), [])
        self.assertEqual(g.positions_for(row(kind="held button", positions="-")), [])

    def test_more_than_twelve_positions_are_not_enumerable(self):
        self.assertIsNone(g.position_values(row(kind="rotary knob finite", positions="range 0..20 step 1")))
        self.assertIsNone(g.position_values(row(kind="rotary knob finite", positions="range 0..13 step 0.1")))
        self.assertEqual(len(g.position_values(row(kind="rotary knob finite", positions="range 0..11 step 1"))), 12)
        self.assertIsNone(g.position_values(row(kind="rotary knob finite", positions="")))


class Convert(unittest.TestCase):
    def convert(self, r, cur=None):
        return g.convert_row(r, cur or curation())

    def test_toggle(self):
        c, why = self.convert(row(node="B757_OVHD_BATTERY_BUTTON", kind="toggle", tips=["BATTERY OFF", "BATTERY ON"],
                                  left="(L:B757_OVHD_BATTERY_BUTTON,bool) ! (>L:B757_OVHD_BATTERY_BUTTON,bool)"))
        self.assertIsNone(why)
        self.assertEqual(c, {
            "node": "B757_OVHD_BATTERY_BUTTON", "section": "Overhead", "panel": "Electrical", "label": "Battery",
            "kind": "toggle", "state_var": "B757_OVHD_BATTERY_BUTTON",
            "positions": [{"value": 0, "label": "Off"}, {"value": 1, "label": "On"}],
            "press": "(L:B757_OVHD_BATTERY_BUTTON,bool) ! (>L:B757_OVHD_BATTERY_BUTTON,bool)",
            "release": None, "hold_ms": 0, "cw": None, "ccw": None})

    def test_finite_knob_with_few_positions_is_a_switch_that_keeps_its_turn_codes(self):
        c, _ = self.convert(row(node="B757_OVHD_STBY_POWER_SWITCH", kind="rotary knob finite", positions="3",
                                cw="(L:B757_OVHD_STBY_POWER_SWITCH, enum) 1 + 2 min (>L:B757_OVHD_STBY_POWER_SWITCH)",
                                ccw="(L:B757_OVHD_STBY_POWER_SWITCH, enum) 1 - 0 max (>L:B757_OVHD_STBY_POWER_SWITCH)"))
        self.assertEqual(c["kind"], "switch")
        self.assertIsNone(c["press"])
        self.assertIn("1 + 2 min", c["cw"])
        self.assertIn("1 - 0 max", c["ccw"])
        self.assertEqual(len(c["positions"]), 3)

    def test_switch_that_steps_on_click_with_clamps_keeps_cw_and_ccw(self):
        c, _ = self.convert(row(kind="multi-position switch", positions="3",
                                left="(L:X) 1 + 2 min (>L:X)",
                                other=[("RightSingle", "(L:X) 1 - 0 max (>L:X)"),
                                       ("WheelDown", "(L:X) 1 + 2 min (>L:X)")]))
        self.assertEqual((c["press"], c["cw"], c["ccw"]), (None, "(L:X) 1 + 2 min (>L:X)", "(L:X) 1 - 0 max (>L:X)"))

    def test_switch_that_cycles_keeps_press(self):
        c, _ = self.convert(row(kind="multi-position switch", positions="3", left="(L:X) 1 + 3 % (>L:X)"))
        self.assertEqual((c["press"], c["cw"], c["ccw"]), ("(L:X) 1 + 3 % (>L:X)", None, None))

    def test_switch_with_no_click_code_is_written_through_its_state_var(self):
        c, why = self.convert(row(node="BBS_fuelcut_RR1", kind="multi-position switch", positions="3",
                                  state=["L:BBS_fuel_RR"], tips=["Left Fuel Switch to RUN", "Left Fuel Switch to RICH",
                                                                "Left Fuel Switch to CUTOFF"]))
        self.assertIsNone(why)
        self.assertEqual((c["press"], c["cw"], c["ccw"], c["state_var"]), (None, None, None, "BBS_fuel_RR"))
        self.assertEqual([p["label"] for p in c["positions"]], ["Run", "Rich", "Cutoff"])

    def test_momentary_button_keeps_press_and_release(self):
        c, _ = self.convert(row(node="B757_MCP_SPD_BUTTON", kind="momentary button", positions="-",
                                state=["L:B757_MCP_SPD_BUTTON_PRESS"],
                                left="1 (>L:B757_MCP_SPD_BUTTON_PRESS, bool)",
                                release="0 (>L:B757_MCP_SPD_BUTTON_PRESS, bool)"))
        self.assertEqual((c["kind"], c["press"], c["release"], c["hold_ms"], c["positions"]),
                         ("button", "1 (>L:B757_MCP_SPD_BUTTON_PRESS, bool)",
                          "0 (>L:B757_MCP_SPD_BUTTON_PRESS, bool)", 0, []))

    def test_button_without_an_l_var_state_has_a_null_state_var(self):
        c, _ = self.convert(row(kind="momentary button", positions="-", state=["O:_AnimProgress"], left="1 (>L:Y)"))
        self.assertIsNone(c["state_var"])

    def test_several_click_templates_on_one_node_run_together(self):
        r = row(kind="momentary button", positions="-")
        r["_clicks"]["left"] = [("", "1 (>L:A)"), ("", "1 (>L:B)")]
        c, _ = self.convert(r)
        self.assertEqual(c["press"], "1 (>L:A) 1 (>L:B)")

    def test_held_button_takes_its_hold_time_from_the_template(self):
        c, _ = self.convert(row(kind="held button", positions="-", left="1 (>L:Y)", release="0 (>L:Y)",
                                scope={"MIN_HELD_DURATION": "2.5", "FIRE_HELD_EVENT": "1"}))
        self.assertEqual((c["kind"], c["hold_ms"]), ("held", 2500))
        c, _ = self.convert(row(kind="held button", positions="-", left="1 (>L:Y)", scope={}))
        self.assertEqual(c["hold_ms"], 1500)
        c, _ = self.convert(row(kind="held button", positions="-", left="1 (>L:Y)", scope={"MIN_HELD_DURATION": "x"}))
        self.assertEqual(c["hold_ms"], 1500)

    def test_select_and_guard(self):
        c, _ = self.convert(row(kind="select button", positions="-", left="1 (>L:X)"))
        self.assertEqual((c["kind"], [p["label"] for p in c["positions"]]), ("select", ["Off", "On"]))
        c, _ = self.convert(row(kind="guard", left="(L:X) ! (>L:X)"))
        self.assertEqual((c["kind"], [p["label"] for p in c["positions"]]), ("guard", ["Closed", "Open"]))

    def test_dropped_kinds_and_banners(self):
        cases = [
            (row(kind="circuit breaker", left="x"), "circuit breaker"),
            (row(kind="cdu key", positions="-", left="x"), "cdu key"),
            (row(kind="lever", positions=""), "lever"),
            (row(kind="rotary knob infinite", positions="inf", cw="1 (>L:X)"), "rotary knob infinite"),
            (row(kind="other", positions=""), "other"),
            (row(kind="rotary knob finite", positions="range 0..20 step 1", cw="x", ccw="y"),
             "rotary knob finite with more than 12 positions"),
            (row(kind="toggle", panel="CB OVERHEAD PANEL", left="x"), "excluded banner"),
        ]
        for r, reason in cases:
            with self.subTest(reason=reason):
                c, why = self.convert(r)
                self.assertIsNone(c)
                self.assertEqual(why, reason)

    def test_a_control_that_cannot_be_operated_is_dropped(self):
        c, why = self.convert(row(kind="toggle"))
        self.assertIsNone(c)
        self.assertEqual(why, "no click code")

    def test_a_toggle_whose_state_cannot_be_read_is_dropped(self):
        c, why = self.convert(row(kind="toggle", state=["A:SOMETHING"], left="1 (>K:SOMETHING)"))
        self.assertIsNone(c)
        self.assertEqual(why, "state is not an L:var")

    def test_a_guard_is_named_after_what_it_covers_and_reads_the_var_its_click_flips(self):
        c, _ = self.convert(row(node="B757_GAURD_3_SWITCH", kind="guard", tips=["Battery"],
                                state=["L:B757_OVHD_BATTERY_BUTTON", "L:GAURD_3_SWITCH"],
                                left="(L:GAURD_3_SWITCH,bool) ! (>L:GAURD_3_SWITCH,bool)"))
        self.assertEqual((c["label"], c["state_var"]), ("Battery Guard", "GAURD_3_SWITCH"))

    def test_a_guard_tip_that_already_says_guard_is_not_doubled(self):
        c, _ = self.convert(row(node="B757_P61_PANEL_SWITCH_GAURD", kind="guard", tips=["P61 Panel GAURD"],
                                left="(L:G) ! (>L:G)"), curation(spelling={"GAURD": "Guard"}))
        self.assertEqual(c["label"], "P61 Panel Guard")

    def test_a_toggles_tip_pair_is_not_its_name(self):
        # the vendor copied tips too: the center fuel pumps say "R PUMP AFT OFF" / "R PUMP AFT ON"
        c, _ = self.convert(row(node="B757_OVHD_C_PUMPS_L_BUTTON", kind="toggle", left="x",
                                tips=["R PUMP AFT OFF", "R PUMP AFT ON"]))
        self.assertEqual(c["label"], "C Pumps L")
        self.assertEqual([p["label"] for p in c["positions"]], ["Off", "On"])

    def test_a_tooltip_title_is_never_a_label(self):
        # the vendor copied titles between controls: every window heat button reads "Anti-ice Window"
        c, _ = self.convert(row(node="B757_OVHD_L_SIDE_WINDOW_HEAT_BUTTON", kind="toggle", left="x",
                                scope={"TOOLTIP_TITLE": "'Anti-ice Window'"}))
        self.assertEqual(c["label"], "L Side Window Heat")

    def test_a_toggle_that_writes_other_than_0_and_1_must_be_curated(self):
        r = row(node="B757_MCP_SW_AP_throttle", kind="toggle", state=["L:AT_ARM_SWITCH"],
                left="(L:AT_ARM_SWITCH, enum) 0 == if{ 10 (>L:AT_ARM_SWITCH) } els{ 0 (>L:AT_ARM_SWITCH) }")
        with self.assertRaises(g.CurationError):
            self.convert(r)
        cur = curation(positions={"B757_MCP_SW_AP_throttle": [{"value": 0, "label": "Armed"},
                                                              {"value": 10, "label": "Off"}]})
        c, _ = self.convert(r, cur)
        self.assertEqual(c["positions"][1], {"value": 10, "label": "Off"})

    def test_excluded_nodes(self):
        c, why = self.convert(row(node="B757_SUNVISOR_L_base", kind="toggle", left="x"),
                              curation(exclude_nodes=["B757_SUNVISOR_L_base"]))
        self.assertEqual((c, why), (None, "excluded node"))

    def test_state_var_takes_the_first_l_var_and_ignores_the_extractor_notes(self):
        r = row(kind="toggle", left="x", state=["A:FOO", "L:BBS_FD1_SWITCH (from the click; no ANIM_CODE)", "L:B"])
        c, _ = self.convert(r)
        self.assertEqual(c["state_var"], "BBS_FD1_SWITCH")

    def test_label_and_position_overrides(self):
        cur = curation(labels={"B757_MCP_AP_ALT_SEL": "Altitude Selector"},
                       positions={"B757_OVHD_APU_SWITCH": ["Off", "On", "Start"],
                                  "B757_FIRE": [{"value": 0, "label": "In"}, {"value": 1, "label": "Pulled"}]})
        c, _ = self.convert(row(node="B757_MCP_AP_ALT_SEL", kind="toggle", left="x"), cur)
        self.assertEqual(c["label"], "Altitude Selector")
        c, _ = self.convert(row(node="B757_OVHD_APU_SWITCH", kind="rotary knob finite", positions="3",
                                cw="(L:A) 1 + 2 min (>L:A)", ccw="(L:A) 1 - 0 max (>L:A)"), cur)
        self.assertEqual(c["positions"], [{"value": 0, "label": "Off"}, {"value": 1, "label": "On"},
                                          {"value": 2, "label": "Start"}])
        c, _ = self.convert(row(node="B757_FIRE", kind="multi-position switch", positions="4", left="x"), cur)
        self.assertEqual([p["label"] for p in c["positions"]], ["In", "Pulled"])

    def test_a_position_override_of_the_wrong_length_is_an_error(self):
        cur = curation(positions={"B757_OVHD_APU_SWITCH": ["Off", "On"]})
        with self.assertRaises(g.CurationError):
            self.convert(row(node="B757_OVHD_APU_SWITCH", kind="rotary knob finite", positions="3",
                             cw="(L:A) 1 + 2 min (>L:A)", ccw="(L:A) 1 - 0 max (>L:A)"), cur)


class SuspectedSlips(unittest.TestCase):
    def test_a_set_without_its_variable_type_is_suspected(self):
        code = "(L:X,bool) ! (>L:X,bool) ! (L:X_ON,bool) ! (>X_ON,bool)"
        self.assertEqual(g.suspected_slips(code), ["set without a variable type"])

    def test_typed_sets_are_not_suspected(self):
        code = "1 (>K:AUTOPILOT_OFF) 1 (>H:EVT) 2 (>O:ButtonLocked, bool) 3 (>A:LIGHT POTENTIOMETER:20, number) 1 (>L:X)"
        self.assertEqual(g.suspected_slips(code), [])

    def test_a_closing_brace_nothing_opened_is_suspected(self):
        self.assertEqual(g.suspected_slips("(L:X) ! (>L:X) }"), ["closing brace nothing opened"])
        self.assertEqual(g.suspected_slips("(L:X) if{ 1 (>L:Y) } els{ 0 (>L:Y) }"), [])
        self.assertEqual(g.suspected_slips(None), [])

    def test_the_click_code_is_replayed_verbatim_and_the_slip_is_only_reported(self):
        slip = "(L:A) ! (>L:A) ! (L:A_ON,bool) ! (>A_ON,bool)"
        brace = "(L:B) ! (>L:B) }"
        rows = [row(node="B757_OVHD_L_PUMPS_FWD_BUTTON", panel="FUEL PANEL", left=slip),
                row(node="B757_MAIN_INSTRUMENT_ADF_Knob_RIGHT_FO", panel="FUEL PANEL", left=brace, state=["L:B"])]
        m, report = g.build_map(rows, curation(), "pkg", "1")
        self.assertEqual([c["press"] for c in m["controls"]], [slip, brace])
        self.assertTrue(all("_suspected" not in c for c in m["controls"]))
        self.assertEqual(report["suspected_slips"], {
            "B757_OVHD_L_PUMPS_FWD_BUTTON": ["press: set without a variable type"],
            "B757_MAIN_INSTRUMENT_ADF_Knob_RIGHT_FO": ["press: closing brace nothing opened"]})


class Sections(unittest.TestCase):
    def test_section_from_the_node_prefix(self):
        for node, section in (("B757_OVHD_X", "Overhead"), ("BBS_OVHD_X", "Overhead"), ("B757_MCP_X", "Glareshield"),
                              ("B757_MAIN_X", "Forward panel"), ("B757_ADI_1_X", "Forward panel"),
                              ("B757_HSI_1_X", "Forward panel"), ("B757_PED_X", "Pedestal"),
                              ("BBS757_FIREUNLOCK1", "Other"), ("OXY_CAPT_TEST", "Other")):
            with self.subTest(node=node):
                self.assertEqual(g.section_of(node), section)

    def test_panel_from_curation_falls_back_to_the_banner_in_title_case(self):
        self.assertEqual(g.panel_of(row(panel="FUEL PANEL"), curation())[0], "Fuel")
        self.assertEqual(g.panel_of(row(panel="WINDOW HEAT CONTROL PANEL BUTTONS"), curation())[0],
                         "Window Heat Control Panel Buttons")

    def test_node_prefix_rule_beats_the_banner(self):
        cur = curation(node_panels=[{"banner": "MAIN BUTTONS AND CONTROL", "prefix": "B757_MCP_", "panel": "MCP"}],
                       panels={"MAIN BUTTONS AND CONTROL": "Lighting"})
        self.assertEqual(g.panel_of(row(node="B757_MCP_SPD_BUTTON", panel="MAIN BUTTONS AND CONTROL"), cur)[0], "MCP")
        self.assertEqual(g.panel_of(row(node="B757_FLOOD_DOME_KNOB", panel="MAIN BUTTONS AND CONTROL"), cur)[0],
                         "Lighting")
        self.assertEqual(g.panel_of(row(node="B757_MCP_SPD_BUTTON", panel="OTHER BANNER"), cur)[0], "Other Banner")

    def test_a_panel_entry_may_name_its_own_section(self):
        cur = curation(panels={"IRS KNOBS": {"name": "IRS", "section": "Overhead"}, "FUEL PANEL": "Fuel"})
        self.assertEqual(g.panel_of(row(node="B757_IRS_L_SWITCH", panel="IRS KNOBS"), cur), ("IRS", "Overhead"))
        self.assertEqual(g.panel_of(row(node="B757_IRS_L_SWITCH", panel="FUEL PANEL"), cur),
                         ("Fuel", "Other"))


class WholeMap(unittest.TestCase):
    def rows(self):
        return [
            row(node="B757_OVHD_FUEL_L_FWD_PUMP_BUTTON", panel="FUEL PANEL", line=20, left="(L:A) ! (>L:A)"),
            row(node="B757_OVHD_BATTERY_BUTTON", panel="ELECTRICAL PANEL", line=30, left="(L:B) ! (>L:B)"),
            row(node="B757_OVHD_LIGHT_BUTTON", panel="ELECTRICAL PANEL", line=10, left="(L:C) ! (>L:C)"),
            row(node="B757_OVHD_LIGHT_SWITCH", panel="ELECTRICAL PANEL", line=11, left="(L:D) ! (>L:D)"),
            row(node="A2_CB", kind="circuit breaker", panel="CB OVERHEAD PANEL", left="x"),
            row(node="A3_CB", kind="circuit breaker", panel="ELECTRICAL PANEL", left="x"),
            row(node="B757_OVHD_BATTERY_BUTTON", panel="ELECTRICAL PANEL", line=99, left="(L:B) ! (>L:B)",
                file="pw.xml"),
            row(node="B757_MCP_SPD_BUTTON", kind="momentary button", panel="ELECTRICAL PANEL", positions="-",
                left="1 (>L:S)", release="0 (>L:S)", line=5),
        ]

    def build(self, rows=None, cur=None):
        return g.build_map(rows or self.rows(), cur or curation(), "pkg", "9.9.9")

    def test_shape_and_order(self):
        m, report = self.build()
        self.assertEqual(list(m), ["aircraft", "package_version", "controls", "status"])
        self.assertEqual((m["aircraft"], m["package_version"], m["status"]), ("pkg", "9.9.9", []))
        self.assertEqual(list(m["controls"][0]), ["node", "section", "panel", "label", "kind", "state_var",
                                                  "positions", "press", "release", "hold_ms", "cw", "ccw"])
        # section (Overhead, Glareshield), then panel, then file line
        sections = [c["section"] for c in m["controls"]]
        self.assertEqual(sections, sorted(sections, key=g.SECTIONS.index))
        lines = [(c["panel"], c["node"]) for c in m["controls"] if c["section"] == "Overhead"]
        self.assertEqual(lines, [("Electrical", "B757_OVHD_LIGHT_BUTTON"), ("Electrical", "B757_OVHD_LIGHT_SWITCH"),
                                 ("Electrical", "B757_OVHD_BATTERY_BUTTON"), ("Fuel", "B757_OVHD_FUEL_L_FWD_PUMP_BUTTON")])

    def test_dropped_rows_are_counted_by_reason(self):
        m, report = self.build()
        self.assertEqual(report["dropped"]["circuit breaker"], 1)
        self.assertEqual(report["dropped"]["excluded banner"], 1)
        self.assertEqual(report["dropped"]["duplicate node"], 1)
        self.assertEqual(len(m["controls"]), 5)

    def test_duplicate_node_keeps_the_first_row(self):
        m, _ = self.build()
        battery = [c for c in m["controls"] if c["node"] == "B757_OVHD_BATTERY_BUTTON"]
        self.assertEqual(len(battery), 1)

    def test_labels_are_unique_inside_one_panel(self):
        # two curated "Light" labels: the second becomes "Light 2"
        cur = curation(labels={"B757_OVHD_LIGHT_BUTTON": "Light", "B757_OVHD_LIGHT_SWITCH": "Light"})
        m, _ = self.build(cur=cur)
        labels = [c["label"] for c in m["controls"] if c["panel"] == "Electrical" and c["section"] == "Overhead"]
        self.assertEqual(labels, ["Light", "Light 2", "Battery"])

    def test_names_that_differ_only_by_their_suffix_keep_it_before_any_numbering(self):
        m, _ = self.build()
        labels = [c["label"] for c in m["controls"] if c["panel"] == "Electrical" and c["section"] == "Overhead"]
        self.assertEqual(labels, ["Light", "Light Switch", "Battery"])
        rows = [row(node="B757_PED_AUDIO_1_MIC_L_LIGHT", kind="select button", positions="-", left="1 (>L:A)"),
                row(node="B757_PED_AUDIO_1_MIC_L_LIGHT_KNOB", kind="toggle", left="y"),
                row(node="B757_PED_AUDIO_1_MIC_L_LIGHT_KNOB_2", kind="toggle", left="z")]
        m, _ = self.build(rows)
        self.assertEqual([c["label"] for c in m["controls"]],
                         ["Audio 1 Mic L Light", "Audio 1 Mic L Light Knob", "Audio 1 Mic L Light Knob 2"])

    def test_the_same_label_in_two_panels_is_fine(self):
        rows = [row(node="B757_OVHD_PUMP_BUTTON", panel="FUEL PANEL", left="x"),
                row(node="B757_OVHD_PUMP_SWITCH", panel="ELECTRICAL PANEL", left="y")]
        m, _ = self.build(rows)
        self.assertEqual(sorted(c["label"] for c in m["controls"]), ["Pump", "Pump"])

    def test_numbered_label_never_collides_with_a_real_one(self):
        controls = [{"panel": "P", "section": "S", "label": l} for l in ("Light", "Light 2", "Light")]
        g.unique_labels(controls)
        self.assertEqual([c["label"] for c in controls], ["Light", "Light 2", "Light 3"])

    def test_json_is_deterministic(self):
        a = g.dumps(self.build()[0])
        b = g.dumps(self.build()[0])
        self.assertEqual(a, b)
        self.assertTrue(a.endswith("\n"))
        self.assertEqual(json.loads(a)["controls"][0]["node"], self.build()[0]["controls"][0]["node"])

    def test_an_override_or_exclusion_for_a_node_not_in_the_package_is_an_error(self):
        for key, value in (("labels", {"B757_NO_SUCH_NODE": "Ghost"}), ("positions", {"B757_NO_SUCH_NODE": ["a"]}),
                           ("exclude_nodes", ["B757_NO_SUCH_NODE"])):
            with self.subTest(key=key):
                with self.assertRaises(g.CurationError):
                    self.build(cur=curation(**{key: value}))

    def test_an_override_for_a_node_that_was_dropped_is_not(self):
        m, report = self.build(cur=curation(labels={"A2_CB": "Breaker"}))
        self.assertEqual(report["overrides_on_dropped"], ["A2_CB"])

    def test_a_banner_with_no_panel_name_is_reported(self):
        rows = [row(node="B757_OVHD_X_BUTTON", panel="NEW VENDOR PANEL", left="x"),
                row(node="B757_OVHD_Y_BUTTON", panel="FUEL PANEL", left="y")]
        m, report = self.build(rows)
        self.assertEqual(report["unmapped_banners"], ["NEW VENDOR PANEL"])
        self.assertEqual(sorted(c["panel"] for c in m["controls"]), ["Fuel", "New Vendor Panel"])

    def test_status_rows(self):
        cur = curation(status=[
            {"key": "B757_APU_STATE", "panel": "Status", "label": "APU",
             "positions": ["Off", "Starting", "Running", "Shutting down"], "announce": True},
            {"key": "B757_APU_RPM_PRC", "panel": "Status", "label": "APU RPM", "announce": False}])
        m, _ = self.build(cur=cur)
        self.assertEqual(m["status"][0], {
            "key": "B757_APU_STATE", "panel": "Status", "label": "APU",
            "positions": [{"value": 0, "label": "Off"}, {"value": 1, "label": "Starting"},
                          {"value": 2, "label": "Running"}, {"value": 3, "label": "Shutting down"}],
            "announce": True})
        self.assertEqual(m["status"][1]["positions"], [])
        self.assertIs(m["status"][1]["announce"], False)


class Curation(unittest.TestCase):
    def write(self, data):
        d = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, d, ignore_errors=True)
        path = os.path.join(d, "curation.json")
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(data, fh)
        return path

    def test_unknown_key_is_refused(self):
        with self.assertRaises(g.CurationError):
            g.load_curation(self.write({"exclude_banner": []}))

    def test_underscore_keys_are_notes(self):
        c = g.load_curation(self.write({"_notes": ["a note"]}))
        self.assertEqual(c["labels"], {})

    def test_the_shipped_curation_loads(self):
        c = g.load_curation(g.DEFAULT_CURATION)
        for banner in ("CB OVERHEAD PANEL", "ADDING L VARS for CDU switches", "FO CDU CODE", "COCKPIT CAPTAIN CHAIR",
                       "RIGHT WINDOW RAIN EFFECTS", "MISC PARTS", "THROTTLES MAIN", "RUDDER TRIM_LIMIT",
                       "EMISSIVE GAUGES"):
            self.assertIn(banner, c["exclude_banners"])
        keys = [s["key"] for s in c["status"]]
        self.assertEqual(keys, ["B757_APU_STATE", "BBS_CA_MASTER_WARNING", "BBS_CA_MASTER_CAUTION",
                                "B757_L_BUS_PWR_SRC", "B757_R_BUS_PWR_SRC", "B757_APU_RPM_PRC",
                                "B757_DEBUG_PRESSURE_CABIN_ALT_FT"])
        self.assertEqual([s["announce"] for s in c["status"]], [True, True, True, False, False, False, False])
        for banner, entry in c["panels"].items():  # the vendor's typos never reach a pilot
            name = entry if isinstance(entry, str) else entry["name"]
            self.assertNotRegex(name, "GAURD|FISRT|Gaurd|Fisrt")
        self.assertTrue({"B757_MCP_AP_ALT_SEL", "B757_OVHD_AUTORATE_SWITCH", "BBS757_FIREHANDLE1_CLICK"}
                        <= set(c["labels"]))
        self.assertNotIn("_suspected_slips", c, "the suspected slips are information, never curation")


class FindPackage(unittest.TestCase):
    def test_finds_the_package_in_community_or_community2024(self):
        with tempfile.TemporaryDirectory() as d:
            base = pathlib.Path(d, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages")
            self.assertIsNone(g.find_package(d))
            pkg = base / "Community2024" / g.PACKAGE_NAME
            pkg.mkdir(parents=True)
            self.assertIsNone(g.find_package(d), "a folder without manifest.json is not the package")
            (pkg / "manifest.json").write_text("{}", encoding="utf-8")
            self.assertEqual(g.find_package(d), pkg)

    def test_community_comes_before_community2024(self):
        with tempfile.TemporaryDirectory() as d:
            base = pathlib.Path(d, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages")
            for sub in ("Community2024", "Community"):
                pkg = base / sub / g.PACKAGE_NAME
                pkg.mkdir(parents=True)
                (pkg / "manifest.json").write_text("{}", encoding="utf-8")
            self.assertEqual(g.find_package(d), base / "Community" / g.PACKAGE_NAME)


LONG_CODE = "(L:X_LONG, bool) ! (>L:X_LONG, bool) " + " ".join(f"{i} (>L:PAD_{i})" for i in range(60))


def write_package(root):
    """A one-control package: model.cfg names an interior that holds one switch with a click longer than the TSV's
    400-character clip."""
    pkg = pathlib.Path(root, "bluebirdsim-aircraft-757-200")
    model = pkg / "SimObjects" / "Airplanes" / "a" / "model"
    model.mkdir(parents=True)
    (pkg / "manifest.json").write_text(json.dumps({"title": "T", "creator": "C", "package_version": "9.9.9"}),
                                       encoding="utf-8")
    (pkg / "layout.json").write_text("{}", encoding="utf-8")
    (model / "model.cfg").write_text("[models]\ninterior=interior.xml\n", encoding="utf-8")
    (model / "interior.xml").write_text(f"""<?xml version="1.0"?>
<ModelInfo><Behaviors>
<!-- ======== ELECTRICAL PANEL ======== -->
<Component ID="BATT" Node="B757_OVHD_BATTERY_BUTTON">
  <UseTemplate Name="ASOBO_GT_Switch_Code">
    <ANIM_NAME>B757_OVHD_BATTERY_BUTTON</ANIM_NAME>
    <ANIM_CODE>(L:X_LONG, bool) 100 *</ANIM_CODE>
    <ANIMTIP_0>BATTERY OFF</ANIMTIP_0>
    <ANIMTIP_1>BATTERY ON</ANIMTIP_1>
    <LEFT_SINGLE_CODE>{LONG_CODE}</LEFT_SINGLE_CODE>
  </UseTemplate>
</Component>
</Behaviors></ModelInfo>
""", encoding="utf-8")
    return pkg


class Extractor(unittest.TestCase):
    def test_extract_returns_unclipped_rows_lights_and_summary(self):
        with tempfile.TemporaryDirectory() as d:
            controls, lights, summary = extract_controls.extract(write_package(d))
        self.assertEqual(len(controls), 1)
        self.assertEqual(lights, [])
        self.assertIn("errors", summary)
        r = controls[0]
        self.assertEqual((r["node"], r["kind"], r["panel"]), ("B757_OVHD_BATTERY_BUTTON", "toggle", "ELECTRICAL PANEL"))
        self.assertGreater(len(LONG_CODE), extract_controls.CLIP)
        self.assertEqual(r["_clicks"]["left"], [("", LONG_CODE)])
        self.assertTrue(r["left"].endswith("…"), "the TSV cell stays clipped")
        self.assertEqual(r["_tips"], [(0, "BATTERY OFF"), (1, "BATTERY ON")])
        self.assertIn("L:X_LONG", r["_state"])

    def test_generator_end_to_end_on_the_fixture_package(self):
        with tempfile.TemporaryDirectory() as d:
            pkg = write_package(d)
            out = os.path.join(d, "out", "map.json")
            cur_path = os.path.join(d, "curation.json")
            with open(cur_path, "w", encoding="utf-8") as fh:
                json.dump({"panels": {"ELECTRICAL PANEL": "Electrical"}}, fh)
            with contextlib.redirect_stdout(io.StringIO()) as stdout:
                g.main([str(pkg), "--out", out, "--curation", cur_path])
            with open(out, encoding="utf-8") as fh:
                m = json.load(fh)
        self.assertEqual((m["aircraft"], m["package_version"]), ("bluebirdsim-aircraft-757-200", "9.9.9"))
        self.assertEqual(len(m["controls"]), 1)
        c = m["controls"][0]
        self.assertEqual(c["press"], LONG_CODE)
        self.assertEqual([p["label"] for p in c["positions"]], ["Off", "On"])
        self.assertEqual((c["label"], c["panel"], c["section"]), ("Battery", "Electrical", "Overhead"))
        self.assertIn("controls: 1", stdout.getvalue())


if __name__ == "__main__":
    unittest.main()
