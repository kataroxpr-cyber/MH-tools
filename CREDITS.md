# Credits

## Project Team

| Name | Contribution |
| --- | --- |
| **KataroXPR** | Creator and maintainer; project direction, feature design, development, feedback and device testing. |
| **OpenAI Codex** | AI-assisted engineering, research, implementation, regression checks and documentation under the creator's direction. |

The Codex credit recognizes AI-assisted work under KataroXPR's direction.
It does not imply endorsement of this release by OpenAI.

Project updates: [KataroXPR](https://www.youtube.com/@KataroXPR).

## Original Tools And References

| Project or contributor | Role in MH Classic Tools |
| --- | --- |
| [mh4edit by Rokumaehn](https://github.com/Rokumaehn/mh4edit) | Original MH4U save-format/encryption core used by Windows; inspected as the licensed baseline for the portable save implementation. Its original MIT notice is retained. |
| MH4U Modern | Earlier desktop-editor source lineage continued by this project. |
| [Seth VanHeulen's mhef](https://github.com/svanheulen/mhef) | Monster Hunter encryption and container-format interoperability references. The upstream Python application is not bundled. |
| [Dasding's MH4U Quest Editor](https://github.com/mh4ucqeditor/editor) | Quest format, ID/option references and reference-editor behavior used for validation. The old editor's executable, JavaScript application and downloaded quests are not bundled. |
| [MH4U Database / DavianCorp](https://github.com/kamegami13/MonsterHunter4UDatabase) | Read-only item/equipment metadata and reference catalog material. The original MIT notice is retained; the APK and raw SQLite database are not shipped. |
| [Azahar project](https://github.com/azahar-emu/azahar) | Emulator interoperability, documented data/mod layouts and the target folder-access workflow. The emulator itself is not bundled. |

Reference acknowledgments are distinct from runtime dependencies. They do not
relicense another project's code or imply that its entire application is
embedded here. See the existing `THIRD_PARTY_NOTICES.md` for the detailed
save/quest/catalog provenance and full inherited notices.

## Libraries And Platform Components

| Component | Contributor | Use |
| --- | --- | --- |
| BouncyCastle.Cryptography 2.7.0 | The Legion of the Bouncy Castle Inc. | Standard Blowfish cryptography; MIT license included. |
| System.IO.Hashing 8.0.0 | Microsoft / .NET Foundation and contributors | CRC-32 for Windows BPS patch export; MIT license included. |
| .NET and WPF | Microsoft / .NET Foundation and contributors | Desktop framework and self-contained runtime; runtime notices included. |
| Avalonia 12.1.3 | AvaloniaUI and contributors | Android/shared mobile UI; MIT license included. |
| Lucide.Avalonia 0.2.24 | Compunet | Avalonia icon integration; MIT license included. |
| Lucide and inherited Feather icons | Lucide contributors / Cole Bemis | Interface icons; ISC and inherited MIT notices included. |
| MicroCom.Runtime 0.11.6 | Nikita Tsukanov | Mobile COM interoperability support; original MIT notice included. |
| SkiaSharp / Skia and HarfBuzzSharp / HarfBuzz | Their respective contributors | Mobile rendering and text shaping; package license and third-party notices retained. |
| AndroidX, Kotlin and related Android bindings | Google, JetBrains, Microsoft and respective contributors | Mobile host support; package license and third-party notices retained. |

The `licenses/` directory and generated component inventory accompany the
release candidates. Build-only packages may appear in that inventory; it is
a build-context notice inventory, not a claim that every listed package ships
inside each application's binary.

## Artwork And Game Content

- **Capcom and respective rights holders:** Monster Hunter, its characters,
  original game artwork and game-derived icon/map material.
- **jM5557:** the user-supplied MH4U HD Remix UI texture pack, identified in its
  metadata as version 3.03, from which selected UI sprites were used.
- **The supplied Dasding archive:** monster/map artwork used in the quest UI.

The existing notices record that redistribution permission for the HD Remix
textures and game-derived quest artwork has not been confirmed. These assets
are not covered by the save core's MIT license. This remains an explicit
public-distribution review item in [Release Status](RELEASE_STATUS.md).

All credited projects retain their own names, rights and applicable terms.
The fan-project disclaimer and this acknowledgment are not replacements for
their full license texts or any required artwork permission.

## Community

Thank you to everyone who tests a release, reports an issue or shares a
suggestion. No unnamed tester is credited as the author of an upstream tool,
and no private tester identity is published by these packages.
