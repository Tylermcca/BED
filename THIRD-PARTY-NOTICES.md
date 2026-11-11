# Third-party notices

The public library takes as few dependencies as it can, and each one is recorded here with its licence (worksheet OSS-004). Test-only packages (xunit, Microsoft.NET.Test.Sdk) are not shipped and are not listed.

| Package | Version | Used by | Licence | Source |
|---|---|---|---|---|
| SharpGLTF.Core | 1.0.7 | `Bed.Import` (glTF reading) | MIT | https://github.com/vpenades/SharpGLTF (licence checked on nuget.org, 2026-10-31) |

The core library (`Bed`) takes no third-party package.

## Stb image decoding

The host (`Bed.Editor.App`), not the generic editor model, references `Ahwoo.Brutal.Stb` 2026.10.2 for bounded CPU image previews. The binding is BRUTAL License v1.0 (existing BRUTAL product credit/notice duties remain); its stb_image v2.30 and stb_image_write v1.16 are MIT OR Unlicense, upstream commit f58f558. Package LICENSE.txt, NOTICE.txt and LICENSES/BRUTAL-STB-THIRD-PARTY-NOTICES.txt must accompany redistribution. Source: https://github.com/RocketWerkz/brutal.stb and https://github.com/nothings/stb. No GPU texture package is added.

## Vendored source

| Component | Version | Used by | Licence | Source |
|---|---|---|---|---|
| ImCode (`ImCode`, `ImCode.Text`) | commit `98e69303054540c43fd67909b6a30975a19aca8b` (2026-10-15) | `Bed.Editor.Text` (the Text panel's code editor), and shipped inside its package | MIT | https://github.com/aproposmath/ImCode, copied unchanged into `third_party/ImCode/` (`PINNED.md` says what and when) |

ImCode is Copyright (c) 2026 aproposmath, and is used under the MIT License:

```
MIT License

Copyright (c) 2026 aproposmath

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
```

A release carries the same text beside the programs as `licenses/ImCode-LICENSE.txt`, and so does the `Bed.Editor.Text` package, whose `THIRD-PARTY-NOTICES.txt` names ImCode. The core editor (`Bed.Editor.ImGui`) carries no ImCode.

## BRUTAL

The editor window, the general app (`brutal-authoring`), the command line (`authoring`) and the checkers game are built with **BRUTAL**, a game development framework by Ahwoo Limited, used under the **BRUTAL License v1.0** (https://github.com/ahwootech/BRUTAL, `LICENSE.txt`). Packages: `Ahwoo.Brutal.Core`, `Ahwoo.Brutal.ImGui`, `Ahwoo.Brutal.ImGui.Backends`, `Ahwoo.Brutal.Glfw`, `Ahwoo.Brutal.Vulkan`, `Ahwoo.Brutal.Vulkan.Abstractions` (versions in `Directory.Packages.props`).

BRUTAL game development framework is Copyright (c) 2024 Ahwoo Limited.

What its licence asks of every program built with it that's given to people, and how this repo does it:
- **The copyright notice in the credits (2.1):** on the app's start screen, in the checkers game's window, and in each program's `--help` (`BrutalCredit.Copyright`).
- **The BRUTAL logo before the program's main function (2.2):** shown on black for 2.5 seconds when the app and the game start, skippable after one second (`BrutalSplash`). The logo is BRUTAL's own `docs/branding/LOGO_BRUTAL_WHT.png`, unaltered except for scaling. The command line can't show graphics, which the licence exempts.
- **BRUTAL's `NOTICE.txt` beside the program (2.3):** copied next to every program by `build/Notices.targets`, and at this repo's root.
- **The third-party licences BRUTAL's packages bundle (4.0):** copied into `licenses/` beside every program by the same targets, with the .NET runtime's licence and notices for a self-contained publish.

A game built with these tools on BRUTAL owes BRUTAL the same; its own adapter or host does that.


## Build-time scene shader compiler

Ahwoo.Brutal.ShaderC 2026.10.2 is referenced only by tools/SceneShaderCompiler, not BED runtime. Managed binding: BRUTAL License v1.0. Native shaderc and SPIRV-Tools: Apache-2.0; glslang and SPIRV-Headers carry additional notices in the package LICENSES/BRUTAL-SHADERC-THIRD-PARTY-NOTICES.txt. Keep package notices with build-tool redistribution. BED embeds only SPIR-V compiled from its self-authored GLSL, and must not ship this compiler. Source: https://github.com/RocketWerkz/brutal.shaderc .


## AccessKit C 0.23.2 (Windows x64)

The approved native binary is the unmodified `lib/windows/x86_64/msvc/shared/accesskit.dll` from
[AccessKit C release 0.23.2](https://github.com/AccessKit/accesskit-c/releases/tag/0.23.2), commit
`99023c87eca49531a8e1efc4657e35779257a463`. The archive SHA256 is
`c7fa84f8f34fca909f662375737a54f98b17cf9eae51fffd94ab3d9f662e3681`; the DLL SHA256 is
`c8e2b528a2f4bbc5ee8e091ae48d55b43156518a0105e48e94c64e101bf1c29b` (359,424 bytes).
The header and MIT/Apache licences are beside it in `third_party/accesskit/0.23.2/`.
The release packer places the DLL beside BED.exe, both licences under `licenses/`, and this notice beside them.
It does not ship symbols, static libraries, Meson, CMake or a Rust toolchain.

### Source-derived dependency inventory

These exact versions come from the archive's Cargo.lock, with Windows target/feature edges checked against the
linked manifests. M/A means MIT OR Apache-2.0; MIT is the proposed redistribution choice where applicable.
This is a **source-derived reachable set, not proof of the crates/features compiled into the official DLL**.
The archive has no compiler-artifact list or resolved target feature report. The root requests no optional
features for this Windows target. `uuid` defaults are disabled, so its lockfile's optional JS/serde edges are
not counted. Other platform and development dependencies are excluded. Host procedural macros are identified
separately and are not asserted to be linked into the DLL.

| Crate | Version | Source-projected side and features | Declared licence and primary source |
|---|---|---|---|
| accesskit-c | 0.23.2 | target; no optional root features | M/A; [manifest][c-manifest] |
| accesskit | 0.25.2 | target; no optional features expected | M/A plus Chromium-derived portions; [manifest][ak-core], [workspace][ak-workspace] |
| accesskit_windows | 0.35.2 | target; Windows API features below | M/A; [manifest][ak-windows] |
| accesskit_consumer | 0.39.2 | target; no optional features expected | M/A; [manifest][ak-consumer] |
| uuid | 1.19.0 | target; default features disabled by accesskit | Apache-2.0 OR MIT; [manifest][uuid] |
| hashbrown | 0.17.1 | target; default-hasher; defaults disabled | M/A; [manifest][hashbrown] |
| foldhash | 0.2.0 | target; defaults disabled by hashbrown | Zlib; [manifest][foldhash] |
| static_assertions | 1.1.0 | target; no nightly feature requested | M/A; [manifest][static] |
| windows | 0.62.2 | target; std and Windows API features | M/A; [manifest][windows] |
| windows-core | 0.62.2 | target; std | M/A; [published manifest][windows-core] |
| windows-collections | 0.3.2 | target; std projected | M/A; [manifest][collections] |
| windows-future | 0.3.2 | target; std projected | M/A; [manifest][future] |
| windows-numerics | 0.3.1 | target; std projected | M/A; [manifest][numerics] |
| windows-link | 0.2.1 | target; no feature requests | M/A; [manifest][link] |
| windows-result | 0.4.1 | target; std projected | M/A; [manifest][result] |
| windows-strings | 0.5.1 | target; std projected | M/A; [manifest][strings] |
| windows-threading | 0.2.1 | target; no feature requests | M/A; [manifest][threading] |
| windows-implement | 0.60.2 | host; defaults disabled | M/A; [published manifest][implement] |
| windows-interface | 0.59.3 | host; defaults disabled | M/A; [published manifest][interface] |
| proc-macro2 | 1.0.106 | host; proc-macro projected | M/A; [published manifest][proc-macro] |
| quote | 1.0.37 | host; proc-macro projected | M/A; [manifest][quote] |
| syn | 2.0.87 | host; full, parsing, printing, clone-impls, proc-macro | M/A; [manifest][syn] |
| unicode-ident | 1.0.8 | host; no feature requests | M/A AND Unicode-DFS-2016; [manifest][unicode] |

The adapter requests Win32_Foundation, Win32_Globalization, Win32_Graphics_Gdi, Win32_System_Com,
Win32_System_LibraryLoader, Win32_System_Ole, Win32_System_Variant, Win32_UI_Accessibility,
Win32_UI_Input_KeyboardAndMouse and Win32_UI_WindowsAndMessaging. Parent/default feature expansion remains
source-projected, not a record of the upstream compiler invocation. The host-side unicode-ident declaration
is Unicode-DFS-2016 in addition to M/A; this is not renamed Unicode-3.0.

The upstream release's Rust compiler version, linked standard-library components and exact release feature
union remain unverified. This table does not claim complete binary attribution or release/legal clearance.
Static PE import inspection (without loading) finds Windows system APIs, VCRUNTIME140.dll and Universal CRT
API sets; BED's existing Visual C++ prerequisite remains necessary. No CRT or OS DLL is redistributed here.
Upstream compiler attestation is unavailable for this prebuilt. The accepted inventory intentionally remains source-derived, rather than claiming observed compilation.

[c-manifest]: https://raw.githubusercontent.com/AccessKit/accesskit-c/0.23.2/Cargo.toml
[ak-workspace]: https://raw.githubusercontent.com/AccessKit/accesskit/accesskit_windows-v0.35.2/Cargo.toml
[ak-windows]: https://raw.githubusercontent.com/AccessKit/accesskit/accesskit_windows-v0.35.2/adapters/windows/Cargo.toml
[ak-core]: https://raw.githubusercontent.com/AccessKit/accesskit/accesskit_windows-v0.35.2/accesskit/Cargo.toml
[ak-consumer]: https://raw.githubusercontent.com/AccessKit/accesskit/accesskit_windows-v0.35.2/accesskit_consumer/Cargo.toml
[hashbrown]: https://raw.githubusercontent.com/rust-lang/hashbrown/v0.17.1/Cargo.toml
[foldhash]: https://raw.githubusercontent.com/orlp/foldhash/v0.2.0/Cargo.toml
[uuid]: https://raw.githubusercontent.com/uuid-rs/uuid/v1.19.0/Cargo.toml
[static]: https://raw.githubusercontent.com/nvzqz/static-assertions-rs/v1.1.0/Cargo.toml
[windows]: https://raw.githubusercontent.com/microsoft/windows-rs/32c3144490c016fe496a0aed769bce60987a2e9d/crates/libs/windows/Cargo.toml
[windows-core]: https://docs.rs/crate/windows-core/0.62.2/source/Cargo.toml
[collections]: https://raw.githubusercontent.com/microsoft/windows-rs/32c3144490c016fe496a0aed769bce60987a2e9d/crates/libs/collections/Cargo.toml
[future]: https://raw.githubusercontent.com/microsoft/windows-rs/32c3144490c016fe496a0aed769bce60987a2e9d/crates/libs/future/Cargo.toml
[numerics]: https://docs.rs/crate/windows-numerics/0.3.1/source/Cargo.toml
[link]: https://raw.githubusercontent.com/microsoft/windows-rs/32c3144490c016fe496a0aed769bce60987a2e9d/crates/libs/link/Cargo.toml
[result]: https://raw.githubusercontent.com/microsoft/windows-rs/32c3144490c016fe496a0aed769bce60987a2e9d/crates/libs/result/Cargo.toml
[strings]: https://raw.githubusercontent.com/microsoft/windows-rs/32c3144490c016fe496a0aed769bce60987a2e9d/crates/libs/strings/Cargo.toml
[threading]: https://raw.githubusercontent.com/microsoft/windows-rs/32c3144490c016fe496a0aed769bce60987a2e9d/crates/libs/threading/Cargo.toml
[implement]: https://docs.rs/crate/windows-implement/0.60.2/source/Cargo.toml
[interface]: https://docs.rs/crate/windows-interface/0.59.3/source/Cargo.toml
[proc-macro]: https://docs.rs/crate/proc-macro2/1.0.106/source/Cargo.toml
[quote]: https://raw.githubusercontent.com/dtolnay/quote/1.0.37/Cargo.toml
[syn]: https://raw.githubusercontent.com/dtolnay/syn/2.0.87/Cargo.toml
[unicode]: https://raw.githubusercontent.com/dtolnay/unicode-ident/1.0.8/Cargo.toml

### MIT attribution and permission

The following MIT copyright notices apply to their corresponding components, in addition to AccessKit's
unmodified notice below. The linked licence files are the source of these attributions:

- windows-rs crates: Copyright (c) Microsoft Corporation.
  [Licence](https://raw.githubusercontent.com/microsoft/windows-rs/32c3144490c016fe496a0aed769bce60987a2e9d/license-mit).
- uuid: Copyright (c) 2014 The Rust Project Developers; Copyright (c) 2018 Ashley Mannix, Christopher Armstrong,
  Dylan DPC, Hunar Roop Kahlon.
  [Licence](https://raw.githubusercontent.com/uuid-rs/uuid/v1.19.0/LICENSE-MIT).
- hashbrown: Copyright (c) 2016 Amanieu d'Antras.
  [Licence](https://raw.githubusercontent.com/rust-lang/hashbrown/v0.17.1/LICENSE-MIT).
- static_assertions: Copyright (c) 2017 Nikolai Vazquez.
  [Licence](https://raw.githubusercontent.com/nvzqz/static-assertions-rs/v1.1.0/LICENSE-MIT).
- Host proc-macro2, quote, syn and unicode-ident include the same MIT permission/disclaimer, without an additional
  copyright line in their MIT files: [proc-macro2](https://raw.githubusercontent.com/dtolnay/proc-macro2/1.0.106/LICENSE-MIT),
  [quote](https://raw.githubusercontent.com/dtolnay/quote/1.0.37/LICENSE-MIT),
  [syn](https://raw.githubusercontent.com/dtolnay/syn/2.0.87/LICENSE-MIT),
  [unicode-ident](https://raw.githubusercontent.com/dtolnay/unicode-ident/1.0.8/LICENSE-MIT).

The MIT permission and disclaimer retained below apply to these MIT components with the corresponding
copyright notices above. AccessKit's separate Apache-2.0 option is retained unchanged in the vendored licence
and release licence folder.

```
Permission is hereby granted, free of charge, to any
person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the
Software without restriction, including without
limitation the rights to use, copy, modify, merge,
publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software
is furnished to do so, subject to the following
conditions:

The above copyright notice and this permission notice
shall be included in all copies or substantial portions
of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF
ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT
SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR
IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.

```

### AccessKit's Chromium-derived portions (BSD-style notice)

Unmodified notice from the official archive's LICENSE.chromium:

```
// Copyright 2015 The Chromium Authors. All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
//
//    * Redistributions of source code must retain the above copyright
// notice, this list of conditions and the following disclaimer.
//    * Redistributions in binary form must reproduce the above
// copyright notice, this list of conditions and the following disclaimer
// in the documentation and/or other materials provided with the
// distribution.
//    * Neither the name of Google Inc. nor the names of its
// contributors may be used to endorse or promote products derived from
// this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

```

### foldhash 0.2.0 (Zlib)

[Unmodified upstream licence](https://raw.githubusercontent.com/orlp/foldhash/v0.2.0/LICENSE):

```
Copyright (c) 2024 Orson Peters

This software is provided 'as-is', without any express or implied warranty. In
no event will the authors be held liable for any damages arising from the use of
this software.

Permission is granted to anyone to use this software for any purpose, including
commercial applications, and to alter it and redistribute it freely, subject to
the following restrictions:
1. The origin of this software must not be misrepresented; you must not claim
    that you wrote the original software. If you use this software in a product,
    an acknowledgment in the product documentation would be appreciated but is
    not required.

2. Altered source versions must be plainly marked as such, and must not be
    misrepresented as being the original software.

3. This notice may not be removed or altered from any source distribution.
```

### Host unicode-ident 1.0.8 additional Unicode notice

Conservatively retained for the source-projected build dependency; not a claim that Unicode tables are in the
DLL. [Unmodified upstream licence](https://raw.githubusercontent.com/dtolnay/unicode-ident/1.0.8/LICENSE-UNICODE):

```
UNICODE, INC. LICENSE AGREEMENT - DATA FILES AND SOFTWARE

See Terms of Use <https://www.unicode.org/copyright.html>
for definitions of Unicode Inc.â€™s Data Files and Software.
NOTICE TO USER: Carefully read the following legal agreement.
BY DOWNLOADING, INSTALLING, COPYING OR OTHERWISE USING UNICODE INC.'S
DATA FILES ("DATA FILES"), AND/OR SOFTWARE ("SOFTWARE"),
YOU UNEQUIVOCALLY ACCEPT, AND AGREE TO BE BOUND BY, ALL OF THE
TERMS AND CONDITIONS OF THIS AGREEMENT.
IF YOU DO NOT AGREE, DO NOT DOWNLOAD, INSTALL, COPY, DISTRIBUTE OR USE
THE DATA FILES OR SOFTWARE.

COPYRIGHT AND PERMISSION NOTICE
Copyright Â© 1991-2022 Unicode, Inc. All rights reserved.
Distributed under the Terms of Use in https://www.unicode.org/copyright.html.
Permission is hereby granted, free of charge, to any person obtaining
a copy of the Unicode data files and any associated documentation
(the "Data Files") or Unicode software and any associated documentation
(the "Software") to deal in the Data Files or Software
without restriction, including without limitation the rights to use,
copy, modify, merge, publish, distribute, and/or sell copies of
the Data Files or Software, and to permit persons to whom the Data Files
or Software are furnished to do so, provided that either
(a) this copyright and permission notice appear with all copies
of the Data Files or Software, or
(b) this copyright and permission notice appear in associated
Documentation.
THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF
ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT OF THIRD PARTY RIGHTS.
IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS
NOTICE BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL
DAMAGES, OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE,
DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER
TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR
PERFORMANCE OF THE DATA FILES OR SOFTWARE.
Except as contained in this notice, the name of a copyright holder
shall not be used in advertising or otherwise to promote the sale,
use or other dealings in these Data Files or Software without prior
written authorization of the copyright holder.
```

## Sound previews

The app references `Ahwoo.BRUTAL.OpenAl` **2026.10.2**, compatible with the existing Core 2026.10.1 (2026.10.3 requires Core 2026.10.4 and is not used). The binding remains under BRUTAL License v1.0. Its dynamically loaded **OpenAL Soft 1.25.2** (commit dd4e07de0fe73d8c0326c4502f63e71da8ef268b) is GNU Library General Public License version 2; the package also includes BSD/PFFFT/fmt/GSL notices. Ship every package LICENSES/*.txt, including BRUTAL-OPENAL-THIRD-PARTY-NOTICES.txt and openal-soft-COPYING.txt. Windows packaging keeps OpenAL32.dll external beside BED.exe, replaceable by the user; no static linking. Source/rebuild instructions: https://github.com/kcat/openal-soft/tree/1.25.2 ; binding: https://github.com/ahwootech/BRUTAL . Preserve the upstream licence's source/relinking duties when redistributing.

OGG/Vorbis decode uses managed **NVorbis 0.10.5**, MIT, Copyright (c) 2020 Andrew Ward. Exact NuGet version checked against https://www.nuget.org/packages/NVorbis/0.10.5 and its package metadata; MIT text checked at https://github.com/NVorbis/NVorbis/blob/master/LICENSE . Its verbatim licence is third_party/NVorbis/LICENSE, staged as licenses/NVorbis-LICENSE.txt. These dependencies are in the app, with no BRUTAL dependency added to Editor or Runtime core. NVorbis's netstandard2.0 dependency declarations include System.Memory 4.5.3 and System.ValueTuple 4.5.0 (MIT).
