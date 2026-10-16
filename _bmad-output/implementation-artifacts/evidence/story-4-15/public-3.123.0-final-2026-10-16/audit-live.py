from pathlib import Path
import hashlib,json
p=Path(__file__).parent
source=p/'live-retry-state.jsonl'
raw=source.read_bytes()
rows=[json.loads(s) for s in raw.decode().splitlines()]
s={r['phase']:r['state'] for r in rows}
c={}
d=s['successful-callback-no-periodic-recovery'];c['callback persists membership and one witness']=d['processCandidate'] and len(d['witness']['entries'])==1
d=s['cascade-expiry-target-commit-before-checkpoint'];c['expiry commit precedes checkpoint']=d['childExpired']==1 and d['checkpoint']['Checkpoint']==3 and bool(d['checkpoint'].get('PendingPlan'))
d=s['cascade-expiry-restart-receipt-and-checkpoint'];c['expiry receipt replay then checkpoint']=d['childExpired']==1 and d['expiryCheckpoint']['Checkpoint']==4 and not d['expiryCheckpoint'].get('PendingPlan') and d['replay']['ReplayObserved'] and d['expiryReceipt']['disposition']==0 and d['expiryReceipt']['purpose']=='works.cascade-expire'
d=s['canonical-related-child-creation-gap'];c['canonical gap retains evidence without sibling mutation']=d['creationGap']['Checkpoint']==6 and d['creationGap']['FailureSequence']==7 and d['creationGap']['ReasonCode']=='evidence-unavailable' and d['creationGap']['ObservedUnresolved'] and d['readiness']=='Degraded' and d['existingChildCancellations']==0
d=s['cascade-target-commit-before-checkpoint'];c['cascade target commit retains five-effect plan']=d['childCancelled']==1 and d['checkpoint']['Checkpoint']==6 and len(d['checkpoint']['PendingPlan']['Effects'])==5
d=s['five-child-persisted-target-receipts'];c['five single cancellations with five success receipts']=len(d)==5 and len({x['effectId'] for x in d})==5 and all(x['cancelled']==1 and x['receipt']['disposition']==0 and x['receipt']['purpose']=='works.cascade-cancel' for x in d)
d=s['cascade-restart-receipt-and-checkpoint'];c['cascade receipt replay advances and clears plan']=d['checkpoint']['Checkpoint']==7 and not d['checkpoint'].get('PendingPlan') and d['receipt']['disposition']==0 and d['receipt']['purpose']=='works.cascade-cancel'
d=s['child-resume-noop-receipt-before-checkpoint'];c['one resume and pending no-op receipt']=d['parentResumed']==1 and d['checkpoint']['Checkpoint']==3 and bool(d['checkpoint'].get('PendingPlan')) and d['lostNoOp']['Disposition']==2
d=s['child-resume-noop-restart-receipt-and-checkpoint'];c['no-op receipt replay before checkpoint']=d['replay']['ReplayObserved'] and d['receipt']['disposition']==2 and d['receipt']['purpose']=='works.child-completion-resume' and d['checkpoint']['Checkpoint']==4 and not d['checkpoint'].get('PendingPlan')
d=s['complete-supported-process-coverage'];c['eleven sources complete without unresolved work']=d['Complete'] and d['Unresolved']==0 and d['RetainedCandidates']==11 and not d['CapacityExceeded']
result={'raw':str(source),'sha256':hashlib.sha256(raw).hexdigest(),'assertions':c,'phaseCount':len(rows)}
(p/'root-runtime-check.json').write_text(json.dumps(result,indent=2)+'\n')
assert len(rows)==10 and all(c.values()),c
redacted=[]
def redact(v):
 if isinstance(v,dict):return {k: ('[redacted]' if k.lower().endswith('payload') or k.lower() in ['privatekey','assertion','authorization'] else redact(x)) for k,x in v.items()}
 if isinstance(v,list):return [redact(x) for x in v]
 return v
(p/'persisted-states.json').write_text(json.dumps(redact(rows),indent=2)+'\n')
print('Ten independent persisted-state assertions passed.')
