Grace Crowne by Dr. Drigg - CharacterModel integration

Open GraceCrowne.scn to use the existing CharacterModel interfaces. The four
outfits are Suit, Exposed, Unzipped and Zipped. Pieces are Suit, Headband, Gadgets,
Goggles and Mask. Gadgets are a separately controllable mesh in the default Suit;
the other creator suit meshes have no separate gadget polygons to extract.
Selections is still the serialized wardrobe state. No runtime API was added.

ModelAnimationTree uses the shared body library (Movement and HeadTurn parameters)
and 73 independent native facial layers, e.g. parameters/eyeBlinkL/add_amount.
The face clips and graph are ordinary editable Godot resources in
resources/models/grace_crowne/. Face amounts are additive; overlapping expressions
can exceed the original creator's intended range. The body's identity and fit are
frozen in Basis rather than offered as runtime morph controls. The creator's
corrective smoothing and body subdivision are evaluated into Basis and each facial delta at preparation;
removing it without freezing its result would leave folded skin at the neckline.

authoring/GraceCrowne.blend is the clean, editable derived Blender source. Its
.gdignore prevents Godot from also importing it with the built-in Blender runner.
The runtime imports GraceCrowne.glb. Original creator scripts/drivers/constraints
and animation were removed; embedded code is never needed for runtime.

Rebuild from a copy of the creator source:
  blender --background --factory-startup --disable-autoexec source-copy.blend
    --python-exit-code 1 --python tools/models/prepare_grace_crowne.py --
    --output /absolute/path/to/scenes/models/GraceCrowne

For later edits, save the derived blend in Blender, triangulate any new topology,
keep facial amounts neutral, and export it with refreshed geometry/mask metadata.
Body mask membership is the Blender FACE/INT attribute grace_mask_bits: bits
1/2/4/8 hide a polygon for Suit/Exposed/Unzipped/Zipped. These frozen parent-face
flags survive subdivision; interpolated original KEEP groups are reference data.
  blender --background --factory-startup --disable-autoexec authoring/GraceCrowne.blend
    --python-exit-code 1 --python tools/models/export_grace_crowne.py --
    --output /absolute/path/to/scenes/models/GraceCrowne/GraceCrowne.glb

Then import in Godot. Source IDs, normalized bones, weights and frozen body/hair
masks are validated at import; changed geometry cannot silently reuse stale flags.
Original Mask KEEP groups are translated into runtime REMOVE triangle bits.
The import compiles the existing ModelWardrobeConfiguration/ModelMaskConfiguration
resources and reuses their normal runtime behavior. No creator driver interpreter
or universal rig adapter is involved.

To rebuild import settings/wrapper/native face resources from scratch:
  dotnet build
  godot --headless --path . --script tools/models/build_grace_crowne.gd -- configure
  godot --headless --editor --import --path .
  godot --headless --path . --script tools/models/build_grace_crowne.gd -- wrapper

Materials are restored from the selected source node graphs, not the shared
Trigger/Zhu Yuan toon shader. materials/materials.json records linked source
values and the source hash. Static base colour, tangent normals, metallic /
roughness / specular and skin / clearcoat masks are extracted with EMIT/NORMAL
bakes. These retain source colour mixes, nail masks, corrected normal channels,
FPv4 packed _S/_M semantics, strand normal shaping, and selected clearcoat.
Emission uses linear HDR EXR. No camera or light response is baked into colour.

grace_crowne.gdshader evaluates DD Fresnel colour, FPv4 hair grazing colour and
emission, accessory emission falloff and an approximate corneal UV parallax live.
The source's selected anisotropic BSDF switch is off; strand/tangent assets are
retained and static strand shaping survives in the normal/roughness channels.
The source MetalLUT gate is retained, including FPv4 FX Mask's blue-only semantics.
Materials are scene-local overrides applied before body/hair masks are compiled;
shaders and textures stay shared. All existing CharacterModel controls remain.

For a material-only refresh, safely open the untouched creator source and run:
  blender --background --factory-startup --disable-autoexec source-copy.blend
    --python-exit-code 1 --python tools/models/extract_grace_materials.py --
    --output /absolute/path/to/scenes/models/GraceCrowne/materials
Then configure material texture mipmaps, import, and rebuild the wrapper:
  godot --headless --path . --script tools/models/build_grace_crowne.gd -- materials
  godot --headless --editor --import --path .
  godot --headless --path . --script tools/models/build_grace_crowne.gd -- wrapper
The preparation script performs extraction automatically in a separate process,
then binds supported static PBR channels into the clean derived Blender source.

Renderer approximations: Godot and Blender Principled/EEVEE use different specular
and clearcoat lobes, shadow filtering and subsurface profiles. Godot's screen-space
skin diffusion approximates the source's 5 mm scale relative to the project SSS
scale; Mobile does not support that screen-space diffusion. Eye parallax uses the
source atlas centres, corneal radius and refraction scale on the deformed tangent
frame, but does not reproduce FPv4's full layered eye/displacement construction.
AgX, exposure, orthographic camera and directional light are matched for evidence;
Godot energy is divided by PI to match Blender Sun irradiance. Different native
SSS/highlight behaviour remains visible and is not baked into the assets.

All weighted bones and ancestry remain, with deliberately normalized <=8 weights.
Extreme deformation still requires visual review when extending animation coverage.
The installed Blender 5.1.1 reports the creator source as a newer file (502.44);
the derived file is saved by 5.1.1 and the chosen output is verified in Godot.

Source identity and conversion statistics are recorded in authoring-data.json.
Original assets are preserved; all conversion output is local.
