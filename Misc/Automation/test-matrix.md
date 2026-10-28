# Release test contract

The wrapper loads this repository as a testable local package and keeps the existing native-import coroutine fixtures. Graphics stay enabled because Unity 6.5+ native import preparation needs a graphics device. The runner copies tracked root-package inputs without copying Git metadata or scratch. Source, tampered, signed and Asset Store round-trip fixtures described in UAS-Common#73 remain separate supplemental acceptance; the ten existing cases do not prove those paths.

Run and verify the shared matrix from this repository before a full release. Use a frozen checkout of [UAS-Common 5bdb5f4](https://github.com/Code-Stage/UAS-Common/tree/5bdb5f4b42658ddef92f8ddbb0041149de9819f6/ai-agent-guides/plugins/uas-shared/skills/ship-asset) or a later reviewed revision throughout testing, upload and finalization. The runner records its exact revision and file hashes; changing it invalidates retained evidence.

The configured minimum is 2022.3. Resolve the latest stable stream from the official archive for each release; do not substitute an installed patch or preview. A new baseline without a verified version-project mapping blocks execution.

This ship.config supplies matrix and branch settings only. Export/upload setup is not configured by this change.
