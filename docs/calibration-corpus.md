# Calibration Corpus

## Purpose
Document the manual calibration procedure for the brickwall detection threshold used by `FftSpectrumAnalyzer`, including the per-track aggregation and the explicit `BrickwallScore` formula.

## Status
- **State:** Provisional (no real corpus collected yet)
- **Default BrickwallThreshold:** 0.65 (in `SpectrumAnalysisOptions`)
- **Action required:** Calibrate with real corpus before publishing
- **Last review:** 2026-11-02
- **Recalibration pending:** yes. Required missing files include physical CDs with natural mastering rolloff (for example **Salsa Kids - Serie Millennium 21, Disc 1**) and confirmed lossy transcodes of the same material.

## Manifest schema
Location: `tools/calibration-corpus/manifest.json`

```json
{
  "OriginCategories": [
    "cd",
    "cd-natural-rolloff",
    "streaming",
    "vinyl",
    "transcode",
    "unknown"
  ],
  "Entries": [
    {
      "FilePath": "audio/cd_verified_01.flac",
      "ExpectedOrigin": "cd",
      "ObservedBrickwallScore": 0.12,
      "ObservedNyquistCutoffHz": 22050,
      "ObservedRolloffSlopeDbPerKHz": 0.4,
      "ObservedSpectralFlatness": 0.2,
      "Notes": "Verified with AccurateRip",
      "TaggedAt": "2026-10-30T00:00:00Z"
    }
  ],
  "CalibratedBrickwallThreshold": 0.65,
  "LastCalibratedAt": "2026-10-30T00:00:00Z"
}
```

### ExpectedOrigin values

| Value | Meaning | Counts as transcode |
|-------|---------|---------------------|
| `cd` | Physical CD verified in AccurateRip, full spectrum. | No |
| `cd-natural-rolloff` | Physical CD with natural mastering filter: gradual rolloff (typically 15-16 kHz, without a vertical wall). Must NOT be flagged as transcode. | No |
| `streaming` | Lossless from a platform (Qobuz/Tidal), full spectrum. | No |
| `vinyl` | Vinyl rip, natural gradual rolloff. | No |
| `transcode` | Lossy source (MP3/AAC) re-encoded to FLAC, vertical brickwall. | Yes |
| `unknown` | Not tagged; excluded from calibration. | N/A (excluded) |

Unknown or invalid categories are logged as `Calibration_UnknownOriginCategory` and excluded from the calculation without deleting the entry from the manifest.

### Canonical case: Salsa Kids - Serie Millennium 21, Disc 1
This physical CD has some tracks with full spectrum up to 22 kHz and other tracks with a gradual mastering rolloff around 15-16 kHz and no vertical wall. It is therefore tagged `cd-natural-rolloff`. Tagging it as `transcode` would create a false-positive reference and skew the calibrator. The analyzer must aggregate the whole album (worst track) instead of analyzing a single track, otherwise the gradual tracks are hidden behind the full-spectrum track.

Real measurement (2026-11-02, 11 tracks, CTDB bit-perfect virtual-drive rip): per-track analysis found cutoffs in tracks 1, 3, 9, 10 and 11; tracks 2, 4, 5, 6, 7 and 8 are full spectrum at 22050 Hz. Before the slope measurement fix the aggregate score was `0.455`, which was too high for a natural rolloff. After measuring the average rolloff over 3 kHz on each side of the knee, the final aggregate is:

- `nyquistCutoffHz = 15089` (track 11, `A Cada Momento`, the lowest cutoff)
- `brickwallScore = 0.285` (maximum across tracks; the maximum belongs to track 9, `Nena`, at 15256 Hz)
- `rolloffSlopeDbPerKHz = 5.58`, `spectralFlatness = 0`
- `worstTrackFilePath = ...\11 Salsa Kids - A Cada Momento.wav`

The album remains `VerifiedCdRip` because `isBrickwallSuspected` is false and CTDB matches 11/11 tracks. The JSON intentionally reports the maximum score together with the track that produced the minimum cutoff so the lowest Nyquist value can always be traced back to a specific file.

### Tagging procedure
1. Decode each candidate file and run `FftSpectrumAnalyzer.AnalyzeAlbumAsync` (or `AnalyzeAsync` for a single track).
2. Record `nyquistCutoffHz`, `brickwallScore`, `rolloffSlopeDbPerKHz` and `spectralFlatness` (for multi-track albums, record the worst track values).
3. Assign `ExpectedOrigin` using the table above. When a physical CD shows a gradual rolloff without a vertical wall, use `cd-natural-rolloff`.
4. Run the calibration tests. `BrickwallCalibrator.CalibrateWithMetrics` returns `FalsePositivesCount`, `FalseNegativesCount` and `F1Score`; a threshold is accepted only when `FalsePositivesCount == 0`.
5. Update `SpectrumAnalysisOptions.BrickwallThreshold` only with an accepted threshold and document the date here.

## BrickwallScore formula (fixed structure)
`FftSpectrumAnalyzer.ComputeBrickwallScore` uses exactly three weighted components:

```text
BrickwallScore = 0.50 * EnergyDropDb_normalized
               + 0.30 * SlopeRatio
               + 0.20 * FlatnessAfterCutoff
```

- `EnergyDropDb_normalized = min(max(EnergyDropDb, 0) / 40, 1)`
- `SlopeRatio = min(abs(RolloffSlopeDbPerKHz) / 15, 1)`
- `FlatnessAfterCutoff = 1 - min(variance_of_post_cutoff_energy_db / 50, 1)`

Interpretation:
- A typical MP3 brickwall has a 30-60 dB drop, a steep transition and flat near-silent energy after the cutoff, producing a score above 0.85.
- A natural mastering rolloff has a 5-15 dB drop, a gradual slope and non-flat post-cutoff energy, producing a score below 0.40.
- The weights and the three-component structure must not change during calibration; only the provisional threshold (0.65) is expected to change.

Cutoff detection runs in two steps:
1. The passband reference is measured as the mean power between 6 and 12 kHz.
2. The onset is the first frequency above 8 kHz where the energy 1 kHz below the candidate is still within 10 dB of the passband and the mean energy 200 Hz-2 kHz above it has dropped at least 6 dB. This avoids picking a narrow notch or the steepest point near Nyquist.
3. The onset is refined to the steepest transition point only when the local rolloff exceeds 20 dB/kHz (a true lossy-codec wall). Otherwise the onset is kept, which is the correct behavior for a gradual mastering rolloff. If no onset is found, the material is treated as full spectrum: cutoff = Nyquist and score = 0.

The energy drop compares the 1 kHz-200 Hz band before the cutoff with the 1 kHz-2 kHz band after it, and flatness is measured from 2 kHz after the cutoff up to Nyquist-200 Hz. The reported `RolloffSlopeDbPerKHz` is measured over 3 kHz on each side of the knee: a narrow "steepest local slope" overstates gentle 1990s mastering rolloffs, while the wide-window average still saturates for a near-vertical lossy-codec wall.

## Per-track aggregation
`ISpectrumAnalyzer.AnalyzeAlbumAsync` analyzes every track and aggregates the worst case:
- `BrickwallScore` = maximum across tracks (the most suspicious track is never hidden).
- `NyquistCutoffHz` = minimum across tracks.
- `RolloffSlopeDbPerKHz` and `SpectralFlatness` = values from the track with the lowest cutoff (highest score on ties).
- `BandEnergyDb` = per-band average across tracks.
- `IsBrickwallSuspected` = true when any track is suspected.
- `WorstTrackFilePath` = path of the track with the lowest cutoff, so the reported Nyquist value can always be traced back to a specific track; `null` for a single-track analysis or `SpectrumAnalysis.Empty`.
- A corrupt or unreadable track is logged and skipped; the remaining tracks are still analyzed. If all tracks fail, the result is `SpectrumAnalysis.Empty` with a warning.

## Procedure (real corpus)
1. Collect ~20 files: >=5 verified CDs (AccurateRip), >=5 `cd-natural-rolloff`, >=5 Qobuz/Tidal, >=5 known transcodes, >=5 vinyl/analog.
2. Tag each entry in the manifest with `ExpectedOrigin` and the observed metrics.
3. Run calibration (tests or tool). Accept only a threshold with zero false positives.
4. Update `SpectrumAnalysisOptions.BrickwallThreshold` and document the date.
5. Mark calibration tests with `[Trait("Category","Calibration")]` to exclude them in CI without a corpus.

## Last calibrated
Not yet calibrated with real corpus. `BrickwallThreshold` remains **0.65 provisional** (updated 2026-11-02). The structure of the formula is final; only the threshold and, if strictly required by the real corpus, the weights may be adjusted later with a documented date.
