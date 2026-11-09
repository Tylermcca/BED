# JSON Schema official-suite fixtures

BORROWED/MIT: official JSON-Schema-Test-Suite fixture bytes and LICENSE, copied unchanged by explicit A026 exception. INVENTED: selection, inventory, and the companion black-box harness. No private/game/workbook/art content.

- Upstream: https://github.com/json-schema-org/JSON-Schema-Test-Suite
- Pinned commit: `3f05891cac5d51ffa068d7076d1372130e00c829`
- Upstream commit date: 2026-10-31T17:59:30Z
- Retrieved UTC date: 2026-10-31
- License checked read-only before copying: MIT, copyright (c) 2012 Julian Berman. Original notice and permission text are preserved verbatim in LICENSE.
- LICENSE SHA-256: `837402bd25fad9b704265801ca3f92566a98157c1f9a7acd6f446299ba1c305a`

## Scope and coverage

Includes every group using only the public supported keywords (including ignored annotations), boolean schemas, and local references in all draft2020-12 files, including relevant optional tests. Supported-schema positions are walked, so arbitrary property names/data are not mistaken for keywords. A URI-shaped $id alone does not exclude a group: references to that document or embedded resource remain local. All unsupported-keyword groups and external-resource references are explicitly inventoried below. Each excluded group excludes all its cases; copied files/groups remain byte-for-byte complete. Unsupported cases are scope exclusions, not suite mismatches. No network retrieval occurs in tests.

- Upstream files: 80; copied files: 41; entirely omitted files: 39.
- Upstream cases: 2355; included rows: 887; excluded cases: 1468.

## Complete file inventory

All paths below are relative to upstream tests/draft2020-12. Included is the number of Theory rows. Hashes are SHA-256 of original upstream bytes (including omitted-file inventory).

| File | Groups | Cases | Included | Copy | Bytes | SHA-256 |
|---|---:|---:|---:|---|---:|---|
| additionalProperties.json | 9 | 21 | 18 | vendored | 7711 | `b96bd533416386ac6421737b48c5c072870ab5dbea910585e9e8fa6305c7ca91` |
| allOf.json | 12 | 30 | 30 | vendored | 8701 | `81045b06706a28f6aa337b485b41a764098e10ac73bb1d346ba0a4285a63e970` |
| anchor.json | 4 | 8 | 0 | not copied | 3502 | `6f60e0782be6072733701459684cadf3741ddd60d96b14698b4d9a19075e9f1e` |
| anyOf.json | 8 | 18 | 18 | vendored | 5461 | `7a6fe41602496c256334ffa3e6fb457b26ac23b97ca03cb16b724ad21a7d216d` |
| boolean_schema.json | 2 | 18 | 18 | vendored | 2818 | `9c8828d325b88a0c632fca22405d5481548b49fb0f9f5536ee401e4d66aa8582` |
| const.json | 17 | 54 | 54 | vendored | 12413 | `83a148d2589cbd211e7e64b31763290f8869d763f513de4658e8d25f0fcc025e` |
| contains.json | 7 | 21 | 21 | vendored | 5174 | `086782fc8b54ac77a4a2afe8b84f8cc7fc9ca7b22473ebd8db3480f2eb8d0ab6` |
| content.json | 4 | 18 | 0 | not copied | 4397 | `9b08c408d32cf64f1966aa300cc246355799d883ed278a61d720493f61711bab` |
| default.json | 3 | 7 | 7 | vendored | 2444 | `db4a8382727f6c687a0fc7bb24347e010c3fcfabd1207150ce025e1a763f6094` |
| defs.json | 1 | 2 | 0 | not copied | 637 | `5a352f0b1ad9aa906758fa4d363a77c4c4b29cfc193e8d6f7ab1e184be2a75d5` |
| dependentRequired.json | 4 | 20 | 20 | vendored | 4294 | `0824c393115498ac1a62551c3174a3ccd54ee4ef323d5eb9de0dfe3a185ad666` |
| dependentSchemas.json | 4 | 20 | 0 | not copied | 4973 | `f77df8d5243c0f58e04053a14a30706f5a6d5a9e7eccbfd17f8d6445841f8752` |
| dynamicRef.json | 21 | 44 | 0 | not copied | 29713 | `dd763124c3d1432977b9c276bf682fa746f82a69b1d9bbc1d79fbdb2a3dda5e4` |
| enum.json | 16 | 53 | 53 | vendored | 11831 | `1abe7eb883bde4a78c62f08e484e16f32b72ca5c1be725aa54d85cc4546b5031` |
| exclusiveMaximum.json | 1 | 4 | 4 | vendored | 846 | `e37fc110c48e2a9de564579ceba3e8ab098a36f904dcc1ef02f8cf7b59dc0473` |
| exclusiveMinimum.json | 1 | 4 | 4 | vendored | 846 | `0395a2423606d07943d3d355d1c9c4a474cc7cb544d3c8f266ceab801c5f0a6d` |
| format.json | 19 | 133 | 0 | not copied | 25644 | `3baf934e2aeace523012f92994e3c9f4b6142d68ba9ece0c12becaffa52eaf5f` |
| if-then-else.json | 12 | 30 | 30 | vendored | 8774 | `ca6fc50a73f054ac5cf55b6981257933995d417b26d1f5426ec60e9b5db61c3e` |
| infinite-loop-detection.json | 1 | 2 | 2 | vendored | 1051 | `9fffe850049048a0e411133f7e5fec347f97c077bde65b34c67dadf96e046bcc` |
| items.json | 10 | 29 | 29 | vendored | 9209 | `d924a8171f1f1d04ff026a4e3491d1376297145563158543005b7073b69ab913` |
| maxContains.json | 5 | 14 | 0 | not copied | 3626 | `2d7cf7557b87b15a08457badc66905593834bbdda98f325a22d136dfdefe81ed` |
| maxItems.json | 2 | 6 | 6 | vendored | 1319 | `59fa6236c0766aaac5fb7c9ef82e205c230efa94271485f614218291be7cbc5c` |
| maxLength.json | 2 | 7 | 7 | vendored | 1483 | `ac43a3991586f2d2d1e7e37cad01bb1a7b1c5d9ad7b558d6029d24b41a04d1a9` |
| maxProperties.json | 3 | 10 | 10 | vendored | 2552 | `4f1d14ac74862f134e6769884a7e72d47328daf91e2c6b475ae5f99891e3477d` |
| maximum.json | 2 | 8 | 8 | vendored | 1660 | `dfbc88ef4ede2966250fb2ad44076856d51481ea01ce679cca70efe47606ee90` |
| minContains.json | 8 | 28 | 0 | not copied | 6604 | `2aca05e48dbdf605fe14c775f5470061cf4c8b679e3e6a955a18334ea97d3c23` |
| minItems.json | 2 | 6 | 6 | vendored | 1302 | `9792cedc446be654836b42d0312eca12051e9e2f24cd5d95dcd3f3fc9adccd0a` |
| minLength.json | 2 | 7 | 7 | vendored | 1473 | `999e9731c57a1296e06e8b9b36b0531614ab7a6ac3c20db6fed50badbf3850ab` |
| minProperties.json | 2 | 10 | 10 | vendored | 1913 | `00f641408d1dc3ff81743e0c730eb430ff818e3bbe37007a64138bd513ade05a` |
| minimum.json | 2 | 11 | 11 | vendored | 2116 | `f6a29cb574b76621da23636a636cf12cb31b01646b89543e6ce1a813376439a3` |
| multipleOf.json | 5 | 11 | 11 | vendored | 2808 | `ada35b586efe55186d35d8888a4e4954724c313253b59bd47ec03637a44cb4ad` |
| not.json | 9 | 40 | 38 | vendored | 8420 | `1fd6ef263efd365680c6aaf8f23aa5430d7b9f80b84f46b1208b1ed8353c28b4` |
| oneOf.json | 11 | 27 | 27 | vendored | 8123 | `a8d80a786d07769cb9b8b9a8903ca4f7aa564446ae6b781c7f7f6ffea11ed363` |
| optional/anchor.json | 1 | 4 | 0 | not copied | 1920 | `5489247c9d8e1a6513137cf0f979cf0fc7b391d4e4e65db0c9833afe30f0a1fa` |
| optional/bignum.json | 7 | 9 | 9 | vendored | 3414 | `52608148c500b3c4eeab2066317939cee5901991c38a234f4cc6179289cd8bf4` |
| optional/cross-draft.json | 1 | 1 | 0 | not copied | 620 | `de66c92876b3ba15ca43f2018ba6cd2451d4a5c8867aae5e7e660a31b3dba0cd` |
| optional/dependencies-compatibility.json | 7 | 36 | 0 | not copied | 8144 | `309878857448fb606bb0809c88c8756100a16bc5fbb976ffbb26721468ccd1d2` |
| optional/dynamicRef.json | 1 | 2 | 0 | not copied | 1875 | `eb6e0dfc35dc5d6cc075cbdfd79f96206d827b249017ab4a2c442255985a5384` |
| optional/ecmascript-regex.json | 20 | 74 | 74 | vendored | 20017 | `8ee6227f32327b5debb30a78088636f5eef3968840f685c615d4be636fd90c92` |
| optional/float-overflow.json | 1 | 1 | 1 | vendored | 466 | `bfccb3bf6e4eb6f15a572dccc0e7ad93e6ff24911991355fd9ba2d75deae02e1` |
| optional/format-assertion.json | 2 | 4 | 0 | not copied | 1839 | `5ddc556bc2e991b36070aa116ee19d347ef778eb1b7f00efc70da0bae90005be` |
| optional/format/date-time.json | 1 | 47 | 0 | not copied | 9276 | `8373dc33291aa26f3539f4c9df298f2ec3ee9827f3b18f2e58417e894cdb08f6` |
| optional/format/date.json | 1 | 81 | 0 | not copied | 15157 | `db4a534a3deb8c760eb589c70141573b710f7d7511d68212a636b4c2663c9da2` |
| optional/format/duration.json | 1 | 51 | 0 | not copied | 8671 | `6196f87b176da1c56f71d82074823a3449d3bc03ed856b263e9e9ea788a4790d` |
| optional/format/ecmascript-regex.json | 6 | 12 | 0 | not copied | 3535 | `7604353236f9873e84ae854cc0782be39878dff6f1e93c520ef37c86dbbe8841` |
| optional/format/email.json | 1 | 71 | 0 | not copied | 13314 | `2d0d8295bd24a445434d016aa8edde7650c0432a83c9106fcf3fbf232b45c79a` |
| optional/format/hostname.json | 2 | 66 | 0 | not copied | 16657 | `7f9642bc5a9e063c552987bc4adff7387eedcb32e4fc3e500b4932faa38a7ac7` |
| optional/format/idn-email.json | 1 | 24 | 0 | not copied | 6243 | `800790e2d8122c05d2fbb5ebcd2d84c80979ceefb03c0f8dc7e307c35a975672` |
| optional/format/idn-hostname.json | 2 | 101 | 0 | not copied | 27545 | `75c21f48aaf6798e3fd73afba365c8964aa10c5f6c1251acab80280579bbc1a0` |
| optional/format/ipv4.json | 1 | 41 | 0 | not copied | 7329 | `bb519c507d351de09bbb8284a8ab06da4e115b5c2115f2f2525d516be34169c5` |
| optional/format/ipv6.json | 1 | 51 | 0 | not copied | 9017 | `39c66aa91e6d764f0a5e94a9b36c4e1a2db11f388c5ff32d5439bafe32072533` |
| optional/format/iri-reference.json | 1 | 21 | 0 | not copied | 3810 | `c9807eba7de6833ce3c3b21d434926cadba5d1b1ed51607df68d300ea3c54680` |
| optional/format/iri.json | 1 | 26 | 0 | not copied | 4984 | `2936dfa842668eef76a0077d8cc10b17df692a662f83af9880ef7011f1fdc5f7` |
| optional/format/json-pointer.json | 1 | 40 | 0 | not copied | 7203 | `0388151d9484b9dd10b734431a8dd742b52c2495b3ba9377c941d03faf099bc1` |
| optional/format/regex.json | 1 | 18 | 0 | not copied | 3494 | `9afae3c2fa67bc1fad234dad211cb4172bd7aa10a9b527536822907c2dfe38bd` |
| optional/format/relative-json-pointer.json | 1 | 26 | 0 | not copied | 4557 | `8bbe1d6bd8a355435fdfafccf143ca338b2530fa486315ce696f109d8e1fb869` |
| optional/format/time.json | 1 | 56 | 0 | not copied | 11332 | `053320b46f1dd2ff52faecbf04e3222daf81ab0cf908336a474edb7024a2c091` |
| optional/format/unknown.json | 1 | 7 | 0 | not copied | 1294 | `34278f85cf5648d5bb054fa83ee022dd6603e63eefc79bb118d9c72130acc67e` |
| optional/format/uri-reference.json | 1 | 31 | 0 | not copied | 7771 | `3a9913d43edd31650d3c4f8cc75b2b701bd6670c514cc759f4586245f72c59cb` |
| optional/format/uri-template.json | 1 | 48 | 0 | not copied | 8415 | `9aab180ff02f8648ad080d26d0ce2cced4e379eef2babd8fc2f289c7fb8e3641` |
| optional/format/uri.json | 1 | 48 | 0 | not copied | 9115 | `661c433e8fd7a1abf794827e0c7c108daa2e0f39ee6b1bcae7f3377c7d2f8bed` |
| optional/format/uuid.json | 1 | 29 | 0 | not copied | 5371 | `d62b676e83a346a6fe4ad1812a4e8da1e7b99a15c5cfb9e9faa3168c37b9365e` |
| optional/id.json | 1 | 3 | 3 | vendored | 1859 | `4d0a3082a630b5a2dcc605000f0129f481da400019b8346f0b3cf77192f0acc8` |
| optional/no-schema.json | 1 | 3 | 3 | vendored | 670 | `8ddd0dc73d767875606b19732b63537b6e00ccfe2aa7e18bdc274a8b934a30c5` |
| optional/non-bmp-regex.json | 2 | 12 | 12 | vendored | 2560 | `b01f24ba860f74baad227988571d44edd6461f51dc5319e418802f2f789721f1` |
| optional/refOfUnknownKeyword.json | 5 | 10 | 2 | vendored | 3234 | `deb7915d8926158fbc5b4a6c12383486c31adedbe025620af8b466bcb09b743c` |
| optional/unknownKeyword.json | 1 | 3 | 0 | not copied | 2123 | `dc66418e962824759cbf5eb6c5a655de320ffeecbb77fd42d50b6e95f9bccecb` |
| pattern.json | 3 | 12 | 12 | vendored | 2461 | `60713a3b01957fad54f00ab30b378463b3e07b35242c945a9325f8d6ae7080fc` |
| patternProperties.json | 6 | 26 | 26 | vendored | 6570 | `aa73ad1e6eac3cee42bc79628f86d813ec80a88c432f62bfca8add91326dd924` |
| prefixItems.json | 4 | 11 | 11 | vendored | 2929 | `5b2006fb0d99f9df8e25e384d665edc57427e7849ac0f963f541626fd82bb522` |
| properties.json | 6 | 28 | 28 | vendored | 7731 | `58f01566cfe3e1fc4fb851d3b2eab13768d65ba5dbc4e9f9387ceaf4244a0522` |
| propertyNames.json | 6 | 22 | 22 | vendored | 5171 | `54469f831102465060d0db9a9611e827fb7e87df38179d8a28ac45c10576ef83` |
| ref.json | 36 | 79 | 72 | vendored | 33550 | `ae53f3f57c220879729225eb416cecac909f06b5adaf15f799b4f3e7c0612998` |
| refRemote.json | 15 | 31 | 0 | not copied | 10306 | `8fcb97acb2b76308cab9d82eccd7388dd7d6ca59cf68f6d03281bc40adc9c7ee` |
| required.json | 5 | 18 | 18 | vendored | 4902 | `3e3900dd0e546c1cb4aaab6b24ea0e06a8f7f8c05b272dcc87e85332501ed42e` |
| type.json | 11 | 80 | 80 | vendored | 14365 | `4c5cbe6cbcd28af73761091367b20e07d0403847e236c06c31fc27061bd81192` |
| unevaluatedItems.json | 29 | 71 | 0 | not copied | 25395 | `477e7e734ba86a76fc605d34afa6867ab25081b072792c2d853cbe666e42b7da` |
| unevaluatedProperties.json | 44 | 129 | 0 | not copied | 50424 | `dbd5b401b09de49215f33ed7b30f4b9cd4a27e439624ad2a6aeb3a3879cac1dd` |
| uniqueItems.json | 6 | 65 | 65 | vendored | 13886 | `20f7c3decb4ff491a722253181ffef351acd23e39ecbbfeea40941f3623cc27a` |
| vocabulary.json | 2 | 5 | 0 | not copied | 1706 | `7c753d28de2d551fe0329b5a0ac939de921ffe1dfd1c82336e8d4a78d904ce8a` |

## Exact excluded groups

Zero-based group indexes match unchanged upstream files; case count covers every test in that group. Entirely omitted files are the inventory rows with Included = 0.

| File | Group | Excluded cases | Group description | Reason |
|---|---:|---:|---|---|
| additionalProperties.json | 8 | 3 | dependentSchemas with additionalProperties | unsupported keywords: dependentSchemas |
| anchor.json | 0 | 2 | Location-independent identifier | unsupported keywords: $anchor |
| anchor.json | 1 | 2 | Location-independent identifier with absolute URI | unsupported keywords: $anchor |
| anchor.json | 2 | 2 | Location-independent identifier with base URI change in subschema | unsupported keywords: $anchor |
| anchor.json | 3 | 2 | same $anchor with different base uri | unsupported keywords: $anchor |
| content.json | 0 | 3 | validation of string-encoded content based on media type | unsupported keywords: contentMediaType |
| content.json | 1 | 3 | validation of binary string-encoding | unsupported keywords: contentEncoding |
| content.json | 2 | 4 | validation of binary-encoded media type documents | unsupported keywords: contentEncoding, contentMediaType |
| content.json | 3 | 8 | validation of binary-encoded media type documents with schema | unsupported keywords: contentEncoding, contentMediaType, contentSchema |
| defs.json | 0 | 2 | validate definition against metaschema | external/remote reference: https://json-schema.org/draft/2020-12/schema |
| dependentSchemas.json | 0 | 8 | single dependency | unsupported keywords: dependentSchemas |
| dependentSchemas.json | 1 | 4 | boolean subschemas | unsupported keywords: dependentSchemas |
| dependentSchemas.json | 2 | 4 | dependencies with escaped characters | unsupported keywords: dependentSchemas |
| dependentSchemas.json | 3 | 4 | dependent subschema incompatible with root | unsupported keywords: dependentSchemas |
| dynamicRef.json | 0 | 2 | A $dynamicRef to a $dynamicAnchor in the same schema resource behaves like a normal $ref to an $anchor | unsupported keywords: $dynamicAnchor, $dynamicRef |
| dynamicRef.json | 1 | 2 | A $dynamicRef to an $anchor in the same schema resource behaves like a normal $ref to an $anchor | unsupported keywords: $anchor, $dynamicRef |
| dynamicRef.json | 2 | 2 | A $ref to a $dynamicAnchor in the same schema resource behaves like a normal $ref to an $anchor | unsupported keywords: $dynamicAnchor |
| dynamicRef.json | 3 | 2 | A $dynamicRef resolves to the first $dynamicAnchor still in scope that is encountered when the schema is evaluated | unsupported keywords: $dynamicAnchor, $dynamicRef |
| dynamicRef.json | 4 | 2 | A $dynamicRef without anchor in fragment behaves identical to $ref | unsupported keywords: $dynamicAnchor, $dynamicRef |
| dynamicRef.json | 5 | 2 | A $dynamicRef with intermediate scopes that don't include a matching $dynamicAnchor does not affect dynamic scope resolution | unsupported keywords: $dynamicAnchor, $dynamicRef |
| dynamicRef.json | 6 | 1 | An $anchor with the same name as a $dynamicAnchor is not used for dynamic scope resolution | unsupported keywords: $anchor, $dynamicAnchor, $dynamicRef |
| dynamicRef.json | 7 | 1 | A $dynamicRef without a matching $dynamicAnchor in the same schema resource behaves like a normal $ref to $anchor | unsupported keywords: $anchor, $dynamicAnchor, $dynamicRef |
| dynamicRef.json | 8 | 1 | A $dynamicRef with a non-matching $dynamicAnchor in the same schema resource behaves like a normal $ref to $anchor | unsupported keywords: $anchor, $dynamicAnchor, $dynamicRef |
| dynamicRef.json | 9 | 2 | A $dynamicRef that initially resolves to a schema with a matching $dynamicAnchor resolves to the first $dynamicAnchor in the dynamic scope | unsupported keywords: $dynamicAnchor, $dynamicRef |
| dynamicRef.json | 10 | 1 | A $dynamicRef that initially resolves to a schema without a matching $dynamicAnchor behaves like a normal $ref to $anchor | unsupported keywords: $anchor, $dynamicAnchor, $dynamicRef |
| dynamicRef.json | 11 | 4 | multiple dynamic paths to the $dynamicRef keyword | unsupported keywords: $dynamicAnchor, $dynamicRef |
| dynamicRef.json | 12 | 3 | after leaving a dynamic scope, it is not used by a $dynamicRef | unsupported keywords: $dynamicAnchor, $dynamicRef |
| dynamicRef.json | 13 | 2 | strict-tree schema, guards against misspelled properties | unsupported keywords: $dynamicAnchor, unevaluatedProperties |
| dynamicRef.json | 14 | 3 | tests for implementation dynamic anchor and reference link | unsupported keywords: $dynamicAnchor |
| dynamicRef.json | 15 | 3 | $ref and $dynamicAnchor are independent of order - $defs first | unsupported keywords: $dynamicAnchor |
| dynamicRef.json | 16 | 3 | $ref and $dynamicAnchor are independent of order - $ref first | unsupported keywords: $dynamicAnchor |
| dynamicRef.json | 17 | 2 | $ref to $dynamicRef finds detached $dynamicAnchor | external/remote reference: http://localhost:1234/draft2020-12/detached-dynamicref.json#/$defs/foo |
| dynamicRef.json | 18 | 2 | $dynamicRef points to a boolean schema | unsupported keywords: $dynamicRef |
| dynamicRef.json | 19 | 2 | $dynamicRef skips over intermediate resources - direct reference | unsupported keywords: $dynamicAnchor, $dynamicRef |
| dynamicRef.json | 20 | 2 | $dynamicRef avoids the root of each schema, but scopes are still registered | unsupported keywords: $dynamicAnchor, $dynamicRef |
| format.json | 0 | 7 | email format | unsupported keywords: format |
| format.json | 1 | 7 | idn-email format | unsupported keywords: format |
| format.json | 2 | 7 | regex format | unsupported keywords: format |
| format.json | 3 | 7 | ipv4 format | unsupported keywords: format |
| format.json | 4 | 7 | ipv6 format | unsupported keywords: format |
| format.json | 5 | 7 | idn-hostname format | unsupported keywords: format |
| format.json | 6 | 7 | hostname format | unsupported keywords: format |
| format.json | 7 | 7 | date format | unsupported keywords: format |
| format.json | 8 | 7 | date-time format | unsupported keywords: format |
| format.json | 9 | 7 | time format | unsupported keywords: format |
| format.json | 10 | 7 | json-pointer format | unsupported keywords: format |
| format.json | 11 | 7 | relative-json-pointer format | unsupported keywords: format |
| format.json | 12 | 7 | iri format | unsupported keywords: format |
| format.json | 13 | 7 | iri-reference format | unsupported keywords: format |
| format.json | 14 | 7 | uri format | unsupported keywords: format |
| format.json | 15 | 7 | uri-reference format | unsupported keywords: format |
| format.json | 16 | 7 | uri-template format | unsupported keywords: format |
| format.json | 17 | 7 | uuid format | unsupported keywords: format |
| format.json | 18 | 7 | duration format | unsupported keywords: format |
| maxContains.json | 0 | 2 | maxContains without contains is ignored | unsupported keywords: maxContains |
| maxContains.json | 1 | 5 | maxContains with contains | unsupported keywords: maxContains |
| maxContains.json | 2 | 2 | maxContains with contains, value with a decimal | unsupported keywords: maxContains |
| maxContains.json | 3 | 3 | minContains < maxContains | unsupported keywords: maxContains, minContains |
| maxContains.json | 4 | 2 | maxContains = 0 with minContains = 0 | unsupported keywords: maxContains, minContains |
| minContains.json | 0 | 2 | minContains without contains is ignored | unsupported keywords: minContains |
| minContains.json | 1 | 5 | minContains=1 with contains | unsupported keywords: minContains |
| minContains.json | 2 | 6 | minContains=2 with contains | unsupported keywords: minContains |
| minContains.json | 3 | 2 | minContains=2 with contains with a decimal value | unsupported keywords: minContains |
| minContains.json | 4 | 4 | maxContains = minContains | unsupported keywords: maxContains, minContains |
| minContains.json | 5 | 4 | maxContains < minContains | unsupported keywords: maxContains, minContains |
| minContains.json | 6 | 2 | minContains = 0 | unsupported keywords: minContains |
| minContains.json | 7 | 3 | minContains = 0 with maxContains | unsupported keywords: maxContains, minContains |
| not.json | 8 | 2 | collect annotations inside a 'not', even if collection is disabled | unsupported keywords: unevaluatedProperties |
| optional/anchor.json | 0 | 4 | $anchor inside an enum is not a real identifier | unsupported keywords: $anchor |
| optional/cross-draft.json | 0 | 1 | refs to historic drafts are processed as historic drafts | external/remote reference: http://localhost:1234/draft2019-09/ignore-prefixItems.json |
| optional/dependencies-compatibility.json | 0 | 7 | single dependency | unsupported keywords: dependencies |
| optional/dependencies-compatibility.json | 1 | 3 | empty dependents | unsupported keywords: dependencies |
| optional/dependencies-compatibility.json | 2 | 6 | multiple dependents required | unsupported keywords: dependencies |
| optional/dependencies-compatibility.json | 3 | 4 | dependencies with escaped characters | unsupported keywords: dependencies |
| optional/dependencies-compatibility.json | 4 | 8 | single schema dependency | unsupported keywords: dependencies |
| optional/dependencies-compatibility.json | 5 | 4 | boolean subschemas | unsupported keywords: dependencies |
| optional/dependencies-compatibility.json | 6 | 4 | schema dependencies with escaped characters | unsupported keywords: dependencies |
| optional/dynamicRef.json | 0 | 2 | $dynamicRef skips over intermediate resources - pointer reference across resource boundary | unsupported keywords: $dynamicAnchor, $dynamicRef |
| optional/format-assertion.json | 0 | 2 | schema that uses custom metaschema with format-assertion: false | unsupported keywords: format |
| optional/format-assertion.json | 1 | 2 | schema that uses custom metaschema with format-assertion: true | unsupported keywords: format |
| optional/format/date-time.json | 0 | 47 | validation of date-time strings | unsupported keywords: format |
| optional/format/date.json | 0 | 81 | validation of date strings | unsupported keywords: format |
| optional/format/duration.json | 0 | 51 | validation of duration strings | unsupported keywords: format |
| optional/format/ecmascript-regex.json | 0 | 1 | \a is not an ECMA 262 control escape | unsupported keywords: format |
| optional/format/ecmascript-regex.json | 1 | 3 | Python-specific regular expression syntax is not valid ECMA 262 | unsupported keywords: format |
| optional/format/ecmascript-regex.json | 2 | 2 | global inline flag groups are not valid ECMA 262 | unsupported keywords: format |
| optional/format/ecmascript-regex.json | 3 | 2 | ECMA 262 named groups and backreferences are valid | unsupported keywords: format |
| optional/format/ecmascript-regex.json | 4 | 1 | ECMA 262 lookbehind is valid, including variable width | unsupported keywords: format |
| optional/format/ecmascript-regex.json | 5 | 3 | ECMA 262 character classes and escapes | unsupported keywords: format |
| optional/format/email.json | 0 | 71 | validation of e-mail addresses | unsupported keywords: format |
| optional/format/hostname.json | 0 | 28 | validation of host names | unsupported keywords: format |
| optional/format/hostname.json | 1 | 38 | validation of A-label (punycode) host names | unsupported keywords: format |
| optional/format/idn-email.json | 0 | 24 | validation of an internationalized e-mail addresses | unsupported keywords: format |
| optional/format/idn-hostname.json | 0 | 81 | validation of internationalized host names | unsupported keywords: format |
| optional/format/idn-hostname.json | 1 | 20 | validation of separators in internationalized host names | unsupported keywords: format |
| optional/format/ipv4.json | 0 | 41 | validation of IP addresses | unsupported keywords: format |
| optional/format/ipv6.json | 0 | 51 | validation of IPv6 addresses | unsupported keywords: format |
| optional/format/iri-reference.json | 0 | 21 | validation of IRI References | unsupported keywords: format |
| optional/format/iri.json | 0 | 26 | validation of IRIs | unsupported keywords: format |
| optional/format/json-pointer.json | 0 | 40 | validation of JSON-pointers (JSON String Representation) | unsupported keywords: format |
| optional/format/regex.json | 0 | 18 | validation of regular expressions | unsupported keywords: format |
| optional/format/relative-json-pointer.json | 0 | 26 | validation of Relative JSON Pointers (RJP) | unsupported keywords: format |
| optional/format/time.json | 0 | 56 | validation of time strings | unsupported keywords: format |
| optional/format/unknown.json | 0 | 7 | unknown format | unsupported keywords: format |
| optional/format/uri-reference.json | 0 | 31 | validation of URI References | unsupported keywords: format |
| optional/format/uri-template.json | 0 | 48 | format: uri-template | unsupported keywords: format |
| optional/format/uri.json | 0 | 48 | validation of URIs | unsupported keywords: format |
| optional/format/uuid.json | 0 | 29 | uuid format | unsupported keywords: format |
| optional/refOfUnknownKeyword.json | 0 | 2 | reference of a root arbitrary keyword  | unsupported keywords: unknown-keyword |
| optional/refOfUnknownKeyword.json | 1 | 2 | reference of a root arbitrary keyword with encoded ref | unsupported keywords: unknown/keyword |
| optional/refOfUnknownKeyword.json | 2 | 2 | reference of an arbitrary keyword of a sub-schema | unsupported keywords: unknown-keyword |
| optional/refOfUnknownKeyword.json | 4 | 2 | reference of an arbitrary keyword of a sub-schema with encoded ref | unsupported keywords: unknown/keyword |
| optional/unknownKeyword.json | 0 | 3 | $id inside an unknown keyword is not a real identifier | unsupported keywords: array_of_schemas, object_of_schemas |
| ref.json | 6 | 2 | remote ref, containing refs itself | external/remote reference: https://json-schema.org/draft/2020-12/schema |
| ref.json | 13 | 1 | ref creates new scope when adjacent to keywords | unsupported keywords: unevaluatedProperties |
| ref.json | 19 | 2 | order of evaluation: $id and $anchor and $ref | unsupported keywords: $anchor |
| ref.json | 27 | 2 | URN base URI with URN and anchor ref | unsupported keywords: $anchor |
| refRemote.json | 0 | 2 | remote ref | remote-reference file explicitly out of scope |
| refRemote.json | 1 | 2 | fragment within remote ref | remote-reference file explicitly out of scope |
| refRemote.json | 2 | 2 | anchor within remote ref | remote-reference file explicitly out of scope |
| refRemote.json | 3 | 2 | ref within remote ref | remote-reference file explicitly out of scope |
| refRemote.json | 4 | 2 | base URI change | remote-reference file explicitly out of scope |
| refRemote.json | 5 | 2 | base URI change - change folder | remote-reference file explicitly out of scope |
| refRemote.json | 6 | 2 | base URI change - change folder in subschema | remote-reference file explicitly out of scope |
| refRemote.json | 7 | 3 | root ref in remote ref | remote-reference file explicitly out of scope |
| refRemote.json | 8 | 2 | remote ref with ref to defs | remote-reference file explicitly out of scope |
| refRemote.json | 9 | 2 | Location-independent identifier in remote ref | remote-reference file explicitly out of scope |
| refRemote.json | 10 | 2 | retrieved nested refs resolve relative to their URI not $id | remote-reference file explicitly out of scope |
| refRemote.json | 11 | 2 | remote HTTP ref with different $id | remote-reference file explicitly out of scope |
| refRemote.json | 12 | 2 | remote HTTP ref with different URN $id | remote-reference file explicitly out of scope |
| refRemote.json | 13 | 2 | remote HTTP ref with nested absolute ref | remote-reference file explicitly out of scope |
| refRemote.json | 14 | 2 | $ref to $ref finds detached $anchor | remote-reference file explicitly out of scope |
| unevaluatedItems.json | 0 | 2 | unevaluatedItems true | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 1 | 2 | unevaluatedItems false | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 2 | 3 | unevaluatedItems as schema | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 3 | 1 | unevaluatedItems with uniform items | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 4 | 2 | unevaluatedItems with tuple | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 5 | 1 | unevaluatedItems with items and prefixItems | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 6 | 2 | unevaluatedItems with items | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 7 | 2 | unevaluatedItems with nested tuple | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 8 | 3 | unevaluatedItems with nested items | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 9 | 2 | unevaluatedItems with nested prefixItems and items | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 10 | 2 | unevaluatedItems with nested unevaluatedItems | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 11 | 4 | unevaluatedItems with anyOf | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 12 | 2 | unevaluatedItems with oneOf | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 13 | 1 | unevaluatedItems with not | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 14 | 4 | unevaluatedItems with if/then/else | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 15 | 2 | unevaluatedItems with boolean schemas | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 16 | 2 | unevaluatedItems with $ref | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 17 | 2 | unevaluatedItems before $ref | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 18 | 2 | unevaluatedItems with $dynamicRef | unsupported keywords: $dynamicAnchor, $dynamicRef, unevaluatedItems |
| unevaluatedItems.json | 19 | 1 | unevaluatedItems can't see inside cousins | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 20 | 2 | item is evaluated in an uncle schema to unevaluatedItems | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 21 | 3 | unevaluatedItems depends on adjacent contains | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 22 | 2 | unevaluatedItems depends on multiple nested contains | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 23 | 8 | unevaluatedItems and contains interact to control item dependency relationship | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 24 | 4 | unevaluatedItems with minContains = 0 | unsupported keywords: minContains, unevaluatedItems |
| unevaluatedItems.json | 25 | 6 | non-array instances are valid | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 26 | 1 | unevaluatedItems with null instance elements | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 27 | 2 | unevaluatedItems can see annotations from if without then and else | unsupported keywords: unevaluatedItems |
| unevaluatedItems.json | 28 | 1 | Evaluated items collection needs to consider instance location | unsupported keywords: unevaluatedItems |
| unevaluatedProperties.json | 0 | 2 | unevaluatedProperties true | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 1 | 3 | unevaluatedProperties schema | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 2 | 2 | unevaluatedProperties false | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 3 | 2 | unevaluatedProperties with adjacent properties | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 4 | 2 | unevaluatedProperties with adjacent patternProperties | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 5 | 2 | unevaluatedProperties with adjacent bool additionalProperties | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 6 | 2 | unevaluatedProperties with adjacent non-bool additionalProperties | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 7 | 2 | unevaluatedProperties with nested properties | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 8 | 2 | unevaluatedProperties with nested patternProperties | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 9 | 2 | unevaluatedProperties with nested additionalProperties | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 10 | 2 | unevaluatedProperties with nested unevaluatedProperties | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 11 | 4 | unevaluatedProperties with anyOf | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 12 | 2 | unevaluatedProperties with oneOf | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 13 | 1 | unevaluatedProperties with not | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 14 | 4 | unevaluatedProperties with if/then/else | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 15 | 4 | unevaluatedProperties with if/then/else, then not defined | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 16 | 4 | unevaluatedProperties with if/then/else, else not defined | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 17 | 2 | unevaluatedProperties with dependentSchemas | unsupported keywords: dependentSchemas, unevaluatedProperties |
| unevaluatedProperties.json | 18 | 2 | unevaluatedProperties with boolean schemas | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 19 | 2 | unevaluatedProperties with $ref | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 20 | 2 | unevaluatedProperties before $ref | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 21 | 2 | unevaluatedProperties with $dynamicRef | unsupported keywords: $dynamicAnchor, $dynamicRef, unevaluatedProperties |
| unevaluatedProperties.json | 22 | 1 | unevaluatedProperties can't see inside cousins | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 23 | 1 | unevaluatedProperties can't see inside cousins (reverse order) | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 24 | 2 | nested unevaluatedProperties, outer false, inner true, properties outside | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 25 | 2 | nested unevaluatedProperties, outer false, inner true, properties inside | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 26 | 2 | nested unevaluatedProperties, outer true, inner false, properties outside | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 27 | 2 | nested unevaluatedProperties, outer true, inner false, properties inside | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 28 | 2 | cousin unevaluatedProperties, true and false, true with properties | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 29 | 2 | cousin unevaluatedProperties, true and false, false with properties | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 30 | 2 | property is evaluated in an uncle schema to unevaluatedProperties | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 31 | 3 | in-place applicator siblings, allOf has unevaluated | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 32 | 3 | in-place applicator siblings, anyOf has unevaluated | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 33 | 7 | unevaluatedProperties + single cyclic ref | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 34 | 8 | unevaluatedProperties + ref inside allOf / oneOf | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 35 | 21 | dynamic evaluation inside nested refs | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 36 | 6 | non-object instances are valid | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 37 | 1 | unevaluatedProperties with null valued instance properties | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 38 | 2 | unevaluatedProperties not affected by propertyNames | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 39 | 2 | unevaluatedProperties can see annotations from if without then and else | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 40 | 3 | dependentSchemas with unevaluatedProperties | unsupported keywords: dependentSchemas, unevaluatedProperties |
| unevaluatedProperties.json | 41 | 1 | Evaluated properties collection needs to consider instance location | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 42 | 2 | Evaluated properties collection needs to consider instance location with patternProperties | unsupported keywords: unevaluatedProperties |
| unevaluatedProperties.json | 43 | 2 | Evaluated properties collection needs to consider instance location with additionalProperties | unsupported keywords: unevaluatedProperties |
| vocabulary.json | 0 | 3 | schema that uses custom metaschema with with no validation vocabulary | localhost custom metaschema/vocabulary semantics require remote resources |
| vocabulary.json | 1 | 2 | ignore unrecognized optional vocabulary | localhost custom metaschema/vocabulary semantics require remote resources |

## Harness registration and execution

Each original group.schema is passed unchanged through GetRawText to Add("suite.schema.json", ...); each original test.data is passed directly to Validate(..., "suite.schema.json"). Find checks that public registration exists. The assertion compares (issues.Count == 0) with the original test.valid. No schema keywords, IDs, references, or data are rewritten. Root $id registration and embedded-resource resolution are solely validator responsibilities. Group/test descriptions are asserted to guard against stale row indexes. Missing fixtures fail explicitly.

The harness uses installed xUnit 2.9.3: each InlineData inherits DataAttribute.Skip and supplies exactly one row, permitting individual mismatch skips. Remove an individual Skip after fixing its finding. No dependency added. Set PYTHONUTF8=1 when invoking overlay_test.py on Windows, so its subprocess reader handles official Unicode descriptions; the command itself is unchanged.

Ignored annotations without dedicated selected upstream test files (e.g. title/description/examples/readOnly/writeOnly/deprecated) receive incidental coverage in unchanged schemas, not a claim of independent annotation assertions. $defs is exercised by local references; its standalone defs.json requires the remote official metaschema and is excluded.

## Measured coverage and known mismatches

The unskipped first measured run was 887 cases: 824 passed, 63 failed. The final delivery retains all 63 disagreements as individual suite-mismatch skips, not passes. File categories below identify official keyword files and supplementary mixed optional suites; rates are pass / included cases, with skips in the denominator.

| Keyword / suite file | Included | Passed | Mismatch skips | Pass rate |
|---|---:|---:|---:|---:|
| additionalProperties.json | 18 | 18 | 0 | 100.00% |
| allOf.json | 30 | 30 | 0 | 100.00% |
| anyOf.json | 18 | 18 | 0 | 100.00% |
| boolean_schema.json | 18 | 18 | 0 | 100.00% |
| const.json | 54 | 54 | 0 | 100.00% |
| contains.json | 21 | 21 | 0 | 100.00% |
| default.json | 7 | 7 | 0 | 100.00% |
| dependentRequired.json | 20 | 20 | 0 | 100.00% |
| enum.json | 53 | 53 | 0 | 100.00% |
| exclusiveMaximum.json | 4 | 4 | 0 | 100.00% |
| exclusiveMinimum.json | 4 | 4 | 0 | 100.00% |
| if-then-else.json | 30 | 30 | 0 | 100.00% |
| infinite-loop-detection.json | 2 | 2 | 0 | 100.00% |
| items.json | 29 | 29 | 0 | 100.00% |
| maxItems.json | 6 | 4 | 2 | 66.67% |
| maxLength.json | 7 | 5 | 2 | 71.43% |
| maxProperties.json | 10 | 8 | 2 | 80.00% |
| maximum.json | 8 | 8 | 0 | 100.00% |
| minItems.json | 6 | 4 | 2 | 66.67% |
| minLength.json | 7 | 5 | 2 | 71.43% |
| minProperties.json | 10 | 8 | 2 | 80.00% |
| minimum.json | 11 | 11 | 0 | 100.00% |
| multipleOf.json | 11 | 10 | 1 | 90.91% |
| not.json | 38 | 38 | 0 | 100.00% |
| oneOf.json | 27 | 27 | 0 | 100.00% |
| optional/bignum.json | 9 | 9 | 0 | 100.00% |
| optional/ecmascript-regex.json | 74 | 46 | 28 | 62.16% |
| optional/float-overflow.json | 1 | 1 | 0 | 100.00% |
| optional/id.json | 3 | 2 | 1 | 66.67% |
| optional/no-schema.json | 3 | 3 | 0 | 100.00% |
| optional/non-bmp-regex.json | 12 | 9 | 3 | 75.00% |
| optional/refOfUnknownKeyword.json | 2 | 2 | 0 | 100.00% |
| pattern.json | 12 | 9 | 3 | 75.00% |
| patternProperties.json | 26 | 23 | 3 | 88.46% |
| prefixItems.json | 11 | 11 | 0 | 100.00% |
| properties.json | 28 | 28 | 0 | 100.00% |
| propertyNames.json | 22 | 22 | 0 | 100.00% |
| ref.json | 72 | 60 | 12 | 83.33% |
| required.json | 18 | 18 | 0 | 100.00% |
| type.json | 80 | 80 | 0 | 100.00% |
| uniqueItems.json | 65 | 65 | 0 | 100.00% |
| **Total** | **887** | **824** | **63** | **92.90%** |

### Every mismatch, grouped by keyword / suite

Indexes are zero-based in unchanged original files. Causes are black-box hypotheses, not implementation diagnoses. Exceptions are findings alongside validity disagreements.

#### maxItems.json

| Group:test | Group / test description | Likely cause | Observed |
|---|---|---|---|
| 1:0 | maxItems validation with a decimal / shorter is valid | integral decimal schema limit throws FormatException | System.FormatException : One of the identified items was in an invalid format. |
| 1:1 | maxItems validation with a decimal / too long is invalid | integral decimal schema limit throws FormatException | System.FormatException : One of the identified items was in an invalid format. |

#### maxLength.json

| Group:test | Group / test description | Likely cause | Observed |
|---|---|---|---|
| 1:0 | maxLength validation with a decimal / shorter is valid | integral decimal schema limit throws FormatException | System.FormatException : One of the identified items was in an invalid format. |
| 1:1 | maxLength validation with a decimal / too long is invalid | integral decimal schema limit throws FormatException | System.FormatException : One of the identified items was in an invalid format. |

#### maxProperties.json

| Group:test | Group / test description | Likely cause | Observed |
|---|---|---|---|
| 1:0 | maxProperties validation with a decimal / shorter is valid | integral decimal schema limit throws FormatException | System.FormatException : One of the identified items was in an invalid format. |
| 1:1 | maxProperties validation with a decimal / too long is invalid | integral decimal schema limit throws FormatException | System.FormatException : One of the identified items was in an invalid format. |

#### minItems.json

| Group:test | Group / test description | Likely cause | Observed |
|---|---|---|---|
| 1:0 | minItems validation with a decimal / longer is valid | integral decimal schema limit throws FormatException | System.FormatException : One of the identified items was in an invalid format. |
| 1:1 | minItems validation with a decimal / too short is invalid | integral decimal schema limit throws FormatException | System.FormatException : One of the identified items was in an invalid format. |

#### minLength.json

| Group:test | Group / test description | Likely cause | Observed |
|---|---|---|---|
| 1:0 | minLength validation with a decimal / longer is valid | integral decimal schema limit throws FormatException | System.FormatException : One of the identified items was in an invalid format. |
| 1:1 | minLength validation with a decimal / too short is invalid | integral decimal schema limit throws FormatException | System.FormatException : One of the identified items was in an invalid format. |

#### minProperties.json

| Group:test | Group / test description | Likely cause | Observed |
|---|---|---|---|
| 1:0 | minProperties validation with a decimal / longer is valid | integral decimal schema limit throws FormatException | System.FormatException : One of the identified items was in an invalid format. |
| 1:1 | minProperties validation with a decimal / too short is invalid | integral decimal schema limit throws FormatException | System.FormatException : One of the identified items was in an invalid format. |

#### multipleOf.json

| Group:test | Group / test description | Likely cause | Observed |
|---|---|---|---|
| 3:0 | float division = inf / always invalid, but naive implementations may raise an overflow error | overflowing numeric quotient is accepted | multipleOf.json [3:0] float division = inf / always invalid, but naive implementations may raise an overflow error: expected valid=False; actual valid=True; issues= |

#### optional/ecmascript-regex.json

| Group:test | Group / test description | Likely cause | Observed |
|---|---|---|---|
| 4:1 | ECMA 262 \d matches ascii digits only / NKO DIGIT ZERO does not match (unlike e.g. Python) | regex character classes differ from ECMAScript Unicode rules | optional/ecmascript-regex.json [4:1] ECMA 262 \d matches ascii digits only / NKO DIGIT ZERO does not match (unlike e.g. Python): expected valid=False; actual valid=True; issues= |
| 4:2 | ECMA 262 \d matches ascii digits only / NKO DIGIT ZERO (as \u escape) does not match | regex character classes differ from ECMAScript Unicode rules | optional/ecmascript-regex.json [4:2] ECMA 262 \d matches ascii digits only / NKO DIGIT ZERO (as \u escape) does not match: expected valid=False; actual valid=True; issues= |
| 5:1 | ECMA 262 \D matches everything but ascii digits / NKO DIGIT ZERO matches (unlike e.g. Python) | regex character classes differ from ECMAScript Unicode rules | optional/ecmascript-regex.json [5:1] ECMA 262 \D matches everything but ascii digits / NKO DIGIT ZERO matches (unlike e.g. Python): expected valid=True; actual valid=False; issues=error [$]: '߀' doesn't fit the pattern ^\D$ |
| 5:2 | ECMA 262 \D matches everything but ascii digits / NKO DIGIT ZERO (as \u escape) matches | regex character classes differ from ECMAScript Unicode rules | optional/ecmascript-regex.json [5:2] ECMA 262 \D matches everything but ascii digits / NKO DIGIT ZERO (as \u escape) matches: expected valid=True; actual valid=False; issues=error [$]: '߀' doesn't fit the pattern ^\D$ |
| 6:1 | ECMA 262 \w matches ascii letters only / latin-1 e-acute does not match (unlike e.g. Python) | regex character classes differ from ECMAScript Unicode rules | optional/ecmascript-regex.json [6:1] ECMA 262 \w matches ascii letters only / latin-1 e-acute does not match (unlike e.g. Python): expected valid=False; actual valid=True; issues= |
| 7:1 | ECMA 262 \W matches everything but ascii letters / latin-1 e-acute matches (unlike e.g. Python) | regex character classes differ from ECMAScript Unicode rules | optional/ecmascript-regex.json [7:1] ECMA 262 \W matches everything but ascii letters / latin-1 e-acute matches (unlike e.g. Python): expected valid=True; actual valid=False; issues=error [$]: 'é' doesn't fit the pattern ^\W$ |
| 8:5 | ECMA 262 \s matches whitespace / zero-width whitespace matches | regex character classes differ from ECMAScript Unicode rules | optional/ecmascript-regex.json [8:5] ECMA 262 \s matches whitespace / zero-width whitespace matches: expected valid=True; actual valid=False; issues=error [$]: '﻿' doesn't fit the pattern ^\s$ |
| 9:5 | ECMA 262 \S matches everything but whitespace / zero-width whitespace does not match | regex character classes differ from ECMAScript Unicode rules | optional/ecmascript-regex.json [9:5] ECMA 262 \S matches everything but whitespace / zero-width whitespace does not match: expected valid=False; actual valid=True; issues= |
| 10:0 | patterns always use unicode semantics with pattern / ascii character in json string | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '\p{Letter}cole' at offset 10. Unknown property 'Letter'. |
| 10:1 | patterns always use unicode semantics with pattern / literal unicode character in json string | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '\p{Letter}cole' at offset 10. Unknown property 'Letter'. |
| 10:2 | patterns always use unicode semantics with pattern / unicode character in hex format in string | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '\p{Letter}cole' at offset 10. Unknown property 'Letter'. |
| 10:3 | patterns always use unicode semantics with pattern / unicode matching is case-sensitive | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '\p{Letter}cole' at offset 10. Unknown property 'Letter'. |
| 11:1 | \w in patterns matches [A-Za-z0-9_], not unicode letters / literal unicode character in json string | regex character classes differ from ECMAScript Unicode rules | optional/ecmascript-regex.json [11:1] \w in patterns matches [A-Za-z0-9_], not unicode letters / literal unicode character in json string: expected valid=False; actual valid=True; issues= |
| 11:2 | \w in patterns matches [A-Za-z0-9_], not unicode letters / unicode character in hex format in string | regex character classes differ from ECMAScript Unicode rules | optional/ecmascript-regex.json [11:2] \w in patterns matches [A-Za-z0-9_], not unicode letters / unicode character in hex format in string: expected valid=False; actual valid=True; issues= |
| 13:2 | \d in pattern matches [0-9], not unicode digits / non-ascii digits (BENGALI DIGIT FOUR, BENGALI DIGIT TWO) | regex character classes differ from ECMAScript Unicode rules | optional/ecmascript-regex.json [13:2] \d in pattern matches [0-9], not unicode digits / non-ascii digits (BENGALI DIGIT FOUR, BENGALI DIGIT TWO): expected valid=False; actual valid=True; issues= |
| 14:0 | pattern with non-ASCII digits / ascii digits | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '^\p{digit}+$' at offset 10. Unknown property 'digit'. |
| 14:1 | pattern with non-ASCII digits / ascii non-digits | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '^\p{digit}+$' at offset 10. Unknown property 'digit'. |
| 14:2 | pattern with non-ASCII digits / non-ascii digits (BENGALI DIGIT FOUR, BENGALI DIGIT TWO) | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '^\p{digit}+$' at offset 10. Unknown property 'digit'. |
| 15:0 | patterns always use unicode semantics with patternProperties / ascii character in json string | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '\p{Letter}cole' at offset 10. Unknown property 'Letter'. |
| 15:1 | patterns always use unicode semantics with patternProperties / literal unicode character in json string | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '\p{Letter}cole' at offset 10. Unknown property 'Letter'. |
| 15:2 | patterns always use unicode semantics with patternProperties / unicode character in hex format in string | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '\p{Letter}cole' at offset 10. Unknown property 'Letter'. |
| 15:3 | patterns always use unicode semantics with patternProperties / unicode matching is case-sensitive | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '\p{Letter}cole' at offset 10. Unknown property 'Letter'. |
| 16:1 | \w in patternProperties matches [A-Za-z0-9_], not unicode letters / literal unicode character in json string | regex character classes differ from ECMAScript Unicode rules | optional/ecmascript-regex.json [16:1] \w in patternProperties matches [A-Za-z0-9_], not unicode letters / literal unicode character in json string: expected valid=False; actual valid=True; issues= |
| 16:2 | \w in patternProperties matches [A-Za-z0-9_], not unicode letters / unicode character in hex format in string | regex character classes differ from ECMAScript Unicode rules | optional/ecmascript-regex.json [16:2] \w in patternProperties matches [A-Za-z0-9_], not unicode letters / unicode character in hex format in string: expected valid=False; actual valid=True; issues= |
| 18:2 | \d in patternProperties matches [0-9], not unicode digits / non-ascii digits (BENGALI DIGIT FOUR, BENGALI DIGIT TWO) | regex character classes differ from ECMAScript Unicode rules | optional/ecmascript-regex.json [18:2] \d in patternProperties matches [0-9], not unicode digits / non-ascii digits (BENGALI DIGIT FOUR, BENGALI DIGIT TWO): expected valid=False; actual valid=True; issues= |
| 19:0 | patternProperties with non-ASCII digits / ascii digits | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '^\p{digit}+$' at offset 10. Unknown property 'digit'. |
| 19:1 | patternProperties with non-ASCII digits / ascii non-digits | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '^\p{digit}+$' at offset 10. Unknown property 'digit'. |
| 19:2 | patternProperties with non-ASCII digits / non-ascii digits (BENGALI DIGIT FOUR, BENGALI DIGIT TWO) | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '^\p{digit}+$' at offset 10. Unknown property 'digit'. |

#### optional/id.json

| Group:test | Group / test description | Likely cause | Observed |
|---|---|---|---|
| 0:1 | $id inside an enum is not a real identifier / match $ref to $id | $id inside enum data confuses local resource resolution | optional/id.json [0:1] $id inside an enum is not a real identifier / match $ref to $id: expected valid=True; actual valid=False; issues=error [$]: matches none of the 2 allowed shapes |

#### optional/non-bmp-regex.json

| Group:test | Group / test description | Likely cause | Observed |
|---|---|---|---|
| 0:0 | Proper UTF-16 surrogate pair handling: pattern / matches empty | regex processes UTF-16 code units instead of Unicode code points | optional/non-bmp-regex.json [0:0] Proper UTF-16 surrogate pair handling: pattern / matches empty: expected valid=True; actual valid=False; issues=error [$]: '' doesn't fit the pattern ^\ud83d\udc32*$ |
| 0:2 | Proper UTF-16 surrogate pair handling: pattern / matches two | regex processes UTF-16 code units instead of Unicode code points | optional/non-bmp-regex.json [0:2] Proper UTF-16 surrogate pair handling: pattern / matches two: expected valid=True; actual valid=False; issues=error [$]: '\ud83d\udc32\ud83d\udc32' doesn't fit the pattern ^\ud83d\udc32*$ |
| 1:4 | Proper UTF-16 surrogate pair handling: patternProperties / doesn't match two | regex processes UTF-16 code units instead of Unicode code points | optional/non-bmp-regex.json [1:4] Proper UTF-16 surrogate pair handling: patternProperties / doesn't match two: expected valid=False; actual valid=True; issues= |

#### pattern.json

| Group:test | Group / test description | Likely cause | Observed |
|---|---|---|---|
| 2:0 | pattern with Unicode property escape requires unicode mode / ASCII letters match | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '^\p{Letter}+$' at offset 11. Unknown property 'Letter'. |
| 2:1 | pattern with Unicode property escape requires unicode mode / Non-ASCII letters match | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '^\p{Letter}+$' at offset 11. Unknown property 'Letter'. |
| 2:2 | pattern with Unicode property escape requires unicode mode / Digits do not match | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '^\p{Letter}+$' at offset 11. Unknown property 'Letter'. |

#### patternProperties.json

| Group:test | Group / test description | Likely cause | Observed |
|---|---|---|---|
| 5:0 | patternProperties with Unicode property escape / Unicode letter property name matches | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '^\p{Letter}+$' at offset 11. Unknown property 'Letter'. |
| 5:1 | patternProperties with Unicode property escape / Unicode letter property name matches but value type is incorrect | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '^\p{Letter}+$' at offset 11. Unknown property 'Letter'. |
| 5:2 | patternProperties with Unicode property escape / Non-letter property name does not match pattern | valid suite regex property escape throws RegexParseException | System.Text.RegularExpressions.RegexParseException : Invalid pattern '^\p{Letter}+$' at offset 11. Unknown property 'Letter'. |

#### ref.json

| Group:test | Group / test description | Likely cause | Observed |
|---|---|---|---|
| 11:0 | Recursive references between schemas / valid tree | embedded resource URI is not resolved | ref.json [11:0] Recursive references between schemas / valid tree: expected valid=True; actual valid=False; issues=error [$.nodes[0]]: the schema refers to 'node', which can't be found; error [$.nodes[1]]: the schema refers to 'node', which can't be found |
| 15:2 | refs with relative uris and defs / valid on both fields | embedded resource URI is not resolved | ref.json [15:2] refs with relative uris and defs / valid on both fields: expected valid=True; actual valid=False; issues=error [$]: the schema refers to 'schema-relative-uri-defs2.json', which can't be found; error [$.foo]: the schema refers to '#/$defs/inner', which can't be found |
| 16:2 | relative refs with absolute uris and defs / valid on both fields | embedded resource URI is not resolved | ref.json [16:2] relative refs with absolute uris and defs / valid on both fields: expected valid=True; actual valid=False; issues=error [$]: the schema refers to 'schema-refs-absolute-uris-defs2.json', which can't be found; error [$.foo]: the schema refers to '#/$defs/inner', which can't be found |
| 17:0 | $id must be resolved against nearest parent, not just immediate parent / number is valid | embedded resource URI is not resolved | ref.json [17:0] $id must be resolved against nearest parent, not just immediate parent / number is valid: expected valid=True; actual valid=False; issues=error [$]: the schema refers to 'http://example.com/b/d.json', which can't be found |
| 18:0 | order of evaluation: $id and $ref / data is valid against first definition | embedded resource URI is not resolved | ref.json [18:0] order of evaluation: $id and $ref / data is valid against first definition: expected valid=True; actual valid=False; issues=error [$]: the schema refers to 'int.json', which can't be found |
| 20:0 | order of evaluation: $id and $ref on nested schema / data is valid against nested sibling | embedded resource URI is not resolved | ref.json [20:0] order of evaluation: $id and $ref on nested schema / data is valid against nested sibling: expected valid=True; actual valid=False; issues=error [$]: the schema refers to 'nested/foo.json', which can't be found |
| 28:0 | URN ref with nested pointer ref / a string is valid | embedded resource URI is not resolved | ref.json [28:0] URN ref with nested pointer ref / a string is valid: expected valid=True; actual valid=False; issues=error [$]: the schema refers to 'urn:uuid:deadbeef-4321-ffff-ffff-1234feebdaed', which can't be found |
| 29:1 | ref to if / an integer is valid | embedded resource URI is not resolved | ref.json [29:1] ref to if / an integer is valid: expected valid=True; actual valid=False; issues=error [$]: the schema refers to 'http://example.com/ref/if', which can't be found |
| 30:1 | ref to then / an integer is valid | embedded resource URI is not resolved | ref.json [30:1] ref to then / an integer is valid: expected valid=True; actual valid=False; issues=error [$]: the schema refers to 'http://example.com/ref/then', which can't be found |
| 31:1 | ref to else / an integer is valid | embedded resource URI is not resolved | ref.json [31:1] ref to else / an integer is valid: expected valid=True; actual valid=False; issues=error [$]: the schema refers to 'http://example.com/ref/else', which can't be found |
| 32:0 | ref with absolute-path-reference / a string is valid | embedded resource URI is not resolved | ref.json [32:0] ref with absolute-path-reference / a string is valid: expected valid=True; actual valid=False; issues=error [$]: the schema refers to '/absref/foobar.json', which can't be found |
| 35:0 | empty tokens in $ref json-pointer / number is valid | empty JSON-pointer tokens are not resolved | ref.json [35:0] empty tokens in $ref json-pointer / number is valid: expected valid=True; actual valid=False; issues=error [$]: the schema refers to '#/$defs//$defs/', which can't be found |

## Self-check: seeded independent reasoning

Seed: `A026-self-check-2026-10-31`, Python `random.Random(seed).sample(rows, 20)` after sorting rows by (file, zero-based group, zero-based test). The final sample was re-drawn after the scope-selection fixes and matched the same 20 identities. Each expectation below was reasoned from the schema/data and sources before comparing the observed validation. An isolated scratch copy of the same harness executed only these 20 rows with mismatch skips removed. It reproduced 18 passing validations and 2 known findings; those 2 are not counted as passes. All 20 upstream flags and row identities were independently confirmed. No self-authored harness defect was found in this sample.

### Quote bank (printed once; short fragments)

These are normative 2020-12 specification excerpts; links identify the exact section. Bank IDs are reused below instead of repeating source quotations.

| ID | Brief exact excerpt | Normative section |
|---|---|---|
| C-ref | "Its results are the results of the referenced schema." | [Core 8.2.3.1](https://json-schema.org/draft/2020-12/json-schema-core#section-8.2.3.1) |
| C-pattern | "each schema" | [Core 10.3.2.2](https://json-schema.org/draft/2020-12/json-schema-core#section-10.3.2.2) |
| C-items | "all instance array elements" | [Core 10.3.1.2](https://json-schema.org/draft/2020-12/json-schema-core#section-10.3.1.2) |
| C-contains | "at least one of its elements" | [Core 10.3.1.3](https://json-schema.org/draft/2020-12/json-schema-core#section-10.3.1.3) |
| C-number | "same mathematical value" | [Core 4.2.2](https://json-schema.org/draft/2020-12/json-schema-core#section-4.2.2) |
| V-min | "greater than, or equal to" | [Validation 6.3.2](https://json-schema.org/draft/2020-12/json-schema-validation#section-6.3.2), [6.5.2](https://json-schema.org/draft/2020-12/json-schema-validation#section-6.5.2) |
| V-enum | "equal to one of the elements" | [Validation 6.1.2](https://json-schema.org/draft/2020-12/json-schema-validation#section-6.1.2) |
| V-unique | "all of its elements are unique" | [Validation 6.4.3](https://json-schema.org/draft/2020-12/json-schema-validation#section-6.4.3) |
| V-type | "its type matches" | [Validation 6.1.1](https://json-schema.org/draft/2020-12/json-schema-validation#section-6.1.1) |
| V-required | "An object instance" | [Validation 6.5.3](https://json-schema.org/draft/2020-12/json-schema-validation#section-6.5.3); non-object applicability: [Core 7.6.1](https://json-schema.org/draft/2020-12/json-schema-core#section-7.6.1) |

Supplementary official explanations below are explanatory documentation, not normative specification quotations:

| ID | Brief exact excerpt | Explanation; normative counterpart |
|---|---|---|
| U-default | "not used to fill in missing values" | [Understanding annotations](https://json-schema.org/understanding-json-schema/reference/annotations); [Validation 9.2](https://json-schema.org/draft/2020-12/json-schema-validation#section-9.2) |
| U-const | "restrict a value to a single value" | [Understanding const](https://json-schema.org/understanding-json-schema/reference/const); [Validation 6.1.3](https://json-schema.org/draft/2020-12/json-schema-validation#section-6.1.3) |
| U-type | "it matches any of the given types" | [Understanding type](https://json-schema.org/understanding-json-schema/reference/type); Validation 6.1.1 above |

Core quote bank: 24 words total; Validation bank: 23 words total. Repeated rule IDs reuse the bank, with no repeated excerpts.

### Twenty sampled cases

File/group:test identifies exact unchanged schema and data. T/F is the independent expected validity, confirmed against upstream valid.

| # | Case | Expected and independent reasoning | Rule IDs / exact sections above | Observed rerun |
|---:|---|---|---|---|
| 1 | optional/no-schema.json 0:1 | F: one character is shorter than two. | V-min, Validation 6.3.2 | PASS |
| 2 | patternProperties.json 4:0 | T: foobar matches; null satisfies type:null. | C-pattern, Core 10.3.2.2; V-type | PASS |
| 3 | const.json 13:3 | F: 9007199254740991 differs from 9007199254740992. | C-number, Core 4.2.2; U-const, Validation 6.1.3 | PASS |
| 4 | minProperties.json 1:1 | F: zero properties is below integral 1.0. | V-min, Validation 6.5.2; mathematical integers: Core 6.3 | KNOWN MISMATCH: FormatException; not a pass |
| 5 | contains.json 4:3 | F: [1,5] has neither even items nor a multiple of three. | C-items/Core 10.3.1.2; C-contains/Core 10.3.1.3 | PASS |
| 6 | enum.json 1:4 | F: an extra property prevents equality with the listed object. | V-enum, Validation 6.1.2; Core 4.2.2 | PASS |
| 7 | default.json 2:2 | T: absent alpha is not inserted or required. | U-default, Validation 9.2; properties: Core 10.3.2.1 | PASS |
| 8 | type.json 6:5 | F: an object is not null. | V-type/Validation 6.1.1; U-type | PASS |
| 9 | type.json 7:0 | T: 1 satisfies the integer alternative. | U-type, Validation 6.1.1 | PASS |
| 10 | patternProperties.json 2:2 | T: a_x_3 matches neither uppercase X_ nor two digits. | C-pattern, Core 10.3.2.2 | PASS |
| 11 | uniqueItems.json 0:9 | F: the two objects are equal. | V-unique, Validation 6.4.3; Core 4.2.2 | PASS |
| 12 | required.json 4:1 | T: object-property requirements do not constrain numeric 12. | V-required/Validation 6.5.3; Core 7.6.1 | PASS |
| 13 | uniqueItems.json 0:22 | T: the objects assign different values to the same keys. | V-unique, Validation 6.4.3; Core 4.2.2 | PASS |
| 14 | items.json 1:1 | T: an empty array has no failing item. | C-items, Core 10.3.1.2 | PASS |
| 15 | type.json 2:0 | F: numeric 1 is not a string. | U-type, Validation 6.1.1 | PASS |
| 16 | enum.json 6:1 | F: [0] differs from [false]; boolean and number remain distinct. | V-enum/Validation 6.1.2; Core 4.2.2 | PASS |
| 17 | type.json 10:1 | T: object is one of array/object/null. | U-type, Validation 6.1.1 | PASS |
| 18 | patternProperties.json 1:4 | F: aaaa matches both patterns; 31 exceeds the second bound 20. | C-pattern/Core 10.3.2.2; Validation 6.2.2 | PASS |
| 19 | const.json 1:0 | T: both object members have identical values. | U-const/Validation 6.1.3; Core 4.2.2 | PASS |
| 20 | ref.json 35:0 | T: empty pointer tokens reach the nested number schema; 1 is numeric. | C-ref, Core 8.2.3.1; JSON Pointer syntax: [Core 5](https://json-schema.org/draft/2020-12/json-schema-core#section-5) | KNOWN MISMATCH: unresolved empty tokens; not a pass |

Scratch rerun real summary (individual mismatch skips removed only in scratch):

```text
Failed!  - Failed:     2, Passed:    18, Skipped:     0, Total:    20, Duration: 178 ms - Brutal.Authoring.Tests.dll (net10.0)
```

The delivered rows retain both required mismatch skips. The scratch rerun does not alter fixture bytes or suite expectations. The final exact full gate uses the delivered harness again.
