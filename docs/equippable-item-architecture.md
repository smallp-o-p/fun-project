# Equippable Item Architecture

Items are composed from capabilities, not subclassed. One authored
`EquippableItemData` resource plus an array of capability resources defines any
item; no new C# classes are needed to create a new kind of item.
Design spec: `docs/superpowers/specs/2026-06-09-item-composition-design.md`.

## Design Goals

- Same Data/Runtime split as the rest of the project, applied per capability:
  authored `Resource` data is immutable; runtime objects own mutable state.
- A new item kind (throwable scanner beacon, multi-use medkit) is data-only:
  compose capabilities in the inspector.
- Battle actions that require a capability take a typed proof, making
  "throw a non-throwable item" a compile error rather than a runtime check.

## Shape

### Data layer (authored, `[GlobalClass]` resources)

- `EquippableItemData : NamedEntityData` — name/description plus
  `Capabilities : Array<ItemCapabilityData>`.
- `ItemCapabilityData : Resource` (abstract) — `CreateRuntime()` factory.
- Concrete capabilities (in `scripts/items/capabilities/`):
  - `ThrowableCapabilityData` — throw range, action point cost, consumes-on-use
  - `BlastCapabilityData` — blast radius, `Array<BattleEffectData>` payload
  - `ChargesCapabilityData` — max charges
  - `ModSlotsCapabilityData` — slot count
  - `ArmorCapabilityData` — armor value via `BaseArmorStat` (which carries the `Element` for elemental matching), plus regen config (`RegenDelayTurns`, `RegenPerTurn`)
- Weapons still subclass: `WeaponData → AmmunitionedWeaponData →
  FirearmWeaponData` extend `EquippableItemData` and inherit the capability
  array (their stats may become capabilities in a later pass).

### Runtime layer (plain C#)

- `EquippableItem` — identity + capability list built via `CreateRuntime()`.
  Duplicate capability types throw `InvalidOperationException` at construction
  (one-per-type invariant).
- Runtime capabilities mirror the data and OWN their state: `ChargesCapability`
  (current charges), `ModSlotsCapability` (the `ModSlot` instances),
  `BlastCapability` (defensively copies the effects list into an
  `IReadOnlyList`), `ThrowableCapability` (clamped numeric values),
  `ArmorCapability` (`Max`, `Current`, `Element`, `RegenDelayRemaining`; methods
  `Reduce`, `RearmRegenDelay`, `TickRegen`). `BattleUnitState.EquippedArmor`
  holds the capability proof as `Option<ItemWith<ArmorCapability>>`, threaded
  through `SpawnUnit`/`AddUnit`.
- Lookup: `item.FindCapability<TCap>()` returns `Option<TCap>` (linear scan —
  capability counts are single-digit; do not add a Type-keyed dictionary
  without profiling evidence).
- `HasModSlots` is implemented by delegation: `GetModSlots()` returns the
  `ModSlotsCapability` slots, or an empty array when the capability is absent.

## Capability proofs ("parse, don't validate")

`item.With<TCap>()` returns `Option<ItemWith<TCap>>` — a proof binding the item
to its capability. Actions that require a capability take the proof type:
`ThrowItem` takes `ItemWith<ThrowableCapability>` and `UseItem` takes
`ItemWith<ChargesCapability>`, so items without the capability are rejected by
the compiler, not at runtime. Proofs cannot go stale because capability sets
are fixed at item construction. This is the same pattern as
`BattleBoardState.ValidatedPoint` and `AliveUnit`.

`Execute` re-checks only the state-dependent facts that can change between
construction and commit (phase, active side, occupancy, attack feasibility
via `AttackContext.Resolve`, route legality); possession and throw range are
the caller's job, same as the rest of the trusted-parameters model.

## Consumption semantics

Consumption is parsed once, at action construction, into a `Consumable` receipt
(`scripts/battle/Consumable.cs`): `Consumable.From(ItemWith<ThrowableCapability>)`
returns `None` for a non-consuming throwable, and `Consumable.From(ItemWith<ChargesCapability>)`
always consumes. `SpendOnce(owner)` is the single home for the consumption rules:

- With a `ChargesCapability`: spend one charge per use; remove from inventory
  when depleted (a charge that cannot be spent is a broken invariant and throws).
- Without: implicitly single-use — removed from inventory after one use.

`UseItem` is the generic active-item verb: one charge per use at
`BattleSession.DefaultUseItemActionPointCost`, raising `ItemUsedBattleEvent`;
effect payloads belong to hooks reacting to that event (the same split as
`ItemThrownBattleEvent` → `CapabilityEffectSystem`).

## Adding a capability

1. Add `FooCapabilityData` (`[GlobalClass]`, file name = class name, defaults
   in exports) and `FooCapability` (runtime state) under
   `scripts/items/capabilities/`.
2. Wire `CreateRuntime() => new FooCapability(this)`.
3. Build before expecting it in the inspector picker (Godot registers global
   classes from compiled assemblies; restart the editor if it doesn't appear).
4. Keep numeric tuning in the stat system — a capability wrapping a single
   stat is the wrong granularity. Capabilities earn their place by cutting
   across item kinds or carrying designer-tuned data groups.

## Known constraints

- Godot cannot export interfaces; capability-as-Resource is the supported
  composition mechanism for designer-authored data.
- Nested capability sub-resources are shared by reference if copy-pasted in
  the inspector; immutable data + runtime wrappers make this safe, but save
  shared capability presets as standalone `.tres` files when reuse is intended.
- Export builds with .NET trimming can strip capability classes that are only
  referenced from `.tres` files. No export pipeline exists yet; when one does,
  add trimmer root descriptors (or disable trimming) for the game assembly.
