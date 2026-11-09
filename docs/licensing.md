# Licensing

## RipVerify (application)

RipVerify is licensed under the **MIT License** (provisional). The full text is
in `LICENSE.txt` at the repository root.

- Copyright (c) 2026 RipVerify contributors.
- "Provisional" means the license may change before the first public stable
  release; the shipped artifact always includes the current `LICENSE.txt`.

## FFmpeg (bundled binary)

RipVerify ships `ffmpeg.exe` as a **separate external executable** under
`ffmpeg\`. It is not linked into the application and is invoked only as a child
process for spectrum analysis.

- Version: `N-128227-g4811625b5f-20261031` (LGPL build, Windows x86-64).
- License: **LGPL v2.1 or later**.
- Binary source (provenance): <https://github.com/BtbN/FFmpeg-Builds/releases/tag/latest>
  (asset `ffmpeg-master-latest-win64-lgpl.zip`).
- Corresponding source code: <https://github.com/FFmpeg/FFmpeg/tree/4811625b5f>.
- Build recipes: <https://github.com/BtbN/FFmpeg-Builds>.
- License text: `build/ffmpeg/LICENSE.txt` (also installed next to the binary).
- Details: `build/ffmpeg/README-ffmpeg.txt`.

Because FFmpeg is distributed as a separate program under the LGPL, its complete
corresponding source is available at the URL above. RipVerify does not modify
FFmpeg. Users may replace the bundled `ffmpeg.exe` with a compatible build of
their choice; point the application at it in Settings.

## CUETools

RipVerify can invoke the user-supplied `CUETools.Console.exe`. CUETools is
**not bundled or redistributed**. It is licensed under the GPL; users install it
separately and remain responsible for complying with its license.

## AccurateRip and CUETools Database (CTDB)

AccurateRip and CTDB results are obtained through CUETools. RipVerify does not
redistribute their data or code. Use of these databases is subject to their
respective terms published by their operators.

## MusicBrainz

Origin searches use the public MusicBrainz web service. RipVerify does not
redistribute MusicBrainz data. Requests identify the application and are
rate-limited by the service terms.

## Code signing

The current installer is **not code-signed**. Windows SmartScreen may warn on
first run. Signing certificates are not part of this phase; add Authenticode
signing to `build/scripts/publish.ps1` and `ISCC` invocation before public
distribution.
