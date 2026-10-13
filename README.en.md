# YB Dissolve Avatar Builder

[**简体中文**](README.md) | [**English**](README.en.md) | [**日本語**](README.ja.md)

**Version:** 0.2.0 · **Author:** YungBurn · **License:** [MIT License](LICENSE)

A Unity Editor tool for **VRChat avatar outfit transitions with directional scan-and-dissolve effects**. Analyze two outfits, generate baked meshes and materials, and optionally set up Animator, VRChat Action Controllers, and Modular Avatar integration.

> This repository contains editable source code, shaders, documentation, and Unity `.meta` files. It does not include a complete Unity project, avatar models, or user-generated assets.

## Features

- **Two-outfit workflow:** Analyze multiple roots and renderers for Outfit A / B, with installation validation.
- **Directional dissolve:** Choose a scan direction in Avatar Root local space. The scan coordinate is baked into `UV4.x`; changing the direction requires rebaking.
- **Independent A↔B timing:** Configure separate pre-scan delays for A→B and B→A, with optional action-duration alignment.
- **Material effects:** Scan edge/glow, overlay, Face / Body material variants, and support for multiple lilToon rendering modes.
- **Controller integration:** Generate FX Controllers and, when needed, VRChat Action Controllers and Modular Avatar menus/parameters.
- **Reversible workflow:** Tools for installation, validation, removal, and restoration.
- **Trilingual UI:** Chinese (first-launch default), English, and Japanese. The selected language persists locally through Unity `EditorPrefs`.

## Requirements

| Component | Purpose | Required? |
| --- | --- | --- |
| Unity Editor | Runs the editor tool | Yes; use a Unity version compatible with your VRChat project |
| [lilToon](https://github.com/lilxyzw/lilToon) (`jp.lilxyzw.liltoon`) | Shader includes / ShaderGUI and material conversion | Yes |
| VRChat SDK Avatars | VRChat Action features | For those features |
| [Modular Avatar](https://modular-avatar.nadena.dev/) | Automatic FX, parameter, and menu setup | For MA integration |

**Note:** The included shaders reference `Packages/jp.lilxyzw.liltoon/Shader/Includes/`. Install lilToon at the expected package path. This repository **does not bundle the full lilToon distribution**.

## Installation

1. Install the appropriate dependencies in your Unity project, including lilToon.
2. Download or clone this repository. Copy the entire **`_YBTools/`** directory into **`Assets/_YBTools/`** in your Unity project.
3. Keep the directory name and all `.meta` files. **Do not** install this folder under Unity `Packages/` or a different path.
4. Wait for Unity to import and compile. Open **`YBTools > 溶解 > Dissolve Avatar Builder`** from the menu. The menu path is currently fixed even when the UI language changes.

## Quick start

1. In **Avatar**, assign Avatar Root, Outfit A / B roots, and a Profile.
2. In **Analysis**, inspect renderers, materials, Read/Write, and UV4 compatibility.
3. In **Effects**, set scan direction, range, and independent A→B / B→A delays.
4. In **Build / Install**, generate baked meshes and materials, then install after reviewing the validation results.
5. Optionally configure Face / Body **Variants**, and generate FX, Action, or MA assets in **Animator / MA**.
6. Run **Validate** and, when needed, test in Unity Play Mode and the VRChat environment.

**Important:** Changing the scan direction does not update existing mesh `UV4.x` data. Rebuild the affected baked meshes and reinstall the relevant assets.

## Repository layout

```text
.
├── README.md / README.en.md / README.ja.md
├── LICENSE                        # MIT for YungBurn's original contributions
├── THIRD_PARTY_NOTICES.md         # Upstream licenses and attribution
└── _YBTools/                      # Copy into your Unity Assets/ folder
    ├── Editor/                    # UI, builders, installers
    ├── Runtime/                   # Profile and install-record types
    ├── Shader/                    # Scan shaders and compatibility variants
    ├── Data/Schema/               # Version and shader metadata
    └── Documentation/             # Setup, usage, licensing
```

See also: [`QuickStart.md`](_YBTools/Documentation/QuickStart.md) · [`Installation.md`](_YBTools/Documentation/Installation.md) · [`MaterialSupport.md`](_YBTools/Documentation/MaterialSupport.md) · [`Troubleshooting.md`](_YBTools/Documentation/Troubleshooting.md)

## License and attribution

**Original code and documentation by YungBurn** are released under the [MIT License](LICENSE). **Copyright © 2026 YungBurn.**

You may use, copy, modify, distribute, sublicense, and commercially use those contributions. **Redistribution of copies or substantial portions must retain the copyright notice and MIT license text.** This preserves YungBurn's copyright attribution. Additional credit such as “Based on YB Dissolve Avatar Builder by YungBurn” is appreciated in derivative projects, but the MIT License does *not* require extra on-screen or promotional attribution.

Some included shaders contain or derive from lilToon components and are also subject to lilToon's original MIT notices. See [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md). Do not attribute all upstream code exclusively to YungBurn.

## Compatibility and support

Test against the Unity, VRChat SDK, lilToon, and Modular Avatar versions used by your project. Packaging and static inspection **do not constitute end-to-end Unity/VRChat runtime testing**.

For bug reports, open a GitHub Issue with reproduction steps, package/Unity versions, and relevant Console logs (with personal information removed).
