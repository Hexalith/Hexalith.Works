#!/usr/bin/env python3
"""Verify the portable retained Story 4.15 correction evidence, without /tmp or NuGet."""
import gzip
import hashlib
import json
import base64
from datetime import datetime
from pathlib import Path
import xml.etree.ElementTree as ET

BASE = Path(__file__).resolve().parent
manifest = json.loads((BASE / 'raw-artifacts.json').read_text())
data = {}
for item in manifest:
    archive = (BASE / item['path']).read_bytes()
    assert hashlib.sha256(archive).hexdigest() == item['compressedSha256'], item['path']
    value = gzip.decompress(archive)
    assert len(value) == item['bytes'], item['path']
    assert hashlib.sha256(value).hexdigest() == item['sha256'], item['path']
    data[Path(item['path']).name.removesuffix('.gz')] = value

checks = {
    'checks--story415-20261018-sdk-all-accepted.xml': 1608,
    'checks--story415-20261018-debug-integration-accepted.xml': 842,
    'checks--story415-20261018-candidate-release-integration-bound.xml': 842,
    'checks--story415-20261018-default-release-package-contract.xml': 2,
    'checks--story415-20261018-UnitTests.xml': 589,
    'checks--story415-20261018-architecture-accepted.xml': 269,
    'checks--story415-20261018-PropertyTests.xml': 3,
    'source-restart-pass--restart.xml': 1,
    'candidate-restart-pass--restart.xml': 1,
    'source-callback-pass--callback.xml': 1,
    'review-checks--focused.xml': 76,
}
for name, total in checks.items():
    assembly = ET.fromstring(data[name]).find('assembly')
    assert assembly is not None
    assert int(assembly.attrib['total']) == total, name
    for attr in ('errors', 'failed', 'skipped', 'not-run'):
        assert int(assembly.attrib[attr]) == 0, (name, attr)
EXPECTED_PHASES = {
    'rescheduled-expiry-committed-current-witness',
    'cascade-expiry-target-commit-before-checkpoint',
    'cascade-expiry-restart-receipt-and-checkpoint',
    'canonical-related-child-creation-gap',
    'cascade-target-commit-before-checkpoint',
    'five-child-persisted-target-receipts',
    'cascade-restart-receipt-and-checkpoint',
    'child-resume-noop-receipt-before-checkpoint',
    'complete-supported-process-coverage',
    'child-resume-noop-restart-receipt-and-checkpoint',
}


def checkpoint(value, position, reason=None):
    assert value['Checkpoint'] == position
    assert value['Source']['Domain'] == 'work'
    assert value['Status'] == (2 if reason else 0)
    assert value['ReasonCode'] == reason
    assert value['FailureSequence'] == (position + 1 if reason else None)
    assert value['ObservedUnresolved'] is bool(reason)
    if reason is None:
        assert value.get('PendingPlan') is None
        assert value['Attempts'] == 0


def plan(value, position, kind, command, count):
    checkpoint(value, position - 1, 'effect-unknown')
    retained = value['PendingPlan']
    assert retained['Sequence'] == position
    assert len(retained['EvidenceDigest']) == 64
    effects = retained['Effects']
    assert len(effects) == count
    assert len({effect['TargetAggregate'] for effect in effects}) == count
    for effect in effects:
        assert effect['Kind'] == kind and effect['CommandType'] == command
        assert effect['TargetDomain'] == 'work' and effect['Ordinal'] == 0
        payload = json.loads(base64.b64decode(effect['CommandPayload']))
        assert payload['TenantId']['Value'] == value['Source']['Tenant']
        assert payload['WorkItemId']['Value'] == effect['TargetAggregate']
    return effects


def receipt(value, effect_id, disposition, source, sequence, effect):
    assert value['effectId'] == effect_id and value['disposition'] == disposition
    assert value['workload'] == 'works' and value['semanticDigest'] and value['causationId']
    assert value['purpose'] == effect['Kind'].removesuffix('.v1')
    assert value['identity'] == {
        'tenant': source['Tenant'], 'sourceDomain': 'work', 'sourceAggregate': source['Aggregate'],
        'sourceEnvelopeSequence': sequence, 'effectKind': effect['Kind'], 'targetDomain': 'work',
        'targetAggregate': effect['TargetAggregate'], 'ordinal': 0,
    }


def replay(value, retained_receipt, effect):
    assert value['ReplayObserved'] is True and value['FailNext'] is False
    assert value['CommittedEffectId'] == retained_receipt['effectId']
    assert value['Disposition'] == retained_receipt['disposition']
    assert value['CausationId'] == retained_receipt['causationId']
    submission = value['Submission']
    assert {key[0].lower() + key[1:]: entry for key, entry in submission['Identity'].items()} == retained_receipt['identity']
    assert submission['CommandType'] == effect['CommandType']
    assert submission['CommandPayload'] == effect['CommandPayload']
    assert submission['MessageId'] == submission['IdempotencyKey'] == 'wrk-' + retained_receipt['effectId']


def verify_restart(records):
    assert len(records) == len(EXPECTED_PHASES)
    states = {record['phase']: record['state'] for record in records}
    assert set(states) == EXPECTED_PHASES
    witness = states['rescheduled-expiry-committed-current-witness']
    assert witness['Sequence'] == 4 and witness['ExpiryWitness']['Revision'] == 2
    before = states['cascade-expiry-target-commit-before-checkpoint']
    effects = plan(before['checkpoint'], 4, 'works.cascade-expire.v1', 'ExpireWorkItem', 1)
    assert witness['AggregateId'] == witness['WorkItemId']['Value'] == before['checkpoint']['Source']['Aggregate']
    assert witness['TenantId']['Value'] == before['checkpoint']['Source']['Tenant']
    assert before['childExpired'] == 1
    assert before['childScheduledExpiry']['Revision'] == 1
    assert datetime.fromisoformat(before['childScheduledExpiry']['EffectiveExpiryUtc']) > datetime.fromisoformat(witness['ExpiryWitness']['EffectiveExpiryUtc'])
    assert 'ExpiryWitness' not in json.loads(base64.b64decode(effects[0]['CommandPayload']))
    after = states['cascade-expiry-restart-receipt-and-checkpoint']
    assert after['expiryCascadeEffect'] == before['expiryCascadeEffect']
    assert after['childScheduledExpiry'] == before['childScheduledExpiry'] and after['childExpired'] == 1
    checkpoint(after['expiryCheckpoint'], 4)
    assert after['expiryCheckpoint']['Source'] == before['checkpoint']['Source']
    receipt(after['expiryReceipt'], before['expiryCascadeEffect'], 0, before['checkpoint']['Source'], 4, effects[0])
    replay(after['replay'], after['expiryReceipt'], effects[0])

    gap = states['canonical-related-child-creation-gap']
    checkpoint(gap['creationGap'], 6, 'evidence-unavailable')
    assert gap['creationGap'].get('PendingPlan') is None
    assert gap['existingChildCancellations'] == 0 and gap['readiness'] == 'Degraded'
    assert gap['gapSweep']['Unresolved'] >= 1 and gap['gapSweep']['Reasons']['evidence-unavailable'] >= 1

    before = states['cascade-target-commit-before-checkpoint']
    effects = plan(before['checkpoint'], 7, 'works.cascade-cancel.v1', 'CancelWorkItem', 5)
    assert before['checkpoint']['Source'] == gap['creationGap']['Source'] and before['childCancelled'] == 1
    children = states['five-child-persisted-target-receipts']
    assert len(children) == 5 and len({child['effectId'] for child in children}) == 5
    assert {child['target'] for child in children} == {effect['TargetAggregate'] for effect in effects}
    assert len({child['receipt']['causationId'] for child in children}) == 1
    for child in children:
        assert child['cancelled'] == 1
        effect = next(effect for effect in effects if effect['TargetAggregate'] == child['target'])
        receipt(child['receipt'], child['effectId'], 0, before['checkpoint']['Source'], 7, effect)
    after = states['cascade-restart-receipt-and-checkpoint']
    checkpoint(after['checkpoint'], 7)
    assert after['checkpoint']['Source'] == before['checkpoint']['Source']
    assert after['committedEffect'] == before['committedEffect']
    assert after['receipt'] == next(child['receipt'] for child in children if child['effectId'] == before['committedEffect'])

    before = states['child-resume-noop-receipt-before-checkpoint']
    effects = plan(before['checkpoint'], 4, 'works.child-completion-resume.v1', 'ResumeWorkItem', 1)
    assert before['parentResumed'] == 1
    assert before['lostNoOp']['CommittedEffectId'] == before['resumedEffect']
    assert before['lostNoOp']['Disposition'] == 2 and before['lostNoOp']['ReplayObserved'] is False
    after = states['child-resume-noop-restart-receipt-and-checkpoint']
    checkpoint(after['checkpoint'], 4)
    assert after['checkpoint']['Source'] == before['checkpoint']['Source']
    assert after['resumedEffect'] == before['resumedEffect']
    receipt(after['receipt'], before['resumedEffect'], 2, before['checkpoint']['Source'], 4, effects[0])
    replay(after['replay'], after['receipt'], effects[0])
    assert before['lostNoOp']['Submission'] == after['replay']['Submission']
    assert before['lostNoOp']['CausationId'] == after['receipt']['causationId']
    coverage = states['complete-supported-process-coverage']
    assert coverage['Complete'] is True and coverage['CatalogComplete'] is True and coverage['CandidatesComplete'] is True
    assert coverage['Unresolved'] == 0 and coverage['Reasons'] == {} and coverage['RetainedCandidates'] == 11
    assert coverage['CapacityExceeded'] is False
    for field in ('Pending', 'PendingObservations', 'PendingPublications', 'PendingTransitions'):
        assert coverage[field] == {}


for label in ('source-restart-pass', 'candidate-restart-pass'):
    verify_restart([json.loads(line) for line in data[label + '--runtime.jsonl'].splitlines()])
for label in ('diagnostic-original', 'candidate-startup-mismatch-fail'):
    assert json.loads(data[label + '--result.json'])['exit'] == 1

replay = json.loads(data['faithful-replay--results.json'])
original, fixed = (item['result'] for item in replay)
assert original['beforeHead'] == original['afterHead']
assert original['pendingBefore'] == original['pendingAfter'] == 6
assert fixed['beforeHead'] == original['beforeHead'] and fixed['afterHead'] != fixed['beforeHead']
assert fixed['pendingBefore'] == 6 and fixed['pendingAfter'] == 5
assert original['checkpoint'] == fixed['checkpoint']
assert original['checkpoint']['Position'] == 11 and original['checkpoint']['OwnerSequenceNumber'] == 5
assert original['receiptSha256'] == fixed['receiptSha256']

producers = json.loads((BASE / 'producers.json').read_text())
assert len(producers['archives']) == 9
packed = {Path(name).name: value for name, value in json.loads(data['provenance--pack-archives.json']).items()}
cold_records = json.loads(data['review-provenance--cold-cache-archive-hashes.json'])['archives']
assert len(cold_records) == 9
cold = {item['archiveName']: item['sha256'] for item in cold_records}
assert len(cold) == len(packed) == 9
assert cold == packed == {Path(item['path']).name: item['sha256'] for item in producers['archives']}
client = next(item for item in producers['archives'] if '/Hexalith.EventStore.Client.' in item['path'])
client_sha = next(value for name, value in client['assemblies'].items() if name.endswith('/Hexalith.EventStore.Client.dll'))
bindings = [json.loads(line) for line in data['provenance--runtime-bindings.jsonl'].splitlines()]
candidate = [item for item in bindings if any('/bin/Release/' in value['path'] for value in item['assemblies'])]
assert len(candidate) == 8
assert sum(item['resource'] == 'works' for item in candidate) == 4
assert sum(item['resource'] == 'eventstore' for item in candidate) == 4
for item in candidate:
    assert next(value['sha256'] for value in item['assemblies'] if value['path'].endswith('/Hexalith.EventStore.Client.dll')) == client_sha
assembly_hashes = {Path(name).name: value for archive in producers['archives'] for name, value in archive['assemblies'].items()}
assert len(assembly_hashes) == 9
# The transitional SDK server is built from projects. Retain that graph instead of
# relabeling its generated Server/Gateway/Admin assemblies as package artifacts.
server_graph = json.loads(data['review-provenance--source-eventstore.deps.json'])['libraries']
assert all(name.endswith('/' + producers['candidate']) for name in server_graph if name.startswith('Hexalith.EventStore'))
source_assemblies = {name.split('/')[0] + '.dll' for name, value in server_graph.items()
                     if name.startswith('Hexalith.EventStore') and value['type'] == 'project'}
assert len(source_assemblies) == 8
packaged_dll_checks = 0
source_comparisons = []
for item in candidate:
    names = {Path(assembly['path']).name for assembly in item['assemblies']}
    assert len(names) == len(item['assemblies'])
    if item['resource'] == 'eventstore':
        assert names == source_assemblies
    else:
        assert names == {'Hexalith.EventStore.' + name + '.dll' for name in
                         ('Client', 'Contracts', 'DomainService', 'ServiceDefaults', 'Server')}
    for assembly in item['assemblies']:
        name = Path(assembly['path']).name
        if item['resource'] == 'eventstore':
            assert name in source_assemblies, name
            if name in assembly_hashes:
                source_comparisons.append({'pid': item['pid'], 'assembly': name,
                                           'matchesArchive': assembly['sha256'] == assembly_hashes[name]})
        else:
            assert name in assembly_hashes, name
            assert assembly['sha256'] == assembly_hashes[name], (item['pid'], name)
            packaged_dll_checks += 1
assert packaged_dll_checks == 20
assert producers['defaultRelease'] == '3.124.0' and not producers['defaultReleaseContainsCorrection']
default = json.loads(data['provenance--default-release-bindings.json'])
assert all(name.endswith('/3.124.0') for name in default['defaultReleaseSdkLibraries'])
assert default['clientDllSha256'] != client_sha
cleanup = json.loads(data['provenance--cleanup.json'])
assert cleanup['watchLimit'] == '1048576' and json.loads(cleanup['aspirePsStdout']) == []
assert not any('story415' in name or 'works-' in name for name in cleanup['runningContainers'])
print(json.dumps({'verifiedArtifacts': len(manifest), 'acceptedReports': len(checks), 'candidateBoundHostStarts': len(candidate), 'packagedHostDllChecks': packaged_dll_checks, 'sourceHostArchiveComparisons': source_comparisons, 'nativeReceiptReplay': 'unchanged receipt/checkpoint; head dequeued only by candidate', 'defaultRelease': 'unfixed public 3.124.0'}, indent=2))
