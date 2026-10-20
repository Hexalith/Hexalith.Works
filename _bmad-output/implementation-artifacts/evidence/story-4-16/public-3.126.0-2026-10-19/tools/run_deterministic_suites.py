"""Run each built Works test project separately and retain exact command/result/XML evidence.
Usage: run_deterministic_suites.py <Debug|Release> <artifacts-root> <results-root> [architecture-artifacts-root]
"""
import hashlib, json, subprocess, sys, time
from pathlib import Path
import xml.etree.ElementTree as ET
configuration, artifacts, output = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
architecture = Path(sys.argv[4]) if len(sys.argv) > 4 else artifacts
output.mkdir(parents=True, exist_ok=False)
results = []
for suite in (sys.argv[5].split(',') if len(sys.argv) > 5 else ['UnitTests', 'PropertyTests', 'ArchitectureTests', 'IntegrationTests']):
    root = architecture if suite == 'ArchitectureTests' else artifacts
    name = 'Hexalith.Works.' + suite
    executable = root / 'bin' / name / configuration.lower() / name
    xml = output / (suite + '.xml')
    command = [str(executable), '-noLogo', '-result-xml', str(xml)]
    if suite == 'IntegrationTests': command += ['-class-', '*SmokeTests', '-class-', '*Platform*']
    started = time.time()
    with (output / (suite + '.log')).open('w') as log:
        code = subprocess.run(command, cwd='/home/administrator/projects/hexalith/works', stdout=log, stderr=subprocess.STDOUT).returncode
    assembly = ET.parse(xml).getroot().find('assembly') if xml.is_file() else None
    entry = {'suite': suite, 'configuration': configuration, 'command': command, 'exitCode': code,
             'seconds': round(time.time() - started, 3),
             'tests': {key: assembly.get(key, '0') for key in ['total', 'passed', 'failed', 'errors', 'skipped', 'not-run']} if assembly is not None else None,
             'xmlSha256': hashlib.sha256(xml.read_bytes()).hexdigest() if xml.is_file() else None}
    results.append(entry)
    (output / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
    print(json.dumps(entry), flush=True)
    counts = entry['tests'] or {}
    if code or counts.get('total') != counts.get('passed') or not int(counts.get('total', '0')) or any(counts.get(key, '0') != '0' for key in ['failed', 'errors', 'skipped', 'not-run']):
        raise SystemExit(1)
