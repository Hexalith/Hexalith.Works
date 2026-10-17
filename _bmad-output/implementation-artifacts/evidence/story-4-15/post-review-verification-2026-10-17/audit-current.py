from pathlib import Path
import hashlib,json,xml.etree.ElementTree as ET
p=Path(__file__).parent
sources=[p/'live-restart-state.jsonl',p/'live-callback-state.jsonl']
rows=[json.loads(l) for f in sources if f.exists() for l in f.read_text().splitlines()]
s={r['phase']:r['state'] for r in rows};checks={}
def check(phase,name,predicate):
 checks[name]=predicate(s[phase]) if phase in s else None
check('successful-callback-no-periodic-recovery','callback membership and witness',lambda d:d['processCandidate'] and len(d['witness']['entries'])==1)
check('rescheduled-expiry-committed-current-witness','revision 2 witness',lambda d:d['ExpiryWitness']['Revision']==2 and bool(d['ExpiryWitness']['ScheduleToken']))
check('cascade-expiry-target-commit-before-checkpoint','expiry target commit before checkpoint',lambda d:d['childExpired']==1 and d['checkpoint']['Checkpoint']==3 and bool(d['checkpoint'].get('PendingPlan')))
check('cascade-expiry-restart-receipt-and-checkpoint','expiry receipt replay before checkpoint',lambda d:d['childExpired']==1 and d['expiryCheckpoint']['Checkpoint']==4 and not d['expiryCheckpoint'].get('PendingPlan') and d['replay']['ReplayObserved'] and d['expiryReceipt']['disposition']==0 and d['expiryReceipt']['purpose']=='works.cascade-expire')
check('canonical-related-child-creation-gap','missing child retains degraded evidence',lambda d:d['creationGap']['Checkpoint']==6 and d['creationGap']['FailureSequence']==7 and d['creationGap']['ReasonCode']=='evidence-unavailable' and d['creationGap']['ObservedUnresolved'] and d['readiness']=='Degraded' and d['existingChildCancellations']==0)
check('cascade-target-commit-before-checkpoint','five retained cascade effects',lambda d:d['childCancelled']==1 and d['checkpoint']['Checkpoint']==6 and len(d['checkpoint']['PendingPlan']['Effects'])==5)
check('five-child-persisted-target-receipts','five unique success receipts and single cancellations',lambda d:len(d)==5 and len({x['effectId'] for x in d})==5 and all(x['cancelled']==1 and x['receipt']['disposition']==0 and x['receipt']['purpose']=='works.cascade-cancel' for x in d))
check('cascade-restart-receipt-and-checkpoint','cascade replay clears plan',lambda d:d['checkpoint']['Checkpoint']==7 and not d['checkpoint'].get('PendingPlan') and d['receipt']['disposition']==0 and d['receipt']['purpose']=='works.cascade-cancel')
check('child-resume-noop-receipt-before-checkpoint','single resume retains noop receipt',lambda d:d['parentResumed']==1 and d['checkpoint']['Checkpoint']==3 and bool(d['checkpoint'].get('PendingPlan')) and d['lostNoOp']['Disposition']==2)
check('child-resume-noop-restart-receipt-and-checkpoint','noop replay precedes checkpoint',lambda d:d['replay']['ReplayObserved'] and d['receipt']['disposition']==2 and d['receipt']['purpose']=='works.child-completion-resume' and d['checkpoint']['Checkpoint']==4 and not d['checkpoint'].get('PendingPlan'))
check('complete-supported-process-coverage','eleven sources complete',lambda d:d['Complete'] and d['Unresolved']==0 and d['RetainedCandidates']==11 and not d['CapacityExceeded'])
result={'inputs':{str(f):hashlib.sha256(f.read_bytes()).hexdigest() for f in sources if f.exists()},'phases':list(s),'assertions':checks,'complete':len(rows)==11 and all(v is True for v in checks.values())}
(p/'root-runtime-audit.json').write_text(json.dumps(result,indent=2)+'\n')
def redact(v):
 if isinstance(v,dict):return {k:('[redacted]' if k.lower().endswith('payload') or k.lower() in ['privatekey','assertion','authorization'] else redact(x)) for k,x in v.items()}
 if isinstance(v,list):return [redact(x) for x in v]
 return v
(p/'persisted-states.json').write_text(json.dumps(redact(rows),indent=2)+'\n')
print(json.dumps(result))
