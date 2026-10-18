"""Verify final deterministic results and optional current-checkout bindings."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def verify(current):
    packet = Path(__file__).resolve().parent
    report = json.loads((packet / 'report.json').read_text())
    raw = {}
    for artifact in report['artifacts']:
        packed = (packet / artifact['path']).read_bytes()
        require(digest(packed) == artifact['sha256'], artifact['path'])
        data = gzip.decompress(packed)
        require(digest(data) == artifact['originalSha256'], artifact['path'])
        raw[artifact['path']] = data
    expected = {'root-debug-integration': 893, 'root-release-integration-retry': 893,
                'architecture': 269, 'unit': 589, 'property': 3, 'review-expiry-focused-final': 14}
    names = {}
    labels = {command['label'] for command in report['commands']}
    require(labels == set(expected) | {'root-debug-build', 'root-release-build', 'review-expiry-build-final'}, 'Command inventory')
    for command in report['commands']:
        label = command['label']
        require(command['exitCode'] == 0, label)
        if 'xml' not in command:
            log = raw[command['log']].decode()
            require('\n    0 Warning(s)\n' in log and '\n    0 Error(s)\n' in log, label)
            continue
        tree = ET.fromstring(raw[command['xml']])
        assembly = tree.find('assembly')
        stats = {key: int(assembly.get(key)) for key in command['tests']}
        require(stats == command['tests'], label)
        require(stats['total'] == stats['passed'] == expected[label], label)
        require(all(stats[key] == 0 for key in ['failed', 'errors', 'skipped', 'not-run']), label)
        names[label] = {test.get('name').replace('\\', ''): test.get('result') for test in tree.iter('test')}
        require(len(names[label]) == expected[label] and set(names[label].values()) == {'Pass'}, label)
    for mode in ['debug', 'release']:
        label = 'root-debug-integration' if mode == 'debug' else 'root-release-integration-retry'
        for method, count in [('RequiredValueMemberPresenceDoesNotRejectExplicitDefaults', 36),
                              ('RepeatedCreationRefusesContextAndQuarantinesWithoutCheckpointAdvance', 4),
                              ('FirstCreationAndRescheduleRemainValidAfterOptionalCreationRejection', 2),
                              ('TerminalSourceMustMatchItsContextualReread', 4)]:
            require(sum(method + '(' in name for name in names[label]) == count, mode + ': ' + method)
        dependency = report['dependencies'][mode]
        actual = {name: value['type'] for name, value in json.loads(raw[dependency['artifact']])['libraries'].items()
                  if name.startswith('Hexalith.EventStore.')}
        require(actual == dependency['libraries'] and len(actual) == 9, mode)
        require(set(actual.values()) == ({'project'} if mode == 'debug' else {'package'}), mode)
    observations = 0
    for row in report['matrix']:
        label = 'root-debug-integration' if row['mode'] == 'debug' else 'root-release-integration-retry'
        require(row['passingCases'] == len(row['cases']), row['scenario'])
        for name in row['cases']:
            require(names[label].get(name.replace('\\', '')) == 'Pass', name)
            observations += 1
    require(observations == report['matrixObservations'] == 130, 'Matrix observations')
    require(len(report['candidatePackages']) == 9, 'Candidate inventory')
    for package in report['candidatePackages']:
        require(package['archiveAssemblySha256'] == package['consumerAssemblySha256'], package['archive'])
    archive = report['archivePush']
    require(archive['target'] == '575c8070355ff9b6afb17521d6eb529a85327e4d', 'Archive target')
    require(archive['remoteVerified'].split()[0] == archive['target'] and archive['action']['exitCode'] == 0, 'Archive push')
    require(report['sdkHead'] == 'f43b121c6c7539487b1f38b3c28508e3ab6264cc', 'SDK producer')
    require(not report['currentSourceLiveRerun'] and not report['candidateRepacked'], 'Runtime scope')
    for name, snapshot in report['snapshots'].items():
        require(digest(raw[snapshot]) == report['sources'][name], name)
    if current:
        root = next(parent for parent in packet.parents if (parent / 'Hexalith.Works.slnx').is_file())
        for name, expected_hash in report['sources'].items():
            require(digest((root / name).read_bytes()) == expected_hash, name)
        sdk = root / 'references/Hexalith.EventStore'
        require(subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=sdk, text=True).strip() == report['sdkHead'], 'Current SDK')
        for package in report['candidatePackages']:
            require(digest((root / package['archive']).read_bytes()) == package['sha256'], package['archive'])
        for mode, configuration in [('debug', 'Debug'), ('release', 'Release')]:
            folder = root / 'tests/Hexalith.Works.IntegrationTests/bin' / configuration / 'net10.0'
            for name, expected_hash in report['binaryBindings'][mode].items():
                require(digest((folder / name).read_bytes()) == expected_hash, configuration + '/' + name)
    print('Verified 893 passes per integration mode, 130 matrix observations, repeated-creation regressions, source/archive bindings and pushed archive tag.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--current', action='store_true')
    verify(parser.parse_args().current)
