# Nexus Review Notes

## Target Upload

- Project: MH Classic Tools Windows x64 **0.18.0**.
- Mod page: https://www.nexusmods.com/monsterhunter4ultimate3ds/mods/12
- Uploaded label: MH Classic Tools 0.18.0.
- Local archive: `MH-Classic-Tools-0.18.0-Windows-x64.zip`.
- ZIP bytes: `93208480`.
- ZIP SHA-256: `ea148922f109fe64784a094b43ff50de219211fd941fad37d11aedcaab70f6e4`.

The remote uploaded bytes were not independently downloaded. Reviewers should
compare the hash with the quarantined attachment. The local archive matches
the original prepared candidate hash; it has not been replaced for this review.

## Why There Are Two EXEs

The original self-contained distribution includes the .NET/WPF runtime.
`ClassicMhTools.exe` is the editor apphost. `createdump.exe` is the runtime's
crash-diagnostic helper, not a second editor. Its Authenticode signature was
verified as Microsoft .NET, and its bytes matched the .NET 8.0.30 runtime copy.

`createdump.exe` SHA-256:
`4e67fd4b78c4cc3d8d032c02bb0a1a4b7d563df0fb6df287a0e3e0f036544d5d`.

The reviewed ZIP contains 5,135 entries and **no BAT, CMD or PS1 files**, nested
archives, duplicate paths or unsafe archive paths. The quarantine screenshot
gave generic possible causes, not a named engine, detection or blocked file.
Neither the helper nor the number of EXEs has been established as the cause.

## Binary And Source Baselines

`ClassicMhTools.dll` SHA-256:
`150ecbebc8d403a1222ab87cc6bfebafd654f55f441018511898fa14803f67b2`.

`ClassicMhTools.Core.dll` SHA-256:
`23b4d751464d1f9747e819d8ee3e168da6eb62e27764dab52ac89aff92a75328`.

`vendor/mh4edit/mh4edit.dll` SHA-256:
`30282552b7996117fa9041738758b548f1a63aba74791d34ad0fa80938baa214`.

The apphost and project-created assemblies are unsigned. Valid signatures
on runtime files and matching checksums establish provenance/integrity, not
that arbitrary code is safe. The imported save core has its exact MIT notice
and [upstream source link](https://github.com/Rokumaehn/mh4edit).

The PDB audit matches source-document checksums and binary/PDB identity; it
does not attest all inputs or guarantee a reproducible binary. Paths in the
public audit are repository-relative. Private PDBs and full local build paths
are not included.

## What Was Tested Previously

On 2026-10-11, a Microsoft Defender custom scan of copies of the Windows ZIP,
Android ZIP and unpacked Windows candidate found no threats, exit code 0,
using definitions `1.459.663.0`. No remediation or security-setting changes
were applied by that custom scan. This is a historical result from one
engine, not a current VirusTotal result, proof of a false positive, or a
guarantee. The exact Nexus rule/detection remains unknown.

## Known Source Difference And Safety Boundaries

The active working tree received a later quest-type-bit fix for sequential
large-monster waves. This handoff uses the checksum-verified published source,
so that later fix is not represented as part of the old binary. Newer failing
regressions are retained under `review/known-issues/` for transparency rather
than compiled into the historical regression suite.

Save and extdata writes require backups, validation and the app's user-facing
actions. Reviewers should use synthetic or disposable copied data; no private
fixture or actual game executable is needed for the normal regression suite.
The experimental story-helper exporter present in the uploaded editor remains
visible in the code; unrelated Nadia, movement and layered-equipment mods do
not belong to this source handoff.

## Request To Moderators

Please identify the exact blocked filename, internal rule or scanner detection
and review this source against the existing uploaded archive. Source publication
does not automatically resolve the independent artwork-permission review.
