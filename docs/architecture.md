# Architecture

## Layering

- **RipVerify.Core** — domain models, abstractions and pure logic. No Avalonia,
  no `System.Diagnostics.Process`, no TagLibSharp, no NAudio, no HttpClient, no
  heavy I/O.
- **RipVerify.Infrastructure** — implementations: scanning, CUE handling,
  process runner, spectrum analysis, origin signals, MusicBrainz, reporting,
  batch orchestration, JSON settings.
- **RipVerify.App** — Avalonia UI (MVVM), DI composition, Serilog, localization.
- **RipVerify.Tests** — xUnit + FluentAssertions + NSubstitute.

Dependency direction is `App -> Infrastructure -> Core`; `Core` depends on
nothing above it. `InternalsVisibleTo("RipVerify.Tests")` is enabled for test
seams.

## Verification pipeline

1. `FolderScanner` resolves folder/`.cue`/audio inputs (`.flac`, `.wav`, `.ape`,
   `.wv`).
2. `CueFixerService` / `CueSanitizer` normalize the cue sheet and `CueGenerator`
   synthesizes one when missing. ANSI cue sheets are decoded with Windows-1252.
3. `CueToolsRunner` executes `CUETools.ARCUE.exe` (user-supplied) with the
   absolute cue path from the CUETools directory, then `CueToolsOutputParser`
   parses the AccurateRip/CTDB sections.
4. `FftSpectrumAnalyzer` uses bundled FFmpeg to decode and FFT the audio.
5. `OriginSignalReader` extracts lossy-origin signals; `MusicBrainzClient`
   provides release candidates.
6. `BrickwallCalibrator` + `JsonCalibrationCorpus` produce the calibration.
7. `VerificationClassifier` maps evidence to seven states:
   `VerifiedCdRip`, `VerifiedWithWarnings`, `AuthenticRipWithLog`,
   `LikelyStreaming`, `UndeterminedLossless`, `FakeLossless`, `Unprocessable`.
   Conflicting AccurateRip/CTDB evidence, or an accurate dBpoweramp file tag
   contradicted by a live database, produces `VerifiedWithWarnings`.
8. `BatchProcessor` runs items sequentially, reporting `IProgress<BatchProgress>`,
   and writes the JSON sidecar per item; failures are logged and isolated.
9. `AlbumDetailViewModel` can re-verify a single item and export Markdown.

## Report format

- Sidecar files next to the album: `ripverify-report.json` and, on demand,
  `ripverify-report.md`.
- `schemaVersion` is `3`; writers reject other versions with a warning.
- Each result records `cueOrigin`, `dbPoweramp` evidence, `parserWarnings`,
  and the existing verification evidence.
- JSON is camelCase, indented, omits nulls and serializes enums as strings.
- Timestamps are ISO 8601 UTC.
- Markdown can be forced to English regardless of UI language.

## FFmpeg detection order

1. `AppSettings.ffmpegPath`, if the file exists.
2. `<application directory>\ffmpeg\ffmpeg.exe` (bundled).
3. `%PROGRAMFILES%\RipVerify\ffmpeg\ffmpeg.exe` (installed copy).
4. `ffmpeg.exe` on `PATH`.

Absence is logged as a warning and disables spectrum analysis; verification
continues with reduced confidence.

## Accessibility and WCAG contrast

The UI uses a dark surface `#1E1E2E`. Status text colors were checked against
that surface for the WCAG AA normal-text threshold (4.5:1):

| Element | Color | Contrast |
| --- | --- | --- |
| Verified CD rip | `#2ECC71` | 7.80:1 |
| Verified with warnings | `#2ECC71` | 7.80:1 |
| Likely streaming | `#F1C40F` | 9.87:1 |
| Undetermined lossless | `#95A5A6` | 6.41:1 |
| Fake lossless | `#FF5C4D` | 5.38:1 |
| Unprocessable | `#E67E22` | 5.76:1 |
| Accent / focus | `#89B4FA` | 7.79:1 |

The original red `#E74C3C` measured **4.29:1** and failed AA, so it was replaced
by `#FF5C4D` (5.38:1). Status color is mapped in `StatusToBrushConverter`.
Color is never the only signal: every status also renders localized text.

Focusable controls use a visible focus accent; tooltips and
`AutomationProperties.Name` are provided on actionable controls and the results
list. Keyboard shortcuts: `Ctrl+O` add folder, `Ctrl+,` settings, `F5`
re-verify selected, `Enter` open selected, `Esc` return to dashboard.

## Localization

All user-facing strings come from `Strings.resx` (English) and
`Strings.es.resx` (Spanish) through `ILocalizationService`. `GetString(key,
language)` supports a forced language for exported Markdown. Tests assert key
parity and that no hard-coded UI strings remain in views.

## Settings and storage

- `%LOCALAPPDATA%\RipVerify\settings.json` persists user settings; it survives
  uninstall.
- Logs are written under `%LOCALAPPDATA%\RipVerify\logs\`.
- First run opens the wizard when `settings.json` has not been completed.

## Installer

- Self-contained `win-x64` publish, packaged with Inno Setup 7. `iscc` is not on
  `PATH` on the build machine, so `build\scripts\build.ps1` resolves
  `C:\Program Files\Inno Setup 7\ISCC.exe` first, then the Inno 6 fallbacks.
  Per-machine install to `{autopf}\RipVerify`; Start Menu shortcut always,
  optional Desktop shortcut.
- FFmpeg is installed under `{app}\ffmpeg\`. Uninstall removes the install
  directory but preserves `%LOCALAPPDATA%\RipVerify`.
- Manual installer validation steps: `docs/installer-test.md`.
