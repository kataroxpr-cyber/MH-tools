# MH Classic Tools: Windows 0.18.0 Source Review

Source for reviewing the Windows editor uploaded to [Nexus Mods, mod 12](https://www.nexusmods.com/monsterhunter4ultimate3ds/mods/12).

**Creator:** KataroXPR. **AI-assisted engineering:** OpenAI Codex.

This is an isolated source handoff, not the private development workspace,
an Android release, or a package of Nadia/gameplay mods. Nothing here requires
turning off antivirus or running with administrator privileges.

## What Matches The Upload

The 76 original C# source documents recorded in the published app and core
portable PDBs were compared with their compiler-recorded checksums. The PDB
identities were also matched to their DLLs. All 76 snapshot documents match.
See [the compiler-checksum report](review/PUBLISHED-SOURCE-CHECKS.json).

This snapshot disables Git line-ending conversion to preserve the recorded
source bytes. Keep its `.gitattributes` when uploading or cloning it.

This is not a claim that project files, all XAML inputs, compiler/runtime
versions, artwork or output binaries have been reproduced byte-for-byte.

The original `QuestCreation.cs` was recovered from a checksum-matching local
backup. The later working copy contains a multi-wave fix which is deliberately
not substituted into a source snapshot for the older upload. The later wave
regressions are supplied separately as [known-issue evidence](review/known-issues/QuestWaveTests.cs.txt),
not silently removed to claim the bug does not exist.

## Build And Test

Requirements: Windows x64, a stable .NET SDK 8 or later, and NuGet access for
the pinned package references. An SDK includes the build tools; a runtime
alone is not enough to compile the project.

```powershell
dotnet restore ClassicMhTools.sln
dotnet build ClassicMhTools.sln -c Release --no-restore
dotnet test tests/ClassicMhTools.Core.Tests/ClassicMhTools.Core.Tests.csproj -c Release --no-build
```

Build output is under `work/build/`. To publish to a new review-only folder:

```powershell
dotnet publish src/MH4UModern/MH4UModern.csproj -c Release -r win-x64 --self-contained true -o work/publish-review
```

These commands do not overwrite the original release or automatically install
anything into a game/emulator. Private game-executable and supplied-quest
fixture tests are opt-in and are not supplied here.

## Artwork Is Not Included

Redistribution permission for HD Remix textures and game-derived quest art is
still unconfirmed, so this source handoff excludes the real PNG/JPG/ICO art.
The only ICO is a newly drawn neutral placeholder required for compilation.
Factual catalogs, lists and map/monster ID metadata are retained with notices.

This code-only build is suitable for compilation, logic tests and inspection.
It does **not** reproduce the full visual UI or pass the release's artwork
audit without the original separately reviewed assets. Do not distribute this
source-review build as the completed user release. Asset omission is not an
antivirus workaround or a claim that the original assets are licensed.

## Review Navigation

- `src/MH4UModern/`: WPF editor UI, entry point and view integration.
- `src/ClassicMhTools.Core/`: save operations, backup/undo, emulator folders,
  custom quest parsing/editing and guarded installation.
- `tests/ClassicMhTools.Core.Tests/`: synthetic regression tests.
- `vendor/mh4edit/`: original MIT-licensed save-core DLL, license and hash.
- `REVIEW.md`: upload fingerprints, the second EXE and review boundaries.
- `SOURCE-MANIFEST.json`: source handoff file inventory and checksums.

No saves, extdata, quests, ROMs, game executable/archive payloads, signing
keys, credentials, SDKs, caches, production binaries or private Git history
are included. No BAT/CMD/PS1 launchers are required in this repository.

## Credits And Terms

See [CREDITS.md](CREDITS.md), [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
and the exact notices in `vendor/`. The inherited MIT notice is retained in
`LICENSE-UPSTREAM-MIT.txt`. No additional blanket project license has been
selected for newly authored code, and no code license extends to game art.

This is an unofficial fan project, not endorsed by Capcom, Microsoft, OpenAI
or Nexus Mods. Public hosting is not a claim of security approval. Source and
documentation in this handoff include AI-assisted work.
