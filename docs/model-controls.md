# Character model controls

`CharacterModel` (in `scenes/models/`) is the single C# entry point on the Zhu Yuan and Trigger model scenes — one root node owning material isolation, mesh masks, clothing, face lighting, and attachments directly, with its source split across `CharacterModel.cs` and the `CharacterModel.Appearance.cs`/`CharacterModel.Wardrobe.cs`/`CharacterModel.Masking.cs` partials. Native Godot `Animation` clips ARE the expression presets: each preset is an authored clip — a single clip may author several facial channels — and a native `AnimationTree` per model drives them, with the graph's own parameters as the independently adjustable preset weights. Designers edit, pose, and keyframe clips in Godot, wire and preview presets in the native `AnimationTree` panel, and can keyframe preset weights like any parameter. Wardrobe selection runs through one uniform clothing API on the root. The tongue and optional-weapon attachments are import-owned native topology (a `BoneAttachment3D` and fixed import-time groups), not scripted services. Each scene is a thin, noneditable wrapper around its authoritative Blender import — meshes, materials, mesh defaults, and hidden source collections are rebuilt by the import pipeline, so import-owned visuals are edited in the Blender source plus the committed import manifests, never inside the wrapper. Everything here is presentation: no battle runtime, no combatant context.

## Scenes and resources

| Asset | Purpose |
| --- | --- |
| `res://scenes/models/ZhuYuan/ZhuYuan.scn` | Zhu Yuan: a thin, noneditable wrapper. Root is `CharacterModel` with its fixed `ModelAnimationTree` child resolved by name; a `Model` child instances the imported Blender scene, the mask setups, wardrobe configuration, and selections are authored on the root, and `AttachmentsPath` points at the import-owned, default-hidden weapons group `Model/rig_D/GeneralSkeleton/Weapons`. |
| `res://scenes/models/Trigger/Trigger.scn` | Trigger: the same root shape minus attachments — `AttachmentsPath` is empty and its optional weapon source collection stays hidden inside the import (it is not an extracted production scene). |
| `res://scenes/models/ZhuYuan/ZhuYuan.blend`, `res://scenes/models/Trigger/Trigger4.2.blend` | Authoritative Blender sources. Godot's import pipeline — `scenes/models/import/humanoid_post_import.gd` with `humanoid_structure.gd` and `humanoid_presentation.gd` — normalizes the skeleton, reparents the tongue rig, adds Zhu Yuan's weapon copies and derived arm meshes, and applies the committed per-model manifests. Each model folder carries eight committed PNG inputs beside its Blender file and wrapper, with matching import sidecars. |
| `res://resources/models/shared/humanoid_body.res` | One shared body animation library for both models: `neutral`, `head_turn`, and `motion_demo` as rotation-only tracks on the normalized `Head`/`LeftUpperArm` bones. |
| `res://resources/models/<model>/humanoid_face.res` | The model's face library: `RESET`, `neutral` (the authored neutral baseline of every animated face channel — not the skeleton bind/rest pose), and one clip per expression preset (`expr_<Preset>`); Zhu Yuan carries 42 clips, Trigger 26. |
| `res://resources/models/<model>/humanoid_face_tree.res` | The model's `AnimationNodeBlendTree` (Zhu Yuan 168 nodes, Trigger 104): the movement/head-turn spine, the `FaceBase` neutral-face layer, and one additive block per expression preset. |
| `res://resources/models/<model>/import_bindings.res`, `res://resources/models/<model>/presentation/presentation.res` | Import-time manifests applied on every reimport: the structure recipe (tongue placement, weapon copies) and the presentation manifest (captured surface materials, saved mesh defaults, required hidden groups). |
| `res://resources/models/<model>/presentation/body_mask.res`, `res://resources/models/trigger/presentation/pantyhose_mask.res` | Saved typed `ModelMaskConfiguration` mesh-mask configurations, including original embedded default meshes. |
| `res://resources/models/<model>/presentation/*_mask_setup.tres` | Saved `ModelMeshMaskSetup` resources, each binding one masked mesh's root-relative path to the `ModelMaskConfiguration` beside it; the root's `MaskSetups` array references them. |
| `res://scenes/models/shared/character.gdshader`, `outline.gdshader` | Shared shader source for surfaces and their authored outline passes. |

The wrapper's `ModelAnimationPlayer` carries two named libraries — `body` (the shared `humanoid_body.res`) and `face` (the per-model `humanoid_face.res`) — so both models share one body clip set while expression clips stay per-model; graph clip references use those library names (`body/neutral`, `face/expr_<Preset>`, …). Scene trees stay active: `ModelAnimationTree.active = true` is saved in both scenes, so the native mixer drives the rig from load. The root's fixed `ModelAnimationTree` child resolves once when the root's `_Ready` runs; a missing child of that name leaves the animation component unconfigured (synthetic fixtures do this), and a child of the wrong type is an authoring error. `MeshRoot` and `AttachmentsPath` are the path-based, optional inputs.

## The model root

One node owns the whole scripted surface — there are no scripted component child nodes. The root's exports:

| Export | Purpose |
| --- | --- |
| `MeshRoot` | Path of the mesh container whose surface materials are isolated once per instance and whose meshes are scanned for face materials. Empty leaves the appearance unconfigured. |
| `HeadBoneName` | Bone driving the face-lighting axes; fixed for the initialized instance like `MeshRoot`. |
| `MaskSetups` | Array of `ModelMeshMaskSetup` resources, each binding one root-relative masked-mesh path to its typed `ModelMaskConfiguration`. |
| `WardrobeConfiguration` | Authored wardrobe dictionary: variants, components with pieces, and mask rules. |
| `Selections` | Per-instance clothing selections (`outfit`, `clothing_enabled`, `pieces/<name>`) — the sole serialized clothing control surface. |
| `AttachmentsPath` | Path of the optional attachment container; empty means no attachment set. |
| `AttachmentsVisible` | Whole-set visibility toggle for the attachment container. |

Initialization order is fixed: `_EnterTree` isolates the surface materials (the root's `_EnterTree` always precedes its own `_Ready`, where the masks first render, so the ordering never depends on sibling node order), then `_Ready`/`Initialize()` initializes the mask setups against their meshes, the wardrobe against the now-ready masks, and the attachments — `isolate → masks → wardrobe → attachments`. Editor startup can assign the typed `MaskSetups` array before the C# class can instantiate, leaving the property empty through a failed `_Ready`; a later non-null assignment to a ready, still-uninitialized model retries `Initialize` (see [Clothing](#clothing-one-uniform-wardrobe-api)). Models are instanced fresh per scene and freed with it — nothing detaches an initialized model and re-adds it (hiding runs through `AttachmentsVisible`, not tree removal) — so initialization runs once per instance.

Mask state is runtime-derived, never serialized: each setup resolves its mesh/configuration pair into a mask runtime that mints the region entries and seeds the states from its configuration defaults on initialization, and every wardrobe write re-derives the complete combination from the controlled and active region entries. `Selections` is the only clothing state that persists in the scene.

## The animation tree graph

Both models use the same graph shape:

- **Movement** — a `Blend2` mixing `Neutral` (`body/neutral`) into `MotionDemo` (`body/motion_demo`), driven by the `parameters/Movement/blend_amount` graph parameter.
- **HeadTurn** — a `Blend2` mixing the movement result into `HeadTurnClip` (`body/head_turn`), filtered to the normalized head bone (`Model/rig_D/GeneralSkeleton:Head`), driven by `parameters/HeadTurn/blend_amount`.
- **FaceBase** — a face-filtered `Add2` (saved at `FaceBase/add_amount = 1`) mixing `FaceNeutral` (`face/neutral`) onto the body-pose path before the first preset layer, restoring the facial neutral baseline `N` ahead of the additive expression deltas.
- **Preset chain** — one `Add2` layer per expression preset; its first input is the running pose (`FaceBase`, then the previous layer), its second input is that preset's neutral-relative delta. The final output is the last layer (currently `Face_Mouth_uu_oo`).
- **Per preset** — a `Sub2` node subtracts a neutral `Ref` clip from the preset's own `Clip`, so the stored expression endpoint is applied relative to the neutral pose.

Every preset follows the same authoring model: the authored neutral baseline `N` (the `neutral` clip — an authored pose, never the skeleton bind/rest pose), the absolute expression endpoint `E` (the preset clip's authored value), and the additive contribution `weight * (E − N)`. Overlapping contributions add: the full output along the body-pose path is `N` plus the summed weighted deltas, with no normalization or clamping.

Graph nodes concatenate the preset's name with a role suffix; preset names are authored directly as the node base names. The preset named `Face_Brows_angry` has the four nodes:

| Node | Class | Role |
| --- | --- | --- |
| `Face_Brows_angryClip` | `AnimationNodeAnimation` | plays the expression clip `face/expr_Face_Brows_angry` |
| `Face_Brows_angryRef` | `AnimationNodeAnimation` | plays the model's `face/neutral` clip (the neutral-reference node) |
| `Face_Brows_angryDelta` | `AnimationNodeSub2` | clip minus neutral; its amount is not on this graph resource — it lives as the consuming scene's `parameters/Face_Brows_angryDelta/sub_amount`, kept at `1` |
| `Face_Brows_angry` | `AnimationNodeAdd2` | the additive layer; its `parameters/Face_Brows_angry/add_amount` is the preset's weight parameter |

Zhu Yuan currently carries 40 preset blocks (5 brows, 11 eyes, 24 mouth) and Trigger carries 24 (mouth); treat those as current authored facts, not an inventory. Every preset layer's `add_amount` and both spine `blend_amount`s default to `0`, so scenes load in the neutral pose.

Node positions in the blend-tree editor are layout-only. They follow a deterministic grid — clips and neutral references in one column, deltas next, the additive chain next, the output last, blocks stacked in sorted name order — and moving a node never changes evaluation.

## Reading and driving preset weights

Preset weights are ordinary `AnimationTree` graph parameters: a preset's weight is `parameters/<Preset>/add_amount` on the model's live `AnimationTree` node. Overlapping neutral-relative contributions add without normalization or clamping, so driving two eye presets at once simply sums their deltas. There is no expression component and no custom accessor — read and write the parameters directly:

```csharp
// The model's animation tree; presets are its graph parameters.
Godot.AnimationTree tree = model.AnimationTree;

// Independent, combinable weights.
tree.Set("parameters/Face_Brows_angry/add_amount", 0.5);
double weight = tree.Get("parameters/Face_Brows_angry/add_amount").AsDouble();
```

The parameters live on the per-instance `AnimationTree`, so weights stay fully separate across instances and die with the instance. `Add2` amounts are plain parameters with no custom range or lifetime guarantees — Godot's own parameter semantics are the contract.

## Clothing: one uniform wardrobe API

The root parses its authored `WardrobeConfiguration` dictionary once and exposes every piece and outfit as a discovered entry — callers never address garment nodes or variant indices directly.

```csharp
// Piece and outfit IDs come from the model's own Pieces/Outfits inventories;
// the snippet shows the call shapes with illustrative names.
foreach (ModelClothingPiece piece in model.Pieces) { /* discover */ }
foreach (ModelOutfit outfit in model.Outfits) { /* discover */ }

Option<ModelClothingPiece> piece = model.FindPiece("Gloves");
Option<ModelOutfit> outfit = model.FindOutfit("Clothing 2");

model.SelectOutfit(outfit.Match(o => o, () => throw new InvalidOperationException("missing")));
model.SetPieceEnabled(piece.Match(p => p, () => throw new InvalidOperationException("missing")), false);
model.ClothingEnabled = false;   // hide the whole outfit
model.ClothingEnabled = true;    // restore it exactly
```

- **Saved versus effective visibility.** `piece.Enabled` is the saved per-piece selection: it only changes through `SetPieceEnabled`, and the master switch never overwrites it. `piece.IsVisible` is the effective state: selected, clothing enabled, and the current outfit owns at least one garment of the piece. `ClothingEnabled = false` therefore hides the outfit while preserving every saved selection for the restore.
- **Preflight before visible changes.** Every write derives the complete resulting clothing state first; required garment nodes and every affected mask's prepared region selection validate before the selection is stored or anything is written, so a rejected write leaves selections, garments, and masks untouched. Selections serialized in the scene (or written before initialization) apply on first initialization, and every initialized write preflights and applies immediately.
- **Editor inspector checkbox.** The model root exposes a `Clothing Enabled` checkbox in the editor inspector. It is an editor-only view of the `Selections` entry `clothing_enabled` — the same state the C# setter writes — so toggling it applies garment visibility and body-mask changes immediately in the editor (ordinary editor undo/redo applies) and the choice persists with the scene through `Selections`. The virtual property itself is never serialized, and the runtime C# API is unchanged.
- **Live `Selections` edits.** Inspector dictionary edits replace the whole `Selections` value; once the model is initialized, the complete stored state preflights and applies immediately (outfit and `pieces/<name>` toggles apply live like the checkbox), a rejected combination reverts the assignment and rethrows, and a replacement made before initialization is stored as-is.
- **Component selections listed.** Initialization materializes one `pieces/<name>` entry per authored component from its authored default — never overwriting a stored selection, and never materializing `outfit` or `clothing_enabled` — so the inspector's `Selections` dictionary lists every component the model has, ready to toggle. A whole-dictionary replacement re-materializes them.
- **Recovery from a failed initialization.** Editor startup can assign the typed `MaskSetups` array before the C# class can instantiate it, so the exported setups land empty and the root fails its `_Ready` (leaving the wardrobe uninitialized too). A later non-null assignment to a ready, still-uninitialized model retries `Initialize`, so stored selections apply and inspector toggles work again without reloading the scene.
- **Overlapping body masks.** Garments claim body regions through mask rules; authored rules (path, name, index) bind at initialization to region entries minted by the resolved mask runtime — the wardrobe never addresses bits afterwards, and a retried initialization rebinds. When several enabled rules cover the same mesh region the contributions OR together (never last-rule-wins), and each affected mesh commits one prepared region selection that retains every region no rule controls.
- **Foreign entries are caller bugs.** `SelectOutfit`/`SetPieceEnabled` reject entries discovered by another model before any state changes.
- **Playback independence.** Clothing operations change garments and masks only; animation playback and preset weights keep running untouched.

The authored `WardrobeConfiguration` schema: `variants` (ordered labels), optional `default_variant` (Trigger authors it; `OutfitIndex` falls back to it when nothing is stored, and writes clamp to the variant range), `components` (piece name → `{visible, pieces: [{path, source, variant}]}`, where `variant: -1` means outfit-independent), and `masks` (rules with `path`, `name`, `index`, `component`, `enabled`, `variant`; saved scenes may still carry a legacy ignored `body` flag). Per-instance selection state lives in `Selections` (`outfit`, `clothing_enabled`, `pieces/<name>`) — the sole serialized clothing control surface. The string-keyed `GetPiece`/`SetPiece` remain available alongside the typed API.

## Mesh masks

Mesh masking is render-only and owned by the root: the source mesh, its blend-shape channels, and its skin stay fully editable while the `RenderingServer`'s render base swaps to a compacted variant of the source geometry. The root's `MaskSetups` array holds one `ModelMeshMaskSetup` per masked mesh, binding its root-relative mesh path to a typed `ModelMaskConfiguration` resource: Inspector-editable fields carry the mask names with their defaults (dictionary insertion order fixes the bit order), the per-surface triangle masks, the default bits with their stored default mesh, and the geometry hash. Generated variants are cached per source mesh and configuration.

Mask state is runtime-only, derived state — never serialized. Each setup's configuration resolves once at initialization into a mask runtime that owns the mesh/configuration pair: it mints one region entry per configured mask (named from the configuration's `Masks`), keeps the bit encoding private, and seeds its states from the configuration defaults; every wardrobe write re-derives the complete combination. Runtime code selects masks only through region entries their owning runtime minted — an entry from another mesh or model rejects before any state or render write, validated against the exact owning runtime, never against name or path equality. Initialization validates each setup against the actual mesh geometry before any render: at most 64 configured masks, default bits within the configured masks, per-surface triangle-mask counts matching the mesh's triangles, triangle flags naming only configured masks, and the geometry hash. Nothing mask-related persists in the saved scene: on load the mask state derives from the configurations plus `Selections` — Zhu Yuan's body mask initializes at its default bits 63 (`Masks_ZZZ_Size02_C`, rendering the stored default mesh's 1566 triangles), and Trigger's default outfit 0 derives body bits 7 (6054 triangles) with `Masks_Pantyhose` at bits 0 (the full source mesh's 8404 triangles).

## Appearance, materials, and authored outlines

Outlines are authored material settings, not runtime state: each outlined surface's material carries the shared outline shader as a `NextPass` with its authored `width_scale` and enabled flag. The root duplicates the mesh root's mutable materials once per instance (`ModelMaterials.Isolate` over `MeshRoot`), so runtime edits — including mask-driven outline weights — stay per-instance while shared shaders and textures stay shared.

Face materials that enable `use_face_sdf` receive world-space lighting axes derived from the bone named by the root's `HeadBoneName` (authored as `Head` — the normalized imported bone — on both wrapper scenes; the C# default stays `head.x` for legacy scenes): the root samples the head pose after the native mixer applies its own (`ProcessPriority = 1`) and updates `head_forward_world`/`head_right_world` every frame — automatically during playback, or on demand via `UpdateFaceAxes()`.

## Attachments

Attachments are fixed scene structure owned by the Blender import, shown or hidden as one unit from the model root:

- **`AttachmentsPath` / `AttachmentsVisible`** (on `CharacterModel`): the root resolves the authored attachment container once during initialization and never re-resolves it. An empty path means the model has no attachment set (Trigger) — assignments then store only; a nonempty path that misses or mis-types its container is an authoring error. The boolean toggles only that container's own `visible` flag — never child flags, animation, clothing, or expression state. Assignments before initialization are stored until ready; initialized assignments with a resolved container apply immediately. The general default is `true`; Zhu Yuan ships with `AttachmentsVisible = false` and `AttachmentsPath = Model/rig_D/GeneralSkeleton/Weapons`, so her import-owned weapons group stays hidden by default.
- **Import-owned optional weapons** (Zhu Yuan, inside the imported scene): the import script copies the four weapon meshes into fixed skeleton-space containers (`BackModule`, `LeftShoulder`, `RightShoulder`, `Belt`) under `rig_D/GeneralSkeleton/Weapons` and derives the two arm meshes (`ArmModules/LeftArmModule`/`RightArmModule`) from the source arm mesh, preserving the encoded normal/tangent stream per selected vertex. This is fixed scene topology; Trigger's optional weapon source collection stays hidden inside its import rather than being extracted.
- **Native tongue attachment** (`Model/rig_D/GeneralSkeleton/TongueAttachment`, both imported scenes): a `BoneAttachment3D` child of the imported skeleton with `bone_name = Head` and `override_pose = false` — Godot owns the bone following, so the tongue follows the normalized `Head` bone; there is no runtime tongue component. Below it, `Placement` carries the authored local transform from the import recipe and holds `Tongue_rig/Skeleton3D/Tongue`; the tongue mesh is hidden by default through the presentation manifest's mesh defaults.

Import-owned attachment nodes follow their parent skeleton whenever the model is in the tree, including while hidden. Paths, the attachment set, and the visibility default are fixed after initialization; there is no runtime attach/detach/rebind API. Deferred dynamic attachments remain future work.

## Authoring workflow

All wrapper and animation authoring happens in Godot (editor panels or scripted `ResourceSaver` writes); scenes and resources are never hand-edited. Import-owned visuals — meshes, surface materials, saved mesh defaults, hidden source collections, the tongue, and Zhu Yuan's weapons — are authored upstream: edit the Blender `.blend` source and the committed `import_bindings.res`/`presentation.res` manifests, then re-import. Never edit the imported `Model` child's nested mesh, material, or default properties inside the thin wrapper: it is noneditable, and hand edits there are lost on the next reimport.

**Adding an expression preset** to a model:

1. Author the preset clip in the model's `humanoid_face.res`: one blend-shape track per facial channel (paths under `Model/rig_D/GeneralSkeleton/...`) holding the absolute endpoint value `E` (the graph subtracts the neutral baseline `N` and scales the delta by the preset weight). A clip may author several facial channels in one preset.
2. Add the four graph nodes to `humanoid_face_tree.res` — `<Preset>Clip` (animation: the new `face/expr_<Preset>` clip), `<Preset>Ref` (animation: `face/neutral`), `<Preset>Delta` (`Sub2`), `<Preset>` (`Add2`, filter enabled on the facial blend-shape tracks the preset drives) — insert the layer into the chain after `FaceBase`, and rewire the chain's tail to the output. The subtraction amount is not a property of the `AnimationNodeSub2` graph resource: after building the nodes, set `parameters/<Preset>Delta/sub_amount = 1` on each consuming scene's actual `AnimationTree` node and save the scene parameters. Add2 weights start at `0`, as do the spine `blend_amount`s (`FaceBase/add_amount` stays at `1`).
3. Wire and preview the layer in Godot's `AnimationTree` editor, verify every clip reference resolves against the wrapper player's `body`/`face` libraries, and reload newly authored instances to confirm they load and start neutral. Runtime code performs no preset validation — authoring correctness lives in the editor workflow.

**Movement and head turn** are authored as graph parameters, not code: blend `parameters/Movement/blend_amount` toward 1 to play `motion_demo`, and `parameters/HeadTurn/blend_amount` toward 1 to play `head_turn`. Both clips come from the shared `humanoid_body.res` — rotation-only tracks on the normalized `Head`/`LeftUpperArm` bones, with one shared 12° head endpoint (see the migration qualifications below). Keep both at `0` in saved scenes so models load neutral.

**Neutral and defaults.** `neutral` exists in both libraries: the shared body `neutral` keys the imported base rotations of `Head`/`LeftUpperArm`, and the per-model `face/neutral` stores the authored neutral baseline `N` of every animated face channel — never replace either with the skeleton bind/rest pose. Expression clips store absolute endpoints `E`; the `FaceBase` layer (at `add_amount = 1`) restores `N`, and the `Sub2` blocks (driven at `sub_amount = 1` through their scene parameters) turn each endpoint into the neutral-relative delta the Add2 layers scale by their weights. When authoring, leave every preset `add_amount` and spine `blend_amount` at its default so the saved scene state stays the neutral pose.

**Preset names and overlaps.** Preset names are the graph node base names, authored directly in the blend tree; clip names follow the `expr_<Preset>` convention. Independent presets may intentionally overlap one physical channel with different contributions (the tested `Smile`/`SmileExtra` fixture pair drives the same `Face:Smile` blend shape through two weights), and that stays valid.

**Fixed topology.** After initialization the graph structure, rig paths, attachment set, and attachment visibility default are fixed — and the imported `Model` child's own topology is fixed at import. Runtime code adjusts preset weights (native `AnimationTree` parameters), clothing selections, and attachment visibility; it never rewrites graphs, node paths, or attachment wiring.

**Lifecycle.** The root initializes once when it enters the tree (material isolation in `_EnterTree`, masks/wardrobe/attachments in `_Ready`); models are instanced fresh per scene and freed with it, so preset weights, mask bits, and selections never leak into another instance. The public component getters (for example `AnimationTree`) throw while the model is uninitialized or the component is unconfigured (a root without its `ModelAnimationTree` child); an initialized model lives with its scene — freed access fails through Godot's own native errors rather than extra guards.

## Tested API example

The guide's example is compiled and executed by `test/ModelAuthoringApiExampleTest.cs` against both the shipped scenes and a synthetic fixture, using the piece ID discovered from `Pieces` — never hard-coded:

```csharp
static void Preview(CharacterModel model, StringName pieceId)
{
  ModelClothingPiece piece = model.FindPiece(pieceId).Match(
    found => found,
    () => throw new InvalidOperationException($"Missing clothing piece '{pieceId}'."));
  model.SetPieceEnabled(piece, false);
  model.ClothingEnabled = false;
}
```

This is the required-application style: a missing capability is a caller bug, so the `Option.Match` throws instead of proceeding. Optional applications instead inspect the `Option` and handle `None` (skip the effect, fall back to another piece, and so on) — `FindPiece` returning `None` is an ordinary value, not an error. Expression-preset authoring belongs to Godot; runtime code drives preset weights through the native `AnimationTree` parameters shown above.

## Inspecting the system

Everything the system owns is inspectable in the editor, read-only; changing any of it follows the [Authoring workflow](#authoring-workflow) above rather than repeating it here.

| To inspect | Where |
| --- | --- |
| Expression graph and weights | Open a wrapper scene and select `ModelAnimationTree`: the AnimationTree panel's Parameters tab shows every layer weight and drives them. |
| Clips | Select `ModelAnimationPlayer`: the Animation panel lists the `body` and `face` libraries. |
| Import results — rig, tongue, weapons, applied materials, hidden groups | Click the `.blend` in the FileSystem dock: the Import dock holds the retarget settings, and the imported scene it opens shows the applied result. |
| Humanoid bone mapping | `res://resources/models/<model>/bone_map.tres`. |
| Surface materials | The `presentation_source` `.res` files open in the Inspector. |
| Manifests (`presentation.res`, `import_bindings.res`) | The Manifest Inspector plugin (`res://addons/manifest_inspector/`) renders their metadata read-only in the Inspector, and its print button dumps the full contents to Output. The binary `.res` files are not text-diffable by design. |

## Import-migration qualifications

The original Blender-import migration was verified on a Windows PC. This review also completed fresh imports on Linux using Blender 5.1.2 and Godot 4.7.2 .NET; both committed source files require Blender 5.1 despite Trigger's filename. See [the review report](model-controls-review.md) for verification scope. Historical accepted differences: the Zhu Yuan and Trigger generated glTFs reference the shared face lightmap PNG with URIs that differ only in case (`Female_Face_lightmap.png` in Zhu Yuan's glTF, `Female_Face_Lightmap.png` in Trigger's), and the current shared imported cache stores the lowercase spelling, so Godot's case-mismatch warning comes from Trigger's capitalized request — the Windows export and runtime path worked, and fresh Linux import now succeeds with the committed model-prefixed PNG inputs; packaged export remains unverified; Trigger's pantyhose mesh from the source import differs from the legacy scene's baked pantyhose by up to ~0.0919 per vertex (the legacy bake's provenance is unestablished, and no source compensation is justified); and the shared `head_turn` endpoint is 12° where Zhu Yuan's legacy clip reached 20° — the deliberate trade for one body library shared by both models.
