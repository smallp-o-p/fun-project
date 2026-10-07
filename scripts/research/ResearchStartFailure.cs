namespace FunProject.Research;

/// <summary>Deterministic research start failures; resolution checks them in declaration order.</summary>
public enum ResearchStartFailure
{
  UnknownProject,
  /// <summary>The requested project is already active; its progress is preserved.</summary>
  Busy,
  AlreadyCompleted,
  Locked,
}
