import datetime
import json
import os
from pathlib import Path
import signal
import subprocess

root = Path('/mnt/wsl/story49-qualification-20261021T072935Z')
record_path = root / 'gate-attempt7-command.json'
quota_path = Path('/proc/sys/fs/inotify/max_user_watches')
original = 1048576
temporary = 2097152

def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()

def save():
    record_path.write_text(json.dumps(record, indent=2) + '\n')

def interrupted(signum, frame):
    raise KeyboardInterrupt(f'signal {signum}')

signal.signal(signal.SIGTERM, interrupted)
signal.signal(signal.SIGINT, interrupted)
command = [str(root / 'platform-source-attempt7/eng/verify-works-host.sh'),
           '--works-root', '/home/administrator/projects/hexalith/works',
           '--runtime-sdk-root', str(root / 'Hexalith.EventStore-source'),
           '--platform-source-binding', str(root / 'platform-source-attempt7.json'),
           '--artifacts-path', str(root / 'attempt7')]
overrides = {'HexalithBuildsRoot': str(root / 'Hexalith.Builds-source'),
             'HexalithEventStoreRoot': str(root / 'Hexalith.EventStore-source'),
             'TMPDIR': str(root / 'tmp'), 'XDG_DATA_HOME': str(root / 'xdg-data'),
             'PYTHONDONTWRITEBYTECODE': '1',
             'DOTNET_USE_POLLING_FILE_WATCHER': '1'}
observed = int(quota_path.read_text())
record = {'command': command, 'environmentOverrides': overrides,
          'startedAt': now(), 'wrapperPid': os.getpid(),
          'executionPolicy': 'Complete sequential gate; no overlapping test suites.',
          'quota': {'userApprovedTemporaryChangeAndRestore': True,
                    'previouslyRecordedOriginal': original,
                    'observedBeforeWrapper': observed,
                    'temporary': temporary,
                    'raisedByWrapper': False}}
if observed not in (original, temporary):
    raise SystemExit(f'Unexpected externally changed watch limit {observed}; refusing to alter it.')
if (root / 'attempt7').exists():
    raise SystemExit('attempt7 already exists; refusing output reuse.')
process = None
save()
try:
    if observed == original:
        subprocess.run(['sudo', '-n', 'sysctl', '-w', f'fs.inotify.max_user_watches={temporary}'], check=True)
        record['quota']['raisedByWrapper'] = True
    assert int(quota_path.read_text()) == temporary
    record['quota']['activeBeforeGate'] = temporary
    save()
    with (root / 'gate-attempt7.log').open('w') as output:
        process = subprocess.Popen(command, cwd='/home/administrator/projects/hexalith/works',
                                   env=os.environ | overrides, stdout=output,
                                   stderr=subprocess.STDOUT, start_new_session=True)
        record['gatePid'] = process.pid
        save()
        record['exitCode'] = process.wait()
except BaseException as error:
    record['wrapperError'] = repr(error)
    if process is not None and process.poll() is None:
        os.killpg(process.pid, signal.SIGTERM)
        try:
            process.wait(timeout=30)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait()
    record['exitCode'] = process.returncode if process is not None else 1
finally:
    current = int(quota_path.read_text())
    record['quota']['observedAfterGate'] = current
    if current == temporary:
        result = subprocess.run(['sudo', '-n', 'sysctl', '-w', f'fs.inotify.max_user_watches={original}'],
                                capture_output=True, text=True)
        record['quota']['restoreExitCode'] = result.returncode
        record['quota']['restoreOutput'] = result.stdout + result.stderr
    elif current != original:
        record['quota']['restoreConflict'] = 'External setting changed during gate; preserved.'
    record['quota']['observedAfterRestore'] = int(quota_path.read_text())
    record['quota']['restored'] = record['quota']['observedAfterRestore'] == original
    record['finishedAt'] = now()
    save()
print(json.dumps(record, indent=2))
raise SystemExit(record.get('exitCode', 1) or (0 if record['quota']['restored'] else 1))
