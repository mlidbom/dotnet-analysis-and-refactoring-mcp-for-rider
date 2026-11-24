#nullable enable
using System;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Impl;
using JetBrains.ReSharper.Psi.Tree;

namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>
///   The code a call is written in, which an <see cref="IncomingCallTree"/> lists as a caller of the member it calls:
///   a <see cref="CallTrees.Callee"/>'s code, whose own callers the tree goes on to, or code that runs as part of
///   something else, such as a field's initializer, which it names by the member it is written in.
/// </summary>
public sealed class Caller : IEquatable<Caller>
{
   /// <summary>What a call runs to run this code; nothing for code that no call runs by itself, such as an initializer.</summary>
   public Callee? Callee { get; }

   /// <summary>The member the code is written in: the <see cref="Callee"/>'s, or else the field, property or type around it.</summary>
   public IDeclaredElement Member { get; }

   Caller(Callee? callee, IDeclaredElement member)
   {
      Callee = callee;
      Member = member;
   }

   /// <summary>The code of <paramref name="callee"/>, whose own callers a tree can go on to.</summary>
   public static Caller Of(Callee callee) => new(callee, callee.Member);

   /// <summary>
   ///   The caller whose code holds <paramref name="node"/>; nothing for code in no type's member, such as a top-level
   ///   statement.
   /// </summary>
   public static Caller? Around(ITreeNode node) =>
      Callee.WhoseCodeHolds(node) is { } callee ? Of(callee) : RunAsPartOfTheMemberAround(node);

   /// <summary>
   ///   Code that runs as part of the member around <paramref name="node"/>, such as a field's initializer, which no
   ///   call runs by itself; nothing outside every type's member.
   /// </summary>
   static Caller? RunAsPartOfTheMemberAround(ITreeNode node) =>
      node.GetContainingNode<ITypeMemberDeclaration>()?.DeclaredElement is { } member ? new Caller(callee: null, member) : null;

   /// <summary>Its name as a call tree writes it: the <see cref="CallTrees.Callee.Name"/>, or the member's qualified name.</summary>
   public string Name => Callee?.Name ?? PsiHelpers.GetQualifiedName(Member);

   /// <summary>Where the member it is written in is declared.</summary>
   public DeclarationSite? DeclarationSite => DeclarationSite.WhereWritten(Member);

   public bool Equals(Caller? other) =>
      other != null && Equals(Callee, other.Callee) && DeclaredElementEqualityComparer.ElementComparer.Equals(Member, other.Member);

   public override bool Equals(object? other) => other is Caller caller && Equals(caller);

   public override int GetHashCode() => Callee?.GetHashCode() ?? DeclaredElementEqualityComparer.ElementComparer.GetHashCode(Member);
}
