import datetime
import hashlib
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

works = Path('/home/administrator/projects/hexalith/works')
artifact = Path('/tmp/story49-pre-cutover-20261020-isolated')
ledger = json.loads((artifact / 'ledger.json').read_text())
parties = works / 'references/Hexalith.Parties'
identity = works / 'references/Hexalith.Platform/src/Hexalith.Platform.Identity'
files = set(identity.rglob('*.cs')) | {identity / 'Hexalith.Platform.Identity.csproj'}
for element in ET.parse(works / 'tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj').getroot().iter('Compile'):
    include = element.get('Include', '')
    if include.startswith('$(HexalithPartiesRoot)/'):
        files.add(parties / include.removeprefix('$(HexalithPartiesRoot)/'))
files = {path for path in files if not {'obj', 'bin'}.intersection(path.parts)}
observed = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(files)}
project = str(identity / 'Hexalith.Platform.Identity.csproj')
if observed[project] != ledger['identityProvenance']['projectSha256']:
    raise ValueError('The explicit test-only Platform.Identity project changed from the initial gate input')
heads = {str(root): subprocess.run(['git', '-C', str(root), 'rev-parse', 'HEAD'], check=True,
                                  capture_output=True, text=True, timeout=30).stdout.strip()
         for root in (parties, identity.parents[1])}
record = artifact / 'linked-test-fixtures.json'
if record.exists():
    previous = json.loads(record.read_text())
    previous['finalObservation'] = {'observedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'heads': heads,
                                    'unchanged': previous['sourceFiles'] == observed and previous['heads'] == heads}
    record.write_text(json.dumps(previous, indent=2) + '\n')
    if not previous['finalObservation']['unchanged']:
        raise ValueError('Explicit linked test fixture source or revision changed')
else:
    record.write_text(json.dumps({'observedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'scope': 'Exact existing test-only Parties linked Compile sources and root-declared Platform.Identity source graph; no production authority',
        'heads': heads, 'sourceFiles': observed}, indent=2) + '\n')
print({'linkedTestFixtureFiles': len(observed), 'heads': heads})
