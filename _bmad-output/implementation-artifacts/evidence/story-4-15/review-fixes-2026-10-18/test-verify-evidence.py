#!/usr/bin/env python3
"""Corrupt semantic evidence while keeping archive integrity valid; verification must refuse it."""
import gzip
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

BASE = Path(__file__).resolve().parent


class EvidenceVerifierTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='story415-verifier-test-')
        self.addCleanup(self.temporary.cleanup)
        self.packet = Path(self.temporary.name) / 'packet'
        shutil.copytree(BASE, self.packet)

    def verify(self, expected_success=False):
        result = subprocess.run(['python3', str(self.packet / 'verify-evidence.py')], cwd='/',
                                capture_output=True, text=True)
        self.assertEqual(result.returncode == 0, expected_success, result.stdout + result.stderr)

    def mutate_archive(self, name, mutate, json_lines=False):
        path = self.packet / 'raw' / name
        original = gzip.decompress(path.read_bytes())
        value = [json.loads(line) for line in original.splitlines()] if json_lines else json.loads(original)
        mutate(value)
        value = ('\n'.join(json.dumps(item) for item in value) if json_lines else json.dumps(value)).encode()
        compressed = gzip.compress(value, mtime=0)
        path.write_bytes(compressed)
        manifest_path = self.packet / 'raw-artifacts.json'
        manifest = json.loads(manifest_path.read_text())
        item = next(item for item in manifest if item['path'] == 'raw/' + name)
        item.update(bytes=len(value), sha256=hashlib.sha256(value).hexdigest(),
                    compressedBytes=len(compressed), compressedSha256=hashlib.sha256(compressed).hexdigest())
        manifest_path.write_text(json.dumps(manifest))

    def mutate_phase(self, phase, mutate):
        self.mutate_archive('candidate-restart-pass--runtime.jsonl.gz',
                            lambda rows: mutate(next(row for row in rows if row['phase'] == phase)), True)

    def test_portable_packet_passes(self):
        self.verify(True)

    def test_ten_unique_unexpected_phases_fail(self):
        self.mutate_phase('complete-supported-process-coverage', lambda row: row.update(phase='unexpected'))
        self.verify()

    def test_missing_retained_plan_fails(self):
        self.mutate_phase('cascade-target-commit-before-checkpoint', lambda row: row['state']['checkpoint'].pop('PendingPlan'))
        self.verify()

    def test_premature_checkpoint_fails(self):
        self.mutate_phase('cascade-target-commit-before-checkpoint', lambda row: row['state']['checkpoint'].update(Checkpoint=7))
        self.verify()

    def test_missing_receipt_replay_fails(self):
        self.mutate_phase('child-resume-noop-restart-receipt-and-checkpoint', lambda row: row['state']['replay'].update(ReplayObserved=False))
        self.verify()

    def test_duplicate_target_event_fails(self):
        self.mutate_phase('five-child-persisted-target-receipts', lambda row: row['state'][0].update(cancelled=2))
        self.verify()

    def test_wrong_receipt_provenance_fails(self):
        self.mutate_phase('five-child-persisted-target-receipts', lambda row: row['state'][0]['receipt']['identity'].update(sourceEnvelopeSequence=6))
        self.verify()

    def test_nonclient_cold_archive_mismatch_fails(self):
        self.mutate_archive('review-provenance--cold-cache-archive-hashes.json.gz',
                            lambda value: next(item for item in value['archives'] if '.Contracts.' in item['archiveName']).update(sha256='0' * 64))
        self.verify()

    def test_nonclient_packaged_host_dll_mismatch_fails(self):
        def corrupt(rows):
            host = next(row for row in rows if row['resource'] == 'works')
            next(item for item in host['assemblies'] if item['path'].endswith('/Hexalith.EventStore.Contracts.dll'))['sha256'] = '0' * 64
        self.mutate_archive('provenance--runtime-bindings.jsonl.gz', corrupt, True)
        self.verify()


if __name__ == '__main__':
    unittest.main()
