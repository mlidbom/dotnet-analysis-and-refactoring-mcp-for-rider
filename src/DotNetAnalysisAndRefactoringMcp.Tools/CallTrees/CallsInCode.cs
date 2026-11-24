#nullable enable
using System.Collections.Generic;
using System.Linq;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.DeclaredElements;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.ExtensionsAPI.Resolve.Managed;
using JetBrains.ReSharper.Psi.Resolve;
using JetBrains.ReSharper.Psi.Tree;

namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>
///   The calls written in a <see cref="Callee"/>'s code, in the order their names are written, each callee once: through a name,
///   with <c>new</c>, a constructor's <c>: base(...)</c>, an indexer, a property with code, a user-defined operator,
///   and a method passed as a delegate.
/// </summary>
/// <remarks>
///   Until 2026-11-24 the outgoing tree followed only calls through a name, which missed constructors, properties,
///   indexers and operators.
///   <br/>Not what is written but doesn't run as the callee's code: doc comments, attributes, <c>nameof</c>, and a local
///   function's body, which runs as a callee of its own. A lambda's body is the callee's: it is written there, to be run
///   by what it is passed to.
///   <br/>Not yet the calls C# makes without their being written: <c>Dispose</c> at the end of a <c>using</c>,
///   <c>GetEnumerator</c> in a <c>foreach</c>, <c>Deconstruct</c>, an implicit conversion. Nor a delegate's
///   <c>Invoke</c>: what it runs is known only when it runs.
/// </remarks>
public static class CallsInCode
{
   public static IReadOnlyList<Call> Of(Callee callee)
   {
      var calls = new List<Call>();
      var callees = new HashSet<Callee>();
      foreach(var declaration in callee.Code)
      {
         var written = new List<(int Offset, Call Call)>();
         for(var child = declaration.FirstChild; child != null; child = child.NextSibling)
            Walk(child, callee, written);

         foreach(var (_, call) in written.OrderBy(it => it.Offset))
            if(callees.Add(call.Callee))
               calls.Add(call);
      }

      return calls;
   }

   /// <summary>
   ///   Collects the calls in <paramref name="node"/>'s subtree, each at the offset of its reference: the name of a
   ///   method, the type of a <c>new</c>, the sign of an operator. The walk meets the outer call first:
   ///   <c>new Palette().Mix(...)</c> is a reference to <c>Mix</c> around the <c>new</c>.
   /// </summary>
   static void Walk(ITreeNode node, Callee callee, List<(int Offset, Call Call)> written)
   {
      if(IsNoPartOfTheCodeThatRuns(node) || !callee.Runs(node))
         return;

      if(ReferenceAt(node) is { } reference && CallThrough(reference, node) is { } call)
         written.Add((reference.GetTreeTextRange().StartOffset.Offset, call));

      for(var child = node.FirstChild; child != null; child = child.NextSibling)
         Walk(child, callee, written);
   }

   static bool IsNoPartOfTheCodeThatRuns(ITreeNode node) =>
      node is IDocCommentBlock or IAttributeSectionList or ILocalFunctionDeclaration
   || node is IInvocationExpression invocation && invocation.IsNameofOperator();

   /// <summary>The reference through which <paramref name="node"/> may call: a name, a <c>new</c>, a constructor's <c>: base(...)</c>, an indexer, an operator.</summary>
   static IReference? ReferenceAt(ITreeNode node) =>
      node switch
      {
         IReferenceExpression reference      => reference.Reference,
         IObjectCreationExpression creation  => creation.ConstructorReference,
         IConstructorInitializer initializer => initializer.Reference,
         IElementAccessExpression access     => access.ElementAccessReference,
         IOperatorExpression operation       => operation.OperatorReference,
         _                                   => null
      };

   static Call? CallThrough(IReference reference, ITreeNode node) =>
      reference.Resolve().DeclaredElement switch
      {
         IFunction function when !IsADelegatesInvoke(function) => new Call(Callee.OfFunction(function), IsPassedAsADelegate(node), IsThroughBase(node)),
         ILocalFunction localFunction                          => new Call(Callee.OfLocalFunction(localFunction), IsPassedAsADelegate(node), isThroughBase: false),
         IProperty property when MayRunCode(property)         => new Call(Callee.OfProperty(property, AccessorsRunBy((ICSharpExpression)node)), asDelegate: false, IsThroughBase(node)),
         _                                                     => null
      };

   /// <summary>Whether <paramref name="node"/> names a method without calling it: <c>shapes.Select(AreaOf)</c>.</summary>
   static bool IsPassedAsADelegate(ITreeNode node) =>
      node is IReferenceExpression reference && InvocationExpressionNavigator.GetByInvokedExpression(reference) == null;

   static bool IsThroughBase(ITreeNode node) =>
      node switch
      {
         IReferenceExpression reference  => reference.QualifierExpression is IBaseExpression,
         IElementAccessExpression access => access.Operand is IBaseExpression,
         _                               => false
      };

   static bool IsADelegatesInvoke(IFunction function) => function is IClrDeclaredElement member && member.GetContainingType() is IDelegate;

   /// <summary>
   ///   Whether reading or writing <paramref name="property"/> may run code: not for an auto-property, unless its overrides
   ///   or implementations may have some.
   /// </summary>
   static bool MayRunCode(IProperty property) =>
      !property.IsAuto || property.IsAbstract || property.IsVirtual || property.IsOverride || property.GetContainingType() is IInterface;

   static Accessors AccessorsRunBy(ICSharpExpression expression)
   {
      var access = expression.GetAccessType(new UniversalContext(expression));
      return (access & ExpressionAccessType.Write) == 0 ? Accessors.Get
           : (access & ExpressionAccessType.Read) == 0  ? Accessors.Set
                                                        : Accessors.GetAndSet;
   }
}
