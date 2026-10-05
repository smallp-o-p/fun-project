# Stat System

Stats and stat modifiers for the project: authored resources with type-safe assignment, class-based runtime lookup, and composable modifiers.

## Core Model

Stats are concrete resource types, not an enum. `Stat` is the abstract base and stores only `BaseValue`; identity comes from the concrete subclass (`HealthStat`, `DamageStat`, `RangeStat`, `ActionPointsStat`, `AimStat`, … in `scripts/stats/concrete_stats/`). A field typed `DamageStat` accepts only a `DamageStat` resource, so a wrong assignment fails at authoring or compile time.

Stats are authored as concrete subresources: `WeaponData` carries `DamageStat`, `RangeStat`, `CriticalChanceStat`; `CombatantData` carries `HealthStat`, `ActionPointsStat`, `WillStat`, `MovementStat`, `AimStat`. `BaseArmorStat` is not authored on `CombatantData` — armor lives on items (`ArmorCapabilityData`), and `BattleUnitState.EquippedArmor` carries the per-battle proof.

## Runtime Access

Runtime wrappers such as `Weapon` and `Combatant` implement `HasStats` and store stats in a `StatSheet` keyed by the concrete stat class. The `StatSheet` constructor validates each key against its value's runtime type, so a mismatched entry throws at construction.

```csharp
var damage = weapon.GetStat<DamageStat>().BaseValue;
combatant.TryGetStat<AimStat>().IfSome(aim => GD.Print(aim.BaseValue));
```

## Stat Modifiers

Three layers:

- `StatModifier` — the atomic numeric operation: `Add`, `Multiply`, `CapMin`, `CapMax`, `PercentAdd`, `Override`. It knows nothing about stats.
- `StatMod` — a typed resource targeting one concrete stat class, holding an array of `StatModifier` operations. Concrete subclasses such as `RangeStatMod` bind the target stat at the type level.
- `EquippableMod` — the slim abstract base for item-slot mods (`Name`, `Description`, virtual `StatContributions`). `MultiStatMod` is the single stat-bearing subclass: it exports `Array<StatMod> StatMods`, and a single-target effect is a one-element list.

The old `EquippableStatMod`/`TypedEquippableStatMod` hierarchy and its `Apply(HasStats)`/`GetAppliedStat` API are removed. `resources/mods/long_barrel.tres` is a `MultiStatMod` example.

## Folding and Stacking

`HasStats.Fold(float baseValue, IEnumerable<StatModifier> modifiers)` is a static method on the `HasStats` interface. Resolution is deterministic:

```
effective = Override (last entry wins) ?? (base + ΣAdd) * (1 + ΣPercentAdd) * Π Multiply
```

`CapMin` and `CapMax` are final clamps applied after the formula: the highest `CapMin` wins, then the lowest `CapMax`. The result is rounded to 5 decimal places to suppress float noise. With multiple `Override` entries the last encountered value wins, so resolution over differing overrides depends on source order.

Stacking rule: `PercentAdd` entries are additive with each other (two +10% bonuses give +20%); `Multiply` is reserved for true compounding (two 1.1× multipliers give 1.21×).

`Resolve`/`TryResolve` are extension methods on `HasStats` (`HasStatsExtensions` in `scripts/stats/`):

- `owner.Resolve<TStat>(IEnumerable<StatMod> sources) : float` — gathers the `StatMod`s targeting `TStat`, collects their modifiers, and folds once over the owner's `BaseValue`. Returns `0f` when the owner lacks the stat.
- `owner.TryResolve<TStat>(...) : Option<float>` — same fold, `None` when the stat is absent.

Resolution is keyed by `Type`; there is no stat enum.

## Effective Stat Access

Higher-level types expose `EffectiveStat<TStat>()`, which gathers the right sources and delegates to `Resolve`.

- `Weapon.EffectiveStat<TStat>()` folds the equipped slot mods and the loaded ammunition's `Modifiers`. `Weapon.StatContributions` exposes the raw list as a property.
- `BattleUnitState.EffectiveStat<TStat>()` / `TryEffectiveStat<TStat>()` folds, in `GatherStatContributions` order:
  1. `Combatant.StatContributions()` — the combatant's own mod slots, faction bonuses, progression, and rank;
  2. deployment-time `UnitLoadout.StatMods` supplied at spawn;
  3. the equipped weapon's `StatContributions`;
  4. active-buff `StatMod`s (refreshed by `TurnStartBuffHook`/`UnitSpawnedBuffHook`).

  The order matters: the last `Override` wins, so sources fold in that sequence.

```csharp
int maxHp = Mathf.RoundToInt(unit.EffectiveStat<HealthStat>());
Option<float> aim = unit.TryEffectiveStat<AimStat>();
```

Combat reads go through these: hit chance uses `TryEffectiveStat<AimStat>()`, range checks use `weapon.EffectiveStat<RangeStat>()`, and max HP/AP/Vision round the effective value.

## Weapon Damage: A Separate Pipeline

Weapon damage modification does not use `HasStats.Fold` or stat mods. It flows through `DamageBundleMod`s (`DamageBundleEquippableMod` in weapon slots, `Ammunition.DamageMods` for ammo), applied sequentially at `Weapon.EmitDamage()` emission time. `DamageStat` is the read-only base-damage input to that pipeline; `DamageStatMod` and the old damage equippable mod are removed. Slot mods and ammo therefore shape damage packets at emission, not stat values.

## Deferred

- Timed buffs, stacking lifetime, and source tracking — no buff expiry or per-source tags yet.
- `ModSlot` category restrictions — slots accept any `EquippableMod` subtype.
- Dirty-flag caching — `EffectiveStat` computes on demand.
