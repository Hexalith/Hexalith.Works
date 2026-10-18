#!/usr/bin/env python3
"""Check retained final-review results and optionally bind current implementation sources."""
import gzip
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

packet = Path(__file__).resolve().parent
report = json.loads((packet / 'report.json').read_text())


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def read(name):
    return gzip.decompress((packet / name).read_bytes())


for item in report['artifacts']:
    packed = (packet / item['path']).read_bytes()
    require(hashlib.sha256(packed).hexdigest() == item['sha256'], item['path'])
    require(hashlib.sha256(gzip.decompress(packed)).hexdigest() == item['uncompressedSha256'], item['path'])
require(len(report['commands']) == 4, 'command inventory')
for command in report['commands']:
    require(command['exitCode'] == 0, command['name'])
    if 'xml' not in command:
        log = read(command['log']).decode()
        require('Build succeeded.' in log and '0 Warning(s)' in log and '0 Error(s)' in log, command['name'])
        continue
    tree = ET.fromstring(read(command['xml']))
    assembly = tree.find('assembly')
    require(assembly is not None, command['name'])
    require(all(int(assembly.get(field, '-1')) == 0 for field in ('failed', 'errors', 'skipped', 'not-run')), command['name'])
    require(int(assembly.get('total')) == int(assembly.get('passed')) == 865, command['name'])
    mode = 'debug' if command['name'] == 'debug-integration' else 'release'
    names = {test.get('name').replace('\\', ''): test.get('result') for test in tree.iter('test')}
    for row in report['matrix']:
        if row['mode'] == mode:
            require(len(row['cases']) == row['passingCases'], row['scenario'])
            require(all(names.get(name.replace('\\', '')) == 'Pass' for name in row['cases']), row['scenario'])
    for method, count in [('WorksSdkIteration4Tests.RequiredValueMemberPresenceDoesNotRejectExplicitDefaults', 16),
                          ('WorksSdkProcessRecoveryTests.TerminalSourceMustMatchItsContextualReread', 4)]:
        values = [value for name, value in names.items() if name.startswith('Hexalith.Works.IntegrationTests.' + method + '(')]
        require(len(values) == count and all(value == 'Pass' for value in values), method)
for name, item in report['sources'].items():
    require(hashlib.sha256(read(item['snapshot'])).hexdigest() == item['sha256'], name)
    if '--current' in sys.argv:
        workspace = next(parent for parent in packet.parents if (parent / 'Hexalith.Works.slnx').is_file())
        require(hashlib.sha256((workspace / name).read_bytes()).hexdigest() == item['sha256'], 'current ' + name)
require(report['matrixObservations'] == sum(row['passingCases'] for row in report['matrix']) == 130, 'matrix observations')
require(report['currentSourceLiveRerun'] is False and report['candidateRepacked'] is False, 'evidence scope')
print('Verified 865 passes in each mode, 130 matrix observations, 40 review regression observations, retained hashes and declared runtime scope.')
