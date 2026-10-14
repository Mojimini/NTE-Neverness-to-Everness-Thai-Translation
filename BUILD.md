# Build and publish

The application version, supported game version and translation revision are separate: current app **1.0.1**, game **1.4.5**, content **v28c**. Builds generate one application version for the assembly, UI, configuration, setup name and installer metadata. The content catalog intentionally remains pinned to the release hosting those exact archives.

## Build prerequisites

- Windows x64 with .NET Framework 4.8 and its C# compiler.
- Official [Inno Setup 6.7.3](https://jrsoftware.org/isdl.php); the build verifies its compiler's Authenticode signer.
- This repository's `runtime/` contains only the baseline runtime/font previews/licenses and pinned content catalog. Accepted larger payloads are Release assets, rather than Git source files.

Run from PowerShell:

```powershell
.\build-release.ps1 -Version 1.0.1 -InnoCompiler 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

The result is `release/Thai-Translator-Setup-1.0.1.exe` and `SHA256SUMS.txt`. `ThaiUpdate.exe` is rebuilt and its actual SHA-256 is written to the configuration immediately before packaging. Local compiler/build tools, generated binaries and personal settings are ignored by Git.

## Publish a program update

1. Make and review the source changes; run `tests/run-tests.ps1` and relevant installer tests in an isolated fixture.
2. Build with a higher numeric version, for example `-Version 1.0.2`.
3. Create a GitHub Release tagged exactly `v1.0.2` with the asset named exactly `Thai-Translator-Setup-1.0.2.exe`. Upload the checksums and release notes. GitHub's asset digest is required by the app.
4. Check the draft asset digest against the local SHA-256. Publish it as a stable latest Release. Drafts and prereleases are ignored by the updater.
5. Download anonymously and verify discovery with an older app version before announcing the update.

Keep `translation-v28c.zip` and `font-tools-v1.zip` on their original pinned content release. An app-only update does not need to upload those archives again. If content changes, create verified archives/catalog file hashes first, point `runtime/content-catalog.json` at the release hosting them, and update the supported game information/manifests. Do not silently replace files under an existing catalog digest.

Never package `receipt.json`, game paths, selected user manifests, personal font selections/imports, QA receipts, local backups, tokens or private fixture files. The installer uses an explicit allowlist, preserves user state and refuses uninstall while the game translation receipt remains active.

## Tests

`tests/run-tests.ps1` compiles fixture test executables with the same .NET compiler. Delivery tests inject an internal HTTPS transport and do not reach the game or GitHub. The public source tests cover ZIP/URL/hash validation, rollback, lazy preparation, update UI, acknowledgement and existing UI behavior.

Installer test builds use a distinct AppId and omit shortcuts/automatic application launch; use `-TestBuild` only with isolated app directories. They must never target an actual user's installed program or game. Optional full delivery integration requires the pinned translation ZIP and an OFL Thai font fixture; see the integration test source for inputs.

The 1.0.1 application uses a WPF retained-mode view with themed selector and scrollbar templates; legacy WinForms UI tests remain as compatibility checks. Progress delivery coalesces the newest worker report instead of scheduling one UI callback for each download buffer. Native visual checks must accompany render-image tests for theme changes, keyboard selection, scrolling and translation progress. The new installer does not include AntdUI.dll.
