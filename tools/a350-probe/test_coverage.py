"""coverage.measure: a drawn text counts as unsaid only when a word or number of it is in no line its display said (plan 24)."""
import unittest

import coverage


def _cap(*texts):
    return {'Texts': [{'Text': t, 'ColourName': 'green', 'X': 1, 'Y': 2} for t in texts]}


class MeasureTests(unittest.TestCase):
    def test_a_dot_leader_or_a_glyph_is_no_gap(self):
        read = ['== E/WD', '  PARK BRK: OFF, cyan', '  ENG 1 EGT: 67 °C, green']
        drawn, missed, _ = coverage.measure([(read, {'CTR_0': _cap('PARK BRK ......OFF', '67!C')})])
        self.assertEqual(2, sum(drawn.values()))
        self.assertEqual(0, sum(missed.values()))

    def test_a_word_said_nowhere_is_a_gap_with_its_place(self):
        read = ['== Captain HUD display', '  ILS, green']
        _, missed, place = coverage.measure([(read, {'HUD_0': _cap('ILS', '109.90')})])
        self.assertEqual({('HUD_0', '109.90', 'green'): 1}, dict(missed))
        self.assertEqual((1, 2), place[('HUD_0', '109.90', 'green')])

    def test_a_display_is_matched_against_its_own_sections_only(self):
        read = ['== Captain PFD', '  Speed: 250 knots', '== Captain ND', '  To waypoint: KAT', '== First Officer PFD']
        _, missed, _ = coverage.measure([(read, {'EFIS_0': _cap('250', 'KAT'), 'EFIS_1': _cap('KAT')})])
        self.assertEqual({('EFIS_1', 'KAT', 'green'): 1}, dict(missed))

    def test_leading_zeros_and_the_snds_separators_are_the_same_number(self):
        read = ['== Standby navigation display', '  Position: N07 10.3 E079 53.5, green', '  Heading: 5']
        _, missed, _ = coverage.measure([(read, {'SND_0': _cap('N07|10.3/E079|53.5', '005')})])
        self.assertEqual(0, sum(missed.values()))

    def test_a_text_of_no_word_is_not_counted(self):
        drawn, _, _ = coverage.measure([(['== FCU display'], {'FCU_0': _cap('---', ' ', '.')})])
        self.assertEqual(0, sum(drawn.values()))


if __name__ == '__main__':
    unittest.main()
