"""Tests for the A330 control-map generator (run: python -I -m unittest discover -s tools/a330-gen -p "test_*.py")."""
import copy
import importlib.util
import json
import os
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
COMMITTED_MAP = os.path.join(HERE, "..", "..", "MSFSBlindAssist", "Resources", "a330_control_map.json")


def load_generator():
    spec = importlib.util.spec_from_file_location("generate_a330_map", os.path.join(HERE, "generate_a330_map.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def small_curation():
    """An in-memory curation: two controls (one with an event, one read-out), one left-out event."""
    return {
        "version": 1,
        "panel_order": {"Overhead": ["Demo"]},
        "own_panels": [],
        "controls": {
            "AIRLINER_DEMO": {"key": "A330_DEMO", "section": "Overhead", "panel": "Demo", "kind": "select", "mode": "set",
                              "read": "INI_DEMO",
                              "positions": [{"value": 0, "input": 0, "name": "0", "proven": False},
                                            {"value": 1, "input": 1, "name": "1", "proven": False}]},
            "A330_DEMO_LT": {"key": "A330_DEMO_LT", "section": "Overhead", "panel": "Demo", "kind": "status",
                             "mode": "set", "read": "INI_DEMO_LT"},
        },
        "name_overrides": {"A330_DEMO": {"label": "Demo", "aircraft_says": None, "why": "test"},
                           "A330_DEMO_LT": {"label": "Demo light", "aircraft_says": None, "why": "test"}},
        "position_overrides": {"A330_DEMO": {"0": {"label": "Off", "aircraft_says": None, "why": "test"},
                                             "1": {"label": "On", "aircraft_says": None, "why": "test"}}},
        "left_out": {"AIRLINER_PRESSED_BY_DEFINITION": "pressed by the definition"},
        "not_yet_offered": [],
        "retired_keys": {},
    }


class MapFromCurationTests(unittest.TestCase):
    def test_the_map_regenerates_identically_from_curation(self):
        gen = load_generator()
        doc = gen.build_doc(gen.load_curation())
        text = json.dumps(doc, indent=1, ensure_ascii=False) + "\n"
        with open(COMMITTED_MAP, "rb") as f:
            committed = f.read()
        # the checkout may hold CRLF line endings; the committed text is LF
        self.assertEqual(committed.decode("utf-8").replace("\r\n", "\n"), text)

    def test_the_curation_itself_is_valid(self):
        gen = load_generator()
        curation = gen.load_curation()
        gen.check_curation(curation)
        gen.check_keys(curation, gen.load_keys())

    def test_a_control_without_a_label_is_refused(self):
        gen = load_generator()
        c = small_curation()
        del c["name_overrides"]["A330_DEMO"]
        with self.assertRaisesRegex(gen.CurationError, "A330_DEMO"):
            gen.check_curation(c)

    def test_an_empty_panel_is_refused_unless_it_is_the_definitions_own(self):
        gen = load_generator()
        c = small_curation()
        c["panel_order"]["Overhead"].append("Empty")
        with self.assertRaisesRegex(gen.CurationError, "Empty"):
            gen.check_curation(c)
        c["own_panels"].append("Empty")
        gen.check_curation(c)


class CatalogueTests(unittest.TestCase):
    CATALOGUE = os.path.join(HERE, "data", "input_events.tsv")

    def test_every_catalogued_event_is_a_control_or_left_out_with_a_reason(self):
        gen = load_generator()
        _, names = gen.load_catalogue(self.CATALOGUE)
        gen.check_catalogue(gen.load_curation(), names)

    def test_a_missing_catalogue_is_an_error(self):
        import tempfile
        gen = load_generator()
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaisesRegex(gen.CurationError, "input_events.tsv"):
                gen.load_catalogue(os.path.join(d, "input_events.tsv"))

    def test_the_map_is_never_generated_without_the_catalogue_check(self):
        # the catalogue is committed: a missing one stops the generator before it writes anything
        import tempfile
        gen = load_generator()
        with tempfile.TemporaryDirectory() as d:
            gen.CATALOGUE = os.path.join(d, "input_events.tsv")
            gen.OUT = os.path.join(d, "a330_control_map.json")
            gen.UNPROVEN = os.path.join(d, "unproven.txt")
            with self.assertRaises(gen.CurationError):
                gen.main()
            self.assertFalse(os.path.exists(gen.OUT))
            self.assertFalse(os.path.exists(gen.UNPROVEN))

    def test_an_unaccounted_event_is_refused_naming_it(self):
        gen = load_generator()
        names = ["AIRLINER_DEMO", "AIRLINER_PRESSED_BY_DEFINITION", "AIRLINER_FORGOTTEN"]
        with self.assertRaisesRegex(gen.CurationError, "AIRLINER_FORGOTTEN"):
            gen.check_catalogue(small_curation(), names)
        gen.check_catalogue(small_curation(), names[:2])

    def test_a_left_out_event_that_is_also_a_control_is_refused(self):
        gen = load_generator()
        c = small_curation()
        c["left_out"]["AIRLINER_DEMO"] = "also pressed by the definition"
        with self.assertRaisesRegex(gen.CurationError, "AIRLINER_DEMO"):
            gen.check_catalogue(c, ["AIRLINER_DEMO", "AIRLINER_PRESSED_BY_DEFINITION"])

    def test_a_left_out_event_needs_a_reason(self):
        gen = load_generator()
        c = small_curation()
        c["left_out"]["AIRLINER_PRESSED_BY_DEFINITION"] = " "
        with self.assertRaisesRegex(gen.CurationError, "AIRLINER_PRESSED_BY_DEFINITION"):
            gen.check_catalogue(c, ["AIRLINER_DEMO", "AIRLINER_PRESSED_BY_DEFINITION"])

    def test_a_control_naming_an_event_the_catalogue_lacks_is_refused(self):
        gen = load_generator()
        with self.assertRaisesRegex(gen.CurationError, "AIRLINER_DEMO"):
            gen.check_catalogue(small_curation(), ["AIRLINER_PRESSED_BY_DEFINITION"])

    def test_not_yet_offered_accounts_for_an_event_only_while_plan_04_is_open(self):
        gen = load_generator()
        c = small_curation()
        c["not_yet_offered"] = ["AIRLINER_LATER"]
        names = ["AIRLINER_DEMO", "AIRLINER_PRESSED_BY_DEFINITION", "AIRLINER_LATER"]
        gen.check_catalogue(c, names)
        gen.PLAN_04_OPEN = False
        with self.assertRaisesRegex(gen.CurationError, "not_yet_offered"):
            gen.check_catalogue(c, names)

    def test_the_catalogue_parser_reads_the_header_and_the_names(self):
        import tempfile
        gen = load_generator()
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "input_events.tsv")
            with open(p, "w", encoding="utf-8") as f:
                f.write("2026-11-27 MSFS 2024 1.0\nAIRLINER_A\t123\nAIRLINER_B\t456\n")
            header, names = gen.load_catalogue(p)
        self.assertIn("2026-11-27", header)
        self.assertEqual(["AIRLINER_A", "AIRLINER_B"], names)


class KeyTests(unittest.TestCase):
    def test_no_key_ever_disappears(self):
        gen = load_generator()
        curation = gen.load_curation()
        now = {e["key"] for e in curation["controls"].values()}
        for k in gen.load_keys():
            self.assertTrue(k in now or k in curation.get("retired_keys", {}), f"key {k} disappeared")

    def test_a_dropped_key_is_refused_naming_it(self):
        gen = load_generator()
        c = small_curation()
        with self.assertRaisesRegex(gen.CurationError, "A330_GONE"):
            gen.check_keys(c, ["A330_DEMO", "A330_DEMO_LT", "A330_GONE"])
        c["retired_keys"]["A330_GONE"] = "abc1234: removed because ..."
        gen.check_keys(c, ["A330_DEMO", "A330_DEMO_LT", "A330_GONE"])

    def test_keys_txt_holds_every_key_of_the_map(self):
        gen = load_generator()
        known = set(gen.load_keys())
        for e in gen.load_curation()["controls"].values():
            self.assertIn(e["key"], known)

    def test_a_duplicate_key_is_refused(self):
        gen = load_generator()
        c = small_curation()
        c["controls"]["AIRLINER_OTHER"] = copy.deepcopy(c["controls"]["AIRLINER_DEMO"])
        with self.assertRaisesRegex(gen.CurationError, "A330_DEMO"):
            gen.check_curation(c)


class PredictiveWindshearTests(unittest.TestCase):
    def test_predictive_windshear_reads_0_as_auto(self):
        gen = load_generator()
        control = next(c for c in gen.build_controls(gen.load_curation()) if c["key"] == "A330_WX_PWS")
        by_value = {p["value"]: p for p in control["positions"]}
        self.assertEqual("Auto", by_value[0]["label"])
        self.assertEqual("Off", by_value[1]["label"])
        # this switch reads back what was written (INI_WX_PWS_SWITCH and B:AIRLINER_WX_PWS agree)
        self.assertEqual([(0, 0), (1, 1)], [(p["value"], p["input"]) for p in control["positions"]])

    def test_predictive_windshear_carries_its_proof_and_both_positions_are_proven(self):
        gen = load_generator()
        entry = gen.load_curation()["controls"]["AIRLINER_WX_PWS"]
        # the E/WD memo the aircraft drives, which the control does not write itself (measured 2026-11-22)
        self.assertEqual({"kind": "ewd_memo", "text": "PRED W/S OFF", "when": "1", "measured": "2026-11-22"}, entry["proof"])
        self.assertEqual([True, True], [p["proven"] for p in entry["positions"]])

    def test_the_proof_and_proven_are_in_the_map(self):
        gen = load_generator()
        control = next(c for c in gen.build_controls(gen.load_curation()) if c["key"] == "A330_WX_PWS")
        self.assertEqual("ewd_memo", control["proof"]["kind"])
        self.assertEqual("PRED W/S OFF", control["proof"]["text"])
        self.assertEqual([True, True], [p["proven"] for p in control["positions"]])


UNPROVEN = os.path.join(HERE, "unproven.txt")


class MapFieldTests(unittest.TestCase):
    def test_every_offered_position_is_proven_or_listed(self):
        gen = load_generator()
        curation = gen.load_curation()
        with open(UNPROVEN, "rb") as f:
            committed = f.read().decode("utf-8").replace("\r\n", "\n")
        self.assertEqual(gen.build_unproven(curation), committed)
        listed = {tuple(line.split("\t")[:2]) for line in committed.splitlines() if line and not line.startswith("#")}
        expected = set()
        for e in curation["controls"].values():
            for p in e.get("positions", []):
                if not p["proven"]:
                    expected.add((e["key"], gen.invariant_number(p["value"])))
        self.assertEqual(expected, listed)
        self.assertNotIn(("A330_WX_PWS", "0"), listed)
        self.assertIn(("A330_STROBE", "0"), listed)

    def test_a_guard_names_a_guard_control_of_its_panel(self):
        gen = load_generator()
        curation = gen.load_curation()
        entries = list(curation["controls"].values())
        guarded = [(i, e) for i, e in enumerate(entries) if "guard" in e]
        self.assertEqual(27, len(guarded))
        for i, e in guarded:
            g = entries[i - 1]
            self.assertEqual(g["key"], e["guard"], e["key"])
            self.assertEqual(g["panel"], e["panel"], e["key"])
            self.assertEqual("select", g["kind"], e["key"])
            self.assertTrue(curation["name_overrides"][g["key"]]["label"].endswith(" guard"), e["key"])

    def test_state_is_only_on_buttons(self):
        gen = load_generator()
        curation = gen.load_curation()
        withstate = [e for e in curation["controls"].values() if "state" in e]
        self.assertEqual(21, len(withstate))
        for e in withstate:
            self.assertEqual("button", e["kind"], e["key"])
            self.assertTrue(e["state"].startswith("INI_"), e["key"])

    def test_a_unit_is_only_on_a_continuous_control(self):
        gen = load_generator()
        curation = gen.load_curation()
        withunit = [e for e in curation["controls"].values() if "unit" in e]
        self.assertEqual(["A330_WX_TILT"], [e["key"] for e in withunit])
        for e in withunit:
            self.assertEqual("continuous", e["kind"], e["key"])
            self.assertLess(e["unit"]["min"], e["unit"]["max"], e["key"])

    def test_a_label_with_a_unit_carries_no_input_numbers(self):
        # the control is offered in its unit, so a label's own numbers (once "15 = level") would disagree with it
        gen = load_generator()
        curation = gen.load_curation()
        for key, e in curation["controls"].items():
            if "unit" in e.keys() or "unit" in curation["controls"][key]:
                label = curation["name_overrides"][e["key"]]["label"]
                self.assertFalse(any(ch.isdigit() for ch in label), label)

    def test_the_map_carries_guard_state_unit_and_proven(self):
        gen = load_generator()
        by = {c["key"]: c for c in gen.build_controls(gen.load_curation())}
        self.assertEqual("A330_FUEL_TK_XFR_COVER", by["A330_FUEL_TK_XFR"]["guard"])
        self.assertEqual("INI_ECAM_ACTIVE_PAGE", by["A330_ECAM_ENG"]["state"])
        self.assertEqual({"name": "degrees", "min": -15, "max": 15}, by["A330_WX_TILT"]["unit"])
        self.assertFalse(by["A330_STROBE"]["positions"][0]["proven"])
        self.assertNotIn("guard", by["A330_STROBE"])
        self.assertNotIn("proof", by["A330_STROBE"])
        keys = list(by["A330_FUEL_TK_XFR"])
        self.assertEqual("guard", keys[-1] if "proof" not in by["A330_FUEL_TK_XFR"] else keys[-2])


# The push-buttons the live panel sweep of 2026-11-27 found toggling through the app's own route
# (SetInputEvent by hash): writing the same input value twice flipped the read back. Its 23:05 trial found a
# press of set 1, a hold and set 0 left the read where it was, so a press is ONE set of `press` (README.md).
MEASURED_TOGGLES = [
    "A330_PRIM1", "A330_PRIM2", "A330_PRIM3", "A330_SEC1", "A330_SEC2",
    "A330_FUEL1L1", "A330_FUEL1L2", "A330_FUEL1LSTBY", "A330_FUEL2R1", "A330_FUEL2R2", "A330_FUEL2RSTBY",
    "A330_FUEL_XFEED", "A330_FUEL_TRIM_MODE", "A330_FUEL_TK_XFR", "A330_INR_TK_L", "A330_INR_TK_R",
    "A330_GEN1", "A330_GEN2", "A330_APU_GEN", "A330_BUS_TIE", "A330_AC_ESS", "A330_GALLEY", "A330_COMMERCIAL",
    "A330_HYD_GREEN", "A330_HYD_GREEN_ENG2", "A330_HYD_BLUE", "A330_HYD_YELLOW",
    "A330_HYD_GREEN_ELEC_OFF", "A330_HYD_GREEN_ELEC_ON", "A330_HYD_BLUE_ELEC_OFF", "A330_HYD_BLUE_ELEC_ON",
    "A330_HYD_YELLOW_ELEC_OFF", "A330_HYD_YELLOW_ELEC_ON",
    "A330_BULK_ISO", "A330_ENG1_BLEED", "A330_ENG2_BLEED", "A330_APU_BLEED", "A330_PACK1", "A330_PACK2",
    "A330_HOT_AIR1", "A330_HOT_AIR2", "A330_CARGO_HOT_AIR", "A330_RAM_AIR", "A330_CAB_FANS", "A330_EXTRACT",
    "A330_WING_ANTI_ICE", "A330_ENG1_ANTI_ICE", "A330_ENG2_ANTI_ICE", "A330_FADEC1", "A330_FADEC2", "A330_CREW_OXY",
    "A330_CPT_FD", "A330_CPT_LS", "A330_CPT_ND_CSTR", "A330_CPT_ND_WPT", "A330_CPT_ND_VOR", "A330_CPT_ND_NDB",
    "A330_CPT_ND_ARPT", "A330_CPT_TERR_ON_ND",
    "A330_FO_FD", "A330_FO_LS", "A330_FO_ND_CSTR", "A330_FO_ND_WPT", "A330_FO_ND_VOR", "A330_FO_ND_NDB",
    "A330_FO_ND_ARPT", "A330_FO_TERR_ON_ND",
    "A330_BRAKE_FAN", "A330_GPWS_SYS", "A330_GPWS_TERR", "A330_GPWS_GS", "A330_GPWS_FLAP", "A330_LAND_RECOVERY",
]


class MeasuredToggleTests(unittest.TestCase):
    def test_the_measured_toggles_are_pressed(self):
        self.assertEqual(73, len(set(MEASURED_TOGGLES)))
        gen = load_generator()
        curation = gen.load_curation()
        by = {c["key"]: c for c in gen.build_controls(curation)}
        for k in MEASURED_TOGGLES:
            c = by[k]
            self.assertEqual("toggle", c["mode"], k)
            self.assertEqual(1, c.get("press"), k)
            # a press is one set: no release value is sent after it (a second set is a second press)
            self.assertNotIn("release", c, k)
            self.assertNotIn("rpn", c, k)
            # still a two-position selector read by its own variable: the combo and its names are unchanged
            self.assertEqual("select", c["kind"], k)
            self.assertTrue(c["read"].startswith("INI_"), k)
            self.assertEqual(2, len(c["positions"]), k)
        # nothing else is pressed by one set: the engine masters stay toggles by their Set's value
        pressed = sorted(c["key"] for c in by.values() if "press" in c)
        self.assertEqual(sorted(MEASURED_TOGGLES), pressed)
        self.assertEqual([], [c["key"] for c in by.values() if "release" in c])

    def test_a_release_value_is_refused(self):
        # set 1, a hold, set 0 was two presses on these buttons (the 23:05 trial): a release in the curation would
        # bring that dead press back
        gen = load_generator()
        c = small_curation()
        c["controls"]["AIRLINER_DEMO"].update({"mode": "toggle", "press": 1, "release": 0})
        with self.assertRaisesRegex(gen.CurationError, "A330_DEMO.*release"):
            gen.check_curation(c)
        del c["controls"]["AIRLINER_DEMO"]["release"]
        gen.check_curation(c)


class HardeningTests(unittest.TestCase):
    def test_a_repeated_key_in_the_curation_is_refused(self):
        import tempfile
        gen = load_generator()
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "curation.json")
            with open(p, "w", encoding="utf-8") as f:
                f.write('{"controls": {"AIRLINER_A": {"key": "A330_A"}, "AIRLINER_A": {"key": "A330_B"}}}')
            with self.assertRaisesRegex(gen.CurationError, "AIRLINER_A"):
                gen.load_curation(p)

    def test_an_orphan_override_is_refused(self):
        gen = load_generator()
        c = small_curation()
        c["name_overrides"]["A330_NOBODY"] = {"label": "Nobody", "aircraft_says": None, "why": "test"}
        with self.assertRaisesRegex(gen.CurationError, "A330_NOBODY"):
            gen.check_curation(c)
        c = small_curation()
        c["position_overrides"]["A330_NOBODY"] = {"0": {"label": "x", "aircraft_says": None, "why": "test"}}
        with self.assertRaisesRegex(gen.CurationError, "A330_NOBODY"):
            gen.check_curation(c)
        c = small_curation()
        c["position_overrides"]["A330_DEMO"]["7"] = {"label": "x", "aircraft_says": None, "why": "test"}
        with self.assertRaisesRegex(gen.CurationError, "'7'.*A330_DEMO"):
            gen.check_curation(c)
        gen.check_curation(small_curation())

    def test_an_event_both_left_out_and_not_yet_offered_is_refused(self):
        gen = load_generator()
        c = small_curation()
        c["not_yet_offered"] = ["AIRLINER_PRESSED_BY_DEFINITION"]
        with self.assertRaisesRegex(gen.CurationError, "AIRLINER_PRESSED_BY_DEFINITION.*not_yet_offered"):
            gen.check_catalogue(c, ["AIRLINER_DEMO", "AIRLINER_PRESSED_BY_DEFINITION"])


if __name__ == "__main__":
    unittest.main()
