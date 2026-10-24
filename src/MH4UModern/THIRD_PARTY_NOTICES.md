# Third-party notices

## Experimental Helper Patch Checksums

System.IO.Hashing 8.0.0 (Microsoft / .NET Foundation and Contributors) provides
the standard CRC-32 implementation for BPS patches. Its MIT license is bundled
at `licenses/system.io.hashing/LICENSE.TXT`. The exporter contains independently
authored ARM trampolines; original game executables and NPC assets are not included.
Azahar's documented mod layout and BPS reader were used as interoperability references:
https://citra.azahar-emu.org/help/feature/game-modding/ and
https://github.com/azahar-emu/azahar/blob/master/src/core/file_sys/patch.cpp .

## Custom Quest Cryptography And Format References

BouncyCastle.Cryptography 2.7.0 provides standard Blowfish ECB. Its MIT license is
bundled in `licenses/bouncycastle/LICENSE.md` (Copyright 2000-2026 The Legion of
the Bouncy Castle Inc.).

The native Custom Quests module is an interoperability implementation with
fixture-backed format references from Seth VanHeulen's `mhef/n3ds.py` and
Dasding's `mh4ucqeditor/editor` MIB parser. Their applications, JS sources,
proxy/server code, MIB quest files and downloaded binaries are not embedded or
redistributed in this app. Downloaded references remain private under `assets/`.

- https://github.com/svanheulen/mhef/blob/master/mhef/n3ds.py
- https://github.com/mh4ucqeditor/editor/blob/master/app/app/utils/mib.js

## User-Supplied Quest Artwork And Reference Metadata

v0.13.1 uses the monster sprite sheet and twenty map PNGs extracted from the
user-supplied Dasding archive. MH4U game-derived artwork belongs to its respective
rights holders and is not covered by the save core's MIT license. No separate
artwork redistribution permission was confirmed; verify those terms before
sharing a public binary/source package containing these images.

`assets/quests/reference.json` contains factual monster/map names and ID mappings,
sprite coordinates, and SHA-256 fingerprints of 108 supplied US official MIBs.
The fingerprints do not contain quest payloads. Field/ID references were checked
against https://github.com/mh4ucqeditor/editor/blob/master/app/app/utils/constants.js .
The old editor's JS and executable are not included in the runtime package.

---

MH4U Modern v0.1 uses the save-format implementation from **mh4edit** by Rokumaehn.
The original project is licensed under the MIT License.

Copyright (c) 2025 Rokumaehn

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

---

## User-supplied MH4U HD Remix UI textures

v0.5 also includes selected cropped UI sprites from a texture pack supplied directly for this fork.
The accompanying pack metadata identifies it as:

- Description: Monster Hunter 4 Ultimate
- Author: jM5557
- Version: 3.03

No redistribution license was included with the uploaded texture archive. These assets are therefore
kept separate from the upstream MIT code attribution. Verify the original texture pack author's terms
before redistributing a public binary/source package that includes them.

---

## MH4U Database 1.08 reference catalog

v0.16.0 also exports factual monster/rank material distributions from the public
source repository's SQLite database. Database item IDs are not game IDs: the
generator matches exact names to the recovered game-item map before exporting
numeric presets. The SQLite database is not included in the portable package.
Source: https://github.com/kamegami13/MonsterHunter4UDatabase . Its MIT notice is
also included at `licenses/mh4u-database/LICENSE.txt`.

v0.11 adds read-only catalog metadata and item icon assets extracted from the **MH4U Database 1.08** APK supplied for this fork.
The matching public source project (`MonsterHunter4UDatabase` by DavianCorp) is licensed under the MIT License.

Copyright (c) 2014 DavianCorp

A copy of that MIT license is included at:
`assets/catalog/LICENSE-MH4U-DATABASE.txt`

The catalog integration is deliberately separated from the save core. The Android application itself is not executed or embedded; selected SQLite rows are exported to TSV and loaded as read-only reference data.
