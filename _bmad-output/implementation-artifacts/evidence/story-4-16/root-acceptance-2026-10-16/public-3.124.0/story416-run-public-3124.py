import pathlib,subprocess,json,hashlib,os,sys,xml.etree.ElementTree as ET
w=pathlib.Path('/home/administrator/projects/hexalith/works');sdk=pathlib.Path('/tmp/story416-sdk-3.124-public-source');out=w/'artifacts/story-4-16/public-3.124.0';prefix=sys.argv[1];method=sys.argv[2]
host=pathlib.Path('/tmp/story416-public-3124-host/bin/release/apphost.dll')
env={'WORKS_PLATFORM_APPHOST_ASSEMBLY':str(host),'WORKS_PLATFORM_APPHOST_SHA256':hashlib.sha256(host.read_bytes()).hexdigest(),'WORKS_PLATFORM_ROOT':'/home/administrator/projects/hexalith/platform','WORKS_PLATFORM_WORKS_ROOT':str(w),'Platform__Works__SourceRoot':str(w),'Platform__Works__EventStoreSourceRoot':str(sdk),'WORKS_PLATFORM_SDK_SOURCE_ROOT':str(sdk),'Platform__Identity__Enabled':'false','Version':'3.124.0','MinVerVersionOverride':'3.124.0','HexalithEventStoreVersion':'3.124.0','HexalithEventStoreRoot':str(sdk),'UseHexalithProjectReferences':'false','UseNuGetDeps':'true','HexalithAspireHostingDaprVersion':'13.6.1-beta.935','PlatformWorksRuntimeRoot':'/home/administrator/projects/hexalith/platform','ArtifactsPath':str(out),'UseArtifactsOutput':'true','WORKS_PLATFORM_RUNTIME_ARTIFACTS':str(out),'WORKS_PLATFORM_HOST_EVIDENCE':prefix+'-starts.jsonl','WORKS_SDK_PROOF_EVIDENCE_PATH':prefix+'-states.jsonl','WORKS_APPHOST_TEST_STARTUP_TIMEOUT_SECONDS':'180'}
cmd=[str(out/'bin/Hexalith.Works.IntegrationTests/release/Hexalith.Works.IntegrationTests'),'-noLogo','-method',method,'-result-xml',prefix+'.xml']
proof={'scope':'PUBLIC3.124.0 qualification preparation; no parity acceptance without complete fresh ledger','command':cmd,'cwd':str(w),'environment':env,'runtimeSource':{'root':str(sdk),'producerCommit':'0737c0befce5338e294a789092a0e223e8264a04','inventory':'_bmad-output/implementation-artifacts/evidence/story-4-16/root-acceptance-2026-10-16/public-3.124.0/runtime-source-inventory.json'},'preparation':'/tmp/story416-public-3124-prepare.json'}
pathlib.Path(prefix+'-command.json').write_text(json.dumps(proof,indent=2)+'\n')
e=os.environ.copy();e.update(env)
with open(prefix+'.log','w') as log:r=subprocess.run(cmd,cwd=w,env=e,stdout=log,stderr=subprocess.STDOUT)
print('exitCode',r.returncode,flush=True)
if pathlib.Path(prefix+'.xml').exists():
 a=ET.parse(prefix+'.xml').getroot().find('assembly');print({k:a.get(k) for k in ['total','passed','failed','skipped','time']},flush=True)
raise SystemExit(r.returncode)
