namespace FunProject.Research;

/// <summary>Deterministic research start failures; resolution checks them in declaration order.</summary>
public enum ResearchStartFailure
{
  UnknownProject,
  Busy,
  AlreadyCompleted,
  Locked,
}
