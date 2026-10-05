# Equippable Item Architecture

Define item kinds with an `EquippableItemData` resource and its capability resources. Each capability supplies a group of authored data and runtime behavior.

## Design Goals

- The project's Data/Runtime split, applied per capability: authored `Resource` data is immutable; runtime objects own mutable state.
- A new item kind (throwable scanner beacon, multi-use medkit) is data-only: compose capabilities in the inspector.
- Battle actions take typed proofs of their required capabilities.

## Shape

### Data layer (authored, `[GlobalClass]` resources)

- `EquippableItemData : NamedEntityData` — name/description plus `Capabilities : Array<ItemCapabilityData>`.
- `ItemCapabilityData : Resource` (abstract) — `CreateRuntime()` factory.
- Concrete capabilities (in `scripts/items/capabilities/`):
  - `ThrowableCapabilityData` — throw range, action-point cost, consumes-on-use
  - `BlastCapabilityData` — blast radius, `Array<BattleEffectData>` payload
  - `ChargesCapabilityData` — max charges
  - `ModSlotsCapabilityData` — slot count
  - `ArmorCapabilityData` — armor value via `BaseArmorStat` (which carries the `Element` for elemental matching), plus regen config (`RegenDelayTurns`, `RegenPerTurn`)
- Weapons still subclass: `WeaponData → AmmunitionedWeaponData → FirearmWeaponData` extend `EquippableItemData` and inherit the capability array.

### Runtime layer (plain C#)

- `EquippableItem` — identity plus capability list built via `CreateRuntime()`. Duplicate capability types throw `InvalidOperationException` at construction (one-per-type invariant).
- Runtime capabilities mirror the data and own their state: `ChargesCapability` (current charges), `ModSlotsCapability` (the `ModSlot` instances), `BlastCapability` (copies the effects list into an `IReadOnlyList`), `ThrowableCapability` (clamped values), `ArmorCapability` (`Max`, `Current`, `Element`, `RegenDelayRemaining`; methods `Reduce`, `RearmRegenDelay`, `TickRegen`).
- Lookup: `item.FindCapability<TCap>()` returns `Option<TCap>` (linear scan — capability counts are single-digit; add a dictionary only with profiling evidence).
- `HasModSlots` is implemented by delegation: `GetModSlots()` returns the `ModSlotsCapability` slots, or an empty array when absent.

## Capability proofs

`item.With<TCap>()` returns `Option<ItemWith<TCap>>`, binding the item to its capability. `ThrowItem` requires `ItemWith<ThrowableCapability>`; `UseItem` requires `ItemWith<ChargesCapability>`. These parameter types enforce capability requirements at compile time. Capability membership stays fixed for the item's lifetime, so the proof remains valid.

`Execute` re-checks state-dependent facts that can change between construction and commit: actor actions interrupt on lost liveness or incapacity before spending AP, ammunition, charges, or items; item possession, remaining charges, and equipment are also re-checked. Capability proofs establish item shape; throw range remains the caller's job.

## Consumption semantics

Consumption is parsed once, at action construction, into a `Consumable` receipt (`scripts/battle/Consumable.cs`): `Consumable.From(ItemWith<ThrowableCapability>)` returns `None` for a non-consuming throwable; `Consumable.From(ItemWith<ChargesCapability>)` always consumes. `SpendOnce(owner)` is the single home for the rules:

- With a `ChargesCapability`: spend one charge per use; remove from inventory when depleted. A failed charge spend is a broken invariant and throws.
- A consumable item lacking `ChargesCapability` leaves the inventory after one use.

`UseItem` is the generic active-item verb: one charge per use at `BattleSession.DefaultUseItemActionPointCost`, raising `ItemUsedBattleEvent`; effect payloads belong to hooks reacting to that event (the same split as `ItemThrownBattleEvent` → `CapabilityEffectSystem`).

## Health and stun damage packets

`DamagePacketData.Kind` and runtime `Damage.Kind` default to `DamageKind.Health`. The kind travels with amount, element, and optional status through packet derivation and damage modifiers; mixed health/stun bundles are supported.

`DamageResolver` processes packets in bundle order. Health packets consume remaining armor (1.5× floored armor loss on an element match); health spill always uses the un-multiplied amount. Stun packets increase only `BattleUnitState.CurrentStun`, preserving health, armor, and its regen delay. Non-positive packets resolve to zero. `DamageResolution` and nonlethal `UnitDamagedBattleEvent` carry separate `ArmorDamage`, `HealthDamage`, and `StunDamage` totals. A status requiring health damage checks its own packet's health spill.

Stun starts at zero. A living unit becomes unconscious when stun is at least its current health; zero health means death. Unconscious bodies keep their inventory, tile occupancy, and targetability. Acting and contributing vision require a living conscious unit. Conscious units recover up to 5 stun at their owner's turn end, after status effects and armor regen; `StunRecoverySystem` owns the fixed unsigned recovery rate.

## Adding a capability

1. Add `FooCapabilityData` (`[GlobalClass]`, file name = class name, defaults in exports) and `FooCapability` (runtime state) under `scripts/items/capabilities/`.
2. Wire `CreateRuntime() => new FooCapability(this)`.
3. Build to register the global class from the compiled assembly. Restart the editor if the Inspector picker remains stale.
4. Keep numeric tuning in the stat system. Use capabilities for behavior shared across item kinds or for related authored data.

## Known constraints

- Use capability Resources for Inspector-authored composition.
- Nested capability sub-resources are shared by reference if copy-pasted in the inspector. Immutable data plus runtime wrappers make this safe; save shared capability presets as standalone `.tres` files when reuse is intended.
- Export builds with .NET trimming can strip capability classes referenced only from `.tres` files. When adding an export pipeline, preserve those classes with trimmer root descriptors or disable trimming for the game assembly.
