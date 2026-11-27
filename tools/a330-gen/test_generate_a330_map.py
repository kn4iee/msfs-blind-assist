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

    @unittest.skipUnless(os.path.exists(CATALOGUE), "data/input_events.tsv not captured yet (plan 04 Task 1 Step 2)")
    def test_every_catalogued_event_is_a_control_or_left_out_with_a_reason(self):
        gen = load_generator()
        _, names = gen.load_catalogue(self.CATALOGUE)
        gen.check_catalogue(gen.load_curation(), names)

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

    def test_the_proof_is_not_in_the_map_yet(self):
        gen = load_generator()
        control = next(c for c in gen.build_controls(gen.load_curation()) if c["key"] == "A330_WX_PWS")
        self.assertNotIn("proof", control)
        self.assertNotIn("proven", control["positions"][0])


if __name__ == "__main__":
    unittest.main()
