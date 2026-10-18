#!/usr/bin/env python3
"""Validate the final reviewed snapshot and its earlier producer proof without live services."""
import ast
import gzip
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
PRIOR = HERE.parent
prior = subprocess.run([sys.executable, str(PRIOR / 'verify-evidence.py')], capture_output=True, text=True)
assert prior.returncode == 0, prior.stderr
prior_result = json.loads(prior.stdout)
raw = {}
for item in json.loads((HERE / 'raw-artifacts.json').read_text()):
    compressed = (HERE / item['path']).read_bytes()
    assert hashlib.sha256(compressed).hexdigest() == item['compressedSha256'], item['path']
    value = gzip.decompress(compressed)
    assert len(value) == item['bytes'] and hashlib.sha256(value).hexdigest() == item['sha256'], item['path']
    raw[Path(item['path']).name.removesuffix('.gz')] = value
report = json.loads((HERE / 'report.json').read_text())
results = json.loads(raw['results.json'])
assert results == report['results'] and len(results) == 10
for result in results:
    assert result['exit'] == 0, result['label']
    if 'xml' not in result:
        assert b'0 Warning(s)' in raw[result['log']] and b'0 Error(s)' in raw[result['log']]
        continue
    assembly = ET.fromstring(raw[result['xml']]).find('assembly')
    for field in ('failed', 'errors', 'skipped', 'not-run'):
        assert int(assembly.get(field)) == 0, (result['label'], field)
    assert assembly.get('passed') == result['tests']['passed']
    if result['label'] in ('debug-integration', 'candidate-integration'):
        assert assembly.get('passed') == '848'
# Share the reviewed phase assertions without running the earlier packet's main body twice.
tree = ast.parse((PRIOR / 'verify-evidence.py').read_text())
nodes = [node for node in tree.body if isinstance(node, (ast.Import, ast.ImportFrom, ast.FunctionDef))
         or isinstance(node, ast.Assign) and any(isinstance(name, ast.Name) and name.id == 'EXPECTED_PHASES' for name in node.targets)]
namespace = {}
exec(compile(ast.Module(body=nodes, type_ignores=[]), '<reviewed semantic assertions>', 'exec'), namespace)
for label in ('debug-restart', 'candidate-restart'):
    namespace['verify_restart']([json.loads(line) for line in raw[label + '-runtime.jsonl'].splitlines()])
callback = json.loads(raw['debug-callback-runtime.jsonl'].splitlines()[0])['state']
assert callback['processCandidate'] and len(callback['witness']['entries']) == 1
assert callback['witness']['entries'][0]['status'] == 'Armed'
producers = json.loads((PRIOR / 'producers.json').read_text())
assembly_hashes = {Path(name).name: digest for archive in producers['archives'] for name, digest in archive['assemblies'].items()}
bindings = json.loads(raw['candidate-restart-bindings.json'])
assert len(bindings) == 8
assert sum(host['resource'] == 'works' for host in bindings) == 4
assert sum(host['resource'] == 'eventstore' for host in bindings) == 4
packaged_checks = 0
for host in bindings:
    assert next(value for name, value in host['assemblies'].items() if Path(name).name == 'Hexalith.EventStore.Client.dll') == assembly_hashes['Hexalith.EventStore.Client.dll']
    if host['resource'] == 'works':
        assert len(host['assemblies']) == 5
        for name, digest in host['assemblies'].items():
            assert assembly_hashes[Path(name).name] == digest, name
            packaged_checks += 1
assert packaged_checks == report['packagedHostDllChecks'] == 20
for index, (name, digest) in enumerate(sorted(report['sourceSha256'].items())):
    assert hashlib.sha256(raw['source-' + str(index) + '-' + Path(name).name]).hexdigest() == digest, name
assert len(report['sourceSha256']) == report['sourceSnapshots'] == 16
assert report['matrixObservations'] == sum(row['passingCases'] for row in report['matrix']) == 130
for mode, filename in [('debug', 'debug-integration.xml'), ('release', 'candidate-integration.xml')]:
    names = {test.get('name').replace('\\', ''): test.get('result') for test in ET.fromstring(raw[filename]).iter('test')}
    for row in report['matrix']:
        if row['mode'] == mode:
            assert len(row['cases']) == row['passingCases']
            assert all(names.get(case.replace('\\', '')) == 'Pass' for case in row['cases'])
    for method, expected in [('WorksSdkIteration4Tests.RequiredValueMemberPresenceDoesNotRejectExplicitDefaults', 4),
                             ('WorksSdkIntentTranslationTests.RejectedAssignmentRetainsExpiryOnlyWhenRequeued', 2)]:
        prefix = 'Hexalith.Works.IntegrationTests.' + method
        matched = [result for name, result in names.items() if name.startswith(prefix + '(')]
        assert len(matched) == expected and all(result == 'Pass' for result in matched), method

cleanup = json.loads(raw['cleanup.json'])
assert cleanup == report['cleanup'] and cleanup['watchLimit'] == '1048576'
assert json.loads(cleanup['aspirePs']) == []
assert not any('story415' in name or name.startswith('works-') for name in cleanup['runningContainers'])
assert not report['published'] and not report['defaultReleaseContainsCorrection'] and report['defaultRelease'] == '3.124.0'
print(json.dumps({'finalReports': 8, 'integrationPerMode': 848, 'restartPhasesAsserted': 20,
                  'packagedHostDllChecks': packaged_checks, 'sourceSnapshots': 16,
                  'priorArtifactsVerified': prior_result['verifiedArtifacts'],
                  'defaultRelease': 'unfixed public 3.124.0'}, indent=2))
