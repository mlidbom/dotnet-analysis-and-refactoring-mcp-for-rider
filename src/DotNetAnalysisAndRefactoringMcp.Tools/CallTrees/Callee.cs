#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Application.Progress;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.DeclaredElements;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Impl;
using JetBrains.ReSharper.Psi.Search;
using JetBrains.ReSharper.Psi.Tree;

namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>
///   What a call runs, which a <see cref="OutgoingCallTreeNode"/> shows: a method, a constructor, an operator or a local
///   function, or the <see cref="CallTrees.Accessors"/> of a property or an indexer that a call reads or writes it through.
/// </summary>
public sealed class Callee : IEquatable<Callee>
{
   /// <summary>An <see cref="IFunction"/>, an <see cref="ILocalFunction"/>, or an <see cref="IProperty"/>, indexers included.</summary>
   public IDeclaredElement Member { get; }

   /// <summary>The accessors a call runs, for a property; <see cref="CallTrees.Accessors.None"/> for a function.</summary>
   public Accessors Accessors { get; }

   Callee(IDeclaredElement member, Accessors accessors)
   {
      Member = member;
      Accessors = accessors;
   }

   public static Callee OfFunction(IFunction function) => new(function, Accessors.None);

   /// <summary>A local function, which ReSharper doesn't model as an <see cref="IFunction"/>.</summary>
   public static Callee OfLocalFunction(ILocalFunction function) => new(function, Accessors.None);

   public static Callee OfProperty(IProperty property, Accessors accessors) => new(property, accessors);

   /// <summary>
   ///   Where asking for the calls of <paramref name="element"/> starts: a function, or a property with every accessor it
   ///   has; nothing for another kind of element, such as a type or a field.
   /// </summary>
   public static Callee? StartingAt(IDeclaredElement element) =>
      element switch
      {
         IFunction function           => OfFunction(function),
         ILocalFunction localFunction => OfLocalFunction(localFunction),
         IProperty property           => OfProperty(property, (property.Getter != null ? Accessors.Get : Accessors.None) | (property.Setter != null ? Accessors.Set : Accessors.None)),
         _                            => null
      };

   /// <summary>
   ///   Its name as a call tree writes it: <c>Shapes.Palette.Pick</c>; with the parameters when it has overloads,
   ///   <c>Shapes.Palette.Mix(first:string, second:string)</c>; <c>new Shapes.Circle</c> for a constructor,
   ///   <c>Shapes.Outline.this[index:int]</c> for an indexer, <c>Shapes.Drawing.Sketch.Draw.Nesting</c> for a local
   ///   function in <c>Draw</c>; and <c>(set)</c> after a property that the call writes.
   /// </summary>
   public string Name => NameOfTheMember() + Accessors switch
   {
      Accessors.Set       => " (set)",
      Accessors.GetAndSet => " (get and set)",
      _                   => ""
   };

   /// <summary>Where it is declared, <c>Shapes\Palette.cs:9</c>, the path relative to <paramref name="root"/>; the assembly of a compiled one.</summary>
   public string Location(SolutionRoot root) =>
      DeclarationSite.WhereWritten(Member) is { File.Length: > 0 } site
         ? $"{root.Relative(site.File)}:{site.Line}"
         : PsiHelpers.AssemblyLabel(Member) ?? "[no source]";

   /// <summary>
   ///   Whether it is declared in the solution's source, or, for an implicit constructor, which has no declaration, its
   ///   type is: a <c>new</c> of it runs the type's field initializers.
   /// </summary>
   public bool IsInSource => Member.GetSourceFiles().Any() || IsTheImplicitConstructorOfATypeInSource;

   bool IsTheImplicitConstructorOfATypeInSource =>
      Member is IConstructor { IsImplicit: true } constructor && constructor.GetContainingType() is { } type && type.GetSourceFiles().Any();

   /// <summary>
   ///   The name of the compiled assembly it is in, such as <c>System.Linq</c>; nothing for one in source, nor for one
   ///   the compiler makes up, such as the <c>+</c> of two integers.
   /// </summary>
   public string? AssemblyName => Member is ICompiledElement ? PsiHelpers.AssemblyNameOf(Member) : null;

   /// <summary>
   ///   Its declarations whose code runs when it is called: a function's, a property's or an indexer's, in C#. None for a
   ///   compiled member, nor for one whose code is no declaration of its own, such as a primary constructor.
   /// </summary>
   public IReadOnlyList<IDeclaration> Code =>
      [.. Member.GetDeclarations().Where(it => it is ICSharpFunctionDeclaration or ILocalFunctionDeclaration or IAccessorOwnerDeclaration)];

   /// <summary>
   ///   Whether <paramref name="node"/>, within its <see cref="Code"/>, runs when it is called: not the accessors of a
   ///   property that the call doesn't run, nor a property's initializer, which runs when its type is constructed.
   /// </summary>
   public bool Runs(ITreeNode node) =>
      node switch
      {
         IAccessorDeclaration accessor          => Accessors.HasFlag(accessor.Kind == AccessorKind.GETTER ? Accessors.Get : Accessors.Set),
         _ when IsAPropertysInitializer(node)   => false,
         _ when IsAGetterWrittenAsAnArrow(node) => Accessors.HasFlag(Accessors.Get),
         _                                      => true
      };

   static bool IsAPropertysInitializer(ITreeNode node) => node.Parent is IPropertyDeclaration property && node == property.Initial;

   /// <summary>The <c>=&gt; ...</c> of <c>public string Caption =&gt; ...</c>: its getter, which has no declaration of its own.</summary>
   static bool IsAGetterWrittenAsAnArrow(ITreeNode node) =>
      node.Parent is IAccessorOwnerDeclaration and IExpressionBodyOwnerDeclaration owner && node == owner.ArrowClause;

   /// <summary>
   ///   Whether a call to it can run another member's code instead: an interface's member, an abstract or a virtual one,
   ///   or an override that isn't sealed.
   /// </summary>
   public bool CanBeOverridden =>
      Member is IOverridableMember member
   && (IsAnInterfacesMember || member.IsAbstract || member.IsVirtual || member is { IsOverride: true, IsSealed: false });

   /// <summary>Whether it is an interface's member, whose overriders are its implementations.</summary>
   public bool IsAnInterfacesMember => Member is IClrDeclaredElement member && member.GetContainingType() is IInterface;

   /// <summary>The members that implement or override it, as ReSharper finds them, each run through the same accessors.</summary>
   public IReadOnlyList<Callee> Overriders()
   {
      var member = (IOverridableMember)Member;
      var overriders = new List<Callee>();
      //refactor: the same search as FindImplementationsTool's and FlowTool.FindSingleImplementation. One type, such
      //   as Overriders.Of(member) beside SuperMembers, should search, with a cap and a stop, for all three.
      member.GetPsiServices().Finder.FindImplementingMembers(
         member,
         member.GetSearchDomain(),
         new FindResultConsumer(result =>
         {
            if(result is FindResultOverridableMember { OverridableMember: { } overrider })
               overriders.Add(new Callee(overrider, Accessors));
            return FindExecution.Continue;
         }),
         searchQuasi: true,
         NullProgressIndicator.Create());
      return overriders;
   }

   public bool Equals(Callee? other) =>
      other != null && Accessors == other.Accessors && DeclaredElementEqualityComparer.ElementComparer.Equals(Member, other.Member);

   public override bool Equals(object? other) => other is Callee callee && Equals(callee);

   public override int GetHashCode() => DeclaredElementEqualityComparer.ElementComparer.GetHashCode(Member) * 4 + (int)Accessors;

   string NameOfTheMember() =>
      Member switch
      {
         IConstructor constructor                       => $"new {PsiHelpers.GetQualifiedName(constructor.GetContainingType()!)}{ParametersIfOverloaded()}",
         IProperty { Parameters.Count: > 0 } indexer   => $"{PsiHelpers.GetQualifiedName(indexer.GetContainingType()!)}.this[{Signature.ParametersOf(indexer)}]",
         ILocalFunction function                        => $"{NameOfTheMemberItIsIn(function)}.{function.ShortName}",
         _                                              => PsiHelpers.GetQualifiedName(Member) + ParametersIfOverloaded()
      };

   /// <summary>
   ///   The qualified name of the member a local function is declared in, <c>Shapes.Drawing.Sketch.Draw</c>: ReSharper
   ///   qualifies it with the type, as if it were the type's member.
   /// </summary>
   static string NameOfTheMemberItIsIn(ILocalFunction function) =>
      function.GetDeclarations().FirstOrDefault()?.GetContainingNode<ITypeMemberDeclaration>()?.DeclaredElement is { } member
         ? PsiHelpers.GetQualifiedName(member)
         : PsiHelpers.GetQualifiedName(function);

   string ParametersIfOverloaded() =>
      Member is IParametersOwner owner && Overloads.Of(Member).Count > 1 ? $"({Signature.ParametersOf(owner)})" : "";
}
