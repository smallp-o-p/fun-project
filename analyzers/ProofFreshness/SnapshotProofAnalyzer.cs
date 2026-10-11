using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace FunProject.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SnapshotProofAnalyzer : DiagnosticAnalyzer
{
  internal static readonly DiagnosticDescriptor Stale = new("FPF001", "Snapshot proof may be stale",
    "'{0}' may carry a snapshot proof invalidated by a mutation or deferred execution; reacquire it at use", "Correctness", DiagnosticSeverity.Error, true);
  internal static readonly DiagnosticDescriptor Effect = new("FPF002", "Preserving proof contract is violated",
    "'{0}' promises to preserve snapshot proofs but may invalidate them through {1}", "Correctness", DiagnosticSeverity.Error, true);
  public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Stale, Effect];
  public override void Initialize(AnalysisContext context)
  {
    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    context.EnableConcurrentExecution();
    context.RegisterOperationBlockAction(block =>
    {
      if (block.OwningSymbol is not IMethodSymbol method) return;
      var effects = new ProofEffects(block.Compilation);
      foreach (var operation in block.OperationBlocks)
      {
        if (ProofEffects.PreservingContract(method) && effects.BodyInvalidates(operation))
          block.ReportDiagnostic(Diagnostic.Create(Effect, method.Locations[0], method.Name, effects.Explain(operation)));
        var root = operation;
        while (root.Parent != null) root = root.Parent;
        ControlFlowGraph? graph = root switch
        {
          IMethodBodyOperation body => ControlFlowGraph.Create(body, block.CancellationToken),
          IConstructorBodyOperation body => ControlFlowGraph.Create(body, block.CancellationToken),
          IBlockOperation body => ControlFlowGraph.Create(body, block.CancellationToken),
          _ => null,
        };
        if (graph != null) new ProofFlow(effects, block.ReportDiagnostic, block.CancellationToken).Analyze(graph);
      }
    });
  }
}
