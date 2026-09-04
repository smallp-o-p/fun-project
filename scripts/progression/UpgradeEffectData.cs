using Godot;

namespace FunProject.Progression;

/// <summary>
/// An authored payload on a skill step. Pure data, interpreted by UnitProgression's
/// aggregation accessors — no runtime wrapper, because steps carry no mutable
/// per-instance state (that lives on UnitProgression).
/// </summary>
[GlobalClass]
public abstract partial class UpgradeEffectData : Resource;
