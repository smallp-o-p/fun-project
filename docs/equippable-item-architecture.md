# Equippable Item Architecture

This document describes the target architecture for authored equippable items and their runtime counterparts.

## Design Goals

- Follow the same pattern used elsewhere in the project: serializable `Resource` data for authored content and runtime objects for mutable state.
- Support weapons, grenades, armor, and utility items under one consistent item model.
- Keep battle behavior serializable as data wherever possible.
- Allow complex items such as grenades, smoke canisters, mines, medkits, and deployables without hardcoding each item as a special-case class.

## Item Hierarchy

Use the same split everywhere: serializable `Resource` data for authoring and lightweight runtime wrappers for mutable use.

### Data Layer

```text
NamedEntityData
\-- EquippableItemData
    +-- WeaponData
    |   \-- FirearmWeaponData
    +-- ThrowableItemData
    |   \-- GrenadeData
    +-- ArmorItemData
    \-- UtilityItemData
```

### Runtime Layer

```text
EquippableItem
+-- Weapon
|   \-- FirearmWeapon
+-- ThrowableItem
|   \-- Grenade
+-- ArmorItem
\-- UtilityItem
```

## Responsibilities

- `EquippableItemData` defines authored, serializable item data such as:
  - name and description
  - icon or presentation metadata
  - inventory or slot constraints
  - weight or economy metadata
  - authored effect payloads
- `EquippableItem` holds mutable runtime state such as:
  - current charges
  - current ammo
  - active mods
  - battle- or mission-specific state
- `WeaponData` and `Weapon` remain specific to attack-capable gear.
- `ThrowableItemData` and `ThrowableItem` cover thrown battle-usable gear without forcing every throwable to look like a firearm.
- `ArmorItemData` and `UtilityItemData` allow passive or activated equipment to fit the same authoring pattern.

## Grenade And Throwable Serialization

Grenades and similar consumables should be composed from serializable effect resources rather than bespoke hardcoded booleans or embedded behavior code.

### Recommended Shape

- `GrenadeData`
  - throw range
  - blast radius or area shape
  - fuse or detonation mode
  - friendly-fire policy if needed
  - array of `BattleEffectData`
- `BattleEffectData`
  - abstract base for authored effect payloads
- concrete effect resources, for example:
  - `DamageEffectData`
  - `StatusEffectData`
  - `TerrainEffectData`
  - `VisibilityEffectData`
  - `SpawnHazardEffectData`

### Serialization Principle

- Serialize effect descriptors as data.
- Interpret them through the tactical battle-effect pipeline at runtime.
- Do not serialize grenade behavior as delegates or ad hoc embedded scripts by default.

This keeps authored items flexible while preserving deterministic combat logic and makes grenades, smoke canisters, mines, medkits, deployables, and other equippables fit the same runtime pipeline.

## Integration Notes

- Weapon mods and grenade payloads should both flow through battle-effect resources instead of separate one-off systems.
- Inventory or equipment code should depend on `EquippableItemData` and `EquippableItem`, not directly on `WeaponData` and `Weapon`.
- Tactical execution should consume runtime items and effect descriptors, not raw authored resources alone.
