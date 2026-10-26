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
    "guarded": {},
    "direct_write": [],
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


class Guarded(unittest.TestCase):
    """Plan 02, task 3: curation's 'guarded'. The emergency lights switch and the two stab trim cutouts move only while
    their guard is open. The vendor's click on each is gated on the guard, which the extractor writes as an '[if <test>] '
    note in front of the click code: not RPN. So a guarded control keeps no click code. It is a switch on its own L:var
    that names its guard, the value the guard reads closed and the value closing the guard writes to it, and the app opens
    the guard, writes the position and closes it (Bbs757Control.Guard). The guard keeps its own row. The rows have the
    shape the extractor gives the vendor's own controls (BBS_752_BASE_INTERIOR.xml: the emergency lights at 8106, the
    stab trim cutouts at 15380) and are converted with the SHIPPED curation."""
    EMERGENCY, EMERGENCY_GUARD = "B757_OVHD_PASS_OXY_Toggle_Switch_CLICK", "B757_OVHD_SWITCH_GAURD"
    STAB_C, STAB_C_GUARD = "B757_Stab_trim_C_toggle_switch_CLICK", "B757_AISLE_STAND_STAB_1_GAURD_SWITCH"
    STAB_R, STAB_R_GUARD = "B757_Stab_trim_R_toggle_switch_CLICK", "B757_AISLE_STAND_STAB_2_GAURD_SWITCH"
    LT_OVRD = "B757_OVHD_LT_OVRD_BUTTON"
    SWITCHES = (EMERGENCY, STAB_C, STAB_R)
    GUARDS = (EMERGENCY_GUARD, STAB_C_GUARD, STAB_R_GUARD)
    CLICK_FIELDS = ("press", "release", "cw", "ccw")

    @staticmethod
    def gated(guard, code):
        """What the extractor makes of a click inside the vendor's '(L:<guard>, bool) 1 == if{ ... }'."""
        return f"[if (L:{guard}, bool) 1 ==] {code}"

    def rows(self):
        """node -> row, in the vendor's shape: the three guarded switches, their three guards and a neighbor of the
        emergency lights switch (the one the panel's cockpit order starts with)."""
        gated, rows = self.gated, {}
        node, guard = self.EMERGENCY, self.EMERGENCY_GUARD
        up, down = f"(L:{node}, number) 1 + 2 min (>L:{node}, number)", f"(L:{node}, number) 1 - 0 max (>L:{node}, number)"
        rows[self.LT_OVRD] = row(node=self.LT_OVRD, panel="LIGHT OVERRIDE", line=8080,
                                 left=f"(L:{self.LT_OVRD},bool) ! (>L:{self.LT_OVRD},bool)")
        # its animation reads an O: var, so the rules find no state var and leave the switch out
        rows[node] = row(node=node, kind="multi-position switch", positions="3", panel="LIGHT OVERRIDE", line=8107,
                         state=[f"O:{node}_SUBMOVEMENT"], left=gated(guard, up),
                         other=[("RightSingle", gated(guard, down)), ("WheelDown", gated(guard, up)),
                                ("WheelUp", gated(guard, down))])
        rows[guard] = row(node=guard, kind="guard", panel="LIGHT OVERRIDE", line=8113, state=["L:" + guard],
                          left=f"(L:{guard}, bool) ! (>L:{guard}, bool) (L:{guard}, bool) 0 == if{{ 1 (>L:{node}, number) }}")
        for node, guard, guard_line, line in ((self.STAB_C, self.STAB_C_GUARD, 15381, 15397),
                                              (self.STAB_R, self.STAB_R_GUARD, 15389, 15403)):
            rows[guard] = row(node=guard, kind="guard", panel="PEDESTAL GAURD SWITCH GEAR", line=guard_line,
                              state=["L:" + guard],
                              left=f"(L:{guard}, bool) ! (>L:{guard}, bool) "
                                   f"(L:{guard}, bool) 0 == if{{ 0 (>L:{node}, number) }}")
            rows[node] = row(node=node, kind="toggle", panel="PEDESTAL GAURD SWITCH GEAR", line=line, state=["L:" + node],
                             left=gated(guard, f"(L:{node}, number) 1 == if{{ 0 }} els{{ 1 }} (>L:{node}, number)"),
                             other=[("WheelDown", gated(guard, f"(L:{node}, number) 1 + 1 min (>L:{node}, number)")),
                                    ("WheelUp", gated(guard, f"(L:{node}, number) 1 - 0 max (>L:{node}, number)"))])
        return rows

    def shipped(self, **changes):
        """The shipped curation, cut down to the nodes of rows(): a table entry for a node the package lacks is an error."""
        cur = g.load_curation(g.DEFAULT_CURATION)
        here = set(self.rows())
        for table in ("labels", "positions", "kinds", "guarded"):
            cur[table] = {n: v for n, v in cur[table].items() if n in here}
        cur["direct_write"] = [n for n in cur["direct_write"] if n in here]
        cur["exclude_nodes"], cur["include_nodes"], cur["panel_order"] = [], [], {}
        cur["control_order"] = {p: ns for p, ns in cur["control_order"].items() if set(ns) <= here}
        cur.update(changes)
        return cur

    def build(self, rows=None, **changes):
        return g.build_map(list((rows or self.rows()).values()), self.shipped(**changes), "pkg", "1")

    def guarded(self, node, **changes):
        """The shipped 'guarded' table with some fields of one node's entry changed, or a node added: whatever a test
        changes is the one thing that is wrong with it."""
        table = self.shipped()["guarded"]
        table[node] = dict(table.get(node) or table[self.EMERGENCY], **changes)
        return table

    def test_guarded_controls(self):
        c = g.load_curation(g.DEFAULT_CURATION)
        rows = self.rows()

        def switch(node, section, panel, label, names, guard, close_sets):
            return {"node": node, "section": section, "panel": panel, "label": label, "kind": "switch", "state_var": node,
                    "positions": [{"value": v, "label": l} for v, l in enumerate(names)],
                    "press": None, "release": None, "hold_ms": 0, "cw": None, "ccw": None,
                    "guard": guard, "guard_closed": 0, "close_sets": close_sets}

        expected = {
            self.EMERGENCY: switch(self.EMERGENCY, "Overhead", "Overhead miscellaneous", "Emergency Lights",
                                   ("OFF", "ARMED", "ON"), self.EMERGENCY_GUARD, 1),
            self.STAB_C: switch(self.STAB_C, "Pedestal", "Stabilizer trim", "Stab Trim C Cutout",
                                ("NORMAL", "CUTOUT"), self.STAB_C_GUARD, 0),
            self.STAB_R: switch(self.STAB_R, "Pedestal", "Stabilizer trim", "Stab Trim R Cutout",
                                ("NORMAL", "CUTOUT"), self.STAB_R_GUARD, 0),
        }
        for node, want in expected.items():
            with self.subTest(node=node):
                control, why, slips = g.convert_row(rows[node], c)
                self.assertIsNone(why)
                self.assertEqual(control, want)
                self.assertEqual(list(control), list(want), "the keys keep the JSON order, the guard fields last")
                self.assertEqual(slips, [], "no click code is replayed, so there is none to suspect")
        # the guard keeps its own row: an ordinary guard, Closed and Open, on the vendor's own click code
        for node in self.GUARDS:
            with self.subTest(guard=node):
                control, why, _ = g.convert_row(rows[node], c)
                self.assertIsNone(why)
                self.assertEqual((control["kind"], [(p["value"], p["label"]) for p in control["positions"]]),
                                 ("guard", [(0, "Closed"), (1, "Open")]))
                self.assertEqual(control["press"], rows[node]["_clicks"]["left"][0][1])
                self.assertNotIn("guard_closed", control)

        m, report = self.build()

        # the emergency lights switch leads its panel and its guard follows; the rest keep cockpit order
        self.assertEqual([(x["node"], x["label"]) for x in m["controls"] if x["panel"] == "Overhead miscellaneous"],
                         [(self.EMERGENCY, "Emergency Lights"), (self.EMERGENCY_GUARD, "Emergency Lights Guard"),
                          (self.LT_OVRD, "Lt Ovrd")])
        self.assertEqual(sorted(x["label"] for x in m["controls"] if x["panel"] == "Stabilizer trim"),
                         ["Stab Trim C Cutout", "Stab Trim C Guard", "Stab Trim R Cutout", "Stab Trim R Guard"])
        # only a guarded control carries the guard fields
        self.assertEqual({x["node"] for x in m["controls"] if "guard" in x}, set(self.SWITCHES))
        self.assertEqual({x["node"] for x in m["controls"] if "guard_closed" in x or "close_sets" in x}, set(self.SWITCHES))

    def test_gate_note_never_reaches_click_code(self):
        # The note is in front of every vendor click here: what the old Stab Trim press began with.
        rows = self.rows()
        for node in self.SWITCHES:
            self.assertTrue(rows[node]["_clicks"]["left"][0][1].startswith("[if "), node)

        m, _ = self.build()

        for control in m["controls"]:
            for field in self.CLICK_FIELDS:
                with self.subTest(node=control["node"], field=field):
                    self.assertNotRegex(control[field] or "", g.GATE_NOTE)
        for control in m["controls"]:
            if control["node"] in self.SWITCHES:
                self.assertEqual([control[f] for f in self.CLICK_FIELDS], [None] * 4, control["node"])
        # the map the app embeds: no click code anywhere starts with the note, and the three guarded switches have none
        with open(g.DEFAULT_OUT, encoding="utf-8") as fh:
            embedded = json.load(fh)
        carrying = [(c["node"], f) for c in embedded["controls"] for f in self.CLICK_FIELDS
                    if re.search(r"(?:^|\s)\[", c[f] or "")]
        self.assertEqual(carrying, [])
        by_node = {c["node"]: c for c in embedded["controls"]}
        for node in self.SWITCHES:
            self.assertEqual((by_node[node]["kind"], [by_node[node][f] for f in self.CLICK_FIELDS]),
                             ("switch", [None] * 4), node)

    def test_gated_control_outside_guarded_stops_the_run(self):
        # The same package with `guarded` left empty: the rules keep the stab trims, their press starting with the note.
        with self.assertRaisesRegex(g.CurationError, rf"{self.STAB_C}: press carries the extractor's gate note "
                                                     r"'\[if \(L:B757_AISLE_STAND_STAB_1_GAURD_SWITCH, bool\) 1 ==\]'"):
            self.build(guarded={})
        # whichever of the four fields carries it, and wherever in the code: the codes of several templates join in one
        node, note = "B757_OVHD_X_SWITCH", "[if (L:G, bool) 1 ==] "
        joined = row(node=node, kind="toggle", left="(L:X) ! (>L:X)")
        joined["_clicks"]["left"].append(("", note + "1 (>L:Y)"))
        gated_release = row(node=node, kind="momentary button", positions="-", left="1 (>L:Y)", release=note + "0 (>L:Y)")
        cases = [
            ("press", row(node=node, kind="toggle", left=note + "(L:X) ! (>L:X)")),
            ("press", joined),
            ("release", gated_release),
            ("cw", row(node=node, kind="rotary knob finite", positions="3", cw=note + "(L:X) 1 + 2 min (>L:X)",
                       ccw="(L:X) 1 - 0 max (>L:X)")),
            ("ccw", row(node=node, kind="rotary knob finite", positions="3", cw="(L:X) 1 + 2 min (>L:X)",
                        ccw=note + "(L:X) 1 - 0 max (>L:X)")),
        ]
        for i, (field, r) in enumerate(cases):
            with self.subTest(field=field, case=i):
                with self.assertRaisesRegex(g.CurationError, rf"{node}: {field} carries the extractor's gate note"):
                    g.build_map([r], curation(), "pkg", "1")
        # only the control the map keeps is checked: a gated row of a node the package repeats is dropped as a repeat
        clean = row(node=node, kind="toggle", left="(L:X) ! (>L:X)")
        m, report = g.build_map([clean, gated_release], curation(), "pkg", "1")
        self.assertEqual(([c["kind"] for c in m["controls"]], report["dropped"]["duplicate node"]), (["toggle"], 1))
        # listed under `guarded` (its guard in the package), the same gated switches build, with no click code
        m, _ = self.build()
        self.assertEqual(sorted(c["node"] for c in m["controls"] if c["node"] in self.SWITCHES), sorted(self.SWITCHES))

    def test_a_guarded_control_the_rules_drop_is_taken_in_and_reported(self):
        # Never silent, as for include_nodes: the run names each guarded control and what the rules alone would have done.
        c, rows = self.shipped(), self.rows()
        self.assertEqual(g.drop_reason(rows[self.EMERGENCY], c), "state is not an L:var")
        self.assertIsNone(g.drop_reason(rows[self.STAB_C], c))
        old, _, _ = g.convert_row(rows[self.STAB_C], dict(c, guarded={}))
        self.assertEqual((old["kind"], old["press"].startswith("[if ")), ("toggle", True), "the press the rules alone make")

        m, report = self.build()

        self.assertEqual(sorted(report["guarded"]), sorted(self.SWITCHES))
        self.assertRegex(report["guarded"][self.EMERGENCY], r"^taken in .*the rules alone leave it out: state is not an L:var")
        for node in (self.STAB_C, self.STAB_R):
            self.assertRegex(report["guarded"][node], r"^re-read .*the rules alone keep it as a toggle")
        self.assertEqual(report["dropped"], collections.Counter(), "the emergency lights switch is not counted as dropped")
        self.assertEqual(report["taken_in"], [], "taken_in stays include_nodes' own")
        plain, plain_report = g.build_map([rows[self.LT_OVRD]], curation(), "pkg", "1")   # none listed, none named
        self.assertEqual((plain_report["guarded"], [c["node"] for c in plain["controls"]]), ({}, [self.LT_OVRD]))

    def test_a_guarded_entry_that_does_not_fit_the_package_stops_the_run(self):
        # Each case changes the shipped curation in one way and nothing else, so the one check it is meant for is the one
        # that stops the run; the shipped curation itself passes all of them.
        sw, guard = self.EMERGENCY, self.EMERGENCY_GUARD
        stab, stab_guard = self.STAB_C, self.STAB_C_GUARD
        cases = [
            ("a node the package does not have", dict(guarded=self.guarded("B757_NO_SUCH_NODE")),
             "guarded name nodes the package does not have: B757_NO_SUCH_NODE"),
            ("a guard the package does not have", dict(guarded=self.guarded(sw, guard="B757_NO_SUCH_GUARD")),
             rf"the guard B757_NO_SUCH_GUARD of {sw} has no row in the map \(the package does not have it\)"),
            ("a guard the rules leave out", dict(exclude_nodes=[guard]),
             rf"the guard {guard} of {sw} has no row in the map \(the rules leave it out: excluded node\)"),
            ("names that do not fit the vendor's count", dict(guarded=self.guarded(sw, positions=["OFF", "ON"])),
             rf"positions for {sw}: 2 names but the control has 3 positions"),
            ("a state var the click never writes", dict(guarded=self.guarded(sw, state_var=sw + "_TYPO")),
             rf"{sw}'s click code never writes {sw}_TYPO"),
            ("a guard that does not gate the click", dict(guarded=self.guarded(stab, guard=self.STAB_R_GUARD)),
             rf"{stab}'s click is not gated on {self.STAB_R_GUARD}'s var"),
            ("a close_sets the guard's click does not write", dict(guarded=self.guarded(stab, close_sets=1)),
             rf"closing {stab_guard} writes 0 to {stab}, but close_sets is 1"),
            ("a close_sets of none when closing writes", dict(guarded=self.guarded(sw, close_sets=None)),
             rf"closing {guard} writes 1 to {sw}, but close_sets is None"),
            ("a node curation leaves out", dict(exclude_nodes=[stab]),
             rf"guarded names {stab}, which curation also leaves out \(excluded node\)"),
            ("a positions entry too", dict(positions={sw: ["OFF", "ARMED", "ON"]}),
             rf"guarded and positions both name: {sw}"),
            ("a kinds entry too", dict(kinds={stab: "button"}), rf"guarded and kinds both name: {stab}"),
        ]
        for what, changes, message in cases:
            with self.subTest(what):
                with self.assertRaisesRegex(g.CurationError, message):
                    self.build(**changes)
        self.build()

    def test_a_guarded_entry_may_name_its_positions_as_value_label_pairs(self):
        # As under 'positions': names are laid onto the vendor's own values, pairs say which values they are.
        pairs = [{"value": 0, "label": "NORMAL"}, {"value": 1, "label": "CUTOUT"}]

        m, _ = self.build(guarded=self.guarded(self.STAB_C, positions=pairs))

        self.assertEqual({c["node"]: c["positions"] for c in m["controls"]}[self.STAB_C], pairs)

    def test_a_guarded_node_under_an_excluded_banner_stops_the_run(self):
        banners = ["CB OVERHEAD PANEL", "PEDESTAL GAURD SWITCH GEAR"]
        with self.assertRaisesRegex(g.CurationError, rf"guarded names {self.STAB_C}, which curation also leaves out "
                                                     r"\(excluded banner\)"):
            self.build(exclude_banners=banners)


class DirectWrite(unittest.TestCase):
    """Plan 02, task 4 (ruling R10): curation's 'direct_write'. The chronometer ET lever's click code cannot take a pick where
    it aims: the vendor's clockwise code steps the value DOWN (3, 2, 1, 0) and its anticlockwise code steps it up but jumps
    HOLD (1) straight to RESET (3), and 2 is no position, so a step down from RESET would land on a value the app then
    refuses every pick from. The code writes nothing but the lever's own L:var, so the map leaves all of it out and the app
    writes that var to the picked value (Bbs757Write.ToPosition's path for a control with no click code). The rows have the
    shape the extractor gives the vendor's own controls (BBS_752_BASE_INTERIOR.xml: the captain's lever at 26713, the first
    officer's at 28529) and are converted with the SHIPPED curation."""
    ET, ET_FO = "B757_MAIN_CHRONO_ET_SWITCH", "B757_MAIN_CHRONO_ET_SWITCH_FO"
    SET, SET_FO = "B757_MAIN_CHRONO_SET_SWITCH", "B757_MAIN_CHRONO_SET_SWITCH_FO"
    VAR = {ET: "BBS_CHRONO_LEFT_SWITCH", ET_FO: "BBS_CHRONO_LEFT_SWITCH_FO",
           SET: "BBS_CHRONO_RIGHT_SWITCH", SET_FO: "BBS_CHRONO_RIGHT_SWITCH_FO"}
    BANNER = {ET: "CAPT CHRONOMETER", SET: "CAPT CHRONOMETER",
              ET_FO: "FISRT OFFICER CHRONOMETER", SET_FO: "FISRT OFFICER CHRONOMETER"}
    CLICK_FIELDS = ("press", "release", "cw", "ccw")
    RUN_HOLD_RESET = [{"value": 0, "label": "RUN"}, {"value": 1, "label": "HOLD"}, {"value": 3, "label": "RESET"}]

    @staticmethod
    def et_codes(var):
        """(cw, ccw) of an ET lever as the vendor wrote them: clockwise lowers the value one step, anticlockwise raises it
        and jumps HOLD (1) straight to RESET (3)."""
        return (f"(L:{var}, enum) -- d 0 < if{{ 0 }} (>L:{var}, enum)",
                f"(L:{var}, enum) 1 == if{{ 3 (>L:{var}, enum) }} "
                f"els{{ (L:{var}, enum) ++ d 3 > if{{ 3 }} (>L:{var}, enum) }}")

    @staticmethod
    def set_codes(var):
        """(cw, ccw) of a SET lever beside it: the other way round, clockwise raises the value and anticlockwise lowers it."""
        return (f"(L:{var}, enum) ++ d 3 > if{{ 3 }} (>L:{var}, enum)",
                f"(L:{var}, enum) -- d 0 < if{{ 0 }} (>L:{var}, enum)")

    def rows(self):
        """node -> row: the two ET levers and the two SET levers beside them, as the extractor reads the vendor's."""
        rows = {}
        for node, codes in ((self.ET, self.et_codes), (self.ET_FO, self.et_codes),
                            (self.SET, self.set_codes), (self.SET_FO, self.set_codes)):
            var = self.VAR[node]
            cw, ccw = codes(var)
            rows[node] = row(node=node, kind="rotary knob finite", positions="4", panel=self.BANNER[node],
                             state=["L:" + var], tips=["RUN", "HOLD", "RESET"], cw=cw, ccw=ccw)
        return rows

    def stab_rows(self):
        """A guarded control for the one case that needs one: the stab trim C cutout and its guard, in the vendor's shape
        (BBS_752_BASE_INTERIOR.xml:15381-15397)."""
        node, guard = "B757_Stab_trim_C_toggle_switch_CLICK", "B757_AISLE_STAND_STAB_1_GAURD_SWITCH"
        return [
            row(node=guard, kind="guard", panel="PEDESTAL GAURD SWITCH GEAR", line=15381, state=["L:" + guard],
                left=f"(L:{guard}, bool) ! (>L:{guard}, bool) (L:{guard}, bool) 0 == if{{ 0 (>L:{node}, number) }}"),
            row(node=node, kind="toggle", panel="PEDESTAL GAURD SWITCH GEAR", line=15397, state=["L:" + node],
                left=f"[if (L:{guard}, bool) 1 ==] (L:{node}, number) 1 == if{{ 0 }} els{{ 1 }} (>L:{node}, number)"),
        ]

    def shipped(self, rows, **changes):
        """The shipped curation, cut down to the nodes of `rows`: a table entry for a node the package lacks is an error."""
        cur = g.load_curation(g.DEFAULT_CURATION)
        here = set(rows)
        for table in ("labels", "positions", "kinds", "guarded"):
            cur[table] = {n: v for n, v in cur[table].items() if n in here}
        cur["direct_write"] = [n for n in cur["direct_write"] if n in here]
        cur["exclude_nodes"], cur["include_nodes"], cur["panel_order"] = [], [], {}
        cur["control_order"] = {p: ns for p, ns in cur["control_order"].items() if set(ns) <= here}
        cur.update(changes)
        return cur

    def build(self, extra=(), **changes):
        rows = {**self.rows(), **{r["node"]: r for r in extra}}
        return g.build_map(list(rows.values()), self.shipped(rows, **changes), "pkg", "1")

    def test_chrono_et_writes_its_state_directly(self):
        c, rows = g.load_curation(g.DEFAULT_CURATION), self.rows()
        self.assertEqual(c["direct_write"], [self.ET, self.ET_FO])
        for node, panel in ((self.ET, "Captain chronometer"), (self.ET_FO, "First officer chronometer")):
            with self.subTest(node=node):
                control, why, slips = g.convert_row(rows[node], c)

                self.assertIsNone(why)
                self.assertEqual(control, {
                    "node": node, "section": "Forward panel", "panel": panel, "label": "Chrono ET", "kind": "switch",
                    "state_var": self.VAR[node], "positions": self.RUN_HOLD_RESET,
                    "press": None, "release": None, "hold_ms": 0, "cw": None, "ccw": None})
                self.assertEqual(slips, [], "no click code is replayed, so there is none to suspect")
                # The entry changes the click code and nothing else: without it the same row keeps the vendor's own code,
                # verbatim, and every other field is the same.
                plain, _, _ = g.convert_row(rows[node], dict(c, direct_write=[]))
                cw, ccw = self.et_codes(self.VAR[node])
                self.assertEqual([plain[f] for f in self.CLICK_FIELDS], [None, None, cw, ccw])
                self.assertEqual({k: v for k, v in plain.items() if k not in self.CLICK_FIELDS},
                                 {k: v for k, v in control.items() if k not in self.CLICK_FIELDS})
                self.assertEqual(list(plain), list(control), "the keys keep the JSON order")
                # The vendor's code is no longer replayed, but it still stands as the evidence for the positions: naming
                # RESET at 2, as the cockpit's own tooltip list does, stops the run because that code writes 3.
                at_two = [{"value": v, "label": l} for v, l in ((0, "RUN"), (1, "HOLD"), (2, "RESET"))]
                with self.assertRaisesRegex(g.CurationError, "writes 3 .* outside its positions"):
                    g.convert_row(rows[node], dict(c, positions=dict(c["positions"], **{node: at_two})))
        # the map the app embeds: both levers keep their state var and positions and hold no click code
        with open(g.DEFAULT_OUT, encoding="utf-8") as fh:
            embedded = {x["node"]: x for x in json.load(fh)["controls"]}
        for node in (self.ET, self.ET_FO):
            x = embedded[node]
            self.assertEqual((x["kind"], x["state_var"], x["positions"], [x[f] for f in self.CLICK_FIELDS]),
                             ("switch", self.VAR[node], self.RUN_HOLD_RESET, [None] * 4), node)

    def test_every_click_field_is_left_out_whichever_slot_the_vendor_used(self):
        # press and release as well as cw and ccw: a switch that cycles on a click and springs back to 1 on release
        node = "B757_VC_P61_SWITCH_X"
        r = row(node=node, kind="multi-position switch", positions="3", state=["L:V"], left="(L:V) 1 + 3 % (>L:V)",
                release="1 (>L:V)")

        plain, _, _ = g.convert_row(r, curation())
        direct, _, _ = g.convert_row(r, curation(direct_write=[node]))

        self.assertEqual([plain[f] for f in self.CLICK_FIELDS], ["(L:V) 1 + 3 % (>L:V)", "1 (>L:V)", None, None])
        self.assertEqual([direct[f] for f in self.CLICK_FIELDS], [None] * 4)
        self.assertEqual({k: v for k, v in direct.items() if k not in self.CLICK_FIELDS},
                         {k: v for k, v in plain.items() if k not in self.CLICK_FIELDS})

    def test_a_slip_in_a_click_code_the_map_leaves_out_is_not_reported(self):
        # Suspected slips are about code the app replays verbatim: code that is left out has none to suspect.
        node, var = self.ET, self.VAR[self.ET]
        cw, ccw = self.et_codes(var)
        r = row(node=node, kind="rotary knob finite", positions="4", panel=self.BANNER[node], state=["L:" + var],
                cw=cw + " }", ccw=ccw)
        c = g.load_curation(g.DEFAULT_CURATION)

        _, _, replayed = g.convert_row(r, dict(c, direct_write=[]))
        _, _, left_out = g.convert_row(r, c)

        self.assertEqual(replayed, ["cw: closing brace nothing opened"])
        self.assertEqual(left_out, [])

    def test_only_the_listed_levers_lose_their_click_code_and_the_report_names_them(self):
        m, report = self.build()
        by_node = {x["node"]: x for x in m["controls"]}

        for node in (self.ET, self.ET_FO):
            self.assertEqual([by_node[node][f] for f in self.CLICK_FIELDS], [None] * 4, node)
        for node in (self.SET, self.SET_FO):  # the SET levers beside them step the right way: their code stays, verbatim
            cw, ccw = self.set_codes(self.VAR[node])
            self.assertEqual([by_node[node][f] for f in self.CLICK_FIELDS], [None, None, cw, ccw], node)
        # the run lists the direct-write controls in curation's order, as it lists the guarded ones, with what it left out
        self.assertEqual(report["direct_write"], {self.ET: "cw, ccw left out", self.ET_FO: "cw, ccw left out"})
        self.assertEqual(report["suspected_slips"], {})
        self.assertEqual(report["dropped"], collections.Counter())
        _, none_listed = self.build(direct_write=[])
        self.assertEqual(none_listed["direct_write"], {})

    def test_direct_write_errors(self):
        # Each case changes the shipped curation, or adds one row, in one way and nothing else, so the one check it is meant
        # for is the one that stops the run; the shipped curation itself passes all of them.
        et, ghost = self.ET, "B757_NO_SUCH_NODE"
        lever = row(node="B757_PED_X_LEVER", kind="lever", positions="", left="1 (>L:X)")
        no_state = row(node="B757_PED_X_BUTTON", kind="momentary button", positions="-", state=["O:_AnimProgress"],
                       left="1 (>L:X)")
        no_positions = row(node="B757_PED_Y_BUTTON", kind="momentary button", positions="-", state=["L:Y"], left="1 (>L:Y)")
        stab = self.stab_rows()
        guarded_node = stab[1]["node"]
        cases = [
            ("a node the package does not have", (), dict(direct_write=[et, ghost]),
             rf"direct_write name nodes the package does not have: {ghost}"),
            ("a node curation leaves out", (), dict(exclude_nodes=[et]),
             rf"direct_write names {et}, which the map does not keep \(the rules leave it out: excluded node\)"),
            ("a node the rules drop", (lever,), dict(direct_write=[et, lever["node"]]),
             rf"direct_write names {lever['node']}, which the map does not keep \(the rules leave it out: lever\)"),
            ("a node with no state var", (no_state,), dict(direct_write=[et, no_state["node"]]),
             rf"direct_write names {no_state['node']}, which has no state var"),
            ("a node with no positions", (no_positions,), dict(direct_write=[et, no_positions["node"]]),
             rf"direct_write names {no_positions['node']}, which has no positions"),
            ("a node that is also guarded", stab, dict(direct_write=[et, guarded_node]),
             rf"guarded and direct_write both name: {guarded_node}"),
        ]
        for what, extra, changes, message in cases:
            with self.subTest(what):
                with self.assertRaisesRegex(g.CurationError, message):
                    self.build(extra, **changes)
        self.build()
        # a guarded control, and a control with a state var and no positions, are only a problem under direct_write
        self.build(stab)
        self.build([no_positions])

    # The premise of direct_write is that leaving the click code out loses nothing. The generator checks it against the
    # vendor's own code on every run, so that a change of BlueBird's stops the run instead of reaching the app, and only a
    # switch is taken: the one control the map may hold without a click code.
    @staticmethod
    def switch_row(node, **clicks):
        """A three-position switch on its own L:var (the node's name), with the click code given."""
        return row(node=node, kind="multi-position switch", positions="3", state=["L:" + node], **clicks)

    def test_direct_write_needs_a_click_code_to_leave_out(self):
        # A switch the vendor gives no click code (the fuel control switches, the P61 test switches) is written through its
        # state var already: the entry would do nothing, and an entry that does nothing is a stale one.
        node = "B757_PED_NOCODE_SWITCH"
        bare = self.switch_row(node)

        with self.assertRaisesRegex(g.CurationError, rf"direct_write names {node}, whose vendor row has no click code"):
            self.build([bare], direct_write=[self.ET, node])
        self.build([bare])   # without the entry it builds, written through its state var

    def test_a_gated_click_belongs_under_guarded_and_direct_write_does_not_hide_it(self):
        # A click that only runs while a guard is open (the extractor's '[if ...]' note: the stab trims, the emergency lights
        # switch) stops the run unless the control is guarded. direct_write must not be a way round that: the app would write
        # the state var with the guard closed.
        node, guard = "B757_PED_GATED_SWITCH", "B757_PED_GATED_GUARD"
        note = f"[if (L:{guard}, bool) 1 ==] "
        up, down, cycle = (f"(L:{node}) 1 + 2 min (>L:{node})", f"(L:{node}) 1 - 0 max (>L:{node})",
                           f"(L:{node}) 1 + 3 % (>L:{node})")
        cases = [
            ("a gated cw and ccw", "cw", self.switch_row(node, left=note + up, other=[("RightSingle", note + down)])),
            ("a gated press", "press", self.switch_row(node, left=note + cycle)),
            ("a gated release", "release", self.switch_row(node, left=cycle, release=note + f"1 (>L:{node})")),
        ]
        for what, field, r in cases:
            with self.subTest(what):
                with self.assertRaisesRegex(g.CurationError, rf"^{node}: {field} carries the extractor's gate note"):
                    self.build([r])   # the rule every other control meets
                with self.assertRaisesRegex(g.CurationError, rf"^curation direct_write names {node}: {field} carries the "
                                                             r"extractor's gate note '\[if .*\]': .*list it under 'guarded'"):
                    self.build([r], direct_write=[self.ET, node])

    def test_direct_write_stops_on_a_click_that_writes_more_than_its_state_var(self):
        # The premise of leaving the click code out: it writes nothing but the lever's own L:var. A BlueBird update that adds
        # a write (another L:var, a K: or an H: event) stops the run, since the app would no longer run it.
        node, var = self.ET, self.VAR[self.ET]
        cw, ccw = self.et_codes(var)

        def lever(cw_code=cw, ccw_code=ccw):
            return row(node=node, kind="rotary knob finite", positions="4", panel=self.BANNER[node], state=["L:" + var],
                       tips=["RUN", "HOLD", "RESET"], cw=cw_code, ccw=ccw_code)

        cases = [
            ("another L:var", " 1 (>L:BBS_SOME_OTHER_VAR)", "L:BBS_SOME_OTHER_VAR"),
            ("a K: event", " 1 (>K:AUTOPILOT_OFF)", "K:AUTOPILOT_OFF"),
            ("an H: event", " (>H:BBS_SOME_EVENT)", "H:BBS_SOME_EVENT"),
            ("two writes", " 1 (>L:BBS_SOME_OTHER_VAR) 1 (>K:AUTOPILOT_OFF)", "K:AUTOPILOT_OFF, L:BBS_SOME_OTHER_VAR"),
        ]
        for what, extra, named in cases:
            for field, r in (("cw", lever(cw_code=cw + extra)), ("ccw", lever(ccw_code=ccw + extra))):
                with self.subTest(what, field=field):
                    message = (rf"direct_write names {node}: its click code also writes {re.escape(named)} "
                               rf"besides its state var {var}")
                    with self.assertRaisesRegex(g.CurationError, message):
                        self.build([r])
        # more writes of the lever's own var are no side effect: the app writes that var
        self.build([lever(cw_code=cw + f" 0 (>L:{var}, enum)")])

    def test_direct_write_refuses_a_node_that_is_also_under_kinds(self):
        # kinds keeps a control's click code verbatim ("only the kind changes") and direct_write leaves it out: a node under
        # both would build as a toggle with its click code gone.
        node, handle = "BBS757_FIREHANDLE1_CLICK3", apu_fire_handle_row()
        self.assertEqual(g.load_curation(g.DEFAULT_CURATION)["kinds"][node], "toggle")

        with self.assertRaisesRegex(g.CurationError, rf"direct_write and kinds both name: {node}"):
            self.build([handle], direct_write=[self.ET, node])
        self.build([handle])   # the kinds entry alone builds, with the vendor's click code verbatim

    def test_direct_write_refuses_a_control_that_is_not_a_switch(self):
        # Only a switch may have no click code in the map (Every_control_has_a_writer in the C# tests): a toggle, a guard or a
        # select is pressed with its own click code.
        def flip(n):
            return f"(L:{n}, bool) ! (>L:{n}, bool)"

        cases = [
            ("toggle", row(node="B757_PED_T_BUTTON", kind="toggle", left=flip("B757_PED_T_BUTTON"))),
            ("guard", row(node="B757_PED_G_GUARD", kind="guard", left=flip("B757_PED_G_GUARD"))),
            ("select", row(node="B757_PED_S_LIGHT", kind="select button", positions="-",
                           left="1 (>L:B757_PED_S_LIGHT, bool)")),
        ]
        for kind, r in cases:
            with self.subTest(kind):
                with self.assertRaisesRegex(g.CurationError,
                                            rf"direct_write names {r['node']}, which is a {kind}, not a switch"):
                    self.build([r], direct_write=[self.ET, r["node"]])
                self.build([r])   # it builds without the entry, with its own click code
        # the stab trim as the vendor wrote it (a gated toggle), left out of guarded: direct_write is no way round the guard
        stab = self.stab_rows()
        with self.subTest("a gated toggle, the stab trim"):
            with self.assertRaisesRegex(g.CurationError,
                                        rf"direct_write names {stab[1]['node']}, which is a toggle, not a switch"):
                self.build(stab, guarded={}, direct_write=[self.ET, stab[1]["node"]])


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

    GUARDED_ENTRY = {"state_var": "B757_X", "positions": ["OFF", "ON"], "guard": "B757_X_GUARD", "guard_closed": 0,
                     "close_sets": 1}

    def test_the_guarded_key_loads_as_a_dict_and_defaults_to_empty(self):
        self.assertEqual(g.load_curation(self.write({}))["guarded"], {})
        entry = dict(self.GUARDED_ENTRY)
        self.assertEqual(g.load_curation(self.write({"guarded": {"B757_X": entry}}))["guarded"], {"B757_X": entry})
        # closing the guard may leave the switch alone (close_sets null), and a position may be an explicit pair
        other = dict(entry, positions=[{"value": 0, "label": "OFF"}, {"value": 11, "label": "ON"}], close_sets=None,
                     guard_closed=0.5)
        self.assertEqual(g.load_curation(self.write({"guarded": {"B757_X": other}}))["guarded"], {"B757_X": other})

    def test_a_guarded_entry_that_is_not_whole_is_refused(self):
        # All five fields, each of the kind the generator reads: a missing or mistyped one stops the run when the file loads.
        entry = self.GUARDED_ENTRY
        bad = [["B757_X"], {"B757_X": "switch"}, {"B757_X": {}}, {"B757_X": dict(entry, extra=1)}]
        bad += [{"B757_X": {k: v for k, v in entry.items() if k != field}} for field in entry]
        bad += [{"B757_X": dict(entry, **change)} for change in (
            {"state_var": ""}, {"state_var": None}, {"guard": ""}, {"guard": 1}, {"positions": []}, {"positions": "OFF"},
            {"positions": ["OFF", 1]}, {"positions": ["OFF", {"value": 1, "label": "ON"}]}, {"positions": [{"value": 0}]},
            {"guard_closed": "0"}, {"guard_closed": True}, {"guard_closed": None}, {"close_sets": "1"},
            {"close_sets": False})]
        for data in bad:
            with self.subTest(data=data):
                with self.assertRaisesRegex(g.CurationError, r"guarded(\[| must be)"):   # not "unknown key(s) guarded"
                    g.load_curation(self.write({"guarded": data}))

    def test_the_direct_write_key_loads_as_a_list_and_defaults_to_empty(self):
        self.assertEqual(g.load_curation(self.write({}))["direct_write"], [])
        c = g.load_curation(self.write({"direct_write": ["B757_X", "B757_Y"]}))
        self.assertEqual(c["direct_write"], ["B757_X", "B757_Y"])

    def test_a_direct_write_entry_that_is_not_a_node_name_is_refused(self):
        for data in ({"B757_X": 1}, "B757_X", [1], [None], [""], [["B757_X"]], ["B757_X", 2]):
            with self.subTest(data=data):
                with self.assertRaisesRegex(g.CurationError, "direct_write (must be|is a list)"):   # not "unknown key(s)"
                    g.load_curation(self.write({"direct_write": data}))

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
                     "B757_MCP_AP_SPD_HOLD_SW", "BBS757_FIREHANDLE1_CLICK3", "B757_PED_AUDIO_1_MIC_", "B757_PED_AUDIO_2_MIC_",
                     # plan 02 task 3: the guarded switches and their guards
                     "B757_OVHD_PASS_OXY_Toggle_Switch_CLICK", "B757_Stab_trim_C_toggle_switch_CLICK",
                     "B757_Stab_trim_R_toggle_switch_CLICK", "B757_AISLE_STAND_STAB_1_GAURD_SWITCH",
                     "B757_AISLE_STAND_STAB_2_GAURD_SWITCH",
                     # plan 02 task 4: the chronometer ET levers whose click code the map leaves out
                     "B757_MAIN_CHRONO_ET_SWITCH", "B757_MAIN_CHRONO_ET_SWITCH_FO"):
            with self.subTest(node=node):
                self.assertIn(node, notes)
        # the keys that make the kind changes, guard the switches and leave out a click code are explained where the other
        # keys are
        self.assertRegex(notes, r"kinds: node -> toggle or button")
        self.assertRegex(notes, r"guarded: node -> \{state_var, positions, guard, guard_closed, close_sets\}")
        self.assertRegex(notes, r"direct_write: list of nodes whose vendor click code the map leaves out")
        self.assertRegex(notes, r"PLAN 02 TASK 4 .*B757_MAIN_CHRONO_ET_SWITCH_FO are direct_write")
        self.assertIn("support.bluebirdsim.com/docs/simvars", notes)

    # Plan 02, task 3: the brief's values, written out here so a change to them is a visible one.
    GUARDED = {
        "B757_OVHD_PASS_OXY_Toggle_Switch_CLICK": {
            "state_var": "B757_OVHD_PASS_OXY_Toggle_Switch_CLICK", "positions": ["OFF", "ARMED", "ON"],
            "guard": "B757_OVHD_SWITCH_GAURD", "guard_closed": 0, "close_sets": 1},
        "B757_Stab_trim_C_toggle_switch_CLICK": {
            "state_var": "B757_Stab_trim_C_toggle_switch_CLICK", "positions": ["NORMAL", "CUTOUT"],
            "guard": "B757_AISLE_STAND_STAB_1_GAURD_SWITCH", "guard_closed": 0, "close_sets": 0},
        "B757_Stab_trim_R_toggle_switch_CLICK": {
            "state_var": "B757_Stab_trim_R_toggle_switch_CLICK", "positions": ["NORMAL", "CUTOUT"],
            "guard": "B757_AISLE_STAND_STAB_2_GAURD_SWITCH", "guard_closed": 0, "close_sets": 0},
    }

    def test_the_shipped_curation_guards_the_emergency_lights_and_the_two_stab_trim_cutouts(self):
        c = g.load_curation(g.DEFAULT_CURATION)

        self.assertEqual(c["guarded"], self.GUARDED)
        self.assertEqual([c["labels"][node] for node in self.GUARDED],
                         ["Emergency Lights", "Stab Trim C Cutout", "Stab Trim R Cutout"])
        # no other table speaks for a guarded control, and none leaves it out
        for node in self.GUARDED:
            with self.subTest(node=node):
                self.assertNotIn(node, c["positions"])
                self.assertNotIn(node, c["kinds"])
                self.assertNotIn(node, c["exclude_nodes"])
        # the guards keep their own rows (labels the vendor's copies would have got wrong), and the emergency lights switch
        # leads its panel with its guard right after it
        self.assertEqual([c["labels"][entry["guard"]] for entry in c["guarded"].values()],
                         ["Emergency Lights Guard", "Stab Trim C Guard", "Stab Trim R Guard"])
        self.assertEqual(c["control_order"]["Overhead miscellaneous"],
                         ["B757_OVHD_PASS_OXY_Toggle_Switch_CLICK", "B757_OVHD_SWITCH_GAURD"])

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
        "Overhead miscellaneous": ["Emergency Lights", "Emergency Lights Guard"],   # plan 02 task 3: the guarded switch
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


# The vendor's two stab trim templates, trimmed (BBS_752_BASE_INTERIOR.xml: BBS_SWITCH_FLIP_GUARD at 1357 and
# BBS_METAL_2STATE_TOGGLE_SWITCH at 1402): a guard whose click writes RESET_VALUE to the switch it covers when the guard
# closes, and a toggle whose click runs only while that guard reads 1.
GATED_INTERIOR = """<?xml version="1.0"?>
<ModelInfo><Behaviors>
<Template Name="BBS_SWITCH_FLIP_GUARD">
  <Parameters>
    <NODE_ID>#NODE_ID#</NODE_ID>
    <SWITCH_TO_RESET>#SWITCH_TO_RESET#</SWITCH_TO_RESET>
    <RESET_VALUE>1</RESET_VALUE>
  </Parameters>
  <Component ID="#NODE_ID#_comp" Node="#NODE_ID#">
    <UseTemplate Name="ASOBO_GT_Switch_Code">
      <ANIM_NAME>#NODE_ID#</ANIM_NAME>
      <ANIM_CODE>(L:#NODE_ID#, bool) 100 *</ANIM_CODE>
      <LEFT_SINGLE_CODE>
        (L:#NODE_ID#, bool) ! (&gt;L:#NODE_ID#, bool)
        (L:#NODE_ID#, bool) 0 == if{
        #RESET_VALUE# (&gt;L:#SWITCH_TO_RESET#, number)
        }
      </LEFT_SINGLE_CODE>
    </UseTemplate>
  </Component>
</Template>
<Template Name="BBS_METAL_2STATE_TOGGLE_SWITCH">
  <Parameters>
    <NODE_ID>#NODE_ID#</NODE_ID>
    <GUARD_NODE_ID>#GUARD_NODE_ID#</GUARD_NODE_ID>
  </Parameters>
  <Component ID="#NODE_ID#_comp" Node="#NODE_ID#">
    <UseTemplate Name="ASOBO_GT_Anim_Code">
      <ANIM_NAME>#NODE_ID#</ANIM_NAME>
      <ANIM_CODE>(L:#NODE_ID#, number)</ANIM_CODE>
    </UseTemplate>
    <UseTemplate Name="ASOBO_GT_MouseRect">
      <MOUSE_ID>#NODE_ID#</MOUSE_ID>
      <MOUSEFLAGS>LeftSingle</MOUSEFLAGS>
      <CALLBACKCODE>
        (L:#GUARD_NODE_ID#, bool) 1 == if{
        (M:Event) 'LeftSingle' scmp 0 == if{
        (L:#NODE_ID#, number) 1 == if{ 0 } els{ 1 } (&gt;L:#NODE_ID#, number)
        }
        }
      </CALLBACKCODE>
    </UseTemplate>
  </Component>
</Template>
<!-- ======== PEDESTAL GAURD SWITCH GEAR ======== -->
<Component ID="BBS757_STAB_TRIM_CUTOUTS">
  <UseTemplate Name="BBS_SWITCH_FLIP_GUARD">
    <NODE_ID>B757_AISLE_STAND_STAB_1_GAURD_SWITCH</NODE_ID>
    <SWITCH_TO_RESET>B757_Stab_trim_C_toggle_switch_CLICK</SWITCH_TO_RESET>
    <RESET_VALUE>0</RESET_VALUE>
  </UseTemplate>
  <UseTemplate Name="BBS_METAL_2STATE_TOGGLE_SWITCH">
    <NODE_ID>B757_Stab_trim_C_toggle_switch_CLICK</NODE_ID>
    <GUARD_NODE_ID>B757_AISLE_STAND_STAB_1_GAURD_SWITCH</GUARD_NODE_ID>
  </UseTemplate>
</Component>
</Behaviors></ModelInfo>
"""


def write_gated_package(root):
    """write_package's package, its interior replaced by GATED_INTERIOR."""
    pkg = write_package(root)
    (pkg / "SimObjects" / "Airplanes" / "a" / "model" / "interior.xml").write_text(GATED_INTERIOR, encoding="utf-8")
    return pkg


# The vendor's captain's chronometer ET lever, trimmed (BBS_752_BASE_INTERIOR.xml:26713-26737): a finite knob whose
# clockwise code steps the value down and whose anticlockwise code steps it up, jumping HOLD (1) straight to RESET (3).
CHRONO_ET_INTERIOR = """<?xml version="1.0"?>
<ModelInfo><Behaviors>
<!-- ======== CAPT CHRONOMETER ======== -->
<Component ID="CHRONO_LEFT_ET_SWITCH" Node="B757_MAIN_CHRONO_ET_SWITCH">
  <UseTemplate Name="ASOBO_GT_Knob_Finite_Code">
    <ANIM_NAME>B757_MAIN_CHRONO_ET_SWITCH</ANIM_NAME>
    <ANIM_CODE>(L:BBS_CHRONO_LEFT_SWITCH, enum)</ANIM_CODE>
    <ANIMTIP_0>RUN</ANIMTIP_0>
    <ANIMTIP_1>HOLD</ANIMTIP_1>
    <ANIMTIP_2>RESET</ANIMTIP_2>
    <ANIM_LENGTH>3</ANIM_LENGTH>
    <CLOCKWISE_CODE>
      (L:BBS_CHRONO_LEFT_SWITCH, enum) -- d 0 &lt; if{ 0 } (&gt;L:BBS_CHRONO_LEFT_SWITCH, enum)
    </CLOCKWISE_CODE>
    <ANTICLOCKWISE_CODE>
      (L:BBS_CHRONO_LEFT_SWITCH, enum) 1 == if{
      3 (&gt;L:BBS_CHRONO_LEFT_SWITCH, enum)
      } els{
      (L:BBS_CHRONO_LEFT_SWITCH, enum) ++ d 3 &gt; if{ 3 } (&gt;L:BBS_CHRONO_LEFT_SWITCH, enum)
      }
    </ANTICLOCKWISE_CODE>
  </UseTemplate>
</Component>
</Behaviors></ModelInfo>
"""


def write_chrono_package(root):
    """write_package's package, its interior replaced by CHRONO_ET_INTERIOR."""
    pkg = write_package(root)
    (pkg / "SimObjects" / "Airplanes" / "a" / "model" / "interior.xml").write_text(CHRONO_ET_INTERIOR, encoding="utf-8")
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

    def test_a_gated_click_from_the_package_stops_the_run_unless_its_control_is_guarded(self):
        # The extractor's own note, from the vendor's own template shape, meets the generator's check.
        node, guard = "B757_Stab_trim_C_toggle_switch_CLICK", "B757_AISLE_STAND_STAB_1_GAURD_SWITCH"
        with tempfile.TemporaryDirectory() as d:
            pkg = write_gated_package(d)
            out = os.path.join(d, "out", "map.json")
            cur_path = os.path.join(d, "curation.json")
            panels = {"PEDESTAL GAURD SWITCH GEAR": {"name": "Stabilizer trim", "section": "Pedestal"}}

            def run(**curation_):
                with open(cur_path, "w", encoding="utf-8") as fh:
                    json.dump({"panels": panels, **curation_}, fh)
                stdout = io.StringIO()
                with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(io.StringIO()):
                    g.main([str(pkg), "--out", out, "--curation", cur_path])
                return stdout.getvalue()

            with self.assertRaises(SystemExit) as stopped:
                run()
            self.assertRegex(str(stopped.exception), rf"^curation: {node}: press carries the extractor's gate note")
            self.assertFalse(os.path.exists(out), "a stopped run writes no map")

            printed = run(labels={node: "Stab Trim C Cutout"}, guarded={node: {
                "state_var": node, "positions": ["NORMAL", "CUTOUT"], "guard": guard, "guard_closed": 0, "close_sets": 0}})
            with open(out, encoding="utf-8") as fh:
                m = json.load(fh)

        by_node = {c["node"]: c for c in m["controls"]}
        c = by_node[node]
        self.assertEqual((c["kind"], c["label"], c["state_var"], [c[f] for f in ("press", "release", "cw", "ccw")]),
                         ("switch", "Stab Trim C Cutout", node, [None] * 4))
        self.assertEqual((c["guard"], c["guard_closed"], c["close_sets"]), (guard, 0, 0))
        self.assertEqual(by_node[guard]["kind"], "guard", "the guard keeps its own row")
        self.assertIn("guarded controls", printed)
        self.assertIn(f"{node}: re-read", printed)

    def test_a_direct_write_lever_from_the_package_is_emitted_without_its_click_code_and_the_run_names_it(self):
        # The vendor's own XML for the chronometer ET lever through the extractor, the generator and its printed report.
        node, var = "B757_MAIN_CHRONO_ET_SWITCH", "BBS_CHRONO_LEFT_SWITCH"
        pairs = [{"value": v, "label": l} for v, l in ((0, "RUN"), (1, "HOLD"), (3, "RESET"))]
        with tempfile.TemporaryDirectory() as d:
            pkg = write_chrono_package(d)
            out = os.path.join(d, "out", "map.json")
            cur_path = os.path.join(d, "curation.json")

            def run(**curation_):
                with open(cur_path, "w", encoding="utf-8") as fh:
                    json.dump({"panels": {"CAPT CHRONOMETER": "Captain chronometer"}, "positions": {node: pairs},
                               **curation_}, fh)
                stdout = io.StringIO()
                with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(io.StringIO()):
                    g.main([str(pkg), "--out", out, "--curation", cur_path])
                with open(out, encoding="utf-8") as fh:
                    return json.load(fh)["controls"], stdout.getvalue()

            plain, printed_plain = run()
            direct, printed = run(direct_write=[node])

        self.assertEqual(len(plain), 1)
        self.assertEqual(len(direct), 1)
        self.assertEqual((plain[0]["press"], plain[0]["release"]), (None, None))
        self.assertIn("-- d 0 <", plain[0]["cw"], "without the entry the vendor's code is replayed, verbatim")
        self.assertIn(f"1 == if{{ 3 (>L:{var}, enum) }}", plain[0]["ccw"])
        self.assertEqual([direct[0][f] for f in ("press", "release", "cw", "ccw")], [None] * 4)
        self.assertEqual({k: v for k, v in direct[0].items() if k not in ("cw", "ccw")},
                         {k: v for k, v in plain[0].items() if k not in ("cw", "ccw")})
        self.assertEqual((direct[0]["kind"], direct[0]["state_var"], direct[0]["positions"]), ("switch", var, pairs))
        self.assertRegex(printed_plain, r"direct-write\s+: 0 ")
        self.assertNotIn(f"{node}: cw, ccw left out", printed_plain)
        self.assertRegex(printed, r"direct-write\s+: 1 ")
        self.assertIn(f"{node}: cw, ccw left out", printed)


if __name__ == "__main__":
    unittest.main()
