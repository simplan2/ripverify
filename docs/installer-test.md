# Installer manual test checklist

No clean Windows VM was available in this phase, so installer validation is
**not executed**. Run these steps on a clean Windows 10/11 x64 VM after building
`build\installer\RipVerify-Setup-<version>.exe` with `build\scripts\build.ps1`.

Build environment note: `iscc` is not on `PATH` on the build machine. Inno Setup
7 is installed and the build script resolves it at
`C:\Program Files\Inno Setup 7\ISCC.exe` (before the Inno 6 fallback paths). The
installer was compiled successfully with that executable.

Record for each item: pass/fail, observed behavior, and a screenshot or log path.

1. **Clean VM install**
   - On a VM with no previous RipVerify install, run the setup executable.
   - Accept the license, keep the default directory, complete setup.
   - Expected: setup finishes without errors; files exist under
     `%PROGRAMFILES%\RipVerify`, including `RipVerify.exe` and
     `ffmpeg\ffmpeg.exe`.
2. **Launch from install directory**
   - Run `%PROGRAMFILES%\RipVerify\RipVerify.exe` directly.
   - Expected: the app opens without missing-DLL errors and shows the dashboard
     (or the first-run wizard on a fresh profile).
3. **Automatic FFmpeg detection without `ffmpegPath`**
   - Ensure `settings.json` has no `ffmpegPath` and no `ffmpeg` is on `PATH`.
   - Expected: spectrum analysis works using the bundled
     `%PROGRAMFILES%\RipVerify\ffmpeg\ffmpeg.exe`; no "FFmpeg not found" warning
     appears in the log.
4. **Start Menu and Desktop shortcuts**
   - Check the Start Menu entry and, if the desktop task was selected, the
     Desktop shortcut.
   - Expected: both point to `%PROGRAMFILES%\RipVerify\RipVerify.exe` and start
     the app.
5. **First-run wizard when `settings.json` is absent**
   - Delete `%LOCALAPPDATA%\RipVerify\settings.json`, then launch the app.
   - Expected: the first-run wizard appears; completing it writes
     `%LOCALAPPDATA%\RipVerify\settings.json` and subsequent launches go
     straight to the dashboard.
6. **Uninstall preserves user data**
   - Note the contents of `%LOCALAPPDATA%\RipVerify\`, then uninstall from
     Settings > Apps.
   - Expected: the install directory, `ffmpeg\`, and both shortcuts are removed;
     `%LOCALAPPDATA%\RipVerify\` (settings and logs) remains intact.

## Sign-off

- Tester:
- Date:
- Windows build:
- Installer version:
- Result: NOT EXECUTED (no VM available)
