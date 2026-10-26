"""Rules of the BlueBird 757-200 control-map generator, on tiny in-memory rows.

No aircraft package is needed: rows are built by hand in the shape extract_controls.extract() returns, and the
extractor's own new interface (unclipped codes) is checked on a one-control package written to a temp folder. The last
two classes test rpn_replay.py and replay the click code of every control of the committed map.

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
import rpn_replay  # noqa: E402

def row(node="B757_OVHD_TEST_BUTTON", kind="toggle", panel="ELECTRICAL PANEL", line=1, positions="2",
        state=None, tips=None, left=None, release=None, cw=None, ccw=None, other=None, scope=None, file="x.xml",
        component=None):
    """One extractor row, the parts the generator reads. Click codes are given as plain strings. `component` is what the
    extractor keeps of the whole component the control sits in, {"reads": [L:var names], "writes": [...]}: only a row that
    curation's 'levers' names needs it."""
    def items(code):
        return [("", code)] if code else []
    r = {
        "file": file, "line": line, "panel": panel, "node": node, "kind": kind, "positions": positions,
        "_state": ["L:" + node] if state is None else state,
        "_tips": tips or [],
        "_clicks": {"left": items(left), "release": items(release), "cw": items(cw), "ccw": items(ccw),
                    "other": list(other or [])},
        "_scope": scope or {},
    }
    if component is not None:
        r["_component_vars"] = component
    return r


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
    "levers": {},
    "direct_write": [],
    "keep_without_node": [],
    "spelling": {"CENTRE": "Center"},
    "status": [],
}


def curation(**changes):
    c = copy.deepcopy(CURATION)
    c.update(changes)
    return c


def shipped_for(nodes, **changes):
    """The shipped curation cut down to `nodes`: a table entry for a node the package lacks is an error, so each table keeps
    only what the rows have (an order only when every node it lists is there too)."""
    cur = g.load_curation(g.DEFAULT_CURATION)
    here = set(nodes)
    for table in ("labels", "positions", "kinds", "guarded", "levers"):
        cur[table] = {n: v for n, v in cur[table].items() if n in here}
    for table in ("direct_write", "exclude_nodes", "include_nodes", "keep_without_node"):
        cur[table] = [n for n in cur[table] if n in here]
    cur["panel_order"] = {}
    cur["control_order"] = {p: ns for p, ns in cur["control_order"].items() if set(ns) <= here}
    cur.update(changes)
    return cur


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
        for table in ("labels", "positions", "kinds", "guarded", "levers"):
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
    # Ruling R12: the flight deck door switch is a spring-return lever (BBS_752_BASE_INTERIOR.xml:6683-6705). Its clockwise
    # code writes 2 and its anticlockwise code writes 0, whatever it reads, and each stamps the O:var the component's own
    # 1 Hz update counts from to spring the lever back to 1 (XML:6684-6687). So a step to Auto from either end lands on the
    # other end, and a direct write takes every pick.
    DOOR, DOOR_VAR = "B757_OVHD_FLT-DK-DOOR_SWITCH", "B757_OVHD_FLT_DK_DOOR_SWITCH"
    DOOR_CW = f"2 (>L:{DOOR_VAR}) (E:ABSOLUTE TIME, seconds) (>O:UpdateTime)"
    DOOR_CCW = f"0 (>L:{DOOR_VAR}) (E:ABSOLUTE TIME, seconds) (>O:UpdateTime)"
    UNLOCKED_AUTO_DENY = [{"value": 0, "label": "Unlocked"}, {"value": 1, "label": "Auto"}, {"value": 2, "label": "Deny"}]

    def door_row(self):
        """The flight deck door switch as the extractor reads the vendor's: a finite knob with its three tips."""
        return row(node=self.DOOR, kind="rotary knob finite", positions="3", panel="FDAS", state=["L:" + self.DOOR_VAR],
                   tips=["UNLOCKED", "AUTO", "DENY"], cw=self.DOOR_CW, ccw=self.DOOR_CCW)

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
        for table in ("labels", "positions", "kinds", "guarded", "levers"):
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
        self.assertEqual(c["direct_write"][:2], [self.ET, self.ET_FO])
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

    def test_the_flight_deck_door_switch_writes_its_state_directly(self):
        c = g.load_curation(g.DEFAULT_CURATION)
        self.assertEqual(c["direct_write"], [self.ET, self.ET_FO, self.DOOR])

        control, why, slips = g.convert_row(self.door_row(), c)

        self.assertIsNone(why)
        self.assertEqual(control, {
            "node": self.DOOR, "section": "Overhead", "panel": "Flight deck door", "label": "Flight Deck Door",
            "kind": "switch", "state_var": self.DOOR_VAR, "positions": self.UNLOCKED_AUTO_DENY,
            "press": None, "release": None, "hold_ms": 0, "cw": None, "ccw": None})
        self.assertEqual(slips, [], "no click code is replayed, so there is none to suspect")
        # The entry changes the click code and nothing else: without it the same row keeps the vendor's own code, verbatim.
        plain, _, _ = g.convert_row(self.door_row(), dict(c, direct_write=[]))
        self.assertEqual([plain[f] for f in self.CLICK_FIELDS], [None, None, self.DOOR_CW, self.DOOR_CCW])
        self.assertEqual({k: v for k, v in plain.items() if k not in self.CLICK_FIELDS},
                         {k: v for k, v in control.items() if k not in self.CLICK_FIELDS})
        # the map the app embeds: the state var and the three positions, and no click code
        with open(g.DEFAULT_OUT, encoding="utf-8") as fh:
            embedded = {x["node"]: x for x in json.load(fh)["controls"]}
        x = embedded[self.DOOR]
        self.assertEqual((x["kind"], x["state_var"], x["positions"], [x[f] for f in self.CLICK_FIELDS]),
                         ("switch", self.DOOR_VAR, self.UNLOCKED_AUTO_DENY, [None] * 4))

    def test_a_write_to_an_o_var_beside_the_state_var_does_not_stop_direct_write(self):
        # An O:var is component-local, and the sim's calculator cannot set one (spec 3.7 #6: the fire handle relock runs on
        # O:vars it cannot set), so the app's replay of the click never performed that write either: leaving it out loses
        # nothing the app could do. The run builds the control and names what it left out, as for any direct-write control.
        m, report = self.build([self.door_row()], direct_write=[self.ET, self.ET_FO, self.DOOR])
        by_node = {x["node"]: x for x in m["controls"]}

        self.assertEqual([by_node[self.DOOR][f] for f in self.CLICK_FIELDS], [None] * 4)
        self.assertEqual(report["direct_write"][self.DOOR], "cw, ccw left out")
        self.assertEqual(report["suspected_slips"], {})
        # the same row without the entry keeps the vendor's code, the O: write included
        plain, _ = self.build([self.door_row()], direct_write=[self.ET, self.ET_FO])
        self.assertEqual({x["node"]: x for x in plain["controls"]}[self.DOOR]["cw"], self.DOOR_CW)

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
        # The premise of leaving the click code out: it writes nothing but the lever's own L:var (and at most O:vars: the
        # sim's calculator cannot set those, see test_a_write_to_an_o_var_beside_the_state_var_does_not_stop_direct_write).
        # A BlueBird update that adds a write (another L:var, a K: or an H: event) stops the run, since the app would no
        # longer run it, and an O: write beside it does not make it pass.
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
            # the O: write is let through, so it is not named: only what the app could have run and no longer does
            ("another L:var beside an O: write", " (>O:UpdateTime) 1 (>L:BBS_SOME_OTHER_VAR)", "L:BBS_SOME_OTHER_VAR"),
            ("a K: event beside an O: write", " (>O:UpdateTime) 1 (>K:AUTOPILOT_OFF)", "K:AUTOPILOT_OFF"),
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
        # nor is an O: write, on either code: it is component-local and the calculator cannot set it
        self.build([lever(cw_code=cw + " (E:ABSOLUTE TIME, seconds) (>O:UpdateTime)", ccw_code=ccw + " 1 (>O:Other, bool)")])
        # nor is a read of anything: only a write is lost
        self.build([lever(cw_code=cw + " (E:ABSOLUTE TIME, seconds) (L:BBS_SOME_OTHER_VAR) (O:BBS_LOCAL) +")])

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


class Levers(unittest.TestCase):
    """Plan 02, task 7: curation's 'levers'. The gear lever and the flap lever are drag handles: the extractor reads each as a
    "lever", which the rules leave out (no click code a pick could replay), and the pilot needs both. The after-takeoff
    checklist's "gear UP, then OFF" needs the OFF detent the simulator's gear keys cannot select. BlueBird publishes each
    lever's state var with its detents ("Write a detent position", support.bluebirdsim.com/docs/simvars), so an entry gives the
    state var and the positions with their values, and the control is built as a switch with no click code: the app writes the
    state var to the picked position, as for any switch with none (Bbs757Write.ToPosition). Nothing here sends anything to a
    sim, the gear lever's UP least of all. The rows have the shape the extractor gives the vendor's own controls
    (BBS_752_BASE_INTERIOR.xml: the gear lever's component at 25038, the flap lever's at 32844) and are converted with the
    SHIPPED curation."""
    GEAR, GEAR_VAR = "B757_MAIN_Landing_Gear_Y", "B757_GEAR_HANDLE_POS"
    FLAP, FLAP_VAR = "BBS757_lever_flaps", "B757_FC_FLAP_LEVER"
    LOCK_OVRD = "B757_MAIN_LOCK_OVRD_SWITCH"
    GEAR_POSITIONS = [{"value": 0, "label": "UP"}, {"value": 0.5, "label": "OFF"}, {"value": 1, "label": "DOWN"}]
    FLAP_POSITIONS = [{"value": v, "label": l} for v, l in
                      ((0, "UP"), (17, "1"), (41, "5"), (67, "15"), (95, "20"), (125, "25"), (150, "30"))]
    CLICK_FIELDS = ("press", "release", "cw", "ccw")
    LEVER_DRAG = ("DRAG_CODE",
                  "(A:SIM ON GROUND,bool) 0 == if{ (O:_Delta,number) 8 * (>L:B757_GEAR_HANDLE_POS_DELTA,number) }")
    GRAB = "0 (>L:B757_GEAR_HANDLE_GRAB,bool) 0 (>L:B757_GEAR_HANDLE_PULL,bool) 0 (>L:B757_GEAR_HANDLE_POS_DELTA,number)"

    def gear_row(self):
        """The gear lever as the extractor reads it: the node the pilot grabs, whose own animation is the pull of the handle
        (L:B757_GEAR_HANDLE_PULL) and whose own code writes the grab, the pull and the drag delta. B757_GEAR_HANDLE_POS is in
        none of that: the component's other animation (the lever's rotation) reads it and its Update sets it."""
        return row(node=self.GEAR, kind="lever", positions="", panel="LANDING GEAR", line=25069,
                   state=["L:B757_GEAR_HANDLE_PULL"], release=self.GRAB,
                   other=[self.LEVER_DRAG,
                          ("LOCK_CODE", "0 (>O:_Delta,number) 1 (>L:B757_GEAR_HANDLE_GRAB,bool) "
                                        "1 (>L:B757_GEAR_HANDLE_PULL,bool) 0 (>L:B757_GEAR_HANDLE_POS_DELTA,number)"),
                          ("UNLOCK_CODE", self.GRAB)],
                   component={"reads": [self.GEAR_VAR, "B757_GEAR_HANDLE_PULL"],
                              "writes": [self.GEAR_VAR, "B757_GEAR_HANDLE_POS_DELTA", "B757_GEAR_HANDLE_GRAB",
                                         "B757_GEAR_HANDLE_PULL"]})

    def flap_row(self):
        """The flap lever as the extractor reads it: one drag template whose animation reads the state var and whose drag and
        axis codes write it, under a banner curation leaves out (RIGHT WINDOW RAIN EFFECTS)."""
        var = self.FLAP_VAR
        return row(node=self.FLAP, kind="lever", positions="range 0..150 step 1", panel="RIGHT WINDOW RAIN EFFECTS",
                   line=32845, state=["L:" + var], scope={"TOOLTIPID": "Flap Lever"},
                   other=[("DRAG_CODE", f"150 (M:DragPercent) * (>L:{var},number)"),
                          ("NEGATIVE_AXIS_CODE", f"(L:{var},number) 1 + 150 min (>L:{var},number)"),
                          ("POSITIVE_AXIS_CODE", f"(L:{var},number) 1 - 0 max (>L:{var},number)")],
                   component={"reads": [var], "writes": [var]})

    def lock_override_row(self):
        """The gear lock override toggle, the one control the Landing gear panel has without the lever (cockpit order puts
        it first: its line is before the lever's)."""
        var = "BBS757_GEAR_LOCK_OVRD_BUTTON"
        return row(node=self.LOCK_OVRD, kind="toggle", panel="LANDING GEAR", line=25026, state=["L:" + var],
                   left=f"(L:{var}, bool) ! (>L:{var}, bool)")

    def stand_rows(self):
        """The six controls of the Control stand as the extractor reads them (all under excluded banners), in cockpit order."""
        flip = "(L:B757_FC_PARK_BRAKE,bool) ! (>L:B757_FC_PARK_BRAKE,bool)"
        rows = {"BBS757_PEDESTAL_Park_Brake_Lever": row(
            node="BBS757_PEDESTAL_Park_Brake_Lever", panel="RIGHT WINDOW RAIN EFFECTS", line=32700,
            state=["L:B757_FC_PARK_BRAKE"], left=flip)}
        for i, (node, value) in enumerate((("BBS757_SPOILERDOWN_CLICK", 0), ("BBS757_SPOILERARMED_CLICK", 10),
                                           ("BBS757_SPOILERUP_CLICK", 100))):
            rows[node] = row(node=node, kind="momentary button", positions="-", panel="RIGHT WINDOW RAIN EFFECTS",
                             line=32800 + i, state=[], left=f"{value} (>L:B757_FC_SPOILER_LEVER, number)")
        rows["ENGINE_Switch_ATDisc_1"] = row(
            node="ENGINE_Switch_ATDisc_1", kind="momentary button", positions="-", panel="THROTTLES MAIN", line=33000,
            state=["L:GO_AROUND_PADDLE1"], left="1 (>L:GO_AROUND_PADDLE1, bool) 1 (>K:AUTO_THROTTLE_TO_GA)",
            release="0 (>L:GO_AROUND_PADDLE1, bool)")
        rows["B757_AT_DISC_1"] = row(
            node="B757_AT_DISC_1", kind="momentary button", positions="-", panel="THROTTLES MAIN", line=33010,
            state=["L:B757_AT_DISC_1"], left="1 (>L:B757_AT_DISC_1, bool)", release="0 (>L:B757_AT_DISC_1, bool)")
        return rows

    def rows(self):
        return {self.LOCK_OVRD: self.lock_override_row(), self.GEAR: self.gear_row(), self.FLAP: self.flap_row()}

    def build(self, rows=None, cockpit=None, **changes):
        rows = rows or self.rows()
        return g.build_map(list(rows.values()), shipped_for(rows, **changes), "pkg", "1", cockpit)

    def entry(self, node, **changes):
        """The shipped 'levers' table with some fields of one node's entry changed: whatever a test changes is the one thing
        that is wrong with it."""
        table = shipped_for(self.rows())["levers"]
        table[node] = dict(table[node], **changes)
        return table

    def test_the_gear_and_flap_levers_are_switches_with_no_click_code(self):
        rows, c = self.rows(), shipped_for(self.rows())

        def switch(node, section, panel, label, state_var, positions):
            return {"node": node, "section": section, "panel": panel, "label": label, "kind": "switch",
                    "state_var": state_var, "positions": positions,
                    "press": None, "release": None, "hold_ms": 0, "cw": None, "ccw": None}

        expected = {
            self.GEAR: switch(self.GEAR, "Forward panel", "Landing gear", "Gear Lever", self.GEAR_VAR, self.GEAR_POSITIONS),
            self.FLAP: switch(self.FLAP, "Pedestal", "Control stand", "Flap Lever", self.FLAP_VAR, self.FLAP_POSITIONS),
        }
        for node, want in expected.items():
            with self.subTest(node=node):
                control, why, slips = g.convert_row(rows[node], c)

                self.assertIsNone(why)
                self.assertEqual(control, want)
                self.assertEqual(list(control), list(want), "the keys keep the JSON order")
                self.assertEqual([control[f] for f in self.CLICK_FIELDS], [None] * 4,
                                 "none of the vendor's drag code is replayed: the app writes the state var")
                self.assertEqual(slips, [], "no click code is replayed, so there is none to suspect")

    def test_without_the_entry_the_rules_leave_a_lever_out_and_nothing_else_takes_it_in(self):
        rows, c = self.rows(), shipped_for(self.rows())
        bare = dict(c, levers={})

        self.assertEqual(g.drop_reason(rows[self.GEAR], c), "lever")
        self.assertEqual(g.drop_reason(rows[self.FLAP], c), "lever", "include_nodes lets it past its excluded banner")
        self.assertEqual(g.convert_row(rows[self.GEAR], bare), (None, "lever", []))
        self.assertEqual(g.convert_row(rows[self.FLAP], bare), (None, "lever", []))
        # the flap lever's banner is curation's own word: an entry for it does not override that (include_nodes does)
        self.assertEqual(g.drop_reason(rows[self.FLAP], dict(c, include_nodes=[])), "excluded banner")
        # the include_nodes entry alone is not enough either: the rules still drop it, and the run stops on that
        with self.assertRaisesRegex(g.CurationError, rf"include_nodes name nodes the rules still leave out: "
                                                     rf"{self.FLAP} \(lever\)"):
            self.build(levers={})

    def test_the_gear_lever_leads_its_panel(self):
        # Cockpit order puts the lock override first (its line is before the lever's); the shipped control_order does not.
        m, _ = self.build()

        self.assertEqual([(c["label"], c["section"]) for c in m["controls"] if c["panel"] == "Landing gear"],
                         [("Gear Lever", "Forward panel"), ("Gear Lock Override", "Forward panel")])
        plain, _ = self.build(control_order={})
        self.assertEqual([c["label"] for c in plain["controls"] if c["panel"] == "Landing gear"],
                         ["Gear Lock Override", "Gear Lever"])

    def test_the_flap_lever_follows_speedbrake_up_on_the_control_stand(self):
        rows = {**self.stand_rows(), self.FLAP: self.flap_row()}

        m, _ = self.build(rows)

        stand = [(c["label"], c["section"], c["panel"]) for c in m["controls"] if c["panel"] == "Control stand"]
        self.assertEqual([label for label, _, _ in stand],
                         ["Parking Brake", "Speedbrake Armed", "Speedbrake Down", "Speedbrake Up", "Flap Lever", "TO/GA",
                          "A/T Disconnect"])
        self.assertEqual({(section, panel) for _, section, panel in stand}, {("Pedestal", "Control stand")})

    def test_the_run_names_each_lever_it_takes_in_and_counts_none_as_dropped(self):
        m, report = self.build()

        self.assertEqual(sorted(report["levers"]), sorted((self.GEAR, self.FLAP)))
        for node in (self.GEAR, self.FLAP):
            self.assertEqual(report["levers"][node], "taken in (the rules alone leave it out: lever)")
        self.assertEqual(report["dropped"], collections.Counter(), "a lever taken in is not counted as dropped")
        self.assertEqual(report["taken_in"], [self.FLAP], "taken_in stays include_nodes' own")
        self.assertEqual({c["node"] for c in m["controls"]}, {self.LOCK_OVRD, self.GEAR, self.FLAP})
        # none named, none reported, and the rules' own count says what they do with a lever (the gear lever's place in its
        # panel's order goes too: an order that names a node the map does not have is a stale one, and stops the run)
        with self.assertRaisesRegex(g.CurationError, rf"control_order lists nodes that are not in Landing gear: "
                                                     rf"{self.GEAR} \(not in the map\)"):
            self.build(levers={}, include_nodes=[])
        _, none = self.build(levers={}, include_nodes=[], control_order={})
        self.assertEqual(none["levers"], {})
        self.assertEqual(none["dropped"], collections.Counter({"lever": 1, "excluded banner": 1}))

    def test_a_levers_entry_that_does_not_fit_the_package_stops_the_run(self):
        # Each case changes the shipped curation, or adds one row, in one way and nothing else, so the one check it is meant
        # for is the one that stops the run; the shipped curation itself passes all of them.
        gear, flap = self.GEAR, self.FLAP
        ghost = "B757_NO_SUCH_NODE"
        breaker = row(node="B757_OVHD_X_CB", kind="circuit breaker", panel="ELECTRICAL PANEL", left="x")
        extra = {breaker["node"]: breaker}
        entry = self.entry(gear)[gear]
        cases = [
            ("a node the package does not have", {}, dict(levers={**self.entry(gear), ghost: entry}),
             rf"levers name nodes the package does not have: {ghost}"),
            ("a node the rules keep", {}, dict(levers={**self.entry(gear), self.LOCK_OVRD: entry}),
             rf"levers names {self.LOCK_OVRD}, which the rules keep as a toggle"),
            ("a node the rules leave out for another reason", extra,
             dict(levers={**self.entry(gear), breaker["node"]: entry}),
             rf"levers names {breaker['node']}, which the rules leave out for another reason than being a lever "
             r"\(circuit breaker\)"),
            ("a node curation leaves out", {}, dict(exclude_nodes=[gear]),
             rf"levers names {gear}, which curation also leaves out \(excluded node\)"),
            ("a node under a banner curation leaves out", {}, dict(include_nodes=[]),
             rf"levers names {flap}, which curation also leaves out \(excluded banner\)"),
            ("a state var the lever's code never touches", {}, dict(levers=self.entry(gear, state_var=self.GEAR_VAR + "_X")),
             rf"{gear}'s own vendor code never reads or writes {self.GEAR_VAR}_X"),
            ("another lever's state var", {}, dict(levers=self.entry(flap, state_var=self.GEAR_VAR)),
             rf"{flap}'s own vendor code never reads or writes {self.GEAR_VAR} "),
            ("a position past the range the code clamps to",
             {}, dict(levers=self.entry(flap, positions=self.FLAP_POSITIONS[:-1] + [{"value": 151, "label": "30"}])),
             rf"{flap} lists the position 151, which is outside the range 0..150 its own code clamps to"),
            ("a position below that range",
             {}, dict(levers=self.entry(flap, positions=[{"value": -1, "label": "UP"}] + self.FLAP_POSITIONS[1:])),
             rf"{flap} lists the position -1, which is outside the range 0..150"),
            ("a guarded entry too", {}, dict(guarded={gear: dict(state_var=self.GEAR_VAR, positions=["UP", "DOWN"],
                                                                 guard="G", guard_closed=0, close_sets=None)}),
             rf"levers and guarded both name: {gear}"),
            ("a direct_write entry too", {}, dict(direct_write=[gear]), rf"levers and direct_write both name: {gear}"),
            ("a kinds entry too", {}, dict(kinds={gear: "button"}), rf"levers and kinds both name: {gear}"),
            ("a positions entry too", {}, dict(positions={gear: ["UP", "OFF", "DOWN"]}),
             rf"levers and positions both name: {gear}"),
        ]
        for what, more, changes, message in cases:
            with self.subTest(what):
                with self.assertRaisesRegex(g.CurationError, message):
                    self.build({**self.rows(), **more}, **changes)
        self.build()                                    # the shipped curation passes each of them: alone,
        self.build({**self.rows(), **extra})            # and with the extra row

    def test_the_state_var_is_checked_against_the_whole_component_not_the_nodes_own_row(self):
        # The gear lever's state var is in neither the row's state (the pull animation) nor its click codes: only the lever's
        # other animation reads it and the component's Update sets it, which the extractor keeps as _component_vars. A row
        # without them (an extractor that kept none) cannot confirm any state var, and the entry is refused.
        rows = self.rows()
        codes = [code for slot in rows[self.GEAR]["_clicks"].values() for _, code in slot]
        touched = {name for code in codes for _write, _kind, name in extract_controls.var_refs(code)}
        self.assertNotIn(self.GEAR_VAR, touched)
        self.assertNotIn("L:" + self.GEAR_VAR, rows[self.GEAR]["_state"])
        self.build(rows)

        del rows[self.GEAR]["_component_vars"]
        with self.assertRaisesRegex(g.CurationError, rf"{self.GEAR}'s own vendor code never reads or writes "
                                                     rf"{self.GEAR_VAR} \(its component reads no L:var and writes no L:var\)"):
            self.build(rows)
        # a write is as good as a read, either confirms the var
        rows = self.rows()
        rows[self.FLAP]["_component_vars"] = {"reads": [], "writes": [self.FLAP_VAR]}
        self.build(rows)
        rows[self.FLAP]["_component_vars"] = {"reads": [self.FLAP_VAR], "writes": []}
        self.build(rows)

    def test_a_lever_no_cockpit_has_stops_the_run_unless_keep_without_node_names_it(self):
        both = g.Cockpit(pw={self.LOCK_OVRD, self.GEAR, self.FLAP}, rr={self.LOCK_OVRD, self.GEAR, self.FLAP})
        no_gear = g.Cockpit(pw={self.LOCK_OVRD, self.FLAP}, rr={self.LOCK_OVRD, self.FLAP})

        m, _ = self.build(cockpit=both)
        self.assertEqual({c["node"] for c in m["controls"]}, {self.LOCK_OVRD, self.GEAR, self.FLAP})
        with self.assertRaisesRegex(g.CurationError, rf"levers names {self.GEAR}, which neither cockpit model has a node for"):
            self.build(cockpit=no_gear)
        m, report = self.build(cockpit=no_gear, keep_without_node=[self.GEAR])
        self.assertIn(self.GEAR, {c["node"] for c in m["controls"]})
        self.assertEqual(report["kept_without_node"], [self.GEAR])
        # one model's cockpit has it: it stays, and its label says whose
        only_pw = g.Cockpit(pw={self.LOCK_OVRD, self.GEAR, self.FLAP}, rr={self.LOCK_OVRD, self.FLAP})
        m, _ = self.build(cockpit=only_pw)
        self.assertEqual({c["node"]: c["label"] for c in m["controls"]}[self.GEAR], "Gear Lever (PW)")

    def test_the_embedded_map_holds_the_two_levers_on_their_panels(self):
        with open(g.DEFAULT_OUT, encoding="utf-8") as fh:
            controls = json.load(fh)["controls"]
        by_node = {c["node"]: c for c in controls}

        for node, panel, section, label, var, positions in (
                (self.GEAR, "Landing gear", "Forward panel", "Gear Lever", self.GEAR_VAR, self.GEAR_POSITIONS),
                (self.FLAP, "Control stand", "Pedestal", "Flap Lever", self.FLAP_VAR, self.FLAP_POSITIONS)):
            with self.subTest(node=node):
                x = by_node[node]
                self.assertEqual((x["kind"], x["section"], x["panel"], x["label"], x["state_var"], x["positions"]),
                                 ("switch", section, panel, label, var, positions))
                self.assertEqual([x[f] for f in self.CLICK_FIELDS], [None] * 4)
                self.assertNotIn("guard", x)
        self.assertEqual([c["label"] for c in controls if c["panel"] == "Landing gear"][:1], ["Gear Lever"])
        stand = [c["label"] for c in controls if c["panel"] == "Control stand"]
        self.assertEqual(stand[stand.index("Speedbrake Up") + 1], "Flap Lever")


class CockpitPresence(unittest.TestCase):
    """Plan 02, task 5 (ruling R65): the map holds only controls the cockpit has. BlueBird ships six interior models (a PW
    and an RR cockpit, each with an AWE and a no-cab variant, which agree on every control node today); the generator
    reads the two standard ones (BBS_752_PW_INTERIOR.gltf, BBS_752_RR_INTERIOR.gltf), and a control can be clicked only
    where a node of its name is in the model. A control whose node is in neither model is left out, counted and named like
    every other drop; a control that one model alone has says which, with (PW) or (RR) at the end of its label. The rows
    have the shape the extractor gives (row()); the cockpit is the two models' node names (g.Cockpit)."""
    BOTH, PW_ONLY, RR_ONLY, NEITHER = ("B757_OVHD_ALPHA_BUTTON", "B757_OVHD_BRAVO_BUTTON", "B757_OVHD_CHARLIE_BUTTON",
                                       "B757_OVHD_DELTA_BUTTON")
    STAB, GUARD = "B757_Stab_trim_C_toggle_switch_CLICK", "B757_AISLE_STAND_STAB_1_GAURD_SWITCH"

    @staticmethod
    def cockpit(pw=(), rr=()):
        return g.Cockpit(pw=set(pw), rr=set(rr))

    def models(self):
        """Alpha is in both models, Bravo in the PW one only, Charlie in the RR one only and Delta in neither."""
        return self.cockpit(pw=[self.BOTH, self.PW_ONLY], rr=[self.BOTH, self.RR_ONLY])

    @staticmethod
    def toggles(*nodes):
        """One toggle on its own L:var per node, in the Electrical panel, in the order given."""
        return [row(node=n, line=i, left=f"(L:{n},bool) ! (>L:{n},bool)") for i, n in enumerate(nodes, 1)]

    def build(self, rows, cockpit=None, **changes):
        return g.build_map(rows, curation(**changes), "pkg", "1", self.models() if cockpit is None else cockpit)

    def test_cockpit_presence(self):
        pw, rr = {"BOTH", "PW_ONLY"}, {"BOTH", "RR_ONLY"}

        self.assertEqual([g.cockpit_presence(n, pw, rr) for n in ("BOTH", "PW_ONLY", "RR_ONLY", "NEITHER")],
                         ["both", "pw", "rr", "none"])
        # a node is matched whole and as written: not as the start of a longer name, not in another case
        self.assertEqual([g.cockpit_presence(n, pw, rr) for n in ("BOT", "BOTH_2", "both", "Pw_Only")], ["none"] * 4)
        self.assertEqual(g.cockpit_presence("BOTH", set(), set()), "none")
        self.assertEqual(g.cockpit_presence("PW_ONLY", pw, set()), "pw")

    def test_absent_controls_dropped(self):
        rows = self.toggles(self.BOTH, self.PW_ONLY, self.RR_ONLY, self.NEITHER)
        models = self.models()

        control, why, slips = g.convert_row(rows[3], curation(), models)

        self.assertEqual((control, why, slips), (None, "no cockpit node", []))
        self.assertEqual(g.NO_COCKPIT_NODE, "no cockpit node")
        self.assertEqual([g.drop_reason(r, curation(), models) for r in rows], [None, None, None, "no cockpit node"])
        self.assertIsNone(g.drop_reason(rows[3], curation()),
                          "given no models nothing is judged: the other rules' tests have none")

        m, report = g.build_map(rows, curation(), "pkg", "1", models)

        self.assertEqual([c["node"] for c in m["controls"]], [self.BOTH, self.PW_ONLY, self.RR_ONLY])
        # counted by reason like every other drop, and named, with the label it would have had and its vendor banner
        # (the app panel it would have been on is whatever curation makes of it now, which the report does not claim)
        self.assertEqual(report["dropped"], collections.Counter({"no cockpit node": 1}))
        self.assertEqual(report["absent"], {self.NEITHER: {"banner": "ELECTRICAL PANEL", "label": "Delta"}})
        # the other rules come first, so their counts stay what they were: a lever no cockpit has is still a lever
        lever = row(node=self.NEITHER, kind="lever", positions="", left="1 (>L:X)")
        banner = row(node=self.NEITHER, panel="CB OVERHEAD PANEL", left="1 (>L:X)")
        self.assertEqual(g.drop_reason(lever, curation(), models), "lever")
        self.assertEqual(g.drop_reason(banner, curation(), models), "excluded banner")
        _, report = g.build_map([lever], curation(), "pkg", "1", models)
        self.assertEqual((report["dropped"], report["absent"]), (collections.Counter({"lever": 1}), {}))

    def test_a_panel_the_drop_empties_is_not_emitted_and_an_order_that_names_it_stops_the_run(self):
        # Panels exist only through their controls: when none is left, the panel is gone from the map (and from the app,
        # which builds its panels from the map's controls). An order entry for it would do nothing, so it is an error.
        fuel = row(node=self.NEITHER, panel="FUEL PANEL", line=2, left="(L:X,bool) ! (>L:X,bool)")
        rows = self.toggles(self.BOTH) + [fuel]

        m, _ = self.build(rows)

        self.assertEqual({(c["section"], c["panel"]) for c in m["controls"]}, {("Overhead", "Electrical")})
        with self.assertRaisesRegex(g.CurationError, "panel_order names panels Overhead does not have: Fuel"):
            self.build(rows, panel_order={"Overhead": ["Electrical", "Fuel"]})
        with self.assertRaisesRegex(g.CurationError, "control_order names a panel no control is on: Fuel"):
            self.build(rows, control_order={"Fuel": [self.NEITHER]})
        with self.assertRaisesRegex(g.CurationError, rf"control_order lists nodes that are not in Electrical: "
                                                     rf"{self.NEITHER} \(not in the map\)"):
            self.build(rows, control_order={"Electrical": [self.BOTH, self.NEITHER]})

    def test_a_curation_entry_never_rescues_a_control_no_cockpit_has(self):
        # kinds, labels and positions about a control the rules drop change nothing and are only reported, as for any drop.
        r = row(node=self.NEITHER, kind="momentary button", positions="-", left="1 (>L:X)")
        for table, entry in (("kinds", {self.NEITHER: "toggle"}), ("labels", {self.NEITHER: "Delta"}),
                             ("positions", {self.NEITHER: ["Off", "On"]})):
            with self.subTest(table):
                m, report = self.build([r], **{table: entry})

                self.assertEqual(m["controls"], [])
                self.assertEqual(report["dropped"], collections.Counter({"no cockpit node": 1}))
                self.assertEqual(report["overrides_on_dropped"], [self.NEITHER], "reported, never silent")
        self.assertEqual(g.convert_row(r, curation(kinds={self.NEITHER: "toggle"}), self.models()),
                         (None, "no cockpit node", []))
        # a direct_write entry is stale once its node is dropped: the run stops until it is removed
        with self.assertRaisesRegex(g.CurationError, rf"direct_write names {self.NEITHER}, which the map does not keep "
                                                     r"\(the rules leave it out: no cockpit node\)"):
            self.build([r], direct_write=[self.NEITHER])

    GUARDED = {STAB: {"state_var": STAB, "positions": ["NORMAL", "CUTOUT"], "guard": GUARD, "guard_closed": 0,
                      "close_sets": 0}}

    def stab_rows(self):
        """The stab trim C cutout and its guard in the vendor's shape (BBS_752_BASE_INTERIOR.xml:15381-15397)."""
        return [
            row(node=self.GUARD, kind="guard", panel="PEDESTAL GAURD SWITCH GEAR", line=1, state=["L:" + self.GUARD],
                left=f"(L:{self.GUARD}, bool) ! (>L:{self.GUARD}, bool) "
                     f"(L:{self.GUARD}, bool) 0 == if{{ 0 (>L:{self.STAB}, number) }}"),
            row(node=self.STAB, kind="toggle", panel="PEDESTAL GAURD SWITCH GEAR", line=2, state=["L:" + self.STAB],
                left=f"[if (L:{self.GUARD}, bool) 1 ==] (L:{self.STAB}, number) 1 == if{{ 0 }} els{{ 1 }} "
                     f"(>L:{self.STAB}, number)"),
        ]

    def test_a_guarded_control_and_its_guard_must_have_their_nodes(self):
        # A guarded entry takes in what the other rules leave out, but never what no cockpit has: a switch with no node is
        # a stale entry, and a guard with no node could never be opened.
        rows = self.stab_rows()
        both = self.cockpit(pw=[self.STAB, self.GUARD], rr=[self.STAB, self.GUARD])

        m, _ = self.build(rows, both, guarded=self.GUARDED)
        self.assertEqual([c["node"] for c in m["controls"]], [self.GUARD, self.STAB])

        no_switch = self.cockpit(pw=[self.GUARD], rr=[self.GUARD])
        with self.assertRaisesRegex(g.CurationError, rf"guarded names {self.STAB}, which neither cockpit model has"):
            self.build(rows, no_switch, guarded=self.GUARDED)
        no_guard = self.cockpit(pw=[self.STAB], rr=[self.STAB])
        with self.assertRaisesRegex(g.CurationError, rf"the guard {self.GUARD} of {self.STAB} has no row in the map "
                                                     r"\(the rules leave it out: no cockpit node\)"):
            self.build(rows, no_guard, guarded=self.GUARDED)

    EMERGENCY, EMERGENCY_GUARD = "B757_OVHD_PASS_OXY_Toggle_Switch_CLICK", "B757_OVHD_SWITCH_GAURD"
    EMERGENCY_ENTRY = {EMERGENCY: {"state_var": EMERGENCY, "positions": ["OFF", "ARMED", "ON"], "guard": EMERGENCY_GUARD,
                                   "guard_closed": 0, "close_sets": 1}}

    def emergency_rows(self):
        """The emergency lights switch and its guard in the vendor's shape (BBS_752_BASE_INTERIOR.xml:8106-8120). The
        switch's animation reads an O: var, so the other rules leave it out ("state is not an L:var") and curation's
        guarded entry TAKES IT IN: the shape in which the cockpit rule, the last of the rules, is never the reason they give."""
        node, guard = self.EMERGENCY, self.EMERGENCY_GUARD

        def gated(code):
            return f"[if (L:{guard}, bool) 1 ==] {code}"

        up = f"(L:{node}, number) 1 + 2 min (>L:{node}, number)"
        down = f"(L:{node}, number) 1 - 0 max (>L:{node}, number)"
        return [
            row(node=guard, kind="guard", panel="LIGHT OVERRIDE", line=1, state=["L:" + guard],
                left=f"(L:{guard}, bool) ! (>L:{guard}, bool) (L:{guard}, bool) 0 == if{{ 1 (>L:{node}, number) }}"),
            row(node=node, kind="multi-position switch", positions="3", panel="LIGHT OVERRIDE", line=2,
                state=[f"O:{node}_SUBMOVEMENT"], left=gated(up), other=[("RightSingle", gated(down))]),
        ]

    def test_a_taken_in_guarded_switch_needs_its_node_too(self):
        # The other rules leave this switch out and say so ("state is not an L:var"), so the cockpit rule, which comes last,
        # never answers for it: a guarded entry has to ask the cockpit itself, or it would keep a switch no cockpit has.
        rows, entry, node, guard = self.emergency_rows(), self.EMERGENCY_ENTRY, self.EMERGENCY, self.EMERGENCY_GUARD
        both = self.cockpit(pw=[node, guard], rr=[node, guard])
        self.assertEqual(g.drop_reason(rows[1], curation(), both), "state is not an L:var")

        m, report = self.build(rows, both, guarded=entry)
        self.assertEqual([c["node"] for c in m["controls"]], [guard, node])
        self.assertRegex(report["guarded"][node], r"^taken in .*the rules alone leave it out: state is not an L:var")

        no_switch = self.cockpit(pw=[guard], rr=[guard])
        with self.assertRaisesRegex(g.CurationError, rf"guarded names {node}, which neither cockpit model has"):
            self.build(rows, no_switch, guarded=entry)
        # one model's cockpit has it: it stays, and its label says whose
        m, _ = self.build(rows, self.cockpit(pw=[node, guard], rr=[guard]), guarded=entry, labels={node: "Emergency Lights"})
        self.assertEqual({c["node"]: c["label"] for c in m["controls"]}[node], "Emergency Lights (PW)")
        # keep_without_node is the way to keep it, and the run names it
        m, report = self.build(rows, no_switch, guarded=entry, keep_without_node=[node])
        self.assertEqual([c["node"] for c in m["controls"]], [guard, node])
        self.assertEqual(report["kept_without_node"], [node])
        # its guard is a control of its own and needs its node as well
        with self.assertRaisesRegex(g.CurationError, rf"the guard {guard} of {node} has no row in the map "
                                                     r"\(the rules leave it out: no cockpit node\)"):
            self.build(rows, self.cockpit(pw=[node], rr=[node]), guarded=entry)

    def outcome(self, rows, node, keep=False, **changes):
        """What the run does with `node` when no cockpit model has it (every other node of `rows` is in both models): "kept",
        "left out" or "stops the run". `keep` lists it under keep_without_node."""
        others = [r["node"] for r in rows if r["node"] != node]
        try:
            m, _ = self.build(rows, self.cockpit(pw=others, rr=others), keep_without_node=[node] if keep else [], **changes)
        except g.CurationError:
            return "stops the run"
        return "kept" if node in {c["node"] for c in m["controls"]} else "left out"

    def test_a_control_no_cockpit_has_is_never_kept_whichever_mechanism_takes_it_in_unless_keep_without_node_names_it(self):
        # The contract, by every curation mechanism that takes a control in, re-reads it or builds it, in the shapes where the
        # other rules keep it (only the cockpit rule drops it) and where they leave it out (an earlier rule answers first,
        # because the cockpit rule comes last). Without keep_without_node the control is never in the map: the run leaves it
        # out or stops. With it, the control is kept exactly where the rules would keep it, and the entry stops the run where
        # they still would not.
        node = self.NEITHER
        flip = f"(L:{node},bool) ! (>L:{node},bool)"
        banner, included = "RIGHT WINDOW RAIN EFFECTS", "BBS757_PEDESTAL_Park_Brake_Lever"
        taken_in = dict(exclude_banners=["CB OVERHEAD PANEL", banner], include_nodes=[included],
                        node_panels=[{"prefix": "BBS757_PEDESTAL_", "panel": "Control stand", "section": "Pedestal"}])
        toggle = [row(node=node, left=flip)]
        lever = [row(node=node, kind="lever", positions="", left="1 (>L:X)")]
        # a drag handle whose own code and component read and write its state var, and curation's levers entry for it
        drag = [("DRAG_CODE", f"150 (M:DragPercent) * (>L:{node},number)")]
        own = {"reads": [node], "writes": [node]}
        handle = [row(node=node, kind="lever", positions="", other=drag, component=own)]
        under_banner = [row(node=node, kind="lever", positions="", panel=banner, other=drag, component=own)]
        entry = {node: {"state_var": node, "positions": [{"value": 0, "label": "UP"}, {"value": 150, "label": "DOWN"}]}}
        handle_taken_in = dict(exclude_banners=["CB OVERHEAD PANEL", banner], include_nodes=[node],
                               node_panels=[{"prefix": node, "panel": "Control stand", "section": "Pedestal"}])
        cases = [
            # what, rows, the control asked about, curation, without keep_without_node, with it
            ("no entry at all", toggle, node, {}, "left out", "kept"),
            ("a label and positions", toggle, node, dict(labels={node: "Delta"}, positions={node: ["Off", "On"]}),
             "left out", "kept"),
            ("a kind, on a control the rules keep",
             [row(node=node, kind="momentary button", positions="-", state=["L:" + node], left=f"1 (>L:{node}, bool)")],
             node, dict(kinds={node: "toggle"}), "left out", "kept"),
            ("a kind, on a control the rules leave out", lever, node, dict(kinds={node: "toggle"}),
             "left out", "stops the run"),
            ("direct_write, on a switch the rules keep",
             [row(node=node, kind="multi-position switch", positions="3", state=["L:" + node],
                  left=f"(L:{node}) 1 + 3 % (>L:{node})")],
             node, dict(direct_write=[node]), "stops the run", "kept"),
            ("guarded, re-read: the rules keep the stab trim", self.stab_rows(), self.STAB, dict(guarded=self.GUARDED),
             "stops the run", "kept"),
            ("guarded, taken in: the rules leave the emergency lights switch out", self.emergency_rows(), self.EMERGENCY,
             dict(guarded=self.EMERGENCY_ENTRY), "stops the run", "kept"),
            ("levers: the rules leave a lever out", handle, node, dict(levers=entry), "stops the run", "kept"),
            ("levers, under a banner curation leaves out and include_nodes takes in", under_banner, node,
             dict(levers=entry, **handle_taken_in), "stops the run", "kept"),
            ("include_nodes, on a control the rules keep",
             [row(node=included, panel=banner, state=["L:B"], left="(L:B,bool) ! (>L:B,bool)")], included, taken_in,
             "stops the run", "kept"),
            ("include_nodes, on a control the rules leave out",
             [row(node=included, kind="lever", positions="", panel=banner, left="1 (>L:X)")], included, taken_in,
             "stops the run", "stops the run"),
        ]
        for what, rows, asked, changes, without, with_keep in cases:
            with self.subTest(what):
                self.assertEqual(self.outcome(rows, asked, **changes), without)
                self.assertEqual(self.outcome(rows, asked, keep=True, **changes), with_keep)

    def test_a_taken_in_node_no_cockpit_has_stops_the_run(self):
        banner, node = "RIGHT WINDOW RAIN EFFECTS", "BBS757_PEDESTAL_Park_Brake_Lever"
        rows = [row(node=node, panel=banner, state=["L:B"], left="(L:B,bool) ! (>L:B,bool)")]
        changes = dict(exclude_banners=["CB OVERHEAD PANEL", banner], include_nodes=[node],
                       node_panels=[{"prefix": "BBS757_PEDESTAL_", "panel": "Control stand", "section": "Pedestal"}])

        m, _ = self.build(rows, self.cockpit(pw=[node], rr=[node]), **changes)
        self.assertEqual([c["node"] for c in m["controls"]], [node])

        with self.assertRaisesRegex(g.CurationError, rf"include_nodes name nodes the rules still leave out: "
                                                     rf"{node} \(no cockpit node\)"):
            self.build(rows, self.cockpit(), **changes)

    def test_variant_suffix_not_doubled(self):
        rows = self.toggles(self.BOTH, self.PW_ONLY, self.RR_ONLY)

        m, report = self.build(rows)

        self.assertEqual([c["label"] for c in m["controls"]], ["Alpha", "Bravo (PW)", "Charlie (RR)"])
        self.assertEqual(report["variant_only"], {
            self.PW_ONLY: {"variant": "pw", "label": "Bravo (PW)"},
            self.RR_ONLY: {"variant": "rr", "label": "Charlie (RR)"}})
        self.assertEqual(report["dropped"], collections.Counter(), "a control one model has is kept")
        # a label that already ends with its variant is left as it is, never "(PW) (PW)": the fuel control switches
        named = {self.PW_ONLY: "L Fuel Control (PW)", self.RR_ONLY: "L Fuel Control (RR)"}
        m, report = self.build(rows, labels=named)
        self.assertEqual([c["label"] for c in m["controls"]][1:], ["L Fuel Control (PW)", "L Fuel Control (RR)"])
        self.assertEqual(report["variant_only"][self.PW_ONLY]["label"], "L Fuel Control (PW)")
        # and a second pass changes nothing
        g.mark_variants(m["controls"], self.models())
        self.assertEqual([c["label"] for c in m["controls"]][1:], ["L Fuel Control (PW)", "L Fuel Control (RR)"])
        # the shipped curation's four fuel control labels are such labels
        c = g.load_curation(g.DEFAULT_CURATION)
        fuel = {"BBS_fuelcut_PW1": "pw", "BBS_fuelcut_PW2": "pw", "BBS_fuelcut_RR1": "rr", "BBS_fuelcut_RR2": "rr"}
        controls = [{"node": n, "label": c["labels"][n]} for n in fuel]
        found = g.mark_variants(controls, self.cockpit(pw=[n for n, v in fuel.items() if v == "pw"],
                                                       rr=[n for n, v in fuel.items() if v == "rr"]))
        self.assertEqual([x["label"] for x in controls],
                         ["L Fuel Control (PW)", "R Fuel Control (PW)", "L Fuel Control (RR)", "R Fuel Control (RR)"])
        self.assertEqual(found, fuel)

    def test_one_name_for_a_control_per_model_is_two_names_not_a_number(self):
        # The cockpits never have both, but the app lists both: the suffix tells them apart before any numbering does.
        rows = self.toggles(self.BOTH, self.PW_ONLY, self.RR_ONLY)

        m, _ = self.build(rows, labels={self.PW_ONLY: "Fuel Control", self.RR_ONLY: "Fuel Control"})
        self.assertEqual([c["label"] for c in m["controls"]][1:], ["Fuel Control (PW)", "Fuel Control (RR)"])

        m, _ = self.build(rows, labels={self.BOTH: "Fuel Control", self.PW_ONLY: "Fuel Control"})
        self.assertEqual([c["label"] for c in m["controls"]][:2], ["Fuel Control", "Fuel Control (PW)"])

        # two controls of one model that still share a name are numbered after the suffix, as any repeat is
        m, _ = self.build(rows, labels={self.PW_ONLY: "Fuel Control", self.RR_ONLY: "Fuel Control"},
                          cockpit=self.cockpit(pw=[self.BOTH, self.PW_ONLY, self.RR_ONLY], rr=[self.BOTH]))
        self.assertEqual([c["label"] for c in m["controls"]][1:], ["Fuel Control (PW)", "Fuel Control (PW) 2"])

    def test_a_label_that_names_another_variant_than_the_cockpit_has_stops_the_run(self):
        rows = self.toggles(self.BOTH, self.PW_ONLY)
        cases = [
            ("RR on a control only the PW cockpit has", {self.PW_ONLY: "Bravo (RR)"},
             rf"{self.PW_ONLY}: its label 'Bravo \(RR\)' says only the RR cockpit has it, but only the PW cockpit does"),
            ("PW on a control both have", {self.BOTH: "Alpha (PW)"},
             rf"{self.BOTH}: its label 'Alpha \(PW\)' says only the PW cockpit has it, but both do"),
            ("RR on a control both have", {self.BOTH: "Alpha (RR)"},
             rf"{self.BOTH}: its label 'Alpha \(RR\)' says only the RR cockpit has it, but both do"),
        ]
        for what, labels, message in cases:
            with self.subTest(what):
                with self.assertRaisesRegex(g.CurationError, message):
                    self.build(rows, labels=labels)

    def test_without_models_no_label_is_touched(self):
        m, report = g.build_map(self.toggles(self.BOTH, self.PW_ONLY), curation(), "pkg", "1")

        self.assertEqual([c["label"] for c in m["controls"]], ["Alpha", "Bravo"])
        self.assertEqual((report["absent"], report["variant_only"], report["kept_without_node"]), ({}, {}, []))

    # Curation's keep_without_node: a control BlueBird's published list proves real although neither model has a node of its
    # name. Like every curation entry it is reported, and one that is stale, conflicting or does nothing stops the run.
    def test_keep_without_node_keeps_a_control_no_cockpit_has_and_the_run_names_it(self):
        rows = self.toggles(self.BOTH, self.NEITHER)

        m, report = self.build(rows, keep_without_node=[self.NEITHER])

        # kept as the rules build it, with no variant in its label: neither cockpit has it
        self.assertEqual([(c["node"], c["label"]) for c in m["controls"]], [(self.BOTH, "Alpha"), (self.NEITHER, "Delta")])
        self.assertEqual(report["kept_without_node"], [self.NEITHER])
        self.assertEqual((report["absent"], report["variant_only"], report["dropped"]), ({}, {}, collections.Counter()))
        m, report = self.build(rows)   # without the entry it is left out
        self.assertEqual(([c["node"] for c in m["controls"]], report["kept_without_node"]), ([self.BOTH], []))

    def test_keep_without_node_errors(self):
        lever = row(node="B757_PED_LEVER", kind="lever", positions="", left="1 (>L:X)")
        rows = self.toggles(self.BOTH, self.NEITHER) + [lever]
        cases = [
            ("a node the package does not have", dict(keep_without_node=["B757_NO_SUCH_NODE"]),
             "keep_without_node name nodes the package does not have: B757_NO_SUCH_NODE"),
            ("a node a cockpit model has", dict(keep_without_node=[self.BOTH]),
             rf"keep_without_node names nodes a cockpit model has \(the entry does nothing, remove it\): {self.BOTH}"),
            ("a node curation also leaves out", dict(keep_without_node=[self.NEITHER], exclude_nodes=[self.NEITHER]),
             rf"keep_without_node and exclude_nodes both name: {self.NEITHER}"),
            ("a node the rules still leave out", dict(keep_without_node=[lever["node"]]),
             rf"keep_without_node names nodes the rules still leave out: {lever['node']} \(lever\)"),
        ]
        for what, changes, message in cases:
            with self.subTest(what):
                with self.assertRaisesRegex(g.CurationError, message):
                    self.build(rows, **changes)
        self.build(rows, keep_without_node=[self.NEITHER])

    def test_a_kept_control_cannot_name_the_wrong_variant(self):
        # in neither model, so a (PW) or (RR) label on it is as wrong as on a control the other model has
        with self.assertRaisesRegex(g.CurationError, rf"{self.NEITHER}: its label 'Delta \(PW\)' says only the PW cockpit "
                                                     r"has it, but neither does"):
            self.build(self.toggles(self.BOTH, self.NEITHER), keep_without_node=[self.NEITHER],
                       labels={self.NEITHER: "Delta (PW)"})

    # What BlueBird's two interior models say of the map the app embeds (read from the models 2026-10-26: PW 3104 nodes, RR
    # 3116, 3094 in both). The generator is run against the installed package, so this is what its run report names.
    ABSENT = (
        "B757_EICAS_COMPUTER_KNOB",   # a second EICAS Computer knob: the EICAS computer switch is B757_ECAM_LRAUTO_KNOB
        "B757_PED_ADF_TONE_SWITCH", "B757_PED_ADF_L_TEST_SWITCH", "B757_PED_ADF_R_TEST_SWITCH", "B757_PED_ADF_L_SWITCH",
        "B757_PED_ADF_R_SWITCH",      # the ADF panel's five switches: its tuning knobs have nodes, and are dropped as knobs
        "B757_PED_WX_R_TEST_BUTTON",  # the left weather radar test button has a node, the right one has none
        "B757_P6_DISPLAY_1_EADI_BUTTON", "B757_P6_DISPLAY_1_EHSI_BUTTON", "B757_P6_DISPLAY_1_EICAS_BUTTON",
        "B757_P6_DISPLAY_2_EICAS_BUTTON", "B757_P6_DISPLAY_2_EADI_BUTTON", "B757_P6_DISPLAY_2_EHSI_BUTTON",
        "B757_P6_EVENT_AUTO_TEST_SWITCH_BUTTON", "B757_P6_EVENT_MAN_TEST_SWITCH_BUTTON",
        "B757_P6_EVENT_MAIN_TEST_SWITCH_BUTTON", "B757_P6_EVENT_REC_TEST_SWITCH_BUTTON",
        "B757_P6_EVENT_ERASE_TEST_SWITCH_BUTTON",
        "B757_GAURD_13_SWITCH", "B757_GAURD_14_SWITCH", "B757_GAURD_15_SWITCH", "B757_P61_PANEL_SWITCH_GAURD",
    )
    VARIANT_ONLY = {
        "BBS_fuelcut_PW1": "L Fuel Control (PW)", "BBS_fuelcut_PW2": "R Fuel Control (PW)",
        "BBS_fuelcut_RR1": "L Fuel Control (RR)", "BBS_fuelcut_RR2": "R Fuel Control (RR)",
        "B757_OVHD_L_ELEC-ENG-CONT_BUTTON": "L ELEC ENG Cont (RR)",
        "B757_OVHD_R_ELEC-ENG-CONT_BUTTON": "R ELEC ENG Cont (RR)",
        "B757_GAURD_1_SWITCH": "Elec Eng Cont 1 Guard (RR)", "B757_GAURD_2_SWITCH": "Elec Eng Cont 2 Guard (RR)",
    }

    def embedded(self):
        with open(g.DEFAULT_OUT, encoding="utf-8") as fh:
            return json.load(fh)

    def test_the_embedded_map_holds_only_controls_the_cockpit_has(self):
        controls = self.embedded()["controls"]
        by_node = {c["node"]: c for c in controls}

        self.assertEqual(len(self.ABSENT), 22)
        self.assertEqual(sorted(n for n in self.ABSENT if n in by_node), [], "no node in either model, yet in the map")
        # the ADF panel held only those five switches and is gone; the other panels lose just the controls named above
        self.assertNotIn("ADF", {c["panel"] for c in controls})
        self.assertIn("B757_PED_WX_L_TEST_BUTTON", by_node)
        self.assertIn("B757_ECAM_LRAUTO_KNOB", by_node)
        # the six guarded switches and guards of task 3 are in both models: they stay
        for node in ("B757_OVHD_PASS_OXY_Toggle_Switch_CLICK", "B757_Stab_trim_C_toggle_switch_CLICK",
                     "B757_Stab_trim_R_toggle_switch_CLICK", "B757_OVHD_SWITCH_GAURD",
                     "B757_AISLE_STAND_STAB_1_GAURD_SWITCH", "B757_AISLE_STAND_STAB_2_GAURD_SWITCH"):
            self.assertIn(node, by_node)

    def test_the_embedded_map_labels_what_one_model_alone_has_and_nothing_else(self):
        controls = self.embedded()["controls"]
        by_node = {c["node"]: c for c in controls}

        self.assertEqual({n: by_node[n]["label"] for n in self.VARIANT_ONLY}, self.VARIANT_ONLY)
        tagged = {c["node"] for c in controls if re.search(r"\((PW|RR)\)$", c["label"])}
        self.assertEqual(tagged, set(self.VARIANT_ONLY))
        self.assertEqual([c["label"] for c in controls if re.search(r"\((PW|RR)\).*\((PW|RR)\)", c["label"])], [])

    def test_no_curation_entry_lingers_for_a_control_the_map_dropped(self):
        c = g.load_curation(g.DEFAULT_CURATION)
        by_node = {x["node"]: x for x in self.embedded()["controls"]}

        self.assertEqual(c["keep_without_node"], [], "BlueBird's list names a variable, not a cockpit control")
        for table in ("labels", "positions", "kinds", "guarded"):
            self.assertEqual([n for n in self.ABSENT if n in c[table]], [], table)
        self.assertEqual([n for n in self.ABSENT if n in c["direct_write"] or n in c["include_nodes"]], [])
        for panel, nodes in c["control_order"].items():
            self.assertEqual([n for n in nodes if n in self.ABSENT], [], panel)
        # every node rule still places a control of the map: the ADF rule went with the switches it named
        for rule in c["node_panels"]:
            if "prefix" in rule:
                with self.subTest(prefix=rule["prefix"]):
                    self.assertTrue(any(n.startswith(rule["prefix"]) for n in by_node), "a rule that places nothing")

    def test_the_run_leaves_out_what_the_cockpit_lacks_and_names_it(self):
        nodes = (self.BOTH, self.PW_ONLY, self.RR_ONLY, self.NEITHER)
        with tempfile.TemporaryDirectory() as d:
            pkg = write_presence_package(d, nodes, pw=[self.BOTH, self.PW_ONLY], rr=[self.BOTH, self.RR_ONLY])
            out = os.path.join(d, "out", "map.json")
            cur_path = os.path.join(d, "curation.json")
            with open(cur_path, "w", encoding="utf-8") as fh:
                json.dump({"panels": {"ELECTRICAL PANEL": "Electrical"}}, fh)
            with contextlib.redirect_stdout(io.StringIO()) as stdout:
                g.main([str(pkg), "--out", out, "--curation", cur_path])
            with open(out, encoding="utf-8") as fh:
                m = json.load(fh)
        printed = stdout.getvalue()

        self.assertEqual([(c["node"], c["label"]) for c in m["controls"]],
                         [(self.BOTH, "Alpha"), (self.PW_ONLY, "Bravo (PW)"), (self.RR_ONLY, "Charlie (RR)")])
        self.assertRegex(printed, r"no cockpit node\s*: 1 ")
        self.assertIn(f"{self.NEITHER}: Delta (vendor banner ELECTRICAL PANEL)", printed)
        self.assertRegex(printed, r"1\s+no cockpit node")   # in the counts of what was dropped, with the other reasons
        self.assertRegex(printed, r"one variant only\s*: 2 ")
        self.assertIn(f"{self.PW_ONLY}: PW only, Bravo (PW)", printed)
        self.assertIn(f"{self.RR_ONLY}: RR only, Charlie (RR)", printed)
        self.assertRegex(printed, rf"{g.INTERIOR_MODELS['pw']}.*{g.INTERIOR_MODELS['rr']}")

    def test_the_run_names_a_control_kept_without_a_node(self):
        with tempfile.TemporaryDirectory() as d:
            pkg = write_presence_package(d, (self.BOTH, self.NEITHER), pw=[self.BOTH], rr=[self.BOTH])
            out = os.path.join(d, "out", "map.json")
            cur_path = os.path.join(d, "curation.json")
            with open(cur_path, "w", encoding="utf-8") as fh:
                json.dump({"panels": {"ELECTRICAL PANEL": "Electrical"}, "keep_without_node": [self.NEITHER]}, fh)
            with contextlib.redirect_stdout(io.StringIO()) as stdout:
                g.main([str(pkg), "--out", out, "--curation", cur_path])
            with open(out, encoding="utf-8") as fh:
                nodes = [c["node"] for c in json.load(fh)["controls"]]

        self.assertEqual(nodes, [self.BOTH, self.NEITHER])
        self.assertRegex(stdout.getvalue(), r"kept without node\s*: 1 ")
        self.assertIn(f"{self.NEITHER}: kept without a cockpit node", stdout.getvalue())


class CockpitModels(unittest.TestCase):
    """The two interior models are read from the package: the names of their nodes, and nothing else. A package without
    both cannot tell which controls the cockpit has, so the run stops (a CurationError, as for any curation that does not
    fit the package) rather than keep, or drop, every control."""

    def package(self, **models):
        d = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, d, ignore_errors=True)
        write_cockpit_models(pathlib.Path(d), **models)
        return pathlib.Path(d)

    def test_the_node_names_of_each_model_are_read(self):
        pkg = self.package(pw=["A", "B", "PW_ONLY"], rr=["A", "B", "RR_ONLY"])

        cockpit = g.read_cockpit(pkg)

        self.assertEqual((cockpit.pw, cockpit.rr), ({"A", "B", "PW_ONLY"}, {"A", "B", "RR_ONLY"}))
        self.assertEqual(g.INTERIOR_MODELS, {"pw": "BBS_752_PW_INTERIOR.gltf", "rr": "BBS_752_RR_INTERIOR.gltf"})

    def test_a_missing_model_stops_the_run(self):
        for variant, kept in (("pw", {"rr": ["A"]}), ("rr", {"pw": ["A"]})):
            with self.subTest(missing=variant):
                pkg = self.package(**{"pw": None, "rr": None, **kept})

                with self.assertRaisesRegex(g.CurationError, rf"{g.INTERIOR_MODELS[variant]}.*is not in "):
                    g.read_cockpit(pkg)
        with self.assertRaises(g.CurationError):
            g.read_cockpit(self.package(pw=None, rr=None))

    def test_main_stops_without_writing_a_map_when_a_model_is_missing(self):
        with tempfile.TemporaryDirectory() as d:
            pkg = write_package(d)
            (pkg / "SimObjects" / "Airplanes" / "bbs-757-200-rr" / "model" / g.INTERIOR_MODELS["rr"]).unlink()
            out = os.path.join(d, "out", "map.json")
            cur_path = os.path.join(d, "curation.json")
            with open(cur_path, "w", encoding="utf-8") as fh:
                json.dump({}, fh)
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as stopped:
                    g.main([str(pkg), "--out", out, "--curation", cur_path])
            written = os.path.exists(out)

        self.assertIn(g.INTERIOR_MODELS["rr"], str(stopped.exception))
        self.assertFalse(written, "a stopped run writes no map")

    def test_a_model_that_cannot_be_read_as_a_gltf_with_nodes_stops_the_run(self):
        # an empty node list would drop every control without a word: it is an error, never an empty cockpit
        cases = [
            ("not JSON", "{not json"),
            ("not an object", "[]"),
            ("no nodes", json.dumps({"asset": {"version": "2.0"}})),
            ("an empty node list", json.dumps({"nodes": []})),
            ("nodes without names", json.dumps({"nodes": [{"mesh": 0}, {"children": [0]}]})),
        ]
        for what, text in cases:
            with self.subTest(what):
                pkg = self.package()
                (pkg / "SimObjects" / "Airplanes" / "bbs-757-200-pw" / "model" / g.INTERIOR_MODELS["pw"]).write_text(
                    text, encoding="utf-8")

                with self.assertRaisesRegex(g.CurationError, rf"{g.INTERIOR_MODELS['pw']}"):
                    g.read_cockpit(pkg)

    def test_a_byte_order_mark_does_not_hide_the_nodes(self):
        pkg = self.package()
        path = pkg / "SimObjects" / "Airplanes" / "bbs-757-200-pw" / "model" / g.INTERIOR_MODELS["pw"]
        path.write_bytes(b"\xef\xbb\xbf" + json.dumps({"nodes": [{"name": "A"}]}).encode("utf-8"))

        self.assertEqual(g.read_cockpit(pkg).pw, {"A"})

    def test_a_model_found_in_two_places_is_ambiguous_and_stops_the_run(self):
        pkg = self.package()
        copy_ = pkg / "SimObjects" / "Airplanes" / "bbs-757-200-pw-copy" / "model"
        copy_.mkdir(parents=True)
        (copy_ / g.INTERIOR_MODELS["pw"]).write_text(json.dumps({"nodes": [{"name": "Z"}]}), encoding="utf-8")

        with self.assertRaisesRegex(g.CurationError, rf"{g.INTERIOR_MODELS['pw']}.* more than one"):
            g.read_cockpit(pkg)


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

    LEVER_ENTRY = {"state_var": "B757_X",
                   "positions": [{"value": 0, "label": "UP"}, {"value": 1, "label": "DOWN"}]}

    def test_the_levers_key_loads_as_a_dict_and_defaults_to_empty(self):
        self.assertEqual(g.load_curation(self.write({}))["levers"], {})
        entry = copy.deepcopy(self.LEVER_ENTRY)
        self.assertEqual(g.load_curation(self.write({"levers": {"B757_X": entry}}))["levers"], {"B757_X": entry})
        # a fractional value is a position like any other: the gear lever's OFF detent is 0.5
        half = dict(entry, positions=[{"value": 0, "label": "UP"}, {"value": 0.5, "label": "OFF"},
                                      {"value": 1, "label": "DOWN"}])
        self.assertEqual(g.load_curation(self.write({"levers": {"B757_X": half}}))["levers"], {"B757_X": half})

    def test_a_levers_entry_that_is_not_whole_is_refused(self):
        # A state var and at least two positions with their values (a lever's row names no values a list of names could be
        # laid onto), the values rising in the order of the lever's travel and each label its own.
        entry = self.LEVER_ENTRY
        up, down = {"value": 0, "label": "UP"}, {"value": 1, "label": "DOWN"}
        bad = [["B757_X"], {"B757_X": "lever"}, {"B757_X": {}}, {"B757_X": dict(entry, extra=1)}]
        bad += [{"B757_X": {k: v for k, v in entry.items() if k != field}} for field in entry]
        bad += [{"B757_X": dict(entry, positions=positions)} for positions in (
            [], "UP", [up], ["UP", "DOWN"], [up, {"value": 1}], [up, {"label": "DOWN"}], [up, dict(down, extra=1)],
            [up, {"value": "1", "label": "DOWN"}], [up, {"value": True, "label": "DOWN"}], [up, dict(down, label="")],
            [up, dict(down, label=1)], [up, dict(down, value=0)], [down, up], [up, dict(down, label="up")])]
        bad += [{"B757_X": dict(entry, state_var=state_var)} for state_var in ("", None, 1)]
        for data in bad:
            with self.subTest(data=data):
                with self.assertRaisesRegex(g.CurationError, r"levers(\[| must be)"):   # not "unknown key(s) levers"
                    g.load_curation(self.write({"levers": data}))

    def test_the_direct_write_key_loads_as_a_list_and_defaults_to_empty(self):
        self.assertEqual(g.load_curation(self.write({}))["direct_write"], [])
        c = g.load_curation(self.write({"direct_write": ["B757_X", "B757_Y"]}))
        self.assertEqual(c["direct_write"], ["B757_X", "B757_Y"])

    def test_a_direct_write_entry_that_is_not_a_node_name_is_refused(self):
        for data in ({"B757_X": 1}, "B757_X", [1], [None], [""], [["B757_X"]], ["B757_X", 2]):
            with self.subTest(data=data):
                with self.assertRaisesRegex(g.CurationError, "direct_write (must be|is a list)"):   # not "unknown key(s)"
                    g.load_curation(self.write({"direct_write": data}))

    def test_the_keep_without_node_key_loads_as_a_list_and_defaults_to_empty(self):
        self.assertEqual(g.load_curation(self.write({}))["keep_without_node"], [])
        c = g.load_curation(self.write({"keep_without_node": ["B757_X", "B757_Y"]}))
        self.assertEqual(c["keep_without_node"], ["B757_X", "B757_Y"])

    def test_a_keep_without_node_entry_that_is_not_a_node_name_is_refused(self):
        for data in ({"B757_X": 1}, "B757_X", [1], [None], [""], [["B757_X"]], ["B757_X", 2]):
            with self.subTest(data=data):
                with self.assertRaisesRegex(g.CurationError, "keep_without_node (must be|is a list)"):   # not unknown key
                    g.load_curation(self.write({"keep_without_node": data}))

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
        # plan 02 task 7: the flap lever, a lever the rules leave out too (it is also under levers, which takes it in)
        "BBS757_lever_flaps": "RIGHT WINDOW RAIN EFFECTS",
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
                     "B757_MAIN_CHRONO_ET_SWITCH", "B757_MAIN_CHRONO_ET_SWITCH_FO",
                     # plan 02 task 6 (ruling R12): the flight deck door switch, left out the same way
                     "B757_OVHD_FLT-DK-DOOR_SWITCH",
                     # plan 02 task 7: the levers the rules leave out, and the state vars their own code reads and writes
                     "B757_MAIN_Landing_Gear_Y", "BBS757_lever_flaps", "B757_GEAR_HANDLE_POS", "B757_FC_FLAP_LEVER"):
            with self.subTest(node=node):
                self.assertIn(node, notes)
        # the keys that make the kind changes, guard the switches and leave out a click code are explained where the other
        # keys are
        self.assertRegex(notes, r"kinds: node -> toggle or button")
        self.assertRegex(notes, r"guarded: node -> \{state_var, positions, guard, guard_closed, close_sets\}")
        self.assertRegex(notes, r"direct_write: list of nodes whose vendor click code the map leaves out")
        self.assertRegex(notes, r"PLAN 02 TASK 4 .*B757_MAIN_CHRONO_ET_SWITCH_FO are direct_write")
        # the door switch: why a direct write (absolute cw and ccw), why its own spring-back still works (the 1 Hz update),
        # why an O: write may be left out (the calculator cannot set one), and whose the presentation is
        self.assertRegex(notes, r"PLAN 02 TASK 6 .*B757_OVHD_FLT-DK-DOOR_SWITCH is direct_write")
        self.assertRegex(notes, r"PLAN 02 TASK 6 .*XML:6684-6687")
        self.assertRegex(notes, r"PLAN 02 TASK 6 .*calculator cannot set")
        self.assertRegex(notes, r"PLAN 02 TASK 6 .*plan 12")
        self.assertRegex(notes, r"direct_write: .*O:vars")   # the key's own explanation says what it lets through
        # the lever key is explained where the others are, and the task's note cites BlueBird's wording and the vendor's code
        # (XML:line) for each state var: the gear lever's is set by its component's Update and shown by its rotation animation,
        # the flap lever's is read by its animation and written by its drag
        self.assertRegex(notes, r"levers: node -> \{state_var, positions\}")
        self.assertRegex(notes, r"PLAN 02 TASK 7 .*B757_GEAR_HANDLE_POS.*XML:25042.*XML:25052")
        self.assertRegex(notes, r"PLAN 02 TASK 7 .*B757_FC_FLAP_LEVER.*XML:32850.*XML:32855")
        self.assertRegex(notes, r"PLAN 02 TASK 7 .*Write a detent position")
        self.assertRegex(notes, r"PLAN 02 TASK 7 .*unmeasured in the sim")
        self.assertIn("support.bluebirdsim.com/docs/simvars", notes)

    def test_the_cockpit_presence_rule_and_what_it_removed_have_their_evidence_in_the_notes(self):
        with open(g.DEFAULT_CURATION, encoding="utf-8-sig") as fh:
            notes = "\n".join(json.load(fh)["_notes"])

        # the key is explained where the others are, and the task's own note says what the two models hold
        self.assertRegex(notes, r"keep_without_node: list of nodes")
        self.assertRegex(notes, r"PLAN 02 TASK 5 .*BBS_752_PW_INTERIOR\.gltf.*BBS_752_RR_INTERIOR\.gltf")
        # why none is kept: BlueBird's list names variables, and the run's own stop on a stale entry stays the check
        self.assertRegex(notes, r"PLAN 02 TASK 5 .*support\.bluebirdsim\.com/docs/simvars")
        self.assertRegex(notes, r"PLAN 02 TASK 5 .*keep_without_node (stays|is) empty")
        # every control it left out and every one it labeled by variant is named, so a regeneration that changes either set
        # is a change someone sees
        for node in CockpitPresence.ABSENT + tuple(CockpitPresence.VARIANT_ONLY):
            with self.subTest(node=node):
                self.assertIn(node, notes)
        # the exemption task 1 left for the second EICAS Computer knob went with the control
        self.assertNotIn("plan 02 task 5 drops it", notes)
        self.assertNotIn("stays numbered", notes)

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

    # Plan 02, task 7: BlueBird's published detents (support.bluebirdsim.com/docs/simvars, read 2026-10-26: the gear handle's
    # "0 = UP; 0.5 = OFF; 1 = DOWN", the flap lever's "0 = UP; 17 = 1; 41 = 5; 67 = 15; 95 = 20; 125 = 25; 150 = 30"),
    # written out here so a change to them is a visible one.
    LEVERS = {
        "B757_MAIN_Landing_Gear_Y": {
            "state_var": "B757_GEAR_HANDLE_POS",
            "positions": [{"value": 0, "label": "UP"}, {"value": 0.5, "label": "OFF"}, {"value": 1, "label": "DOWN"}]},
        "BBS757_lever_flaps": {
            "state_var": "B757_FC_FLAP_LEVER",
            "positions": [{"value": 0, "label": "UP"}, {"value": 17, "label": "1"}, {"value": 41, "label": "5"},
                          {"value": 67, "label": "15"}, {"value": 95, "label": "20"}, {"value": 125, "label": "25"},
                          {"value": 150, "label": "30"}]},
    }

    def test_the_shipped_curation_gives_the_gear_and_flap_levers_bluebirds_detents(self):
        c = g.load_curation(g.DEFAULT_CURATION)
        gear, flap = self.LEVERS

        self.assertEqual(c["levers"], self.LEVERS)
        self.assertEqual([c["labels"][node] for node in self.LEVERS], ["Gear Lever", "Flap Lever"])
        # no other table speaks for a lever, and none leaves it out
        for node in self.LEVERS:
            with self.subTest(node=node):
                self.assertNotIn(node, c["positions"])
                self.assertNotIn(node, c["kinds"])
                self.assertNotIn(node, c["guarded"])
                self.assertNotIn(node, c["direct_write"])
                self.assertNotIn(node, c["exclude_nodes"])
        # the gear lever's banner is a panel of its own; the flap lever's banner is curation's own exclusion, so the flap lever
        # is taken in by node (which names the banner it is under) and placed by a node rule
        self.assertNotIn(gear, c["include_nodes"])
        self.assertIn(flap, c["include_nodes"])
        self.assertIn("RIGHT WINDOW RAIN EFFECTS", c["exclude_banners"])
        self.assertEqual(g.panel_of(row(node=gear, panel="LANDING GEAR"), c), ("Landing gear", "Forward panel"))
        self.assertEqual(g.panel_of(row(node=flap, panel="RIGHT WINDOW RAIN EFFECTS"), c), ("Control stand", "Pedestal"))
        # the gear lever is first on its panel, the flap lever is right after Speedbrake Up
        self.assertEqual(c["control_order"]["Landing gear"], [gear])
        stand = c["control_order"]["Control stand"]
        self.assertEqual(stand[stand.index("BBS757_SPOILERUP_CLICK") + 1], flap)
        self.assertEqual(len(stand), len(set(stand)))

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
        # no ADF panel: its five switches have no node in either cockpit model (plan 02 task 5), so nothing is left on it
        "Pedestal": ["Control stand", "Engines", "TCAS and transponder", "EICAS control", "Radio tuning panel 1",
                     "Audio panel 1", "Fire protection", "Stabilizer trim", "Weather radar left",
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
        "Control stand": ["Parking Brake", "Speedbrake Armed", "Speedbrake Down", "Speedbrake Up", "Flap Lever", "TO/GA",
                          "A/T Disconnect"],   # plan 02 task 7: the flap lever right after Speedbrake Up
        "Landing gear": ["Gear Lever"],        # plan 02 task 7: first on its panel, the lock override follows in cockpit order
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

# The nodes the fixture packages' interiors use, so that the cockpit holds them all (write_cockpit_models).
FIXTURE_NODES = ("B757_OVHD_BATTERY_BUTTON", "B757_AISLE_STAND_STAB_1_GAURD_SWITCH",
                 "B757_Stab_trim_C_toggle_switch_CLICK", "B757_MAIN_CHRONO_ET_SWITCH")


def write_cockpit_models(pkg, pw=FIXTURE_NODES, rr=FIXTURE_NODES):
    """The two interior models the generator reads for the cockpit (g.INTERIOR_MODELS), in the folders BlueBird keeps them
    in. A glTF is JSON and the generator reads only the names of its nodes, so each holds nothing else. A variant given as
    None gets no model."""
    for variant, names in (("pw", pw), ("rr", rr)):
        if names is None:
            continue
        folder = pathlib.Path(pkg, "SimObjects", "Airplanes", f"bbs-757-200-{variant}", "model")
        folder.mkdir(parents=True, exist_ok=True)
        gltf = {"asset": {"version": "2.0"}, "nodes": [{"name": n} for n in names]}
        (folder / g.INTERIOR_MODELS[variant]).write_text(json.dumps(gltf), encoding="utf-8")


def write_package(root):
    """A one-control package: model.cfg names an interior that holds one switch with a click longer than the TSV's
    400-character clip. Its cockpit models hold every node the fixture packages use (write_cockpit_models)."""
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
    write_cockpit_models(pkg)
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


# The vendor's gear lever and flap lever, trimmed (BBS_752_BASE_INTERIOR.xml:25038-25096 and 32844-32866). The gear lever's
# component holds an Update that sets L:B757_GEAR_HANDLE_POS from the sim's gear handle, the lever's rotation animation that
# reads it (node B757_MAIN_Landing_Gear_Lever), the pull animation and the drag of the node the pilot grabs
# (B757_MAIN_Landing_Gear_Y), whose own code never touches that var. The flap lever is one drag template under a banner
# the curation leaves out.
LEVER_INTERIOR = """<?xml version="1.0"?>
<ModelInfo><Behaviors>
<!-- ======== LANDING GEAR ======== -->
<Component ID="Gear_Handle_Interact" Node="B757_MAIN_Landing_Gear_Y">
  <Update Frequency="2.0">
    (O:IsInit) 0 == if{
    1 (&gt;O:IsInit)
    (A:GEAR HANDLE POSITION,bool) (&gt;L:B757_GEAR_HANDLE_POS,number)
    0 (&gt;L:B757_GEAR_HANDLE_POS_DELTA, number)
    0 (&gt;L:B757_GEAR_HANDLE_GRAB, number)
    0 (&gt;L:B757_GEAR_HANDLE_PULL, number)
    }
  </Update>
  <UseTemplate Name="ASOBO_GT_Anim_Code">
    <NODE_ID>B757_MAIN_Landing_Gear_Lever</NODE_ID>
    <ANIM_NAME>B757_MAIN_Landing_Gear_Lever</ANIM_NAME>
    <ANIM_CODE>(L:B757_GEAR_HANDLE_POS,number) 20 *</ANIM_CODE>
    <ANIM_LENGTH>20</ANIM_LENGTH>
  </UseTemplate>
  <UseTemplate Name="ASOBO_GT_Anim_Code">
    <NODE_ID>B757_MAIN_Landing_Gear_Y</NODE_ID>
    <ANIM_NAME>B757_MAIN_Landing_Gear_Y</ANIM_NAME>
    <ANIM_CODE>(L:B757_GEAR_HANDLE_PULL,number) 10 *</ANIM_CODE>
    <ANIM_LENGTH>10</ANIM_LENGTH>
  </UseTemplate>
  <UseTemplate Name="ASOBO_GT_Lever_DraggingWheelLeftDrag_Axis">
    <HIGHLIGHT_NODE_ID>B757_MAIN_Landing_Gear_Y</HIGHLIGHT_NODE_ID>
    <AXIS>Y</AXIS>
    <LOCK_CODE>
      0 (&gt;O:_Delta,number)
      1 (&gt;L:B757_GEAR_HANDLE_GRAB,bool)
      1 (&gt;L:B757_GEAR_HANDLE_PULL,bool)
      0 (&gt;L:B757_GEAR_HANDLE_POS_DELTA,number)
    </LOCK_CODE>
    <DRAG_CODE>
      (A:SIM ON GROUND,bool) 0 == if{ (O:_Delta,number) 8 * (&gt;L:B757_GEAR_HANDLE_POS_DELTA,number) }
    </DRAG_CODE>
  </UseTemplate>
</Component>
<!-- ======== RIGHT WINDOW RAIN EFFECTS ======== -->
<Component ID="B757_CUSTOM_FLAP_HANDLE" Node="BBS757_lever_flaps">
  <UseTemplate Name="ASOBO_GT_Lever_DraggingWheelLeftDrag_Axis">
    <CURSOR>Grab</CURSOR>
    <DRAG_CODE>150 (M:DragPercent) * (&gt;L:B757_FC_FLAP_LEVER,number)</DRAG_CODE>
    <ANIM_NAME>BBS_757_lever_flaps</ANIM_NAME>
    <ANIM_LENGTH>150</ANIM_LENGTH>
    <ANIM_CODE>(L:B757_FC_FLAP_LEVER, number)</ANIM_CODE>
    <POSITIVE_AXIS_CODE>(L:B757_FC_FLAP_LEVER,number) 1 - 0 max (&gt;L:B757_FC_FLAP_LEVER,number)</POSITIVE_AXIS_CODE>
    <NEGATIVE_AXIS_CODE>(L:B757_FC_FLAP_LEVER,number) 1 + 150 min (&gt;L:B757_FC_FLAP_LEVER,number)</NEGATIVE_AXIS_CODE>
    <TOOLTIPID>Flap Lever</TOOLTIPID>
  </UseTemplate>
</Component>
</Behaviors></ModelInfo>
"""

LEVER_NODES = ("B757_MAIN_Landing_Gear_Y", "B757_MAIN_Landing_Gear_Lever", "BBS757_lever_flaps")


def write_lever_package(root):
    """write_package's package, its interior replaced by LEVER_INTERIOR and both cockpit models holding the lever nodes."""
    pkg = write_package(root)
    (pkg / "SimObjects" / "Airplanes" / "a" / "model" / "interior.xml").write_text(LEVER_INTERIOR, encoding="utf-8")
    write_cockpit_models(pkg, pw=LEVER_NODES, rr=LEVER_NODES)
    return pkg


def write_presence_package(root, nodes, pw, rr):
    """write_package's package, its interior replaced by one switch per node of `nodes` (each flips its own L:var), and its
    cockpit models by two that hold the node names `pw` and `rr`: a node in one list, in both or in neither."""
    pkg = write_package(root)
    switches = "\n".join(f"""<Component ID="{n}_COMP" Node="{n}">
  <UseTemplate Name="ASOBO_GT_Switch_Code">
    <ANIM_NAME>{n}</ANIM_NAME>
    <ANIM_CODE>(L:{n}, bool) 100 *</ANIM_CODE>
    <ANIMTIP_0>{n} OFF</ANIMTIP_0>
    <ANIMTIP_1>{n} ON</ANIMTIP_1>
    <LEFT_SINGLE_CODE>(L:{n}, bool) ! (&gt;L:{n}, bool)</LEFT_SINGLE_CODE>
  </UseTemplate>
</Component>""" for n in nodes)
    (pkg / "SimObjects" / "Airplanes" / "a" / "model" / "interior.xml").write_text(
        f'<?xml version="1.0"?>\n<ModelInfo><Behaviors>\n<!-- ======== ELECTRICAL PANEL ======== -->\n{switches}\n'
        "</Behaviors></ModelInfo>\n", encoding="utf-8")
    write_cockpit_models(pkg, pw=pw, rr=rr)
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

    GEAR, FLAP = "B757_MAIN_Landing_Gear_Y", "BBS757_lever_flaps"

    def test_a_row_keeps_the_l_vars_its_whole_component_reads_and_writes(self):
        # The gear lever's state var is set by the component's Update and shown by its OTHER animation (the lever's rotation):
        # the row of the node the pilot grabs, whose own animation is the pull of the handle, holds neither, so the row keeps
        # what the whole component reads and writes, as private data for the generator to check an entry against.
        with tempfile.TemporaryDirectory() as d:
            controls, _, _ = extract_controls.extract(write_lever_package(d))
        by_node = {r["node"]: r for r in controls}
        gear, flap = by_node[self.GEAR], by_node[self.FLAP]

        self.assertEqual((gear["kind"], flap["kind"]), ("lever", "lever"))
        self.assertEqual(gear["_state"], ["L:B757_GEAR_HANDLE_PULL"])
        self.assertNotIn("L:B757_GEAR_HANDLE_POS", gear["_state"] + gear["_writes"], "not in the row's own code")
        self.assertEqual(sorted(gear["_component_vars"]["reads"]), ["B757_GEAR_HANDLE_POS", "B757_GEAR_HANDLE_PULL"])
        self.assertEqual(sorted(gear["_component_vars"]["writes"]),
                         ["B757_GEAR_HANDLE_GRAB", "B757_GEAR_HANDLE_POS", "B757_GEAR_HANDLE_POS_DELTA",
                          "B757_GEAR_HANDLE_PULL"])
        self.assertEqual(flap["_component_vars"], {"reads": ["B757_FC_FLAP_LEVER"], "writes": ["B757_FC_FLAP_LEVER"]})
        # only L: vars count (an A: or O: var is no state var), and the components of two controls are not mixed
        everything = {n for r in controls for part in r["_component_vars"].values() for n in part}
        self.assertEqual(everything, {"B757_GEAR_HANDLE_POS", "B757_GEAR_HANDLE_PULL", "B757_GEAR_HANDLE_GRAB",
                                      "B757_GEAR_HANDLE_POS_DELTA", "B757_FC_FLAP_LEVER"})
        self.assertNotIn("B757_FC_FLAP_LEVER", gear["_component_vars"]["reads"] + gear["_component_vars"]["writes"])

    def test_a_component_keeps_what_its_update_reads_too(self):
        # A var an Update only reads is the component's own code as much as one it writes.
        with tempfile.TemporaryDirectory() as d:
            pkg = write_package(d)
            (pkg / "SimObjects" / "Airplanes" / "a" / "model" / "interior.xml").write_text("""<?xml version="1.0"?>
<ModelInfo><Behaviors>
<Component ID="BATT" Node="B757_OVHD_BATTERY_BUTTON">
  <Update Frequency="1.0">(L:B757_ONLY_READ_BY_AN_UPDATE, bool) if{ 1 (&gt;L:B757_ONLY_WRITTEN_BY_AN_UPDATE, bool) }</Update>
  <UseTemplate Name="ASOBO_GT_Switch_Code">
    <ANIM_CODE>(L:B757_OVHD_BATTERY_BUTTON, bool) 100 *</ANIM_CODE>
    <LEFT_SINGLE_CODE>(L:B757_OVHD_BATTERY_BUTTON, bool) ! (&gt;L:B757_OVHD_BATTERY_BUTTON, bool)</LEFT_SINGLE_CODE>
  </UseTemplate>
</Component>
</Behaviors></ModelInfo>
""", encoding="utf-8")
            controls, _, _ = extract_controls.extract(pkg)

        (battery,) = controls
        self.assertEqual(sorted(battery["_component_vars"]["reads"]),
                         ["B757_ONLY_READ_BY_AN_UPDATE", "B757_OVHD_BATTERY_BUTTON"])
        self.assertEqual(sorted(battery["_component_vars"]["writes"]),
                         ["B757_ONLY_WRITTEN_BY_AN_UPDATE", "B757_OVHD_BATTERY_BUTTON"])

    def test_levers_from_the_package_are_emitted_without_click_code_and_the_run_names_them(self):
        # The vendor's own XML for both levers through the extractor, the generator and its printed report.
        gear, flap = self.GEAR, self.FLAP
        levers = {
            gear: {"state_var": "B757_GEAR_HANDLE_POS",
                   "positions": [{"value": 0, "label": "UP"}, {"value": 0.5, "label": "OFF"}, {"value": 1, "label": "DOWN"}]},
            flap: {"state_var": "B757_FC_FLAP_LEVER",
                   "positions": [{"value": 0, "label": "UP"}, {"value": 17, "label": "1"}, {"value": 150, "label": "30"}]},
        }
        base = {"panels": {"LANDING GEAR": {"name": "Landing gear", "section": "Forward panel"}},
                "exclude_banners": ["RIGHT WINDOW RAIN EFFECTS"],
                "node_panels": [{"prefix": flap, "panel": "Control stand", "section": "Pedestal"}],
                "labels": {gear: "Gear Lever", flap: "Flap Lever"}}
        wrong = {**levers, gear: dict(levers[gear], state_var="B757_GEAR_HANDLE_POSITION")}
        with tempfile.TemporaryDirectory() as d:
            pkg = write_lever_package(d)
            out = os.path.join(d, "out", "map.json")
            cur_path = os.path.join(d, "curation.json")

            def run(data):
                with open(cur_path, "w", encoding="utf-8") as fh:
                    json.dump(data, fh)
                stdout = io.StringIO()
                with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(io.StringIO()):
                    g.main([str(pkg), "--out", out, "--curation", cur_path])
                with open(out, encoding="utf-8") as fh:
                    return json.load(fh)["controls"], stdout.getvalue()

            bare, printed_bare = run(base)
            controls, printed = run(dict(base, include_nodes=[flap], levers=levers))
            with self.assertRaises(SystemExit) as stopped:
                run(dict(base, include_nodes=[flap], levers=wrong))

        self.assertEqual(bare, [], "without the entry the rules leave both levers out")
        self.assertRegex(printed_bare, r"levers\s+: 0 ")
        self.assertRegex(printed_bare, r"1\s+lever\n")
        self.assertRegex(printed_bare, r"1\s+excluded banner")
        by_node = {c["node"]: c for c in controls}
        self.assertEqual(sorted(by_node), sorted((gear, flap)))
        for node, (panel, section, label) in ((gear, ("Landing gear", "Forward panel", "Gear Lever")),
                                              (flap, ("Control stand", "Pedestal", "Flap Lever"))):
            c = by_node[node]
            self.assertEqual((c["kind"], c["panel"], c["section"], c["label"], c["state_var"], c["positions"]),
                             ("switch", panel, section, label, levers[node]["state_var"], levers[node]["positions"]))
            self.assertEqual([c[f] for f in ("press", "release", "cw", "ccw")], [None] * 4)
        self.assertRegex(printed, r"levers\s+: 2 ")
        for node in (gear, flap):
            self.assertIn(f"{node}: taken in (the rules alone leave it out: lever)", printed)
        self.assertNotRegex(printed, r"\d\s+lever\n", "a lever taken in is not counted as dropped")
        self.assertRegex(str(stopped.exception), rf"^curation: curation levers: {gear}'s own vendor code never reads or "
                                                 r"writes B757_GEAR_HANDLE_POSITION")


class RpnReplay(unittest.TestCase):
    """Plan 02, task 6: rpn_replay.replay runs the subset of the sim's RPN that the cockpit's click code uses, on a copy of
    the L:vars it is given. These pin the interpreter on small code; ClickCodeReplay runs it over every control of the map."""

    @staticmethod
    def run_code(code, **lvars):
        return rpn_replay.replay(code, lvars)

    def test_arithmetic_takes_its_operands_in_stack_order(self):
        after = self.run_code("7 2 - (>L:SUB) 7 2 + (>L:ADD) 4 ++ (>L:INC) 4 -- (>L:DEC) -1.5 (>L:NEG)")
        self.assertEqual([after[k] for k in ("SUB", "ADD", "INC", "DEC", "NEG")], [5, 9, 5, 3, -1.5])

    def test_a_set_pops_the_top_of_the_stack(self):
        after = self.run_code("2 3 (>L:A) (>L:B)")
        self.assertEqual((after["A"], after["B"]), (3, 2))

    def test_units_are_ignored_whether_or_not_a_space_follows_the_comma(self):
        after = self.run_code("(L:A, bool) (>L:B,enum) (L:A) (>L:C, number) (L:A,bool) (>L:D)", A=1)
        self.assertEqual([after[k] for k in "BCD"], [1, 1, 1])

    def test_a_variable_never_set_reads_zero(self):
        self.assertEqual(self.run_code("(L:NEVER_SET) 1 + (>L:X)")["X"], 1)

    def test_not_is_a_logical_not(self):
        after = self.run_code("(L:A) ! (>L:NOT_A) (L:B) ! (>L:NOT_B)", A=0, B=7)
        self.assertEqual((after["NOT_A"], after["NOT_B"]), (1, 0))

    def test_comparisons_and_logic_push_one_or_zero(self):
        after = self.run_code("1 2 < (>L:LT) 1 2 > (>L:GT) 2 2 == (>L:EQ) 1 2 == (>L:NE) "
                              "1 1 and (>L:AND1) 1 0 and (>L:AND0) 0 0 or (>L:OR0) 0 5 or (>L:OR1)")
        self.assertEqual([after[k] for k in ("LT", "GT", "EQ", "NE", "AND1", "AND0", "OR0", "OR1")],
                         [1, 0, 1, 0, 1, 0, 0, 1])

    def test_min_and_max_hold_a_step_at_the_ends(self):
        up, down = "(L:X, enum) 1 + 2 min (>L:X)", "(L:X, enum) 1 - 0 max (>L:X)"
        self.assertEqual([self.run_code(up, X=x)["X"] for x in (0, 1, 2)], [1, 2, 2])
        self.assertEqual([self.run_code(down, X=x)["X"] for x in (0, 1, 2)], [0, 0, 1])

    def test_dup_and_the_increment_steps_run_the_chronometer_lever_code(self):
        # B757_MAIN_CHRONO_SET_SWITCH's cw and ccw, as the vendor wrote them: step, test the copy, clamp
        cw, ccw = "(L:S, enum) ++ d 3 > if{ 3 } (>L:S, enum)", "(L:S, enum) -- d 0 < if{ 0 } (>L:S, enum)"
        self.assertEqual([self.run_code(cw, S=s)["S"] for s in range(4)], [1, 2, 3, 3])
        self.assertEqual([self.run_code(ccw, S=s)["S"] for s in range(4)], [0, 0, 1, 2])

    def test_modulo_keeps_the_remainder(self):
        self.assertEqual(self.run_code("7 4 % (>L:X)")["X"], 3)
        with self.assertRaisesRegex(rpn_replay.ReplayError, "modulo by zero"):
            self.run_code("7 0 % (>L:X)")

    def test_if_runs_its_block_on_a_nonzero_condition_and_els_on_zero(self):
        code = "(L:C) if{ 1 (>L:X) } els{ 2 (>L:X) } 9 (>L:Y)"
        on, off = self.run_code(code, C=1), self.run_code(code, C=0)
        self.assertEqual((on["X"], on["Y"], off["X"], off["Y"]), (1, 9, 2, 9), "the code after a block still runs")

    def test_if_without_els_and_nested_blocks(self):
        code = "(L:A) if{ (L:B) if{ 1 (>L:X) } els{ 2 (>L:X) } }"
        got = {(a, b): self.run_code(code, A=a, B=b).get("X") for a in (0, 1) for b in (0, 1)}
        self.assertEqual(got, {(0, 0): None, (0, 1): None, (1, 0): 2, (1, 1): 1})

    def test_the_engine_fire_handle_press_gates_on_its_rotation_unlock_and_fire(self):
        # BBS757_FIREHANDLE1_CLICK's press as the map has it: and, or and an if inside an els
        press = ("(L:ROT, enum) 1 == (L:UNLOCK, bool) 1 == (L:FIRE, bool) 1 == or and if{ 1 (>L:SW, bool) } "
                 "els{ (L:ROT, enum) 1 == if{ 0 (>L:SW, bool) } }")

        def switch(**state):
            return self.run_code(press, **state)["SW"]

        self.assertEqual(switch(ROT=1, UNLOCK=1, SW=0), 1, "rotated and unlocked: pulled")
        self.assertEqual(switch(ROT=1, FIRE=1, SW=0), 1, "rotated with the fire active: pulled")
        self.assertEqual(switch(ROT=1, SW=1), 0, "rotated, locked, no fire: pushed in")
        self.assertEqual(switch(UNLOCK=1, SW=1), 1, "not rotated: nothing happens")
        self.assertEqual(switch(SW=0), 0)

    def test_k_and_h_events_are_recorded_and_change_no_variable(self):
        after = self.run_code("1 (>K:AUTOPILOT_OFF) (>H:BBS_757_EICAS_DEBUG) 5 (>L:X)")
        self.assertEqual(after["X"], 5)
        self.assertEqual(after["K:AUTOPILOT_OFF"], 1, "the value the event was sent with")
        self.assertIn("H:BBS_757_EICAS_DEBUG", after)
        self.assertEqual(sorted(after), ["H:BBS_757_EICAS_DEBUG", "K:AUTOPILOT_OFF", "X"], "no L:var of those names")

    def test_o_vars_read_zero_until_written_and_their_writes_are_recorded(self):
        after = self.run_code("(O:LOCK) 1 + (>L:X) 4 (>O:LOCK) (O:LOCK) (>L:Y)")
        self.assertEqual((after["X"], after["Y"], after["O:LOCK"]), (1, 4, 4))
        self.assertNotIn("LOCK", after, "an O:var is not the L:var of the same name")

    def test_e_and_a_vars_read_zero_unless_given(self):
        code = "(E:SIMULATION TIME, second) 1 + (>L:X) (A:CIRCUIT ON:27, bool) 2 + (>L:Y)"
        after = self.run_code(code)
        self.assertEqual((after["X"], after["Y"]), (1, 2))
        self.assertEqual(self.run_code(code, **{"A:CIRCUIT ON:27": 1})["Y"], 3, "given under its prefixed name")

    def test_a_pop_from_an_empty_stack_gives_zero(self):
        # the cockpit's code relies on it (the bus tie buttons write a var and then run a `!`); unmeasured in the sim
        self.assertEqual(self.run_code("! (>L:X)")["X"], 1)
        self.assertEqual(self.run_code("(>L:X)", X=5)["X"], 0)

    def test_the_variables_passed_in_are_not_changed(self):
        before = {"X": 1}
        after = rpn_replay.replay("5 (>L:X)", before)
        self.assertEqual((before, after), ({"X": 1}, {"X": 5}))

    def test_code_it_cannot_parse_is_an_error_naming_the_problem(self):
        cases = {
            "2 3 pow": "unknown token 'pow'",
            "1 }": "a closing brace nothing opened",
            "1 if{ 2": "a block that is never closed",
            "1 els{ 2 }": "els{ with no if{",
            "(L:X": "an opening parenthesis that is never closed",
            "1 (>X, bool)": "a variable with no type",
            "1 (>B:X)": "unsupported variable type 'B'",
            "(K:X)": "K: variables cannot be read",
            "1 (>A:X)": "A: variables cannot be written",
            "1 { 2": "unexpected '{'",
        }
        for code, why in cases.items():
            with self.subTest(code=code):
                with self.assertRaisesRegex(rpn_replay.ReplayError, re.escape(why)):
                    rpn_replay.replay(code, {})


# ---- every control's click code ---------------------------------------------------------------------------------------
CLICK_FIELDS = ("press", "release", "cw", "ccw")
SLACK = 0.001   # Bbs757Rpn.ValueTolerance: the app compares a value read back with a position to this slack

# Vendor clicks that act only while another L:var allows it, so they take their control to another position only from the
# right state of that var. node -> (why: the click code it comes from, {the control's start value: the L:vars it needs
# from there}); the key None stands for every start value the entry does not name. The control is replayed from each of
# its positions as for any other, with those vars set; an entry that is not needed, or that does not make the click work,
# is reported like any other failure: never an exemption.
PRECONDITIONS = {
    # The fire handles (BBS_752_BASE_INTERIOR.xml, cited as XML:line). A handle is pulled while it is unlocked or its fire
    # is active, and pushed in when neither is so; the engine handles also want their rotation at 1 (Center).
    "BBS757_FIREHANDLE1_CLICK": (
        "press (XML:15572-15579) writes 1 when L:BBS757_ENG1_FIRE_HANDLE_ROTATION reads 1 and L:BBS757_FIREUNLOCK1 or "
        "L:B757_L_ENG_FIRE_ACTIVE does, else 0 when the rotation reads 1",
        {0: {"BBS757_ENG1_FIRE_HANDLE_ROTATION": 1, "BBS757_FIREUNLOCK1": 1},
         1: {"BBS757_ENG1_FIRE_HANDLE_ROTATION": 1, "BBS757_FIREUNLOCK1": 0, "B757_L_ENG_FIRE_ACTIVE": 0}}),
    "BBS757_FIREHANDLE1_CLICK2": (
        "press (XML:15669-15676) writes 1 when L:BBS757_ENG2_FIRE_HANDLE_ROTATION reads 1 and L:BBS757_FIREUNLOCK2 or "
        "L:B757_R_ENG_FIRE_ACTIVE does, else 0 when the rotation reads 1",
        {0: {"BBS757_ENG2_FIRE_HANDLE_ROTATION": 1, "BBS757_FIREUNLOCK2": 1},
         1: {"BBS757_ENG2_FIRE_HANDLE_ROTATION": 1, "BBS757_FIREUNLOCK2": 0, "B757_R_ENG_FIRE_ACTIVE": 0}}),
    # The APU handle's press (XML:15766-15774) has its `and` right after the rotation test, with no second operand, so
    # the rotation plays no part (this replay pops an empty stack as 0): a gate on the unlock and the fire alone.
    "BBS757_FIREHANDLE1_CLICK3": (
        "press (XML:15766-15774) writes 1 when L:BBS757_FIREUNLOCK3 or L:B757_APU_FIRE_ACTIVE reads 1, else 0",
        {0: {"BBS757_FIREUNLOCK3": 1},
         1: {"BBS757_FIREUNLOCK3": 0, "B757_APU_FIRE_ACTIVE": 0}}),
    # The handles' rotation: cw and ccw step it only while the handle is pulled; while it is in, both write 1 (Center).
    "B757_PED_Fire_DISCH_1": (
        "cw and ccw (XML:15608-15617) step L:BBS757_ENG1_FIRE_HANDLE_ROTATION only while L:B757_ENGFIRE_1_SWITCH reads 1, "
        "else both write 1",
        {None: {"B757_ENGFIRE_1_SWITCH": 1}}),
    "B757_PED_Fire_DISCH_2": (
        "cw and ccw (XML:15705-15714) step L:BBS757_ENG2_FIRE_HANDLE_ROTATION only while L:B757_ENGFIRE_2_SWITCH reads 1, "
        "else both write 1",
        {None: {"B757_ENGFIRE_2_SWITCH": 1}}),
    "B757_PED_Fire_DISCH_3": (
        "cw and ccw (XML:15801-15810) step L:BBS757_APU_FIRE_HANDLE_ROTATION only while L:B757_APUFIRE_SWITCH reads 1, "
        "else both write 1",
        {None: {"B757_APUFIRE_SWITCH": 1}}),
}

# The vendor's own click code that no RPN parser accepts, found by this replay and listed in curation.json under
# _suspected_slips (the generator prints the same list). node -> {field: what is wrong}. The map holds the vendor's code
# verbatim, and what the sim does with such a click (run it, stop at the slip, refuse it whole) is unmeasured, so the
# replay neither accepts the slip nor leaves the control unchecked: the code must still fail to parse as pinned, and with
# the slipped token left out (without_slips) it must do what the map says.
VENDOR_SLIPS = {
    **{node: {"press": "a set with no variable type: (>B757_..._ON, bool) after the flip of the button's own var"}
       for node in ("B757_OVHD_L_PUMPS_FWD_BUTTON", "B757_OVHD_L_PUMPS_AFT_BUTTON", "B757_OVHD_R_PUMPS_FWD_BUTTON",
                    "B757_OVHD_R_PUMPS_AFT_BUTTON", "B757_OVHD_C_PUMPS_L_BUTTON", "B757_OVHD_C_PUMPS_R_BUTTON",
                    "B757_OVHD_XFEED_PUMPS_FWD_BUTTON", "B757_OVHD_XFEED_PUMPS_AFT_BUTTON")},
    "B757_MAIN_INSTRUMENT_ADF_Knob_RIGHT_FO": {"press": "a closing brace nothing opened, after the flip of its var"},
}


def near(a, b):
    return abs(a - b) < SLACK


def without_slips(code):
    """The code with the slipped token left out: a set with no variable type, a closing brace nothing opened."""
    code, depth, kept = g.MISSING_L.sub("", code), 0, []
    for piece in re.split(r"(if\{|els\{|\})", code):
        if piece in ("if{", "els{"):
            depth += 1
        elif piece == "}":
            if depth == 0:
                continue
            depth -= 1
        kept.append(piece)
    return "".join(kept)


def shape(c):
    """What the app does with this control (Bbs757Write.ToPosition, Bbs757Rpn.ToggleTo and StepTo), which decides what is
    checked: "skip" (no click code: guarded, direct_write and the sim's own 3-state switches are written through their
    state var), "parse" (a button, a held button, or a switch with only a release: the code only has to parse), "press"
    (a toggle, guard or select: one press moves it), "steps" (a switch with cw and ccw: one step per position), "cycle" (a
    switch with a press only: presses cycle forward, wrapping). None when no check covers the control's shape."""
    if not any(c[f] for f in CLICK_FIELDS):
        return "skip"
    if c["kind"] in ("button", "held"):
        return "parse"
    if c["kind"] in ("toggle", "guard", "select") and c["press"]:
        return "press"
    if c["kind"] == "switch":
        if c["cw"] and c["ccw"]:
            return "steps"
        if c["press"] and not (c["cw"] or c["ccw"]):
            return "cycle"
        if not (c["press"] or c["cw"] or c["ccw"]):
            return "parse"
    return None


def landing_failures(c, how, codes, needs):
    """Replay the control's code from each of its positions: where does the state var land?"""
    node, var = c["node"], c["state_var"]
    values = [p["value"] for p in c["positions"]]
    failures = []

    def lands_on(field, start):
        state = dict(needs.get(start, needs.get(None, {})))
        state[var] = start
        try:
            return rpn_replay.replay(codes[field], state)[var]
        except rpn_replay.ReplayError as e:   # an error only some start states reach: the parse check cannot see it
            failures.append(f"{node}: {field} from {start:g} cannot run ({e}): {codes[field]}")
            return None

    for i, start in enumerate(values):
        if how == "press":
            got = lands_on("press", start)
            if got is not None and (near(got, start) or not any(near(got, v) for v in values)):
                failures.append(f"{node}: press from {start:g} lands on {got:g}, not on another of its positions "
                                f"{[f'{v:g}' for v in values]}: {codes['press']}")
            continue
        if how == "cycle":
            wanted = [("press", values[(i + 1) % len(values)])]
        else:
            wanted = [("cw", values[min(i + 1, len(values) - 1)]), ("ccw", values[max(i - 1, 0)])]
        for field, want in wanted:
            got = lands_on(field, start)
            if got is not None and not near(got, want):
                failures.append(f"{node}: {field} from {start:g} lands on {got:g}, not on {want:g}: {codes[field]}")
    return failures


def control_failures(c, needs, slips):
    node, how = c["node"], shape(c)
    if how is None:
        return [f"{node}: no check covers a {c['kind']} with {[f for f in CLICK_FIELDS if c[f]]}"]
    failures, codes = [], {}
    for field in CLICK_FIELDS:
        code = c[field]
        if not code:
            if field in slips:
                failures.append(f"{node}: pinned as a vendor slip in {field}, which has no code")
            continue
        try:
            rpn_replay.replay(code, {})
            if field in slips:
                failures.append(f"{node}: {field} is pinned as a vendor slip ({slips[field]}) but parses: unpin it")
            codes[field] = code
        except rpn_replay.ReplayError as e:
            if field not in slips:
                failures.append(f"{node}: {field} does not parse ({e}): {code}")
                continue
            codes[field] = without_slips(code)
            try:
                rpn_replay.replay(codes[field], {})
            except rpn_replay.ReplayError as e2:
                failures.append(f"{node}: {field} still does not parse with its slip left out ({e2}): {codes[field]}")
                codes.pop(field)
    if how in ("press", "steps", "cycle"):
        if not c["state_var"] or len(c["positions"]) < 2:
            return failures + [f"{node}: a {how} control needs a state var and two positions to check"]
        for start, state in (needs or {}).items():   # a typo would excuse nothing, so a stale entry is an error
            if start is not None and not any(near(start, p["value"]) for p in c["positions"]):
                failures.append(f"{node}: a precondition names the start {start:g}, which is not one of its positions")
            for var in state:
                if not any(re.search(rf"L:{re.escape(var)}\b", code) for code in codes.values()):
                    failures.append(f"{node}: a precondition sets {var}, which no click code of the control reads")
        if all(f in codes for f in CLICK_FIELDS if c[f] and f != "release"):
            failures += landing_failures(c, how, codes, needs or {})
            if needs and not landing_failures(c, how, codes, {}):
                failures.append(f"{node}: its precondition is not needed: the click works without it")
    elif needs:
        failures.append(f"{node}: a precondition is of no use: no click code of this control is replayed from a position")
    return failures


def replay_failures(controls, preconditions=None, slips=None):
    """One message per failure, each naming its control and its click code (empty when every control checks out). The
    codes are replayed through rpn_replay.replay; what is checked follows shape(). The tables are PRECONDITIONS and
    VENDOR_SLIPS (given empty, none is allowed): a control they name that the map lacks is reported too."""
    preconditions, slips = preconditions or {}, slips or {}
    nodes = {c["node"] for c in controls}
    failures = [f"{node}: {what} names a control the map does not have"
                for what, table in (("a precondition", preconditions), ("a pinned vendor slip", slips))
                for node in table if node not in nodes]
    for c in controls:
        _why, needs = preconditions.get(c["node"], (None, None))
        failures += control_failures(c, needs, slips.get(c["node"], {}))
    return failures


def control(kind, positions=(0, 1), node="B757_X", **codes):
    """A map control as the generator writes it, on state var X, with only the click codes given."""
    return {"node": node, "kind": kind, "state_var": "X",
            "positions": [{"value": v, "label": f"P{v}"} for v in positions],
            **{f: codes.get(f) for f in CLICK_FIELDS}}


class ClickCodeReplay(unittest.TestCase):
    """Plan 02, task 6: the app moves a control by replaying the cockpit's own click code, so a pick lands where it aims only
    if that code does what the map says. replay_failures runs every control's code through rpn_replay.replay offline and
    checks it the way the app uses it: a toggle, guard or select is pressed once and must land on another of its positions; a
    switch with cw and ccw is stepped from each position and must land on the next or previous one, or stay at an end; a
    switch with only a press cycles forward; every code, a button's included, must parse. Nothing here reads the package."""

    def embedded(self):
        with open(g.DEFAULT_OUT, encoding="utf-8") as fh:
            return json.load(fh)

    def test_every_control_replays_to_its_positions(self):
        failures = replay_failures(self.embedded()["controls"], PRECONDITIONS, VENDOR_SLIPS)

        self.assertEqual(failures, [], "\n" + "\n".join(failures))

    def test_the_embedded_map_has_controls_for_every_check(self):
        counts = collections.Counter(shape(c) for c in self.embedded()["controls"])

        self.assertNotIn(None, counts, "a control no check covers")
        for how in ("parse", "press", "steps"):
            self.assertGreater(counts[how], 0, how)

    def test_a_broken_click_code_in_the_embedded_map_is_reported_by_node(self):
        controls = copy.deepcopy(self.embedded()["controls"])
        before = replay_failures(controls, PRECONDITIONS, VENDOR_SLIPS)
        toggle = next(c for c in controls if shape(c) == "press" and c["node"] not in PRECONDITIONS)
        switch = next(c for c in controls if shape(c) == "steps" and c["node"] not in PRECONDITIONS)
        toggle["press"] = f"(L:{toggle['state_var']}) (>L:{toggle['state_var']})"    # writes back what it read
        switch["cw"], switch["ccw"] = switch["ccw"], switch["cw"]                     # steps the wrong way

        failures = replay_failures(controls, PRECONDITIONS, VENDOR_SLIPS)

        new = [m for m in failures if m not in before]
        self.assertEqual({m.split(":")[0] for m in new}, {toggle["node"], switch["node"]})

    def test_the_pinned_vendor_slips_are_the_ones_the_generator_suspects(self):
        # curation.json's _suspected_slips lists the same nodes, and the generator prints them on every run
        suspected = {c["node"]: [f for f in CLICK_FIELDS if g.suspected_slips(c[f])] for c in self.embedded()["controls"]}

        self.assertEqual({n: fields for n, fields in suspected.items() if fields},
                         {n: [f for f in CLICK_FIELDS if f in pinned] for n, pinned in VENDOR_SLIPS.items()})

    def test_a_toggle_guard_or_select_must_land_on_its_other_position(self):
        flip = "(L:X) ! (>L:X)"
        self.assertEqual(replay_failures([control(k, node=f"B757_{k.upper()}", press=flip)
                                          for k in ("toggle", "guard", "select")]), [])
        stuck = control("toggle", node="B757_STUCK", press="(L:X) (>L:X)")          # writes back what it read
        off_map = control("guard", node="B757_OFF_MAP", press="5 (>L:X)")            # lands on no position
        failures = replay_failures([stuck, off_map])

        self.assertEqual([m.split(":")[0] for m in failures], ["B757_STUCK"] * 2 + ["B757_OFF_MAP"] * 2,
                         "each position is a start of its own")
        self.assertTrue(all(stuck["press"] in m for m in failures[:2]) and all(off_map["press"] in m for m in failures[2:]))

    def test_a_switch_must_step_to_the_next_or_previous_position_and_stay_at_an_end(self):
        up, down = "(L:X) 1 + 2 min (>L:X)", "(L:X) 1 - 0 max (>L:X)"
        self.assertEqual(replay_failures([control("switch", (0, 1, 2), cw=up, ccw=down)]), [])
        # positions that are not consecutive numbers: the next listed one is the next step
        self.assertEqual(replay_failures([control("switch", (0, 11), cw="11 (>L:X)", ccw="0 (>L:X)")]), [])
        jumps = control("switch", (0, 1, 2), node="B757_JUMPS", cw="(L:X) 2 + 2 min (>L:X)", ccw=down)
        wraps = control("switch", (0, 1, 2), node="B757_WRAPS", cw="(L:X) 1 + 3 % (>L:X)", ccw=down)
        reversed_ = control("switch", (0, 1, 2), node="B757_REVERSED", cw=down, ccw=up)

        self.assertEqual([m.split(": ")[1].split(" lands")[0] for m in replay_failures([jumps])], ["cw from 0"])
        self.assertEqual([m.split(": ")[1].split(" lands")[0] for m in replay_failures([wraps])], ["cw from 2"])
        self.assertEqual(len(replay_failures([reversed_])), 6)

    def test_a_switch_with_a_press_only_cycles_forward_through_its_positions(self):
        self.assertEqual(replay_failures([control("switch", (0, 1, 2), press="(L:X) 1 + 3 % (>L:X)")]), [])
        backwards = control("switch", (0, 1, 2), node="B757_BACK", press="(L:X) 1 - 0 max (>L:X)")

        self.assertEqual(len(replay_failures([backwards])), 3)

    def test_a_control_with_no_click_code_is_skipped_and_a_button_is_only_parsed(self):
        guarded = control("switch", (0, 1, 2), node="B757_GUARDED")                   # no press, release, cw or ccw
        button = control("button", (), press="1 (>L:Y)", release="0 (>L:Y)")          # writes nothing it must land on
        spring = control("switch", (0, 1, 2), node="B757_SPRING", release="1 (>L:X)")
        self.assertEqual(replay_failures([guarded, button, spring]), [])

        failures = replay_failures([control("held", (), node="B757_BROKEN", press="1 (>L:Y", release="0 (>L:Y)")])

        self.assertEqual(len(failures), 1)
        self.assertTrue(failures[0].startswith("B757_BROKEN: press does not parse ("))
        self.assertTrue(failures[0].endswith("): 1 (>L:Y"), failures[0])

    def test_code_that_does_not_parse_is_reported_by_node_with_the_code(self):
        bad = control("toggle", node="B757_BAD", press="(L:X) ! (>L:X) }")

        self.assertEqual(replay_failures([bad]), ["B757_BAD: press does not parse (a closing brace nothing opened): "
                                                  "(L:X) ! (>L:X) }"])

    def test_an_error_only_some_start_states_reach_is_reported_by_node_not_raised(self):
        divides = control("toggle", node="B757_DIVIDES", press="2 (L:X) 1 - % (>L:X)")   # divides by zero from position 1

        failures = replay_failures([divides])

        self.assertEqual([m.split(" lands")[0].split(" cannot")[0] for m in failures],
                         ["B757_DIVIDES: press from 0", "B757_DIVIDES: press from 1"])
        self.assertIn("cannot run (modulo by zero)", failures[1])

    def test_a_shape_no_check_covers_is_reported_not_passed(self):
        odd = control("switch", (0, 1, 2), node="B757_ODD", press="1 (>L:X)", cw="(L:X) 1 + 2 min (>L:X)")

        [message] = replay_failures([odd])

        self.assertTrue(message.startswith("B757_ODD: no check covers"), message)

    def test_a_precondition_gives_a_gated_click_the_state_it_needs_and_is_never_an_exemption(self):
        gated = control("toggle", node="B757_GATED", press="(L:G, bool) if{ 1 (>L:X) } els{ 0 (>L:X) }")
        needs = {"B757_GATED": ("press writes 1 while L:G reads 1 and 0 otherwise", {0: {"G": 1}})}

        [message] = replay_failures([gated])

        self.assertIn("B757_GATED: press from 0 lands on 0", message)      # G unset: the press writes 0 from 0 too
        self.assertEqual(replay_failures([gated], needs), [])
        # a state that does not make the click work excuses nothing
        useless = {"B757_GATED": ("why", {0: {"G": 0}})}
        self.assertEqual(len(replay_failures([gated], useless)), 1)
        # the key None stands for every start the entry does not name, and a start it names takes its own state
        rotate = control("switch", (0, 1, 2), node="B757_ROTATE",
                         cw="(L:G) if{ (L:X) 1 + 2 min (>L:X) } els{ 1 (>L:X) }",
                         ccw="(L:G) if{ (L:X) 1 - 0 max (>L:X) } els{ 1 (>L:X) }")
        self.assertEqual(len(replay_failures([rotate])), 4, "while G reads 0 every step writes 1")
        self.assertEqual(replay_failures([rotate], {"B757_ROTATE": ("why", {None: {"G": 1}})}), [])
        stepped = replay_failures([rotate], {"B757_ROTATE": ("why", {None: {"G": 1}, 1: {"G": 0}})})
        self.assertEqual([m.split(" lands")[0] for m in stepped], ["B757_ROTATE: cw from 1", "B757_ROTATE: ccw from 1"])
        # a start that is no position, or a var no click code reads, is a typo and is reported
        [message] = replay_failures([gated], {"B757_GATED": ("why", {0: {"G": 1}, 5: {"G": 1}})})
        self.assertIn("B757_GATED: a precondition names the start 5, which is not one of its positions", message)
        [message] = replay_failures([gated], {"B757_GATED": ("why", {0: {"G": 1, "NOPE": 1}})})
        self.assertIn("B757_GATED: a precondition sets NOPE, which no click code of the control reads", message)
        # one the click does not need, or for a control the map lacks, is reported
        plain = control("toggle", node="B757_PLAIN", press="(L:X) ! (>L:X) (L:G) (>L:Y)")    # reads G, never needs it
        self.assertEqual(replay_failures([plain], {"B757_PLAIN": ("why", {0: {"G": 1}})}),
                         ["B757_PLAIN: its precondition is not needed: the click works without it"])
        self.assertEqual(replay_failures([plain], {"B757_NONE": ("why", {})}),
                         ["B757_NONE: a precondition names a control the map does not have"])
        button = control("button", (), node="B757_BUTTON", press="1 (>L:Y)")
        [message] = replay_failures([button], {"B757_BUTTON": ("why", {None: {"G": 1}})})
        self.assertTrue(message.startswith("B757_BUTTON: a precondition is of no use"), message)

    def test_without_slips_leaves_out_only_the_slipped_token(self):
        fine = "(L:C) if{ 1 (>L:X) } els{ 2 (>L:X) } (L:Y) ! (>L:Y, bool)"

        self.assertEqual(without_slips(fine), fine)
        self.assertEqual(without_slips("(L:C) if{ 1 (>L:X) } } 2 (>L:Y)"), "(L:C) if{ 1 (>L:X) }  2 (>L:Y)")
        self.assertEqual(without_slips("(L:Y) ! (>L:Y, bool) (L:Z) ! (>Z, bool)"), "(L:Y) ! (>L:Y, bool) (L:Z) ! ")

    def test_a_pinned_vendor_slip_must_still_fail_to_parse_and_the_rest_of_the_click_must_still_work(self):
        brace = control("toggle", node="B757_BRACE", press="(L:X) ! (>L:X) }")
        no_type = control("toggle", node="B757_NO_TYPE", press="(L:X) ! (>L:X) (L:X_ON) ! (>X_ON, bool)")
        slips = {"B757_BRACE": {"press": "a closing brace nothing opened"}, "B757_NO_TYPE": {"press": "a set with no type"}}

        self.assertEqual(replay_failures([brace, no_type], slips=slips), [])
        self.assertEqual(len(replay_failures([brace, no_type])), 2, "unpinned, each is a parse failure")
        # the code is fixed, or the pinned field has no code, or the control is gone: the pin is stale
        fixed = control("toggle", node="B757_FIXED", press="(L:X) ! (>L:X)")
        [stale] = replay_failures([fixed], slips={"B757_FIXED": {"press": "a slip"}})
        self.assertIn("B757_FIXED: press is pinned as a vendor slip (a slip) but parses", stale)
        [no_code] = replay_failures([fixed], slips={"B757_FIXED": {"cw": "a slip"}})
        self.assertIn("pinned as a vendor slip in cw, which has no code", no_code)
        self.assertEqual(replay_failures([fixed], slips={"B757_NONE": {"press": "a slip"}}),
                         ["B757_NONE: a pinned vendor slip names a control the map does not have"])
        # a slip is not the only thing wrong: what is left of the click is checked as any other
        both = control("toggle", node="B757_BOTH", press="(L:X) (>L:X) }")
        still = replay_failures([both], slips={"B757_BOTH": {"press": "a closing brace nothing opened"}})
        self.assertEqual([m.split(" lands")[0] for m in still], ["B757_BOTH: press from 0", "B757_BOTH: press from 1"])


if __name__ == "__main__":
    unittest.main()
