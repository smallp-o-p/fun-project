# Universal Animation Library

Quaternius' **Universal Animation Library 1 — Standard**, downloaded on 2026-10-10 from the [creator's page](https://quaternius.itch.io/universal-animation-library). The included [license](License.txt) is CC0 1.0.

This is an animation asset pack, not an editor or runtime plugin. Only `Unreal-Godot/UAL1_Standard.glb` is vendored: the in-place version, not the `_RM` root-motion version. It contains 43 clips and is imported as a native Godot `AnimationLibrary`.

The library is not connected to any character, animation graph, demo, or gameplay system. No project-specific bone mapping or retargeting is configured. Before playback on a character, its bone names, track paths, rest axes and rest silhouette must be matched; see [Godot's retargeting guide](https://docs.godotengine.org/en/stable/tutorials/assets_pipeline/retargeting_3d_skeletons.html). In particular, the library's T-pose differs from our current models' A-pose.

The GLB is stored in Git LFS. Run `git lfs pull` after cloning, then open the project in Godot 4.7.2 mono to import it. Generated `.godot` files are not tracked. No additional software dependency is required.

- Source archive SHA-256: `cc73fc4e495b82958207316596317a3f40b9fa38065bde1027937452da537724`
- Vendored GLB SHA-256: `69591853d817488edaa8fd9bf8fc1d821eaeaf789f8627b3cd23b41c4ed67997`
