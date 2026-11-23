"""Temporary synthetic-fixture CPU sample. Export only aggregate timings for named methods."""
from __future__ import annotations

import argparse
from collections import Counter
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time


MARKER = "cascade-after-delayed-child-created"
FAMILIES = {
    "technicalReceiverObservation": "TechnicalHttpRouteCatalogReceiver.Observe(",
    "technicalReceiverFingerprint": "TechnicalHttpRouteCatalogReceiver.Fingerprint(",
    "technicalObservationCompletion": "TechnicalHttpObservationPass.Complete(",
    "assemblyImageIdentity": "TechnicalHttpActionObservation.AssemblyIdentity(",
    "technicalActionDescription": "TechnicalHttpActionObservation.DescribeCore(",
    "jsonSchemaExport": "JsonSchemaExporter.GetJsonSchemaAsNode(",
    "routeCatalogAuthority": "Hexalith.EventStore.ServiceDefaults.Routing.RouteCatalogAuthority",
    "gatewayStreamController": "Hexalith.EventStore.Controllers.StreamsController",
    "aggregateActor": "Hexalith.EventStore.Server.Actors.AggregateActor",
}


def reached_marker(evidence: Path) -> bool:
    if not evidence.exists():
        return False
    with evidence.open(encoding="utf-8") as stream:
        for line in stream:
            if line.endswith("\n") and json.loads(line).get("phase") == MARKER:
                return True
    return False


def gateway_pid(gate: Path) -> int:
    base = (gate / "runtime/bin/Hexalith.EventStore/release/Hexalith.EventStore").resolve()
    matches = []
    for proc in Path('/proc').iterdir():
        if not proc.name.isdigit():
            continue
        try:
            executable = (proc / 'exe').resolve(strict=True)
            if executable == base:
                matches.append(int(proc.name))
            elif executable.name == 'dotnet' and os.fsencode(str(base) + '.dll') in (proc / 'cmdline').read_bytes().split(b'\0'):
                matches.append(int(proc.name))
        except (FileNotFoundError, PermissionError, ProcessLookupError):
            continue
    if len(matches) != 1:
        raise RuntimeError('Expected one live gateway at the owned runtime path')
    return matches[0]


def cpu_seconds(pid: int) -> float:
    fields = Path(f'/proc/{pid}/stat').read_text().rsplit(') ', 1)[1].split()
    return (int(fields[11]) + int(fields[12])) / os.sysconf('SC_CLK_TCK')


def summarize(profile_path: Path) -> dict:
    profile = json.loads(profile_path.read_text())
    frames = profile['shared']['frames']
    masks = [{name for name, pattern in FAMILIES.items() if pattern in frame['name']} for frame in frames]
    cpu_markers = {i for i, item in enumerate(frames) if item['name'] == 'CPU_TIME'}
    all_samples = Counter()
    cpu_samples = Counter()
    for thread in profile['profiles']:
        if thread['type'] != 'evented' or thread['unit'] != 'milliseconds':
            raise ValueError('Unexpected sampling format')
        stack = []
        active = Counter()
        previous = None
        for event in thread['events']:
            frame = event['frame']
            current = event['at']
            if previous is not None and stack:
                delta = current - previous
                all_samples['total'] += delta
                for key in active:
                    all_samples[key] += delta
                if stack[-1] in cpu_markers:
                    cpu_samples['total'] += delta
                    for key in active:
                        cpu_samples[key] += delta
            if event['type'] == 'O':
                stack.append(frame)
                active.update(masks[frame])
            else:
                if stack.pop() != frame:
                    raise ValueError('Unbalanced sampled stack')
                for key in masks[frame]:
                    active[key] -= 1
                    if not active[key]:
                        del active[key]
            previous = current
    return {'inclusiveSampledThreadMilliseconds': {k: round(v, 2) for k, v in all_samples.items()},
            'inclusiveCpuMarkerMilliseconds': {k: round(v, 2) for k, v in cpu_samples.items()}}


def collect(gate: Path, tool: Path) -> None:
    while not reached_marker(gate / 'persisted-states.jsonl'):
        time.sleep(0.5)
    result = {'phase': MARKER, 'scope': 'synthetic gateway process', 'sampleStartedUtc': datetime.now(timezone.utc).isoformat()}
    try:
        pid = gateway_pid(gate)
        with tempfile.TemporaryDirectory(prefix='works-gateway-cpu-') as directory:
            trace = Path(directory) / 'sample.nettrace'
            before = cpu_seconds(pid)
            started = time.monotonic()
            subprocess.run([str(tool), 'collect', '--process-id', str(pid), '--profile', 'dotnet-sampled-thread-time',
                            '--duration', '00:00:00:45', '--buffersize', '32', '--output', str(trace)],
                           cwd=directory, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True, timeout=90)
            result['elapsedSeconds'] = round(time.monotonic() - started, 3)
            try:
                result['processCpuSeconds'] = round(cpu_seconds(pid) - before, 3)
            except FileNotFoundError:
                result['gatewayExitedDuringCapture'] = True
            subprocess.run([str(tool), 'convert', str(trace), '--format', 'Speedscope'], cwd=directory,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True, timeout=90)
            result.update(summarize(trace.with_suffix('.speedscope.json')))
            result['status'] = 'captured'
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        result.update(status='diagnostic-unavailable', errorType=type(error).__name__)
    (gate / 'gateway-cpu-sample.json').write_text(json.dumps(result, indent=2) + '\n')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('gate', type=Path)
    parser.add_argument('tool', type=Path)
    args = parser.parse_args()
    collect(args.gate, args.tool)
