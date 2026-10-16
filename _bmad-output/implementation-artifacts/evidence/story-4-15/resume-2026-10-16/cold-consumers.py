from pathlib import Path
import importlib.util,sys
root=Path('/home/administrator/projects/hexalith/works')
script=root/'references/Hexalith.EventStore/scripts/validate-consumer-package-references.py'
spec=importlib.util.spec_from_file_location('candidate_consumers',script)
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
feed=root/'.artifacts/story-4-15/feed'
for suffix in ['Contracts','Client','DomainService']:
 package=feed/f'Hexalith.EventStore.{suffix}.3.122.1-story415.20261016.10.nupkg'
 metadata=module.read_package_metadata(package) if hasattr(module,'read_package_metadata') else __import__('release_package_contract').read_package_metadata(package)
 print(f'Cold selected-package consumer: {metadata.package_id} {metadata.version}',flush=True)
 module.validate_library_package(metadata,feed)
print('All three cold package-only API consumers passed; each restored into its own empty packages directory.')
