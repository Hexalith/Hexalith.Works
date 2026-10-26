"""Evidence gates reject successful exits with incomplete or substituted execution."""
import copy
from contextlib import ExitStack
import hashlib
import http.client
import io
import json
import os
import socket
import subprocess
from pathlib import Path
import tempfile
import tarfile
import unittest
import zipfile
from unittest.mock import patch

import verify_works_host
from verify_works_host import (CROSS_CHECKS, DUMP_INVENTORY, PROOF_NAMES, PROOFS, RESTORE_PHASE, ROWS, SNAPSHOT_PHASE, SUPPORTING_CLASSES,
                               alert_cleared_after_raise, alert_raised_after, bind_persisted_outcomes, delegated_identity, fixture_sources,
                               platform_composition_literals, published_producers, require_unchanged_fixture_sources, utc_ticks,
                               require_archive_assembly, require_consumer_pin, require_consumer_pins, require_host_package_pins, require_identical_runtime,
                               require_passed_tests, require_published_runtime_source, required_phases, restored_fleet_matches,
                               selected_sdk_version, sha256, verify_host_records)

VERSION = '3.125.1'
DIGEST = 'a' * 64
HOOK_TYPE = 'Hexalith.Platform.Works.Runtime.WorksRecoveryHostingStartup'
VOLUME = 'works-sdk416-owned'
RETAINED = [{'role': 'retained-rollback', 'schedulerVolume': VOLUME}]
PREVIEW_MODEL = Path(__file__).resolve().parents[1] / 'src/Hexalith.Platform.Composition/WorksPreviewModel.cs'
LITERALS = {'EventStore__RateLimiting__PermitLimit': '8192', 'EventStore__RateLimiting__ConsumerPermitLimit': '8192',
            'Authentication__Workload__AllowedCallers__0': 'eventstore', 'Authentication__Workload__AllowedCallers__1': 'eventstore-admin',
            'Works__Recovery__UseSdkRuntime': 'true', 'Works__Projection__UseSdkDelivery': 'true'}


def identity(calls=2):
    actor = '01ARZ3NDEKTSV4RRFFQ69G5FAV'
    return {'tenant': 'sdk415-tenant', 'isDelegated': True, 'authenticatedWorkloadId': 'eventstore', 'originalActorId': actor,
            'userId': actor, 'partyId': 'party-416-executor', 'resolvedTenant': 'sdk415-tenant', 'resolvedParty': 'party-416-executor',
            'resolvedActor': actor, 'identityCalls': calls}


def compositions():
    """Platform's composed tracked values per resource, as the harness records them."""
    common = {'DOTNET_ENVIRONMENT': 'Development'}
    return {'eventstore': common | {key: value for key, value in LITERALS.items() if key.startswith('EventStore__RateLimiting__')},
            'works': common | {key: value for key, value in LITERALS.items() if not key.startswith('EventStore__RateLimiting__')},
            'eventstore-operations': dict(common), 'eventstore-admin': dict(common)}


def heads(count):
    return {f'source-{index}': {'expectedOwnerSequence': 2, 'expectedGlobalPosition': 10 + index,
                                'observed': {'OwnerSequenceNumber': 2, 'Position': 10 + index}} for index in range(count)}


def valid_states():
    """The smallest state per required phase that carries every qualifying proof field."""
    refused = {'receiverStatus': 403, 'callerStatus': 403}
    disposition = {'audit': {'Attempts': [{'Actor': 'synthetic-operator-416'}]}, 'acceptedCallerStatus': 204, 'beforeGeneration': 0,
                   'afterGeneration': 2, 'wrongPurpose': refused, 'wrongTenant': refused, 'wrongAggregate': refused,
                   'auditOutage': {'receiverStatus': 500, 'callerStatus': 500}}
    fleet = {'certificates/ca.crt': 'b' * 64, 'redis-key-count': '682'}
    runtime = {'/runtime/Hexalith.Works.dll': 'c' * 64}
    return {
        'platform-historical-lifecycle-tree-concurrent-claim-persisted': {'familyDigest': DIGEST, 'claimOutcomes': ['Completed', 'Rejected'],
                                                                         'lifecycle': {'Events': [{}] * 7}, 'replay': {'Status': 'Completed'}},
        'platform-historical-restart-replay-and-family': {'completed': True, 'claimCount': 1, 'familyDigest': DIGEST},
        'platform-executor-filtering-and-origin-tenant-publish-refusals': {'wrongCaller': 401, 'wrongPort': 401, 'forbiddenPublish': 403,
                                                                           'own': {'found': True}, 'visible': ['mine', 'queued', 'race'],
                                                                           'hidden': {'found': False}, 'wrongActor': 'Forbidden',
                                                                           'wrongTenant': 'Forbidden', 'unchangedFamily': DIGEST,
                                                                           'queryIdentity': identity()},
        'complete-supported-process-coverage': {'Complete': True, 'Unresolved': 0, 'RetainedCandidates': 12},
        'periodic-writers-disabled-readiness-degraded': {'status': 'Degraded'},
        # The startup point precedes the observed Degraded state; only the later raise qualifies.
        'disabled-recovery-otlp-alert-export': {'degradedObservedAt': '2026-10-19T07:00:05.1234567+00:00',
                                                'points': [{'Name': 'eventstore.domain.recovery.alert', 'Value': 1, 'ReceivedAt': '2026-10-19T07:00:01.0000000+00:00', 'UnixTimeNanoseconds': utc_ticks('2026-10-19T07:00:01Z') * 100},
                                                           {'Name': 'eventstore.domain.recovery.alert', 'Value': 1, 'ReceivedAt': '2026-10-19T07:00:06.0000000+00:00', 'UnixTimeNanoseconds': utc_ticks('2026-10-19T07:00:06Z') * 100}]},
        'durable-recovery-alert-export-and-clear': [{'Name': 'eventstore.domain.recovery.alert', 'Value': 1, 'UnixTimeNanoseconds': 10},
                                                    {'Name': 'eventstore.domain.recovery.alert', 'Value': 0, 'UnixTimeNanoseconds': 20}],
        'platform-named-redelivery-capture-through-commit-complete-family': {
            'redelivered': {'Outcomes': [{'Status': 1}]}, 'fenced': {'Outcomes': [{'Status': 2}]},
            'concurrentDelivery': {'redeliveryLogicalCount': 1}, 'before': {'Digest': DIGEST}, 'after': {'Digest': DIGEST},
            'negatives': {'wrongOrigin': 401, 'wrongOperation': 403}, 'queryIdentity': identity()},
        'platform-named-conflict-durable-quarantine-with-unchanged-family': {'acceptedFamilySha256': DIGEST, 'unchangedFamilySha256': DIGEST,
                                                                             'repeatedConflictCount': 1, 'conflict': {'SourceStream': 'item'},
                                                                             'queryIdentity': identity(1)},
        'platform-initial-projection-convergence': {'naturalCatchUp': True, 'heads': heads(12)},
        'platform-focused-projection-rebuild': {'concurrentDelivery': {'automaticCatchUpAfterCommit': True, 'oldGenerationStayedReadable': True,
                                                                       'after': {'Digest': DIGEST}, 'expectedDigest': DIGEST},
                                                'sources': ['source'] * 11, 'committed': {'Phase': 3}, 'queryIdentity': identity(4)},
        'platform-query-and-explicit-receipt-replay': {'query': {'found': True, 'latestAcceptedSourceSequence': 7},
                                                       'queryIdentity': identity(1),
                                                       'nonDelegatedFullTenantQuery': {'error': 'Forbidden', 'isDelegated': False,
                                                                                       'authenticatedWorkloadId': 'eventstore', 'identityCalls': 0},
                                                       'receipt': {'EffectId': 'EFFECT'}, 'conflictingReplayStatus': 500,
                                                       'durableCollisionCount': 1, 'attributedCollisionAudit': True},
        'successful-callback-no-periodic-recovery': {'witness': {'entries': [{}]}, 'processCandidate': True},
        'platform-two-host-ha-lost-firing-stale-callback-recovery': {
            'actualStaleCallbackObserved': True, 'staleSchedulerFireResumed': 1, 'retiredReminderRemovedAfterFiring': True,
            'beforeCallbacks': 1, 'afterCallbacks': 2, 'resumeCountAtPrimaryStop': 0, 'primaryStoppedBeforeDue': True,
            'trust': {'trustedClientAcceptedByServer': True, 'noClientCertificateRefusedByServer': True, 'foreignCaClientRefusedByServer': True,
                      'nameMismatchRejectedByProbe': True, 'serverIdentity': 'spiffe://public/ns/default/works'}},
        'cascade-expiry-restart-receipt-and-checkpoint': {'replay': {'ReplayObserved': True}, 'childExpired': 1, 'expiryReceipt': {'disposition': 0}},
        'concurrent-sdk-reminder-reissue-after-lost-scheduler-wakeup': {'callers': 2, 'persistedSchedulerReminder': {'actorType': 'WorksSdkReminderActor'}},
        'paged-process-candidates-pre-restart': {'expectedSources': 12, 'pages': [{}, {}, {}, {}]},
        'canonical-related-child-creation-gap': {'creationGap': {'ReasonCode': 'evidence-unavailable'}, 'existingChildCancellations': 0, 'readiness': 'Degraded'},
        'paged-process-restart-and-shared-store-owner-fencing': {'linkedPages': 4, 'candidates': [{}] * 12, 'staleOwnerWriteRefused': True,
                                                                 'concurrentOwners': 1, 'priorFence': 32, 'successorFence': 33},
        'protected-scoped-disposition-audit-before-mutation': disposition,
        'callback-protected-scoped-disposition-audit-before-mutation': copy.deepcopy(disposition),
        'platform-sdk-named-mode-legacy-route-refused': {'legacyRouteStatus': 404, 'missingChannelStatus': 401,
                                                         'acceptedFamilySha256': DIGEST, 'unchangedFamilySha256': DIGEST},
        'child-resume-noop-restart-receipt-and-checkpoint': {'replay': {'ReplayObserved': True, 'Disposition': 2}, 'checkpoint': {'Checkpoint': 4}},
        'five-child-persisted-target-receipts': [{'receipt': {'purpose': 'works.cascade-cancel', 'disposition': 0}, 'cancelled': 1}] * 5,
        'cascade-restart-receipt-and-checkpoint': {'receipt': {'effectId': 'CASCADE'}, 'committedEffect': 'CASCADE', 'checkpoint': {'Checkpoint': 7}},
        SNAPSHOT_PHASE: {'fleet': fleet | {DUMP_INVENTORY: 'd' * 64}, 'runtimeHashes': runtime, 'schedulerVolume': VOLUME},
        RESTORE_PHASE: {'convergence': {'query': {'found': True, 'conversationCorrelationId': {'value': 'synthetic-capture-416-0123456789'}},
                                        'queryIdentity': identity(1),
                                        'receipt': {'Replayed': True, 'Disposition': 2, 'EffectId': 'EFFECT'}, 'resumedCount': 1,
                                        'restoredSweep': {'Complete': True, 'Unresolved': 0, 'RetainedCandidates': 12},
                                        'durableCollisionCount': 1, 'restoredOperatorAuditAttempts': 1},
                        'fleet': copy.deepcopy(fleet), 'runtimeHashes': copy.deepcopy(runtime), 'dateResumed': 1, 'childCancelled': 1,
                        'childExpired': 1, 'restoredConfigurationVerified': True},
    }


class WorksEvidenceGateTests(unittest.TestCase):
    def test_agents_nested_compile_helpers_are_explicitly_hash_bound(self):
        helpers = set(verify_works_host.VERIFIER_HELPERS)
        self.assertTrue({"verify-agents-host.sh", "ext-host-1/verify_full.py", "ext-host-1/prepare_acceptance.py",
                         "ext-host-1/test_verify_full.py", "ext-host-1/test_prepare_acceptance.py"}.issubset(helpers))
        self.assertEqual(len(helpers), len(verify_works_host.VERIFIER_HELPERS))
        for name in helpers:
            self.assertTrue((Path(__file__).parent / name).is_file(), name)

    def test_full_and_ci_profiles_record_their_real_dependency_checks_and_same_live_requirement(self):
        full = verify_works_host.verification_profile('full')
        ci = verify_works_host.verification_profile('ci')
        self.assertEqual(full['consumerConfigurations'], ['Debug', 'Release'])
        self.assertEqual(full['agentsConfiguration'], 'Debug')
        self.assertEqual(ci['consumerConfigurations'], ['Release'])
        self.assertEqual(ci['agentsConfiguration'], 'Release')
        self.assertEqual(ci['sourceConformance'], 'not-requested; full-local-profile-only')
        self.assertEqual(full['liveProofs'], ci['liveProofs'])
        with self.assertRaises(ValueError): verify_works_host.verification_profile('reduced')

    def test_ci_consumer_builds_are_release_and_package_only(self):
        for project in ['apphost.cs', 'tests/consumer.csproj']:
            for configuration in verify_works_host.verification_profile('ci')['consumerConfigurations']:
                command = verify_works_host.consumer_build_command(project, configuration, VERSION)
                self.assertEqual(command[command.index('-c') + 1], 'Release')
                self.assertIn('-p:UseHexalithProjectReferences=false', command)
                self.assertIn('-p:UseNuGetDeps=true', command)
                self.assertNotIn('-p:UseHexalithProjectReferences=true', command)
        debug = verify_works_host.consumer_build_command('consumer.csproj', 'Debug', VERSION)
        self.assertIn('-p:UseHexalithProjectReferences=true', debug)
        self.assertIn('-p:UseNuGetDeps=false', debug)

    def test_r10_points_at_the_current_workload_policy_and_attaches_all_live_commands(self):
        self.assertEqual(ROWS['R10'][3], 'src/Hexalith.Works/Runtime/WorksDomainAdapters.cs')
        names = ['retained-host-preflight', 'platform-recovery', 'platform-historical', 'platform-ha', 'platform-mtls', 'platform-projection']
        commands = [{'name': name, 'exitCode': 0} for name in names + ['build-only']]
        self.assertEqual(verify_works_host.target_commands(commands), commands[:-1])

    def test_linked_composition_source_exception_is_bound_from_the_compiled_root_and_detects_mutation(self):
        works = self.directory()
        platform = self.directory()
        identity = works / 'references/Hexalith.Platform/src/Hexalith.Platform.Identity'
        stale = works / 'references/Hexalith.Platform/src/Hexalith.Platform.Composition'
        compiled = platform / 'src/Hexalith.Platform.Composition'
        for directory, project in ((identity, 'Identity'), (stale, 'Composition'), (compiled, 'Composition')):
            directory.mkdir(parents=True)
            (directory / 'Adapter.cs').write_text('// original ' + project)
            (directory / 'bin').mkdir()
            (directory / 'bin/Generated.cs').write_text('// excluded')
        (identity / 'Hexalith.Platform.Identity.csproj').write_text('<Project />')
        (stale / 'Hexalith.Platform.Composition.csproj').write_text('<Project />')
        (compiled / 'Hexalith.Platform.Composition.csproj').write_text(
            '<Project><ItemGroup><Compile Include="../../DaprSelfHostedMtls.cs" Link="DaprSelfHostedMtls.cs" />'
            '<EmbeddedResource Include="../../eng/ext-secrets-1/public-joint-plan.json" /></ItemGroup></Project>')
        linked = platform / 'DaprSelfHostedMtls.cs'
        linked.write_text('// linked control plane')
        resource = platform / 'eng/ext-secrets-1/public-joint-plan.json'
        resource.parent.mkdir(parents=True)
        resource.write_text('{"plan":"compiled"}')
        test_project = works / 'tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj'
        test_project.parent.mkdir(parents=True)
        test_project.write_text('<Project />')
        with patch('verify_works_host.subprocess.check_output', return_value='fixture-commit\n'):
            before = verify_works_host.linked_fixture_snapshot(works, platform)
            self.assertEqual(before['compositionRoot'], str(platform))
            self.assertIn(str(compiled / 'Adapter.cs'), before['sourceFiles'])
            self.assertIn(str(linked), before['sourceFiles'])
            self.assertIn(str(resource), before['sourceFiles'])
            self.assertIn(str(identity / 'Adapter.cs'), before['sourceFiles'])
            self.assertFalse(any(name.startswith(str(stale)) for name in before['sourceFiles']))
            self.assertFalse(any('/bin/' in name for name in before['sourceFiles']))
            # The uncompiled Works dependency copy is not the bound exception.
            (stale / 'Adapter.cs').write_text('// changed uncompiled copy')
            self.assertEqual(verify_works_host.linked_fixture_snapshot(works, platform), before)
            for path in (compiled / 'Adapter.cs', linked, resource):
                with self.subTest(path=path.name):
                    original = path.read_text()
                    path.write_text('// changed compiled composition')
                    after = verify_works_host.linked_fixture_snapshot(works, platform)
                    self.assertNotEqual(before['sourceFiles'][str(path)], after['sourceFiles'][str(path)])
                    path.write_text(original)
            resource.unlink()
            with self.assertRaises(FileNotFoundError):
                verify_works_host.linked_fixture_snapshot(works, platform)

    def test_linked_fixture_sources_remain_bound_below_output_named_workspace_parents(self):
        for parent in ('bin', 'obj'):
            with self.subTest(parent=parent):
                workspace = self.directory() / parent
                works, platform = workspace / 'works', workspace / 'platform'
                identity = works / 'references/Hexalith.Platform/src/Hexalith.Platform.Identity'
                composition = platform / 'src/Hexalith.Platform.Composition'
                parties = works / 'references/Hexalith.Parties'
                for directory, name in ((identity, 'Identity'), (composition, 'Composition')):
                    directory.mkdir(parents=True)
                    (directory / 'Adapter.cs').write_text('// source')
                    (directory / ('Hexalith.Platform.' + name + '.csproj')).write_text('<Project />')
                    for output in ('bin', 'obj'):
                        (directory / output).mkdir()
                        (directory / output / 'Generated.cs').write_text('// excluded')
                (composition / 'Hexalith.Platform.Composition.csproj').write_text(
                    '<Project><Compile Include="../../Linked.cs" /><Compile Include="../../obj/Generated.cs" /></Project>')
                (platform / 'Linked.cs').write_text('// linked')
                project = works / 'tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj'
                project.parent.mkdir(parents=True)
                project.write_text('<Project><Compile Include="$(HexalithPartiesRoot)/src/Authority.cs" />'
                                   '<Compile Include="$(HexalithPartiesRoot)/obj/Generated.cs" /></Project>')
                authority = parties / 'src/Authority.cs'
                authority.parent.mkdir(parents=True)
                authority.write_text('// authority')
                with patch('verify_works_host.subprocess.check_output', return_value='a' * 40 + '\n'):
                    before = verify_works_host.linked_fixture_snapshot(works, platform)
                    expected = {str(identity / 'Adapter.cs'), str(identity / 'Hexalith.Platform.Identity.csproj'),
                                str(composition / 'Adapter.cs'), str(composition / 'Hexalith.Platform.Composition.csproj'),
                                str(platform / 'Linked.cs'), str(authority)}
                    self.assertEqual(set(before['sourceFiles']), expected)
                    for path in (identity / 'Adapter.cs', composition / 'Adapter.cs', platform / 'Linked.cs', authority):
                        original = path.read_text()
                        path.write_text('// changed')
                        self.assertNotEqual(before, verify_works_host.linked_fixture_snapshot(works, platform))
                        path.write_text(original)

    def test_ci_execution_records_release_checks_without_claiming_absent_debug_success(self):
        works = self.directory()
        artifacts = self.directory() / 'evidence'
        args = type('Args', (), {'works_root': works, 'runtime_sdk_root': None, 'artifacts_path': artifacts,
                                'prepare_only': False, 'profile': 'ci'})()
        snapshot = {'root': str(works), 'commit': 'c', 'sourceFiles': {}, 'inventorySha256': 'i'}
        def command(arguments, **kwargs):
            name = Path(kwargs['stdout'].name).stem
            if name == 'platform-file-host-release': raise RuntimeError('stopped after profile checks')
            if name == 'agents-full-refusal':
                kwargs['stdout'].write('DependencyNotAvailable: EXT-HOST-1')
                return subprocess.CompletedProcess(arguments, 1)
            return subprocess.CompletedProcess(arguments, 0)
        with patch('verify_works_host.selected_sdk_version', return_value=VERSION), \
                patch('verify_works_host.require_consumer_pins', return_value={}), \
                patch('verify_works_host.require_host_package_pins', return_value={}), \
                patch('verify_works_host.source_snapshot', return_value=snapshot), \
                patch('verify_works_host.fixture_sources', return_value={}), \
                patch('verify_works_host.canonical_source_snapshot', return_value=snapshot), \
                patch('verify_works_host.linked_fixture_snapshot', return_value={}) as linked, \
                patch('verify_works_host.sha256', return_value=DIGEST), \
                patch('verify_works_host.require_passed_tests', return_value={'total': 1, 'passed': 1, 'skipped': 0}), \
                patch('verify_works_host.run_owned', side_effect=command):
            self.assertEqual(verify_works_host.run_verification(args), 1)
        # The Composition exception is bound from the Platform tree passed to the consumer as PlatformWorksRuntimeRoot.
        platform = Path(verify_works_host.__file__).resolve().parents[1]
        self.assertEqual(linked.call_args.args, (works, platform))
        ledger = json.loads((artifacts / 'ledger.json').read_text())
        self.assertEqual(ledger['profile']['name'], 'ci')
        self.assertIn('Release', ledger['checks'])
        self.assertNotIn('Debug', ledger['checks'])
        self.assertEqual(set(ledger['sourceProducers']), {'worksRuntimeSdk'})
        self.assertIn('stopped after profile checks', ledger['failure'])
        for entry in ledger['commands']:
            if entry['name'].startswith('agents-'):
                self.assertIn('--configuration', entry['command'])
                self.assertIn('Release', entry['command'])
            if entry['command'][0] == 'dotnet':
                self.assertIn('-p:UseHexalithProjectReferences=false', entry['command'])
                self.assertIn('-p:UseNuGetDeps=true', entry['command'])

    def run_until_live_lanes(self, runtime_bytes, isolated_hash=None, finish=False, final_error=None, observed_runtime_envs=None):
        """Drive the real gate sequence through toolchain qualification and optionally final acceptance."""
        works = self.directory()
        for _, _, _, consumer in ROWS.values():
            if not consumer.startswith('P/'):
                (works / consumer).parent.mkdir(parents=True, exist_ok=True)
                (works / consumer).write_text('// consumer')
        artifacts = self.directory() / 'evidence'
        runtime = self.directory() / 'daprd'
        runtime.write_bytes(runtime_bytes)
        platform = Path(verify_works_host.__file__).resolve().parents[1]
        qualified = hashlib.sha256(b'qualified runtime').hexdigest()
        pins = {'daprCli': '1.18.2', 'daprRuntime': '1.18.7', 'linuxAmd64DaprdSha256': qualified,
                'catalog': str(platform / verify_works_host.CATALOG), 'catalogSha256': DIGEST}
        args = type('Args', (), {'works_root': works, 'runtime_sdk_root': None, 'artifacts_path': artifacts,
                                'prepare_only': False, 'profile': 'ci'})()
        snapshot = {'root': str(works), 'commit': 'c', 'sourceFiles': {}, 'inventorySha256': 'i'}
        producers = {package: {'version': VERSION} for _, package, _, _ in ROWS.values()} | {'Hexalith.EventStore.Client': {'version': VERSION}}

        def command(arguments, **kwargs):
            name = Path(kwargs['stdout'].name).stem
            if observed_runtime_envs is not None and verify_works_host.target_commands([{'name': name}]):
                observed_runtime_envs.append(kwargs['env'])
            if verify_works_host.target_commands([{'name': name}]) and not finish:
                raise RuntimeError('stopped at live lane ' + name)
            if name == 'works-consumer-release':
                output = artifacts / 'consumer/bin/Hexalith.Works.IntegrationTests/release'
                output.mkdir(parents=True)
                for suffix in ('', '.dll', '.deps.json', '.runtimeconfig.json'):
                    (output / ('Hexalith.Works.IntegrationTests' + suffix)).write_text('qualified output')
            if name == 'works-consumer-supporting':
                consumer = json.loads((artifacts / 'ledger.json').read_text())['consumer']
                self.assertEqual(Path(consumer['root']), Path(arguments[0]).parent)
                self.assertEqual(len(consumer['artifacts']), 4)
            if name == 'agents-full-refusal':
                kwargs['stdout'].write('DependencyNotAvailable: EXT-HOST-1')
                return subprocess.CompletedProcess(arguments, 1)
            return subprocess.CompletedProcess(arguments, 0)

        prepare = verify_works_host.isolated_daprd.prepare

        def isolated(directory, path, port):
            binding = prepare(directory, path, port)
            return binding | ({'runtimeSha256': isolated_hash} if isolated_hash else {})

        with ExitStack() as final_checks, \
                patch('verify_works_host.selected_sdk_version', return_value=VERSION), \
                patch('verify_works_host.require_consumer_pins', return_value={}), \
                patch('verify_works_host.require_host_package_pins', return_value={}), \
                patch('verify_works_host.source_snapshot', return_value=snapshot), \
                patch('verify_works_host.fixture_sources', return_value={}), \
                patch('verify_works_host.canonical_source_snapshot', return_value=snapshot), \
                patch('verify_works_host.linked_fixture_snapshot', return_value={}), \
                patch('verify_works_host.sha256', return_value=DIGEST), \
                patch('verify_works_host.require_passed_tests', return_value={'total': 1, 'passed': 1, 'skipped': 0}), \
                patch('verify_works_host.published_producers', return_value=producers), \
                patch('verify_works_host.require_published_runtime_source', return_value={}), \
                patch('verify_works_host.ET.parse', return_value=verify_works_host.ET.ElementTree(verify_works_host.ET.fromstring(
                    '<Project><HexalithAspireHostingDaprVersion>1.0.0</HexalithAspireHostingDaprVersion></Project>'))), \
                patch('verify_works_host.select_control_plane_ports', return_value={'sentry': 41001, 'placement': 41002, 'scheduler': 41003}), \
                patch('verify_works_host.isolated_daprd.prepare', side_effect=isolated), \
                patch('works_toolchain.load', return_value=pins), \
                patch('works_toolchain.subprocess.check_output', side_effect=['1.18.7', 'CLI version: 1.18.2\nRuntime version: 1.18.7']), \
                patch.dict(os.environ, {'WORKS_PLATFORM_DAPRD': str(runtime)}), \
                patch('verify_works_host.run_owned', side_effect=command):
            for target, value in (('require_identical_runtime', {}), ('retained_works_host.prepare', {}),
                                  ('verify_host_records', []), ('isolated_daprd.verify', {})):
                final_checks.enter_context(patch('verify_works_host.' + target, return_value=value))
            closure = final_checks.enter_context(patch('verify_works_host.require_unchanged_inputs', side_effect=final_error))
            outcomes = final_checks.enter_context(patch('verify_works_host.bind_persisted_outcomes', return_value={'complete': True}))
            self.assertEqual(verify_works_host.run_verification(args), 0 if finish and final_error is None else 1)
            if finish:
                closure.assert_called_once()
                self.assertEqual(closure.call_args.args[1:4], (works, platform, None))
                if final_error is None:
                    outcomes.assert_called_once()
                else:
                    outcomes.assert_not_called()
        return json.loads((artifacts / 'ledger.json').read_text()), artifacts, qualified

    def test_release_dependency_record_names_every_accepted_test_only_source_exception(self):
        ledger, _, _ = self.run_until_live_lanes(b'qualified runtime')
        self.assertEqual(ledger['dependencyModes']['Release'],
                         'public NuGet ' + VERSION + ' consumer graph plus explicitly named test-only '
                         'Platform.Identity, Platform.Composition, conditional Platform.Works.Runtime '
                         'and linked Parties authority/query source')

    def test_source_fetch_token_is_not_inherited_by_the_live_runtime(self):
        observed = []
        with patch.dict(os.environ, {'GITHUB_TOKEN': 'synthetic-source-fetch-token'}):
            self.run_until_live_lanes(b'qualified runtime', observed_runtime_envs=observed)
        self.assertTrue(observed)
        for environment in observed:
            self.assertFalse('GITHUB_TOKEN' in environment)

    def test_complete_pipeline_checks_final_inputs_before_accepting_persisted_evidence(self):
        ledger, _, _ = self.run_until_live_lanes(b'qualified runtime', finish=True)
        self.assertEqual(ledger['status'], 'passed-disposable-synthetic-parity-and-rollback')
        self.assertEqual(ledger['rollback'], {'complete': True})
        self.assertNotIn('failure', ledger)

    def test_final_input_refusal_prevents_pipeline_acceptance_and_persisted_evidence_binding(self):
        ledger, _, _ = self.run_until_live_lanes(b'qualified runtime', finish=True,
                                                final_error=ValueError('Final source input drift'))
        self.assertEqual(ledger['status'], 'incomplete')
        self.assertEqual(ledger['failure'], 'Final source input drift')
        self.assertFalse(ledger['rollback']['complete'])

    def test_unqualified_runtime_refuses_before_any_live_platform_lane(self):
        ledger, artifacts, _ = self.run_until_live_lanes(b'unqualified runtime')
        self.assertIn("qualified Works proof runtime hash", ledger['failure'])
        self.assertEqual(verify_works_host.target_commands(ledger['commands']), [])
        self.assertNotIn('qualifiedToolchain', ledger)
        self.assertNotIn('isolatedRuntime', ledger)
        self.assertFalse((artifacts / 'isolated-daprd').exists())
        # Preparation did run, so the refusal is the toolchain gate and not an earlier stop.
        self.assertIn('works-consumer-release', [entry['name'] for entry in ledger['commands']])

    def test_isolated_runtime_other_than_the_qualified_bytes_refuses_before_any_live_platform_lane(self):
        ledger, _, qualified = self.run_until_live_lanes(b'qualified runtime', isolated_hash='f' * 64)
        self.assertIn('isolated daprd differs', ledger['failure'])
        self.assertEqual(ledger['qualifiedToolchain']['runtimeSha256'], qualified)
        self.assertEqual(verify_works_host.target_commands(ledger['commands']), [])

    def test_qualified_runtime_binds_toolchain_and_both_isolated_launchers_before_live_lanes(self):
        ledger, _, qualified = self.run_until_live_lanes(b'qualified runtime')
        self.assertIn('stopped at live lane retained-host-preflight', ledger['failure'])
        self.assertEqual(ledger['qualifiedToolchain']['runtimeSha256'], qualified)
        self.assertEqual(ledger['qualifiedToolchain']['linuxAmd64DaprdSha256'], qualified)
        self.assertEqual(ledger['isolatedRuntime']['runtimeSha256'], qualified)
        self.assertEqual(ledger['retainedPreflightRuntime']['runtimeSha256'], qualified)
        self.assertEqual([entry['name'] for entry in verify_works_host.target_commands(ledger['commands'])], ['retained-host-preflight'])

    def test_toolchain_must_be_derived_from_the_platform_builds_catalog(self):
        platform = Path(verify_works_host.__file__).resolve().parents[1]
        catalog = str(platform / verify_works_host.CATALOG)
        with patch('verify_works_host.sha256', return_value=DIGEST):
            verify_works_host.require_toolchain_authority({'catalog': catalog, 'catalogSha256': DIGEST}, platform)
            for qualified in ({'catalog': str(self.directory() / 'Directory.Packages.props'), 'catalogSha256': DIGEST},
                              {'catalog': catalog, 'catalogSha256': 'b' * 64}):
                with self.subTest(qualified=qualified), self.assertRaisesRegex(ValueError, 'Builds catalog'):
                    verify_works_host.require_toolchain_authority(qualified, platform)

    def test_agents_scaffold_defaults_to_source_debug_but_accepts_package_release(self):
        directory = self.directory()
        commands = directory / 'bin'
        commands.mkdir()
        captured = directory / 'dotnet-args'
        for name, body in {'python3': '#!/bin/sh\nexit 0\n',
                           'dotnet': '#!/bin/sh\nprintf "%s\\n" "$@" > "$AGENTS_BUILD_ARGS"\n'}.items():
            executable = commands / name
            executable.write_text(body)
            executable.chmod(0o755)
        environment = os.environ.copy() | {'PATH': str(commands) + os.pathsep + os.environ['PATH'], 'AGENTS_BUILD_ARGS': str(captured)}
        script = Path(__file__).parent / 'verify-agents-host.sh'
        for configuration in ['Debug', 'Release']:
            options = [] if configuration == 'Debug' else ['--configuration', 'Release']
            result = subprocess.run(['bash', script, '--mode', 'LocalScaffold', *options, '--artifacts-path', directory / configuration],
                                    env=environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
            self.assertEqual(result.returncode, 0, result.stdout)
            arguments = captured.read_text().splitlines()
            self.assertEqual(arguments[arguments.index('-c') + 1], configuration)
            self.assertIn('-p:UseNuGetDeps=' + ('true' if configuration == 'Release' else 'false'), arguments)
            self.assertIn('-p:UseHexalithProjectReferences=' + ('false' if configuration == 'Release' else 'true'), arguments)
        captured.unlink()
        refused = subprocess.run(['bash', script, '--configuration', 'Custom'], env=environment,
                                 stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        self.assertNotEqual(refused.returncode, 0)
        self.assertFalse(captured.exists())

    def result(self, attributes='', test='<test method="Required" result="Pass" />'):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        path = Path(temporary.name) / 'result.xml'
        path.write_text(f'<assemblies><assembly total="1" {attributes}>{test}</assembly></assemblies>')
        return path

    def directory(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        return Path(temporary.name)

    def test_skipped_failed_error_and_not_run_counts_are_refused(self):
        for key in ['skipped', 'failed', 'errors', 'not-run']:
            with self.subTest(key=key), self.assertRaises(ValueError):
                require_passed_tests(self.result(f'{key}="1"'))

    def test_empty_filtered_or_nonpassing_execution_is_refused(self):
        for test in ['', '<test method="Required" result="Skip" />', '<test method="Required" result="Fail" />']:
            with self.subTest(test=test), self.assertRaises(ValueError):
                require_passed_tests(self.result(test=test))
        with self.assertRaises(ValueError):
            require_passed_tests(self.result(), ['Missing'])

    def test_passed_required_method_has_exact_result_hash(self):
        path = self.result()
        self.assertEqual(require_passed_tests(path, ['Required']), {'total': 1, 'passed': 1, 'skipped': 0, 'sha256': sha256(path)})

    def test_required_supporting_classes_must_execute(self):
        with self.assertRaises(ValueError):
            require_passed_tests(self.result(), required_types=["RequiredClass"])
        path = self.result(test='<test type="RequiredClass" method="Required" result="Pass" />')
        self.assertEqual(require_passed_tests(path, required_types=["RequiredClass"])["total"], 1)

    def archive(self, files):
        archive = io.BytesIO()
        with tarfile.open(fileobj=archive, mode='w') as output:
            for name, content in files.items():
                member = tarfile.TarInfo(name)
                member.size = len(content)
                output.addfile(member, io.BytesIO(content))
        return archive.getvalue()

    def test_runtime_source_must_match_public_archive_producer(self):
        content = b'public producer source'
        snapshot = {'root': '/fixture/sdk', 'commit': 'metadata-only-new-head',
                    'sourceFiles': {'src/Producer.cs': hashlib.sha256(content).hexdigest()}, 'inventorySha256': 'bound-inventory'}
        producers = {'Published.SDK': {'repositoryCommit': 'public-producer'}}
        with patch('verify_works_host.subprocess.check_output', return_value=self.archive({'src/Producer.cs': content})):
            result = require_published_runtime_source(snapshot, producers)
            self.assertTrue(result['matchesPublishedProducer'])
            self.assertEqual(result['repositoryCommit'], 'public-producer')
            snapshot['sourceFiles']['src/Producer.cs'] = 'unpublished-patch'
            with self.assertRaises(ValueError):
                require_published_runtime_source(snapshot, producers)

    def test_every_published_src_build_input_is_compared(self):
        files = {'src/Producer.cs': b'source', 'src/Directory.Build.props': b'<Project />', 'src/appsettings.json': b'{}'}
        snapshot = {'root': '/fixture/sdk', 'commit': None, 'inventorySha256': 'bound-inventory',
                    'sourceFiles': {name: hashlib.sha256(content).hexdigest() for name, content in files.items()}}
        producers = {'Published.SDK': {'repositoryCommit': 'public-producer'}}
        with patch('verify_works_host.subprocess.check_output', return_value=self.archive(files)):
            self.assertTrue(require_published_runtime_source(snapshot, producers)['matchesPublishedProducer'])
            for name in ['src/Directory.Build.props', 'src/appsettings.json']:
                changed = copy.deepcopy(snapshot)
                changed['sourceFiles'][name] = 'unpublished-input'
                with self.subTest(name=name), self.assertRaisesRegex(ValueError, name):
                    require_published_runtime_source(changed, producers)
                removed = copy.deepcopy(snapshot)
                del removed['sourceFiles'][name]
                with self.subTest(removed=name), self.assertRaisesRegex(ValueError, name):
                    require_published_runtime_source(removed, producers)

    def host_record(self, root, role='platform-target', ha=False):
        assembly = root / 'bin/release/apphost.dll'
        assembly.parent.mkdir(parents=True, exist_ok=True)
        assembly.write_bytes(b'bound fixture')
        runtime = root / 'consumer/Hexalith.Platform.Works.Runtime.dll'
        runtime.parent.mkdir(parents=True, exist_ok=True)
        runtime.write_bytes(b'platform runtime')
        # Path-mapped deterministic builds give Platform's host output the same bytes the hook loads.
        (root / 'bin/release/Hexalith.Platform.Works.Runtime.dll').write_bytes(b'platform runtime')
        sdk = [{'name': 'Hexalith.EventStore.Aspire', 'version': VERSION + '.0', 'path': str(assembly), 'sha256': sha256(assembly)}]
        names = ['eventstore', 'works', 'eventstore-operations', 'eventstore-admin'] + (['works-ha'] if ha else [])
        hosting = {'hostingStartupAssemblies': ['Hexalith.Platform.Works.Runtime', 'Hexalith.Works.IntegrationTests'],
                   'startupHooks': [str(runtime), str(root / 'consumer/Hexalith.Works.IntegrationTests.dll')],
                   'startups': [{'name': 'Hexalith.Platform.Works.Runtime', 'loaded': True, 'location': str(runtime), 'sha256': sha256(runtime),
                                 'hostingStartupTypes': [HOOK_TYPE]},
                                {'name': 'Hexalith.Works.IntegrationTests', 'loaded': True, 'hostingStartupTypes': []}],
                   'platformRuntimeEnabled': True, 'platformTelemetryRegisteredBeforeProof': True, 'telemetryPublisherRegistrations': 1,
                   'environment': compositions()['works']}
        composed = compositions()
        overlaps = [{'key': 'APP_API_TOKEN', 'rule': 'captured-restorable-synthetic-secret'}]
        guard = {name: {'platformComposedKeys': sorted([*values, 'APP_API_TOKEN', 'OTEL_SERVICE_NAME']), 'fixtureKeys': ['APP_API_TOKEN', 'WORKS_SDK_PROOF'],
                        'permittedOverlaps': copy.deepcopy(overlaps), 'composition': values} for name, values in composed.items()}
        guard['works']['platformComposedKeys'] += ['DOTNET_STARTUP_HOOKS']
        guard['works']['fixtureKeys'] += ['DOTNET_STARTUP_HOOKS']
        guard['works']['permittedOverlaps'].append({'key': 'DOTNET_STARTUP_HOOKS', 'rule': 'extends-composed-list'})
        effective = copy.deepcopy(composed) | ({'works-ha': copy.deepcopy(composed['works'])} if ha else {})
        startups = {name: copy.deepcopy(hosting) for name in ('works', 'works-ha') if name in names}
        startups['eventstore'] = {'environment': copy.deepcopy(composed['eventstore'])}
        return assembly, {'assembly': str(assembly), 'sha256': sha256(assembly), 'directory': str(root), 'schedulerVolume': VOLUME,
                          'role': role, 'actorType': 'WorksSdkReminderActor', 'readinessPassed': True, 'worksReadinessExpectation': 'Healthy',
                          'sdkAssemblies': sdk,
                          'runtimeModules': [{'resource': name, 'configuration': 'Release', 'entry': str(assembly), 'entrySha256': sha256(assembly),
                                              'sdkAssemblies': sdk} for name in names],
                          'hostingStartups': startups, 'compositionGuard': guard, 'effectiveEnvironment': effective}

    def verify(self, root, assembly, *records, runtime_artifacts=None, control_plane_ports=None):
        path = root / 'starts.jsonl'
        path.write_text(''.join(json.dumps(record) + '\n' for record in records))
        return verify_host_records(path, assembly, root, VERSION,
                                   runtime_artifacts={str(assembly): sha256(assembly)} if runtime_artifacts is None else runtime_artifacts,
                                   host_runtime=root / 'bin/release/Hexalith.Platform.Works.Runtime.dll', composition_literals=LITERALS,
                                   control_plane_ports=control_plane_ports)

    def test_every_start_binds_run_owned_ports_and_preserves_prebuilt_dependencies(self):
        root = self.directory()
        assembly, record = self.host_record(root, ha=True)
        ports = {'sentry': 61001, 'placement': 61005, 'scheduler': 61006}
        record.update(controlPlanePorts=ports, dependencyBuildsSuppressed=True)
        self.assertEqual(len(self.verify(root, assembly, record, control_plane_ports=ports)), 1)
        for changed in [record | {'controlPlanePorts': None}, record | {'controlPlanePorts': ports | {'sentry': 50001}},
                        record | {'dependencyBuildsSuppressed': False}, record | {'dependencyBuildsSuppressed': None}]:
            with self.subTest(changed=changed), self.assertRaises(ValueError):
                self.verify(root, assembly, changed, control_plane_ports=ports)

    def test_control_plane_port_selection_uses_distinct_unprivileged_ports(self):
        ports = verify_works_host.select_control_plane_ports()
        self.assertEqual(set(ports), {'sentry', 'placement', 'scheduler'})
        self.assertEqual(len(set(ports.values())), 3)
        self.assertTrue(all(type(port) is int and 1024 <= port < 65535 for port in ports.values()))
        lower, upper = verify_works_host.host_ephemeral_port_range()
        self.assertTrue(all(not lower <= port <= upper for port in ports.values()))

    def test_control_plane_ports_cannot_collide_with_later_application_allocator(self):
        # A very wide allocator forces explicit selection into its small complement.
        with patch.object(verify_works_host, 'host_ephemeral_port_range', return_value=(1024, 65000)):
            ports = verify_works_host.select_control_plane_ports()
        self.assertTrue(all(65000 < port < 65535 for port in ports.values()))
        with self.assertRaisesRegex(ValueError, 'outside the host ephemeral range'):
            verify_works_host.select_control_plane_ports((1024, 65535))

    def test_docker_wsl_boundary_port_cannot_be_selected_even_when_socket_bind_succeeds(self):
        # A free socket is insufficient: an actual owned Alpine publish on 65535 times out.
        with patch.object(verify_works_host.socket, 'socket') as socket_factory:
            ports = verify_works_host.select_control_plane_ports((1024, 65531), ())
        self.assertTrue(all(65531 < port < 65535 for port in ports.values()))
        self.assertNotIn(65535, [call.args[0][1] for call in socket_factory.return_value.bind.call_args_list])
        with self.assertRaisesRegex(ValueError, 'outside the host ephemeral range'):
            verify_works_host.select_control_plane_ports((1024, 65534), ())

    def test_administrator_reserved_ports_are_never_selected(self):
        ports = verify_works_host.select_control_plane_ports((1024, 65000), ((65530, 65534),))
        self.assertTrue(all(65000 < port < 65530 for port in ports.values()))
        with self.assertRaisesRegex(ValueError, 'outside the host ephemeral range'):
            verify_works_host.select_control_plane_ports((1024, 65000), ((65001, 65535),))

    def test_reserved_port_ranges_are_read_and_invalid_or_unreadable_policy_refuses(self):
        path = self.directory() / 'reserved-ports'
        with self.assertRaisesRegex(ValueError, 'Cannot establish'):
            verify_works_host.host_reserved_port_ranges(path)
        for value, expected in [('', ()), ('15001,65000-65010', ((15001, 15001), (65000, 65010)))]:
            path.write_text(value)
            self.assertEqual(verify_works_host.host_reserved_port_ranges(path), expected)
        for value in ('invalid', '65000-1024', '0', '1024,', '65536', '1-2-3'):
            with self.subTest(value=value):
                path.write_text(value)
                with self.assertRaisesRegex(ValueError, 'Cannot establish'):
                    verify_works_host.host_reserved_port_ranges(path)

    def test_control_plane_selection_rejects_an_ipv6_only_occupied_port(self):
        candidate = verify_works_host.select_control_plane_ports()['sentry']
        with socket.socket(socket.AF_INET6, socket.SOCK_STREAM) as occupied:
            occupied.setsockopt(socket.IPPROTO_IPV6, socket.IPV6_V6ONLY, 1)
            occupied.bind(('::1', candidate))
            self.assertNotIn(candidate, verify_works_host.select_control_plane_ports().values())
            # The failed IPv6 attempt must release its successful IPv4 probe too.
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as ipv4:
                ipv4.bind(('127.0.0.1', candidate))

    def test_unreadable_or_invalid_host_ephemeral_range_refuses(self):
        path = self.directory() / 'ephemeral-range'
        with self.assertRaisesRegex(ValueError, 'Cannot establish'):
            verify_works_host.host_ephemeral_port_range(path)
        for value in ('invalid', '32768', '65000 1024', '0 65535', '1024 65536'):
            with self.subTest(value=value):
                path.write_text(value)
                with self.assertRaisesRegex(ValueError, 'Cannot establish'):
                    verify_works_host.host_ephemeral_port_range(path)

    def test_another_host_or_scheduler_cannot_qualify_platform(self):
        root = self.directory()
        assembly, record = self.host_record(root)
        _, ha = self.host_record(root, ha=True)
        self.assertEqual(len(self.verify(root, assembly, record, ha)), 2)
        for key, value in [('sha256', 'changed'), ('assembly', str(root / 'old-host.dll')), ('directory', str(root / 'works')), ('schedulerVolume', 'hexalith-works-dapr-scheduler'), ('role', 'unknown'), ('actorType', 'DateReminderActor'), ('readinessPassed', False), ('runtimeModules', []), ('sdkAssemblies', []), ('sdkAssemblies', [{'name': 'Hexalith.EventStore.Aspire', 'version': '3.124.0.0', 'path': str(assembly), 'sha256': sha256(assembly)}]), ('sdkAssemblies', [{'name': 'Hexalith.EventStore.Aspire', 'version': '3.125.2.0', 'path': str(assembly), 'sha256': sha256(assembly)}])]:
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.verify(root, assembly, record | {key: value}, ha)

    def test_ha_start_must_bind_the_independent_replica_module(self):
        root = self.directory()
        assembly, record = self.host_record(root)
        with self.assertRaisesRegex(ValueError, 'works-ha'):
            self.verify(root, assembly, record)
        _, ha = self.host_record(root, ha=True)
        del ha['hostingStartups']['works-ha']
        with self.assertRaisesRegex(ValueError, 'works-ha'):
            self.verify(root, assembly, record, ha)

    def test_platform_start_must_load_platform_hosting_startup_before_the_proof(self):
        root = self.directory()
        assembly, record = self.host_record(root, ha=True)
        self.assertEqual([item['resource'] for item in self.verify(root, assembly, record)[0]['platformHosting']], ['works', 'works-ha'])
        mutations = {
            'absent record': lambda hosting: hosting.clear(),
            'proof only': lambda hosting: hosting.update(hostingStartupAssemblies=['Hexalith.Works.IntegrationTests']),
            'proof first': lambda hosting: hosting.update(hostingStartupAssemblies=['Hexalith.Works.IntegrationTests', 'Hexalith.Platform.Works.Runtime']),
            'replaced hook': lambda hosting: hosting.update(startupHooks=[hosting['startupHooks'][1]]),
            'not loaded': lambda hosting: hosting['startups'][0].update(loaded=False),
            'no hosting type': lambda hosting: hosting['startups'][0].update(hostingStartupTypes=[]),
            'unbound artifact': lambda hosting: hosting['startups'][0].update(sha256='0' * 64),
            'runtime gate off': lambda hosting: hosting.update(platformRuntimeEnabled=False),
            'did not run first': lambda hosting: hosting.update(platformTelemetryRegisteredBeforeProof=False),
            'duplicate telemetry': lambda hosting: hosting.update(telemetryPublisherRegistrations=2),
        }
        for name, mutate in mutations.items():
            for resource in ['works', 'works-ha']:
                changed = copy.deepcopy(record)
                mutate(changed['hostingStartups'][resource])
                with self.subTest(name=name, resource=resource), self.assertRaisesRegex(ValueError, resource):
                    self.verify(root, assembly, changed)

    def test_degraded_expected_start_records_unpassed_readiness_only(self):
        root = self.directory()
        assembly, record = self.host_record(root, ha=True)
        degraded = record | {'readinessPassed': False, 'worksReadinessExpectation': 'Degraded'}
        self.assertEqual(len(self.verify(root, assembly, degraded)), 1)
        for changed in [degraded | {'readinessPassed': True}, record | {'readinessPassed': False},
                        record | {'worksReadinessExpectation': None}, degraded | {'role': 'retained-rollback'}]:
            with self.subTest(changed={key: changed.get(key) for key in ('readinessPassed', 'worksReadinessExpectation', 'role')}), self.assertRaises(ValueError):
                self.verify(root, assembly, changed)

    def test_retained_rollback_must_bind_the_retained_works_host(self):
        root = self.directory()
        assembly, record = self.host_record(root, ha=True)
        retained = root / 'runtime/bin/Hexalith.Works.AppHost/release/Hexalith.Works.AppHost.dll'
        retained.parent.mkdir(parents=True)
        retained.write_bytes(b'retained host')
        rollback = record | {'role': 'retained-rollback', 'assembly': str(retained), 'sha256': sha256(retained),
                             'directory': str(root / 'works'), 'hostingStartups': {}}
        self.assertEqual(len(self.verify(root, assembly, record, rollback)), 2)
        other = root / 'runtime/bin/Other.AppHost/release/Other.AppHost.dll'
        other.parent.mkdir(parents=True)
        other.write_bytes(b'other host')
        debug = root / 'runtime/bin/Hexalith.Works.AppHost/debug/Hexalith.Works.AppHost.dll'
        debug.parent.mkdir(parents=True)
        debug.write_bytes(b'debug host')
        for name, changed in [('platform host relabeled', rollback | {'assembly': str(assembly), 'sha256': sha256(assembly)}),
                              ('foreign host', rollback | {'assembly': str(other), 'sha256': sha256(other)}),
                              ('debug host', rollback | {'assembly': str(debug), 'sha256': sha256(debug)}),
                              ('changed binary', rollback | {'sha256': '0' * 64}),
                              ('shared scheduler', rollback | {'schedulerVolume': 'hexalith-works-dapr-scheduler'})]:
            with self.subTest(name=name), self.assertRaises(ValueError):
                self.verify(root, assembly, record, changed)

    def test_retained_startup_preflight_cannot_supply_platform_or_rollback_parity(self):
        root = self.directory()
        assembly, record = self.host_record(root)
        retained = root / 'runtime/bin/Hexalith.Works.AppHost/release/Hexalith.Works.AppHost.dll'
        retained.parent.mkdir(parents=True)
        retained.write_bytes(b'exact historical host')
        record.update(role='retained-rollback', assembly=str(retained), sha256=sha256(retained))
        path = root / 'preflight.jsonl'
        path.write_text(json.dumps(record) + '\n')
        self.assertEqual(len(verify_host_records(path, assembly, root, VERSION,
            retained_assembly=retained, retained_preflight=True)), 1)
        with self.assertRaisesRegex(ValueError, 'No actual Platform starts'):
            verify_host_records(path, assembly, root, VERSION, retained_assembly=retained)
        with self.assertRaisesRegex(ValueError, 'exactly one'):
            verify_host_records(path, assembly, root, VERSION, retained_preflight=True)

    def test_mixed_or_missing_fleet_configuration_cannot_qualify(self):
        root = self.directory()
        release, record = self.host_record(root, ha=True)
        debug = root / 'bin/debug/apphost.dll'
        debug.parent.mkdir(parents=True)
        debug.write_bytes(b'valid independently hashed artifact')
        modules = record['runtimeModules']
        for configuration in [None, 'Debug', 'Custom']:
            changed = [modules[0] | {'configuration': configuration}, *modules[1:]]
            with self.subTest(configuration=configuration), self.assertRaisesRegex(ValueError, 'Release configuration'):
                self.verify(root, release, record | {'runtimeModules': changed})
        changed = [modules[0] | {'entry': str(debug), 'entrySha256': sha256(debug)}, *modules[1:]]
        with self.assertRaisesRegex(ValueError, 'Release configuration'):
            self.verify(root, release, record | {'runtimeModules': changed})
        with self.assertRaisesRegex(ValueError, 'Release deployment'):
            self.verify(root, debug, record | {'assembly': str(debug), 'sha256': sha256(debug)})

    def test_live_module_from_another_sdk_version_cannot_qualify(self):
        root = self.directory()
        assembly, record = self.host_record(root, ha=True)
        record['runtimeModules'][0]['sdkAssemblies'] = [record['sdkAssemblies'][0] | {'version': '3.124.0.0'}]
        with self.assertRaisesRegex(ValueError, 'unpublished runtime SDK assembly'):
            self.verify(root, assembly, record)

    def outcomes(self, states, extra=()):
        path = self.directory() / 'states.jsonl'
        path.write_text(''.join(json.dumps({'phase': phase, 'state': state}) + '\n' for phase, state in [*states.items(), *extra]))
        return path

    def test_valid_outcomes_derive_every_rows_four_proofs_from_named_phases(self):
        rows = {key: {} for key in ROWS}
        result = bind_persisted_outcomes(self.outcomes(valid_states()), rows, RETAINED)
        self.assertTrue(result['complete'])
        self.assertEqual(len(result['crossChecks']), len(CROSS_CHECKS))
        for key, row in rows.items():
            self.assertEqual(row['status'], 'passed')
            self.assertEqual(set(row['requiredProofs']), set(PROOF_NAMES))
            for name, proof in row['requiredProofs'].items():
                self.assertTrue(proof['passed'])
                self.assertEqual([item['phase'] for item in proof['evidence']], [phase for phase, _ in PROOFS[key][name]])
                self.assertTrue(all(item['fields'] for item in proof['evidence']))

    def test_every_mapped_proof_field_is_load_bearing(self):
        for key, proofs in PROOFS.items():
            for name, requirements in proofs.items():
                for phase, fields in requirements:
                    for field, (predicate, _) in fields.items():
                        states = valid_states()
                        with self.subTest(row=key, proof=name, phase=phase, field=field):
                            if not field:
                                # A whole-state predicate accepts the qualifying state and refuses an empty one.
                                self.assertTrue(predicate(states[phase]))
                                self.assertFalse(predicate({} if isinstance(states[phase], dict) else []))
                                continue
                            *parents, leaf = field.split('.')
                            container = states[phase]
                            for part in parents:
                                container = container[int(part)] if isinstance(container, list) else container[part]
                            del container[leaf]
                            with self.assertRaisesRegex(ValueError, 'lacks required field|Cross-phase binding failed'):
                                bind_persisted_outcomes(self.outcomes(states), {key: {}}, RETAINED)

    def test_non_qualifying_values_are_refused(self):
        cases = [
            ('platform-two-host-ha-lost-firing-stale-callback-recovery', ('trust', 'foreignCaClientRefusedByServer'), False, 'R10'),
            ('platform-two-host-ha-lost-firing-stale-callback-recovery', ('trust', 'serverIdentity'), 'spiffe://public/ns/default/eventstore', 'R10'),
            ('platform-sdk-named-mode-legacy-route-refused', ('legacyRouteStatus',), 200, 'R9'),
            ('platform-sdk-named-mode-legacy-route-refused', ('unchangedFamilySha256',), 'b' * 64, 'R9'),
            ('platform-executor-filtering-and-origin-tenant-publish-refusals', ('hidden', 'found'), True, 'R5'),
            ('platform-focused-projection-rebuild', ('concurrentDelivery', 'automaticCatchUpAfterCommit'), False, 'R4'),
            ('platform-initial-projection-convergence', ('heads', 'source-3', 'observed', 'Position'), 2, 'R3'),
            ('periodic-writers-disabled-readiness-degraded', ('status',), 'Healthy', 'R2'),
            ('platform-executor-filtering-and-origin-tenant-publish-refusals', ('wrongPort',), 200, 'R1'),
            ('protected-scoped-disposition-audit-before-mutation', ('afterGeneration',), 0, 'R8'),
            ('paged-process-restart-and-shared-store-owner-fencing', ('staleOwnerWriteRefused',), 'true', 'R7'),
            (RESTORE_PHASE, ('dateResumed',), 2, 'R6'),
            (RESTORE_PHASE, ('convergence', 'receipt', 'Replayed'), False, 'R11'),
        ]
        for phase, path, value, row in cases:
            states = valid_states()
            container = states[phase]
            for part in path[:-1]:
                container = container[part]
            container[path[-1]] = value
            with self.subTest(phase=phase, path=path), self.assertRaisesRegex(ValueError, row + ' .*does not qualify'):
                bind_persisted_outcomes(self.outcomes(states), {row: {}}, RETAINED)
        states = valid_states()
        states['five-child-persisted-target-receipts'] = states['five-child-persisted-target-receipts'][:4]
        with self.assertRaisesRegex(ValueError, 'R11 persisted'):
            bind_persisted_outcomes(self.outcomes(states), {key: {} for key in ROWS}, RETAINED)

    def test_duplicate_missing_or_unbound_phases_cannot_accept_rows(self):
        states = valid_states()
        with self.assertRaisesRegex(ValueError, 'Duplicate persisted outcome phase'):
            bind_persisted_outcomes(self.outcomes(states, [('platform-initial-projection-convergence', states['platform-initial-projection-convergence'])]),
                                    {}, RETAINED)
        for phase in sorted(required_phases()):
            missing = valid_states()
            del missing[phase]
            with self.subTest(phase=phase), self.assertRaisesRegex(ValueError, 'did not execute: ' + phase):
                bind_persisted_outcomes(self.outcomes(missing), {}, RETAINED)
        with self.assertRaisesRegex(ValueError, 'retained-host restore start'):
            bind_persisted_outcomes(self.outcomes(valid_states()), {}, [{'role': 'platform-target'}])
        malformed = self.directory() / 'states.jsonl'
        malformed.write_text(json.dumps({'phase': 'no-state'}) + '\n')
        with self.assertRaisesRegex(ValueError, 'no phase or state'):
            bind_persisted_outcomes(malformed, {}, RETAINED)

    def test_restore_and_restart_must_bind_the_captured_platform_state(self):
        for phase, path, value, message in [
            (RESTORE_PHASE, ('fleet', 'redis-key-count'), '681', 'restored fleet equals the complete snapshot'),
            (RESTORE_PHASE, ('runtimeHashes', '/runtime/Hexalith.Works.dll'), 'd' * 64, 'restored runtime binaries'),
            (RESTORE_PHASE, ('convergence', 'receipt', 'EffectId'), 'OTHER', 'Platform-written receipt'),
            ('platform-historical-restart-replay-and-family', ('familyDigest',), 'e' * 64, 'persisted historical family'),
        ]:
            states = valid_states()
            container = states[phase]
            for part in path[:-1]:
                container = container[part]
            container[path[-1]] = value
            with self.subTest(message=message), self.assertRaisesRegex(ValueError, message):
                bind_persisted_outcomes(self.outcomes(states), {key: {} for key in ROWS}, RETAINED)

    def test_focused_projection_rebuild_is_a_required_r4_outcome(self):
        self.assertIn('platform-focused-projection-rebuild', [phase for phase, _ in PROOFS['R4']['persisted']])
        states = valid_states()
        del states['platform-focused-projection-rebuild']
        with self.assertRaisesRegex(ValueError, 'platform-focused-projection-rebuild'):
            bind_persisted_outcomes(self.outcomes(states), {key: {} for key in ROWS}, RETAINED)

    def test_every_row_maps_all_four_proofs_to_real_phases(self):
        self.assertEqual(set(PROOFS), set(ROWS))
        for key, proofs in PROOFS.items():
            self.assertEqual(set(proofs), set(PROOF_NAMES), key)
            for name, requirements in proofs.items():
                self.assertTrue(requirements, key + ' ' + name)
                self.assertTrue(all(fields for _, fields in requirements), key + ' ' + name)
        self.assertNotEqual(PROOFS['R9']['negative'][0][0], PROOFS['R9']['persisted'][0][0])

    def catalog(self, body, name='Directory.Packages.props', root=None):
        root = root or self.directory()
        path = root / name
        path.write_text('<Project><PropertyGroup>' + body + '</PropertyGroup></Project>')
        return path

    def test_catalog_selects_exactly_one_stable_public_sdk_version(self):
        selected = self.catalog("<HexalithEventStoreVersion Condition=\"'$(HexalithEventStoreVersion)' == ''\">3.125.1</HexalithEventStoreVersion>")
        self.assertEqual(selected_sdk_version(selected), VERSION)
        for body in ['', '<HexalithEventStoreVersion>3.125.1-story416.1</HexalithEventStoreVersion>',
                     '<HexalithEventStoreVersion>3.125.1</HexalithEventStoreVersion><HexalithEventStoreVersion>3.125.0</HexalithEventStoreVersion>',
                     '<HexalithEventStoreVersion>$(Other)</HexalithEventStoreVersion>']:
            with self.subTest(body=body), self.assertRaises(ValueError):
                selected_sdk_version(self.catalog(body))

    def test_works_consumer_default_pin_must_name_the_qualified_producer(self):
        pinned = self.catalog('<HexalithEventStoreVersion>3.125.1</HexalithEventStoreVersion>')
        self.assertEqual(require_consumer_pin(pinned, VERSION)['sha256'], sha256(pinned))
        for body in ['<HexalithEventStoreVersion>3.124.0</HexalithEventStoreVersion>', '']:
            with self.subTest(body=body), self.assertRaisesRegex(ValueError, 'differs from the Platform catalog'):
                require_consumer_pin(self.catalog(body), VERSION)

    def test_both_works_props_files_must_name_the_qualified_producer(self):
        pin = '<HexalithEventStoreVersion>3.125.1</HexalithEventStoreVersion>'
        root = self.directory()
        self.catalog(pin, 'Directory.Build.props', root)
        self.catalog(pin, 'Directory.Packages.props', root)
        self.assertEqual(set(require_consumer_pins(root, VERSION)), {'Directory.Build.props', 'Directory.Packages.props'})
        for name in ['Directory.Build.props', 'Directory.Packages.props']:
            changed = self.directory()
            for other in ['Directory.Build.props', 'Directory.Packages.props']:
                self.catalog('<HexalithEventStoreVersion>3.124.0</HexalithEventStoreVersion>' if other == name else pin, other, changed)
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, name):
                require_consumer_pins(changed, VERSION)

    def test_file_host_eventstore_packages_must_match_the_catalog(self):
        def host(*lines):
            path = self.directory() / 'apphost.cs'
            path.write_text('\n'.join(['#!/usr/bin/env dotnet', '#:sdk Aspire.AppHost.Sdk@13.6.1', '#:package Aspire.Hosting.Redis@13.6.1', *lines, 'var x = 1;']) + '\n')
            return path
        pinned = host('#:package Hexalith.EventStore.Aspire@3.125.1', '#:package Hexalith.EventStore.ServiceDefaults@3.125.1')
        self.assertEqual(require_host_package_pins(pinned, VERSION)['packages'], ['Hexalith.EventStore.Aspire', 'Hexalith.EventStore.ServiceDefaults'])
        for lines in [('#:package Hexalith.EventStore.Aspire@3.125.1', '#:package Hexalith.EventStore.ServiceDefaults@3.124.0'),
                      ('#:package Hexalith.EventStore.Aspire@3.125.2',), (),
                      ('#:package Hexalith.EventStore.Aspire@3.125.1', '#:package Hexalith.EventStore.Aspire@3.125.1')]:
            with self.subTest(lines=lines), self.assertRaises(ValueError):
                require_host_package_pins(host(*lines), VERSION)

    def test_contract_requires_every_row(self):
        self.assertEqual(set(ROWS), {f'R{index}' for index in range(1, 12)})

    def test_loaded_platform_runtime_must_equal_the_host_output_dll(self):
        root = self.directory()
        assembly, record = self.host_record(root, ha=True)
        self.assertEqual(len(self.verify(root, assembly, record)), 1)
        (root / 'bin/release/Hexalith.Platform.Works.Runtime.dll').write_bytes(b'consumer compile with another pdb path')
        with self.assertRaisesRegex(ValueError, 'host-output Hexalith.Platform.Works.Runtime.dll in works'):
            self.verify(root, assembly, record)
        host, consumer = root / 'host.dll', root / 'consumer.dll'
        host.write_bytes(b'same')
        consumer.write_bytes(b'same')
        self.assertTrue(require_identical_runtime(host, consumer)['identical'])
        consumer.write_bytes(b'different')
        with self.assertRaisesRegex(ValueError, 'Path-mapped deterministic builds still differ'):
            require_identical_runtime(host, consumer)

    def test_every_live_module_entry_must_be_a_prebuilt_runtime_artifact(self):
        root = self.directory()
        assembly, record = self.host_record(root, ha=True)
        for artifacts in [{}, {str(assembly): '0' * 64}, {str(root / 'other.dll'): sha256(assembly)}]:
            with self.subTest(artifacts=artifacts), self.assertRaisesRegex(ValueError, 'prebuilt runtime artifacts'):
                self.verify(root, assembly, record, runtime_artifacts=artifacts)

    def test_platform_target_must_run_at_platform_composition(self):
        root = self.directory()
        assembly, record = self.host_record(root, ha=True)
        self.assertIn('works-ha', self.verify(root, assembly, record)[0]['platformComposition'])
        mutations = {
            'missing guard': (lambda r: r.pop('compositionGuard'), 'did not record Platform'),
            'missing resource': (lambda r: r['compositionGuard'].pop('eventstore-admin'), 'composition for eventstore-admin'),
            'equal-value overlap': (lambda r: r['compositionGuard']['works']['fixtureKeys'].append('DOTNET_ENVIRONMENT'), 'DOTNET_ENVIRONMENT'),
            'unlisted overlap': (lambda r: r['compositionGuard']['works']['platformComposedKeys'].append('WORKS_SDK_PROOF'), 'WORKS_SDK_PROOF'),
            'wrong rule': (lambda r: r['compositionGuard']['works']['permittedOverlaps'][1].update(rule='captured-restorable-synthetic-secret'), 'DOTNET_STARTUP_HOOKS'),
            'phantom permission': (lambda r: r['compositionGuard']['eventstore']['permittedOverlaps'].append({'key': 'OTEL_EXPORTER_OTLP_ENDPOINT', 'rule': 'redirects-otlp-export-to-owned-loopback-collector'}), 'eventstore'),
            'production environment': (lambda r: r['compositionGuard']['eventstore-operations']['composition'].update(DOTNET_ENVIRONMENT='Production'), 'Development'),
            'composed limit differs from source': (lambda r: r['compositionGuard']['eventstore']['composition'].update(EventStore__RateLimiting__PermitLimit='100000'), 'differ from its source'),
            'composed callers differ from source': (lambda r: r['compositionGuard']['works']['composition'].pop('Authentication__Workload__AllowedCallers__1'), 'differ from its source'),
            'launched limit differs': (lambda r: r['effectiveEnvironment']['eventstore'].update(EventStore__RateLimiting__PermitLimit='100000'), 'launched effective environment of eventstore'),
            'launched extra caller': (lambda r: r['effectiveEnvironment']['works'].update(Authentication__Workload__AllowedCallers__2='works'), 'launched effective environment of works'),
            'replica flag differs': (lambda r: r['effectiveEnvironment']['works-ha'].update(Works__Recovery__UseSdkRuntime='false'), 'works-ha'),
            'in-process caller differs': (lambda r: r['hostingStartups']['works']['environment'].update(Authentication__Workload__AllowedCallers__0='works'), 'in-process effective environment of works'),
            'missing in-process record': (lambda r: r['hostingStartups']['eventstore'].pop('environment'), 'in-process environment: eventstore'),
            'missing launched environment': (lambda r: r['effectiveEnvironment'].pop('eventstore-operations'), 'effective environment of eventstore-operations'),
        }
        for name, (mutate, message) in mutations.items():
            changed = copy.deepcopy(record)
            mutate(changed)
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, message):
                self.verify(root, assembly, changed)

    def test_platform_composition_literals_are_read_from_platform_source(self):
        literals = platform_composition_literals(PREVIEW_MODEL)
        self.assertEqual({key: value for key, value in literals.items() if key.startswith('EventStore__RateLimiting__')},
                         {'EventStore__RateLimiting__PermitLimit': '8192', 'EventStore__RateLimiting__ConsumerPermitLimit': '8192',
                          'EventStore__RateLimiting__TenantPermitLimits__anonymous': '8192', 'EventStore__RateLimiting__ConsumerPermitLimits__anonymous': '8192'})
        self.assertEqual(literals['Authentication__Workload__AllowedCallers__1'], 'eventstore-admin')
        self.assertEqual(literals['Works__Recovery__UseSdkRuntime'], 'true')
        unreadable = self.directory() / 'WorksPreviewModel.cs'
        unreadable.write_text('builder.WithEnvironment("Other", "value");')
        with self.assertRaisesRegex(ValueError, 'could not be read'):
            platform_composition_literals(unreadable)
        conflicting = self.directory() / 'WorksPreviewModel.cs'
        conflicting.write_text(PREVIEW_MODEL.read_text() + '\n.WithEnvironment("Authentication__Workload__AllowedCallers__0", "works")')
        with self.assertRaisesRegex(ValueError, 'conflicting literals'):
            platform_composition_literals(conflicting)

    def test_alert_clear_must_follow_the_last_raise(self):
        check = alert_cleared_after_raise('eventstore.domain.recovery.alert')
        def point(value, at):
            return {'Name': 'eventstore.domain.recovery.alert', 'Value': value, 'UnixTimeNanoseconds': at}
        self.assertTrue(check([point(1, 10), point(0, 20)]))
        self.assertTrue(check([point(1, 10), point(0, 5), point(1, 15), point(0, 30)]))
        for points in [[point(0, 5), point(1, 10)], [point(1, 10), point(0, 20), point(1, 30)], [point(0, 20)], [point(1, 10)],
                       [{'Name': 'eventstore.domain.recovery.alert', 'Value': 0}, point(1, 10)], 'not points']:
            with self.subTest(points=points):
                self.assertFalse(check(points))

    def test_alert_raise_must_follow_the_observed_degraded_state(self):
        check = alert_raised_after('eventstore.domain.recovery.alert', 'points', 'degradedObservedAt')
        observed = '2026-10-19T07:00:05.1234567+00:00'
        def state(*points, at=observed):
            return {'degradedObservedAt': at, 'points': [{'Name': 'eventstore.domain.recovery.alert', 'Value': value, 'ReceivedAt': received}
                                                         | {'UnixTimeNanoseconds': utc_ticks(received) * 100} for value, received in points]}
        self.assertTrue(check(state((1, '2026-10-19T07:00:05.1234568+00:00'))))
        self.assertTrue(check(state((1, '2026-10-19T09:00:06+02:00'))))
        self.assertTrue(check(state((1, '2026-10-19T07:00:06Z'))))
        delayed = state((1, '2026-10-19T07:00:06Z'))
        delayed['points'][0]['UnixTimeNanoseconds'] = utc_ticks('2026-10-19T07:00:01Z') * 100
        self.assertFalse(check(delayed), 'A buffered startup export received later must not qualify')
        delayed['points'][0]['UnixTimeNanoseconds'] = utc_ticks(observed) * 100
        self.assertFalse(check(delayed), 'An export at the same observed tick must not qualify')
        del delayed['points'][0]['UnixTimeNanoseconds']
        self.assertFalse(check(delayed), 'A missing exporter timestamp must not qualify')
        early_receipt = state((1, '2026-10-19T07:00:01Z'))
        early_receipt['points'][0]['UnixTimeNanoseconds'] = utc_ticks('2026-10-19T07:00:06Z') * 100
        self.assertFalse(check(early_receipt), 'A late export with an early receipt must not qualify')
        float_export = state((1, '2026-10-19T07:00:06Z'))
        float_export['points'][0]['UnixTimeNanoseconds'] = float(utc_ticks('2026-10-19T07:00:06Z') * 100)
        self.assertFalse(check(float_export), 'Exporter time must be an integer, never a float')
        # True and False compare as 1 and 0 ns; only a pre-epoch observation lets the time order accept those values.
        pre_epoch = state((1, '1970-01-01T00:00:01Z'), at='1969-12-31T23:59:59Z')
        for export_time, qualifies in [(1, True), (0, True), (True, False), (False, False)]:
            pre_epoch['points'][0]['UnixTimeNanoseconds'] = export_time
            with self.subTest(export_time=export_time):
                self.assertEqual(check(pre_epoch), qualifies, 'Exporter time must be an integer, never a boolean')
        for name, refused in [
                ('startup point only', state((1, '2026-10-19T07:00:01+00:00'))),
                ('same tick', state((1, observed))),
                ('one tick earlier', state((1, '2026-10-19T07:00:05.1234566+00:00'))),
                ('earlier in another offset', state((1, '2026-10-19T09:00:05+02:00'))),
                ('later clear only', state((1, '2026-10-19T07:00:01+00:00'), (0, '2026-10-19T07:00:06+00:00'))),
                ('unreceived point', {'degradedObservedAt': observed, 'points': [{'Name': 'eventstore.domain.recovery.alert', 'Value': 1}]}),
                ('another gauge', {'degradedObservedAt': observed, 'points': [{'Name': 'eventstore.domain.recovery.ready', 'Value': 1, 'ReceivedAt': '2026-10-19T07:00:06Z'}]}),
                ('no observation', state((1, '2026-10-19T07:00:06Z'), at=None)),
                ('offsetless observation', state((1, '2026-10-19T07:00:06Z'), at='2026-10-19T07:00:05')),
                ('malformed observation', state((1, '2026-10-19T07:00:06Z'), at='yesterday')),
                ('points list', [{'Name': 'eventstore.domain.recovery.alert', 'Value': 1, 'ReceivedAt': '2026-10-19T07:00:06Z'}])]:
            with self.subTest(name=name):
                self.assertFalse(check(refused))
        self.assertEqual(utc_ticks('2026-10-19T07:00:05.1234567+00:00') - utc_ticks('2026-10-19T07:00:05Z'), 1234567)
        self.assertEqual(utc_ticks('2026-10-19T09:00:05.5+02:00'), utc_ticks('2026-10-19T07:00:05.5000000Z'))
        for malformed in [None, 5, '', '2026-10-19', '2026-10-19T07:00:05', '2026-10-19T07:00:05.12345678Z', '2026-13-19T07:00:05Z']:
            with self.subTest(malformed=malformed):
                self.assertIsNone(utc_ticks(malformed))
        states = valid_states()
        states['disabled-recovery-otlp-alert-export']['points'].pop()
        with self.assertRaisesRegex(ValueError, 'R2 negative proof: disabled-recovery-otlp-alert-export .*does not qualify'):
            bind_persisted_outcomes(self.outcomes(states), {'R2': {}}, RETAINED)

    def test_supporting_classes_include_every_deterministic_admission_class(self):
        # Every class is pinned: a class dropped from this tuple runs in no admission lane.
        self.assertEqual(SUPPORTING_CLASSES, (
            'WorksSdkPartitionedRebuildTests', 'WorksSdkProjectionQueryTests', 'WorkItemSharedProjectionRebuildHandlerTests',
            'WorksSdkProtectedHttpTests', 'WorksQueryPolicyDeadlineTests', 'WorksSdkProofRedisOwnershipTests',
            'WorksPlatformDispositionProbeTests', 'WorksPlatformSelectedSdkPathsTests', 'WorksPlatformFleetRestoreTests',
            'WorksAppHostDiscoveryIsolationTests', 'WorksPlatformHostingCompositionTests', 'WorksPlatformTelemetryCollectorTests',
            'WorksPlatformIdentityFixtureTests', 'WorksPlatformCompositionGuardTests', 'WorksAppHostSelectionTests',
            'WorksSdkPackageContractTests'))

    def test_query_identity_must_be_a_resolved_delegated_executor(self):
        self.assertTrue(delegated_identity(identity()))
        for key, value in [('isDelegated', False), ('authenticatedWorkloadId', 'works'), ('userId', 'workload:eventstore'), ('originalActorId', ''),
                           ('partyId', 'party-416-other'), ('tenant', 'foreign'), ('identityCalls', 0), ('identityCalls', True), ('resolvedActor', 'other')]:
            with self.subTest(key=key, value=value):
                self.assertFalse(delegated_identity(identity() | {key: value}))
        self.assertFalse(delegated_identity(None))

    def test_retained_rollback_must_use_the_snapshot_scheduler_volume(self):
        rows = {key: {} for key in ROWS}
        self.assertTrue(bind_persisted_outcomes(self.outcomes(valid_states()), rows, RETAINED)['complete'])
        for starts in [[{'role': 'retained-rollback', 'schedulerVolume': 'works-sdk416-other'}], [{'role': 'retained-rollback'}],
                       [*RETAINED, {'role': 'retained-rollback', 'schedulerVolume': 'works-sdk416-other'}]]:
            with self.subTest(starts=starts), self.assertRaisesRegex(ValueError, "snapshot's Scheduler volume"):
                bind_persisted_outcomes(self.outcomes(valid_states()), {key: {} for key in ROWS}, starts)
        states = valid_states()
        del states[SNAPSHOT_PHASE]['schedulerVolume']
        with self.assertRaisesRegex(ValueError, "snapshot's Scheduler volume"):
            bind_persisted_outcomes(self.outcomes(states), {key: {} for key in ROWS}, RETAINED)

    def test_restored_fleet_is_an_independent_re_observation_of_the_snapshot(self):
        snapshot = {'certificates/ca.crt': 'b' * 64, 'redis-key-count': '682', DUMP_INVENTORY: 'd' * 64}
        self.assertTrue(restored_fleet_matches({'certificates/ca.crt': 'b' * 64, 'redis-key-count': '682'}, snapshot))
        for restored in [dict(snapshot), {'certificates/ca.crt': 'b' * 64}, {'certificates/ca.crt': 'b' * 64, 'redis-key-count': '681'},
                         {'certificates/ca.crt': 'b' * 64, 'redis-key-count': '682', 'extra': 'e' * 64}, 'fleet']:
            with self.subTest(restored=restored):
                self.assertFalse(restored_fleet_matches(restored, snapshot))
        self.assertFalse(restored_fleet_matches({'redis-key-count': '682'}, {'redis-key-count': '682'}))

    def test_every_integration_test_source_is_a_bound_fixture(self):
        works = self.directory()
        root = works / 'tests/Hexalith.Works.IntegrationTests'
        for relative in ['WorksSdkProofResponseHandler.cs', 'Nested/WorksSdkProofProducer.cs', 'bin/Release/Generated.cs', 'obj/Release/Generated.cs', 'notes.md']:
            (root / relative).parent.mkdir(parents=True, exist_ok=True)
            (root / relative).write_text('// ' + relative)
        (works / 'src/Hexalith.Works.AppHost').mkdir(parents=True)
        (works / 'src/Hexalith.Works.AppHost/ProjectMetadataPaths.cs').write_text('// paths')
        self.assertEqual(sorted(fixture_sources(works)), ['src/Hexalith.Works.AppHost/ProjectMetadataPaths.cs',
                                                          'tests/Hexalith.Works.IntegrationTests/Nested/WorksSdkProofProducer.cs',
                                                          'tests/Hexalith.Works.IntegrationTests/WorksSdkProofResponseHandler.cs',
                                                          'tests/Hexalith.Works.IntegrationTests/notes.md'])

    def test_fixture_projects_runner_helpers_and_copied_resources_are_bound(self):
        works = self.directory() / 'bin/obj/works'
        names = ['tests/Hexalith.Works.IntegrationTests/Hexalith.Works.IntegrationTests.csproj',
                 'tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/event.json',
                 'tests/Hexalith.Works.IntegrationTests/xunit.runner.json',
                 'tests/Hexalith.Works.Testing/WorkItemStateBuilder.cs',
                 'tests/Hexalith.Works.Testing/Hexalith.Works.Testing.csproj', 'xunit.runner.json']
        for name in [*names, 'tests/Hexalith.Works.Testing/obj/Generated.cs',
                     'tests/Hexalith.Works.IntegrationTests/bin/copied.json']:
            path = works / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('original fixture')
        baseline = fixture_sources(works)
        self.assertEqual(set(baseline), set(names))
        for name in names:
            path = works / name
            for change in ('mutation', 'removal'):
                with self.subTest(name=name, change=change):
                    if change == 'removal':
                        path.unlink()
                    else:
                        path.write_text('changed fixture')
                    with self.assertRaisesRegex(ValueError, 'Qualified fixtures changed'):
                        require_unchanged_fixture_sources(works, baseline)
                    path.write_text('original fixture')
        for name in ('tests/Hexalith.Works.Testing/Added.cs',
                     'tests/Hexalith.Works.IntegrationTests/SchemaEvolution/Golden/Added.json'):
            path = works / name
            path.write_text('new input')
            with self.assertRaisesRegex(ValueError, 'Qualified fixtures changed'):
                require_unchanged_fixture_sources(works, baseline)
            path.unlink()
        require_unchanged_fixture_sources(works, baseline)

    def test_fixture_inventory_refuses_linked_source_files_and_directories(self):
        works = self.directory()
        root = works / 'tests/Hexalith.Works.IntegrationTests'
        root.mkdir(parents=True)
        outside = self.directory()
        (outside / 'Fixture.cs').write_text('// linked fixture')
        for kind in ('file', 'directory'):
            link = root / ('Linked.cs' if kind == 'file' else 'Linked')
            target = outside / 'Fixture.cs' if kind == 'file' else outside
            with self.subTest(kind=kind):
                link.symlink_to(target, target_is_directory=kind == 'directory')
                with self.assertRaisesRegex(ValueError, 'symbolic link'):
                    fixture_sources(works)
                link.unlink()

    def test_fixture_inventory_refuses_additions_removals_and_changed_bytes(self):
        works = self.directory()
        root = works / 'tests/Hexalith.Works.IntegrationTests'
        root.mkdir(parents=True)
        fixture = root / 'Fixture.cs'
        fixture.write_text('// original fixture')
        resolver = works / 'src/Hexalith.Works.AppHost/ProjectMetadataPaths.cs'
        resolver.parent.mkdir(parents=True)
        resolver.write_text('// paths')
        baseline = fixture_sources(works)
        require_unchanged_fixture_sources(works, baseline)
        added = root / 'AddedBeforeCompilation.cs'
        added.write_text('// compiled without an original binding')
        with self.assertRaisesRegex(ValueError, 'pre-command baseline: .*AddedBeforeCompilation.cs'):
            require_unchanged_fixture_sources(works, baseline)
        added.unlink()
        fixture.unlink()
        with self.assertRaisesRegex(ValueError, 'pre-command baseline: .*Fixture.cs'):
            require_unchanged_fixture_sources(works, baseline)
        fixture.write_text('// changed fixture')
        with self.assertRaisesRegex(ValueError, 'pre-command baseline: .*Fixture.cs'):
            require_unchanged_fixture_sources(works, baseline)
        fixture.write_text('// original fixture')
        require_unchanged_fixture_sources(works, baseline)

    def test_published_producer_loop_binds_the_consumer_dll_to_its_archive(self):
        root = self.directory()
        cache = root / 'packages'
        consumer = root / 'consumer'
        consumer.mkdir()
        package = 'Hexalith.EventStore.Client'
        restored = VERSION
        relative = package.lower() + '/' + restored
        folder = cache / relative
        folder.mkdir(parents=True)
        artifact = folder / (package.lower() + '.' + restored + '.nupkg')
        entry = 'lib/net10.0/' + package + '.dll'
        contents = b'official-package-assembly'
        commit = 'b' * 40
        repository = 'https://github.com/Hexalith/Hexalith.EventStore'
        with zipfile.ZipFile(artifact, 'w') as archive:
            archive.writestr(package + '.nuspec',
                             '<package><metadata><repository url="' + repository + '" commit="' + commit + '" /></metadata></package>')
            archive.writestr(entry, contents)
        (folder / '.nupkg.metadata').write_text(json.dumps({'source': 'https://api.nuget.org/v3/index.json'}))
        assets = root / 'project.assets.json'
        assets.write_text(json.dumps({'libraries': {
            package + '/' + restored: {'type': 'package', 'path': relative, 'sha512': 'restore-sha512'},
            'Hexalith.EventStore.Source/' + restored: {'type': 'project'},
            'Other.Package/1.0.0': {'type': 'package'}}, 'packageFolders': {str(cache): {}}}))
        assembly = consumer / (package + '.dll')
        assembly.write_bytes(contents)
        self.assertEqual(published_producers(assets, consumer, restored), {package: {
            'version': restored, 'sha256': sha256(artifact), 'source': 'https://api.nuget.org/v3/index.json',
            'nugetSha512': 'restore-sha512', 'repositoryCommit': commit, 'repositoryUrl': repository,
            'consumerAssemblySha256': sha256(assembly), 'archiveAssemblyEntry': entry}})
        assembly.write_bytes(b'altered-extracted-consumer-dll')
        with self.assertRaisesRegex(ValueError, 'Consumer assembly differs from its selected public archive'):
            published_producers(assets, consumer, restored)

    def test_extracted_package_assembly_must_match_the_selected_archive(self):
        root = self.directory()
        assembly = root / 'Hexalith.EventStore.Client.dll'
        assembly.write_bytes(b'published-assembly')
        artifact = root / 'package.nupkg'
        with zipfile.ZipFile(artifact, 'w') as archive:
            archive.writestr('lib/net10.0/' + assembly.name, assembly.read_bytes())
        bound = require_archive_assembly(artifact, assembly)
        self.assertEqual(bound['consumerAssemblySha256'], sha256(assembly))
        self.assertEqual(bound['archiveAssemblyEntry'], 'lib/net10.0/' + assembly.name)
        assembly.write_bytes(b'modified-extracted-assembly')
        with self.assertRaisesRegex(ValueError, 'differs from its selected public archive'):
            require_archive_assembly(artifact, assembly)

    def test_canonical_inventory_binds_full_revision_and_ordinary_test_changes(self):
        root = self.directory()
        fixture = root / 'tests' / 'fixture.cs'
        fixture.parent.mkdir()
        fixture.write_text('original fixture')
        helper = root / 'eng' / 'helper.py'
        helper.parent.mkdir()
        helper.write_text('helper')
        head = 'a' * 40
        names = b'tests/fixture.cs\0eng/helper.py\0_bmad-output/evidence.json\0'
        with patch('verify_works_host.subprocess.check_output', side_effect=[names, head + '\n', names, head + '\n']):
            before = verify_works_host.canonical_source_snapshot(root)
            fixture.write_text('changed fixture')
            after = verify_works_host.canonical_source_snapshot(root)
        self.assertEqual(before['commit'], head)
        self.assertEqual(set(before['sourceFiles']), {'tests/fixture.cs', 'eng/helper.py'})
        self.assertNotEqual(before['inventorySha256'], after['inventorySha256'])
        self.assertNotEqual(before['sourceFiles']['tests/fixture.cs'], after['sourceFiles']['tests/fixture.cs'])

    def test_fixture_inventory_is_bound_before_the_first_command(self):
        works = self.directory()
        artifacts = self.directory() / 'evidence'
        args = type('Args', (), {'works_root': works, 'runtime_sdk_root': None, 'artifacts_path': artifacts, 'prepare_only': False})()
        snapshot = {'root': str(works), 'commit': 'c', 'sourceFiles': {}, 'inventorySha256': 'i'}
        fixtures = {'tests/Hexalith.Works.IntegrationTests/fixture.cs': DIGEST}
        def command(*unused, **kwargs):
            self.assertEqual(json.loads((artifacts / 'ledger.json').read_text())['fixtureSources'], fixtures)
            raise RuntimeError('stopped after checking the pre-command inventory')
        with patch('verify_works_host.selected_sdk_version', return_value=VERSION), \
                patch('verify_works_host.require_consumer_pins', return_value={}), \
                patch('verify_works_host.require_host_package_pins', return_value={}), \
                patch('verify_works_host.source_snapshot', return_value=snapshot), \
                patch('verify_works_host.fixture_sources', return_value=fixtures), \
                patch('verify_works_host.canonical_source_snapshot', return_value=snapshot), \
                patch('verify_works_host.linked_fixture_snapshot', return_value={}), \
                patch('verify_works_host.sha256', return_value=DIGEST), \
                patch('verify_works_host.run_owned', side_effect=command):
            self.assertEqual(verify_works_host.run_verification(args), 1)
        self.assertIn('pre-command inventory', json.loads((artifacts / 'ledger.json').read_text())['failure'])

    def test_unexpected_failure_types_are_recorded_in_the_ledger(self):
        snapshot = {'commit': 'c', 'sourceFiles': {}, 'inventorySha256': 'i'}
        cases = [
            ('command', {'run': KeyError('unexpected-ledger-key')}, "KeyError: 'unexpected-ledger-key'"),
            ('command', {'run': RuntimeError('unexpected runtime failure')}, 'RuntimeError: unexpected runtime failure'),
            ('command', {'run': http.client.IncompleteRead(b'partial', 10)}, 'IncompleteRead: IncompleteRead(7 bytes read, 10 more expected)'),
            # The inventory before the first command is recorded too, not only failures inside a command.
            ('inventory', {'snapshot': RuntimeError('unreadable consumer source')}, 'RuntimeError: unreadable consumer source'),
            ('selection', {'version': RuntimeError('unreadable catalog')}, 'RuntimeError: unreadable catalog'),
        ]
        for stage, failures, expected in cases:
            works = self.directory()
            artifacts = self.directory() / 'evidence'
            args = type('Args', (), {'works_root': works, 'runtime_sdk_root': None, 'artifacts_path': artifacts, 'prepare_only': False})()
            with self.subTest(stage=stage, expected=expected), \
                    patch('verify_works_host.selected_sdk_version', return_value=VERSION, side_effect=failures.get('version')), \
                    patch('verify_works_host.require_consumer_pins', return_value={}), \
                    patch('verify_works_host.require_host_package_pins', return_value={}), \
                    patch('verify_works_host.source_snapshot', return_value=snapshot | {'root': str(works)}, side_effect=failures.get('snapshot')), \
                    patch('verify_works_host.fixture_sources', return_value={}), \
                    patch('verify_works_host.canonical_source_snapshot', return_value=snapshot | {'root': str(works)}), \
                    patch('verify_works_host.linked_fixture_snapshot', return_value={}), \
                    patch('verify_works_host.sha256', return_value=DIGEST), \
                    patch('verify_works_host.run_owned', side_effect=failures.get('run', AssertionError('no command expected'))):
                self.assertEqual(verify_works_host.run_verification(args), 1)
                ledger = json.loads((artifacts / 'ledger.json').read_text())
                self.assertEqual(ledger['failure'], expected)
                self.assertEqual(ledger['status'], 'incomplete')


if __name__ == '__main__':
    unittest.main()
