"""Only the exact CLI endpoint can change; runtime, mTLS and policy arguments remain bound."""
import json
from pathlib import Path
import tempfile
import subprocess
import unittest

import isolated_daprd


class IsolatedDaprdTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.runtime = self.root / "real-daprd"
        self.runtime.write_bytes(b"original runtime")
        self.directory = self.root / "isolated"
        self.binding = isolated_daprd.prepare(self.directory, self.runtime, 61001)
        self.config = self.root / "config.yaml"
        self.config.write_text("spec:\n  mtls:\n    enabled: true\n  accessControl:\n    defaultAction: deny\n")
        self.arguments = ["--app-id", "works", "--config", str(self.config), "--enable-mtls", "--sentry-address", "localhost:50001"]

    def test_executes_original_runtime_and_changes_only_the_exact_endpoint(self):
        calls = []
        isolated_daprd.execute(self.directory, self.arguments, executor=lambda *args: calls.append(args))
        expected = [*self.arguments[:-1], "127.0.0.1:61001"]
        self.assertEqual(calls, [(str(self.runtime), [str(self.runtime), *expected])])
        self.assertEqual(self.arguments[-1], "localhost:50001")
        record = json.loads((self.directory / "launches.jsonl").read_text())
        self.assertEqual({key: record[key] for key in self.binding}, self.binding)
        self.assertEqual(record["configSha256"], isolated_daprd.digest(self.config))
        self.assertEqual(record["originalSentryAddress"], "localhost:50001")

    def test_unexpected_or_ambiguous_launch_arguments_refuse_before_execution(self):
        for arguments in [[], ["--enable-mtls"], self.arguments[:-1], self.arguments[:-1] + ["foreign:50001"],
                          self.arguments + ["--sentry-address", "localhost:50001"], self.arguments + ["--sentry-address=localhost:50001"],
                          self.arguments + ["--enable-mtls"], [value for value in self.arguments if value != "--enable-mtls"],
                          self.arguments[:-2] + ["--sentry-address=localhost:50001"], self.arguments + ["--config", str(self.config)]]:
            with self.subTest(arguments=arguments), self.assertRaises(ValueError):
                isolated_daprd.execute(self.directory, arguments, executor=lambda *_: self.fail("Unexpected execution"))

    def test_runtime_and_adapter_substitution_are_refused(self):
        for path in [self.runtime, self.directory / ".dapr/bin/daprd"]:
            original = path.read_bytes()
            path.write_bytes(original + b"changed")
            with self.subTest(path=path), self.assertRaises(ValueError):
                isolated_daprd.execute(self.directory, self.arguments, executor=lambda *_: self.fail("Unexpected execution"))
            path.write_bytes(original)

    def test_read_only_cli_metadata_probe_keeps_the_original_runtime(self):
        calls = []
        isolated_daprd.execute(self.directory, ["--version"], executor=lambda *args: calls.append(args))
        self.assertEqual(calls, [(str(self.runtime), [str(self.runtime), "--version"])])
        self.assertFalse((self.directory / "launches.jsonl").exists())

    def test_cli_runtime_home_layout_executes_the_original_binary(self):
        runtime = self.root / "executable-daprd"
        runtime.write_text("#!/bin/sh\nprintf '%s\\n' \"$@\"\n")
        runtime.chmod(0o700)
        home = self.root / "cli-runtime-home"
        isolated_daprd.prepare(home, runtime, 61001)
        shim = home / ".dapr/bin/daprd"
        result = subprocess.run([str(shim), *self.arguments], check=True, capture_output=True, text=True, timeout=5)
        self.assertEqual(result.stdout.splitlines(), [*self.arguments[:-1], "127.0.0.1:61001"])

    def test_every_successful_start_must_match_an_actual_bound_sidecar_launch(self):
        isolated_daprd.execute(self.directory, self.arguments, executor=lambda *_: None)
        start = {"daprdRuntimePath": str(self.directory), "sidecarLaunches": [
            {"appId": "works", "config": str(self.config), "configSha256": isolated_daprd.digest(self.config)}]}
        self.assertEqual(isolated_daprd.verify(self.directory, self.binding, [start])["launchCount"], 1)
        for changed in [start | {"daprdRuntimePath": "foreign"}, start | {"sidecarLaunches": []},
                        start | {"sidecarLaunches": [{"appId": "foreign", "config": str(self.config), "configSha256": isolated_daprd.digest(self.config)}]}]:
            with self.subTest(changed=changed), self.assertRaises(ValueError):
                isolated_daprd.verify(self.directory, self.binding, [changed])
        original = self.runtime.read_bytes()
        self.runtime.write_bytes(original + b"changed")
        with self.assertRaises(ValueError):
            isolated_daprd.verify(self.directory, self.binding, [start])

    def test_invalid_port_binding_is_refused(self):
        for port in [True, 0, 1023, 65536, "61001"]:
            with self.subTest(port=port), self.assertRaises(ValueError):
                isolated_daprd.prepare(self.root / "invalid", self.runtime, port)


if __name__ == "__main__":
    unittest.main()
