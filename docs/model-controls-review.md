# Character model controls — historical review report

This report is historical. The current authoring and API guide is [model-controls.md](model-controls.md); git history holds the full text of the reviews summarized here.

Two review passes reshaped the model system in October 2026:

1. **Author-time ownership pass (1 October 2026).** Introduced typed authored wardrobe resources with editor-time validation plus live runtime checks; runtime code kept only instance-scoped selection controls and region bindings.
2. **Import simplification pass (2 October 2026).** Moved that definition into the Blender import: the imported scene now carries the compiled `ModelWardrobeConfiguration` catalog and fixed `ModelMeshMaskSetup` bindings, validation runs at import, and faces switched to the shared normal-based toon shader (removing face-SDF textures, head-axis uniforms, and runtime face-lighting updates). A follow-up re-verified fresh imports on Linux (Blender 5.1.2, Godot 4.7.2 .NET).

Historical assertions recorded by those reviews, retained as context only:

- The generated glTFs of both models referenced the shared face lightmap PNG with case-differing URIs; the shared imported cache stored the lowercase spelling. Fresh Linux import succeeded with the committed model-prefixed PNG inputs; packaged export remained unverified at the time.
- Trigger's pantyhose mesh from the source import differed from the legacy scene's baked pantyhose by up to about 0.0919 per vertex; the legacy bake's provenance was unestablished, and no source compensation was made.
- The shared `head_turn` clip endpoint is 12°, where Zhu Yuan's legacy clip reached 20° — the deliberate trade for one body library shared by both models.
- Verification pass counts, file inventories, and per-test results from the original reports described those commits, not the current tree. Do not cite them as current facts.
