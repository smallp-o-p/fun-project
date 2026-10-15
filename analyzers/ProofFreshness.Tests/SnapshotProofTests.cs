using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FunProject.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace FunProject.Analyzers.Tests;

public sealed class SnapshotProofTests
{
  private const string Contract = """
    using System;
    using FunProject.Battle;
    using LanguageExt;
    using Query = FunProject.Battle.Query;
    namespace FunProject.Battle {
      [AttributeUsage(AttributeTargets.Struct)] sealed class SnapshotProofAttribute : Attribute { }
      [AttributeUsage(AttributeTargets.Method)] sealed class InvalidatesProofsAttribute : Attribute { }
      [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)] sealed class PreservesProofsAttribute : Attribute { }
      [SnapshotProof] public readonly struct AliveUnit {
        public int Position => 0;
        public object State => new object();
      }
      public readonly struct StableCapability { public int Value => 0; }
      public class Query(AliveUnit proof) { public int Value { get; set; } public int Read() => proof.Position; }
      public sealed class IndexedQuery(AliveUnit proof) { public int this[int _] => proof.Position; }
      public sealed class BattleUnitState { public int CurrentHealth { get; set; } }
      public sealed class DerivedQuery(AliveUnit p) : Query(p);
      public sealed class ArgumentQuery(AliveUnit proof, int _) { public int Read() => proof.Position; }
      public interface PreservingProperty { [PreservesProofs] int Value { get; } }
      public sealed class Mutator(BattleSession s) { public int Value { get => 0; set => s.Mutate(); } }
      public sealed class BadQuery {
        readonly AliveUnit proof;
        public BadQuery(AliveUnit p, BattleSession s) { proof = p; s.Mutate(); }
        public int Read() => proof.Position;
      }
      public interface Unknown { void Call(); }
      public sealed class BattleSession {
        public BattleUnitState Unit { get; } = new();
        public Option<AliveUnit> TryGetAlive(object state) => LanguageExt.Prelude.Some(MintAlive(state));
        public int MutationArgument() { Mutate(); return 0; }
        public void Consume(AliveUnit p, int n) { _ = p.Position; }
        public AliveUnit Copy(AliveUnit p) => p;
        public void Callback(Action callback) => callback();
        public AliveUnit MintAlive(object state) => new AliveUnit();
        [InvalidatesProofs] public void Mutate() { }
        public void Read(AliveUnit p) { _ = p.Position; }
        public void Helper() { Mutate(); }
        public void SafeHelper() { _ = 1; }
      }
    }
    """;

  [Theory]
  [InlineData("var p = s.MintAlive(new object()); void Read() { s.Read(p); } Read(); Read();", 0)]
  [InlineData("var p = s.MintAlive(new object()); void Read() { s.Read(p); } Read(); s.Mutate(); Read();", 1)]
  [InlineData("var p = s.MintAlive(new object()); void Read() { s.Read(p); } Action later = Read;", 1)]
  [InlineData("var p = s.MintAlive(new object()); void Change() { s.Mutate(); } Change(); s.Read(p);", 1)]
  [InlineData("var p = s.MintAlive(new object()); void Read<T>() { s.Read(p); } s.Mutate(); Read<int>();", 1)]
  [InlineData("var p = s.MintAlive(new object()); void Read<T>() { s.Read(p); } Action later = Read<int>;", 1)]
  [InlineData("var old = s.MintAlive(new object()); s.Mutate(); var p = s.MintAlive(new object()); void Replace() { p = old; } Replace(); s.Read(p);", 1)]
  [InlineData("var p = s.MintAlive(new object()); void Outer() { void Inner() { s.Read(p); } Inner(); } s.Mutate(); Outer();", 1)]
  [InlineData("var p = s.MintAlive(new object()); void Read(int n) { s.Read(p); if (n > 0) LanguageExt.Prelude.Some(1).IfSome(_ => Read(n - 1)); } Read(1);", 1)]
  [InlineData("var p = s.MintAlive(new object()); s.Read(p); s.Read(p);", 0)]
  [InlineData("var p = s.MintAlive(new object()); s.Mutate(); s.Read(p);", 1)]
  [InlineData("var p = s.MintAlive(new object()); var q = p; s.Mutate(); s.Read(q);", 1)]
  [InlineData("var p = s.MintAlive(new object()); if (flag) s.Mutate(); s.Read(p);", 1)]
  [InlineData("var p = s.MintAlive(new object()); s.Mutate(); if (flag) p = s.MintAlive(new object()); s.Read(p);", 1)]
  [InlineData("var p = s.MintAlive(new object()); s.Mutate(); p = s.MintAlive(new object()); s.Read(p);", 0)]
  [InlineData("var p = s.MintAlive(new object()); s.Helper(); s.Read(p);", 1)]
  [InlineData("var p = s.MintAlive(new object()); s.SafeHelper(); s.Read(p);", 0)]
  [InlineData("var p = s.MintAlive(new object()); var q = new Query(p); s.Mutate(); _ = q.Read();", 1)]
  [InlineData("var p = s.MintAlive(new object()); var q = new Query(p); _ = q.Read(); _ = q.Read();", 0)]
  [InlineData("var p = new StableCapability(); s.Mutate(); _ = p.Value; _ = p.Value;", 0)]
  [InlineData("var p = s.MintAlive(new object()); s.Mutate(); _ = p.State;", 0)]
  [InlineData("var p = s.MintAlive(new object()); while (flag) { s.Read(p); s.Mutate(); }", 1)]
  [InlineData("var p = s.MintAlive(new object()); s.Consume(p, s.MutationArgument());", 1)]
  [InlineData("var p = s.MintAlive(new object()); try { } finally { s.Mutate(); } s.Read(p);", 1)]
  [InlineData("var p = s.MintAlive(new object()); s.Unit.CurrentHealth = 0; s.Read(p);", 1)]
  [InlineData("var p = s.MintAlive(new object()); s.Mutate(); var q = s.Copy(p); s.Read(q);", 2)]
  [InlineData("var p = s.MintAlive(new object()); s.Callback(() => s.Mutate()); s.Read(p);", 1)]
  [InlineData("var p = s.TryGetAlive(new object()); s.Mutate(); if (p.Case is AliveUnit q) s.Read(q);", 2)]
  [InlineData("var p = s.TryGetAlive(new object()); p.IfSome(q => { s.Read(q); s.Read(q); });", 0)]
  [InlineData("var p = s.TryGetAlive(new object()); s.Mutate(); p.IfSome(q => s.Read(q));", 2)]
  [InlineData("var p = s.MintAlive(new object()); Action later = () => s.Read(p);", 1)]
  [InlineData("var p = s.MintAlive(new object()); var q = LanguageExt.Prelude.Some(p); s.Mutate(); if (q.Case is AliveUnit copy) s.Read(copy);", 2)]
  [InlineData("var p = s.MintAlive(new object()); var m = new Mutator(s); m.Value = 1; s.Read(p);", 1)]
  [InlineData("var q = new Query(s.MintAlive(new object())); Func<int> read = q.Read; s.Mutate(); _ = read();", 1)]
  [InlineData("var q = new BadQuery(s.MintAlive(new object()), s); _ = q.Read();", 1)]
  [InlineData("var p = s.MintAlive(new object()); var q = new ArgumentQuery(p, s.MutationArgument()); _ = q.Read();", 2)]
  [InlineData("var p = s.MintAlive(new object()); var m = new Mutator(s); m.Value++; s.Read(p);", 1)]
  [InlineData("var p = s.MintAlive(new object()); var m = new Mutator(s); m.Value += 1; s.Read(p);", 1)]
  [InlineData("var p = s.MintAlive(new object()); var q = new DerivedQuery(p); s.Mutate(); _ = q.Read();", 1)]
  [InlineData("var p = s.MintAlive(new object()); try { s.Mutate(); } catch { } s.Read(p);", 1)]
  [InlineData("var p = s.MintAlive(new object()); try { s.Mutate(); } catch { p = s.MintAlive(new object()); } s.Read(p);", 1)]
  [InlineData("var p = s.MintAlive(new object()); try { s.Mutate(); } finally { p = s.MintAlive(new object()); } s.Read(p);", 0)]
  [InlineData("var p = s.TryGetAlive(new object()); p.IfSome(q => { s.Read(q); s.Mutate(); });", 0)]
  [InlineData("var p = s.MintAlive(new object()); LanguageExt.Prelude.Some(1).IfSome(_ => { s.Read(p); s.Mutate(); });", 0)]
  [InlineData("var p = s.MintAlive(new object()); LanguageExt.Prelude.Some(1).IfSome(_ => { s.Mutate(); s.Read(p); });", 1)]
  [InlineData("var q = new IndexedQuery(s.MintAlive(new object())); _ = q[s.MutationArgument()];", 1)]
  [InlineData("var p = s.MintAlive(new object()); var q = new Query(p) { Value = s.MutationArgument() }; _ = q.Read();", 2)]
  public async Task TracksProofLifetime(string body, int expected)
  {
    var diagnostics = await Analyze(Contract + "class Use { void Run(FunProject.Battle.BattleSession s, bool flag) { " + body + " } }");
    Assert.Equal(expected, diagnostics.Count(d => d.Id == "FPF001"));
    Assert.DoesNotContain(diagnostics, d => d.Id != "FPF001");
  }

  [Theory]
  [InlineData("class Retained(AliveUnit p) { public object Read() => p.State; }", 0)]
  [InlineData("class Retained { private AliveUnit Proof { get; } public Retained(AliveUnit p) { Proof = p; } public object Read() => Proof.State; }", 0)]
  [InlineData("class Retained(AliveUnit p) { public int Read() => p.Position; }", 1)]
  [InlineData("class Retained { private AliveUnit p; public Retained(AliveUnit proof) { p = proof; } public AliveUnit Read() => p; }", 1)]
  public async Task RetentionDistinguishesIdentityFromSnapshot(string declaration, int expected)
  {
    var diagnostics = await Analyze(Contract + declaration + "class Use { void Run(BattleSession s) { var retained = new Retained(s.MintAlive(new object())); s.Mutate(); _ = retained.Read(); } }");
    Assert.Equal(expected, diagnostics.Count(d => d.Id == "FPF001"));
  }

  [Theory]
  [InlineData("Equal(1, 2);", 0)]
  [InlineData("Equal(new FunProject.Core.Vector3I(1, 0, 0), new FunProject.Core.Vector3I(2, 0, 0));", 0)]
  [InlineData("Equal(new Mutating(s), new Mutating(s));", 1)]
  [InlineData("comparer.Equals(1, 2);", 1)]
  [InlineData("_ = $\"{new Mutating(s)}\";", 1)]
  [InlineData("_ = $\"{1}\";", 0)]
  public async Task DefaultScalarEqualityPreservesProofs(string call, int expected)
  {
    var diagnostics = await Analyze(Contract + """
      namespace FunProject.Core { public readonly record struct Vector3I(int X, int Y, int Z); }
      class Mutating(BattleSession s) {
        public override bool Equals(object? other) { s.Mutate(); return false; }
        public override int GetHashCode() => 0;
      }
      class Use {
        static void Equal<T>(T a, T b) {
          if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(a, b))
            throw new InvalidOperationException($"{a} differs from {b}");
        }
        void Run(BattleSession s, System.Collections.Generic.EqualityComparer<int> comparer) {
          var p = s.MintAlive(new object());
      """ + call + "s.Read(p); } }");
    Assert.Equal(expected, diagnostics.Count(d => d.Id == "FPF001"));
  }

  [Fact]
  public async Task ChecksPreservingHelperBody()
  {
    var diagnostics = await Analyze(Contract + "class Use { [FunProject.Battle.PreservesProofs] void Lie(FunProject.Battle.BattleSession s) { s.Mutate(); } }");
    Assert.Contains(diagnostics, d => d.Id == "FPF002");
  }

  [Fact]
  public async Task RejectsUnknownExternalDispatch()
  {
    var diagnostics = await Analyze(Contract + "class Use { void Run(BattleSession s, IDisposable mutator) { var p = s.MintAlive(new object()); mutator.Dispose(); s.Read(p); } }");
    Assert.Contains(diagnostics, d => d.Id == "FPF001");
  }

  [Fact]
  public async Task UnexecutedMintDoesNotRefreshStoredProof()
  {
    var diagnostics = await Analyze(Contract + """
      class Use {
        static AliveUnit cached;
        static BattleSession session = new();
        static AliveUnit Mint() => session.MintAlive(new object());
        static AliveUnit Ignore(Func<AliveUnit> factory) => cached;
        static AliveUnit Fetch() => Ignore(Mint);
        void Run(BattleSession s) { cached = s.MintAlive(new object()); s.Mutate(); var q = Fetch(); s.Read(q); }
      }
      """);
    Assert.Contains(diagnostics, d => d.Id == "FPF001" && d.Location.GetLineSpan().StartLinePosition.Line >= 30);
  }

  [Fact]
  public async Task PreservingPropertyImplementationsAreChecked()
  {
    var diagnostics = await Analyze(Contract + "class LyingProperty(BattleSession s) : PreservingProperty { public int Value { get { s.Mutate(); return 0; } } }");
    Assert.Contains(diagnostics, d => d.Id == "FPF002");
  }

  [Fact]
  public async Task UnknownComparerCanInvalidateUnpassedProof()
  {
    var diagnostics = await Analyze(Contract + "class Use { void Run(BattleSession s, System.Collections.Generic.List<int> list, System.Collections.Generic.IComparer<int> comparer) { var p = s.MintAlive(new object()); list.Sort(comparer); s.Read(p); } }");
    Assert.Contains(diagnostics, d => d.Id == "FPF001");
  }

  [Fact]
  public async Task HelperConstructingMutatorInvalidates()
  {
    var diagnostics = await Analyze(Contract + """
      class Change {
        public Change(BattleSession s) { s.Mutate(); }
        static void Helper(BattleSession s) { _ = new Change(s); }
        void Run(BattleSession s) { var p = s.MintAlive(new object()); Helper(s); s.Read(p); }
      }
      """);
    Assert.Contains(diagnostics, d => d.Id == "FPF001");
  }

  [Fact]
  public async Task PrimaryConstructorInitializerEffectsAreIncluded()
  {
    var diagnostics = await Analyze(Contract + "class Change(BattleSession s) { readonly int n = s.MutationArgument(); static void Helper(BattleSession s) { _ = new Change(s); } void Run(BattleSession s) { var p = s.MintAlive(new object()); Helper(s); s.Read(p); } }");
    Assert.Contains(diagnostics, d => d.Id == "FPF001");
  }

  [Theory]
  [InlineData("class Change { static BattleSession session = new(); readonly int n = session.MutationArgument(); }")]
  [InlineData("class Base { public Base() { new BattleSession().Mutate(); } } class Change : Base { }")]
  [InlineData("class Change { public Change() { } static BattleSession session = new(); readonly int n = session.MutationArgument(); }")]
  [InlineData("class Base(BattleSession s) { readonly int n = s.MutationArgument(); } class Change() : Base(new BattleSession());")]
  public async Task ConstructorEffectsInvalidateUnpassedProof(string declaration)
  {
    var diagnostics = await Analyze(Contract + declaration + "class Use { void Run(BattleSession s) { var p = s.MintAlive(new object()); _ = new Change(); s.Read(p); } }");
    Assert.Contains(diagnostics, d => d.Id == "FPF001");
  }

  [Fact]
  public async Task StringFormattingCanDispatchToMutator()
  {
    var diagnostics = await Analyze(Contract + "class Use { void Run(BattleSession s, object mutator) { var p = s.MintAlive(new object()); _ = string.Format(\"{0}\", mutator); s.Read(p); } }");
    Assert.Contains(diagnostics, d => d.Id == "FPF001");
  }

  [Theory]
  [InlineData("void Run(BattleSession s) { var p = s.MintAlive(new object()); void Read() { s.Read(p); } s.Mutate(); Read(); }")]
  [InlineData("void Run(BattleSession s, AliveUnit p) { void Read() { s.Read(p); } s.Mutate(); Read(); }")]
  [InlineData("void Run(BattleSession s, AliveUnit p) { void Outer() { void Inner() { s.Read(p); } Inner(); } s.Mutate(); Outer(); }")]
  public async Task CapturedLocalFunctionCannotRetainProof(string method)
  {
    var diagnostics = await Analyze(Contract + "class Use { " + method + " }");
    Assert.Contains(diagnostics, d => d.Id == "FPF001");
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task RepeatedCallbackNeedsItsOwnAcquisition(bool reacquire)
  {
    var read = reacquire ? "s.Read(s.MintAlive(new object()));" : "s.Read(p);";
    var diagnostics = await Analyze(Contract + """
      namespace ZLinq {
        public static class Repeated {
          public static bool All(this int[] xs, Func<int, bool> callback) { foreach (var x in xs) if (!callback(x)) return false; return true; }
        }
      }
      """ + "class Use { void Run(BattleSession s) { var p = s.MintAlive(new object()); ZLinq.Repeated.All(new[] { 1, 2 }, _ => { " + read + " s.Mutate(); return true; }); } }");
    Assert.Equal(!reacquire, diagnostics.Any(d => d.Id == "FPF001"));
  }

  [Fact]
  public async Task DeferredLambdaCannotCaptureOuterProofParameter()
  {
    var diagnostics = await Analyze(Contract + "class Use { void Run(BattleSession s, AliveUnit p) { Action later = () => s.Read(p); } }");
    Assert.Contains(diagnostics, d => d.Id == "FPF001");
  }

  [Fact]
  public async Task RepeatedMutatingMethodGroupCannotConsumeProofSequence()
  {
    var diagnostics = await Analyze(Contract + """
      namespace ZLinq {
        public static class Repeated {
          public static bool All(this AliveUnit[] xs, Func<AliveUnit, bool> callback) { foreach (var x in xs) if (!callback(x)) return false; return true; }
        }
      }
      class Holder(BattleSession s) { public bool ReadThenMutate(AliveUnit p) { s.Read(p); s.Mutate(); return true; } }
      class Use { void Run(BattleSession s) { var ps = new[] { s.MintAlive(new object()), s.MintAlive(new object()) }; ZLinq.Repeated.All(ps, new Holder(s).ReadThenMutate); } }
      """);
    Assert.Contains(diagnostics, d => d.Id == "FPF001");
  }

  internal static async Task<ImmutableArray<Diagnostic>> Analyze(string source)
  {
    var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
      .Select(path => MetadataReference.CreateFromFile(path));
    var compilation = CSharpCompilation.Create("Case", [CSharpSyntaxTree.ParseText(source)], references,
      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    var diagnostics = await compilation.WithAnalyzers([new SnapshotProofAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    return diagnostics;
  }
}
