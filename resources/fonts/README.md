# Bundled UI font candidates

These are unmodified candidate assets only. No theme, scene, project default, or
runtime font assignment uses them yet. Godot can import these TrueType files when
needed; choose weights/variations explicitly during later integration.

## Provenance

All binaries and each family's adjacent `OFL.txt` are byte-for-byte copies from
[google/fonts at `9710da1eacb3be272583c3224dcb70f9da6eadbb`](https://github.com/google/fonts/tree/9710da1eacb3be272583c3224dcb70f9da6eadbb/ofl),
retrieved 2026-10-01. Filenames and directory names match that repository's `ofl/`
directories. Version numbers below come from the binaries' OpenType name tables.
`SHA256SUMS` covers the original binaries and license files; verify from this
directory with `sha256sum -c SHA256SUMS`.

| Family / upstream directory | Font version | Included styles | TTF bytes |
| --- | --- | --- | ---: |
| [Anton](https://github.com/google/fonts/tree/9710da1eacb3be272583c3224dcb70f9da6eadbb/ofl/anton) | 2.116 | Regular (400), 1 static file | 170,812 |
| [Barlow Condensed](https://github.com/google/fonts/tree/9710da1eacb3be272583c3224dcb70f9da6eadbb/ofl/barlowcondensed) | 1.408 | Thin–Black, upright + italic, 18 static files | 1,943,544 |
| [Archivo](https://github.com/google/fonts/tree/9710da1eacb3be272583c3224dcb70f9da6eadbb/ofl/archivo) | 2.001 | Upright + italic, 2 variable files: weight 100–900, width 62–125 | 1,399,964 |
| [Chakra Petch](https://github.com/google/fonts/tree/9710da1eacb3be272583c3224dcb70f9da6eadbb/ofl/chakrapetch) | 1.000 | ExtraLight–Bold, upright + italic, 12 static files | 958,116 |
| [Barlow](https://github.com/google/fonts/tree/9710da1eacb3be272583c3224dcb70f9da6eadbb/ofl/barlow) | 1.408 | Thin–Black, upright + italic, 18 static files | 1,947,400 |
| [IBM Plex Mono](https://github.com/google/fonts/tree/9710da1eacb3be272583c3224dcb70f9da6eadbb/ofl/ibmplexmono) | 2.3 | Thin–Bold (100–700), upright + italic, 14 static files | 1,973,388 |

Total: 65 TTF files, 8,393,224 bytes (8.00 MiB). No redundant static versions of
Archivo, webfont conversions, subsets, or source/build files are included.
Archivo's source default weight is 600, not 400; set the weight intentionally
when integrating it. Fonts are ordinary Git blobs, matching this repository's
existing asset storage (no Git LFS configuration; largest file is 741,368 bytes).

## License and distribution

Each family is supplied under the SIL Open Font License 1.1. Its exact upstream
copyright notice and full license are preserved in that family's `OFL.txt`.
The licenses allow bundling and redistribution with commercial software, subject
to their conditions; fonts must not be sold by themselves. Keep the copyright
notices and licenses with redistributed fonts, including in release packages.
When configuring a game export, explicitly ensure these non-resource `.txt`
license files are included (or ship an equivalent accessible license bundle).
This asset-only change does not configure an export preset.

IBM's supplied license declares the Reserved Font Name "Plex". These files are
unmodified; review the relevant OFL conditions before changing any font, naming a
modified version, or redistributing it. The game and documents made using the
fonts do not themselves become OFL-licensed.
