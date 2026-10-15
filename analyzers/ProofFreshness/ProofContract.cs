using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FunProject.Analyzers;

internal sealed class ProofContract(Compilation compilation)
{
  private readonly Dictionary<ITypeSymbol, bool> _carriers = new(SymbolEqualityComparer.Default);
  internal static bool Has(ISymbol symbol, string name)
  {
    if (symbol is IMethodSymbol { AssociatedSymbol: { } associated } && Has(associated, name)) return true;
    foreach (var attribute in symbol.GetAttributes())
      if (attribute.AttributeClass?.ToDisplayString() == "FunProject.Battle." + name + "Attribute") return true;
    return false;
  }
  internal static bool IsProof(ITypeSymbol? type) => type != null && Has(type, "SnapshotProof");
  internal bool Carries(ITypeSymbol? type)
  {
    if (type == null) return false;
    if (!_carriers.TryGetValue(type, out var result))
      _carriers[type] = result = Carries(type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
    return result;
  }
  private bool Carries(ITypeSymbol? type, HashSet<ITypeSymbol> seen)
  {
    if (type == null || !seen.Add(type)) return false;
    if (IsProof(type)) return true;
    if (type is IArrayTypeSymbol array) return Carries(array.ElementType, seen);
    if (type is not INamedTypeSymbol named) return false;
    if (Carries(named.BaseType, seen)) return true;
    foreach (var argument in named.TypeArguments)
      if (Carries(argument, seen)) return true;
    foreach (var member in named.GetMembers())
      if (member is IFieldSymbol { IsStatic: false } field && field.Locations.Length > 0 && field.Locations[0].IsInSource
        && Carries(field.Type, new(seen, SymbolEqualityComparer.Default)) && !IdentityOnly(named, field.AssociatedSymbol ?? field)) return true;
    foreach (var constructor in named.InstanceConstructors)
      foreach (var reference in constructor.DeclaringSyntaxReferences)
        if (reference.GetSyntax() is TypeDeclarationSyntax syntax)
          foreach (var parameter in constructor.Parameters)
            if (syntax.DescendantNodes().OfType<IdentifierNameSyntax>().Any(identifier => SymbolEqualityComparer.Default.Equals(compilation.GetSemanticModel(syntax.SyntaxTree).GetSymbolInfo(identifier).Symbol, parameter.OriginalDefinition)
              && identifier.Ancestors().Any(node => node is MethodDeclarationSyntax or PropertyDeclarationSyntax or IndexerDeclarationSyntax))
              && Carries(parameter.Type, new(seen, SymbolEqualityComparer.Default)) && !IdentityOnly(named, parameter)) return true;
    return false;
  }
  // Private retention of an identity is not retention of a snapshot fact. Adding a
  // position read or exposing the whole proof makes the object a carrier again.
  private bool IdentityOnly(INamedTypeSymbol type, ISymbol symbol)
  {
    if (symbol is not IParameterSymbol && symbol.DeclaredAccessibility != Accessibility.Private) return false;
    foreach (var reference in type.DeclaringSyntaxReferences)
    {
      var syntax = reference.GetSyntax();
      var model = compilation.GetSemanticModel(syntax.SyntaxTree);
      foreach (var identifier in syntax.DescendantNodes().OfType<IdentifierNameSyntax>())
      {
        if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol?.OriginalDefinition, symbol.OriginalDefinition)) continue;
        if (identifier.Parent is AssignmentExpressionSyntax assignment && assignment.Left == identifier) continue;
        if (symbol is IParameterSymbol && !identifier.Ancestors().Any(node => node is MethodDeclarationSyntax or PropertyDeclarationSyntax or IndexerDeclarationSyntax)) continue;
        if (identifier.Parent is MemberAccessExpressionSyntax access && access.Expression == identifier
          && model.GetSymbolInfo(access).Symbol is IPropertySymbol property && Identity(property)) continue;
        return false;
      }
    }
    return true;
  }
  internal static bool Identity(IPropertySymbol property) => IsProof(property.ContainingType)
    && property.ContainingNamespace.ToDisplayString() == "FunProject.Battle"
    && property.Name is "State" or "Entity" or "Id";
  internal static bool Mints(IMethodSymbol method) => method.ContainingNamespace.ToDisplayString() == "FunProject.Battle"
    && method.ContainingType.Name is "BattleSession" or "BattleRuntime"
    && method.Name is "TryGetAlive" or "TryGetAliveObject" or "TryGetAttackTarget" or "MintAlive" or "MintAliveObject" or "Query";
}
