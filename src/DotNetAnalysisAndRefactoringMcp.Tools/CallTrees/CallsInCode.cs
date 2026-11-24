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
///   with <c>new</c>, a constructor's <c>: base(...)</c>, an indexer, a property with code, read, written or set in an
///   object initializer, a user-defined operator, and a method passed as a delegate.
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
      if(IsNoCode(node) || IsTheCodeOfACalleeOfItsOwn(node) || !callee.Runs(node))
         return;

      if(ReferenceAt(node) is { } reference && CallThrough(reference, node) is { Callee.MayRunCode: true } call)
         written.Add((reference.GetTreeTextRange().StartOffset.Offset, call));

      for(var child = node.FirstChild; child != null; child = child.NextSibling)
         Walk(child, callee, written);
   }

   /// <summary>
   ///   The call that <paramref name="reference"/>, which a search found, makes where it is written, as <see cref="Of"/>
   ///   finds it in the code around it, whether or not the member it calls has code: an auto-property's readers are
   ///   its callers too. Nothing for a reference that calls nothing, such as one in a doc comment or a <c>nameof</c>.
   /// </summary>
   public static Call? MadeThrough(IReference reference)
   {
      var node = reference.GetTreeNode();
      return ReferenceAt(node) != null && !IsInNoCode(node) ? CallThrough(reference, node) : null;
   }

   /// <summary>What is written in a member's declaration but doesn't run as code: a doc comment, an attribute, a <c>nameof</c>.</summary>
   static bool IsNoCode(ITreeNode node) =>
      node is IDocCommentBlock or IAttributeSectionList
   || node is IInvocationExpression invocation && invocation.IsNameofOperator();

   /// <summary>Whether <paramref name="node"/> is, or is inside, what <see cref="IsNoCode"/> is.</summary>
   static bool IsInNoCode(ITreeNode node)
   {
      for(var it = node; it != null; it = it.Parent)
         if(IsNoCode(it))
            return true;
      return false;
   }

   /// <summary>A local function's body, which runs when it is called, as a callee of its own.</summary>
   static bool IsTheCodeOfACalleeOfItsOwn(ITreeNode node) => node is ILocalFunctionDeclaration;

   /// <summary>
   ///   The reference through which <paramref name="node"/> may call: a name, a <c>new</c>, a constructor's
   ///   <c>: base(...)</c>, an indexer, an operator, a property set in an object initializer, and a primary
   ///   constructor's call of its base type's, <c>class DetailedSketch() : Sketch(new Circle(1))</c>.
   /// </summary>
   static IReference? ReferenceAt(ITreeNode node) =>
      node switch
      {
         IReferenceExpression reference                    => reference.Reference,
         IObjectCreationExpression creation                => creation.ConstructorReference,
         IConstructorInitializer initializer               => initializer.Reference,
         IElementAccessExpression access                   => access.ElementAccessReference,
         IOperatorExpression operation                     => operation.OperatorReference,
         IMemberInitializer initializer                    => initializer.Reference,
         IExtendedType { ArgumentList: not null } baseType => baseType.ConstructorReference,
         _                                                 => null
      };

   static Call? CallThrough(IReference reference, ITreeNode node) =>
      reference.Resolve().DeclaredElement switch
      {
         IFunction function when !IsADelegatesInvoke(function) => new Call(Callee.OfFunction(function), IsPassedAsADelegate(node), IsThroughBase(node)),
         ILocalFunction localFunction                          => new Call(Callee.OfLocalFunction(localFunction), IsPassedAsADelegate(node), isThroughBase: false),
         IProperty property                                    => new Call(Callee.OfProperty(property, AccessorsRunBy(node)), asDelegate: false, IsThroughBase(node)),
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
   ///   The accessors that <paramref name="node"/>, a reference to a property, runs: as the expression is read or written,
   ///   or for <c>Title = ...</c> in an object initializer, the setter; for <c>Bounds = { ... }</c>, which sets the
   ///   members of the object the property holds, the getter.
   /// </summary>
   static Accessors AccessorsRunBy(ITreeNode node) =>
      node switch
      {
         ICSharpExpression expression                => AccessorsRunBy(expression.GetAccessType(new UniversalContext(expression))),
         IMemberInitializer { Initializer: not null } => Accessors.Get,
         _                                           => Accessors.Set
      };

   static Accessors AccessorsRunBy(ExpressionAccessType access) =>
      (access & ExpressionAccessType.Write) == 0 ? Accessors.Get
    : (access & ExpressionAccessType.Read) == 0  ? Accessors.Set
                                                 : Accessors.GetAndSet;
}
