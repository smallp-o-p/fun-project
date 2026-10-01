# Character model controls simplification and component report

Reviewed 1 October 2026 in the Linux cloud clone. The feature now uses less runtime state and less repeated derivation while retaining the same public model controls, authored assets, import safety checks, and scene layout. The largest reduction is in wardrobe coordination; mask selection now has one authoritative bitset instead of a name-keyed state dictionary plus a reconstructed bitset and a second region-index dictionary.

This report covers the **whole character-model-controls feature**, then the additional local simplification pass. The authoring guide remains [model-controls.md](model-controls.md).

## Scope and comparison

- Repository: `smallp-o-p/fun-project`
- Starting feature commit: `1b67154611c8788f1d987b6eba0373c4971f88f1`
- Fetched master tip: `f08508843e92d97d7945fc642bbce3c513873f15`
- Merge base: `9d690124e9d9141cd231be2102e7f43cbb9e5d55`
- Feature scope: `git diff origin/master...HEAD`, equivalent here to the feature commit versus its parent
- The feature changes 322 tracked paths. Git reports 7,435 insertions and six deletions, but this includes three-line LFS pointers; it is **not** a meaningful source-code or asset-size measure
- The feature adds 27 C#/GDScript/shader source files or source-file changes, totalling 5,477 inserted source lines before this pass, including tests
- This pass is an uncommitted local diff. No push, merge, history rewrite, desktop edit, or legacy-asset deletion was performed

The final appendix lists every feature-changed path, including generated identity/import sidecars and binary dependencies, so the component descriptions do not hide unexamined file categories.

## How the pieces fit together

1. Blender sources and PNG inputs are imported using the committed humanoid bone maps and import settings
2. The post-import callback repairs bone hierarchy while preserving global transforms, then applies the structure recipe and presentation manifest
3. Thin model wrapper scenes instance the imported result and add the CharacterModel root, native animation player/tree, mask setups, and wardrobe configuration
4. Each live root isolates its material overrides before masking can alter outline weights
5. Masks resolve authored geometry and region identities; the wardrobe derives visibility and mask changes from the saved Selections dictionary
6. Native AnimationTree parameters drive body and facial clips. The root samples the animated head after the mixer and updates face-lighting axes
7. Optional attachment visibility changes the imported container's own visibility flag. Tongue and weapon topology need no runtime attachment service

All of this is presentation-owned. It has no battle-session, combatant, command-executor, or gameplay-state dependency.

## Runtime code and why it remains

### CharacterModel root

[CharacterModel.cs](../scenes/models/CharacterModel.cs) is the single scene-facing C# entry point. It coordinates initialization and exposes the native AnimationTree and optional attachment visibility. The fixed `ModelAnimationTree` child is resolved once; missing means unconfigured, wrong type is invalid authoring. A nonempty attachment path must resolve to Node3D.

The order is intentional: `_EnterTree` isolates materials; `_Ready` initializes masks, wardrobe, then attachments. The ready initializer also requests appearance setup, because tests, failed initialization, and late MaskSetups assignment can enter through that path. The one-time guards and late-assignment retry are not redundant calls that can simply be deleted. Process priority 1 places face-axis updates after the native mixer's default-priority evaluation.

### Appearance and material isolation

[CharacterModel.Appearance.cs](../scenes/models/CharacterModel.Appearance.cs) finds face-SDF materials below MeshRoot, resolves their head bone, and derives world-space forward/right axes from the animated skeleton relative to its rest orientation. It tracks freed native objects defensively for editor/reimport activity and disables processing when no face materials need updates. The last sampled basis avoids redundant shader writes.

[ModelMaterials.cs](../scenes/models/ModelMaterials.cs) duplicates each surface override and its authored ShaderMaterial outline pass per instance, marking the copies local to the scene. Shader and texture resources remain shared. This prevents one model's lighting or remapped outline weights from changing another model. Replacing this with indiscriminate deep duplication would copy reusable assets; deleting it would leak mutable visual state between instances.

### Mesh mask configuration and binding

[ModelMaskConfiguration.cs](../scenes/models/ModelMaskConfiguration.cs) is the immutable-after-use authored resource. Its ordered mask names define bit positions and initial enabled states; triangle flags identify the regions covering each triangle. GeometryHash prevents applying a recipe to a different source mesh. DefaultBits identifies the combination represented by the optional prebuilt DefaultMesh: it is deliberately separate from the initial selections in Masks.

[ModelMeshMaskSetup.cs](../scenes/models/ModelMeshMaskSetup.cs) binds one root-relative MeshInstance3D path to one typed configuration. Three saved setups cover the two body meshes and Trigger's pantyhose. This small resource avoids embedding path/configuration pairing logic into each model or assuming every model has exactly one masked mesh.

### Mask runtime and generated geometry

[CharacterModel.Masking.cs](../scenes/models/CharacterModel.Masking.cs) resolves setups, validates surface/triangle counts and geometry identity, and mints region handles bound to one runtime. It supports up to 64 regions, including bit 63 and the valid all-enabled value `-1`. Current selection is a private bitset. Handles carry their bit index; reference membership in the minted region list rejects foreign or fabricated handles.

The render mesh changes through RenderingServer rather than replacing MeshInstance3D.Mesh. The source mesh, skin, and editable blend-shape channels therefore remain available. The runtime keeps the active mesh strongly referenced. Its two-entry, per-configuration FIFO variant cache shares immutable generated meshes between instances. A nullable last-rendered bitset remains necessary: `-1` is a legitimate selection and cannot be an uninitialized sentinel; stored selection may also change before rendering is permitted.

[ModelMaskGeometry.cs](../scenes/models/ModelMaskGeometry.cs) contains stateless geometry work: source hashing, triangle filtering, vertex-channel compaction, source-vertex-index metadata, and outline weight textures. It preserves blend-shape arrays and skin-weight strides, records the source vertex for each compacted vertex, and emits a degenerate triangle for an all-hidden surface so surface indexing remains stable. Unsupported channels and malformed strides still reject explicitly.

The compacted mesh needs remapped per-vertex outline weights. The texture is 256 texels wide with enough rows for the values; zero padding is intentional. Removing either source-index metadata or remapping would assign weights to the wrong vertices after masking.

### Wardrobe coordination

[CharacterModel.Wardrobe.cs](../scenes/models/CharacterModel.Wardrobe.cs) parses authored variants, components, garment paths, and mask rules. Selections is the sole serialized clothing state. The root exposes lazy inventories and lookup, outfit selection, the clothing master switch, and piece toggles; the editor's clothing switch delegates to the same selection path.

Parsing uses locals and publishes only a valid payload, so corrected invalid authoring can retry. Required garment paths and mask rule name/index pairs bind before normal operation. Typed writes coerce/clamp where already specified; replacing the raw dictionary keeps the existing separate semantics and rolls it back if derivation rejects.

A coordinated write derives proposed garment visibility and prepares mask commits before storing the new selection. Every rule on an affected mask contributes, not just the rule belonging to the changed piece. Contributions OR together; uncontrolled regions and unaffected mask owners survive. Commit order remains the order in which affected rules first introduce owners. Removing this preflight or treating a rule as an independent last-writer-wins update would break overlapping garments and rejected-write guarantees.

[ModelClothingPiece.cs](../scenes/models/ModelClothingPiece.cs) and [ModelOutfit.cs](../scenes/models/ModelOutfit.cs) are small owner-bound discovery handles. They expose IDs/display names, and pieces distinguish saved enablement from effective visibility. They avoid applying one instance's discovered handle to another instance, while Option-returning lookup represents ordinary absence. A generic capability/component framework would add complexity rather than remove it.

## Import pipeline and why it remains

### Shared post-import callback

[humanoid_post_import.gd](../scenes/models/import/humanoid_post_import.gd) validates the normalized humanoid map and repair links, rejects skeletal clips that would be invalidated by hierarchy changes, captures global rest/pose transforms, reparents the configured bones, and recomputes local transforms. Capturing transforms before reparenting is essential to retaining the original appearance and skinning. It then calls the structure and presentation companions, rejecting the import on a reported error.

### Structural recipe

[humanoid_structure.gd](../scenes/models/import/humanoid_structure.gd) loads each model's import_bindings.res and validates its tongue and optional arm-partition metadata. It moves the tongue rig below a Head-bound BoneAttachment3D with an authored placement transform. Zhu Yuan additionally receives four copied weapon meshes and two arm-module subsets below an initially hidden Weapons container.

Arm extraction copies selected vertex attributes, skin references, surface names/materials, LOD indices, and encoded normal/tangent bytes. The guarded ArrayMesh `_surfaces` reconstruction is engine-sensitive but remains necessary for exact encoded-byte preservation, including the headless renderer where ordinary GPU updates are ineffective. Bounds, layout, role, skin, and LOD validation were retained. Removing them would silently accept malformed asset recipes or change geometry.

### Presentation manifest

[humanoid_presentation.gd](../scenes/models/import/humanoid_presentation.gd) loads presentation.res without relying on a stale nested resource cache, translates recorded legacy paths into the import-owned hierarchy, restores surface overrides, visibility, transforms, and blend-shape defaults, and hides required source/weapon groups. These manifests preserve authored choices not reproduced by Blender-to-glTF import alone. Their legacy-path map is a compatibility boundary for the committed data, not an unused migration utility.

## Authored scenes and resources

### Source models and wrappers

- `scenes/models/ZhuYuan/ZhuYuan.blend` and `scenes/models/Trigger/Trigger4.2.blend` are the authoritative models. Despite Trigger's filename, both stored files have Blender 5.1 headers; a compatible Blender is needed for fresh import
- Each model folder holds eight PNG inputs beside its Blender file, wrapper, and import sidecars. The former guide's references to absent textures/ directories were corrected
- The two `.blend.import` files retain source UID, humanoid mapping and post-import settings. They are necessary reproducibility inputs, not disposable imported-cache output
- `ZhuYuan.scn` and `Trigger.scn` are noneditable wrappers around the imported model. They save animation libraries/parameters and the root's mask/wardrobe/attachment configuration. Binary serialization keeps the authored graph and nested resource references together

### Native animation resources

- `resources/models/shared/humanoid_body.res`: shared body neutral, head_turn, and motion_demo clips
- `resources/models/<model>/humanoid_face.res`: per-model RESET, authored neutral, and absolute expression endpoints; Zhu Yuan has 40 presets and Trigger 24
- `resources/models/<model>/humanoid_face_tree.res`: native blend trees, 168 and 104 nodes respectively
- `resources/models/<model>/bone_map.tres`: the explicit source-to-humanoid bone mapping consumed by import

The animation graph mixes body motion and head turn, restores the authored facial neutral baseline, then sums independently weighted expression deltas. Each preset uses clip, neutral-reference, Sub2, and Add2 nodes. For neutral N and endpoint E, its contribution is weight times (E minus N). Overlapping contributions intentionally add without normalization or clamping. Sub2 amounts and the FaceBase layer must remain at one; preset and body control weights load at zero. Native parameters are inspectable/keyframeable, so a parallel custom expression evaluator or preset-state API is unnecessary.

### Import manifests and masks

For each model, import_bindings.res describes structural placements and presentation/presentation.res describes appearance restoration. Body mask configurations retain original geometry identity and prebuilt defaults. Trigger has a separate pantyhose mask and captured face material, outline, and vertex-weight resources. The three `*_mask_setup.tres` files connect these configurations to the runtime root's mesh paths.

### Captured presentation dependencies

The presentation_source folders contain reusable captured dependencies: Trigger has 17 images, 46 materials, and three shader resources; Zhu Yuan has 22 images and 116 materials. These preserve authored visual settings and texture data. Binary dependency names alone do not establish that an asset is redundant. No captured material, image, shader, wrapper, or legacy asset was removed in this pass.

### Shared shader source

[character.gdshader](../scenes/models/shared/character.gdshader) implements the authored toon surface treatment: color conversion, ramp shading, packed masks, metallic/matcap treatment, highlights/rim lighting, and face-SDF lighting using the per-instance head axes. [outline.gdshader](../scenes/models/shared/outline.gdshader) implements inverted-hull outlines with authored width/color/distance behavior and per-vertex weights. The declared shader controls are used; deleting them without a visual/material migration would change appearance.

## Editor integration and repository support

- [manifest_inspector/plugin.gd](../addons/manifest_inspector/plugin.gd) owns registration/unregistration of the inspector extension
- [manifest_inspector_plugin.gd](../addons/manifest_inspector/manifest_inspector_plugin.gd) presents read-only bounded metadata summaries for model resources and recursively prints full manifest contents. It makes opaque binary metadata inspectable without becoming a runtime dependency
- [plugin.cfg](../addons/manifest_inspector/plugin.cfg) declares the plugin; the feature adds it to project.godot's enabled plugins
- `.gitignore` exceptions include this plugin despite the general addons exclusion
- `resources/models/.gitattributes` tracks binary `.res` files with LFS; `scenes/models/.gitattributes` extends the existing `.scn` rule to `.blend` and `.png`
- `.uid` sidecars preserve Godot script/shader identity across paths. The orphan `ModelAttachmentPart.cs.uid` has no corresponding script and is removed in this pass; its original feature addition remains visible in the appendix. PNG `.import` sidecars preserve their import configuration and identity
- [architecture-reference.md](architecture-reference.md) records the presentation ownership/lifecycle boundary. [model-controls.md](model-controls.md) provides the detailed API and authoring workflow

The missing `addons/godot_ai` files are a pre-existing clean-clone setup issue: project.godot already referenced that ignored plugin before this feature. It emits autoload/editor warnings during these checks. This review did not invent a stub or silently change the project's plugin configuration.

## Tests and why each group remains

- [CharacterModelAssetTest.cs](../test/CharacterModelAssetTest.cs): real scene topology, fixed-child contract, authored defaults/outlines, wardrobe/masks, attachments, and cross-instance isolation
- [CharacterModelAnimationAssetTest.cs](../test/CharacterModelAnimationAssetTest.cs): actual bone/blend-shape output, automatic-frame lighting order, overlapping and multi-channel deltas, saved parameters, and playback preservation during clothing changes
- [CharacterModelAppearanceAttachmentTest.cs](../test/CharacterModelAppearanceAttachmentTest.cs): synthetic material isolation, lighting axes, initialization retry, outline/mask interaction, and invalid attachment paths
- [ModelClothingTest.cs](../test/ModelClothingTest.cs): discovery/lookup, outfit and clothing changes, persistent piece choices, owner rejection, overlapping rules, componentless rules, and malformed/missing garment authoring
- [ModelMaskWardrobeTest.cs](../test/ModelMaskWardrobeTest.cs): lifecycle/retry, dictionary replacement, bit combinations, channel/skin compaction, outline remapping, all-64-bit behavior, invalid geometry/configuration, cache reuse/eviction, and per-instance state
- [ModelAuthoringApiExampleTest.cs](../test/ModelAuthoringApiExampleTest.cs): compiles and executes the guide's discovery-based API example against synthetic and real models
- [helpers/ModelFixture.cs](../test/helpers/ModelFixture.cs): owns and frees synthetic/real scene instances and provides consistent start behavior
- [helpers/ModelAnimationGraph.cs](../test/helpers/ModelAnimationGraph.cs): builds the native synthetic graph, avoiding hand-coded expression evaluation in tests
- Feature additions to [helpers/TestData.cs](../test/helpers/TestData.cs): authored mask meshes/configurations, wardrobe recipes, animation clips, and selection helpers
- Feature additions to [helpers/Assert.cs](../test/helpers/Assert.cs): readable region-state assertions; existing unrelated assertion helpers are unchanged
- This pass adds foreign/fabricated-region rejection and unaffected-owner preservation tests, plus [model_import_regression.gd](../test/model_import_regression.gd), a direct Godot importer regression harness

The new GDScript harness checks both models' required hidden paths, recipe metadata validation, 16/32-bit LOD partitioning and invalid corners, copied vertex channels, and exact encoded normal/tangent bytes after upload. Assertions protect the simplifications rather than merely checking line counts or private dead fields.

## Simplifications made in this pass

1. **One mask selection representation.** Replaced the name-to-bool dictionary and repeated reconstruction with `_enabledBits`; moved the immutable bit index into each minted Region and replaced the second region-index dictionary with a direct index/reference membership check. Initial defaults, all 64 bits, foreign/fabricated rejection, and delayed rendering remain covered
2. **Direct affected-mask derivation.** Replaced the manual grouping dictionary, controlled/active list-building passes, and redundant commit dictionary with ordered distinct affected owners and a list of prepared commits. All rules for each selected owner still participate, preserving overlap and uncontrolled state
3. **Resolve garments once per proposed write.** Derivation retains the already-resolved Node alongside its desired visibility; commit no longer looks the path up a second time. The existing generic-node/property behavior is preserved
4. **One outfit-membership predicate.** Rule activation and piece effective visibility share the same garment-membership check
5. **One optional face-axis cache value.** Replaced a separate Initialized flag plus Basis with nullable last Basis, preserving the initial-write distinction without two coupled fields
6. **No intermediate outline-padding array.** The output byte buffer is already zeroed; source floats are copied directly into it. Texture dimensions and padded bytes stay unchanged
7. **One hidden-path list per import.** Removed extras-plus-optional-weapons branching and the duplicated hidden-count calculation
8. **Load and validate the recipe together.** Removed the single-use `ctx["recipe"]` transfer and separate pipeline stage while retaining the same loading/metadata diagnostics
9. **Direct LOD rejection.** Replaced the temporary bad flag and break-then-return flow with the same immediate diagnostic, preserving bounds-first short-circuit checks
10. **Removed unused normal/tangent stride output.** Destination stride remains independently computed and validated
11. **Removed one orphan script UID.** `ModelAttachmentPart.cs.uid` had no matching script; it was obsolete feature metadata, not a model asset

### Size accounting

The six edited production C#/GDScript files lose **80 net lines**: 50 inserted and 130 removed. Across all feature-added production source, the count falls from 2,500 to 2,420 lines. Counts include comments/blank lines and exclude tests, documentation, binaries, and identity sidecars. The orphan UID removes one additional metadata line.

Regression coverage adds 42 C# lines and a 172-line GDScript harness. Therefore this pass is a production-code reduction, **not a claim that the entire diff has fewer lines**; the test/report additions deliberately make the total diff larger. No existing test was deleted or weakened.

Further shortening would mostly compress syntax, remove diagnostics/tests, or collapse useful authoring boundaries. The retained validation and lifetime distinctions account for most remaining code. This is a measured reduction in state and control flow, not a claim that physical minimum line count proves correctness.

## Verification

### Final results

| Check | Result |
| --- | --- |
| Normal GdUnit suite on the final cleaned working tree, using Godot's OpenGL compatibility renderer | **1,080 passed, 0 failed, 0 skipped**, 42 seconds, exit 0 |
| `dotnet format` followed by `--verify-no-changes`, scoped to changed C# files | Passed, exit 0 |
| Debug build | Passed, zero errors; 10 existing nullable warnings outside model code |
| ExportRelease build | Passed, zero errors; two existing nullable warnings in geoscape code |
| Direct importer regression | **120 checks, zero failures**, exit 0, before and after importer simplification |
| Fresh source imports with original and final importer code | Both models successfully rebuilt from Blender sources |
| Additional real-renderer direct model checks | **68 model cases passed** |
| Diff whitespace and report-link checks | Passed; no missing local report links |
| Static final-diff and component-coverage review | No unresolved behavior-equivalence finding; appendix covers all 322 original feature paths |

The final normal-suite result is the completion evidence. It was run after temporary direct-runner source/scene files were removed from the repository. No existing test was skipped to obtain it.

### Earlier failures and remaining warnings

An earlier normal full run passed 1,079 of 1,080 cases, failing the unchanged `DialogueViewTest.PhysicsProcessDrivesReveal`. An isolated repeat also failed; the final full repeat passed without a code change to dialogue. Its line 87 assumes an eight-character reveal is still incomplete after ten render frames, so timing differs on the software renderer. Treat this as a known timing-sensitive test, not a permanently repaired issue.

Earlier direct headless checks reported three failures: `CharacterModelAppearanceAttachmentTest.IsolationCarriesFaceAxesAndLeavesAuthoredMaterialsUntouched`, `DialogueViewTest.ClickOverPanelAdvances`, and `DialogueViewTest.PanelAndPortraitStayOnscreen`. The appearance assertion reads shader-default uniforms that the dummy renderer does not provide; it passed under the real OpenGL renderer and the normal suite. A direct OpenGL run instead reported `DialogueViewTest.PhysicsProcessDrivesReveal` and `DialogueViewTest.ClickOverPanelAdvances`; both passed in the final normal run. Direct invocation was supplemental and is not represented as equivalent to the GdUnit runner's lifecycle or diagnostics.

The normal suite reports one orphan-node warning. Direct runs also emit native shutdown/resource-leak diagnostics and the pre-existing missing godot_ai autoload message. Importer negative tests intentionally provoke missing-metadata diagnostics while verifying returned errors. These logs are not described as warning-free.

The Debug nullable warnings are in `GeoscapeMapControl.cs`, `CaptivityView.cs`, and `UnitActionCacheTest.cs`; ExportRelease retains the two geoscape warnings. None was introduced in model-control source.

### Source import evidence

The clean clone initially had no imported model cache. Both `.blend` files passed compressed-data integrity checks and contain Blender 5.1 headers. The preinstalled Blender 4.3.2 could not read them. Using Blender 5.1.2 with embedded Python auto-execution disabled resolved that setup issue. Godot 4.7.2 .NET then rebuilt both imports with the original scripts and again with the simplified scripts:

- Trigger: 556 bones, 17 repaired links, 25 presentation surfaces, 20 restored mesh defaults, two hidden groups
- Zhu Yuan: 595 bones, 17 repaired links, 61 presentation surfaces, 34 restored mesh defaults, two hidden groups

Fresh reimport regenerates binary serialization, so this is not a claim that generated `.scn` cache files are byte-identical. The real-scene/animation tests and importer extraction/encoded-byte assertions check the relevant behavior and data contracts. All tracked Blender files, PNGs, wrappers, binary resources, and their import settings remain unchanged.

### Commands and environment

Toolchain: Godot `4.7.2.stable.mono.official.ed1daf0bf`, .NET SDK `10.0.401`, Blender `5.1.2`, Linux, OpenGL compatibility on Mesa llvmpipe. No Godot editor scene edit was made. Headless import and runtime tests were used because no connected godot-ai editor session was available.

The project's committed `.runsettings` contains a Windows Godot path. Verification used an external copy with the cloud Godot launcher as GODOT_BIN and GdUnit parameters `--rendering-method gl_compatibility --audio-driver Dummy`; the repository's `.runsettings` was not changed. Shell-only attempts could not create the adapter/formatter named pipes, so the same authorized commands were launched in the cloud desktop session, where the normal tools completed.

Representative final commands, run from the repository with the toolchain on PATH:

```sh
dotnet format fun-project.csproj --no-restore --verify-no-changes \
  --include scenes/models/CharacterModel.Appearance.cs \
  scenes/models/CharacterModel.Wardrobe.cs scenes/models/CharacterModel.Masking.cs \
  scenes/models/ModelMaskGeometry.cs test/ModelMaskWardrobeTest.cs
dotnet build fun-project.csproj --no-restore
dotnet build fun-project.csproj --no-restore -c ExportRelease
dotnet test fun-project.csproj --no-restore --settings /workspace/shared/model-gui-test.runsettings
godot --headless --path . --script res://test/model_import_regression.gd
git diff --check
```

ExportRelease here means compilation only. Export templates, a packaged executable, screenshot parity, an interactive clothing-authoring session, and live assembly hot reload were not verified.

## Limits and retained risks

- The per-configuration generated-variant limit is two, but the static configuration registry has no global lifetime bound. A cache-ownership redesign needs live reload/lifetime evidence
- Configuration and topology are fixed after initialization. In-place resource edits after use are outside the documented contract
- Editor hot reload was covered through existing pre-Ready/late-assignment simulations, not an actual assembly reload session
- The encoded `_surfaces` path remains Godot-version-sensitive; rerun import/byte checks on engine upgrades
- Arm recipes validate role counts and layout but do not independently fingerprint triangle ordering. Same-count topology edits still require a regenerated authored recipe
- Mask setup validation and initial rendering are not a global rollback transaction. This simplification preserves the existing lifecycle rather than adding a new transaction layer
- Raw Selections dictionary replacement is not silently normalized to the typed setters' coercion rules
- Existing disclosed source-migration differences remain: Trigger's pantyhose differs from the historical baked mesh by up to approximately 0.0919 per vertex; the shared head-turn endpoint is 12 degrees instead of Zhu Yuan's earlier 20-degree endpoint. This pass did not alter either
- Fresh Linux source import and real-scene checks do not establish packaged-export parity or visual equivalence on every target. No packaged game export or Windows rerun was performed

## Complete feature path inventory

Inventory entries below are the merge-base-to-feature changes, not merely files touched in this pass. Paths sharing a component have the same retention reason described above; the appendix makes every binary and sidecar explicit. `A` means added and `M` means modified. Local report/regression additions are described above and are not misrepresented as already committed feature files.

### Documentation and repository configuration

5 paths.

- `M` `.gitignore`
- `M` `docs/architecture-reference.md`
- `A` `docs/model-controls.md`
- `M` `project.godot`
- `M` `scenes/models/.gitattributes`

### Manifest Inspector editor plugin

3 paths.

- `A` `addons/manifest_inspector/manifest_inspector_plugin.gd`
- `A` `addons/manifest_inspector/plugin.cfg`
- `A` `addons/manifest_inspector/plugin.gd`

### Godot script and shader identity sidecars

26 paths.

- `A` `addons/manifest_inspector/manifest_inspector_plugin.gd.uid`
- `A` `addons/manifest_inspector/plugin.gd.uid`
- `A` `scenes/models/CharacterModel.Appearance.cs.uid`
- `A` `scenes/models/CharacterModel.Masking.cs.uid`
- `A` `scenes/models/CharacterModel.Wardrobe.cs.uid`
- `A` `scenes/models/CharacterModel.cs.uid`
- `A` `scenes/models/ModelAttachmentPart.cs.uid`
- `A` `scenes/models/ModelClothingPiece.cs.uid`
- `A` `scenes/models/ModelMaskConfiguration.cs.uid`
- `A` `scenes/models/ModelMaskGeometry.cs.uid`
- `A` `scenes/models/ModelMaterials.cs.uid`
- `A` `scenes/models/ModelMeshMaskSetup.cs.uid`
- `A` `scenes/models/ModelOutfit.cs.uid`
- `A` `scenes/models/import/humanoid_post_import.gd.uid`
- `A` `scenes/models/import/humanoid_presentation.gd.uid`
- `A` `scenes/models/import/humanoid_structure.gd.uid`
- `A` `scenes/models/shared/character.gdshader.uid`
- `A` `scenes/models/shared/outline.gdshader.uid`
- `A` `test/CharacterModelAnimationAssetTest.cs.uid`
- `A` `test/CharacterModelAppearanceAttachmentTest.cs.uid`
- `A` `test/CharacterModelAssetTest.cs.uid`
- `A` `test/ModelAuthoringApiExampleTest.cs.uid`
- `A` `test/ModelClothingTest.cs.uid`
- `A` `test/ModelMaskWardrobeTest.cs.uid`
- `A` `test/helpers/ModelAnimationGraph.cs.uid`
- `A` `test/helpers/ModelFixture.cs.uid`

### Authored model configuration and animation resources

21 paths.

- `A` `resources/models/.gitattributes`
- `A` `resources/models/shared/humanoid_body.res`
- `A` `resources/models/trigger/bone_map.tres`
- `A` `resources/models/trigger/humanoid_face.res`
- `A` `resources/models/trigger/humanoid_face_tree.res`
- `A` `resources/models/trigger/import_bindings.res`
- `A` `resources/models/trigger/presentation/body_mask.res`
- `A` `resources/models/trigger/presentation/body_mask_setup.tres`
- `A` `resources/models/trigger/presentation/face_material.res`
- `A` `resources/models/trigger/presentation/face_outline.res`
- `A` `resources/models/trigger/presentation/face_weights.res`
- `A` `resources/models/trigger/presentation/pantyhose_mask.res`
- `A` `resources/models/trigger/presentation/pantyhose_mask_setup.tres`
- `A` `resources/models/trigger/presentation/presentation.res`
- `A` `resources/models/zhu_yuan/bone_map.tres`
- `A` `resources/models/zhu_yuan/humanoid_face.res`
- `A` `resources/models/zhu_yuan/humanoid_face_tree.res`
- `A` `resources/models/zhu_yuan/import_bindings.res`
- `A` `resources/models/zhu_yuan/presentation/body_mask.res`
- `A` `resources/models/zhu_yuan/presentation/body_mask_setup.tres`
- `A` `resources/models/zhu_yuan/presentation/presentation.res`

### trigger captured images

17 paths.

- `A` `resources/models/trigger/presentation_source/images/155b031c67f8698bbf9634faf45ea2adfb1967ec8719afd0b55e1bed680e05e0.res`
- `A` `resources/models/trigger/presentation_source/images/18802c188c071758b905888b78d9c929e56e77bb872dea1a631559b1cb008250.res`
- `A` `resources/models/trigger/presentation_source/images/21a7aa41e442a8cdc9e85918ec640b401c5cb4b06be58a9d06b635bedff0d3fe.res`
- `A` `resources/models/trigger/presentation_source/images/294dee95ff8471fea05548e88b508473d2b397597b1bb11d6f0a0c0e36558ec1.res`
- `A` `resources/models/trigger/presentation_source/images/2ec9837b87dcda6007262b91780d6c7f4b030a8e5c247c6895be69faae25cee2.res`
- `A` `resources/models/trigger/presentation_source/images/669c921082c514acf0b07258bc678c14fcf247769fb30f99f426111a73e4d77a.res`
- `A` `resources/models/trigger/presentation_source/images/8675d56e790de00142548d56eefe80c0736ac9237d17a437423f9fb887f9a674.res`
- `A` `resources/models/trigger/presentation_source/images/87b0393e68eb0f9c34c27342cc7a19c5e85601e714ea7aec20499873d6d9fca6.res`
- `A` `resources/models/trigger/presentation_source/images/8d02cc50c920358a93c4915757a8cc1a05f513fd628b800bf258e1daff08b3de.res`
- `A` `resources/models/trigger/presentation_source/images/93f955ced03bd62a00b571f519b2a397c44f160ccc4d4568c4c5fb929ff73ae4.res`
- `A` `resources/models/trigger/presentation_source/images/9f355ff21fb7747bb4846dc736b349696b5aa3f29e6504102034dc25b30a0bcd.res`
- `A` `resources/models/trigger/presentation_source/images/b1ccd120a58922bcfe91338ef7b809047b951981006f32b979fac23fbd12990f.res`
- `A` `resources/models/trigger/presentation_source/images/b610824a1e2c6755a83c26b17045f85c54153fc2b1013a6901f752ad69866ee6.res`
- `A` `resources/models/trigger/presentation_source/images/bb5c56a15bf377696aea8c743b04c28c46a3860f110e9b3c0bf3cbf30b8ef7b9.res`
- `A` `resources/models/trigger/presentation_source/images/bcb583054b59a7d84cd81d75d10c09029979a3e485ad28025fd2d30fc604bb1b.res`
- `A` `resources/models/trigger/presentation_source/images/c9ac1cecf7f4e5ce60d6dcfa0dadf30b573664c6c0efc9adc4b223fc4de4ef70.res`
- `A` `resources/models/trigger/presentation_source/images/d53602fb0d93e10517169cce317e4ea08bba451230836a60b924b814c0ef6077.res`

### trigger captured materials

46 paths.

- `A` `resources/models/trigger/presentation_source/materials/material_0.res`
- `A` `resources/models/trigger/presentation_source/materials/material_1.res`
- `A` `resources/models/trigger/presentation_source/materials/material_10.res`
- `A` `resources/models/trigger/presentation_source/materials/material_11.res`
- `A` `resources/models/trigger/presentation_source/materials/material_12.res`
- `A` `resources/models/trigger/presentation_source/materials/material_13.res`
- `A` `resources/models/trigger/presentation_source/materials/material_14.res`
- `A` `resources/models/trigger/presentation_source/materials/material_15.res`
- `A` `resources/models/trigger/presentation_source/materials/material_16.res`
- `A` `resources/models/trigger/presentation_source/materials/material_19.res`
- `A` `resources/models/trigger/presentation_source/materials/material_2.res`
- `A` `resources/models/trigger/presentation_source/materials/material_20.res`
- `A` `resources/models/trigger/presentation_source/materials/material_21.res`
- `A` `resources/models/trigger/presentation_source/materials/material_22.res`
- `A` `resources/models/trigger/presentation_source/materials/material_23.res`
- `A` `resources/models/trigger/presentation_source/materials/material_24.res`
- `A` `resources/models/trigger/presentation_source/materials/material_25.res`
- `A` `resources/models/trigger/presentation_source/materials/material_26.res`
- `A` `resources/models/trigger/presentation_source/materials/material_27.res`
- `A` `resources/models/trigger/presentation_source/materials/material_28.res`
- `A` `resources/models/trigger/presentation_source/materials/material_29.res`
- `A` `resources/models/trigger/presentation_source/materials/material_3.res`
- `A` `resources/models/trigger/presentation_source/materials/material_30.res`
- `A` `resources/models/trigger/presentation_source/materials/material_31.res`
- `A` `resources/models/trigger/presentation_source/materials/material_32.res`
- `A` `resources/models/trigger/presentation_source/materials/material_33.res`
- `A` `resources/models/trigger/presentation_source/materials/material_34.res`
- `A` `resources/models/trigger/presentation_source/materials/material_35.res`
- `A` `resources/models/trigger/presentation_source/materials/material_36.res`
- `A` `resources/models/trigger/presentation_source/materials/material_37.res`
- `A` `resources/models/trigger/presentation_source/materials/material_38.res`
- `A` `resources/models/trigger/presentation_source/materials/material_39.res`
- `A` `resources/models/trigger/presentation_source/materials/material_4.res`
- `A` `resources/models/trigger/presentation_source/materials/material_40.res`
- `A` `resources/models/trigger/presentation_source/materials/material_41.res`
- `A` `resources/models/trigger/presentation_source/materials/material_42.res`
- `A` `resources/models/trigger/presentation_source/materials/material_43.res`
- `A` `resources/models/trigger/presentation_source/materials/material_44.res`
- `A` `resources/models/trigger/presentation_source/materials/material_45.res`
- `A` `resources/models/trigger/presentation_source/materials/material_46.res`
- `A` `resources/models/trigger/presentation_source/materials/material_47.res`
- `A` `resources/models/trigger/presentation_source/materials/material_5.res`
- `A` `resources/models/trigger/presentation_source/materials/material_6.res`
- `A` `resources/models/trigger/presentation_source/materials/material_7.res`
- `A` `resources/models/trigger/presentation_source/materials/material_8.res`
- `A` `resources/models/trigger/presentation_source/materials/material_9.res`

### trigger captured shaders

3 paths.

- `A` `resources/models/trigger/presentation_source/shaders/49e6353034c399e825fdf6a3eb09ea406ebfceab4a25097aee98088e03350002.res`
- `A` `resources/models/trigger/presentation_source/shaders/9bb39bf8b9212f5597f2a7de29a050d7e05db30c921f60cc170bd0a66171fc19.res`
- `A` `resources/models/trigger/presentation_source/shaders/cbd10a3c6702a11416c0b8eff065745b7a0c9d2dac20d679a5ea445fc18d6a29.res`

### zhu_yuan captured images

22 paths.

- `A` `resources/models/zhu_yuan/presentation_source/images/01000d6f5536de9199b0b4e304113087689871cf6e13343754c953d2e64cc697.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/066194a01bc33693a11e8a0df60c7df21f21d8ce9322c1eaab638d7cd4cb004b.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/11649dcd06d027d4e07ac69097690f41139e558d7209092e42d3d332c05f822b.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/35203a62372d020f53f882f568f9a5a57c6834ea4e1d9dff3fbf075b27993171.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/442e96415fba1e012918d2b6fcc998a7a8d1546cb6218dadf08400a6b71756c4.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/45843967c0a62459aa4d8d74700d798f0a7ba085024f03d6c62a803a50585863.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/46aeebb23d95bb588a78fb2afe19ad00f0138c40b642fb4b5e75861f2ac6c7ff.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/49a580edf0684673fca8158dbe132c2ce618e67fb38f956d073e8b2ae0b6bd05.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/65484a0bf3db49597d8372aa74c6af05e5ec040d58ae7866c4a05476dcdce369.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/6fe9e478df1c0a8b4f627dc1c9f1e678b1dc98d6e83c12ef3847e6ab19ba8f6b.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/718ee06660e0ca5b6e5b075c6c6290ff7b947eff3bc7d6fdb136500b1e8fbe44.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/7c9140d9aca00ab38072229ab1064518da44406088797a6d01c5c435d42b4a85.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/83a899df15c489d5e906b57f5f73fcf5a988ab7dbe8f5d3f5024ee46f9cf5dbc.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/8f25efe6f5141c97793cb554445c98d034f27dc9021feee0ba2cf9a0c34db5a9.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/9bf67f79de6dc7a385d48dd542d5e4d5671dff3d99ce04bcd1ee4998bd8a3d12.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/a357e74d53f97e02aea35d1f8413627ebf87f2cb558c21c84fb92c65d5a7d999.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/aa3cf29bf38a87822e829c08ade55ad83b20c58331a6803e498b2e78382c853e.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/aa5fa416daf8d42531cc04b5e23891d8dba7f2d91cb0a6ea8a64d96bebc004e5.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/bbe7cc981ad7eaaa401852dff9238dc5d74ac8c5f80dde1a7892b4997289d45e.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/cd90c8925f06605b151092040e151b5e9ce02fec4d929bfbfca0374b088fd58a.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/d2c4b1d8e1b5ed9db30f9fdd3765f876c2aed72a55b8c237a7f911073a566fcf.res`
- `A` `resources/models/zhu_yuan/presentation_source/images/fd9298149a8506254da7cf7256bd49429f5fecd7430803298621198ee88babb5.res`

### zhu_yuan captured materials

116 paths.

- `A` `resources/models/zhu_yuan/presentation_source/materials/material_0.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_1.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_10.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_100.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_101.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_102.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_103.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_104.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_105.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_106.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_107.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_108.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_109.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_11.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_110.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_111.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_112.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_113.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_114.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_115.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_12.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_13.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_14.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_15.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_16.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_17.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_18.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_19.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_2.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_20.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_21.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_22.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_23.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_24.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_25.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_26.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_27.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_28.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_29.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_3.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_30.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_31.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_32.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_33.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_34.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_35.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_36.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_37.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_38.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_39.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_4.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_40.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_41.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_42.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_43.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_44.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_45.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_46.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_47.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_48.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_49.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_5.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_50.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_51.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_52.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_53.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_54.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_55.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_56.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_57.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_58.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_59.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_6.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_60.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_61.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_62.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_63.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_64.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_65.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_66.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_67.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_68.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_69.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_7.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_70.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_71.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_72.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_73.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_74.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_75.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_76.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_77.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_78.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_79.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_8.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_80.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_81.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_82.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_83.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_84.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_85.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_86.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_87.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_88.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_89.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_9.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_90.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_91.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_92.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_93.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_94.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_95.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_96.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_97.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_98.res`
- `A` `resources/models/zhu_yuan/presentation_source/materials/material_99.res`

### Runtime model source

10 paths.

- `A` `scenes/models/CharacterModel.Appearance.cs`
- `A` `scenes/models/CharacterModel.Masking.cs`
- `A` `scenes/models/CharacterModel.Wardrobe.cs`
- `A` `scenes/models/CharacterModel.cs`
- `A` `scenes/models/ModelClothingPiece.cs`
- `A` `scenes/models/ModelMaskConfiguration.cs`
- `A` `scenes/models/ModelMaskGeometry.cs`
- `A` `scenes/models/ModelMaterials.cs`
- `A` `scenes/models/ModelMeshMaskSetup.cs`
- `A` `scenes/models/ModelOutfit.cs`

### Blender sources import settings and wrapper scenes

6 paths.

- `M` `scenes/models/Trigger/Trigger.scn`
- `A` `scenes/models/Trigger/Trigger4.2.blend`
- `A` `scenes/models/Trigger/Trigger4.2.blend.import`
- `A` `scenes/models/ZhuYuan/ZhuYuan.blend`
- `A` `scenes/models/ZhuYuan/ZhuYuan.blend.import`
- `M` `scenes/models/ZhuYuan/ZhuYuan.scn`

### PNG model inputs and texture import settings

32 paths.

- `A` `scenes/models/Trigger/Trigger4.2_Female_Face_Lightmap.png`
- `A` `scenes/models/Trigger/Trigger4.2_Female_Face_Lightmap.png.import`
- `A` `scenes/models/Trigger/Trigger4.2_Nail.png`
- `A` `scenes/models/Trigger/Trigger4.2_Nail.png.import`
- `A` `scenes/models/Trigger/Trigger4.2_Tang_MainTex02.png`
- `A` `scenes/models/Trigger/Trigger4.2_Tang_MainTex02.png.import`
- `A` `scenes/models/Trigger/Trigger4.2_Trigger_Body_Map1_N.png`
- `A` `scenes/models/Trigger/Trigger4.2_Trigger_Body_Map1_N.png.import`
- `A` `scenes/models/Trigger/Trigger4.2_Trigger_Body_Map2_N.png`
- `A` `scenes/models/Trigger/Trigger4.2_Trigger_Body_Map2_N.png.import`
- `A` `scenes/models/Trigger/Trigger4.2_Trigger_Face_D.png`
- `A` `scenes/models/Trigger/Trigger4.2_Trigger_Face_D.png.import`
- `A` `scenes/models/Trigger/Trigger4.2_Trigger_Weapon_N.png`
- `A` `scenes/models/Trigger/Trigger4.2_Trigger_Weapon_N.png.import`
- `A` `scenes/models/Trigger/Trigger4.2_pths.png`
- `A` `scenes/models/Trigger/Trigger4.2_pths.png.import`
- `A` `scenes/models/ZhuYuan/ZhuYuan_02_ZZZ_B.png`
- `A` `scenes/models/ZhuYuan/ZhuYuan_02_ZZZ_B.png.import`
- `A` `scenes/models/ZhuYuan/ZhuYuan_Female_Face_lightmap.png`
- `A` `scenes/models/ZhuYuan/ZhuYuan_Female_Face_lightmap.png.import`
- `A` `scenes/models/ZhuYuan/ZhuYuan_Tang_MainTex02.png`
- `A` `scenes/models/ZhuYuan/ZhuYuan_Tang_MainTex02.png.import`
- `A` `scenes/models/ZhuYuan/ZhuYuan_ZhuYuan_Body_Map1_N.png`
- `A` `scenes/models/ZhuYuan/ZhuYuan_ZhuYuan_Body_Map1_N.png.import`
- `A` `scenes/models/ZhuYuan/ZhuYuan_ZhuYuan_Body_Map2_N.png`
- `A` `scenes/models/ZhuYuan/ZhuYuan_ZhuYuan_Body_Map2_N.png.import`
- `A` `scenes/models/ZhuYuan/ZhuYuan_ZhuYuan_Face_D.png`
- `A` `scenes/models/ZhuYuan/ZhuYuan_ZhuYuan_Face_D.png.import`
- `A` `scenes/models/ZhuYuan/ZhuYuan_ZhuYuan_Weapon_Map2_N.png`
- `A` `scenes/models/ZhuYuan/ZhuYuan_ZhuYuan_Weapon_Map2_N.png.import`
- `A` `scenes/models/ZhuYuan/ZhuYuan_yelan.png`
- `A` `scenes/models/ZhuYuan/ZhuYuan_yelan.png.import`

### Importer source

3 paths.

- `A` `scenes/models/import/humanoid_post_import.gd`
- `A` `scenes/models/import/humanoid_presentation.gd`
- `A` `scenes/models/import/humanoid_structure.gd`

### Shared shader source

2 paths.

- `A` `scenes/models/shared/character.gdshader`
- `A` `scenes/models/shared/outline.gdshader`

### Feature tests and shared test support

10 paths.

- `A` `test/CharacterModelAnimationAssetTest.cs`
- `A` `test/CharacterModelAppearanceAttachmentTest.cs`
- `A` `test/CharacterModelAssetTest.cs`
- `A` `test/ModelAuthoringApiExampleTest.cs`
- `A` `test/ModelClothingTest.cs`
- `A` `test/ModelMaskWardrobeTest.cs`
- `M` `test/helpers/Assert.cs`
- `A` `test/helpers/ModelAnimationGraph.cs`
- `A` `test/helpers/ModelFixture.cs`
- `M` `test/helpers/TestData.cs`

