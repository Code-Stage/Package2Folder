# Release test contract

The wrapper loads this repository as a testable local package. Graphics stay enabled because Unity 6.5+ native import preparation needs a graphics device. The runner copies tracked root-package inputs without Git metadata or scratch.

The native-import suite retains the existing valid, silent-import and prepared-window cancellation controls. It also requires a damaged archive to fail without importing an asset, followed by a successful valid retry. On Unity 6.5+, a separate case cancels the native window before Package2Folder redirects it, while `nativeImportPending` is true, checks pending state and update-handler cleanup, then retries. This case is compiled only on editors with the new native preparation route; it does not become a skipped minimum-editor case. It does not test aborting decompression before a native window exists.

Signed-valid and invalid-signature cases require a retained signed artifact with provenance. The generated damaged archive tests decompression failure, not signature validation. A real Asset Store round trip remains separate acceptance. These changes need fresh runtime evidence; the earlier ten-case results do not prove the added checks.

Run and verify the shared matrix from this repository before a full release. Use a frozen checkout of [UAS-Common 5bdb5f4](https://github.com/Code-Stage/UAS-Common/tree/5bdb5f4b42658ddef92f8ddbb0041149de9819f6/ai-agent-guides/plugins/uas-shared/skills/ship-asset) or a later reviewed revision throughout testing, upload and finalization. The runner records its exact revision and file hashes; changing it invalidates retained evidence.

The configured minimum is 2022.3. Resolve the latest stable stream from the official archive for each release; do not substitute an installed patch or preview. A new baseline without a verified version-project mapping blocks execution.

This ship.config supplies matrix and branch settings only. Export/upload setup is not configured by this change.
