from pathlib import Path
import datetime,json,subprocess,sys,time
prefix=sys.argv[1]
started=time.monotonic();active=None;observations=[];success=False
while time.monotonic()-started<1000:
    try:
        states=Path(prefix+'-states.jsonl')
        rows=[json.loads(x) for x in states.read_text().splitlines()] if states.exists() else []
        if not any(row.get('phase')=='complete-platform-fleet-snapshot' for row in rows):
            time.sleep(2);continue
        if active is None:active=time.monotonic()
        if time.monotonic()-active>90:break
        result=subprocess.run(['python3','/tmp/story416-root-inspect-owned-maps.py',prefix],text=True,capture_output=True)
        if result.returncode:
            observations.append({'checkedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'inspector-inconclusive','exitCode':result.returncode})
        else:
            receipt=json.loads(result.stdout)
            records=[json.loads(x) for x in Path(prefix+'-starts.jsonl').read_text().splitlines()]
            receipt['latestRole']=records[-1]['role'];receipt['latestReadinessPassed']=records[-1]['readinessPassed']
            observations.append(receipt)
            if receipt['status']=='matched' and receipt['latestRole']=='retained-rollback' and receipt['latestReadinessPassed']:
                success=True;break
    except (FileNotFoundError,json.JSONDecodeError):pass
    time.sleep(2)
path=Path(prefix+'-retained-map-watch.json')
path.write_text(json.dumps({'scope':'Read-only owned runtime process mappings after complete snapshot; no state mutation','status':'matched-retained-runtime' if success else 'inconclusive-no-complete-retained-map-sample','checkedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'observations':observations},indent=2)+'\n')
print(json.dumps({'resultPath':str(path),'matchedRetainedRuntime':success,'samples':len(observations)}))
sys.exit(0 if success else 2)
