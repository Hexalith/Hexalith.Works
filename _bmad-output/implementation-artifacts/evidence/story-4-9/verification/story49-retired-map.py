import json,re,subprocess,xml.etree.ElementTree as E
from pathlib import Path
w=Path('/home/administrator/projects/hexalith/works');dest=w/'_bmad-output/implementation-artifacts/evidence/story-4-9/verification';dest.mkdir(parents=True,exist_ok=True)
xmls=[Path('/tmp/story49-integration-final2.xml'),Path('/tmp/story49-architecture-final.xml'),Path('/tmp/story49-platform-cutover-focused.xml')]
xmls+=list(Path('/tmp/story49-pre-cutover-20261020-isolated').glob('*.xml'))
passed={}
for p in xmls:
 for t in E.parse(p).findall('.//test'):
  if t.get('result')=='Pass': passed.setdefault(t.get('type')+'.'+t.get('method'),[]).append(str(p))
def named(class_name,method):
 hits=[k for k in passed if k.endswith('.'+class_name+'.'+method)]
 if len(hits)!=1:raise ValueError((class_name,method,hits))
 return hits[0]
query=[named('WorksSdkProjectionQueryTests',m) for m in ['Verified_executor_sees_own_assigned_and_tenant_queued_but_not_foreign_assigned','Wrong_actor_tenant_and_origin_are_denied_before_any_Works_read','Capture_stages_complete_family_and_commit_changes_stale_selection_to_current','ConcurrentNamedDeliveriesForTwoAggregatesSurviveRebuildPromotionRestartAndRedelivery','Restart_recovers_checkpoint_and_rejects_conflicting_redelivery']]
callback=named('WorksSdkRecoveryPipelineSmokeTests','SuccessfulCallbacksRegisterSdkReminderAndProcessWithoutPeriodicRecovery')
recovery=named('WorksSdkRecoveryPipelineSmokeTests','SdkRemindersAndCascadeConvergeThroughPersistedReceiptsAcrossRestart')
historical=named('WorksPlatformHistoricalPipelineSmokeTests','PlatformLifecycleTreeClaimFilteringAndSecuritySurviveRestart')
ha=named('WorksPlatformHaPipelineSmokeTests','TwoWorksHostsRecoverLostFiringAfterPrimaryStopsWithoutDuplicateEffect')
projection=named('WorksPlatformProjectionPipelineSmokeTests','ActualPlatformSharedRebuildAndConcurrentCatchUpPreserveCompleteFamily')
intent=[named('WorksSdkIntentTranslationTests',m) for m in ['AllCurrentDatesAndExpiryHaveCommittedWitnesses','MixedAwaitSuspensionEmitsOnlyExactDateAndIndependentExpiry','ChildResumeRequiresCurrentExactAwaitWhileSdkOwnsReceiptReplay','ExpiredParentMapsChildrenToCascadeExpiry','TerminalTranslationUsesEnvelopeAndRetainsPlanAfterChildTerminates']]
catalog=[named('WorksSdkRecoverySourceTests',m) for m in ['ProcessCatalogReturnsHealthyMembershipAndIncompleteDegradedScopeAfterRestart','TombstoneOutageAllowsHealthyMembershipAndDurableRecoveryAcrossRestart','MissingFamilyMembershipRetainsIncompleteDiscovery']]
composition=[named('WorksSdkRecoveryCompositionTests',m) for m in ['SdkTransportsShareValidatedEffectiveAddress','SdkCapacityOverrideRequiresCoupledBudgets','DefaultCompositionSelectsSdkProjectionAndRecovery','LegacyRuntimeSelectionIsRefused','ForeignWakeupIdentityIsRefusedBeforeDiscovery','WakeupPreservesSdkQuarantineAndCheckpoint']]
guards=[named('PlatformHostingBoundaryTests',m) for m in ['WorksDoesNotOwnTopologyOrServiceDefaults','ReintroducedHostingProjectsAreDetected','WorksDoesNotImplementGenericRuntimeMechanics']]
paths=subprocess.check_output(['git','-C',str(w),'diff','--name-only','--diff-filter=D','--','tests']).decode().splitlines()
records=[]
for p in paths:
 old=subprocess.check_output(['git','-C',str(w),'show','HEAD:'+p]).decode();name=Path(p).stem
 if 'Recording' in name:category='Removed helper for the retired local scheduler/submitter; typed SDK transport behavior is exercised.';rep=composition+intent
 elif 'Rollback' in name:category='Complete-fleet rollback is executed through the separately qualified historical topology.';rep=[recovery]
 elif any(s in name for s in ['AppHostTopology','CommandPipeline']):category='Topology ownership and historical lifecycle/security move to Platform.';rep=guards+[historical,ha]
 elif 'DomainEvent' in name:category='Canonical SDK typed event ingress, callback discovery, conflict quarantine and redelivery replace the local processor/subscription.';rep=[callback,recovery]+query[-1:]
 elif 'RecoveryOptions' in name:category='SDK-only selection, coupled capacity and shared trusted/ordinary transport configuration replace inactive local runtime options.';rep=composition
 elif any(s in name for s in ['Query','ReadModelGeneration','ProjectionQuery']):category='Canonical SDK generation selection, tenant/executor filtering, no-disclosure authorization and restart replace the local query/dispatcher fixtures.';rep=query+[historical,projection]
 elif 'IndexDispatcher' in name:category='SDK named projection publishes complete family and recovery membership; no local dispatcher remains.';rep=query[-3:]+catalog+[callback]
 elif any(s in name for s in ['Date','Reminder','Pending']):category='Works date/expiry intent and fail-closed discovery remain covered; SDK owns scheduling, retries, serialization and lost-firing recovery.';rep=intent[:2]+catalog+[callback,recovery,ha]
 else:category='Works cascade/child-resume translation and contextual source safety remain covered; SDK owns paging/checkpoints/fences, reissue and receipt replay.';rep=intent[2:]+catalog+[recovery]
 records.append({'retiredFile':p,'retiredPublicTestMethods':re.findall(r'public\s+(?:async\s+)?(?:Task|void)\s+(\w+)\(',old),'reason':category,'replacementTests':list(dict.fromkeys(rep))})
partial=[('WorkItemSharedProjectionRebuildHandlerTests.cs',['Committed_reconciliation_survives_later_ordinary_dispatches_of_the_reconciled_streams','Later_child_spawn_merges_with_reconciled_children_instead_of_replacing_them','Dispatch_after_commit_admits_a_new_aggregate_to_the_current_generation'],query[-3:]+[projection]),('WorksSdkRecoverySourceTests.cs',['Pending_date_source_discovers_sdk_tenants_and_uses_stream_truth_over_pinned_index','Pending_date_source_reports_partial_sdk_candidate_failure_without_losing_healthy_stream','Pending_date_source_does_not_borrow_parking_from_an_older_generation','Cascade_source_uses_parent_stream_and_one_sdk_generation_for_child_terminality','Cascade_source_treats_missing_current_rollup_as_active_even_when_old_generation_was_terminal','Pending_date_source_reports_incomplete_when_restarted_sdk_family_has_conflict_or_orphan_parking','Cascade_source_keeps_terminal_child_active_when_restarted_sdk_family_has_conflict_or_orphan_parking'],catalog+query[-1:]+[recovery]),('WorksPlatformSelectedSdkPathsTests.cs',['ExplicitArchiveRootSelectsTheMatchingSdkEntryPoint','InvalidExplicitSelectionRefusesFallback','DefaultDeclaredSdkPathIsPreserved'],[recovery])]
for file,methods,rep in partial:records.append({'retiredFile':'tests/Hexalith.Works.IntegrationTests/'+file,'retiredMethodsOnly':methods,'reason':'Retired implementation-specific entry points; preserved domain/data compatibility and current SDK/Platform behavior are tested. Historical path selection is separately bound by the archive verifier.','replacementTests':rep})
used=set(k for r in records for k in r['replacementTests'])
result={'scope':'Behavior replacements for removed integration fixtures, not claims that removed private implementation types still exist. Compatibility rebuild/fold tests remain explicitly on the historical data format, while active host legacy flags fail closed.','records':records,'executedReplacementEvidence':{k:passed[k] for k in sorted(used)},'archiveRefusalTests':['test_retained_works_host.RetainedWorksHostTests.'+n for n in ['test_archive_and_historical_git_blob_substitution_refuse','test_unsafe_or_duplicate_members_refuse_even_with_an_updated_archive_digest','test_later_source_changes_additions_and_symlinks_refuse','test_later_dependency_substitution_refuses']]}
(dest/'retired-test-replacements.json').write_text(json.dumps(result,indent=2)+'\n');print(len(records),'retired source/method groups mapped to',len(used),'actually passing replacement methods')
