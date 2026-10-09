# Character model controls

`CharacterModel` in `scenes/models/` owns the Zhu Yuan and Trigger models' mesh masks, clothing, and attachments. Its implementation spans `CharacterModel.cs`, `CharacterModel.Wardrobe.cs`, and `CharacterModel.Masking.cs`.

Native Godot `Animation` clips define expression presets. A clip can drive several facial channels. Each model's `AnimationTree` drives these clips through preset-weight parameters.

The root exposes the clothing API. Blender import creates the tongue and optional-weapon attachments. The complete system belongs to presentation.

The pipeline rebuilds topology and source presentation on import; the committed wrappers also retain editable `Model` instances and local material overrides. Prefer upstream source/manifest edits for new import-owned visual changes, and reconcile any retained wrapper override when changing the corresponding source material.

## Scenes and resources

| Asset | Purpose |
| --- | --- |
| `res://scenes/models/ZhuYuan/ZhuYuan.scn` | Zhu Yuan wrapper. Root is `CharacterModel` with its fixed `ModelAnimationTree` child; a `Model` child instances the imported Blender scene; selections save on the root; the wardrobe catalog with fixed mask bindings comes from the imported `Model`; `AttachmentsPath` points at the import-owned, default-hidden weapons group `Model/rig_D/GeneralSkeleton/Weapons`. |
| `res://scenes/models/Trigger/Trigger.scn` | Trigger wrapper with an empty `AttachmentsPath`. |
| `<model>.blend` sources | Authoritative Blender sources. The import pipeline (`scenes/models/import/humanoid_post_import.gd` with `humanoid_structure.gd` and `humanoid_presentation.gd`) normalizes the skeleton, reparents the tongue rig, adds Zhu Yuan's weapon copies and derived arm meshes, and applies the committed per-model manifests. Each model folder carries eight committed PNG inputs beside the Blender file. |
| `res://resources/models/shared/humanoid_body.res` | Shared body library: `neutral`, `head_turn`, `motion_demo` as rotation-only tracks on the normalized `Head`/`LeftUpperArm` bones. |
| `res://resources/models/<model>/humanoid_face.res` | Face library: `RESET`, `neutral` (the authored neutral baseline of every animated face channel), and one clip per expression preset (`expr_<Preset>`). |
| `res://resources/models/<model>/humanoid_face_tree.res` | The `AnimationNodeBlendTree`: movement/head-turn spine, the `FaceBase` neutral-face layer, and one additive block per preset. |
| `import_bindings.res` / `presentation/presentation.res` | Import-time manifests applied on every reimport: structure recipe (tongue placement, weapon copies) and presentation manifest (captured surface materials, saved mesh defaults, required hidden groups). |
| `body_mask.res`, `pantyhose_mask.res` | Saved typed `ModelMaskConfiguration` mesh-mask configurations. |
| Imported `Model` metadata `wardrobe_catalog` | Generated, storage-only `ModelWardrobeConfiguration` compiled from Blender custom properties. Instance choices live in `Selections`. |
| `character.gdshader`, `outline.gdshader` | Shared shader source for surfaces and their authored outline passes. |

The wrapper's `ModelAnimationPlayer` carries two named libraries — `body` (shared) and `face` (per-model) — so graph clip references use those names (`body/neutral`, `face/expr_<Preset>`, …). `ModelAnimationTree.active = true` is saved in both scenes, so the mixer drives the rig from load. The root's fixed `ModelAnimationTree` child resolves once in `_Ready`; a missing child leaves the animation component unconfigured (synthetic fixtures do this), and a wrong-type child is an authoring error.

## The model root

The root owns the scripted controls and exports:

| Export | Purpose |
| --- | --- |
| `Selections` | Per-instance clothing selections (`outfit`, `clothing_enabled`, `pieces/<name>`) — the sole serialized clothing control surface. |
| `AttachmentsPath` | Path of the optional attachment container; empty means none. |
| `AttachmentsVisible` | Whole-set visibility toggle for the attachment container. |

Godot localizes marked materials during `PackedScene.Instantiate()`, before any lifecycle callback. `_Ready`/`Initialize()` orders masks → wardrobe → attachments. Each scene creates and frees its own model instances. Topology stays fixed for an instance's lifetime. Mask state derives from defaults and selections. `Selections` is the persisted clothing state.

## Animation graph

Both models share one graph shape:

- **Movement** — a `Blend2` mixing `body/neutral` into `body/motion_demo`, driven by `parameters/Movement/blend_amount`.
- **HeadTurn** — a `Blend2` mixing the movement result into `body/head_turn`, filtered to the normalized head bone, driven by `parameters/HeadTurn/blend_amount`.
- **FaceBase** — a face-filtered `Add2` (saved at `add_amount = 1`) mixing `face/neutral` onto the body-pose path before the first preset layer.
- **Preset chain** — one `Add2` layer per preset; first input is the running pose, second input is that preset's neutral-relative delta.

Every preset subtracts its neutral reference: the output is the authored neutral baseline `N` plus `weight * (E − N)`, where `E` is the preset clip's absolute endpoint. Overlapping contributions sum directly, preserving their full combined value.

Node names concatenate the preset name with a role suffix; the preset named `Face_Brows_angry` has:

| Node | Class | Role |
| --- | --- | --- |
| `Face_Brows_angryClip` | `AnimationNodeAnimation` | plays `face/expr_Face_Brows_angry` |
| `Face_Brows_angryRef` | `AnimationNodeAnimation` | plays `face/neutral` |
| `Face_Brows_angryDelta` | `AnimationNodeSub2` | clip minus neutral; its amount lives as the consuming scene's `parameters/Face_Brows_angryDelta/sub_amount`, kept at `1` |
| `Face_Brows_angry` | `AnimationNodeAdd2` | the additive layer; `parameters/Face_Brows_angry/add_amount` is the preset weight |

Every preset layer's `add_amount` and both spine `blend_amount`s default to `0`, so scenes load neutral. Node positions in the editor are layout-only.

## Reading and driving preset weights

Preset weights are ordinary `AnimationTree` graph parameters — `parameters/<Preset>/add_amount` on the model's live tree. Read and write these parameters through the native tree:

```csharp
Godot.AnimationTree tree = model.AnimationTree;
tree.Set("parameters/Face_Brows_angry/add_amount", 0.5);
double weight = tree.Get("parameters/Face_Brows_angry/add_amount").AsDouble();
```

Parameters live on the per-instance tree, so weights stay separate across instances and die with the instance. Godot's own parameter semantics are the contract.

## Clothing: one uniform wardrobe API

The root consumes the shared typed `ModelWardrobeConfiguration` and creates only instance-scoped piece/outfit controls and mask-region bindings. Discover pieces and outfits through the root's inventories:

```csharp
foreach (ModelClothingPiece piece in model.Pieces) { /* discover */ }
foreach (ModelOutfit outfit in model.Outfits) { /* discover */ }

Option<ModelClothingPiece> piece = model.FindPiece("Gloves");
Option<ModelOutfit> outfit = model.FindOutfit("Clothing 2");

piece.Match(p => p, () => throw new InvalidOperationException("missing")).Enabled = false;
model.ClothingEnabled = false;   // hide the whole outfit
model.ClothingEnabled = true;    // restore it exactly
```

- **Clothing visibility.** `piece.Enabled` stores the per-piece selection and changes through its setter. The master switch preserves that selection. `piece.IsVisible` is the effective state: selected, clothing enabled, and the current outfit owns a garment of the piece. `ClothingEnabled = false` hides the outfit while preserving every saved selection.
- **Preflight.** Every write derives the complete resulting state first and checks live garment and mask nodes for teardown before storing the selection, so a rejected write leaves selections, garments, and masks untouched. Selections written before initialization apply on first initialization.
- **Editor inspector checkbox.** The root exposes a `Clothing Enabled` checkbox — an editor-only view of the `Selections` entry `clothing_enabled`. Toggling it applies garment and body-mask changes immediately in the editor, with ordinary undo/redo; the choice persists through `Selections`.
- **Live `Selections` edits.** Inspector dictionary edits replace the whole value; once initialized, the stored state preflights and applies immediately, and a rejected combination reverts the assignment and rethrows. A replacement made before initialization is stored as-is.
- **Component selections listed.** Initialization adds each missing `pieces/<name>` entry from its authored default. It preserves stored choices and leaves `outfit` and `clothing_enabled` entries as supplied.
- **Overlapping body masks.** Garments claim body regions through mask rules, import-checked and bound by index at initialization. When several enabled rules cover one region, the contributions combine with OR. Each mesh preserves its uncontrolled regions.
- **Instance-scoped controls.** `piece.Enabled` and `outfit.Select()` operate on the instance that exposes them.
- **Playback independence.** Clothing operations change garments and masks only; animation keeps running.

The importer compiles four storage-only resource types: catalog, components, garment paths, and mask rules. C# exposes read-only views. The root reads the catalog from its imported `Model` child.

Import validates IDs, variants, garment references, mask links, and source geometry before publishing the catalog. Runtime binds the validated definitions once. Selection writes use those bindings. The string-keyed `GetPiece`/`SetPiece` APIs remain available.

## Mesh masks

Mesh masking is render-only and root-owned: source mesh, blend shapes, and skin stay intact while the `RenderingServer` render base swaps to a compacted variant. The importer compiles one `ModelMeshMaskSetup` per fixed source mesh into the catalog, pairing the resolved path with the checked `ModelMaskConfiguration`. Generated variants are cached by source mesh and configuration identity.

`ModelImportValidation` runs from the enforced Blender post-import callback. It checks the 64-bit limit, default and triangle bit ranges, source geometry hash, indexed triangle topology, per-surface counts, supported whole-vertex channels including blend shapes, and default mesh/remap consistency. Mask outline weights must match source vertices, and mutable outline materials must be scene-local. Invalid data rejects import before the catalog is published. Runtime masks use the validated definitions.

Each instance derives mask state from imported defaults and `Selections`. Overlapping rules combine with OR and preserve uncontrolled regions.

## Appearance, materials, and authored outlines

Each mutable surface override and its mutable outline `NextPass` must be saved with `ResourceLocalToScene = true`. Godot creates the instance-local materials during `Instantiate()`. Shaders and authored textures stay shared. Masking assigns an instance-specific `vertex_weights` texture reference and preserves the shared source texture.

Local-to-scene preserves aliases within a scene, so independently masked surfaces need distinct authored outline materials even when they share shader and texture. Save these flags before packing or instantiating the scene. See [Godot's resource-local-to-scene contract](https://docs.godotengine.org/en/stable/classes/class_resource.html#class-resource-property-resource-local-to-scene).

Faces use the shared shader's normal-based toon lighting. Animation deforms the skinned face.

## Attachments

Attachments are fixed scene structure owned by the Blender import, shown or hidden as one unit from the root:

- **`AttachmentsPath` / `AttachmentsVisible`**: the root binds the authored container once during initialization. An empty path stores visibility choices only (Trigger); a nonempty path that misses or mis-types the container is an authoring error. The boolean toggles only the container's own `visible` flag. Assignments before initialization are stored until ready; initialized assignments apply immediately. Zhu Yuan ships with `AttachmentsVisible = false` and `AttachmentsPath = Model/rig_D/GeneralSkeleton/Weapons`.
- **Import-owned optional weapons** (Zhu Yuan): the import copies four weapon meshes into fixed skeleton-space containers (`BackModule`, `LeftShoulder`, `RightShoulder`, `Belt`) under `rig_D/GeneralSkeleton/Weapons` and derives the two arm meshes, preserving the encoded normal/tangent stream per selected vertex. Trigger's optional weapon source collection stays hidden inside its import.
- **Native tongue attachment** (both imported scenes): a `BoneAttachment3D` under the imported skeleton with `bone_name = Head` and `override_pose = false`; below it, `Placement` holds the authored local transform and the tongue mesh, hidden by default through the presentation manifest.

Import-owned attachment nodes follow their skeleton whenever the model is in the tree, including while hidden. Paths, the attachment set, and the visibility default stay fixed after initialization. Dynamic attachments remain deferred.

## Authoring workflow

Author wrappers and animations through Godot editor panels or scripted `ResourceSaver` writes. Import-owned visuals — meshes, surface materials, saved mesh defaults, hidden source collections, the tongue, Zhu Yuan's weapons — are authored upstream: edit the Blender source and the committed manifests, then re-import. The wrappers retain `[editable path="Model"]` and local material overrides, which can take precedence over imported presentation settings; prefer upstream edits and reconcile existing wrapper overrides, then verify the saved wrapper after reimport.

**Adding an expression preset:**

1. Author the preset clip in the model's `humanoid_face.res`: one blend-shape track per facial channel (paths under `Model/rig_D/GeneralSkeleton/...`) holding the absolute endpoint `E`. A clip may author several channels in one preset.
2. Add the four graph nodes to `humanoid_face_tree.res` — `<Preset>Clip`, `<Preset>Ref`, `<Preset>Delta` (`Sub2`), `<Preset>` (`Add2`, filter enabled on the facial tracks the preset drives) — insert the layer after `FaceBase`, and rewire the chain tail to the output. Set `parameters/<Preset>Delta/sub_amount = 1` on each consuming scene's actual `AnimationTree` node and save the scene parameters. Add2 weights start at `0`, as do the spine `blend_amount`s (`FaceBase/add_amount` stays at `1`).
3. Wire and preview the layer in the `AnimationTree` editor, verify every clip reference resolves against the `body`/`face` libraries, and reload newly authored instances to confirm they load neutral. Validate presets through this editor workflow.

**Movement and head turn** are authored as graph parameters: blend `parameters/Movement/blend_amount` and `parameters/HeadTurn/blend_amount` toward `1` to play the clips. Keep both at `0` in saved scenes so models load neutral.

**Neutral and defaults.** The shared body `neutral` keys the imported base rotations; the per-model `face/neutral` stores the authored neutral baseline `N`. Preserve each clip's authored baseline. Expression clips store absolute endpoints `E`; the `FaceBase` layer restores `N`, and the `Sub2` blocks turn each endpoint into the delta the Add2 layers scale by their weights.

**Preset names and overlaps.** Preset names are the graph node base names; clip names follow `expr_<Preset>`. Independent presets may intentionally overlap one physical channel with different contributions, and that stays valid.

**Wardrobe membership belongs to Blender.** The `rig_D` object carries a `wardrobe_catalog` JSON custom property (version `1`, ordered `variants`, `default_variant`, `components`, `masks`). Referenced mesh objects carry stable `wardrobe_source_id` strings; keep those IDs when renaming. A component uses its saved selection ID and `{ "visible": true, "pieces": [{ "source": "Shirt", "variant": -1 }] }`; `variant: -1` means every outfit. A mask row has `{ "source", "name", "index", "component", "enabled", "variant" }`; an empty component is an ungated rule. Generated Zhu Yuan equipment containers use the import's fixed `role` names. Edit these properties and garment meshes in Blender, build the C# project, then re-import; `humanoid_wardrobe.gd` validates and compiles them into the imported scene. The catalog is generated output; adding or removing entries requires a source change and reimport.

**Fixed topology and lifecycle.** After initialization the graph structure, rig paths, attachment set, and visibility default are fixed; the imported `Model` child's topology is fixed at import. Runtime code adjusts preset weights, clothing selections, and attachment visibility. Graphs, node paths, and attachment wiring stay fixed. Material localization happens at instantiation; the root binds its animation child and initializes masks/wardrobe/attachments in `_Ready`. Preset weights, mask bits, and selections belong to each instance. The public component getters (for example `AnimationTree`) throw while the model is uninitialized or unconfigured; coordinated clothing writes reject a freed target before changing anything.

## Tested API example

The guide's example is compiled and executed by `test/ModelAuthoringApiExampleTest.cs` against both shipped scenes and a synthetic fixture, using a piece ID discovered from `Pieces`:

```csharp
static void Preview(CharacterModel model, StringName pieceId)
{
  ModelClothingPiece piece = model.FindPiece(pieceId).Match(
    found => found,
    () => throw new InvalidOperationException($"Missing clothing piece '{pieceId}'."));
  piece.Enabled = false;
  model.ClothingEnabled = false;
}
```

This is the required-application style: a missing capability is a caller bug, so the `Option.Match` throws. For optional applications, inspect the `Option` and handle `None` as expected absence.

## Inspecting the system

Everything the system owns is inspectable read-only in the editor; changing any of it follows [Authoring workflow](#authoring-workflow).

| To inspect | Where |
| --- | --- |
| Expression graph and weights | Open a wrapper scene and select `ModelAnimationTree`; the AnimationTree panel's Parameters tab shows every layer weight and drives them. |
| Clips | Select `ModelAnimationPlayer`; the Animation panel lists the `body` and `face` libraries. |
| Import results — rig, tongue, weapons, materials, hidden groups | Click the `.blend` in the FileSystem dock: the Import dock holds retarget settings, and the imported scene shows the applied result. |
| Humanoid bone mapping | `res://resources/models/<model>/bone_map.tres`. |
| Surface materials | The `presentation_source` `.res` files open in the Inspector. |
| Manifests | The Manifest Inspector plugin (`res://addons/manifest_inspector/`) renders their metadata read-only and can dump the full contents to Output. Use the Inspector or printed metadata to compare binary `.res` files. |

## Platform verification

The original Blender-import migration was verified on Windows; a later pass re-verified fresh imports on Linux (Blender 5.1.2, Godot 4.7.2 .NET). Both committed source files require Blender 5.1. Git history records the original review and verification scope; those historical runs are not evidence of a current passing import.
