using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace FunProject.Analyzers;

// Source helpers are checked transitively. Unknown dispatch and callbacks invalidate.
internal sealed class ProofEffects(Compilation compilation)
{
  private readonly ProofContract _contract = new(compilation);
  internal bool Carries(ITypeSymbol? type) => _contract.Carries(type);
  private readonly Dictionary<IMethodSymbol, bool> _cache = new(SymbolEqualityComparer.Default);
  internal bool Invalidates(IMethodSymbol method)
  {
    method = method.ReducedFrom ?? method;
    if (!_cache.TryGetValue(method, out var effect))
    {
      effect = Invalidates(method, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
      _cache[method] = effect;
    }
    return effect;
  }
  private bool Invalidates(IMethodSymbol method, HashSet<IMethodSymbol> visiting, Dictionary<IParameterSymbol, bool>? delegates = null, Dictionary<ITypeParameterSymbol, ITypeSymbol>? types = null)
  {
    method = method.ReducedFrom ?? method;
    types = types != null ? new(types, SymbolEqualityComparer.Default) : new(SymbolEqualityComparer.Default);
    for (var i = 0; i < method.TypeParameters.Length; i++)
      if (!SymbolEqualityComparer.Default.Equals(method.TypeParameters[i], method.TypeArguments[i])) types[method.TypeParameters[i]] = Resolve(method.TypeArguments[i], types);
    for (var i = 0; i < method.ContainingType.TypeParameters.Length; i++)
      if (!SymbolEqualityComparer.Default.Equals(method.ContainingType.TypeParameters[i], method.ContainingType.TypeArguments[i])) types[method.ContainingType.TypeParameters[i]] = Resolve(method.ContainingType.TypeArguments[i], types);
    if (method.ContainingType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.EqualityComparer<T>" && method.Name == "get_Default"
      && Scalar(method.ContainingType.TypeArguments[0], types)) return false;
    method = method.OriginalDefinition;
    if (ProofContract.Has(method, "InvalidatesProofs")) return true;
    if (!visiting.Add(method)) return false;
    try
    {
      if (method.MethodKind == MethodKind.DelegateInvoke) return true;
      if (method.ContainingType.ToDisplayString() == "System.Delegate" && method.Name == "DynamicInvoke") return true;
      if (method.DeclaringSyntaxReferences.Length == 0 && ExternalRead(method)
        && (method.ContainingNamespace.ToDisplayString().StartsWith("System.Collections", System.StringComparison.Ordinal)
          || method.ContainingNamespace.ToDisplayString().StartsWith("LanguageExt", System.StringComparison.Ordinal)
          || method.ContainingNamespace.ToDisplayString().StartsWith("ZLinq", System.StringComparison.Ordinal))) return false;
      if (method.IsAbstract || method.IsVirtual && !method.IsSealed || method.ContainingType.TypeKind == TypeKind.Interface && !method.IsStatic)
        return !PreservingContract(method);
      if (method.MethodKind == MethodKind.Constructor)
        foreach (var reference in method.ContainingType.DeclaringSyntaxReferences)
          if (reference.GetSyntax() is TypeDeclarationSyntax type)
          {
            var model = compilation.GetSemanticModel(type.SyntaxTree);
            if (type.BaseList != null)
              foreach (var baseSyntax in type.BaseList.Types.OfType<PrimaryConstructorBaseTypeSyntax>())
              {
                if (model.GetSymbolInfo(baseSyntax).Symbol is IMethodSymbol baseConstructor && Invalidates(baseConstructor, visiting, delegates, types)) return true;
                foreach (var argument in baseSyntax.ArgumentList.Arguments)
                  if (model.GetOperation(argument.Expression) is { } baseArgument && Walk(baseArgument, visiting, delegates, types)) return true;
              }
            foreach (var member in type.Members)
            {
              if (member is FieldDeclarationSyntax field && !field.Modifiers.Any(SyntaxKind.StaticKeyword))
                foreach (var variable in field.Declaration.Variables)
                  if (variable.Initializer is { } initializer && model.GetOperation(initializer.Value) is { } init && Walk(init, visiting, delegates, types)) return true;
              if (member is PropertyDeclarationSyntax { Initializer: { } propertyInitializer } && model.GetOperation(propertyInitializer.Value) is { } propertyInit && Walk(propertyInit, visiting, delegates, types)) return true;
            }
          }
      // Source constructors run instance initializers even without an explicit body.
      // Include the implicit base call; explicit initializers are walked below.
      if (method.MethodKind == MethodKind.Constructor && method.ContainingType.TypeKind == TypeKind.Class
        && method.ContainingType.Locations.Any(location => location.IsInSource) && method.ContainingType.BaseType is { } baseType
        && !method.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is ConstructorDeclarationSyntax { Initializer: not null }))
        foreach (var constructor in baseType.InstanceConstructors)
          if (constructor.Parameters.Length == 0 && Invalidates(constructor, visiting, delegates, types)) return true;
      if (method.DeclaringSyntaxReferences.Length == 0)
        return !(method.IsImplicitlyDeclared && method.ContainingType.Locations.Length > 0 && method.ContainingType.Locations[0].IsInSource) && !ExternalRead(method);
      foreach (var reference in method.DeclaringSyntaxReferences)
      {
        var syntax = reference.GetSyntax();
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var operation = model.GetOperation(syntax);
        if (operation == null && syntax is AccessorDeclarationSyntax accessor)
          operation = accessor.Body != null ? model.GetOperation(accessor.Body) : accessor.ExpressionBody != null ? model.GetOperation(accessor.ExpressionBody.Expression) : null;
        if (operation != null && (Walk(operation, visiting, delegates, types) || method.MethodKind == MethodKind.LocalFunction && CapturedProofWrite(operation, method))) return true;
      }
      return false;
    }
    finally { visiting.Remove(method); }
  }
  internal bool Invalidates(IInvocationOperation call) => CallInvalidates(call, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default), null);
  private bool CallInvalidates(IInvocationOperation call, HashSet<IMethodSymbol> visiting, Dictionary<IParameterSymbol, bool>? delegates, Dictionary<ITypeParameterSymbol, ITypeSymbol>? types = null)
  {
    if (call.TargetMethod.ContainingType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.EqualityComparer<T>"
      && call.TargetMethod.Name == "Equals" && Scalar(call.TargetMethod.ContainingType.TypeArguments[0], types)
      && call.Instance is IPropertyReferenceOperation { Property.Name: "Default", Property.IsStatic: true } comparer
      && comparer.Property.ContainingType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.EqualityComparer<T>") return false;
    if (call.TargetMethod.Name == "Dispose" && call.IsImplicit && call.Syntax.AncestorsAndSelf().Any(syntax => syntax is ForEachStatementSyntax or ForEachVariableStatementSyntax)) return false;
    if (call.TargetMethod.MethodKind == MethodKind.DelegateInvoke)
      return DelegateEffect(call.Instance, visiting, delegates, types);
    var bindings = delegates != null ? new Dictionary<IParameterSymbol, bool>(delegates, SymbolEqualityComparer.Default) : new Dictionary<IParameterSymbol, bool>(SymbolEqualityComparer.Default);
    var callbackEffect = false;
    foreach (var argument in call.Arguments)
      if (argument.Parameter != null && argument.Parameter.Type.TypeKind == TypeKind.Delegate)
      {
        var effect = DelegateEffect(argument.Value, visiting, delegates, types);
        callbackEffect |= effect;
        bindings[argument.Parameter.OriginalDefinition] = effect;
      }
    return callbackEffect || Invalidates(call.TargetMethod, visiting, bindings, types);
  }
  private static ITypeSymbol Resolve(ITypeSymbol type, Dictionary<ITypeParameterSymbol, ITypeSymbol>? types)
  {
    var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
    while (type is ITypeParameterSymbol parameter && types != null && types.TryGetValue(parameter, out var bound) && seen.Add(type)) type = bound;
    return type;
  }
  private bool CapturedProofWrite(IOperation operation, IMethodSymbol method)
  {
    if (operation is IAssignmentOperation assignment && Carries(assignment.Target.Type))
    {
      var owner = assignment.Target switch
      {
        ILocalReferenceOperation local => local.Local.ContainingSymbol,
        IParameterReferenceOperation parameter => parameter.Parameter.ContainingSymbol,
        _ => null,
      };
      if (owner != null && !SymbolEqualityComparer.Default.Equals(owner, method)) return true;
    }
    return operation.ChildOperations.Any(child => CapturedProofWrite(child, method));
  }
  internal static bool Scalar(ITypeSymbol? type, Dictionary<ITypeParameterSymbol, ITypeSymbol>? types = null)
  {
    if (type == null) return false;
    type = Resolve(type, types);
    return type.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_Decimal
      || type.SpecialType == SpecialType.System_String || type.TypeKind == TypeKind.Enum
      || type.ToDisplayString() is "Godot.Vector3I" or "Godot.Vector3";
  }
  private bool DelegateEffect(IOperation? value, HashSet<IMethodSymbol> visiting, Dictionary<IParameterSymbol, bool>? delegates, Dictionary<ITypeParameterSymbol, ITypeSymbol>? types = null)
  {
    if (value is IConversionOperation conversion) return DelegateEffect(conversion.Operand, visiting, delegates, types);
    if (value is IDelegateCreationOperation creation) return DelegateEffect(creation.Target, visiting, delegates, types);
    if (value is IMethodReferenceOperation reference) return Invalidates(reference.Method, visiting, delegates, types);
    if (value is IAnonymousFunctionOperation lambda) return Walk(lambda.Body, visiting, delegates, types);
    if (value is IFlowAnonymousFunctionOperation flowLambda) return BodyInvalidates(flowLambda);
    if (value is IParameterReferenceOperation parameter && delegates != null && delegates.TryGetValue(parameter.Parameter.OriginalDefinition, out var effect)) return effect;
    return true;
  }

  internal bool BodyInvalidates(IOperation operation)
  {
    if (operation is IFlowAnonymousFunctionOperation lambda)
      operation = compilation.GetSemanticModel(lambda.Syntax.SyntaxTree).GetOperation(lambda.Syntax) ?? operation;
    return Walk(operation, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
  }
  internal static bool PreservingContract(IMethodSymbol method)
  {
    if (ProofContract.Has(method, "PreservesProofs")) return true;
    if (method.OverriddenMethod != null && PreservingContract(method.OverriddenMethod)) return true;
    foreach (var member in method.ContainingType.AllInterfaces)
      foreach (var contract in member.GetMembers(method.AssociatedSymbol?.Name ?? method.Name))
        if (ProofContract.Has(contract, "PreservesProofs")
          && SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(contract), method.AssociatedSymbol ?? (ISymbol)method)) return true;
    return false;
  }
  internal static bool RepeatedCallbacks(IMethodSymbol method) => InlineCallbacks(method)
    && method.ContainingType.Name is not "Option" and not "Either";
  internal static bool InlineCallbacks(IMethodSymbol method) =>
    method.ContainingNamespace.ToDisplayString().StartsWith("LanguageExt", System.StringComparison.Ordinal)
      && method.Name is "Map" or "Bind" or "Match" or "IfSome" or "IfNone" or "MapLeft" or "IfRight" or "IfLeft"
    || method.ContainingNamespace.ToDisplayString().StartsWith("ZLinq", System.StringComparison.Ordinal)
      && method.Name is "All" or "Any" or "Count";
  private static bool ExternalRead(IMethodSymbol method)
  {
    var ns = method.ContainingNamespace.ToDisplayString();
    var type = method.ContainingType.Name;
    var name = method.Name;
    if (ns == "System.Numerics" && type is "Vector2" or "Vector3" or "Vector4") return true;
    if (ns == "System")
      return type is "Nullable" or "Math" or "MathF" or "Int32" or "Int64" or "Double" or "Single" or "Boolean"
        || type == "String" && name is "get_Length" or "get_Chars" or "IsNullOrEmpty" or "IsNullOrWhiteSpace" or "Equals" or "Compare" or "CompareOrdinal" or "Contains" or "StartsWith" or "EndsWith" or "Substring" or "Trim" or "TrimStart" or "TrimEnd" or "Replace" or "Split" or "ToLowerInvariant" or "ToUpperInvariant"
        || type.EndsWith("Exception", System.StringComparison.Ordinal) && name is ".ctor" or "ThrowIfNull" or "ThrowIfNegative" or "ThrowIfLessThan" or "ThrowIf" or "ThrowIfGreaterThan" or "ThrowIfNegativeOrZero"
        || type is "Object" or "Attribute" && name is ".ctor" or "ReferenceEquals" or "GetType"
        || type == "Array" && name is "get_Length" or "get_LongLength" or "Empty" or "AsReadOnly" or "Copy" or "GetEnumerator"
        || type == "Type" && name is "GetTypeFromHandle" or "IsAssignableTo" or "IsAssignableFrom" or "get_Name"
        || type == "Random" && name is ".ctor" or "Next";
    if (ns.StartsWith("System.Collections", System.StringComparison.Ordinal))
      return name is ".ctor" or "get_Current" or "MoveNext" or "get_Key" or "get_Value" or "get_Values" or "get_Keys" or "get_Count" or "get_Item" or "set_Item" or "TryGetValue" or "ContainsKey" or "Contains" or "Add" or "AddRange" or "Remove" or "RemoveAt" or "Clear" or "Enqueue" or "Dequeue" or "Peek" or "TryDequeue" or "TryPeek" or "ToArray" or "ToList" or "AsReadOnly" or "GetEnumerator" or "UnionWith" or "Overlaps" or "SetEquals";
    if (ns.StartsWith("LanguageExt", System.StringComparison.Ordinal))
      return method.MethodKind is MethodKind.PropertyGet or MethodKind.Conversion
        || name is ".ctor" or "Some" or "Optional" or "Value" or "None" or "Left" or "Right" or "Map" or "MapLeft" or "Bind" or "Match" or "IfSome" or "IfNone" or "IfLeft" or "IfRight" or "ValueUnsafe" or "Somes" or "Contains" or "ToArray";
    if (ns.StartsWith("ZLinq", System.StringComparison.Ordinal))
      return name is "GetEnumerator" or "get_Current" or "MoveNext" or "Dispose" or "AsValueEnumerable" or "Skip" or "Take" or "Where" or "Select" or "SelectMany" or "All" or "Any" or "Count" or "ToArray" or "ToList" or "ToHashSet" or "ToDictionary" or "Single" or "SingleOrDefault" or "First" or "FirstOrDefault" or "Concat" or "Reverse" or "OrderBy" or "ThenBy" or "OrderByDescending" or "Distinct" or "Sum" or "Min" or "Max" or "SequenceEqual" or "Contains" or "OfType" or "Cast";
    return method.ContainingType.ToDisplayString() is "Godot.Mathf" or "Godot.Vector3I" or "Godot.Vector3"
      || ns == "Godot.Collections" && (method.MethodKind == MethodKind.PropertyGet || name == ".ctor");
  }
  private bool Walk(IOperation operation, HashSet<IMethodSymbol> visiting, Dictionary<IParameterSymbol, bool>? delegates = null, Dictionary<ITypeParameterSymbol, ITypeSymbol>? types = null)
  {
    if (operation is IAwaitOperation) return true;
    if (operation is IInterpolationOperation interpolation && !Scalar(interpolation.Expression.Type, types)) return true;
    if (operation is IInvocationOperation call)
    {
      if (CallInvalidates(call, visiting, delegates, types)) return true;
    }
    if (operation is IObjectCreationOperation creation && creation.Constructor != null && Invalidates(creation.Constructor, visiting, delegates, types)) return true;
    if (operation is IPropertyReferenceOperation property && property.Property.GetMethod is { } getter
      && Invalidates(getter, visiting, delegates, types)) return true;
    if (operation is IAssignmentOperation assignment && (DomainWrite(assignment.Target)
      || assignment.Target is IPropertyReferenceOperation { Property.SetMethod: { } setter } && Invalidates(setter, visiting, delegates, types))) return true;
    if (operation is IIncrementOrDecrementOperation increment && (DomainWrite(increment.Target)
      || increment.Target is IPropertyReferenceOperation { Property.SetMethod: { } incrementSetter } && Invalidates(incrementSetter, visiting, delegates, types))) return true;
    foreach (var child in operation.ChildOperations)
      if (Walk(child, visiting, delegates, types)) return true;
    return false;
  }
  internal bool ReturnsFresh(IInvocationOperation call) => FreshOrigin(call, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
  internal bool ReturnsFresh(IMethodSymbol method) => ReturnsFresh(method, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
  private bool ReturnsFresh(IMethodSymbol method, HashSet<IMethodSymbol> seen)
  {
    if (ProofContract.Mints(method)) return true;
    if (!seen.Add(method.OriginalDefinition)) return false;
    var found = false;
    foreach (var reference in method.DeclaringSyntaxReferences)
    {
      var syntax = reference.GetSyntax();
      var operation = compilation.GetSemanticModel(syntax.SyntaxTree).GetOperation(syntax);
      if (operation == null) continue;
      foreach (var child in Returns(operation))
        if (child is IReturnOperation returned && returned.ReturnedValue != null)
        {
          found = true;
          if (!FreshOrigin(returned.ReturnedValue, seen)) return false;
        }
    }
    return found;
  }
  private bool FreshOrigin(IOperation operation, HashSet<IMethodSymbol> seen)
  {
    if (operation is IConversionOperation conversion) return FreshOrigin(conversion.Operand, seen);
    if (operation is IObjectCreationOperation creation && ProofContract.IsProof(creation.Type))
    {
      for (var owner = compilation.GetSemanticModel(creation.Syntax.SyntaxTree).GetEnclosingSymbol(creation.Syntax.SpanStart); owner != null; owner = owner.ContainingSymbol)
        if (owner is IMethodSymbol mint && ProofContract.Mints(mint)) return true;
      return false;
    }
    if (operation is not IInvocationOperation call) return false;
    if (ReturnsFresh(call.TargetMethod, new HashSet<IMethodSymbol>(seen, SymbolEqualityComparer.Default))) return true;
    var ns = call.TargetMethod.ContainingNamespace.ToDisplayString();
    var combinator = (ns.StartsWith("LanguageExt", System.StringComparison.Ordinal) || ns.StartsWith("ZLinq", System.StringComparison.Ordinal)
      || ns.StartsWith("System.Linq", System.StringComparison.Ordinal))
      && call.TargetMethod.Name is "Match" or "Map" or "Bind" or "Select" or "Where" or "ToArray" or "ToList" or "Single" or "AsValueEnumerable" or "Some";
    if (!combinator && !Forwards(call.TargetMethod)) return false;
    if (call.Instance != null && FreshOrigin(call.Instance, seen)) return true;
    foreach (var argument in call.Arguments)
    {
      if (combinator && argument.Value is IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda }
        && ReturnsFresh(lambda.Symbol, new HashSet<IMethodSymbol>(seen, SymbolEqualityComparer.Default))) return true;
      if (combinator && argument.Value is IDelegateCreationOperation { Target: IFlowAnonymousFunctionOperation flowLambda }
        && ReturnsFresh(flowLambda.Symbol, new HashSet<IMethodSymbol>(seen, SymbolEqualityComparer.Default))) return true;
      if (combinator && argument.Value is IDelegateCreationOperation { Target: IMethodReferenceOperation reference }
        && ReturnsFresh(reference.Method, new HashSet<IMethodSymbol>(seen, SymbolEqualityComparer.Default))) return true;
      if (FreshOrigin(argument.Value, seen)) return true;
    }
    return false;
  }
  private bool Forwards(IMethodSymbol method)
  {
    var found = false;
    foreach (var reference in method.DeclaringSyntaxReferences)
    {
      var syntax = reference.GetSyntax();
      var operation = compilation.GetSemanticModel(syntax.SyntaxTree).GetOperation(syntax);
      if (operation == null) continue;
      foreach (var child in Descendants(operation))
        if (child is IReturnOperation { ReturnedValue: { } value })
        {
          found = true;
          if (!Forwarded(value)) return false;
        }
    }
    return found;
  }
  private static bool Forwarded(IOperation value)
  {
    if (value is IConversionOperation conversion) return Forwarded(conversion.Operand);
    if (value is IThrowOperation) return true;
    if (value is IParameterReferenceOperation) return true;
    return value is IInvocationOperation call && InlineCallbacks(call.TargetMethod)
      && call.Instance is IParameterReferenceOperation;
  }

  internal string Explain(IOperation operation)
  {
    foreach (var child in operation.ChildOperations)
    {
      if (child is IObjectCreationOperation made && made.Constructor != null && Invalidates(made.Constructor)) return ExplainMethod(made.Constructor, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
      if (child is IInvocationOperation call && Invalidates(call.TargetMethod))
        return ExplainMethod(call.TargetMethod, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
      if (child is IPropertyReferenceOperation property && property.Property.GetMethod is { } getter && Invalidates(getter))
        return ExplainMethod(getter, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
      var nested = Explain(child);
      if (nested.Length > 0) return nested;
    }
    return "";
  }
  private string ExplainMethod(IMethodSymbol method, HashSet<IMethodSymbol> seen)
  {
    if (!seen.Add(method)) return method.ToDisplayString();
    foreach (var reference in method.DeclaringSyntaxReferences)
    {
      var syntax = reference.GetSyntax();
      var operation = compilation.GetSemanticModel(syntax.SyntaxTree).GetOperation(syntax);
      if (operation == null) continue;
      foreach (var child in Descendants(operation))
      {
        var callee = child switch { IInvocationOperation call => call.TargetMethod, IObjectCreationOperation made => made.Constructor, IPropertyReferenceOperation property => property.Property.GetMethod, _ => null };
        if (callee != null && Invalidates(callee)) return method.Name + " -> " + ExplainMethod(callee, seen);
      }
    }
    return method.ToDisplayString();
  }
  private static IEnumerable<IOperation> Returns(IOperation operation)
  {
    foreach (var child in operation.ChildOperations)
    {
      if (child is IAnonymousFunctionOperation or ILocalFunctionOperation) continue;
      if (child is IReturnOperation) yield return child;
      foreach (var nested in Returns(child)) yield return nested;
    }
  }
  private static IEnumerable<IOperation> Descendants(IOperation operation)
  {
    foreach (var child in operation.ChildOperations) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
  }

  internal static bool DomainWrite(IOperation target)
  {
    var owner = target switch
    {
      IFieldReferenceOperation field => field.Field.ContainingType,
      IPropertyReferenceOperation property => property.Property.ContainingType,
      _ => null,
    };
    return owner?.ContainingNamespace.ToDisplayString() == "FunProject.Battle"
      && (owner.Name == "BattleBoardState" || owner.Name == "BattleObjectState"
        || owner.Name == "BattleUnitState" && target is IPropertyReferenceOperation { Property.Name: "CurrentHealth" });
  }
}
