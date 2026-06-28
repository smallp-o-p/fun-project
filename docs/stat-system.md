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

- `Option<TStat> TryGetStat<TStat>()`
- `GetStat<TStat>()`

Typical usage:

```csharp
var damage = weapon.GetStat<DamageStat>().BaseValue;

combatant.TryGetStat<AimStat>().IfSome(aim => GD.Print(aim.BaseValue));
```

Internally, runtime wrappers such as `Weapon` and `Combatant` store stats in a `StatSheet` keyed by the concrete stat class. The `StatSheet` constructor validates that each key matches its value's runtime type, so a mismatched entry throws at construction rather than at read time.

## Stat Modifiers

There are three layers in the modifier model.

### `StatModifier`

- `StatModifier` is the atomic numeric operation — a pure `{Operation, Value}` data holder.
- Supported operations:
  - `Add` — adds a flat amount
  - `Multiply` — multiplies the running total (true compounding; reserve for this intent)
  - `CapMin` — sets a lower bound on the final value
  - `CapMax` — sets an upper bound on the final value
  - `PercentAdd` — contributes an additive percentage bonus (see stacking rule below)
  - `Override` — replaces all other ops and sets the value directly

This class does not know anything about which stat it is affecting.

### `StatMod`

- `StatMod` is a typed resource that targets one concrete stat class.
- It contains an array of `StatModifier` operations.
- It is the atomic stat-effect building block.
- Concrete subclasses such as `RangeStatMod` and `AimStatMod` bind the target stat at the type level.

### `EquippableMod`

- `EquippableMod` is the slim abstract base for item-slot mods.
- It carries `Name`, `Description`, and a virtual `IEnumerable<StatMod> StatContributions` property (defaults to empty).
- Subtypes override `StatContributions` to expose their stat effects.

## Equippable Mods

`MultiStatMod` is the single stat-bearing equippable mod class. The old `EquippableStatMod` / `TypedEquippableStatMod` hierarchy and all per-stat `*EquippableStatMod` classes have been removed, along with the `Apply(HasStats)` / `GetAppliedStat` / `TryGetAppliedStat` API they provided.

### `MultiStatMod`

- `MultiStatMod` inherits from `EquippableMod`.
- It exports `Array<StatMod> StatMods`.
- A single-target effect is just a one-element list.
- It overrides `StatContributions` to return `StatMods`.

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

Weapon slots accept `MultiStatMod` (stat pipeline) or `DamageBundleEquippableMod` (damage pipeline). There is no longer a separate single-target `EquippableStatMod` class.

Examples in `resources/mods/`:

- `long_barrel.tres` — a `MultiStatMod` that increases `RangeStat`.

`dragons_breath.tres` and `suppressor_coil.tres` are `DamageBundleEquippableMod` resources that belong to the weapon damage pipeline, not the stat-mod system. They shape damage packets at emission time rather than modifying a `DamageStat` value.

## Stacking Rule and Resolution

### `HasStats.Fold`

All stat modifiers are folded by `HasStats.Fold(float baseValue, IEnumerable<StatModifier> modifiers)` — a `static` method on the `HasStats` interface (pure arithmetic, independent of any owner). Resolution is deterministic and source-order-independent:

```
effective = Override ?? (base + ΣAdd) * (1 + ΣPercentAdd) * Π Multiply
```

`CapMin` and `CapMax` are applied as a **final clamp** after the formula above. If multiple `CapMin` entries exist, the highest wins; if multiple `CapMax` entries exist, the lowest wins. The result is rounded to 5 decimal places to suppress float noise.

Stacking rule: `PercentAdd` entries are **additive** with each other (stack as a sum before multiplying). `Multiply` is reserved for true compounding. If you want "two +10 % bonuses give +20 %", use `PercentAdd(0.1f)` twice. If you want "two 1.1× multipliers give 1.21×", use `Multiply(1.1f)` twice.

### `HasStats.Resolve` / `HasStats.TryResolve`

`HasStats` exposes resolution as **default interface methods** on the stat owner itself:

- `Resolve<TStat>(IEnumerable<StatMod> sources) : float` — gathers all `StatMod`s whose target type is `TStat`, collects their `StatModifier`s, and folds once over the owner's `BaseValue`. Returns `0f` if the owner has no `TStat`.
- `TryResolve<TStat>(IEnumerable<StatMod> sources) : Option<float>` — same fold, but returns `None` if the owner has no `TStat` (useful for stats that are optional on a given entity).

(Default interface methods are only visible through the interface, so internal callers use `((HasStats)owner).Resolve<TStat>(...)`; the `EffectiveStat` wrappers hide that cast.)

Resolution is keyed by `Type`; there is no stat enum.

## Effective Stat Access

Higher-level types expose a single `EffectiveStat<TStat>()` call that gathers the right sources and delegates to the owner's `Resolve`.

### `Weapon.EffectiveStat<TStat>()`

Resolves weapon-owned stats (Range, CriticalChance, Ammunition, etc.) by folding the contributions from all equipped slot mods and the loaded ammunition's `Modifiers`:

```csharp
float range = weapon.EffectiveStat<RangeStat>();
```

`Weapon.StatContributions()` (the public method that collects slot-mod and ammo `StatMod`s) is also available if you need the raw list.

### `BattleUnitState.EffectiveStat<TStat>()` and `TryEffectiveStat<TStat>()`

Resolves unit-owned stats (Aim, Health, ActionPoints, Vision, Movement, Will, etc.) by folding:

1. Stat contributions from the combatant's own mod slots.
2. The owning faction's `StatBonuses` (`FactionData.FactionBonuses`, surfaced as `Faction.StatBonuses`).
3. Stat contributions from the equipped weapon's slots (so a scope targeting `AimStat` buffs the wielder).

```csharp
int maxHp = Mathf.RoundToInt(unit.EffectiveStat<HealthStat>());

// Optional — returns None if the unit has no AimStat
Option<float> aim = unit.TryEffectiveStat<AimStat>();
```

Combat reads through these: hit-chance uses `TryEffectiveStat<AimStat>()`, range checks use `weapon.EffectiveStat<RangeStat>()`, and max HP/AP/Vision properties call `Mathf.RoundToInt(EffectiveStat<...>())`.

## Composition Now Fully Live

The following sources are all active participants in stat resolution:

- Weapon mod slots (`MultiStatMod` resources equipped on `ModSlot`s)
- `Ammunition.Modifiers` — per-ammo-type stat adjustments
- `FactionData.FactionBonuses` / `Faction.StatBonuses` — faction-wide stat bonuses

`StatMod` is still single-target, but a single `MultiStatMod` can carry multiple `StatMod` entries, each targeting a different stat.

## Mod Slots

`ModSlot` stores `EquippableMod`. A slot can equip:

- a stat-pipeline mod: `MultiStatMod`
- a damage-pipeline mod: `DamageBundleEquippableMod`

Effective stat access (above) replaces the old per-slot `GetAppliedStat` / `TryGetAppliedStat` pattern.

## Weapon Damage: A Separate Pipeline

Weapon damage modification does **not** go through `HasStats.Fold` or the stat-mod system. It flows entirely through `DamageBundleMod`s (`DamageBundleEquippableMod` in weapon mod slots, `Ammunition.DamageMods` for ammo effects) applied sequentially at `Weapon.EmitDamage()` emission time. `DamageStat` exists as the read-only base-damage input to that pipeline. `DamageStatMod` and the old `DamageEquippableStatMod` were removed. See `docs/superpowers/specs/2026-06-10-weapon-damage-pipeline-design.md` for the full design.

## Why This Design Exists

The main reason for this model is to remove an easy class of authoring mistakes.

With the older enum-based approach, it was possible to assign a generic `Stat` resource to the wrong field and only discover the mismatch at runtime.

With the current approach:

- `DamageStat` fields only accept `DamageStat`
- `HealthStat` fields only accept `HealthStat`
- modifier resources encode their target stat in their own type
- `StatSheet` validates key/value consistency at construction time

This shifts more mistakes from runtime to authoring time.

## Deferred Items

The following are explicitly out of scope for the current implementation:

- Timed buffs, stacking lifetime, and source tracking — there is no buff expiry or per-source tag system yet.
- `ModSlot` slot-types and slot labels — slots accept any `EquippableMod` subtype; category restrictions are not yet enforced.
- Dirty-flag caching — `EffectiveStat` resolves compute-on-demand; there is no caching layer.

## Summary

- Stats are identified by concrete class, not enum.
- Authoring is done with typed Godot resources.
- Runtime lookup is generic and class-based through `HasStats`.
- `StatModifier` (`Add`, `Multiply`, `CapMin`, `CapMax`, `PercentAdd`, `Override`) is the atomic numeric op — a pure data holder.
- `HasStats.Fold` (a `static` interface method) combines modifiers deterministically: `Override ?? (base + ΣAdd) * (1 + ΣPercentAdd) * Π Multiply`, then clamp. Percents are additive; `Multiply` is true compounding.
- `HasStats.Resolve<TStat>` / `TryResolve<TStat>` (default interface methods) gather matching `StatMod`s and fold over the owner's base value.
- `MultiStatMod` is the sole stat-bearing equippable mod. Single-target effects use a one-element `StatMods` list.
- Effective stats are read via `Weapon.EffectiveStat<TStat>()` (weapon stats) and `BattleUnitState.EffectiveStat<TStat>()` / `TryEffectiveStat<TStat>()` (unit stats, gathering combatant slots + faction bonuses + equipped-weapon slots).
- Weapon damage modification is a deliberate exception routed through `DamageBundleMod`, not this system.
