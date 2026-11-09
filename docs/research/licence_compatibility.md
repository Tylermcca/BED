# Licence compatibility for combined plug-ins

Research checked 2026-10-31. This is not legal advice. It is a reproducible test oracle for warnings, not an approval to distribute a product. No implementation or existing licence tests were read. No game code, records or art were copied.

## Meaning and scope

**MIXED: primary licence texts plus FSF, ASF and Mozilla interpretations.** The classifications answer whether a lawful distribution route exists for two accurately identified components in one combined work. A false result assumes the distributor actually fulfils every applicable condition. It does not mean that either original licence can cover the entire result, or that the original notices can be removed. Compatibility in this existence sense is symmetric; permission to relicense is often directional.

The fixture assumes separate identifiable component files or modules, with no undisclosed third-party terms, patents or exceptions. It tests distribution, rather than private use. GPL version selection and the LGPL's express GPL conversion permission are included; arbitrary dual licences and negotiated exceptions are not.

**INVENTED: fixture shorthand.** `LicenseRef-Closed` means a proprietary component whose owner does not grant recipients the corresponding-source and redistribution rights needed to release that component under GPL or AGPL terms. It does not, by itself, describe replacement, relinking or reverse-engineering rights for an LGPL library. Consequently those two LGPL rows are null. This custom identifier is defined here, not asserted to be an SPDX-listed licence.

The 54 rows contain 8 clashes, 44 available combination routes and 2 unresolved cases. An absent row, unknown identifier or null result must remain unchecked. It must not be turned into a false result.

## Summary table

The row numbers match the JSON array, starting at 1. True means a clash under the stated assumptions; false means an available route; null needs more facts.

| Row | Component A | Component B | clash | Primary section |
|---|---|---|---|---|
| 1 | MIT | GPL-2.0-only | false | [source](https://spdx.org/licenses/MIT.html#licenseText) |
| 2 | MIT | GPL-3.0-only | false | [source](https://spdx.org/licenses/MIT.html#licenseText) |
| 3 | BSD-2-Clause | GPL-2.0-only | false | [source](https://spdx.org/licenses/BSD-2-Clause.html#licenseText) |
| 4 | BSD-2-Clause | GPL-3.0-only | false | [source](https://spdx.org/licenses/BSD-2-Clause.html#licenseText) |
| 5 | BSD-3-Clause | GPL-2.0-only | false | [source](https://spdx.org/licenses/BSD-3-Clause.html#licenseText) |
| 6 | BSD-3-Clause | GPL-3.0-only | false | [source](https://spdx.org/licenses/BSD-3-Clause.html#licenseText) |
| 7 | Zlib | GPL-2.0-only | false | [source](https://spdx.org/licenses/Zlib.html#licenseText) |
| 8 | Zlib | GPL-3.0-only | false | [source](https://spdx.org/licenses/Zlib.html#licenseText) |
| 9 | ISC | GPL-2.0-only | false | [source](https://spdx.org/licenses/ISC.html#licenseText) |
| 10 | ISC | GPL-3.0-only | false | [source](https://spdx.org/licenses/ISC.html#licenseText) |
| 11 | Unlicense | GPL-2.0-only | false | [source](https://spdx.org/licenses/Unlicense.html#licenseText) |
| 12 | Unlicense | GPL-3.0-only | false | [source](https://spdx.org/licenses/Unlicense.html#licenseText) |
| 13 | CC0-1.0 | GPL-2.0-only | false | [source](https://spdx.org/licenses/CC0-1.0.html#licenseText) |
| 14 | CC0-1.0 | GPL-3.0-only | false | [source](https://spdx.org/licenses/CC0-1.0.html#licenseText) |
| 15 | BSL-1.0 | GPL-2.0-only | false | [source](https://spdx.org/licenses/BSL-1.0.html#licenseText) |
| 16 | BSL-1.0 | GPL-3.0-only | false | [source](https://spdx.org/licenses/BSL-1.0.html#licenseText) |
| 17 | GPL-2.0-only | Apache-2.0 | true | [source](https://www.apache.org/licenses/GPL-compatibility.html#:~:text=GPL%20version%202) |
| 18 | GPL-2.0-only | GPL-3.0-only | true | [source](https://www.gnu.org/licenses/gpl-faq.en.html#v2v3Compatibility) |
| 19 | GPL-2.0-only | AGPL-3.0-only | true | [source](https://spdx.org/licenses/GPL-2.0-only.html#licenseText) |
| 20 | GPL-2.0-only | LGPL-3.0-only | true | [source](https://www.gnu.org/licenses/license-list.en.html#LGPLv3) |
| 21 | GPL-2.0-or-later | GPL-3.0-only | false | [source](https://www.gnu.org/licenses/gpl-faq.en.html#v2v3Compatibility) |
| 22 | GPL-2.0-or-later | Apache-2.0 | false | [source](https://www.apache.org/licenses/GPL-compatibility.html#:~:text=GPLv3) |
| 23 | GPL-2.0-or-later | AGPL-3.0-only | false | [source](https://spdx.org/licenses/GPL-3.0-only.html#licenseText) |
| 24 | GPL-3.0-only | AGPL-3.0-only | false | [source](https://spdx.org/licenses/GPL-3.0-only.html#licenseText) |
| 25 | GPL-2.0-only | GPL-2.0-or-later | false | [source](https://spdx.org/licenses/GPL-2.0-only.html#licenseText) |
| 26 | Apache-2.0 | AGPL-3.0-only | false | [source](https://www.gnu.org/licenses/license-compatibility.en.html#:~:text=Apache) |
| 27 | LGPL-2.1-only | GPL-2.0-only | false | [source](https://spdx.org/licenses/LGPL-2.1-only.html#licenseText) |
| 28 | LGPL-2.1-only | GPL-3.0-only | false | [source](https://spdx.org/licenses/LGPL-2.1-only.html#licenseText) |
| 29 | LGPL-3.0-only | GPL-2.0-or-later | false | [source](https://spdx.org/licenses/LGPL-3.0-only.html#licenseText) |
| 30 | LGPL-3.0-only | GPL-3.0-only | false | [source](https://spdx.org/licenses/LGPL-3.0-only.html#licenseText) |
| 31 | LGPL-2.1-only | Apache-2.0 | false | [source](https://spdx.org/licenses/LGPL-2.1-only.html#licenseText) |
| 32 | LGPL-3.0-only | Apache-2.0 | false | [source](https://www.gnu.org/licenses/license-compatibility.en.html#:~:text=Apache) |
| 33 | LGPL-2.1-only | AGPL-3.0-only | false | [source](https://spdx.org/licenses/LGPL-2.1-only.html#licenseText) |
| 34 | LGPL-3.0-only | AGPL-3.0-only | false | [source](https://spdx.org/licenses/AGPL-3.0-only.html#licenseText) |
| 35 | LGPL-2.1-only | LGPL-3.0-only | false | [source](https://spdx.org/licenses/LGPL-2.1-only.html#licenseText) |
| 36 | MPL-2.0 | GPL-2.0-only | false | [source](https://www.mozilla.org/en-US/MPL/2.0/#distribution-of-a-larger-work) |
| 37 | MPL-2.0 | GPL-3.0-only | false | [source](https://www.mozilla.org/en-US/MPL/2.0/#distribution-of-a-larger-work) |
| 38 | MPL-2.0 | GPL-2.0-or-later | false | [source](https://www.mozilla.org/en-US/MPL/2.0/#distribution-of-a-larger-work) |
| 39 | MPL-2.0 | AGPL-3.0-only | false | [source](https://www.mozilla.org/en-US/MPL/2.0/#distribution-of-a-larger-work) |
| 40 | MPL-2.0 | LGPL-2.1-only | false | [source](https://www.mozilla.org/en-US/MPL/2.0/#distribution-of-a-larger-work) |
| 41 | MPL-2.0 | LGPL-3.0-only | false | [source](https://www.mozilla.org/en-US/MPL/2.0/#distribution-of-a-larger-work) |
| 42 | MPL-2.0 | Apache-2.0 | false | [source](https://www.mozilla.org/en-US/MPL/2.0/FAQ/#mpl-bsd-and-apache) |
| 43 | MPL-2.0 | LicenseRef-Closed | false | [source](https://www.mozilla.org/en-US/MPL/2.0/FAQ/#virality) |
| 44 | GPL-2.0-only | LicenseRef-Closed | true | [source](https://spdx.org/licenses/GPL-2.0-only.html#licenseText) |
| 45 | GPL-3.0-only | LicenseRef-Closed | true | [source](https://spdx.org/licenses/GPL-3.0-only.html#licenseText) |
| 46 | GPL-2.0-or-later | LicenseRef-Closed | true | [source](https://spdx.org/licenses/GPL-2.0-only.html#licenseText) |
| 47 | AGPL-3.0-only | LicenseRef-Closed | true | [source](https://spdx.org/licenses/AGPL-3.0-only.html#licenseText) |
| 48 | LGPL-2.1-only | LicenseRef-Closed | null | [source](https://spdx.org/licenses/LGPL-2.1-only.html#licenseText) |
| 49 | LGPL-3.0-only | LicenseRef-Closed | null | [source](https://spdx.org/licenses/LGPL-3.0-only.html#licenseText) |
| 50 | MIT | LicenseRef-Closed | false | [source](https://spdx.org/licenses/MIT.html#licenseText) |
| 51 | BSD-3-Clause | LicenseRef-Closed | false | [source](https://spdx.org/licenses/BSD-3-Clause.html#licenseText) |
| 52 | Apache-2.0 | LicenseRef-Closed | false | [source](https://spdx.org/licenses/Apache-2.0.html#licenseText) |
| 53 | GPL-2.0-only | GPL-2.0-only | false | [source](https://spdx.org/licenses/GPL-2.0-only.html#licenseText) |
| 54 | GPL-3.0-only | GPL-3.0-only | false | [source](https://spdx.org/licenses/GPL-3.0-only.html#licenseText) |

## Rules that change the answer

The first sixteen rows infer a route from the individual SPDX licence texts, corroborated by the named [FSF entries](https://www.gnu.org/licenses/license-list.en.html). Retain each component's notices and conditions. The [SPDX list](https://spdx.org/licenses/) supplies exact identifiers, including version suffixes; it does not certify a particular distribution.

GPL-2.0-only cannot be upgraded merely because a dependency needs GPLv3. GPL-2.0-or-later permits selecting GPLv3, or GPLv2 when the other component requires it. See the [FSF version answer](https://www.gnu.org/licenses/gpl-faq.en.html#v2v3Compatibility) and its [combination matrix](https://www.gnu.org/licenses/gpl-faq.en.html#AllCompatibility).

[ASF's compatibility explanation](https://www.apache.org/licenses/GPL-compatibility.html) distinguishes Apache-2.0 with GPLv3 from fixed GPLv2. A third party can make the GPLv3 combination. ASF's own Apache-only project policy cannot absorb GPL code; that policy does not negate the third-party route.

[LGPL-2.1 section 3](https://spdx.org/licenses/LGPL-2.1-only.html#licenseText) permits conversion to GPLv2 or a newer GPL. The `only` suffix does not delete that clause. Rows 28, 31, 33 and 35 can therefore select GPLv3; this does not make Apache-2.0 compatible with GPLv2. Conversion changes the obligations for that copy and is irreversible. [LGPL-3.0](https://spdx.org/licenses/LGPL-3.0-only.html#licenseText) builds on GPLv3 with additional permissions.

[GPLv3 section 13](https://spdx.org/licenses/GPL-3.0-only.html#licenseText) and [AGPLv3 section 13](https://spdx.org/licenses/AGPL-3.0-only.html#licenseText) allow their combined work; each part retains its own licence and AGPL's network-source conditions apply to the combination. AGPLv3 is not itself a later GPL version: row 23 selects GPLv3 first.

[MPL section 3.3](https://www.mozilla.org/en-US/MPL/2.0/#distribution-of-a-larger-work) permits the qualifying Larger Work route with the secondary licences defined in section 1.12. These rows mean plain MPL-2.0, without incompatible legacy provenance or Exhibit B. The separate [SPDX no-copyleft-exception identifier](https://spdx.org/licenses/MPL-2.0-no-copyleft-exception.html) must not be folded into this fixture's MPL-2.0 label. [Mozilla FAQ Q14](https://www.mozilla.org/en-US/MPL/2.0/FAQ/#mpl-and-lgpl) requires an existing secondary-licensed combination and additional availability under both licences; Q25 explains the default. LGPLv3 is a later version of the listed LGPLv2.1. MPL's separate-file boundary also permits proprietary Larger Works while MPL-covered source remains available. See Q11 and Q13 in that FAQ.

## When a plug-in forms a combined work

The [GPL FAQ plug-in answer](https://www.gnu.org/licenses/gpl-faq.en.html#GPLPlugins) considers shared structures and function calls across dynamic linking evidence of a combined program. Simple invocation with limited communication can be separate or borderline. A process boundary alone is not a reliable exemption: intimate interprocess exchange can also combine programs. This fixture adopts the card's combined-work premise; it does not decide an arbitrary architecture's legal status.

The [nonfree host answer](https://www.gnu.org/licenses/gpl-faq.en.html#GPLPluginsInNF) supplies the strong-copyleft warning case. For LGPL libraries, inspect the actual EULA and packaging against [LGPL-2.1 section 6](https://spdx.org/licenses/LGPL-2.1-only.html#licenseText), [LGPL-3.0 section 4](https://spdx.org/licenses/LGPL-3.0-only.html#licenseText), and the [FAQ on static and dynamic LGPL use](https://www.gnu.org/licenses/gpl-faq.en.html#LGPLStaticVsDynamic). Closed source alone does not answer whether users can replace the library, relink and debug permitted changes.

## Limits and warning policy

**BORROWED: the user's stated warning policy.** Missing licence metadata and known clashes are warnings, never load refusals. This table supplies evidence for that policy without changing its contract.

A flat pair cannot resolve `AND`/`OR` expressions, dual licensing, custom terms, exceptions, ownership permissions, contributor notices, patent grants, trademark rights, jurisdiction or whether the work really is combined. A compatible SPDX pair does not prove its files carry accurate labels. Static linking, dynamic linking, copied source, separate files, independently distributed programs and remote services can entail different duties. An exception must actually cover the relevant component and use. Consult qualified counsel for a release.

No host licence is selected by this research. No recommendation to integrate either game is made.

## Source audit: seeded sample

Python `random.Random(210031).sample(range(1,55),20)` selected rows 17, 1, 45, 3, 41, 18, 42, 22, 32, 34, 14, 37, 40, 15, 23, 33, 21, 47, 27, 16. All twenty were re-read against the sources below and their scoped route or clash checked. GNU direct retrieval timed out; its named sections were reopened through the search service's indexed primary-page text, whose crawl was about five months old. Mozilla, ASF and SPDX bodies were reopened directly. This is a retrieval limitation, not a claim to have downloaded a fresh GNU body.

The quotation bank gives exact deciding sentences where short enough and clearly marked excerpts otherwise. Repeated audit references reuse the same quotation rather than copying a source repeatedly.

| Evidence | Exact quotation | Section |
|---|---|---|
| F-CC0 | “Both public domain works and the lax license provided by CC0 are compatible with the GNU GPL.” | [FSF CC0](https://www.gnu.org/licenses/license-list.en.html#CC0) |
| F-BOOST | “compatible with the GNU GPL.” (deciding excerpt) | [FSF Boost](https://www.gnu.org/licenses/license-list.en.html#boost) |
| MIT | “The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.” | [MIT notice condition](https://spdx.org/licenses/MIT.html#licenseText) |
| BSD2 | “Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:” (permission excerpt) | [BSD-2-Clause grant](https://spdx.org/licenses/BSD-2-Clause.html#licenseText) |
| ASF2 | “never considered the Apache License to be compatible with GPL version 2” (deciding excerpt) | [ASF GPLv2 paragraph](https://www.apache.org/licenses/GPL-compatibility.html) |
| ASF3 | “Apache 2 software can therefore be included in GPLv3 projects” (deciding excerpt) | [ASF GPLv3 paragraph](https://www.apache.org/licenses/GPL-compatibility.html) |
| V2 | “You may not impose any further restrictions on the recipients' exercise of the rights granted herein.” | [GPLv2 section 6](https://spdx.org/licenses/GPL-2.0-only.html#licenseText) |
| FAQ-V | “No.” | [FAQ: Is GPLv3 compatible with GPLv2?](https://www.gnu.org/licenses/gpl-faq.en.html#v2v3Compatibility) |
| FAQ-C | “If they form a single combined program this means that combination of the GPL-covered plug-in with the nonfree main program would violate the GPL.” | [FAQ nonfree host](https://www.gnu.org/licenses/gpl-faq.en.html#GPLPluginsInNF) |
| APACHE3 | “Any GNU license, version N, subsumes the Apache 2.0 license provided N is at least 3.” | [FSF subsumption section](https://www.gnu.org/licenses/license-compatibility.en.html#:~:text=Any%20GNU%20license) |
| L21 | “You may opt to apply the terms of the ordinary GNU General Public License instead of this License to a given copy of the Library.” | [LGPLv2.1 section 3](https://spdx.org/licenses/LGPL-2.1-only.html#licenseText) |
| M14 | “The software must not be “Incompatible With Secondary Licenses.”” | [MPL FAQ Q14 condition 1](https://www.mozilla.org/en-US/MPL/2.0/FAQ/#mpl-and-lgpl) |
| M25 | “It is not considered Incompatible with Secondary Licenses.” | [MPL FAQ Q25](https://www.mozilla.org/en-US/MPL/2.0/FAQ/#full-copy-only) |
| M13 | “Yes to both.” | [MPL FAQ Q13 BSD/Apache combinations](https://www.mozilla.org/en-US/MPL/2.0/FAQ/#mpl-bsd-and-apache) |
| GPL3-B | “permission to link or combine any covered work with a work licensed under version 3 of the GNU Affero General Public License” (deciding excerpt) | [GPLv3 section 13](https://spdx.org/licenses/GPL-3.0-only.html#licenseText) |
| AGPL-C | “You must license the entire work, as a whole, under this License to anyone who comes into possession of a copy.” | [AGPLv3 section 5c](https://spdx.org/licenses/AGPL-3.0-only.html#licenseText) |

The audit judgements below are inferences from those passages plus their surrounding conditions, rather than quotations of a source's conclusion for every possible pair.

| Draw | Row | Checked result and deciding evidence |
|---|---|---|
| 1 | 17 | true: ASF2 and V2 exclude the fixed-v2 route. |
| 2 | 1 | false: MIT grant and notice condition preserve the GPLv2 route; FSF Expat entry agrees. |
| 3 | 45 | true: FAQ-C; defined closed rights deny the GPLv3 combined-work obligations. |
| 4 | 3 | false: BSD2's conditions allow the GPLv2 route; FSF FreeBSD entry agrees. |
| 5 | 41 | false: M14/M25 and MPL sections 1.12/3.3 admit LGPLv3 as a later secondary version. |
| 6 | 18 | true: FAQ-V and V2; fixed versions offer no common option. |
| 7 | 42 | false: M13 confirms the separate-file Apache combination. |
| 8 | 22 | false: ASF3 applies after the or-later component chooses GPLv3. |
| 9 | 32 | false: APACHE3 applies to the LGPLv3-based route; Apache obligations remain. |
| 10 | 34 | false: GPL3-B supplies the bridge for the LGPLv3-based component; AGPL section 13 agrees. |
| 11 | 14 | false: F-CC0 covers this GPL route; no patent clearance is implied. |
| 12 | 37 | false: M14/M25 and MPL sections 1.12/3.3 admit GPLv3. |
| 13 | 40 | false: M14/M25 and MPL sections 1.12/3.3 explicitly admit LGPLv2.1. |
| 14 | 15 | false: F-BOOST; retained Boost conditions permit GPLv2 distribution. |
| 15 | 23 | false: choose GPLv3, then GPL3-B; do not label AGPL a later GPL. |
| 16 | 33 | false: L21's next sentences permit newer GPL conversion, then GPL3-B supplies the bridge. |
| 17 | 21 | false: FAQ-V's following paragraph allows GPLv3 when the original notice says or-later. |
| 18 | 47 | true: AGPL-C requires rights the defined closed component withholds. |
| 19 | 27 | false: L21 and the following notice-conversion sentence explicitly allow GPLv2. |
| 20 | 16 | false: F-BOOST permits the GPLv3 route as well. |

Result: 20/20 scoped judgements passed. The LGPL/Closed nulls were additionally inspected against both linked sources; no unconditional compatibility was inferred from a proprietary label. Section anchors on Mozilla were checked against the live HTML, replacing guessed heading slugs before hand-in.
