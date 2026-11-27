"""Tests for the A330 control-map generator (run: python -I -m unittest discover -s tools/a330-gen -p "test_*.py")."""
import importlib.util
import os
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))


def load_generator():
    spec = importlib.util.spec_from_file_location("generate_a330_map", os.path.join(HERE, "generate_a330_map.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class PredictiveWindshearTests(unittest.TestCase):
    def test_predictive_windshear_reads_0_as_auto(self):
        gen = load_generator()
        control = next(c for c in gen.CONTROLS if c["key"] == "A330_WX_PWS")
        by_value = {p["value"]: p for p in control["positions"]}
        self.assertEqual("Auto", by_value[0]["label"])
        self.assertEqual("Off", by_value[1]["label"])
        # this switch reads back what was written (INI_WX_PWS_SWITCH and B:AIRLINER_WX_PWS agree)
        self.assertEqual([(0, 0), (1, 1)], [(p["value"], p["input"]) for p in control["positions"]])


if __name__ == "__main__":
    unittest.main()
