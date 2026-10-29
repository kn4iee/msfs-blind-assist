"""The generator's paths the map fixture does not reach (tools review 2026-10-30): a knob, a spring selector and a command
classified from their input events (the knob and the spring are 1.2.7's own, the command synthetic: 1.2.7 has none), a
knob push's words, the checklist steps, the digit-tag rewrite and main() end to end."""
import contextlib
import io
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import generate_a350_map as gen


def _ie(ie_id, **fields):
    ie = {'id': ie_id, 'tt_desc': '', 'tt_value': '', 'units': '', 'init': '', 'set_code': '', 'inc_code': '', 'dec_code': ''}
    ie.update(fields)
    return ie


def _classified(ie, loc=None):
    return gen.classify(gen.new_entry(ie, 'OVERHEAD', 'PANEL', 'TITLE'), ie, loc or {})


class KindTests(unittest.TestCase):
    def test_a_percent_control_writing_its_l_var_is_a_knob(self):
        # AIRLINER_AIR_TEMP_COCKPIT, A350 1.2.7.
        c = _classified(_ie('AIRLINER_AIR_TEMP_COCKPIT', units='percent',
                            init='(L:INI_TEMP_KNOB1) 0 max 100 min (>O:AIRLINER_AIR_TEMP_COCKPIT_Position)',
                            set_code='p0 0 max 100 min s0 (>O:AIRLINER_AIR_TEMP_COCKPIT_Position) l0 (>L:INI_TEMP_KNOB1) '
                                     '(O:AIRLINER_AIR_TEMP_COCKPIT_Position) 3.6 * dnor (>O:_KnobAnimVar)',
                            dec_code='(L:INI_TEMP_KNOB1) p0 - (>B:AIRLINER_AIR_TEMP_COCKPIT_Set)'))
        self.assertEqual(('knob', 'L:INI_TEMP_KNOB1', 1.0), (c['kind'], c['state_var'], c['scale']))
        self.assertEqual('AIRLINER_AIR_TEMP_COCKPIT_Set', c['event'])

    def test_a_three_position_selector_with_a_neutral_middle_springs_back_to_it(self):
        # AIRLINER_OIS_AVNCS_EFB_LEFT, A350 1.2.7: EFB, NEUTRAL, AVNCS.
        loc = {'INI.TOOLTIPS.OIS_AVNCS_EFB_LEFT.0': 'EFB', 'INI.TOOLTIPS.OIS_AVNCS_EFB_LEFT.1': 'NEUTRAL',
               'INI.TOOLTIPS.OIS_AVNCS_EFB_LEFT.2': 'AVNCS'}
        c = _classified(_ie('AIRLINER_OIS_AVNCS_EFB_LEFT', units='numbers',
                            init='(L:INI_OIS_AVNCS_EFB_LEFT) sp0 l0 (>O:AIRLINER_OIS_AVNCS_EFB_LEFT_Position)',
                            set_code='p0 2 min 0 max (>O:AIRLINER_OIS_AVNCS_EFB_LEFT_Position) (O:AIRLINER_OIS_AVNCS_EFB_LEFT_Position) '
                                     's0 l0 0 == if{ 0 sp0 l0 (>L:INI_OIS_AVNCS_EFB_LEFT) l0 (>O:PrevPos) g1 } '
                                     'l0 1 == if{ 1 sp0 l0 (>L:INI_OIS_AVNCS_EFB_LEFT) l0 (>O:PrevPos) g1 } '
                                     'l0 2 == if{ 2 sp0 l0 (>L:INI_OIS_AVNCS_EFB_LEFT) l0 (>O:PrevPos) g1 } :1',
                            dec_code='(O:AIRLINER_OIS_AVNCS_EFB_LEFT_Position) p0 - (>B:AIRLINER_OIS_AVNCS_EFB_LEFT_Set)',
                            tt_value='(B:AIRLINER_OIS_AVNCS_EFB_LEFT, Number) sp0 l0 0 == if{ (R:1:INI.TOOLTIPS.OIS_AVNCS_EFB_LEFT.0) quit } '
                                     'l0 1 == if{ (R:1:INI.TOOLTIPS.OIS_AVNCS_EFB_LEFT.1) quit } '
                                     'l0 2 == if{ (R:1:INI.TOOLTIPS.OIS_AVNCS_EFB_LEFT.2) quit }'), loc)
        self.assertEqual(('spring', 1, 'L:INI_OIS_AVNCS_EFB_LEFT'), (c['kind'], c['rest'], c['state_var']))
        self.assertEqual({'0': 'EFB', '1': 'NEUTRAL', '2': 'AVNCS'}, dict(c['positions']))

    def test_a_constant_written_to_a_command_shown_by_another_l_var_is_a_command(self):
        loc = {'INI.TOOLTIPS.EXAMPLE.OFF': 'OFF', 'INI.TOOLTIPS.EXAMPLE.ON': 'ON'}
        c = _classified(_ie('AIRLINER_EXAMPLE', units='boolean',
                            set_code='1 (>L:INI_EXAMPLE_COMMAND)',
                            tt_value='(L:INI_EXAMPLE_STATE, number) 0 == if{ (R:1:INI.TOOLTIPS.EXAMPLE.OFF) } '
                                     'els{ (R:1:INI.TOOLTIPS.EXAMPLE.ON) }'),
                        loc)
        self.assertEqual(('command', 'L:INI_EXAMPLE_STATE'), (c['kind'], c['state_var']))


class KnobActionTests(unittest.TestCase):
    def test_a_knob_push_or_pull_reads_its_words_beside_the_title(self):
        loc = {'INI.TOOLTIPS.FCU_SPD_KNOB.PUSH': 'MANAGED SPEED', 'INI.TOOLTIPS.FCU_SPD_KNOB.PULL': 'SELECTED SPEED'}
        self.assertEqual('MANAGED SPEED', gen.knob_action(loc, 'INI.TOOLTIPS.FCU_SPD_KNOB.TITLE', 'AIRLINER_FCU_SPD_KNOB_PUSH'))
        self.assertEqual('SELECTED SPEED', gen.knob_action(loc, 'INI.TOOLTIPS.FCU_SPD_KNOB.TITLE', 'AIRLINER_FCU_SPD_KNOB_pull'))
        self.assertIsNone(gen.knob_action(loc, 'INI.TOOLTIPS.FCU_SPD_KNOB.TITLE', 'AIRLINER_FCU_SPD_KNOB'))
        self.assertIsNone(gen.knob_action(loc, 'INI.TOOLTIPS.FCU_SPD_KNOB.ACTION', 'AIRLINER_FCU_SPD_KNOB_PUSH'))


class ChecklistStepTests(unittest.TestCase):
    def test_each_control_takes_the_first_live_step_naming_it(self):
        data = {'controls': [
            {'id': 'AIRLINER_ELEC_BAT_1', 'state_var': 'L:INI_BATTERY_1_SWITCH', 'event': 'AIRLINER_ELEC_BAT_1_Set', 'checklist': None},
            {'id': 'AIRLINER_APU_MASTER', 'state_var': 'L:INI_APU_MASTER', 'event': 'AIRLINER_APU_MASTER_Set', 'checklist': None},
            {'id': 'AIRLINER_EXT_1', 'state_var': None, 'event': 'AIRLINER_EXT_1_Set', 'checklist': None},
            {'id': 'AIRLINER_NOT_NAMED', 'state_var': None, 'event': None, 'checklist': None},
        ]}
        xml = ('<Checklist>'
               '<!-- <Checkpoint><Instrument id="NOT_NAMED"/></Checkpoint> -->'
               '<Checkpoint><Instrument id="ELEC_BAT_1"/></Checkpoint>'
               '<Checkpoint><Test Code="(L:INI_APU_MASTER) 1 =="/></Checkpoint>'
               '<Checkpoint><Action Code="1 (>B:AIRLINER_EXT_1_Set)"/><Instrument id="ELEC_BAT_1"/></Checkpoint>'
               '</Checklist>')
        gen.add_checklist_steps(data, xml)
        self.assertEqual([1, 2, 3, None], [c['checklist'] for c in data['controls']])


class ReadBehaviorTests(unittest.TestCase):
    def test_part_marker_tags_beginning_with_a_digit_are_read(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, 'A350_Interior.behavior.xml')
            with open(path, 'w', encoding='utf-8') as f:
                f.write('<ModelBehaviors><Component ID="A350_INTERIOR"><0FAB/><12AB/></Component></ModelBehaviors>')
            self.assertIsNotNone(gen.read_behavior(path))


class MainTests(unittest.TestCase):
    def test_main_writes_the_map_from_a_package(self):
        import test_generate_a350_map as fixture
        with tempfile.TemporaryDirectory() as pkg:
            behavior = os.path.join(pkg, gen.BEHAVIOR_FILE)
            checklist = os.path.join(pkg, gen.CHECKLIST_FILE)
            os.makedirs(os.path.dirname(behavior))
            os.makedirs(os.path.dirname(checklist))
            with open(behavior, 'w', encoding='utf-8') as f:
                f.write(fixture._fixture())
            with open(checklist, 'w', encoding='utf-8') as f:
                f.write('<Checklist><Checkpoint><Instrument id="ELEC_BAT_1"/></Checkpoint></Checklist>')
            with open(os.path.join(pkg, gen.LOC_FILE), 'w', encoding='utf-8') as f:
                json.dump({'LocalisationPackage': {'Strings': fixture.LOC}}, f)
            with open(os.path.join(pkg, 'manifest.json'), 'w', encoding='utf-8') as f:
                json.dump({'package_version': '1.2.7'}, f)
            out = os.path.join(pkg, 'map.json')
            printed = io.StringIO()
            with contextlib.redirect_stdout(printed):
                self.assertEqual(0, gen.main(['--package', pkg, '--out', out]))
            with open(out, encoding='utf-8') as f:
                data = json.load(f)
            self.assertEqual('1.2.7', data['package_version'])
            self.assertEqual(12, len(data['controls']))
            self.assertEqual(1, {c['id']: c for c in data['controls']}['AIRLINER_ELEC_BAT_1']['checklist'])
            self.assertIn('controls 12', printed.getvalue())


if __name__ == '__main__':
    unittest.main()
