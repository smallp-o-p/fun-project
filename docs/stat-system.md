# Stat System

This document describes the current stat and stat-modifier model used by the project.

## Design Goals

- Keep authored stat data easy to serialize as Godot `Resource` objects.
- Make stat assignment type-safe in both C# and the Godot inspector.
- Avoid enum-driven stat wiring where a stat's identity can drift from its declared purpose.
- Keep modifier behavior composable enough to support item mods, ammunition effects, and faction-level bonuses.

## Core Model

The stat system is built around concrete resource types rather than a shared `StatType` enum.

### Base Stat

- `Stat` is the abstract base resource.
- It currently stores only `BaseValue`.
- A stat's identity comes from its concrete subclass, not from a mutable field.

Examples:

- `HealthStat`
- `DamageStat`
- `RangeStat`
- `ActionPointsStat`
- `AimStat`

This means a field typed as `DamageStat` can only accept a `DamageStat` resource.

## Authoring Pattern

Stats are authored as concrete subresources inside higher-level data resources.

Examples:

- `WeaponData`
  - `DamageStat`
  - `RangeStat`
  - `CriticalChanceStat`
- `CombatantData`
  - `HealthStat`
  - `ActionPointsStat`
  - `WillStat`
  - `MovementStat`
  - `AimStat`

This keeps the serialized data small while giving the inspector strong type restrictions.

`BaseArmorStat` (armor value + `Element` for elemental matching) is no longer authored on `CombatantData`. Armor lives on items: `ArmorCapabilityData` holds the `BaseArmorStat` and regen config, and `BattleUnitState.EquippedArmor` carries the per-battle proof that a unit has armor equipped.

## Runtime Access

Runtime classes do not look up stats by enum. They expose class-based lookup through `HasStats`.

`HasStats` supports:

- `TryGetStat(Type statType, out Stat stat)`
- `TryGetStat<TStat>(out TStat stat)`
- `GetStat<TStat>()`

Typical usage:

```csharp
var damage = weapon.GetStat<DamageStat>().BaseValue;

if (combatant.TryGetStat<AimStat>(out var aim))
{
  GD.Print(aim.BaseValue);
}
```

Internally, runtime wrappers such as `Weapon` and `Combatant` store stats in a runtime dictionary keyed by the concrete stat class.

## Stat Modifiers

There are three layers in the modifier model.

### `StatModifier`

- `StatModifier` is the atomic numeric operation.
- It transforms a numeric value.
- Supported operations currently include:
  - add
  - multiply
  - minimum cap
  - maximum cap

This class does not know anything about which stat it is affecting.

### `StatMod`

- `StatMod` is a typed resource that targets one concrete stat class.
- It contains an array of `StatModifier` operations.
- It is the atomic stat-effect building block.
- Applying a `StatMod`:
  - looks up the target stat on a `HasStats` object
  - starts from that stat's `BaseValue`
  - applies each `StatModifier` in order
  - returns the resulting float

Concrete subclasses such as `RangeStatMod` and `AimStatMod` bind the target stat at the type level.

### `EquippableMod`

- `EquippableMod` is the shared base for item-slot mods.
- It adds:
  - `Name`
  - `Description`
  - a common `Apply(HasStats)` contract that returns one resolved value per affected stat type
- It also provides helper accessors such as:
  - `TryGetAppliedStat<TStat>()`
  - `GetAppliedStat<TStat>()`

This gives weapon mod slots one runtime interface regardless of whether a mod affects one stat or several.

## Equippable Mods

There are currently two equippable mod shapes.

### `EquippableStatMod`

- `EquippableStatMod` is the single-target equippable form.
- It inherits from `EquippableMod`.
- It still targets one concrete stat type.
- It exposes `ApplyToTarget()` for direct single-stat use.

Concrete equippable modifier resources such as `RangeEquippableStatMod` and `AimEquippableStatMod` are what authored item mods use.

### `MultiStatMod`

- `MultiStatMod` also inherits from `EquippableMod`.
- It exports `Array<StatMod>`.
- It applies each contained `StatMod` and returns a result map keyed by concrete stat class.
- It rejects duplicate entries for the same target stat type when applied.

This is the current way to author one slot-mounted mod that affects multiple stats.

Example shape:

```csharp
var mod = new MultiStatMod
{
  Name = "Tactical Overhaul",
  StatMods =
  [
    new CriticalChanceStatMod { Modifiers = [StatModifier.Add(2)] },
    new RangeStatMod { Modifiers = [StatModifier.Add(6)] },
  ]
};
```

Examples in `resources/mods/`:

- `long_barrel.tres` — a single-target `EquippableStatMod` that increases `RangeStat`.

`dragons_breath.tres` and `suppressor_coil.tres` are now `DamageBundleEquippableMod` resources that belong to the weapon damage pipeline, not the stat-mod system. They shape damage packets at emission time rather than modifying a `DamageStat` value.

Weapon slots support either single-target (`EquippableStatMod`) or multi-target (`MultiStatMod`) equippable mods, as well as `DamageBundleEquippableMod` for damage-pipeline mods.

## Composition Already In Use

The current model supports multi-effect composition in more than one place.

Examples:

- `AmmunitionData` exports `Array<StatMod>`
- `FactionData` exports `Array<StatMod>`
- `MultiStatMod` exports `Array<StatMod>`

That means ammunition, faction bonuses, and slot-mounted equippable mods can all contain several independently typed stat effects in one resource, even though each individual `StatMod` remains single-target.

## Mod Slots

`ModSlot` now stores `EquippableMod`, not `EquippableStatMod`.

That means a slot can equip:

- a single-target mod such as `RangeEquippableStatMod`
- a multi-target mod such as `MultiStatMod`
- a damage-pipeline mod such as `DamageBundleEquippableMod`

Typical runtime usage:

```csharp
var slot = weapon.GetModSlots()[0];

float range = slot.EquippedMod!.GetAppliedStat<RangeStat>(weapon);

if (slot.EquippedMod.TryGetAppliedStat<AimStat>(weapon, out var aim))
{
  GD.Print(aim);
}
```

## Why This Design Exists

The main reason for this model is to remove an easy class of authoring mistakes.

With the older enum-based approach, it was possible to assign a generic `Stat` resource to the wrong field and only discover the mismatch later.

With the current approach:

- `DamageStat` fields only accept `DamageStat`
- `HealthStat` fields only accept `HealthStat`
- modifier resources encode their target stat in their own type

This shifts more mistakes from runtime to authoring time.

## Current Limitation

`StatMod` is intentionally single-target.

That keeps:

- the API simple
- each modifier resource easy to reason about
- serialization straightforward

Multi-stat behavior exists one level up through `MultiStatMod`, not by weakening `StatMod` itself.

The main remaining limitation is that higher-level gameplay systems still need to decide how and when to consume these resolved stat maps. The stat system now supports multi-stat slot mods, but it does not yet define a broader effect pipeline for non-numeric or battle-event-driven mod behavior.

Weapon damage modification is a deliberate exception to the stat-mod pipeline: it flows entirely through `DamageBundleMod`s (`DamageBundleEquippableMod` in weapon mod slots, `Ammunition.DamageMods` for ammo effects) and is applied at `Weapon.EmitDamage()` emission time. `DamageStat` still exists as the read-only base-damage input to that pipeline. `DamageStatMod` and `DamageEquippableStatMod` were removed; see `docs/superpowers/specs/2026-06-10-weapon-damage-pipeline-design.md` for the full design.

## Summary

- Stats are identified by concrete class, not enum.
- Authoring is done with typed Godot resources.
- Runtime lookup is generic and class-based.
- Numeric operations are separated from stat targeting.
- `StatMod` remains the single-target atomic building block.
- `EquippableMod` is the common slot-mounted mod interface.
- `MultiStatMod` composes several typed `StatMod` resources into one equippable mod.
- Multi-effect behavior is composed from multiple `StatMod` resources rather than weakening the type model.
