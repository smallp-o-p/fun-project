# Equippable Item Architecture

Items are composed from capabilities, not subclassed. One authored `EquippableItemData` resource plus an array of capability resources defines any item; no new C# classes are needed to create a new kind of item.

## Design Goals

- The project's Data/Runtime split, applied per capability: authored `Resource` data is immutable; runtime objects own mutable state.
- A new item kind (throwable scanner beacon, multi-use medkit) is data-only: compose capabilities in the inspector.
- Battle actions that require a capability take a typed proof, so "throw a non-throwable item" is a compile error, not a runtime check.

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

## Capability proofs ("parse, don't validate")

`item.With<TCap>()` returns `Option<ItemWith<TCap>>` — a proof binding the item to its capability. Actions that require a capability take the proof type: `ThrowItem` takes `ItemWith<ThrowableCapability>` and `UseItem` takes `ItemWith<ChargesCapability>`, so an item without the capability is rejected by the compiler. Proofs cannot go stale because capability sets are fixed at item construction. This is the same pattern as `BattleBoardState.ValidatedPoint` and `AliveUnit`.

`Execute` re-checks state-dependent facts that can change between construction and commit: actor actions interrupt on lost liveness or incapacity before spending AP, ammunition, charges, or items; item possession, remaining charges, and equipment are also re-checked. Capability proofs establish item shape; throw range remains the caller's job.

## Consumption semantics

Consumption is parsed once, at action construction, into a `Consumable` receipt (`scripts/battle/Consumable.cs`): `Consumable.From(ItemWith<ThrowableCapability>)` returns `None` for a non-consuming throwable; `Consumable.From(ItemWith<ChargesCapability>)` always consumes. `SpendOnce(owner)` is the single home for the rules:

- With a `ChargesCapability`: spend one charge per use; remove from inventory when depleted. A charge that cannot be spent is a broken invariant and throws.
- Without: implicitly single-use — removed from inventory after one use.

`UseItem` is the generic active-item verb: one charge per use at `BattleSession.DefaultUseItemActionPointCost`, raising `ItemUsedBattleEvent`; effect payloads belong to hooks reacting to that event (the same split as `ItemThrownBattleEvent` → `CapabilityEffectSystem`).

## Health and stun damage packets

`DamagePacketData.Kind` and runtime `Damage.Kind` default to `DamageKind.Health`. The kind travels with amount, element, and optional status through packet derivation and damage modifiers; mixed health/stun bundles are supported.

`DamageResolver` processes packets in bundle order. Health packets consume remaining armor (1.5× floored armor loss on an element match); health spill always uses the un-multiplied amount. Stun packets bypass armor completely, leave it for later packets, and increase only `BattleUnitState.CurrentStun`; they neither reduce health nor re-arm armor regeneration. Non-positive packets resolve to zero. `DamageResolution` and nonlethal `UnitDamagedBattleEvent` carry separate `ArmorDamage`, `HealthDamage`, and `StunDamage` totals. A status requiring health damage checks its own packet's spill; stun cannot borrow health damage from another packet.

Stun starts at zero. A living unit becomes unconscious when stun is at least its current health; zero health means death. Unconscious bodies keep their inventory, tile occupancy, and targetability, but cannot act or contribute vision. Conscious units recover up to 5 stun at their owner's turn end, after status effects and armor regen; `StunRecoverySystem` is a default executor system with a fixed unsigned rate, and no setup surface configures it.

## Adding a capability

1. Add `FooCapabilityData` (`[GlobalClass]`, file name = class name, defaults in exports) and `FooCapability` (runtime state) under `scripts/items/capabilities/`.
2. Wire `CreateRuntime() => new FooCapability(this)`.
3. Build before expecting it in the inspector picker (Godot registers global classes from compiled assemblies; restart the editor if it does not appear).
4. Keep numeric tuning in the stat system — a capability wrapping a single stat is the wrong granularity. Capabilities earn their place by cutting across item kinds or carrying designer-tuned data groups.

## Known constraints

- Godot cannot export interfaces; capability-as-Resource is the supported composition mechanism for designer-authored data.
- Nested capability sub-resources are shared by reference if copy-pasted in the inspector. Immutable data plus runtime wrappers make this safe; save shared capability presets as standalone `.tres` files when reuse is intended.
- Export builds with .NET trimming can strip capability classes referenced only from `.tres` files. No export pipeline exists yet; when one does, add trimmer root descriptors (or disable trimming) for the game assembly.
