---
title: Observe the real Operations bootstrap refusal across host shutdown
type: bugfix
created: '2026-11-02'
status: done
route: oneshot
baseline_commit: 75355303691b56d904d69a3996c5a0af03f3de08
---

<frozen-after-approval reason="User authorized CI repairs preserving runtime guards and waived human validation">

## Intent

The real unsupported MVC-only Operations bootstrap correctly stops its host, but CI job125014587420 races that stop: WebApplicationFactory.CreateClient observes a disposed IServiceProvider before the test captures the receiver and stopping token. Capture those real objects before startup. Permit only that specific composite-startup transport race, then require the original exact receiver refusal, actual host stopping and absence of observation/activation/commit artifacts. Preserve the supported finite profile and all existing policy assertions. No SDK runtime or production API changes, generic exception acceptance, fake stop signal, skipped test or fabricated receiver capability.

</frozen-after-approval>

## Implementation Notes

Owner /tmp/works-sdk-compat-qualification-20261101; preserve its seven unrelated dirty physical peer gitlinks. Change only OperationsBootstrapFactory and DevelopmentOperationsBootstrapTests. Retain normal host build/start behavior and acquire the real registered receiver/lifetime before starting. Verify both theory rows and the full109-case Operations assembly against Release outputs, with zero skips, and repeat the focused pair to exercise startup ordering. Retain exact commands/logs/XML and failed attempts. Use one blind review under the stable build workflow; parent handles source commit, push and fresh hosted checks. The failing CI case is ActualBootstrapKeepsTheSupportedFiniteProfileAndRefusesMvcOnlyComposite(composite:true), System.ObjectDisposedException ObjectName=IServiceProvider in WebApplicationFactory.ConfigureHostBuilder, before existing refusal assertions. Source head7535530; no native qualification claim follows local fixture tests.

## Review Triage Log

- blind-1: medium, patch. The first filter matched any IServiceProvider disposal from CreateClient, while the permitted race is the framework TestServer lookup. Require that exact first non-DI caller and retain proof/stop/artifact checks; independently require unrelated provider-disposal errors to propagate.
- blind-2: medium, patch. Repeating ordinary startup did not force the new disposal branch. Add a schedule that waits for the actual SDK-requested stop and disposes before factory lookup, preserving the supported and ordinary composite cases and all refusal assertions. No new review loop is required for these direct patches.

## Final verification

Both review findings were patched directly. The Release build is clean. All111 Operations tests pass with zero skips, including the original supported/default-refusal rows, a forced real-stop-before-TestServer-lookup row and an unrelated-disposal propagation control. Eight repeated four-case focused runs also pass (32/32). No production files changed and all seven pre-existing dirty peer gitlinks remain unstaged. Evidence: Works .artifacts/upgrade-release-orchestration-20261101/sdk-operations-bootstrap-race/. Fresh hosted checks are required for the resulting head.
