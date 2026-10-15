using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace FunProject.Analyzers;

// Fresh/Stale is a may-analysis: joins retain any stale path, and loops converge
// before diagnostics are emitted. Copies preserve provenance; mutations kill every copy.
internal sealed class ProofFlow(ProofEffects effects, Action<Diagnostic> report, CancellationToken cancellation,
  HashSet<IMethodSymbol>? activeLocals = null)
{
  private readonly HashSet<IMethodSymbol> _activeLocals = activeLocals ?? new(SymbolEqualityComparer.Default);
  private const int Fresh = 1;
  private const int Stale = 2;
  private readonly HashSet<(SyntaxTree, int)> _reported = [];
  private ControlFlowGraph _graph = null!;
  private bool _emit;
  private readonly Dictionary<CaptureId, IFlowAnonymousFunctionOperation> _callbacks = [];

  internal void Analyze(ControlFlowGraph graph, Dictionary<object, int>? initial = null)
  {
    _graph = graph;
    var inputs = new Dictionary<object, int>?[graph.Blocks.Length];
    inputs[0] = initial != null ? new(initial) : [];
    foreach (var block in graph.Blocks)
    {
      foreach (var operation in block.Operations) Seed(operation, inputs[0]!);
      if (block.BranchValue != null) Seed(block.BranchValue, inputs[0]!);
    }
    foreach (var local in graph.LocalFunctions)
      inputs[0] = NestedState(graph.GetLocalFunctionControlFlowGraph(local, cancellation), inputs[0]!);
    var continuations = new Dictionary<int, List<BasicBlock>>();
    foreach (var block in graph.Blocks)
    {
      AddContinuations(block.FallThroughSuccessor);
      AddContinuations(block.ConditionalSuccessor);
    }
    var queue = new Queue<int>();
    queue.Enqueue(0);
    _emit = false;
    while (queue.Count > 0)
    {
      cancellation.ThrowIfCancellationRequested();
      var index = queue.Dequeue();
      var block = graph.Blocks[index];
      var state = new Dictionary<object, int>(inputs[index]!);
      VisitBlock(block, state);
      // Exception handlers get conservative stale inputs: a failed call does not prove
      // reacquisition occurred. A mint inside the handler can restore freshness.
      for (var region = block.EnclosingRegion; region != null && region != graph.Root; region = region.EnclosingRegion)
        if (region.Kind == ControlFlowRegionKind.Try && region.EnclosingRegion?.Kind == ControlFlowRegionKind.TryAndCatch)
          foreach (var handler in region.EnclosingRegion.NestedRegions)
            if (handler.Kind is ControlFlowRegionKind.Catch or ControlFlowRegionKind.FilterAndHandler)
            {
              var exceptional = new Dictionary<object, int>(state);
              Invalidate(exceptional);
              if (handler.FirstBlockOrdinal < graph.Blocks.Length) Propagate(graph.Blocks[handler.FirstBlockOrdinal], exceptional);
            }
      Follow(block.FallThroughSuccessor, state);
      Follow(block.ConditionalSuccessor, state);
      if (continuations.TryGetValue(index, out var destinations))
        foreach (var destination in destinations) Propagate(destination, state);
    }
    _emit = true;
    foreach (var block in graph.Blocks)
      if (inputs[block.Ordinal] is { } input) VisitBlock(block, new(input));

    void AddContinuations(ControlFlowBranch? branch)
    {
      if (branch == null || branch.FinallyRegions.IsEmpty) return;
      for (var i = 0; i < branch.FinallyRegions.Length; i++)
      {
        var region = branch.FinallyRegions[i];
        var destination = i + 1 < branch.FinallyRegions.Length ? graph.Blocks[branch.FinallyRegions[i + 1].FirstBlockOrdinal] : branch.Destination;
        if (destination == null) continue;
        if (!continuations.TryGetValue(region.LastBlockOrdinal, out var list)) continuations[region.LastBlockOrdinal] = list = [];
        list.Add(destination);
      }
    }
    void Follow(ControlFlowBranch? branch, Dictionary<object, int> state)
    {
      if (branch == null) return;
      Propagate(branch.FinallyRegions.IsEmpty ? branch.Destination : graph.Blocks[branch.FinallyRegions[0].FirstBlockOrdinal], state);
    }
    void Propagate(BasicBlock? destination, Dictionary<object, int> state)
    {
      if (destination == null) return;
      if (inputs[destination.Ordinal] == null)
      {
        inputs[destination.Ordinal] = new(state);
        queue.Enqueue(destination.Ordinal);
        return;
      }
      var changed = false;
      var target = inputs[destination.Ordinal]!;
      foreach (var pair in state)
      {
        target.TryGetValue(pair.Key, out var before);
        var after = before | pair.Value;
        if (after != before) { target[pair.Key] = after; changed = true; }
      }
      if (changed) queue.Enqueue(destination.Ordinal);
    }
  }

  private Dictionary<object, int> NestedState(ControlFlowGraph graph, Dictionary<object, int> state)
  {
    var nested = new Dictionary<object, int>(state);
    foreach (var block in graph.Blocks)
    {
      foreach (var operation in block.Operations) Seed(operation, nested);
      if (block.BranchValue != null) Seed(block.BranchValue, nested);
    }
    foreach (var local in graph.LocalFunctions)
      nested = NestedState(graph.GetLocalFunctionControlFlowGraph(local, cancellation), nested);
    return nested;
  }

  private void Seed(IOperation operation, Dictionary<object, int> state)
  {
    object? key = operation switch
    {
      IParameterReferenceOperation parameter => parameter.Parameter,
      IInstanceReferenceOperation => "this",
      IFieldReferenceOperation field => field.Field,
      IPropertyReferenceOperation property when property.Instance == null => property.Property,
      _ => null,
    };
    if (key != null && effects.Carries(operation.Type) && !state.ContainsKey(key)) state[key] = operation is IFieldReferenceOperation { Field.IsStatic: true } ? Stale : Fresh;
    foreach (var child in operation.ChildOperations) Seed(child, state);
  }

  private void VisitBlock(BasicBlock block, Dictionary<object, int> state)
  {
    foreach (var operation in block.Operations) Eval(operation, state);
    if (block.BranchValue != null)
    {
      var value = Eval(block.BranchValue, state);
      if (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return) Use(block.BranchValue, value);
    }
  }

  private int Eval(IOperation? operation, Dictionary<object, int> state)
  {
    if (operation == null) return 0;
    switch (operation)
    {
      case ILocalReferenceOperation local:
        return Read(local.Local, local.Type, state, Stale);
      case IParameterReferenceOperation parameter:
        return Read(parameter.Parameter, parameter.Type, state, Fresh);
      case IInstanceReferenceOperation instance:
        return Read("this", instance.Type, state, Fresh);
      case IFlowCaptureReferenceOperation capture:
        return Read(capture.Id, capture.Type, state, Stale);
      case IFlowCaptureOperation capture:
        var captured = Eval(capture.Value, state);
        state[capture.Id] = captured;
        if (capture.Value is IDelegateCreationOperation { Target: IFlowAnonymousFunctionOperation lambda }) _callbacks[capture.Id] = lambda;
        return captured;
      case IVariableDeclaratorOperation variable:
        var value = Eval(variable.Initializer?.Value, state);
        state[variable.Symbol] = value;
        return value;
      case ISimpleAssignmentOperation assignment:
        var assigned = Eval(assignment.Value, state);
        // Evaluate the storage receiver as well (indexers and setters can have effects).
        if (assignment.Target is not ILocalReferenceOperation and not IParameterReferenceOperation)
          Eval(assignment.Target, state);
        if (ProofEffects.DomainWrite(assignment.Target)
          || assignment.Target is IPropertyReferenceOperation { Property.SetMethod: { } setter } && effects.Invalidates(setter)) Invalidate(state);
        Set(assignment.Target, assigned, state);
        return assigned;
      case ICompoundAssignmentOperation compound:
        Eval(compound.Target, state);
        Eval(compound.Value, state);
        if (ProofEffects.DomainWrite(compound.Target) || compound.Target is IPropertyReferenceOperation { Property.SetMethod: { } compoundSetter } && effects.Invalidates(compoundSetter)) Invalidate(state);
        return 0;
      case IIncrementOrDecrementOperation increment:
        Eval(increment.Target, state);
        if (ProofEffects.DomainWrite(increment.Target) || increment.Target is IPropertyReferenceOperation { Property.SetMethod: { } incrementSetter } && effects.Invalidates(incrementSetter)) Invalidate(state);
        return 0;
      case IConversionOperation conversion:
        return Eval(conversion.Operand, state);
      case IPropertyReferenceOperation property:
        var receiver = ProofContract.Identity(property.Property)
          ? Eval(property.Instance, state)
          : EvaluateInputs(property.Instance, property.Arguments, state);
        if (property.Property.GetMethod is { } getter && effects.Invalidates(getter)) Invalidate(state);
        return property.Property.Name == "Case" ? receiver : effects.Carries(property.Type) ? receiver != 0 ? receiver : Read(property.Property, property.Type, state, Fresh) : 0;
      case IFieldReferenceOperation field:
        var owner = Eval(field.Instance, state);
        if (!field.IsDeclaration) Use(field.Instance, owner);
        return effects.Carries(field.Type) ? owner != 0 ? owner : Read(field.Field, field.Type, state, field.Field.IsStatic ? Stale : Fresh) : 0;
      case IInvocationOperation call:
        return Call(call, state);
      case IObjectCreationOperation creation:
        var created = EvaluateInputs(null, creation.Arguments, state);
        if (creation.Constructor != null && effects.Invalidates(creation.Constructor))
        {
          Invalidate(state);
          if (created != 0) created = Stale;
        }
        state[creation] = effects.Carries(creation.Type) ? created != 0 ? created : Fresh : 0;
        Eval(creation.Initializer, state);
        return state[creation];
      case IReturnOperation returned:
        var returnedValue = Eval(returned.ReturnedValue, state);
        Use(returned.ReturnedValue, returnedValue);
        return returnedValue;
      case IIsPatternOperation pattern:
        var matched = Eval(pattern.Value, state);
        BindPattern(pattern.Pattern, matched, state);
        return 0;
      case IDelegateCreationOperation { Target: IMethodReferenceOperation bound }:
        var boundReceiver = Eval(bound.Instance, state);
        // A delegate can execute arbitrarily later; a receiver carrying a proof cannot escape.
        if (boundReceiver != 0) Use(bound.Instance, Stale);
        AnalyzeLocal(bound.Method, state, deferred: true);
        return 0;
      case IDelegateCreationOperation { Target: IFlowAnonymousFunctionOperation deferred }:
        if (_emit && !(operation.Parent is IArgumentOperation { Parent: IInvocationOperation immediate } && ProofEffects.InlineCallbacks(immediate.TargetMethod)))
        {
          var deferredGraph = _graph.GetAnonymousFunctionControlFlowGraph(deferred, cancellation);
          var deferredState = NestedState(deferredGraph, state);
          Invalidate(deferredState);
          foreach (var parameter in deferred.Symbol.Parameters) deferredState.Remove(parameter);
          new ProofFlow(effects, report, cancellation, _activeLocals).Analyze(deferredGraph, deferredState);
        }
        // Immediate LanguageExt callbacks are analyzed by Call. Other closures must not
        // retain a proof; their execution time is unknown.
        return 0;
      case IAwaitOperation awaited:
        Eval(awaited.Operation, state);
        Invalidate(state);
        return effects.Carries(awaited.Type) ? Stale : 0;
      case IInterpolationOperation interpolation:
        foreach (var child in interpolation.ChildOperations) Eval(child, state);
        if (!ProofEffects.Scalar(interpolation.Expression.Type)) Invalidate(state);
        return 0;
      default:
        var result = 0;
        foreach (var child in operation.ChildOperations) result |= Eval(child, state);
        return effects.Carries(operation.Type) ? result : 0;
    }
  }

  private int Call(IInvocationOperation call, Dictionary<object, int> state)
  {
    var provenance = EvaluateInputs(call.Instance, call.Arguments, state);
    AnalyzeLocal(call.TargetMethod, state, deferred: false, call);
    var callbacks = new List<IFlowAnonymousFunctionOperation>();
    foreach (var argument in call.Arguments)
    {
      if (argument.Value is IDelegateCreationOperation { Target: IFlowAnonymousFunctionOperation lambda }) callbacks.Add(lambda);
      if (argument.Value is IFlowCaptureReferenceOperation capture && _callbacks.TryGetValue(capture.Id, out var callback)) callbacks.Add(callback);
    }
    if (provenance != 0 && ProofEffects.RepeatedCallbacks(call.TargetMethod))
      foreach (var argument in call.Arguments)
        if (argument.Value is IDelegateCreationOperation { Target: IMethodReferenceOperation reference } && effects.Invalidates(reference.Method))
          Use(argument.Value, Stale);
    foreach (var callback in callbacks)
    {
      if (!_emit) continue;
      var callbackGraph = _graph.GetAnonymousFunctionControlFlowGraph(callback, cancellation);
      var callbackState = NestedState(callbackGraph, state);
      var repeatsMutation = ProofEffects.RepeatedCallbacks(call.TargetMethod) && effects.BodyInvalidates(callback);
      if (!ProofEffects.InlineCallbacks(call.TargetMethod) || repeatsMutation) Invalidate(callbackState);
      foreach (var parameter in callback.Symbol.Parameters)
        if (effects.Carries(parameter.Type)) callbackState[parameter] = repeatsMutation ? Stale : provenance != 0 ? provenance : Fresh;
      new ProofFlow(effects, report, cancellation, _activeLocals).Analyze(callbackGraph, callbackState);
    }
    var invalidates = effects.Invalidates(call);
    foreach (var callback in callbacks)
      invalidates |= effects.BodyInvalidates(callback);
    if (invalidates) Invalidate(state);
    if (!effects.Carries(call.Type)) return 0;
    if (provenance != 0) return invalidates ? Stale : provenance;
    return effects.ReturnsFresh(call) && !invalidates ? Fresh : Stale;
  }

  private void AnalyzeLocal(IMethodSymbol method, Dictionary<object, int> state, bool deferred, IInvocationOperation? call = null)
  {
    if (!_emit || method.MethodKind != MethodKind.LocalFunction) return;
    method = method.OriginalDefinition;
    for (var graph = _graph; graph != null; graph = graph.Parent)
    {
      if (!System.Linq.Enumerable.Contains(graph.LocalFunctions, method, SymbolEqualityComparer.Default)) continue;
      var localGraph = graph.GetLocalFunctionControlFlowGraph(method, cancellation);
      var captured = NestedState(localGraph, state);
      if (deferred) Invalidate(captured);
      foreach (var parameter in method.Parameters) captured.Remove(parameter);
      if (call != null)
        foreach (var argument in call.Arguments)
          if (argument.Parameter != null && effects.Carries(argument.Parameter.Type)) captured[argument.Parameter.OriginalDefinition] = state[argument.Value];
      // Recursive closure freshness is outside this small analysis. Reject a recursive
      // proof use rather than silently assuming its captures remain fresh.
      if (!_activeLocals.Add(method))
      {
        if (System.Linq.Enumerable.Any(captured.Values, value => value != 0)) Use(call, Stale);
        return;
      }
      try { new ProofFlow(effects, report, cancellation, _activeLocals).Analyze(localGraph, captured); }
      finally { _activeLocals.Remove(method); }
      return;
    }
  }

  private int EvaluateInputs(IOperation? instance, System.Collections.Immutable.ImmutableArray<IArgumentOperation> arguments, Dictionary<object, int> state)
  {
    var inputs = new List<IOperation>();
    if (instance != null) inputs.Add(instance);
    foreach (var argument in arguments) inputs.Add(argument.Value);
    foreach (var input in inputs) state[input] = Eval(input, state);
    var provenance = 0;
    foreach (var input in inputs)
    {
      var value = state[input];
      Use(input, value);
      provenance |= value;
    }
    return provenance;
  }

  private int Read(object key, ITypeSymbol? type, Dictionary<object, int> state, int initial)
  {
    if (state.TryGetValue(key, out var stored)) return stored;
    if (!effects.Carries(type)) return 0;
    state[key] = initial;
    var value = initial;
    return value;
  }
  private static void Set(IOperation target, int value, Dictionary<object, int> state)
  {
    object? key = target switch
    {
      ILocalReferenceOperation local => local.Local,
      IParameterReferenceOperation parameter => parameter.Parameter,
      IFieldReferenceOperation field => field.Field,
      IPropertyReferenceOperation property => property.Property,
      _ => null,
    };
    if (key != null) state[key] = value;
  }
  private static void Invalidate(Dictionary<object, int> state)
  {
    foreach (var key in new List<object>(state.Keys))
      if (state[key] != 0) state[key] = Stale;
  }
  private void Use(IOperation? operation, int value)
  {
    if (!_emit || operation == null || (value & Stale) == 0) return;
    var syntax = operation.Syntax;
    if (_reported.Add((syntax.SyntaxTree, syntax.SpanStart)))
      report(Diagnostic.Create(SnapshotProofAnalyzer.Stale, syntax.GetLocation(), syntax.ToString()));
  }
  private static void BindPattern(IPatternOperation pattern, int value, Dictionary<object, int> state)
  {
    if (pattern is IDeclarationPatternOperation { DeclaredSymbol: { } symbol }) state[symbol] = value;
    foreach (var child in pattern.ChildOperations)
      if (child is IPatternOperation nested) BindPattern(nested, value, state);
  }
}
