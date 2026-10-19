"""Run Works deterministic test binaries for one configuration and record exact results."""
import json, subprocess, sys, time, hashlib
from pathlib import Path
import xml.etree.ElementTree as ET
W = Path('/home/administrator/projects/hexalith/works'); config = sys.argv[1]; out = Path(sys.argv[2]); out.mkdir(parents=True, exist_ok=True)
results = []
for name in ['UnitTests', 'PropertyTests', 'ArchitectureTests', 'IntegrationTests']:
    exe = W / f'tests/Hexalith.Works.{name}/bin/{config}/net10.0/Hexalith.Works.{name}'
    xml = out / f'{config.lower()}-{name}.xml'
    cmd = [str(exe), '-noLogo', '-result-xml', str(xml)]
    if name == 'IntegrationTests': cmd += ['-class-', '*SmokeTests', '-class-', '*Platform*']
    started = time.time()
    with (out / f'{config.lower()}-{name}.log').open('w') as log:
        code = subprocess.run(cmd, cwd=W, stdout=log, stderr=subprocess.STDOUT).returncode
    a = ET.parse(xml).getroot().find('assembly') if xml.exists() else None
    results.append({'suite': name, 'configuration': config, 'command': [str(c) for c in cmd], 'exitCode': code, 'seconds': round(time.time() - started, 1),
                    'tests': {k: a.get(k) for k in ('total', 'passed', 'failed', 'errors', 'skipped', 'not-run')} if a is not None else None,
                    'xmlSha256': hashlib.sha256(xml.read_bytes()).hexdigest() if xml.exists() else None})
    (out / f'{config.lower()}-results.json').write_text(json.dumps(results, indent=2) + '\n')
    print(json.dumps(results[-1]['tests']), name, code, flush=True)
