# SDK boundary verification

Five historical consumer findings are reconciled in the [verification spec](../_bmad-output/implementation-artifacts/spec-five-deferred-sdk-boundary-verification.md). Their original ledger evidence is retained.

| Historical finding | Current boundary and qualification |
| --- | --- |
| Three terminal handlers unpinned | SDK host registration resolves the concrete Works wakeup for cancellation, expiry and completion. Explicit delivery/redelivery controls reconstruct persisted SDK source membership without advancing its process checkpoint and verify the exact reminder target. |
| Topology testing builders undisposed | The retired topology test no longer exists. Every successful shared-harness builder acquisition enters its cleanup boundary before Platform capture or fixture callbacks. Application and builder disposal retain their bounded waits and primary-failure reporting. |
| Background store reads contaminate denial checks | QueryWindowStore attributes operations to the actual HTTP request using AsyncLocal scope. A gated identity denial overlaps 32 post-start background reads and stays at zero; a subsequent authorized HTTP query positively counts store reads. |
| Actor handlers absent in SDK mode | The real canonical host advertises WorksSdkReminderActor. Actor method and reminder callback endpoints require PUT and the SDK sidecar-channel policy. Its actor deactivation endpoint requires the app-channel token and dispatches a valid synthetic request; bare unprotected actor endpoints also fail startup inventory validation. |
| Retryable delivery failures survive a healthy replay | The SDK journal owns the attempt count for each immutable delivery. A synthetic transient fold failure followed by a successful real Works fold removes pending failure evidence; identical receipt replay does not refold or change selected documents. A new delivery starts its own failure count at one. |

The replay fault is injected at the SDK fold seam; it does not demonstrate a supported mutation of immutable stream bytes or change the handling of unknown events. Terminal parking still requires the existing covering authoritative rebuild and explicit SDK conflict resolution described in [reminder/process adoption](sdk-reminder-process-adoption.md). The actor check is an in-process HTTP route proof with a synthetic unused actor, without scheduler, persistence or mTLS transport qualification. These deterministic consumer checks do not replace Platform live parity or production admission.
