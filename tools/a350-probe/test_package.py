"""package.find: the A350 package as MSFS Blind Assist finds it (MsfsPackagesLocator), so the scripts run on any PC (plan 20)."""
import os
import tempfile
import unittest

import package


def _cfg(path, *lines):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines) + '\n')


class FindTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = self.tmp.name
        self.env = {'APPDATA': os.path.join(self.root, 'Roaming'), 'LOCALAPPDATA': os.path.join(self.root, 'Local')}

    def tearDown(self):
        self.tmp.cleanup()

    def _package_under(self, packages_root, community='Community'):
        p = os.path.join(packages_root, community, package.NAME)
        os.makedirs(p)
        return p

    def test_the_environment_wins(self):
        self.assertEqual('X:\\mine', package.find(dict(self.env, A350_PACKAGE='X:\\mine')))

    def test_the_roaming_config_names_the_packages_folder(self):
        root = os.path.join(self.root, 'D', 'MSFS')
        want = self._package_under(root)
        _cfg(os.path.join(self.env['APPDATA'], 'Microsoft Flight Simulator 2024', 'UserCfg.opt'),
             'Version 1', f'InstalledPackagesPath "{root}"')
        self.assertEqual(want, package.find(self.env))

    def test_the_store_config_when_no_roaming_one(self):
        root = os.path.join(self.root, 'Store', 'Packages')
        want = self._package_under(root)
        _cfg(os.path.join(self.env['LOCALAPPDATA'], 'Packages', 'Microsoft.Limitless_8wekyb3d8bbwe', 'LocalCache', 'UserCfg.opt'),
             f'InstalledPackagesPath "{root}"')
        self.assertEqual(want, package.find(self.env))

    def test_community2024_is_looked_in_too(self):
        root = os.path.join(self.root, 'P')
        want = self._package_under(root, 'Community2024')
        _cfg(os.path.join(self.env['APPDATA'], 'Microsoft Flight Simulator 2024', 'UserCfg.opt'), f'InstalledPackagesPath "{root}"')
        self.assertEqual(want, package.find(self.env))

    def test_the_next_boot_path_is_not_the_running_sims(self):
        running, next_boot = os.path.join(self.root, 'Now'), os.path.join(self.root, 'Later')
        want = self._package_under(running)
        self._package_under(next_boot)
        _cfg(os.path.join(self.env['APPDATA'], 'Microsoft Flight Simulator 2024', 'UserCfg.opt'),
             f'InstalledPackagesPathNextBoot "{next_boot}"', f'InstalledPackagesPath "{running}"')
        self.assertEqual(want, package.find(self.env))

    def test_nothing_found_is_none(self):
        self.assertIsNone(package.find(self.env))


if __name__ == '__main__':
    unittest.main()
