namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   A tool that runs as Rider runs its refactorings: on ReSharper's main thread, under a read lock, with the documents
///   committed, and in no PSI transaction of the call's. What it changes takes the write lock where ReSharper's own code
///   takes it, and a transaction it opens itself.
/// </summary>
/// <remarks>
///   <para>
///     Not under the write lock, as an <see cref="IMcpSelfTransactingWriteTool"/> runs: ReSharper's searches run on
///     several threads at once, and while the main thread holds the write lock, their threads can't read. The search
///     ends in an <c>OperationCanceledException</c>. Until 2026-11-18 rename_symbol ran so, and every rename of a type
///     in a project that references ASP.NET Core failed when ReSharper hadn't yet cached the project's controllers, as
///     Rename asks for them to find the files to rename with the type.
///   </para>
///   <para>
///     Rider's <c>WorkflowExecuter</c> runs a refactoring's <c>Execute</c> under a <c>ReadLockCookie</c>, in a PSI
///     transaction, on the main thread.
///   </para>
/// </remarks>
public interface IMcpMainThreadTool : IMcpTool {}
