"""Focused CLI regressions for captured fixture credentials and ordinary evidence fingerprints."""
import base64
import gzip
import json
from pathlib import Path
import secrets
import subprocess
import sys
import tempfile
import unittest

SCANNER = Path(__file__).with_name("secret_scan.py")
RECEIVERS = ("eventstore", "eventstore-admin", "works", "works-ha", "eventstore-operations")


class SecretScanTests(unittest.TestCase):
    def scan(self, files):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for relative, contents in files.items():
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(contents)
            output = root / "scan.json"
            result = subprocess.run([sys.executable, str(SCANNER), str(root), str(output)],
                                    text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            self.assertEqual(result.returncode, 0, result.stderr)
            return json.loads(output.read_text()), output.read_text()

    def test_all_captured_receiver_tokens_are_found_in_plain_and_gzip_files(self):
        tokens = {receiver: secrets.token_hex(32) for receiver in RECEIVERS}
        raw = json.dumps(tokens).encode()
        for compressed in (False, True):
            with self.subTest(compressed=compressed):
                name = "nested/app-channel-tokens.json" + (".gz" if compressed else "")
                result, report = self.scan({name: gzip.compress(raw) if compressed else raw})
                self.assertTrue(result["credentialMaterialFound"])
                findings = [item for item in result["unclassified"] if item["pattern"] == "appChannelToken"]
                self.assertEqual({item["field"] for item in findings}, set(RECEIVERS))
                self.assertEqual(len(findings), len(RECEIVERS))
                for token in tokens.values():
                    self.assertNotIn(token, report)

    def test_authority_key_fields_are_found_even_below_generic_pattern_lengths(self):
        authority = {name: base64.b64encode(secrets.token_bytes(32)).decode()
                     for name in ("signingKey", "digestKey", "developmentSigningKey")}
        self.assertEqual(len(authority["digestKey"]), 44)
        raw = json.dumps(authority).encode()
        for compressed in (False, True):
            with self.subTest(compressed=compressed):
                name = "synthetic-authority.json" + (".gz" if compressed else "")
                result, report = self.scan({name: gzip.compress(raw) if compressed else raw})
                self.assertTrue(result["credentialMaterialFound"])
                findings = [item for item in result["unclassified"] if item["pattern"] == "credentialField"]
                self.assertEqual({item["field"] for item in findings}, set(authority))
                for key in authority.values():
                    self.assertNotIn(key, report)

    def test_digest_key_in_decompressed_jsonl_outcomes_is_found(self):
        digest = base64.b64encode(secrets.token_bytes(32)).decode()
        raw = (json.dumps({"state": {"digestKey": digest}}) + "\n").encode()
        result, report = self.scan({"persisted-states.jsonl.gz": gzip.compress(raw)})
        self.assertTrue(result["credentialMaterialFound"])
        self.assertEqual(result["unclassified"][0]["field"], "digestKey")
        self.assertNotIn(digest, report)

    def test_sha256_bindings_and_receiver_named_binary_fingerprints_are_not_credentials(self):
        fingerprint = secrets.token_hex(32)
        fingerprints = {"sha256": fingerprint, "digestKeySha256": fingerprint,
                        "appChannelTokensSha256": fingerprint, "works": fingerprint,
                        "fleet": {"app-channel-tokens.json": fingerprint}}
        raw = json.dumps(fingerprints).encode()
        result, _ = self.scan({"ledger.json": raw, "ledger.json.gz": gzip.compress(raw),
                               "synthetic-authority.json": json.dumps({"appChannelTokensSha256": fingerprint}).encode()})
        self.assertFalse(result["credentialMaterialFound"])
        self.assertEqual(result["unclassifiedMatches"], 0)

    def test_existing_payload_classification_and_unknown_long_base64_detection_are_preserved(self):
        payload = base64.b64encode(secrets.token_bytes(120)).decode()
        result, _ = self.scan({"outcomes.json.gz": gzip.compress(json.dumps({"Payload": payload}).encode())})
        self.assertFalse(result["credentialMaterialFound"])
        self.assertEqual(result["summary"][0]["count"], 1)
        result, _ = self.scan({"unknown.json": json.dumps({"other": payload}).encode()})
        self.assertTrue(result["credentialMaterialFound"])


if __name__ == "__main__":
    unittest.main()
