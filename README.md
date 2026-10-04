# Rhythians Mod

Your Rhythians profile, map ratings, rewards, and challenge passes inside Rhythia.

## Install

Close Rhythia and run **Rhythians.exe** from the latest beta release. The installer finds your Steam library; use Browse if you keep the game elsewhere. Open Rhythia, accept the first-run notice, then sign in through the website. **F8** opens your account menu.

The installer checks the supported Windows game build and keeps the original graphics library. Run **Rhythians.Uninstall.exe** from the game folder with Rhythia closed to remove the mod. Your maps and game scores are untouched. Login and notice acceptance stay saved on your Windows account.

New releases appear in game with **Update and restart** and **Cancel** buttons. You can also check in Profile → Settings. Downloads are checked against the release asset’s SHA-256 digest before installation.

## In game

- Grid, list, and selected-map views show Rhythians difficulty and rankability.
- Ranked and legacy maps can award points. Unranked maps still show their analysis.
- Map cards and the play button show the full potential score. A separate line shows the extra points available above your current best. Your active mode is green: RPL for Lock, RPS for Spin, RPVR for VR.
- No Fail and practice starts show zero rewards. Speed changes update the preview.
- Challenge labels include category, level, and pass status. Eligible full completions at normal speed or faster can record challenge passes, including levels 7–10.
- Rhythia imports and mod submissions share one best score per map and mode.
- Missing-data thumbnails have a **Check map** button.
- Linked players show Rhythians rankings on the leaderboard and map rewards on score rows.
- Your profile menu contains ranks, scores, passes, and submission settings.
- Login attempts expire after two minutes and offer Retry and Cancel.
- A compact daily panel checks today's pass, turns green when complete, and downloads missing maps through Rhythia's importer. Daily resets follow the website's UTC day.
- Map details stay visible from a local cache during refresh. Background map work pauses during gameplay.

Maps without an online ID are matched against the website by chart contents. Ambiguous or missing matches stay unavailable. A different local chart version cannot submit against the website's analyzed version.

Difficulty supports 0.20×–4.00×; extra speed curves load in the background. Scored submissions currently support 0.50×–2.00×.

## Build

Use Zig 0.14.1 and the .NET 10 SDK on Windows:

```powershell
./Build.ps1 -Zig zig -Dotnet dotnet
```

The release is written to `dist`. The native library forwards Rhythia's OpenGL calls and places labels using its own fonts and menu bounds. The helper reads the local game database and connects to `https://www.rhythians.com`; it stores the login token using Windows account encryption.

Website support lives in [Rhythians](https://github.com/Evaxle/Rhythians). Score validation checks the linked player, chart identity, completion, mode, and modifiers. It is not replay-based anti-cheat verification. VR hardware and a real completed-run submission still need a live acceptance test.
