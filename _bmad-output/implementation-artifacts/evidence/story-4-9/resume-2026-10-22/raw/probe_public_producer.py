from pathlib import Path
import datetime, hashlib, importlib.util, json, subprocess, sys, urllib.request, xml.etree.ElementTree as ET
workspace=Path('/home/administrator/projects/hexalith/works')
output=Path(__file__).resolve().parent
receipt={'observedAt':datetime.datetime.now(datetime.timezone.utc).isoformat(), 'status':'nonqualifying; fixed producer is not published'}
receipt['repositoryHeads']={name:subprocess.check_output(['git','-C',str(workspace/path),'rev-parse','HEAD'],text=True).strip() for name,path in {'Works':'.','Builds':'references/Hexalith.Builds','Platform':'references/Hexalith.Platform','EventStore':'references/Hexalith.EventStore'}.items()}
receipt['nugetIndexes']={}
for name in ('hexalith.eventstore.client','hexalith.eventstore.domainservice','hexalith.eventstore.aspire'):
    url=f'https://api.nuget.org/v3-flatcontainer/{name}/index.json'
    with urllib.request.urlopen(url,timeout=20) as response: raw=response.read()
    versions=json.loads(raw)['versions']
    stable=[version for version in versions if '-' not in version]
    receipt['nugetIndexes'][name]={'url':url,'responseSha256':hashlib.sha256(raw).hexdigest(),'latestStable':stable[-1],'stableTail':stable[-8:]}
    (output/(name+'-index.json')).write_bytes(raw)
package=Path('/home/administrator/.nuget/packages/hexalith.eventstore.client/3.126.1')
nuspec=package/'hexalith.eventstore.client.nuspec'
repository=next(node.attrib for node in ET.parse(nuspec).getroot().iter() if node.tag.endswith('repository'))
archive=package/'hexalith.eventstore.client.3.126.1.nupkg'
receipt['publishedProducer']={'repositoryCommit':repository['commit'],'repositoryUrl':repository['url'],'version':'3.126.1','archiveSha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'nuspecSha256':hashlib.sha256(nuspec.read_bytes()).hexdigest()}
helper=workspace/'references/Hexalith.Platform/eng/verify_works_producer.py'
spec=importlib.util.spec_from_file_location('producer',helper)
module=importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
receipt['canonicalHelper']={'path':str(helper),'sha256':hashlib.sha256(helper.read_bytes()).hexdigest()}
selected=module.runtime_source_snapshot(workspace/'references/Hexalith.EventStore')
receipt['selectedRuntimeSource']={'inventorySha256':selected['inventorySha256'],'fileCount':len(selected['sourceFiles'])}
try:
    result=module.qualify_runtime_source(selected,{'Client':receipt['publishedProducer']},tag='v3.126.1')
    receipt['qualification']={'passed':True,'result':result}
except Exception as error:
    receipt['qualification']={'passed':False,'exceptionType':type(error).__name__,'error':str(error)}
fix='src/Hexalith.EventStore.Client/Projections/ReadModelBatchImmutableRecord.cs'
probe=subprocess.run(['git','-C',str(workspace/'references/Hexalith.EventStore'),'cat-file','-e','v3.126.1:'+fix],capture_output=True,text=True)
receipt['releaseFixMembership']={'path':fix,'tag':'v3.126.1','exitCode':probe.returncode,'stderr':probe.stderr.strip(),'fixCommit':subprocess.check_output(['git','-C',str(workspace/'references/Hexalith.EventStore'),'log','-1','--format=%H','--',fix],text=True).strip()}
(output/'public-producer-probe.json').write_text(json.dumps(receipt,indent=2)+'\n')
print(json.dumps({'receipt':str(output/'public-producer-probe.json'),'latestStable':{name:item['latestStable'] for name,item in receipt['nugetIndexes'].items()},'qualificationPassed':receipt['qualification']['passed'],'qualificationError':receipt['qualification'].get('error','')[:1200],'releaseFixMembership':receipt['releaseFixMembership']},indent=2))
sys.exit(0 if not receipt['qualification']['passed'] and receipt['releaseFixMembership']['exitCode']==128 else 1)
