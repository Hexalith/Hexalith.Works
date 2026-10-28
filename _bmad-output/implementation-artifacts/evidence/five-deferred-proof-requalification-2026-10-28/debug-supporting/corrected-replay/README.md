# Corrected isolated Debug replay

The corrected `replay.py` was executed end to end in `/tmp/works-five-proof-debug-corrected-replay-2`; its driver exited **0**. It builds two independent Debug outputs from current sources with explicit source-mode flags and explicit paths to the root-declared EventStore and PolymorphicSerializations dependencies. It executes no preexisting ordinary assembly.

The isolated baseline build passed with **0 warnings/errors**. Its complete sixteen-class supporting selection passed **345/345, 0 failed, 0 skipped, 0 not-run**, in **140.471s**. The exact selected existing identity method also passed **1/1** in a separate invocation. The separately isolated mutant build passed with **0 warnings/errors**; the same method then failed **1/1**, exit **1**, on the injected `#if DEBUG` marker `controlled-debug-runtime-mutation`.

The complete ordinary Integration output inventory is byte-for-byte equal before and after replay: **562 file/directory entries**. Both inventories and their full file SHA-256 hashes are retained. No ordinary output was executed or rebuilt by replay. Three parse controls preserve apostrophes, double quotes, and combined quote/XML metacharacters in generated target paths; the target now uses ElementTree's XML writer.

`replay.py.txt` is the exact executed script and matches `inputs.json`'s full SHA-256. Driver/build/test commands, results, XML, compressed logs, complete runtime manifests, mutation inputs and aggregate bindings are retained. `selected-source-and-build-observation.json` records source/build bytes observed at retention; the fresh build commands establish the replay inputs without claiming a separate whole-source qualification protocol. The refused first source-root invocation remains under `failed-root-propagation-attempt/` and provides no passing test claim.

This focused replay assesses Debug supporting coverage and mutation sensitivity. It changes no upstream canonical admission requirement and claims no fresh complete Platform parity run.
