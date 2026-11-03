"""Where the iniBuilds A350 package is, for this folder's scripts: A350_PACKAGE when set, else the simulator's own UserCfg.opt
(its InstalledPackagesPath, never the NextBoot one: the Roaming config first, then the Store build's) and the A350 in its
Community or Community2024 folder, as MSFS Blind Assist finds it (MsfsPackagesLocator). Read-only."""
import os

NAME = 'inibuilds-aircraft-a350'


def _installed_packages_paths(text):
    for line in text.splitlines():
        key = line.find('InstalledPackagesPath')
        if key < 0 or line.startswith('InstalledPackagesPathNextBoot', key):
            continue
        start = line.find('"', key)
        end = line.find('"', start + 1) if start >= 0 else -1
        if end > start + 1:
            yield line[start + 1:end]


def find(environ=os.environ):
    """The package folder, or None when neither A350_PACKAGE nor the sim's config leads to one."""
    if environ.get('A350_PACKAGE'):
        return environ['A350_PACKAGE']
    configs = (os.path.join(environ.get('APPDATA', ''), 'Microsoft Flight Simulator 2024', 'UserCfg.opt'),
               os.path.join(environ.get('LOCALAPPDATA', ''), 'Packages', 'Microsoft.Limitless_8wekyb3d8bbwe', 'LocalCache',
                            'UserCfg.opt'))
    for config in configs:
        try:
            with open(config, encoding='utf-8', errors='replace') as f:
                text = f.read()
        except OSError:
            continue
        for root in _installed_packages_paths(text):
            for community in ('Community', 'Community2024'):
                folder = os.path.join(root, community, NAME)
                if os.path.isdir(folder):
                    return folder
    return None
