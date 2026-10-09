# Architecture map

Read the guide for the subsystem being changed; follow its links only as needed. Each contract has one home. Source and tests establish current behavior; update the owning guide when that behavior changes.

- [Battle runtime](battlescape-architecture.md): startup, state ownership, typed queries, visibility, combat, turn flow, and presentation.
- [Action execution](battle-action-executor.md): atomic steps, interrupts, failures, hooks, and event results.
- [Board and paths](battlescape-tile-system.md): coordinates, tile storage, exact-world versus faction-knowledge movement.
- [Map authoring](battle-map-authoring.md): native GridMaps, annotations, footprint baking, and export.
- [Campaign runtime](strategic-runtime.md): mission handoff/return, conditions, view navigation, engineering, research, and payment.
- [Items](equippable-item-architecture.md): authored/runtime split, capabilities, equipment proofs, and consumption.
- [Stats](stat-system.md): typed identity, folding, and contribution order.
- [Character models](model-controls.md): import-owned structure, animation, clothing, masks, and attachments.
- [Testing](testing.md): environment setup, verification, and coverage tools. Test design and audit rules live in the [test-audit skill](../.agents/skills/test-audit/SKILL.md).
