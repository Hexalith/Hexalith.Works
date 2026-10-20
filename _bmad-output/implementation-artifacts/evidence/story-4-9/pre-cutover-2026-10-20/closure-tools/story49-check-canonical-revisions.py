import datetime
import hashlib
import json
from pathlib import Path
import subprocess
import sys

artifact = Path('/tmp/story49-pre-cutover-20261020-isolated')
ledger = json.loads((artifact / 'ledger.json').read_text())
paths = ('src', 'tests', 'eng', 'DaprComponents', 'config', 'schemas', '.config', 'apphost.cs', 'global.json', 'Directory.Build.props',
         'Directory.Build.targets', 'Directory.Packages.props', 'Directory.Solution.targets', '.editorconfig')
roots = {'works': Path('/home/administrator/projects/hexalith/works'), 'platform': Path('/home/administrator/projects/hexalith/platform')}
expected = {'works': '696781961155d764ed805843f55b40dc5882e887', 'platform': '12c2952a5156f5841c6f9b0dbf3f4125a69d0baf'}
def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def vcs(root, *arguments):
    return subprocess.run(['git', '-C', str(root), *arguments], check=True, capture_output=True, timeout=30).stdout
def snapshot(root):
    names = sorted(set(vcs(root, 'ls-files', '-z', '--cached', '--others', '--exclude-standard', '--', *paths).decode().strip('\0').split('\0')))
    root_names = [name for name in vcs(root, 'ls-files', '-z', '--cached', '--others', '--exclude-standard').decode().strip('\0').split('\0') if name and len(Path(name).parts) == 1]
    names = sorted(set(names) | set(root_names))
    inventory = {name: digest(root / name) for name in names if name and (root / name).is_file()}
    return {'head': vcs(root, 'rev-parse', 'HEAD').decode().strip(), 'sourceFiles': inventory,
            'inventorySha256': hashlib.sha256(json.dumps(inventory, sort_keys=True).encode()).hexdigest()}
observed = {name: snapshot(root) for name, root in roots.items()}
head_changes = {}
for name in roots:
    if observed[name]['head'] != expected[name]:
        changed_names = vcs(roots[name], 'diff', '--name-only', expected[name], observed[name]['head']).decode().splitlines()
        if any(not path.startswith('_bmad-output/') for path in changed_names):
            raise ValueError('Canonical ' + name + ' revision changed inputs outside documentation/evidence')
        head_changes[name] = {'from': expected[name], 'to': observed[name]['head'], 'committedChangedPaths': changed_names,
                              'classification': 'documentation/evidence only; complete ordinary input inventory must still be identical'}
catalog = ledger['selectedSdk']
if digest(catalog['catalog']) != catalog['catalogSha256']:
    raise ValueError('The selected producer catalog changed during the gate')
for path, value in ledger['platformConsumerSources'].items():
    if observed['platform']['sourceFiles'].get(path) != value:
        raise ValueError('Initial Platform source drift: ' + path)
for path, value in ledger['fixtureSources'].items():
    if observed['works']['sourceFiles'].get(path) != value:
        raise ValueError('Initial Works fixture drift: ' + path)
for path, value in ledger['worksConsumerSource']['sourceFiles'].items():
    if observed['works']['sourceFiles'].get(path) != value:
        raise ValueError('Initial Works consumer source drift: ' + path)
for name, value in ledger['verifierHelpers'].items():
    if observed['platform']['sourceFiles'].get('eng/' + name) != value:
        raise ValueError('Initial verifier helper drift: ' + name)
if observed['platform']['sourceFiles']['eng/verify_works_host.py'] != ledger['verifierSha256']:
    raise ValueError('Initial verifier drift')
record = artifact / 'canonical-revisions-complete.json'
if record.exists():
    previous = json.loads(record.read_text())
    changed = {name: sorted(key for key in set(previous['repositories'][name]['sourceFiles']) | set(observed[name]['sourceFiles'])
                            if previous['repositories'][name]['sourceFiles'].get(key) != observed[name]['sourceFiles'].get(key)) for name in roots}
    previous['finalObservation'] = {'observedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                                    'heads': {name: value['head'] for name, value in observed.items()}, 'sourceDrift': changed,
                                    'documentationOnlyHeadAdvances': head_changes, 'selectedCatalogUnchanged': True}
    previous['unchanged'] = not any(changed.values())
    record.write_text(json.dumps(previous, indent=2) + '\n')
    if not previous['unchanged']:
        raise ValueError('Relevant ordinary source, test or eng inputs changed: ' + str(changed))
else:
    record.write_text(json.dumps({'observedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'scope': 'All tracked and untracked nonignored src, tests, eng, DaprComponents, config, schemas, .config and top-level files; includes shim/verifier, linked root C# and production policies',
        'launchObservedHeadsMatchedDirectVcs': True, 'gateInputInventoriesMatched': True, 'repositories': observed}, indent=2) + '\n')
print({name: {'head': value['head'], 'files': len(value['sourceFiles']), 'inventorySha256': value['inventorySha256']} for name, value in observed.items()})
