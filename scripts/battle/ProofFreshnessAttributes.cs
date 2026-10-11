using System;

namespace FunProject.Battle;

/// <summary>A receipt of mutable battle facts, reusable until a battle mutation.</summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class SnapshotProofAttribute : Attribute;

/// <summary>A synchronous boundary that can invalidate every outstanding battle snapshot.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
public sealed class InvalidatesProofsAttribute : Attribute;

/// <summary>A checked synchronous contract: this method and its callees preserve battle snapshots.
/// Other state, such as ammunition or query caches, may still change.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
public sealed class PreservesProofsAttribute : Attribute;
