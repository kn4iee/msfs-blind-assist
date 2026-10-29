import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import generate_a350_map as gen
from a350_fixture import FixtureBuilder

# Tooltip words in the shapes en-US.locPak holds them (package 1.2.7).
LOC = {
    'INI.TOOLTIPS.GRAVITY_GEAR_COVER.0': 'OPEN', 'INI.TOOLTIPS.GRAVITY_GEAR_COVER.1': 'CLOSE',
    'INI.TOOLTIPS.ELEC_BAT_1.TITLE': 'BAT 1',
    'INI.TOOLTIPS.ELEC_BAT_1.OFF': 'PUSH',
    'INI.TOOLTIPS.ELEC_BAT_1.ON': 'PUSH',
    'INI.TOOLTIPS.ANTI_ICE_WING.TITLE': 'WING',
    'INI.TOOLTIPS.ANTI_ICE_WING.OFF': 'CLOSE',
    'INI.TOOLTIPS.ANTI_ICE_WING.ON': 'OPEN',
    'INI.TOOLTIPS.SIGNS_SEAT_BELTS.TITLE': 'SEAT BELTS',
    'INI.TOOLTIPS.SIGNS_SEAT_BELTS.1': 'ON',
    'INI.TOOLTIPS.SIGNS_SEAT_BELTS.2': 'AUTO',
    'INI.TOOLTIPS.SIGNS_SEAT_BELTS.3': 'OFF',
    # A selector whose positions write its L:var's values in reverse (none on the 1.2.7 map; review 2026-10-29).
    'INI.TOOLTIPS.SIGNS_REVERSED.TITLE': 'REVERSED',
    'INI.TOOLTIPS.SIGNS_REVERSED.1': 'STATE 0', 'INI.TOOLTIPS.SIGNS_REVERSED.2': 'STATE 1',
    'INI.TOOLTIPS.SIGNS_REVERSED.3': 'STATE 2',
    'INI.TOOLTIPS.MAINT_ELEC_BAT_VOLTAGE_SELECTOR.TITLE': 'BAT SELECTOR',
    'INI.TOOLTIPS.MAINT_ELEC_BAT_VOLTAGE_SELECTOR.0': 'EMER 1',
    'INI.TOOLTIPS.MAINT_ELEC_BAT_VOLTAGE_SELECTOR.1': 'EMER 2',
    'INI.TOOLTIPS.MAINT_ELEC_BAT_VOLTAGE_SELECTOR.2': 'OFF',
    'INI.TOOLTIPS.MAINT_ELEC_BAT_VOLTAGE_SELECTOR.3': 'BAT 1',
    'INI.TOOLTIPS.MAINT_ELEC_BAT_VOLTAGE_SELECTOR.4': 'BAT 2',
    'INI.TOOLTIPS.EMER_ELEC_RAT.TITLE': 'RAT',
    'INI.TOOLTIPS.APU_START.TITLE': 'APU START',
    'INI.TOOLTIPS.ECAM_ENG.TITLE': 'ENG',
    'INI.TOOLTIPS.CABIN_PRESS_ALTITUDE_TARGET.TITLE': 'LDG ELEVN',
    'INI.TOOLTIPS.LDG_LEVER.TITLE': 'L/G LEVER',
    'INI.TOOLTIPS.LDG_LEVER.UP': 'UP',
    'INI.TOOLTIPS.LDG_LEVER.DOWN': 'DOWN',
}


def _fixture():
    f = FixtureBuilder()
    root = f.component('A350_INTERIOR')
    overhead = f.component('OVERHEAD', root)
    elec = f.component('ELEC', overhead)
    signs = f.component('SIGNS', overhead)
    anti_ice = f.component('ANTI_ICE', overhead)
    maint = f.component('MAINTENANCE', overhead)
    emer = f.component('EMER_ELEC', overhead)
    apu = f.component('APU', overhead)
    press = f.component('CABIN_PRESS', overhead)
    pedestal = f.component('PEDESTAL', root)
    ecam = f.component('ECAM_BUTTONS', pedestal)
    mip = f.component('MIP', root)
    ldg = f.component('LDG_PANEL', mip)

    def add(owner, ie_id, title, **ie):
        f.mouserect(owner, ie_id.replace('AIRLINER_', ''), '(>B:%s_Toggle)' % ie_id, title=title, entries=[ie_id])
        short = ie_id.replace('AIRLINER_', '')
        ie.setdefault('inc', '(O:%s_Position) p0 + (>B:%s_Set)' % (ie_id, ie_id))
        ie.setdefault('dec', '(O:%s_Position) p0 - (>B:%s_Set)' % (ie_id, ie_id))
        f.inputevent(owner, ie_id, **ie)

    def onoff(name):
        return ("(O:AIRLINER_%s_Position) 0 == if{ (R:1:INI.TOOLTIPS.%s.OFF) } els{ (R:1:INI.TOOLTIPS.%s.ON) }"
                % (name, name, name))

    add(elec, 'AIRLINER_ELEC_BAT_1', 'INI.TOOLTIPS.ELEC_BAT_1.TITLE', units='Boolean',
        set_code=("p0 1 min 0 max (>O:AIRLINER_ELEC_BAT_1_Position) (L:INI_BATTERY_1_SWITCH) ! "
                  "(>L:INI_BATTERY_1_SWITCH) 1 (>O:_ButtonAnimVar)"),
        init="l0 1 0 l0 ? (>O:AIRLINER_ELEC_BAT_1_Position)", tt_value=onoff('ELEC_BAT_1'))
    add(anti_ice, 'AIRLINER_ANTI_ICE_WING', 'INI.TOOLTIPS.ANTI_ICE_WING.TITLE', units='Boolean',
        set_code=("p0 1 min 0 max (>O:AIRLINER_ANTI_ICE_WING_Position) (L:INI_WING_ANTI_ICE) ! "
                  "(>L:INI_WING_ANTI_ICE) 1 (>O:_ButtonAnimVar)"),
        init="l0 1 0 l0 ? (>O:AIRLINER_ANTI_ICE_WING_Position)", tt_value=onoff('ANTI_ICE_WING'))
    add(signs, 'AIRLINER_SIGNS_SEAT_BELTS', 'INI.TOOLTIPS.SIGNS_SEAT_BELTS.TITLE', units='numbers',
        set_code=("p0 2 min 0 max (>O:AIRLINER_SIGNS_SEAT_BELTS_Position) (O:AIRLINER_SIGNS_SEAT_BELTS_Position) s0 "
                  "l0 0 == if{ 0 (>L:INI_SEATBELTS_SWITCH) g1 } l0 1 == if{ 1 (>L:INI_SEATBELTS_SWITCH) g1 } "
                  "l0 2 == if{ 2 (>L:INI_SEATBELTS_SWITCH) g1 } :1"),
        init="(L:INI_SEATBELTS_SWITCH) sp0 l0 (>O:AIRLINER_SIGNS_SEAT_BELTS_Position)",
        tt_value=' '.join("(L:INI_SEATBELTS_SWITCH, number) %d == if{ (R:1:INI.TOOLTIPS.SIGNS_SEAT_BELTS.%d) '%%s' (F:Format) }"
                          % (v, v + 1) for v in range(3)))
    add(signs, 'AIRLINER_SIGNS_REVERSED', 'INI.TOOLTIPS.SIGNS_REVERSED.TITLE', units='numbers',
        set_code=("p0 2 min 0 max (>O:AIRLINER_SIGNS_REVERSED_Position) (O:AIRLINER_SIGNS_REVERSED_Position) s0 "
                  "l0 0 == if{ 2 (>L:INI_REVERSED) g1 } l0 1 == if{ 1 (>L:INI_REVERSED) g1 } "
                  "l0 2 == if{ 0 (>L:INI_REVERSED) g1 } :1"),
        init="(L:INI_REVERSED) sp0 l0 (>O:AIRLINER_SIGNS_REVERSED_Position)",
        tt_value=' '.join("(L:INI_REVERSED, number) %d == if{ (R:1:INI.TOOLTIPS.SIGNS_REVERSED.%d) '%%s' (F:Format) }"
                          % (v, v + 1) for v in range(3)))
    add(maint, 'AIRLINER_MAINT_ELEC_BAT_VOLTAGE_SELECTOR', 'INI.TOOLTIPS.MAINT_ELEC_BAT_VOLTAGE_SELECTOR.TITLE',
        units='numbers',
        set_code=("p0 4 min 0 max (>O:S) (O:S) s0 " + ' '.join(
            "l0 %d == if{ %d (>L:INI_BATT_VOLTAGE_SELECTOR) g1 }" % (v, v) for v in range(5)) + " :1"),
        init="(L:INI_BATT_VOLTAGE_SELECTOR) sp0 l0 (>O:S)",
        tt_value="(B:AIRLINER_MAINT_ELEC_BAT_VOLTAGE_SELECTOR, Number) sp0 " + ' '.join(
            "l0 %d == if{ (R:1:INI.TOOLTIPS.MAINT_ELEC_BAT_VOLTAGE_SELECTOR.%d) quit }" % (v, v) for v in range(5)))
    add(emer, 'AIRLINER_EMER_ELEC_RAT_COVER', 'INI.TOOLTIPS.EMER_ELEC_RAT.TITLE', units='Boolean',
        set_code=("p0 1 min 0 max (>O:AIRLINER_EMER_ELEC_RAT_Cover_Position) (O:AIRLINER_EMER_ELEC_RAT_Cover_Position) s0 "
                  "l0 0 == if{ 0 (>O:AIRLINER_EMER_ELEC_RAT_Cover_Position) g1 } "
                  "l0 1 == if{ 1 (>O:AIRLINER_EMER_ELEC_RAT_Cover_Position) g1 } :1 1 (>O:_ButtonAnimVar)"),
        inc='(O:AIRLINER_EMER_ELEC_RAT_Cover_Position) p0 + (>B:AIRLINER_EMER_ELEC_RAT_Cover_Set)',
        dec='(O:AIRLINER_EMER_ELEC_RAT_Cover_Position) p0 - (>B:AIRLINER_EMER_ELEC_RAT_Cover_Set)')
    add(apu, 'AIRLINER_APU_START', 'INI.TOOLTIPS.APU_START.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:AIRLINER_APU_START_Position) 1 (>L:INI_APU_START_BUTTON) 1 (>O:_ButtonAnimVar)",
        tt_value=onoff('APU_START'))
    add(press, 'AIRLINER_CABIN_PRESS_ALTITUDE_TARGET', 'INI.TOOLTIPS.CABIN_PRESS_ALTITUDE_TARGET.TITLE', units='',
        set_code=("p0 p0 0 > if{ 1 (>L:INI_CABIN_PRESS_ALTITUDE_TARGET_INC) } els{ 1 (>L:INI_CABIN_PRESS_ALTITUDE_TARGET_DEC) } "
                  "(O:_KnobAnimVar) 10 p0 * + dnor (>O:_KnobAnimVar)"),
        tt_value="(L:INI_CABIN_PRESS_ALTITUDE_TARGET) flr '%dft' (F:Format)",
        inc='p0 (>B:AIRLINER_CABIN_PRESS_ALTITUDE_TARGET_Set)', dec='p0 -1 * (>B:AIRLINER_CABIN_PRESS_ALTITUDE_TARGET_Set)')
    add(ecam, 'AIRLINER_ECAM_ENG', 'INI.TOOLTIPS.ECAM_ENG.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:AIRLINER_ECAM_ENG_Position) p0 2 == if{ 1 (>L:PUSH_ECAM_ENG) } els{ 0 (>L:PUSH_ECAM_ENG) }",
        tt_value=onoff('ECAM_ENG'))
    # The gear lever writes the aircraft's own key events, not an L:var (package 1.2.7).
    add(ldg, 'AIRLINER_LDG_LEVER', 'INI.TOOLTIPS.LDG_LEVER.TITLE', units='Boolean',
        set_code=("p0 1 min 0 max (>O:AIRLINER_LDG_LEVER_Position) p0 0 == if{ (A:SIM ON GROUND, bool) ! if{ "
                  "'INI.GEAR_UP' (>F:KeyEvent) } els{ 1 (>B:AIRLINER_LDG_LEVER_Set) } } els{ 'INI.GEAR_DOWN' (>F:KeyEvent) }"),
        init="(L:INI_GEAR_HANDLE_STATUS) 1 0 l0 ? (>O:AIRLINER_LDG_LEVER_Position)",
        tt_value=("(O:AIRLINER_LDG_LEVER_Position) 0 == if{ (R:1:INI.TOOLTIPS.LDG_LEVER.UP) } "
                  "els{ (R:1:INI.TOOLTIPS.LDG_LEVER.DOWN) } '%s' (F:Format)"))
    # A tooltip that reads the event's own Bool: true is the .1 word (the gravity gear cover CLOSE), false the .0.
    add(ecam, 'AIRLINER_GRAVITY_GEAR_COVER', 'INI.TOOLTIPS.GRAVITY_GEAR_COVER.TITLE', units='Boolean',
        set_code='p0 1 min 0 max (>O:AIRLINER_GRAVITY_GEAR_COVER_Position) p0 (>L:INI_GRAVITY_GEAR_COVER)',
        init='(L:INI_GRAVITY_GEAR_COVER) sp0 l0 (>O:AIRLINER_GRAVITY_GEAR_COVER_Position)',
        tt_value=('(B:AIRLINER_GRAVITY_GEAR_COVER, Bool) if{ (R:1:INI.TOOLTIPS.GRAVITY_GEAR_COVER.1) } '
                  'els{ (R:1:INI.TOOLTIPS.GRAVITY_GEAR_COVER.0) }'))
    # The HUD declutter key: its input event only animates the key; the click code flips INI_FCU_HUD_DECLUTTER_LEFT.
    add(ecam, 'INSTRUMENT_FCU_HUD_DECLUTTER_LEFT_IE_ID', 'INI.TOOLTIPS.FCU_HUD_DECLUTTER_LEFT.ACTION', units='Boolean',
        set_code=("p0 1 min 0 max (>O:INSTRUMENT_FCU_HUD_DECLUTTER_LEFT_IE_ID_Position) "
                  "(O:INSTRUMENT_FCU_HUD_DECLUTTER_LEFT_IE_ID_Position) s0 l0 0 == if{ 0 (>L:INI_FCU_HUD_DECLUTTER_LEFT_ANIM) g1 } "
                  "l0 1 == if{ 1 (>L:INI_FCU_HUD_DECLUTTER_LEFT_ANIM) g1 } :1 1 (>O:_ButtonAnimVar)"),
        init="(L:INI_FCU_HUD_DECLUTTER_LEFT_ANIM) sp0 l0 (>O:INSTRUMENT_FCU_HUD_DECLUTTER_LEFT_IE_ID_Position)",
        tt_value=("(B:INSTRUMENT_FCU_HUD_DECLUTTER_LEFT_IE_ID, Bool) if{ (R:1:COCKPIT.TOOLTIPSV2.GT_STATE_ON) } "
                  "els{ (R:1:COCKPIT.TOOLTIPSV2.GT_STATE_OFF) }"))
    return f.build()


def _map():
    path = os.path.join(tempfile.mkdtemp(), 'A350_Interior.behavior.xml')
    with open(path, 'w', encoding='utf-8') as fh:
        fh.write(_fixture())
    return gen.build_map(gen.read_behavior(path), LOC, '1.2.7')


def _by_id():
    return {c['id']: c for c in _map()['controls']}


class GenerateA350MapTests(unittest.TestCase):
    def test_every_control_is_read(self):
        self.assertEqual(12, len(_map()['controls']))

    def test_area_and_panel_come_from_the_component_tree(self):
        c = _by_id()['AIRLINER_ELEC_BAT_1']
        self.assertEqual(('OVERHEAD', 'ELEC'), (c['area'], c['panel']))
        self.assertEqual(('PEDESTAL', 'ECAM_BUTTONS'), (_by_id()['AIRLINER_ECAM_ENG']['area'], _by_id()['AIRLINER_ECAM_ENG']['panel']))

    def test_toggle_reads_its_l_var(self):
        c = _by_id()['AIRLINER_ELEC_BAT_1']
        self.assertEqual('toggle', c['kind'])
        self.assertEqual('A350_ELEC_BAT_1', c['key'])
        self.assertEqual('BAT 1', c['title'])
        self.assertEqual('AIRLINER_ELEC_BAT_1_Set', c['event'])
        self.assertEqual('L:INI_BATTERY_1_SWITCH', c['state_var'])

    def test_toggle_words_from_the_tooltip_file(self):
        self.assertEqual({'0': 'CLOSE', '1': 'OPEN'}, _by_id()['AIRLINER_ANTI_ICE_WING']['positions'])

    def test_toggle_whose_words_are_both_push_reads_off_and_on(self):
        self.assertEqual({'0': 'OFF', '1': 'ON'}, _by_id()['AIRLINER_ELEC_BAT_1']['positions'])

    def test_selector_worded_by_its_l_var_value_counts_from_one(self):
        c = _by_id()['AIRLINER_SIGNS_SEAT_BELTS']
        self.assertEqual('selector', c['kind'])
        self.assertEqual('L:INI_SEATBELTS_SWITCH', c['state_var'])
        self.assertEqual({'0': 'ON', '1': 'AUTO', '2': 'OFF'}, c['positions'])

    # Review 2026-10-29 (panels minor 7): words by the L:var's value were looked up by the position, so a selector whose
    # positions write other values read the wrong words. Each position reads the word of the value it writes.
    def test_selector_worded_by_its_l_var_value_reads_each_positions_value(self):
        c = _by_id()['AIRLINER_SIGNS_REVERSED']
        self.assertEqual({'0': '2', '1': '1', '2': '0'}, c['values'])
        self.assertEqual({'0': 'STATE 2', '1': 'STATE 1', '2': 'STATE 0'}, c['positions'])

    def test_selector_worded_by_position_counts_from_zero(self):
        c = _by_id()['AIRLINER_MAINT_ELEC_BAT_VOLTAGE_SELECTOR']
        self.assertEqual({'0': 'EMER 1', '1': 'EMER 2', '2': 'OFF', '3': 'BAT 1', '4': 'BAT 2'}, c['positions'])

    def test_cover(self):
        c = _by_id()['AIRLINER_EMER_ELEC_RAT_COVER']
        self.assertEqual('cover', c['kind'])
        self.assertEqual('AIRLINER_EMER_ELEC_RAT_Cover_Set', c['event'])

    def test_a_push_that_writes_a_constant_is_a_button(self):
        c = _by_id()['AIRLINER_APU_START']
        self.assertEqual(('button', 1), (c['kind'], c['press']))

    def test_a_key_whose_click_code_flips_an_l_var_is_a_two_position_selector_on_it(self):
        c = _by_id()['INSTRUMENT_FCU_HUD_DECLUTTER_LEFT_IE_ID']
        self.assertEqual(('selector', 'L:INI_FCU_HUD_DECLUTTER_LEFT', {'0': 'OFF', '1': 'ON'}, None),
                         (c['kind'], c['state_var'], c['positions'], c['press']))

    def test_a_tooltip_reading_its_own_bool_names_true_and_false(self):
        c = _by_id()['AIRLINER_GRAVITY_GEAR_COVER']
        self.assertEqual(('L:INI_GRAVITY_GEAR_COVER', {'0': 'OPEN', '1': 'CLOSE'}), (c['state_var'], c['positions']))

    def test_hold(self):
        self.assertEqual(('hold', 2), (_by_id()['AIRLINER_ECAM_ENG']['kind'], _by_id()['AIRLINER_ECAM_ENG']['press']))

    def test_encoder_names_its_display(self):
        c = _by_id()['AIRLINER_CABIN_PRESS_ALTITUDE_TARGET']
        self.assertEqual(('encoder', 'L:INI_CABIN_PRESS_ALTITUDE_TARGET'), (c['kind'], c['state_var']))

    def test_output_is_deterministic_and_names_the_a350(self):
        text = gen.to_json(_map())
        self.assertEqual(text, gen.to_json(_map()))
        data = json.loads(text)
        self.assertEqual(('inibuilds-aircraft-a350', '1.2.7', 'tools/a350-gen/generate_a350_map.py'),
                         (data['package'], data['package_version'], data['generator']))


if __name__ == '__main__':
    unittest.main()


class LampRuleTests(unittest.TestCase):
    def test_a_lamp_with_its_power_at_the_end(self):
        self.assertEqual(gen.lamp_rule('(L:INI_BATTERY_1_FAULT) 1 == (L:INI_ANNLT_SWITCH) 0 == + 1 min (L:INI_DC_LIGHTS_FAILURE) *'),
                         ('(L:INI_BATTERY_1_FAULT) 1 ==', 'DC'))

    def test_a_lamp_with_its_power_and_brightness_in_front(self):
        # The EFIS selectors' position markers: power x brightness first, then the state.
        code = ('(L:INI_DC_LIGHTS_FAILURE) 10 * (L:INI_FMS_SELECTOR) 0 == if{ -1 sp0 } els{ (L:INI_MAP_MODE_CAPT_SWITCH) sp0 } '
                'l0 0 == (L:INI_ANNLT_SWITCH) 0 == or (L:INI_GENERAL_LIGHT_MULTIPLIER) * *')
        self.assertEqual(gen.lamp_rule(code),
                         ('(L:INI_FMS_SELECTOR) 0 == if{ -1 sp0 } els{ (L:INI_MAP_MODE_CAPT_SWITCH) sp0 } l0 0 ==', 'DC'))


class KeyEventLeverTests(unittest.TestCase):
    def test_a_lever_writing_key_events_is_a_selector_on_the_l_var_its_init_reads(self):
        c = _by_id()['AIRLINER_LDG_LEVER']
        self.assertEqual('selector', c['kind'])
        self.assertEqual('L:INI_GEAR_HANDLE_STATUS', c['state_var'])
        self.assertEqual({'0': 'UP', '1': 'DOWN'}, dict(c['positions']))
