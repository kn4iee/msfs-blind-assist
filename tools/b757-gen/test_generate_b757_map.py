"""Rules of the BlueBird 757-200 control-map generator, on tiny in-memory rows.

No aircraft package is needed: rows are built by hand in the shape extract_controls.extract() returns, and the
extractor's own new interface (unclipped codes) is checked on a one-control package written to a temp folder.

Run from the repo root, any of:
    python -I tools/b757-gen/test_generate_b757_map.py
    python -I -m unittest discover -s tools/b757-gen -p "test_*.py"
    python -m unittest tools/b757-gen/test_generate_b757_map.py        (without -I: -I leaves the hyphenated
                                                                         folder off the import path)
"""
import collections
import contextlib
import copy
import io
import json
import os
import pathlib
import re
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
    "include_nodes": [],
    "panels": {"ELECTRICAL PANEL": "Electrical", "FUEL PANEL": "Fuel"},
    "node_panels": [],
    "panel_order": {},
    "control_order": {},
    "labels": {},
    "positions": {},
    "kinds": {},
    "spelling": {"CENTRE": "Center"},
    "status": [],
}


def curation(**changes):
    c = copy.deepcopy(CURATION)
    c.update(changes)
    return c


# The APU fire handle writes 1 (pulled) when unlocked or on fire and 0 otherwise: a gate, not a flip, so the extractor
# reads a momentary button with no positions, although the handle keeps its own state in L:B757_APUFIRE_SWITCH.
APU_HANDLE_PRESS = ("(L:BBS757_APU_FIRE_HANDLE_ROTATION, enum) 1 == and (L:BBS757_FIREUNLOCK3, bool) 1 == "
                    "(L:B757_APU_FIRE_ACTIVE, bool) 1 == or if{ 1 (>L:B757_APUFIRE_SWITCH, bool) } "
                    "els{ 0 (>L:B757_APUFIRE_SWITCH, bool) }")


def apu_fire_handle_row():
    return row(node="BBS757_FIREHANDLE1_CLICK3", kind="momentary button", positions="-",
               panel="PEDESTAL GAURD SWITCH GEAR", state=["L:B757_APUFIRE_SWITCH"],
               tips=["Pull APU Fire Switch", "Reset Engine 2 Fire Switch"], left=APU_HANDLE_PRESS)


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
        """(control, why): convert_row without the suspected-slips list, which has its own tests."""
        control, why, _slips = g.convert_row(r, cur or curation())
        return control, why

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

    def test_a_switch_that_writes_a_value_its_positions_do_not_list_must_be_curated(self):
        # B757_OVHD_CABINALT_SWITCH: three tips, but the turn code writes 0 and 7
        r = row(node="B757_OVHD_CABINALT_SWITCH", kind="rotary knob finite", positions="3",
                tips=["OFF", "AUTO", "ON"],
                cw="7 (>L:B757_OVHD_CABINALT_SWITCH) (E:ABSOLUTE TIME, seconds) (>O:UpdateTime)",
                ccw="0 (>L:B757_OVHD_CABINALT_SWITCH) (E:ABSOLUTE TIME, seconds) (>O:UpdateTime)")
        with self.assertRaisesRegex(g.CurationError, "writes 7 .* outside its positions"):
            self.convert(r)
        # named positions that include the value make it pass; so does leaving the node out
        cur = curation(positions={"B757_OVHD_CABINALT_SWITCH": [{"value": 0, "label": "A"}, {"value": 4, "label": "B"},
                                                                {"value": 7, "label": "C"}]})
        c, _ = self.convert(r, cur)
        self.assertEqual([p["value"] for p in c["positions"]], [0, 4, 7])
        c, why = self.convert(r, curation(exclude_nodes=["B757_OVHD_CABINALT_SWITCH"]))
        self.assertEqual((c, why), (None, "excluded node"))

    def test_a_switch_that_writes_only_listed_values_passes(self):
        r = row(node="B757_OVHD_FLT-DK-DOOR_SWITCH", kind="rotary knob finite", positions="3",
                tips=["UNLOCKED", "AUTO", "DENY"],
                cw="2 (>L:B757_OVHD_FLT_DK_DOOR_SWITCH) (E:ABSOLUTE TIME, seconds) (>O:UpdateTime)",
                ccw="0 (>L:B757_OVHD_FLT_DK_DOOR_SWITCH) (E:ABSOLUTE TIME, seconds) (>O:UpdateTime)",
                state=["L:B757_OVHD_FLT_DK_DOOR_SWITCH"])
        c, why = self.convert(r)
        self.assertIsNone(why)
        self.assertEqual([p["label"] for p in c["positions"]], ["Unlocked", "Auto", "Deny"])

    def test_curated_positions_are_checked_against_what_the_code_writes_too(self):
        r = row(node="B757_MCP_SW_AP_throttle", kind="toggle", state=["L:AT_ARM_SWITCH"],
                left="(L:AT_ARM_SWITCH, enum) 0 == if{ 10 (>L:AT_ARM_SWITCH) } els{ 0 (>L:AT_ARM_SWITCH) }")
        cur = curation(positions={"B757_MCP_SW_AP_throttle": ["Armed", "Off"]})  # names for 0 and 1: 10 is not listed
        with self.assertRaises(g.CurationError):
            self.convert(r, cur)

    def test_suspected_slips_come_back_as_a_third_value(self):
        control, why, slips = g.convert_row(
            row(kind="toggle", left="(L:A) ! (>L:A) ! (>A_ON,bool) }", state=["L:A"]), curation())
        self.assertIsNone(why)
        self.assertEqual(slips, ["press: set without a variable type", "press: closing brace nothing opened"])
        self.assertNotIn("_suspected", control)
        self.assertEqual(g.convert_row(row(kind="circuit breaker"), curation()), (None, "circuit breaker", []))

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


    def test_a_curated_explicit_list_may_skip_a_value_the_extractor_counts(self):
        # B757_OVHD_L_ENG_START_SWITCH: the extractor counts 6 positions, but the click code computes its values and
        # never writes 2 (cw goes 1 to 3, ccw 3 to 1), so curation names the five that exist.
        node = "B757_OVHD_L_ENG_START_SWITCH"
        cw = "(L:S, enum) 1 == if{ (L:S, enum) 2 + (>L:S) } els{ (L:S, enum) 1 + 6 min (>L:S) }"
        ccw = "(L:S, enum) 3 == if{ (L:S, enum) 2 - (>L:S) } els{ (L:S, enum) 1 - 1 max (>L:S) }"
        pairs = [{"value": v, "label": l} for v, l in ((1, "GND"), (3, "AUTO"), (4, "P4"), (5, "P5"), (6, "P6"))]
        cur = curation(positions={node: pairs})

        c, why = self.convert(row(node=node, kind="rotary knob finite", positions="6", cw=cw, ccw=ccw, state=["L:S"]), cur)

        self.assertIsNone(why)
        self.assertEqual(c["positions"], pairs)
        self.assertEqual((c["cw"], c["ccw"]), (cw, ccw))  # the click code is still replayed verbatim
        # the safety net holds for an explicit list: a literal the code writes outside it still stops the run
        with self.assertRaisesRegex(g.CurationError, "writes 2 .* outside its positions"):
            self.convert(row(node=node, kind="rotary knob finite", positions="6", cw=cw + " 2 (>L:S)", ccw=ccw,
                             state=["L:S"]), cur)
        # names in value order, by contrast, must still be one per value the extractor counts
        with self.assertRaises(g.CurationError):
            self.convert(row(node=node, kind="rotary knob finite", positions="6", cw=cw, ccw=ccw, state=["L:S"]),
                         curation(positions={node: ["GND", "AUTO", "P4", "P5", "P6"]}))

    def test_a_name_list_is_laid_onto_the_controls_real_values_in_value_order(self):
        # B757_OVHD_WARNING_IND_SWITCH rests at 0 and 11 (its code writes those two constants): two names are the pair
        # (0, 11), not (0, 1). The list form keeps non-contiguous values; the names follow the values in ascending order.
        node = "B757_OVHD_WARNING_IND_SWITCH"
        r = row(node=node, kind="rotary knob finite", positions="2", state=["L:" + node],
                cw=f"11 (>L:{node})", ccw=f"0 (>L:{node})")

        c, why = self.convert(r, curation(positions={node: ["DIM", "BRT"]}))

        self.assertIsNone(why)
        self.assertEqual([(p["value"], p["label"]) for p in c["positions"]], [(0, "DIM"), (11, "BRT")])

    def test_the_chronometer_ET_lever_is_named_where_its_click_code_lands_past_HOLD(self):
        # BlueBird's list puts RESET at 3 with 2 unused; the cockpit's tooltip list puts it at 2. The vendor's code jumps
        # from HOLD (1) straight to 3, so RESET is 3, 2 is left out, and the run refuses a list that names 2 instead.
        node, var = "B757_MAIN_CHRONO_ET_SWITCH", "BBS_CHRONO_LEFT_SWITCH"
        r = row(node=node, kind="rotary knob finite", positions="4", state=["L:" + var],
                cw=f"(L:{var}, enum) -- d 0 < if{{ 0 }} (>L:{var}, enum)",
                ccw=f"(L:{var}, enum) 1 == if{{ 3 (>L:{var}, enum) }} "
                    f"els{{ (L:{var}, enum) ++ d 3 > if{{ 3 }} (>L:{var}, enum) }}")

        def reset_at(value):
            return curation(positions={node: [{"value": v, "label": l}
                                              for v, l in ((0, "RUN"), (1, "HOLD"), (value, "RESET"))]})

        c, why = self.convert(r, reset_at(3))
        self.assertIsNone(why)
        self.assertEqual([(p["value"], p["label"]) for p in c["positions"]], [(0, "RUN"), (1, "HOLD"), (3, "RESET")])
        with self.assertRaisesRegex(g.CurationError, "writes 3 .* outside its positions"):
            self.convert(r, reset_at(2))

    # Plan 02, task 2: `kinds` corrects how the extractor read a control it keeps. The click code stays the vendor's.
    def test_a_curated_kind_turns_a_momentary_button_into_a_toggle_on_its_own_state(self):
        node, r = "BBS757_FIREHANDLE1_CLICK3", apu_fire_handle_row()

        c, _ = self.convert(r)
        self.assertEqual((c["kind"], c["state_var"], c["positions"]), ("button", "B757_APUFIRE_SWITCH", []))

        c, why = self.convert(r, curation(kinds={node: "toggle"}, positions={node: ["In", "Pulled"]}))

        self.assertIsNone(why)
        self.assertEqual((c["kind"], c["state_var"], c["release"], c["cw"], c["ccw"]),
                         ("toggle", "B757_APUFIRE_SWITCH", None, None, None))
        self.assertEqual([(p["value"], p["label"]) for p in c["positions"]], [(0, "In"), (1, "Pulled")])
        self.assertEqual(c["press"], APU_HANDLE_PRESS, "the vendor's click code is replayed verbatim")

    def test_a_toggle_made_by_curation_still_needs_a_state_var_and_keeps_the_safety_net(self):
        node = "BBS757_FIREHANDLE1_CLICK3"
        r = apu_fire_handle_row()
        r["_state"] = ["O:_AnimProgress"]
        self.assertIsNone(self.convert(r)[1], "a button needs no state var")
        # the entry asks for what the row cannot give: the run stops, the control is not quietly dropped
        with self.assertRaisesRegex(g.CurationError, "cannot be built from it: state is not an L:var"):
            self.convert(r, curation(kinds={node: "toggle"}))
        # a value the click code writes that the positions do not list still stops the run
        cur = curation(kinds={node: "toggle"}, positions={node: [{"value": 0, "label": "In"}, {"value": 2, "label": "Pulled"}]})
        with self.assertRaisesRegex(g.CurationError, "writes 1 .* outside its positions"):
            self.convert(apu_fire_handle_row(), cur)

    def test_a_curated_kind_turns_a_select_into_a_button_with_no_positions(self):
        # The audio mic selects: a press clears every microphone of the panel and sets this one, so "Off" can never be
        # picked and a combo of Off and On promises what the control cannot do. A button is pressed and says so.
        node = "B757_PED_AUDIO_1_MIC_L_LIGHT"
        press = " ".join(f"0 (>L:B757_PED_AUDIO_1_MIC_{m}_LIGHT, bool)" for m in ("L", "C", "R", "LM", "RR", "PA", "INT")) \
            + f" 1 (>L:{node}, bool)"
        r = row(node=node, kind="select button", positions="-", panel="INDIVIDUAL AUDIO/RADIO PANELS",
                state=["L:" + node], left=press)

        c, _ = self.convert(r)
        self.assertEqual((c["kind"], [p["label"] for p in c["positions"]]), ("select", ["Off", "On"]))

        c, why = self.convert(r, curation(kinds={node: "button"}))

        self.assertIsNone(why)
        self.assertEqual((c["kind"], c["state_var"], c["positions"], c["release"]), ("button", node, [], None))
        self.assertEqual(c["press"], press)

    def test_a_curated_kind_never_rescues_a_control_the_rules_drop(self):
        # The rules decide first, on the row as the extractor read it, in every family of drop; a kinds entry never
        # changes the answer. None of these comes back as the toggle or button curation asks for, and none stops the run.
        node = "B757_PED_X"
        cases = [
            ("excluded banner", row(node=node, kind="momentary button", positions="-", panel="CB OVERHEAD PANEL",
                                    left="1 (>L:X)"), {}),
            ("excluded node", row(node=node, kind="momentary button", positions="-", left="1 (>L:X)"),
             {"exclude_nodes": [node]}),
            ("lever", row(node=node, kind="lever", positions="", left="1 (>L:X)"), {}),
            ("rotary knob finite with more than 12 positions",
             row(node=node, kind="rotary knob finite", positions="range 0..39 step 1", cw="x", ccw="y"), {}),
            ("multi-position switch without positions",
             row(node=node, kind="multi-position switch", positions="", left="x"), {}),
            ("no click code", row(node=node, kind="toggle"), {}),
            ("state is not an L:var", row(node=node, kind="toggle", state=["A:SOMETHING"], left="1 (>K:SOMETHING)"), {}),
        ]
        for why, r, changes in cases:
            for wanted in ("toggle", "button"):
                with self.subTest(why=why, wanted=wanted):
                    self.assertEqual(self.convert(r, curation(**changes)), (None, why))
                    self.assertEqual(self.convert(r, curation(kinds={node: wanted}, **changes)), (None, why))

    def test_a_curated_kind_only_re_reads_what_it_was_made_for(self):
        # Two re-readings and no others, a momentary button as a toggle and a select as a button, so that "only the kind
        # changes" holds: a guard's state var, a held button's hold time and a switch's turn codes would change with it.
        node = "B757_PED_X"
        kept = {
            "toggle": row(node=node, kind="toggle", left="(L:X) ! (>L:X)"),
            "momentary button": row(node=node, kind="momentary button", positions="-", left="1 (>L:X)"),
            "select button": row(node=node, kind="select button", positions="-", left="1 (>L:X)"),
            "guard": row(node=node, kind="guard", left="(L:X) ! (>L:X)"),
            "held button": row(node=node, kind="held button", positions="-", left="1 (>L:X)", release="0 (>L:X)"),
            "rotary knob finite": row(node=node, kind="rotary knob finite", positions="3",
                                      cw="(L:X) 1 + 2 min (>L:X)", ccw="(L:X) 1 - 0 max (>L:X)"),
            "multi-position switch": row(node=node, kind="multi-position switch", positions="3",
                                         left="(L:X) 1 + 2 min (>L:X)", other=[("RightSingle", "(L:X) 1 - 0 max (>L:X)")]),
        }
        for read, wanted in (("momentary button", "toggle"), ("select button", "button")):
            with self.subTest(read=read, wanted=wanted):
                c, why = self.convert(kept[read], curation(kinds={node: wanted}))
                self.assertEqual((why, c["kind"]), (None, wanted))
        for read, wanted in (("toggle", "button"), ("select button", "toggle"), ("guard", "toggle"), ("guard", "button"),
                             ("held button", "toggle"), ("held button", "button"), ("rotary knob finite", "toggle"),
                             ("multi-position switch", "button")):
            with self.subTest(read=read, wanted=wanted):
                with self.assertRaisesRegex(g.CurationError, f"extractor read it as a {read}"):
                    self.convert(kept[read], curation(kinds={node: wanted}))
        for read, wanted in (("toggle", "toggle"), ("momentary button", "button")):
            with self.subTest(read=read, wanted=wanted, does="nothing"):
                with self.assertRaisesRegex(g.CurationError, "does nothing"):
                    self.convert(kept[read], curation(kinds={node: wanted}))


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
        self.assertTrue(all(list(c) == ["node", "section", "panel", "label", "kind", "state_var", "positions",
                                        "press", "release", "hold_ms", "cw", "ccw"] for c in m["controls"]))
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
                           ("kinds", {"B757_NO_SUCH_NODE": "button"}), ("exclude_nodes", ["B757_NO_SUCH_NODE"])):
            with self.subTest(key=key):
                with self.assertRaises(g.CurationError):
                    self.build(cur=curation(**{key: value}))

    def test_an_override_for_a_node_that_was_dropped_is_not(self):
        m, report = self.build(cur=curation(labels={"A2_CB": "Breaker"}))
        self.assertEqual(report["overrides_on_dropped"], ["A2_CB"])
        m, report = self.build(cur=curation(kinds={"A2_CB": "button"}))
        self.assertEqual(report["overrides_on_dropped"], ["A2_CB"])

    def test_a_curated_kind_changes_the_control_and_only_that_control(self):
        m, _ = self.build(cur=curation(kinds={"B757_MCP_SPD_BUTTON": "toggle"}))
        by_node = {c["node"]: c for c in m["controls"]}
        spd = by_node["B757_MCP_SPD_BUTTON"]
        self.assertEqual((spd["kind"], [p["label"] for p in spd["positions"]]), ("toggle", ["Off", "On"]))
        self.assertEqual(by_node["B757_OVHD_BATTERY_BUTTON"]["kind"], "toggle")  # untouched
        self.assertEqual(by_node["B757_OVHD_LIGHT_SWITCH"]["kind"], "toggle")

    def test_a_curated_kind_for_a_node_the_rules_drop_is_reported_and_changes_nothing(self):
        # Not an error (a package update may drop a control a curation entry was written for), and not a rescue either:
        # the control stays out, counted under the rule that dropped it, and the entry is named in the report.
        node = "B757_OVHD_HUGE_KNOB"
        rows = self.rows() + [row(node=node, kind="rotary knob finite", positions="range 0..39 step 1",
                                  panel="ELECTRICAL PANEL", cw="x", ccw="y")]
        m, report = self.build(rows, curation(kinds={node: "button"}))
        self.assertEqual(report["dropped"]["rotary knob finite with more than 12 positions"], 1)
        self.assertEqual(report["overrides_on_dropped"], [node])
        self.assertNotIn(node, [c["node"] for c in m["controls"]])

    def test_a_curated_kind_the_control_already_has_does_nothing_so_it_is_an_error(self):
        # An entry that changes nothing is a stale one: it would go on claiming a fix BlueBird's own code now makes.
        with self.assertRaisesRegex(g.CurationError, "does nothing"):
            self.build(cur=curation(kinds={"B757_OVHD_BATTERY_BUTTON": "toggle"}))
        with self.assertRaisesRegex(g.CurationError, "does nothing"):
            self.build(cur=curation(kinds={"B757_MCP_SPD_BUTTON": "button"}))

    def test_a_banner_with_no_panel_name_is_reported(self):
        rows = [row(node="B757_OVHD_X_BUTTON", panel="NEW VENDOR PANEL", left="x"),
                row(node="B757_OVHD_Y_BUTTON", panel="FUEL PANEL", left="y")]
        m, report = self.build(rows)
        self.assertEqual(report["unmapped_banners"], ["NEW VENDOR PANEL"])
        self.assertEqual(sorted(c["panel"] for c in m["controls"]), ["Fuel", "New Vendor Panel"])

    def test_a_node_rule_names_its_controls_so_their_banner_is_not_reported(self):
        cur = curation(node_panels=[{"prefix": "BBS_fuelcut_", "panel": "Fuel control", "section": "Pedestal"}])
        rows = [row(node="BBS_fuelcut_PW1", panel="(no banner)", kind="multi-position switch", positions="2",
                    left="(L:BBS_fuelcut_PW1) 2 + 2 min (>L:BBS_fuelcut_PW1)",
                    other=[("RightSingle", "(L:BBS_fuelcut_PW1) 2 - 0 max (>L:BBS_fuelcut_PW1)")]),
                row(node="B757_OVHD_NEW_BUTTON", panel="(no banner)", left="x")]
        m, report = self.build(rows, cur)
        self.assertEqual(report["unmapped_banners"], ["(no banner)"], "the control no rule names surfaces")
        self.assertIn(("Pedestal", "Fuel control"), [(c["section"], c["panel"]) for c in m["controls"]])
        self.assertEqual(len(m["controls"]), 2)
        _, report = self.build(rows[:1], cur)
        self.assertEqual(report["unmapped_banners"], [])

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


class IncludeNodes(unittest.TestCase):
    """A control under an excluded vendor banner is taken in by NODE (include_nodes), never by re-including the
    banner: the banner's other controls stay out. Panel and section come from a node_panels rule."""
    BANNER = "RIGHT WINDOW RAIN EFFECTS"
    BRAKE = "BBS757_PEDESTAL_Park_Brake_Lever"
    WIPER = "BBS757_RAIN_WIPER_TOGGLE"

    def cur(self, **changes):
        base = dict(exclude_banners=["CB OVERHEAD PANEL", self.BANNER],
                    include_nodes=[self.BRAKE],
                    node_panels=[{"prefix": "BBS757_PEDESTAL_", "panel": "Control stand", "section": "Pedestal"}])
        base.update(changes)
        return curation(**base)

    def rows(self):
        return [row(node=self.BRAKE, panel=self.BANNER, line=5, state=["L:B757_FC_PARK_BRAKE"],
                    left="(L:B757_FC_PARK_BRAKE,bool) ! (>L:B757_FC_PARK_BRAKE,bool)"),
                row(node=self.WIPER, panel=self.BANNER, line=6, left="(L:W,bool) ! (>L:W,bool)")]

    def build(self, rows=None, cur=None):
        return g.build_map(rows or self.rows(), cur or self.cur(), "pkg", "1")

    def test_a_listed_node_is_taken_in_and_the_rest_of_its_banner_stays_out(self):
        m, report = self.build()
        self.assertEqual([c["node"] for c in m["controls"]], [self.BRAKE])
        self.assertEqual(report["dropped"]["excluded banner"], 1)
        self.assertEqual(report["taken_in"], [self.BRAKE])

    def test_the_banner_itself_is_still_excluded(self):
        cur = self.cur()
        self.assertIn(self.BANNER, cur["exclude_banners"])
        control, why, _ = g.convert_row(self.rows()[1], cur)
        self.assertEqual((control, why), (None, "excluded banner"))
        control, why, _ = g.convert_row(self.rows()[0], cur)
        self.assertIsNone(why)
        self.assertEqual(control["node"], self.BRAKE)

    def test_without_the_entry_the_node_is_dropped_as_before(self):
        m, report = self.build(cur=self.cur(include_nodes=[]))
        self.assertEqual(m["controls"], [])
        self.assertEqual(report["dropped"]["excluded banner"], 2)
        self.assertEqual(report["taken_in"], [])

    def test_panel_and_section_come_from_a_node_panels_rule(self):
        m, report = self.build()
        self.assertEqual((m["controls"][0]["section"], m["controls"][0]["panel"]), ("Pedestal", "Control stand"))
        self.assertEqual(report["unmapped_banners"], [], "the rule names the control, so its banner is not a guess")

    def test_a_taken_in_node_keeps_the_vendors_click_code_verbatim(self):
        m, _ = self.build()
        self.assertEqual(m["controls"][0]["press"], "(L:B757_FC_PARK_BRAKE,bool) ! (>L:B757_FC_PARK_BRAKE,bool)")

    def test_a_node_the_package_does_not_have_is_an_error(self):
        with self.assertRaisesRegex(g.CurationError, "include_nodes name nodes the package does not have"):
            self.build(cur=self.cur(include_nodes=[self.BRAKE, "BBS757_NO_SUCH_NODE"]))

    def test_a_node_that_is_also_excluded_is_an_error(self):
        with self.assertRaisesRegex(g.CurationError, "include_nodes and exclude_nodes both name"):
            self.build(cur=self.cur(exclude_nodes=[self.BRAKE]))

    def test_a_node_no_excluded_banner_hides_is_an_error(self):
        rows = self.rows() + [row(node="B757_OVHD_BATTERY_BUTTON", panel="ELECTRICAL PANEL", left="(L:B) ! (>L:B)")]
        with self.assertRaisesRegex(g.CurationError, "no excluded banner hides"):
            self.build(rows, self.cur(include_nodes=[self.BRAKE, "B757_OVHD_BATTERY_BUTTON"]))

    def test_a_node_the_rules_still_leave_out_is_an_error(self):
        rows = [row(node=self.BRAKE, kind="lever", panel=self.BANNER, positions="", line=5)]
        with self.assertRaisesRegex(g.CurationError, r"still leave out: BBS757_PEDESTAL_Park_Brake_Lever \(lever\)"):
            self.build(rows)

    def test_a_taken_in_node_no_node_panels_rule_places_is_an_error(self):
        # Without a rule it would fall back to a panel named after the EXCLUDED banner ("Right Window Rain Effects").
        with self.assertRaisesRegex(g.CurationError,
                                    r"include_nodes name nodes no node_panels rule places: BBS757_PEDESTAL_Park_Brake_Lever"):
            self.build(cur=self.cur(node_panels=[]))
        with self.assertRaisesRegex(g.CurationError, "no node_panels rule places"):
            self.build(cur=self.cur(node_panels=[{"prefix": "BBS757_OTHER_", "panel": "Control stand"}]))

    def test_include_nodes_loads_as_a_list_and_defaults_to_empty(self):
        d = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, d, ignore_errors=True)
        path = os.path.join(d, "curation.json")
        for data, expected in (({}, []), ({"include_nodes": ["A"]}, ["A"])):
            with open(path, "w", encoding="utf-8") as fh:
                json.dump(data, fh)
            self.assertEqual(g.load_curation(path)["include_nodes"], expected)
        with open(path, "w", encoding="utf-8") as fh:
            json.dump({"include_nodes": {"A": 1}}, fh)
        with self.assertRaises(g.CurationError):
            g.load_curation(path)


class FlowOrder(unittest.TestCase):
    """panel_order and control_order (ruling R37): the order the pilot tabs through the panels of a section and, inside
    a panel, through the controls. What is not listed keeps the old order (panels alphabetical, controls in cockpit
    order), and labels are made before the order is applied."""
    PANELS = {"ELECTRICAL PANEL": "Electrical", "FUEL PANEL": "Fuel", "ICE PANEL": "Anti-ice", "ZULU PANEL": "Zulu",
              "MCP PANEL": {"name": "MCP", "section": "Glareshield"},
              "EFIS PANEL": {"name": "EFIS", "section": "Pedestal"}}
    BATTERY, GENERATOR, BUS_TIE = "B757_OVHD_BATTERY_BUTTON", "B757_OVHD_GENERATOR_BUTTON", "B757_OVHD_BUS_TIE_BUTTON"
    PUMP = "B757_OVHD_PUMP_BUTTON"

    def rows(self):
        """In cockpit order (the file line): Zulu, Fuel, Anti-ice, three Electrical controls, the MCP, the EFIS."""
        def r(node, banner, line):
            return row(node=node, panel=banner, line=line, left="x")
        return [
            r("B757_OVHD_ZULU_BUTTON", "ZULU PANEL", 1),
            r(self.PUMP, "FUEL PANEL", 2),
            r("B757_OVHD_ICE_BUTTON", "ICE PANEL", 3),
            r(self.BATTERY, "ELECTRICAL PANEL", 4),
            r(self.GENERATOR, "ELECTRICAL PANEL", 5),
            r(self.BUS_TIE, "ELECTRICAL PANEL", 6),
            r("B757_MCP_SPD_BUTTON", "MCP PANEL", 7),
            r("B757_PED_EFIS_BUTTON", "EFIS PANEL", 8),
        ]

    def build(self, rows=None, **changes):
        return g.build_map(rows or self.rows(), curation(panels=self.PANELS, **changes), "pkg", "1")

    @staticmethod
    def panels(m):
        """(section, panel) pairs in map order."""
        return list(dict.fromkeys((c["section"], c["panel"]) for c in m["controls"]))

    @staticmethod
    def nodes(m, panel):
        return [c["node"] for c in m["controls"] if c["panel"] == panel]

    def test_without_an_order_panels_are_alphabetical_and_controls_keep_cockpit_order(self):
        m, _ = self.build()

        self.assertEqual(self.panels(m), [("Overhead", "Anti-ice"), ("Overhead", "Electrical"), ("Overhead", "Fuel"),
                                          ("Overhead", "Zulu"), ("Glareshield", "MCP"), ("Pedestal", "EFIS")])
        self.assertEqual(self.nodes(m, "Electrical"), [self.BATTERY, self.GENERATOR, self.BUS_TIE])

    def test_listed_panels_come_first_in_the_listed_order_and_the_rest_follow_alphabetically(self):
        m, _ = self.build(panel_order={"Overhead": ["Zulu", "Electrical"]})

        self.assertEqual([p for s, p in self.panels(m) if s == "Overhead"], ["Zulu", "Electrical", "Anti-ice", "Fuel"])

    def test_a_listed_panel_moves_ahead_of_the_alphabet_only_inside_its_own_section(self):
        m, _ = self.build(panel_order={"Pedestal": ["EFIS"], "Overhead": ["Fuel"]})

        self.assertEqual(list(dict.fromkeys(c["section"] for c in m["controls"])), ["Overhead", "Glareshield", "Pedestal"])
        self.assertEqual([p for s, p in self.panels(m) if s == "Overhead"], ["Fuel", "Anti-ice", "Electrical", "Zulu"])

    def test_a_panel_order_leaves_the_controls_inside_each_panel_alone(self):
        m, _ = self.build(panel_order={"Overhead": ["Electrical"]})

        self.assertEqual(self.nodes(m, "Electrical"), [self.BATTERY, self.GENERATOR, self.BUS_TIE])

    def test_listed_controls_come_first_in_the_listed_order_and_the_rest_keep_cockpit_order(self):
        m, _ = self.build(control_order={"Electrical": [self.BUS_TIE]})
        self.assertEqual(self.nodes(m, "Electrical"), [self.BUS_TIE, self.BATTERY, self.GENERATOR])

        m, _ = self.build(control_order={"Electrical": [self.GENERATOR, self.BUS_TIE]})
        self.assertEqual(self.nodes(m, "Electrical"), [self.GENERATOR, self.BUS_TIE, self.BATTERY])

    def test_a_control_order_leaves_the_panels_where_they_are(self):
        plain, _ = self.build()
        ordered, _ = self.build(control_order={"Electrical": [self.BUS_TIE]})

        self.assertEqual(self.panels(ordered), self.panels(plain))
        self.assertEqual(self.nodes(ordered, "Fuel"), self.nodes(plain, "Fuel"))

    def test_the_two_orders_work_together(self):
        m, _ = self.build(panel_order={"Overhead": ["Electrical", "Fuel"]},
                          control_order={"Electrical": [self.GENERATOR], "Fuel": [self.PUMP]})

        self.assertEqual([c["node"] for c in m["controls"]][:5],
                         [self.GENERATOR, self.BATTERY, self.BUS_TIE, self.PUMP, "B757_OVHD_ICE_BUTTON"])

    def test_labels_are_made_in_cockpit_order_whatever_the_order(self):
        # Two controls called "Light": the second in the cockpit is "Light 2", also when the order puts it first.
        rows = [row(node="B757_OVHD_A_BUTTON", panel="ELECTRICAL PANEL", line=1, left="x"),
                row(node="B757_OVHD_B_BUTTON", panel="ELECTRICAL PANEL", line=2, left="x")]
        m, _ = self.build(rows, labels={"B757_OVHD_A_BUTTON": "Light", "B757_OVHD_B_BUTTON": "Light"},
                          control_order={"Electrical": ["B757_OVHD_B_BUTTON"]})

        self.assertEqual([(c["node"], c["label"]) for c in m["controls"]],
                         [("B757_OVHD_B_BUTTON", "Light 2"), ("B757_OVHD_A_BUTTON", "Light")])

    def test_the_report_names_every_panel_panel_order_does_not_list(self):
        _, report = self.build(panel_order={"Overhead": ["Zulu", "Electrical"]})
        self.assertEqual(report["unordered_panels"],
                         ["Overhead / Anti-ice", "Overhead / Fuel", "Glareshield / MCP", "Pedestal / EFIS"])

        _, report = self.build(panel_order={"Overhead": ["Zulu", "Electrical", "Anti-ice", "Fuel"],
                                            "Glareshield": ["MCP"], "Pedestal": ["EFIS"]})
        self.assertEqual(report["unordered_panels"], [])

    def test_a_panel_the_section_does_not_have_is_an_error(self):
        with self.assertRaisesRegex(g.CurationError, "panel_order names panels Overhead does not have: Ghost"):
            self.build(panel_order={"Overhead": ["Electrical", "Ghost"]})
        # a panel of another section is not this section's
        with self.assertRaisesRegex(g.CurationError, "panel_order names panels Overhead does not have: MCP"):
            self.build(panel_order={"Overhead": ["MCP"]})

    def test_a_panel_listed_twice_is_an_error(self):
        with self.assertRaisesRegex(g.CurationError, "panel_order lists a panel twice in Overhead: Fuel"):
            self.build(panel_order={"Overhead": ["Fuel", "Electrical", "Fuel"]})

    def test_an_unknown_section_is_an_error(self):
        with self.assertRaisesRegex(g.CurationError, "panel_order names a section the map does not have: Cockpit"):
            self.build(panel_order={"Cockpit": ["Fuel"]})

    def test_an_empty_list_does_nothing_so_it_is_an_error(self):
        with self.assertRaisesRegex(g.CurationError, "panel_order lists no panels for Overhead"):
            self.build(panel_order={"Overhead": []})
        with self.assertRaisesRegex(g.CurationError, "control_order lists no nodes for Electrical"):
            self.build(control_order={"Electrical": []})

    def test_a_control_order_for_a_panel_no_control_is_on_is_an_error(self):
        with self.assertRaisesRegex(g.CurationError, "control_order names a panel no control is on: Ghost"):
            self.build(control_order={"Ghost": [self.BATTERY]})

    def test_a_node_that_is_not_in_the_panel_is_an_error_that_says_where_it_is(self):
        with self.assertRaisesRegex(g.CurationError,
                                    r"control_order lists nodes that are not in Electrical: "
                                    r"B757_OVHD_PUMP_BUTTON \(in Overhead / Fuel\), B757_NO_SUCH_NODE \(not in the map\)"):
            self.build(control_order={"Electrical": [self.BATTERY, self.PUMP, "B757_NO_SUCH_NODE"]})

    def test_a_node_listed_twice_is_an_error(self):
        with self.assertRaisesRegex(g.CurationError, f"control_order lists a node twice in Electrical: {self.BATTERY}"):
            self.build(control_order={"Electrical": [self.BATTERY, self.GENERATOR, self.BATTERY]})

    def test_a_panel_name_in_two_sections_cannot_be_ordered_by_name(self):
        panels = dict(self.PANELS, **{"EFIS TWO": {"name": "EFIS", "section": "Forward panel"}})
        rows = self.rows() + [row(node="B757_MAIN_EFIS_BUTTON", panel="EFIS TWO", line=9, left="x")]
        with self.assertRaisesRegex(g.CurationError, r"control_order names EFIS, which is in more than one section "
                                                     r"\(Forward panel, Pedestal\)"):
            g.build_map(rows, curation(panels=panels, control_order={"EFIS": ["B757_PED_EFIS_BUTTON"]}), "pkg", "1")

    def test_a_node_rule_with_a_section_moves_an_overhead_node_to_another_section(self):
        # B757_OVHD_ nodes land in the Overhead by their prefix; the Engines panel on the Pedestal says its section.
        rule = {"banner": "LIGHT OVERRIDE", "prefix": "B757_OVHD_ENG_START", "panel": "Engines", "section": "Pedestal"}
        start = row(node="B757_OVHD_ENG_START_SWITCH", panel="LIGHT OVERRIDE", left="x")

        self.assertEqual(g.panel_of(start, curation(node_panels=[rule])), ("Engines", "Pedestal"))
        without_section = {k: v for k, v in rule.items() if k != "section"}
        self.assertEqual(g.panel_of(start, curation(node_panels=[without_section])), ("Engines", "Overhead"))


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

    def test_the_order_keys_load_as_dicts_and_default_to_empty(self):
        c = g.load_curation(self.write({}))
        self.assertEqual((c["panel_order"], c["control_order"]), ({}, {}))

        c = g.load_curation(self.write({"panel_order": {"Overhead": ["Fuel"]}, "control_order": {"Fuel": ["B757_X"]}}))
        self.assertEqual((c["panel_order"], c["control_order"]), ({"Overhead": ["Fuel"]}, {"Fuel": ["B757_X"]}))

    def test_an_order_that_is_not_a_dict_of_lists_of_names_is_refused(self):
        for data in ({"panel_order": ["Fuel"]}, {"panel_order": {"Overhead": "Fuel"}}, {"panel_order": {"Overhead": [1]}},
                     {"control_order": ["B757_X"]}, {"control_order": {"Fuel": "B757_X"}},
                     {"control_order": {"Fuel": [None]}}):
            with self.subTest(data=data):
                with self.assertRaises(g.CurationError):
                    g.load_curation(self.write(data))

    def test_the_kinds_key_loads_as_a_dict_and_defaults_to_empty(self):
        self.assertEqual(g.load_curation(self.write({}))["kinds"], {})
        c = g.load_curation(self.write({"kinds": {"B757_X": "button", "B757_Y": "toggle"}}))
        self.assertEqual(c["kinds"], {"B757_X": "button", "B757_Y": "toggle"})

    def test_a_kind_curation_may_only_name_toggle_or_button(self):
        # Only these two are built entirely by what the generator already does for them: a switch needs its values and
        # a guard its cover; a kind it cannot build is refused when the file loads, never half-applied.
        for data in ({"kinds": ["B757_X"]}, {"kinds": {"B757_X": "switch"}}, {"kinds": {"B757_X": "guard"}},
                     {"kinds": {"B757_X": "select"}}, {"kinds": {"B757_X": "held"}}, {"kinds": {"B757_X": "lever"}},
                     {"kinds": {"B757_X": None}}, {"kinds": {"B757_X": 1}}):
            with self.subTest(data=data):
                with self.assertRaises(g.CurationError):
                    g.load_curation(self.write(data))

    def test_the_shipped_curation_loads(self):
        c = g.load_curation(g.DEFAULT_CURATION)
        for banner in ("CB OVERHEAD PANEL", "ADDING L VARS for CDU switches", "FO CDU CODE", "COCKPIT CAPTAIN CHAIR",
                       "RIGHT WINDOW RAIN EFFECTS", "MISC PARTS", "THROTTLES MAIN", "RUDDER TRIM_LIMIT",
                       "EMISSIVE GAUGES"):
            self.assertIn(banner, c["exclude_banners"])
        keys = [s["key"] for s in c["status"]]
        self.assertEqual(keys, ["B757_APU_STATE", "BBS_CA_MASTER_WARNING", "BBS_CA_MASTER_CAUTION",
                                "B757_L_BUS_PWR_SRC", "B757_R_BUS_PWR_SRC", "B757_APU_RPM_PRC",
                                "B757_DEBUG_PRESSURE_CABIN_ALT_FT", "BBS757_vis_chocks"])
        self.assertEqual([s["announce"] for s in c["status"]], [True, True, True, False, False, False, False, False])
        for banner, entry in c["panels"].items():  # the vendor's typos never reach a pilot
            name = entry if isinstance(entry, str) else entry["name"]
            self.assertNotRegex(name, "GAURD|FISRT|Gaurd|Fisrt")
        self.assertTrue({"B757_MCP_AP_ALT_SEL", "B757_OVHD_AUTORATE_SWITCH", "BBS757_FIREHANDLE1_CLICK"}
                        <= set(c["labels"]))

    def test_the_shipped_curation_leaves_no_banner_to_a_guess(self):
        c = g.load_curation(g.DEFAULT_CURATION)
        self.assertNotIn("(no banner)", c["panels"])
        self.assertTrue(any(r.get("prefix") == "BBS_fuelcut_" for r in c["node_panels"]))
        self.assertIn("B757_OVHD_CABINALT_SWITCH", c["exclude_nodes"])
        self.assertNotIn("B757_OVHD_CABINALT_SWITCH", c["positions"])

    CONTROL_STAND = {
        "BBS757_PEDESTAL_Park_Brake_Lever": "RIGHT WINDOW RAIN EFFECTS",
        "BBS757_SPOILERDOWN_CLICK": "RIGHT WINDOW RAIN EFFECTS",
        "BBS757_SPOILERARMED_CLICK": "RIGHT WINDOW RAIN EFFECTS",
        "BBS757_SPOILERUP_CLICK": "RIGHT WINDOW RAIN EFFECTS",
        "ENGINE_Switch_ATDisc_1": "THROTTLES MAIN",
        "B757_AT_DISC_1": "THROTTLES MAIN",
    }

    def test_the_shipped_curation_takes_in_the_control_stand_by_node_and_keeps_the_banners_out(self):
        c = g.load_curation(g.DEFAULT_CURATION)
        self.assertEqual(sorted(c["include_nodes"]), sorted(self.CONTROL_STAND))
        for node, banner in self.CONTROL_STAND.items():
            with self.subTest(node=node):
                self.assertIn(banner, c["exclude_banners"])
                self.assertEqual(g.panel_of(row(node=node, panel=banner), c), ("Control stand", "Pedestal"))
        # only the left TO/GA and the first A/T disconnect: the right-hand ones are not taken in
        self.assertNotIn("ENGINE_Switch_ATDisc_2", c["include_nodes"])
        self.assertNotIn("B757_AT_DISC_2", c["include_nodes"])

    def control_stand_rows(self):
        flip = "(L:B757_FC_PARK_BRAKE,bool) ! (>L:B757_FC_PARK_BRAKE,bool)"
        rows = {
            "BBS757_PEDESTAL_Park_Brake_Lever": row(
                node="BBS757_PEDESTAL_Park_Brake_Lever", kind="toggle", positions="2", panel="RIGHT WINDOW RAIN EFFECTS",
                state=["L:B757_FC_PARK_BRAKE"], tips=["Parking Brake"], left=flip),
            "ENGINE_Switch_ATDisc_1": row(
                node="ENGINE_Switch_ATDisc_1", kind="momentary button", positions="-", panel="THROTTLES MAIN",
                state=["L:GO_AROUND_PADDLE1"],
                left="1 (>L:GO_AROUND_PADDLE1, bool) 1 (>K:AUTO_THROTTLE_TO_GA)", release="0 (>L:GO_AROUND_PADDLE1, bool)"),
            "B757_AT_DISC_1": row(
                node="B757_AT_DISC_1", kind="momentary button", positions="-", panel="THROTTLES MAIN",
                state=["L:B757_AT_DISC_1"], left="1 (>L:B757_AT_DISC_1, bool)", release="0 (>L:B757_AT_DISC_1, bool)"),
        }
        for node, value in (("BBS757_SPOILERDOWN_CLICK", 0), ("BBS757_SPOILERARMED_CLICK", 10), ("BBS757_SPOILERUP_CLICK", 100)):
            rows[node] = row(node=node, kind="momentary button", positions="-", panel="RIGHT WINDOW RAIN EFFECTS",
                             state=[], left=f"{value} (>L:B757_FC_SPOILER_LEVER, number)")
        return rows

    def test_the_shipped_curation_names_and_converts_the_control_stand(self):
        c = g.load_curation(g.DEFAULT_CURATION)
        rows = self.control_stand_rows()
        expected = {
            "BBS757_PEDESTAL_Park_Brake_Lever": ("Parking Brake", "toggle", ["Released", "Set"]),
            "BBS757_SPOILERDOWN_CLICK": ("Speedbrake Down", "button", []),
            "BBS757_SPOILERARMED_CLICK": ("Speedbrake Armed", "button", []),
            "BBS757_SPOILERUP_CLICK": ("Speedbrake Up", "button", []),
            "ENGINE_Switch_ATDisc_1": ("TO/GA", "button", []),
            "B757_AT_DISC_1": ("A/T Disconnect", "button", []),
        }
        for node, (label, kind, positions) in expected.items():
            with self.subTest(node=node):
                control, why, slips = g.convert_row(rows[node], c)
                self.assertIsNone(why)
                self.assertEqual((control["label"], control["kind"], [p["label"] for p in control["positions"]]),
                                 (label, kind, positions))
                self.assertEqual((control["section"], control["panel"], slips), ("Pedestal", "Control stand", []))
                self.assertEqual(control["press"], rows[node]["_clicks"]["left"][0][1], "the click code is verbatim")
        toga, _, _ = g.convert_row(rows["ENGINE_Switch_ATDisc_1"], c)
        self.assertEqual(toga["release"], "0 (>L:GO_AROUND_PADDLE1, bool)")
        brake, _, _ = g.convert_row(rows["BBS757_PEDESTAL_Park_Brake_Lever"], c)
        self.assertEqual([(p["value"], p["label"]) for p in brake["positions"]], [(0, "Released"), (1, "Set")])

    def test_the_shipped_curation_labels_the_two_hold_buttons_by_what_they_hold(self):
        # B757_MCP_HOLD_1 / _2: the vendor's disabled original component fires H:BBS_AP_HDG_HOLD / H:BBS_AP_ALT_HOLD
        c = g.load_curation(g.DEFAULT_CURATION)
        self.assertEqual(c["labels"]["B757_MCP_HOLD_1_BUTTON"], "HDG HOLD")
        self.assertEqual(c["labels"]["B757_MCP_HOLD_2_BUTTON"], "ALT HOLD")

    def test_the_shipped_curation_names_the_three_cmd_buttons_left_center_and_right(self):
        # The 757's autopilots are L, C and R; the vendor numbers their nodes 1 to 3 (the app speaks "CMD R on").
        c = g.load_curation(g.DEFAULT_CURATION)
        self.assertEqual([c["labels"][f"B757_MCP_CMD_{n}_BUTTON"] for n in (1, 2, 3)], ["CMD L", "CMD C", "CMD R"])

    # Rows in the shape of the vendor's, so the shipped position lists are checked by the generator's own safety net
    # (every value the click code writes must be one of the positions).
    def knob(self, node, state, count, banner="MAIN PANEL SWITCH GEAR + BUTTONS"):
        return row(node=node, kind="rotary knob finite", positions=str(count), panel=banner, state=["L:" + state],
                   cw=f"(L:{state}, enum) 1 + {count - 1} min (>L:{state})",
                   ccw=f"(L:{state}, enum) 1 - 0 max (>L:{state})")

    def test_the_shipped_curation_names_the_positions_the_evidence_supports(self):
        c = g.load_curation(g.DEFAULT_CURATION)
        n = lambda *labels: [(i, l) for i, l in enumerate(labels)]
        ranges = n("10 NM", "20 NM", "40 NM", "80 NM", "160 NM", "320 NM")
        modes = n("VOR", "APP", "MAP", "PLAN")
        irs = n("OFF", "ALIGN", "NAV", "ATT")
        cases = [
            (self.knob("B757_MAIN_knob_gears_altn_autobrake", "BBS_AUTOBRAKE_KNOB", 8),
             n("RTO", "OFF", "DISARM", "1", "2", "3", "4", "MAX AUTO")),
            # the alternate flaps selector and the TCAS mode knob are named by BlueBird's list: test_bluebird_position_names
            (self.knob("B757_ADI_1_TFC_KNOB", "BBS_ND1_Range_Index", 6, "EHSI/ADI"), ranges),
            (self.knob("B757_ADI_2_TFC_KNOB", "BBS_ND2_Range_Index", 6, "EHSI/ADI"), ranges),
            (self.knob("B757_ADI_1_CTR_KNOB", "B757_ADI_1_CTR_KNOB", 4, "EHSI/ADI"), modes),
            (self.knob("B757_ADI_2_CTR_KNOB", "B757_ADI_2_CTR_KNOB", 4, "EHSI/ADI"), modes),
            (self.knob("B757_IRS_L_SWITCH", "B757_IRS_L_SWITCH", 4, "IRS KNOBS"), irs),
            (self.knob("B757_IRS_C_SWITCH", "B757_IRS_C_SWITCH", 4, "IRS KNOBS"), irs),
            (self.knob("B757_IRS_R_SWITCH", "B757_IRS_R_SWITCH", 4, "IRS KNOBS"), irs),
        ]
        for r, names in cases:
            with self.subTest(node=r["node"]):
                control, why, _ = g.convert_row(r, c)
                self.assertIsNone(why)
                self.assertEqual([(p["value"], p["label"]) for p in control["positions"]], names)
        # the PW fuel control switches rest at 0 and 2 (the vendor's panel states): CUTOFF and RUN
        for node in ("BBS_fuelcut_PW1", "BBS_fuelcut_PW2"):
            r = row(node=node, kind="multi-position switch", positions="2", panel="(no banner)", state=["L:" + node],
                    left=f"(L:{node}) 2 + 2 min (>L:{node})", other=[("RightSingle", f"(L:{node}) 2 - 0 max (>L:{node})")])
            with self.subTest(node=node):
                control, why, _ = g.convert_row(r, c)
                self.assertIsNone(why)
                self.assertEqual([(p["value"], p["label"]) for p in control["positions"]], [(0, "CUTOFF"), (2, "RUN")])
        r = row(node="B757_PED_TCAS_XPNDR_SWITCH", kind="toggle", positions="2", panel="INDIVIDUAL AUDIO/RADIO PANELS",
                state=["L:B757_PED_TCAS_XPNDR_SWITCH"], tips=["XPNDR"],
                left="(L:B757_PED_TCAS_XPNDR_SWITCH,bool) ! (>L:B757_PED_TCAS_XPNDR_SWITCH,bool)")
        control, _, _ = g.convert_row(r, c)
        self.assertEqual([(p["value"], p["label"]) for p in control["positions"]], [(0, "L"), (1, "R")])

    # Plan 02, task 1: every position BlueBird's simvar list names, by node, as (value, label) in the control's own value
    # order. The values are the control's real ones, not 0, 1, 2 ...: the engine start selectors never write 2, the
    # warning indicator rests at 0 and 11, and the chronometer ET lever's code jumps from HOLD (1) straight to 3.
    BLUEBIRD_POSITIONS = {
        "B757_IRS_CENTRE_KNOB": [(0, "L"), (1, "C"), (2, "R")],
        "B757_IRS_LARGE_KNOB": [(0, "TK/GS"), (1, "PPOS"), (2, "WIND"), (3, "HDG/STS")],
        "B757_OVHD_WIPER_SWITCH": [(0, "OFF"), (1, "LOW"), (2, "HIGH")],
        "B757_OVHD_SEATBELTS_SWITCH": [(0, "OFF"), (1, "AUTO"), (2, "ON")],
        "B757_OVHD_NO_SMOKING_SWITCH": [(0, "OFF"), (1, "AUTO"), (2, "ON")],
        "B757_OVHD_MODE_SELECT_SWITCH": [(0, "AUTO 1"), (1, "AUTO 2"), (2, "MAN")],
        "B757_OVHD_SEL_L_PACK_SWITCH": [(0, "OFF"), (1, "AUTO"), (2, "STBY N"), (3, "STBY C"), (4, "STBY W")],
        "B757_OVHD_SEL_R_PACK_SWITCH": [(0, "OFF"), (1, "AUTO"), (2, "STBY N"), (3, "STBY C"), (4, "STBY W")],
        "B757_CABIN_CEILING": [(0, "Off"), (1, "33%"), (2, "66%"), (3, "100%")],
        "B757_CABIN_WINDOW": [(0, "Off"), (1, "50%"), (2, "100%")],
        "B757_CABIN_ENTRY": [(0, "Off"), (1, "50%"), (2, "100%")],
        "B757_OVHD_WARNING_IND_SWITCH": [(0, "DIM"), (11, "BRT")],
        "B757_MCP_AP_HDG_BNKLIM": [(0, "AUTO"), (1, "5"), (2, "10"), (3, "15"), (4, "20"), (5, "25")],
        "B757_MAIN_knob_gears_altn_flaps": [(0, "NORM"), (1, "UP"), (2, "1"), (3, "5"), (4, "15"), (5, "20"), (6, "25"),
                                            (7, "30")],
        "B757_MAIN_M_Gau_STBY_GYRO_SELECTOR": [(0, "OFF"), (1, "ILS"), (2, "BCRS")],
        "B757_MAIN_CHRONO_ET_SWITCH": [(0, "RUN"), (1, "HOLD"), (3, "RESET")],
        "B757_MAIN_CHRONO_ET_SWITCH_FO": [(0, "RUN"), (1, "HOLD"), (3, "RESET")],
        "B757_MAIN_CHRONO_SET_SWITCH": [(0, "RUN"), (1, "HOLD"), (2, "Slow slew"), (3, "Fast slew")],
        "B757_MAIN_CHRONO_SET_SWITCH_FO": [(0, "RUN"), (1, "HOLD"), (2, "Slow slew"), (3, "Fast slew")],
        "B757_OVHD_ENG_START_SWITCH": [(0, "1"), (1, "2"), (2, "BOTH")],
        "B757_OVHD_L_ENG_START_SWITCH": [(1, "GND"), (3, "AUTO"), (4, "OFF"), (5, "CONT"), (6, "FLT")],
        "B757_OVHD_R_ENG_START_SWITCH": [(1, "GND"), (3, "AUTO"), (4, "OFF"), (5, "CONT"), (6, "FLT")],
        "B757_PED_TCAS_MODE_KNOB": [(0, "STBY"), (1, "ALT RPTG OFF"), (2, "XPNDR"), (3, "TA ONLY"), (4, "TA/RA")],
        "B757_EICAS_LWR_THRUST-REF_KNOB": [(0, "L"), (1, "BOTH"), (2, "R")],
        "B757_ECAM_LRAUTO_KNOB": [(0, "L"), (1, "AUTO"), (2, "R")],
        "B757_VC_P61_SWITCH_2": [(0, "ON"), (1, "AUTO"), (2, "TEST")],
        # the P61 test switches: 1 is neutral on all, 0 and 2 are the two tests (BlueBird's list, by the switch's label)
        "B757_VC_P61_SWITCH_3": [(0, "Upper test"), (1, "Neutral"), (2, "Lower test")],
        "B757_VC_P61_SWITCH_4": [(0, "Upper test"), (1, "Neutral"), (2, "Lower test")],
        "B757_VC_P61_SWITCH_5": [(0, "Position 0"), (1, "Neutral"), (2, "Position 2")],  # Duct Leak Fuel Qty: no names
        "B757_VC_P61_SWITCH_6": [(0, "Wing anti-ice test"), (1, "Neutral"), (2, "Probe heat test")],
        "B757_VC_P61_SWITCH_7": [(0, "Ground proximity test"), (1, "Neutral"), (2, "Hydraulic generator test")],
        "B757_VC_P61_SWITCH_8": [(0, "Takeoff config test"), (1, "Neutral"), (2, "Landing config test")],
        "B757_VC_P61_SWITCH_9": [(0, "Air data test"), (1, "Neutral"), (2, "Stall warning test")],
        "B757_VC_P61_SWITCH_10": [(0, "Air data test"), (1, "Neutral"), (2, "Stall warning test")],
    }

    def bluebird_rows(self):
        """One extractor-shaped row per node in BLUEBIRD_POSITIONS. A knob that counts up from 0 has as many positions
        as it has names; the controls whose values are not simply 0 to n-1, and the P61 switches, carry the vendor's
        own click code, as the generated map holds it."""
        rows = {node: self.knob(node, node, len(names)) for node, names in self.BLUEBIRD_POSITIONS.items()}

        node = "B757_OVHD_WARNING_IND_SWITCH"
        rows[node] = row(node=node, kind="rotary knob finite", positions="2", state=["L:" + node],
                         cw=f"11 (>L:{node})", ccw=f"0 (>L:{node})")
        for node in ("B757_OVHD_L_ENG_START_SWITCH", "B757_OVHD_R_ENG_START_SWITCH"):  # the extractor counts 6
            rows[node] = row(
                node=node, kind="rotary knob finite", positions="6", state=["L:" + node],
                cw=f"(L:{node}, enum) 1 == if{{ (L:{node}, enum) 2 + (>L:{node}) }} "
                   f"els{{ (L:{node}, enum) 1 + 6 min (>L:{node}) }}",
                ccw=f"(L:{node}, enum) 3 == if{{ (L:{node}, enum) 2 - (>L:{node}) }} "
                    f"els{{ (L:{node}, enum) 1 - 1 max (>L:{node}) }}")
        for node, var in (("B757_MAIN_CHRONO_ET_SWITCH", "BBS_CHRONO_LEFT_SWITCH"),
                          ("B757_MAIN_CHRONO_ET_SWITCH_FO", "BBS_CHRONO_LEFT_SWITCH_FO")):  # the extractor counts 4
            rows[node] = row(
                node=node, kind="rotary knob finite", positions="4", state=["L:" + var],
                cw=f"(L:{var}, enum) -- d 0 < if{{ 0 }} (>L:{var}, enum)",
                ccw=f"(L:{var}, enum) 1 == if{{ 3 (>L:{var}, enum) }} "
                    f"els{{ (L:{var}, enum) ++ d 3 > if{{ 3 }} (>L:{var}, enum) }}")
        for node, var in (("B757_MAIN_CHRONO_SET_SWITCH", "BBS_CHRONO_RIGHT_SWITCH"),
                          ("B757_MAIN_CHRONO_SET_SWITCH_FO", "BBS_CHRONO_RIGHT_SWITCH_FO")):
            rows[node] = row(node=node, kind="rotary knob finite", positions="4", state=["L:" + var],
                             cw=f"(L:{var}, enum) ++ d 3 > if{{ 3 }} (>L:{var}, enum)",
                             ccw=f"(L:{var}, enum) -- d 0 < if{{ 0 }} (>L:{var}, enum)")
        node, var = "B757_VC_P61_SWITCH_2", "BBS757_FLT_REC_SWITCH"
        rows[node] = row(node=node, kind="multi-position switch", positions="3", state=["L:" + var],
                         left=f"(L:{var}) 1 + 2 min (>L:{var})",
                         other=[("RightSingle", f"(L:{var}) 1 - 0 max (>L:{var})")])
        for n in range(3, 11):  # no click of their own: the release puts the switch back to 1
            node = f"B757_VC_P61_SWITCH_{n}"
            rows[node] = row(node=node, kind="multi-position switch", positions="3", state=[f"L:{node}_TEST_SW"],
                             release=f"1 (>L:{node}_TEST_SW, number)")
        return rows

    def test_bluebird_position_names(self):
        c = g.load_curation(g.DEFAULT_CURATION)
        rows = self.bluebird_rows()

        self.assertEqual(set(rows), set(self.BLUEBIRD_POSITIONS))
        for node, expected in self.BLUEBIRD_POSITIONS.items():
            with self.subTest(node=node):
                control, why, _ = g.convert_row(rows[node], c)
                self.assertIsNone(why)
                self.assertEqual([(p["value"], p["label"]) for p in control["positions"]], expected)

    def test_every_curated_position_list_but_the_unpublished_one_has_no_numbered_name(self):
        # BlueBird names every position the curation lists; the Duct Leak Fuel Qty test switch is the one control whose 0
        # and 2 no source names (its 1 is neutral, like the other test switches), so its list keeps "Position n".
        c = g.load_curation(g.DEFAULT_CURATION)
        numbered = {}
        for node, entry in c["positions"].items():
            labels = [p["label"] if isinstance(p, dict) else p for p in entry]
            hits = [label for label in labels if re.fullmatch(r"Position \d+", label)]
            if hits:
                numbered[node] = hits
        self.assertEqual(numbered, {"B757_VC_P61_SWITCH_5": ["Position 0", "Position 2"]})

    # Plan 02, task 2: the labels, kinds and positions that match the cockpit, by node. Each row has the shape the
    # extractor gives the vendor's own control (banner, tips, state var and click code as in BBS_752_BASE_INTERIOR.xml)
    # and is converted with the SHIPPED curation. node -> (label, kind, [(value, label)]).
    OFF_ON = [(0, "Off"), (1, "On")]
    CLOSED_OPEN = [(0, "Closed"), (1, "Open")]
    LABEL_CORRECTIONS = {
        # the guard over the emergency lights switch (BBS_B757_EMERGENCY_LIGHTS_COMPONENT), not the passenger oxygen
        "B757_OVHD_SWITCH_GAURD": ("Emergency Lights Guard", "guard", CLOSED_OPEN),
        # the hydraulic panel: two engine pumps, and the four electric pumps (the vendor's names for the center pair say
        # ENG1 and ENG2 and HYD, and for the side pair ENG ELEC)
        "B757_OVHD_L_ENG_HYD_PUMPS_BUTTON": ("L ENG HYD Pumps", "toggle", OFF_ON),
        "B757_OVHD_R_ENG_HYD_PUMPS_BUTTON": ("R ENG HYD Pumps", "toggle", OFF_ON),
        "B757_OVHD_R_ENG_ELEC_PUMPS_BUTTON": ("R ELEC Pump", "toggle", OFF_ON),
        "B757_OVHD_C_ENG1_HYD_PUMPS_BUTTON": ("C1 ELEC Pump", "toggle", OFF_ON),
        "B757_OVHD_C_ENG2_HYD_PUMPS_BUTTON": ("C2 ELEC Pump", "toggle", OFF_ON),
        "B757_OVHD_L_ENG_ELEC_PUMPS_BUTTON": ("L ELEC Pump", "toggle", OFF_ON),
        # the crossfeed valves (the vendor's names and tips say pumps)
        "B757_OVHD_XFEED_PUMPS_FWD_BUTTON": ("Crossfeed FWD", "toggle", OFF_ON),
        "B757_OVHD_XFEED_PUMPS_AFT_BUTTON": ("Crossfeed Aft", "toggle", OFF_ON),
        # the guards of the two cargo fire bottle buttons (LVAR_DOWN of their template instances)
        "B757_GAURD_11_SWITCH": ("1 BTL Cargo Fire Guard", "guard", CLOSED_OPEN),
        "B757_GAURD_12_SWITCH": ("2 BTL Cargo Fire Guard", "guard", CLOSED_OPEN),
        "B757_MCP_btn_autopilot_disengage": ("Autopilot Disengage Bar", "toggle", [(0, "Up"), (1, "Down")]),
        "BBS757_THRREF_CLICK": ("Thrust Ref Pull", "toggle", [(0, "In"), (1, "Pulled")]),
        "B757_MCP_AP_SPD_HOLD_SW": ("Speed Intervene", "button", []),
        # a toggle on the pulled state, as the engine handles are, so the generic path reads it before it presses
        "BBS757_FIREHANDLE1_CLICK3": ("APU Fire Handle", "toggle", [(0, "In"), (1, "Pulled")]),
    }

    def correction_rows(self):
        def flip(var):
            return f"(L:{var},bool) ! (>L:{var},bool)"

        def toggle(node, banner, tips):
            return row(node=node, kind="toggle", positions="2", panel=banner, state=["L:" + node], tips=list(tips),
                       left=flip(node))

        def guard(node, banner, left, covers=None):
            return row(node=node, kind="guard", positions="2", panel=banner, state=["L:" + node], left=left,
                       scope={"LVAR_DOWN": covers} if covers else None)

        emergency = "B757_OVHD_SWITCH_GAURD"
        rows = {
            emergency: guard(emergency, "LIGHT OVERRIDE",
                             f"(L:{emergency}, bool) ! (>L:{emergency}, bool) (L:{emergency}, bool) 0 == "
                             f"if{{ 1 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK, number) }}"),
        }
        for node, tips in (("B757_OVHD_L_ENG_HYD_PUMPS_BUTTON", ("L ENG PUMP OFF", "L ENG PUMPS ON")),
                           ("B757_OVHD_R_ENG_HYD_PUMPS_BUTTON", ("R ENG PUMP OFF", "R ENG PUMPS ON")),
                           ("B757_OVHD_R_ENG_ELEC_PUMPS_BUTTON", ("R ENG ELEC OFF", "R ENG ELEC ON")),
                           ("B757_OVHD_C_ENG1_HYD_PUMPS_BUTTON", ("C ENG1 PUMP OFF", "C ENG1 PUMPS ON")),
                           ("B757_OVHD_C_ENG2_HYD_PUMPS_BUTTON", ("C ENG2 PUMP OFF", "C ENG2 PUMPS ON")),
                           ("B757_OVHD_L_ENG_ELEC_PUMPS_BUTTON", ("L ENG ELEC OFF", "L ENG ELEC ON"))):
            rows[node] = toggle(node, "HYDRAULIC PANEL", tips)
        for side in ("FWD", "AFT"):
            node = f"B757_OVHD_XFEED_PUMPS_{side}_BUTTON"
            rows[node] = toggle(node, "FUEL PANEL", (f"XFEED PUMP {side} OFF", f"XFEED PUMP {side} ON"))
        for node, covers in (("B757_GAURD_11_SWITCH", "B757_PED_1BTL_CARGOFIRE_BUTTON"),
                             ("B757_GAURD_12_SWITCH", "B757_PED_2BTL_CARGOFIRE_BUTTON")):
            rows[node] = guard(node, "PEDESTAL GAURD SWITCH GEAR", f"(L:{node}, bool) ! (>L:{node}, bool)", covers)
        node = "B757_MCP_btn_autopilot_disengage"
        rows[node] = row(node=node, kind="toggle", positions="2", panel="AP MASTER DISCONNECT", state=["L:" + node],
                         left=f"(L:{node}, bool) ! (>L:{node}) (L:{node}, bool) if{{ 1 (>K:AUTOPILOT_DISENGAGE_SET) }} "
                              f"els{{ 0 (>K:AUTOPILOT_DISENGAGE_SET) }}")
        node = "BBS757_THRREF_CLICK"
        rows[node] = row(node=node, kind="toggle", positions="2", state=["L:BBS757_THRREF_PULL"],
                         panel="EICAS KEY FUNCTIONS - FORWARD ONLY - EXCLUDE CRT BRIGHTNESS",
                         left="(L:BBS757_THRREF_PULL,bool) ! (>L:BBS757_THRREF_PULL,bool)")
        node = "B757_MCP_AP_SPD_HOLD_SW"
        rows[node] = row(node=node, kind="momentary button", positions="-", panel="(after DOOR CODE)",
                         state=["L:B757_MCP_IAS_MACH_KNOB_BUTTON"], left="1 (>L:B757_MCP_IAS_MACH_KNOB_BUTTON)",
                         release="0 (>L:B757_MCP_IAS_MACH_KNOB_BUTTON)")
        rows["BBS757_FIREHANDLE1_CLICK3"] = apu_fire_handle_row()
        return rows

    MIC_CHANNELS = ("L", "C", "R", "LM", "RR", "PA", "INT")

    def mic_select_rows(self):
        """The fourteen audio mic selects, seven to a panel: a press clears the panel's seven and sets this one."""
        rows = {}
        for panel in (1, 2):
            var = {ch: f"B757_PED_AUDIO_{panel}_MIC_{ch}_LIGHT" for ch in self.MIC_CHANNELS}
            for ch in self.MIC_CHANNELS:
                press = " ".join(f"0 (>L:{v}, bool)" for v in var.values()) + f" 1 (>L:{var[ch]}, bool)"
                rows[var[ch]] = row(node=var[ch], kind="select button", positions="-", state=["L:" + var[ch]],
                                    panel="INDIVIDUAL AUDIO/RADIO PANELS", left=press)
        return rows

    def test_label_corrections(self):
        c = g.load_curation(g.DEFAULT_CURATION)
        rows = self.correction_rows()

        self.assertEqual(set(rows), set(self.LABEL_CORRECTIONS))
        for node, expected in self.LABEL_CORRECTIONS.items():
            with self.subTest(node=node):
                control, why, _ = g.convert_row(rows[node], c)
                self.assertIsNone(why)
                self.assertEqual((control["label"], control["kind"], [(p["value"], p["label"]) for p in control["positions"]]),
                                 expected)
                self.assertEqual(control["press"], rows[node]["_clicks"]["left"][0][1], "the click code is the vendor's")
        # a label never moves a control: the electric pumps keep the order the preflight flow wants, right, center, left
        self.assertEqual(c["control_order"]["Hydraulics"],
                         ["B757_OVHD_L_ENG_HYD_PUMPS_BUTTON", "B757_OVHD_R_ENG_HYD_PUMPS_BUTTON",
                          "B757_OVHD_R_ENG_ELEC_PUMPS_BUTTON", "B757_OVHD_C_ENG1_HYD_PUMPS_BUTTON",
                          "B757_OVHD_C_ENG2_HYD_PUMPS_BUTTON", "B757_OVHD_L_ENG_ELEC_PUMPS_BUTTON"])
        # each mic select is a button on its own state var: pressing selects it and clears the others
        mics = self.mic_select_rows()
        self.assertEqual(len(mics), 14)
        for node, r in mics.items():
            with self.subTest(node=node):
                control, why, _ = g.convert_row(r, c)
                self.assertIsNone(why)
                self.assertEqual((control["kind"], control["state_var"], control["positions"], control["release"]),
                                 ("button", node, [], None))
                self.assertEqual(control["press"], r["_clicks"]["left"][0][1], "the click code is the vendor's")

    def test_labels_unique_per_panel(self):
        # [VAR-1]. The generator numbers a repeat ("L ELEC Pump 2") instead of stopping, so a clash would reach the pilot
        # as a name that means nothing: no corrected control may need a number, and the six hydraulic pumps, which share
        # one panel, read six different names.
        c = g.load_curation(g.DEFAULT_CURATION)
        rows = list(self.correction_rows().values())
        for pump in ("L_PUMPS_FWD", "L_PUMPS_AFT", "R_PUMPS_FWD", "R_PUMPS_AFT", "C_PUMPS_L", "C_PUMPS_R"):
            node = f"B757_OVHD_{pump}_BUTTON"
            rows.append(row(node=node, kind="toggle", panel="FUEL PANEL", left=f"(L:{node},bool) ! (>L:{node},bool)"))
        controls = [g.convert_row(r, c)[0] for r in rows]
        before = [(x["node"], x["label"]) for x in controls]

        g.unique_labels(controls)

        self.assertEqual([(x["node"], x["label"]) for x in controls], before, "a repeated label was numbered")
        repeats = [k for k, n in collections.Counter((x["section"], x["panel"], x["label"]) for x in controls).items() if n > 1]
        self.assertEqual(repeats, [])
        hydraulics = [x["label"] for x in controls if (x["section"], x["panel"]) == ("Overhead", "Hydraulics")]
        self.assertEqual(sorted(hydraulics), sorted(["L ENG HYD Pumps", "R ENG HYD Pumps", "R ELEC Pump", "C1 ELEC Pump",
                                                     "C2 ELEC Pump", "L ELEC Pump"]))
        fuel = [x["label"] for x in controls if (x["section"], x["panel"]) == ("Overhead", "Fuel")]
        self.assertEqual(len(fuel), 8)

    def test_the_overhead_ignition_selector_is_labelled_ignition(self):
        # The overhead ignition selector's node says ENG START; it selects the igniters (the vendor's tooltip
        # 'Engine Ignitors'), and the panel's other two rows are the L and R ENG Start selectors.
        c = g.load_curation(g.DEFAULT_CURATION)
        self.assertEqual(c["labels"]["B757_OVHD_ENG_START_SWITCH"], "Ignition")

    def test_the_shipped_curation_shows_the_chocks_without_announcing_them(self):
        # Ruling R31: L:BBS757_vis_chocks, 0 removed, 1 in place (measured), a Status row, never spoken.
        c = g.load_curation(g.DEFAULT_CURATION)
        chocks = [g.status_row(s) for s in c["status"] if s["key"] == "BBS757_vis_chocks"]
        self.assertEqual(chocks, [{"key": "BBS757_vis_chocks", "panel": "Status", "label": "Chocks",
                                   "positions": [{"value": 0, "label": "Removed"}, {"value": 1, "label": "In place"}],
                                   "announce": False}])

    def test_the_notes_are_written_in_us_english(self):
        with open(g.DEFAULT_CURATION, encoding="utf-8-sig") as fh:
            notes = "\n".join(json.load(fh)["_notes"])
        for british in ("initialise", "colour", "centre"):
            self.assertNotIn(british, notes)

    def test_each_named_position_list_has_its_evidence_in_the_notes(self):
        with open(g.DEFAULT_CURATION, encoding="utf-8-sig") as fh:
            data = json.load(fh)
        notes = "\n".join(data["_notes"])
        for node in ("B757_MAIN_knob_gears_altn_autobrake", "BBS_fuelcut_PW1", "BBS_fuelcut_PW2", "B757_IRS_L_SWITCH",
                     "B757_IRS_C_SWITCH", "B757_IRS_R_SWITCH", "B757_ADI_1_TFC_KNOB", "B757_ADI_2_TFC_KNOB",
                     "B757_ADI_1_CTR_KNOB", "B757_ADI_2_CTR_KNOB", "B757_PED_TCAS_MODE_KNOB",
                     "B757_PED_TCAS_XPNDR_SWITCH", "B757_MAIN_knob_gears_altn_flaps", "B757_IRS_LARGE_KNOB",
                     "B757_IRS_CENTRE_KNOB", "B757_MCP_HOLD_1_BUTTON", "BBS757_PEDESTAL_Park_Brake_Lever",
                     "B757_OVHD_SEATBELTS_SWITCH", "B757_OVHD_NO_SMOKING_SWITCH", "B757_OVHD_SEL_L_PACK_SWITCH",
                     "B757_OVHD_SEL_R_PACK_SWITCH", "B757_OVHD_MODE_SELECT_SWITCH", "B757_OVHD_ENG_START_SWITCH",
                     "B757_MCP_AP_HDG_BNKLIM", "B757_EICAS_LWR_THRUST-REF_KNOB", "B757_OVHD_WIPER_SWITCH",
                     "BBS757_vis_chocks",
                     # plan 02 task 1: the choices that rest on more than BlueBird's list being read
                     "B757_MAIN_CHRONO_ET_SWITCH", "B757_MAIN_CHRONO_SET_SWITCH", "B757_VC_P61_SWITCH_5"):
            with self.subTest(node=node):
                self.assertIn(node, notes)
        self.assertIn("unmeasured", notes)
        self.assertIn("Position names from BlueBird's simvar list (support.bluebirdsim.com/docs/simvars, values checked "
                      "against source 2026-10-21), read 2026-10-25.", data["_notes"])

    def test_each_label_and_kind_correction_has_its_evidence_in_the_notes(self):
        with open(g.DEFAULT_CURATION, encoding="utf-8-sig") as fh:
            notes = "\n".join(json.load(fh)["_notes"])
        for node in ("B757_OVHD_SWITCH_GAURD", "B757_OVHD_C_ENG1_HYD_PUMPS_BUTTON", "B757_OVHD_C_ENG2_HYD_PUMPS_BUTTON",
                     "B757_OVHD_L_ENG_ELEC_PUMPS_BUTTON", "B757_OVHD_R_ENG_ELEC_PUMPS_BUTTON",
                     "B757_OVHD_XFEED_PUMPS_FWD_BUTTON", "B757_OVHD_XFEED_PUMPS_AFT_BUTTON", "B757_GAURD_11_SWITCH",
                     "B757_GAURD_12_SWITCH", "B757_MCP_btn_autopilot_disengage", "BBS757_THRREF_CLICK",
                     "B757_MCP_AP_SPD_HOLD_SW", "BBS757_FIREHANDLE1_CLICK3", "B757_PED_AUDIO_1_MIC_", "B757_PED_AUDIO_2_MIC_"):
            with self.subTest(node=node):
                self.assertIn(node, notes)
        # the key that makes the two kind changes is explained where the other keys are
        self.assertRegex(notes, r"kinds: node -> toggle or button")
        self.assertIn("support.bluebirdsim.com/docs/simvars", notes)

    # Ruling R37 (the owner's direction, 2026-10-25): the panels in the manual's preflight flow, the most used controls
    # first. Written out here as the owner approved them, so a change to the shipped curation is a visible change.
    PANEL_ORDER = {
        "Overhead": ["Electrical", "APU", "IRS", "Engine control and yaw damper", "Hydraulics", "Overhead miscellaneous",
                     "Fuel", "Anti-ice", "Wipers", "Exterior lights", "Window heat", "Passenger signs", "Pressurization",
                     "Air conditioning and bleed", "Cabin lights", "IRS keypad", "Switch guards", "Flight deck door",
                     "CVR", "SELCAL and cabin call", "Ground service", "Warning lights test"],
        "Glareshield": ["MCP", "VOR/DME", "Master warning and caution"],
        "Forward panel": ["Main panel switches", "Landing gear", "Captain EFIS", "Yokes", "Captain autoland status",
                          "Standby attitude indicator", "Captain DDRMI", "Captain chronometer", "Oxygen",
                          "Cockpit miscellaneous", "First officer EFIS", "First officer autoland status",
                          "First officer DDRMI", "First officer chronometer"],
        "Pedestal": ["Control stand", "Engines", "TCAS and transponder", "EICAS control", "Radio tuning panel 1",
                     "Audio panel 1", "Fire protection", "Stabilizer trim", "Weather radar left", "ADF",
                     "Radio tuning panel 2", "Radio tuning panel 3", "Audio panel 2", "Weather radar right",
                     "Pedestal radio panels"],
        "Other": ["P61 panel"],
    }
    # The first controls of each panel that has an order, by the label the pilot hears (the rest keep cockpit order).
    CONTROL_ORDER_LABELS = {
        "Electrical": ["Battery", "STBY Power", "L Bus Tie", "R Bus Tie", "L Gen Cont", "R Gen Cont", "APU Gen",
                       "EXT PWR", "L Util Bus", "R Util Bus", "L Gen Drive", "R Gen Drive"],
        "Hydraulics": ["L ENG HYD Pumps", "R ENG HYD Pumps", "R ELEC Pump", "C1 ELEC Pump", "C2 ELEC Pump",
                       "L ELEC Pump"],
        "Fuel": ["L Pumps FWD", "L Pumps Aft", "R Pumps FWD", "R Pumps Aft", "C Pumps L", "C Pumps R",
                 "Crossfeed FWD", "Crossfeed Aft"],
        "Exterior lights": ["Anti-col Red Lights", "Position Lights", "Anti-col White Lights", "Taxi Light",
                            "L Landing Light", "R Landing Light", "L Runway Turnoff", "R Runway Turnoff", "Wing Lights",
                            "Logo Lights"],
        "Air conditioning and bleed": ["L Pack Selector", "R Pack Selector", "Isolation", "L ENG Bleed", "APU Bleed",
                                       "R ENG Bleed", "Trim Air", "L Recirc", "R Recirc", "L Pack", "R Pack"],
        "Passenger signs": ["Seatbelts", "No Smoking"],
        "IRS": ["IRS L", "IRS C", "IRS R"],
        "Engine control and yaw damper": ["L Yaw Damper", "R Yaw Damper", "L ELEC ENG Cont", "R ELEC ENG Cont"],
        "MCP": ["Captain F/D", "F/O F/D", "A/T Arm", "THR", "SPD", "LNAV", "VNAV", "FLCH", "HDG SEL", "HDG HOLD", "V/S",
                "ALT HOLD", "LOC", "APP", "CMD L", "CMD C", "CMD R", "Autopilot Disengage Bar", "IAS/MACH",
                "ALT SEL Push", "Speed Intervene", "Bank Limit", "BCRS 1"],
        "Main panel switches": ["Autobrake", "Takeoff Check", "TOGA", "CLB", "CON", "CRZ", "TMSP 1", "TMSP 2",
                                "HDG Ref", "Cancel", "Recall"],
        "Control stand": ["Parking Brake", "Speedbrake Armed", "Speedbrake Down", "Speedbrake Up", "TO/GA",
                          "A/T Disconnect"],
        "Engines": ["Ignition", "L ENG Start", "L Fuel Control (PW)", "R ENG Start", "R Fuel Control (PW)",
                    "L Fuel Control (RR)", "R Fuel Control (RR)"],
        "TCAS and transponder": ["TCAS Mode"],   # the app's Squawk Code field follows it (BBS757Definition.Readouts.cs)
        "Captain EFIS": ["ND Mode", "ND Range"],
        "First officer EFIS": ["ND Mode", "ND Range"],
    }

    def test_the_shipped_curation_orders_the_panels_in_the_preflight_flow(self):
        c = g.load_curation(g.DEFAULT_CURATION)

        self.assertEqual(c["panel_order"], self.PANEL_ORDER)
        for section, names in c["panel_order"].items():
            with self.subTest(section=section):
                self.assertIn(section, g.SECTIONS)
                self.assertEqual(len(names), len(set(names)), "a panel listed twice")

    def test_the_shipped_curation_puts_the_most_used_controls_first(self):
        # A node's label is its curated one, else the one made from its name: the pilot hears the label.
        c = g.load_curation(g.DEFAULT_CURATION)

        self.assertEqual(set(c["control_order"]), set(self.CONTROL_ORDER_LABELS))
        for panel, labels in self.CONTROL_ORDER_LABELS.items():
            with self.subTest(panel=panel):
                nodes = c["control_order"][panel]
                self.assertEqual(len(nodes), len(set(nodes)), "a node listed twice")
                self.assertEqual([c["labels"].get(n) or g.humanize(n, c["spelling"]) for n in nodes], labels)

    ENGINE_CONTROLS = {
        "B757_OVHD_ENG_START_SWITCH": "LIGHT OVERRIDE", "B757_OVHD_L_ENG_START_SWITCH": "LIGHT OVERRIDE",
        "B757_OVHD_R_ENG_START_SWITCH": "LIGHT OVERRIDE", "BBS_fuelcut_PW1": "(no banner)",
        "BBS_fuelcut_PW2": "(no banner)", "BBS_fuelcut_RR1": "(no banner)", "BBS_fuelcut_RR2": "(no banner)",
    }

    def test_the_shipped_curation_puts_the_start_selectors_and_fuel_switches_on_one_pedestal_panel(self):
        # The three overhead selectors would land in the Overhead by their node prefix: the rules name the Pedestal.
        c = g.load_curation(g.DEFAULT_CURATION)

        for node, banner in self.ENGINE_CONTROLS.items():
            with self.subTest(node=node):
                self.assertEqual(g.panel_of(row(node=node, panel=banner), c), ("Engines", "Pedestal"))
        named = {r["panel"] for r in c["node_panels"]} | {e if isinstance(e, str) else e["name"]
                                                          for e in c["panels"].values()}
        self.assertNotIn("Engine start", named)
        self.assertNotIn("Fuel control", named)
        self.assertEqual(set(c["control_order"]["Engines"]), set(self.ENGINE_CONTROLS))


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
                json.dump({"panels": {"ELECTRICAL PANEL": "Electrical"}, "panel_order": {"Overhead": ["Electrical"]}}, fh)
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

    def test_the_run_names_the_panels_panel_order_does_not_list(self):
        # A panel BlueBird adds later sits alphabetically after the listed ones: the run says so, on stderr like its
        # other notes.
        with tempfile.TemporaryDirectory() as d:
            pkg = write_package(d)
            out = os.path.join(d, "out", "map.json")
            cur_path = os.path.join(d, "curation.json")

            def run(data):
                with open(cur_path, "w", encoding="utf-8") as fh:
                    json.dump(data, fh)
                stderr = io.StringIO()
                with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(stderr):
                    g.main([str(pkg), "--out", out, "--curation", cur_path])
                return stderr.getvalue()

            panels = {"ELECTRICAL PANEL": "Electrical"}
            unlisted = run({"panels": panels})
            listed = run({"panels": panels, "panel_order": {"Overhead": ["Electrical"]}})

        self.assertIn("panel_order does not list", unlisted)
        self.assertIn("Overhead / Electrical", unlisted)
        self.assertNotIn("panel_order", listed)


if __name__ == "__main__":
    unittest.main()
