#nullable enable
using System;
using JetBrains.Util;
using JetBrains.Util.Logging;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   A ReSharper crash as one line an agent can act on. The stack trace goes to the IDE log, not the caller.
/// </summary>
public static class ToolFailure
{
   static readonly ILogger Log = Logger.GetLogger(typeof(ToolFailure));

   /// <summary>
   ///   <see cref="Line"/>, with the stack trace written to the IDE log.
   /// </summary>
   public static string Logged(Exception exception, string subject)
   {
      // As text: an exception kept keeps loaded every tools build its stack trace runs through.
      Log.Warn($"DotNetAnalysisAndRefactoringMcp failed on {subject}: {exception}");

      return Line(exception, subject);
   }

   public static string Line(Exception exception, string subject)
   {
      var cause = exception is AggregateException { InnerExceptions.Count: 1 } aggregate
                     ? aggregate.InnerExceptions[0]
                     : exception;
      var message = FirstLine(cause.Message);

      return $"ReSharper failed on {subject}: {cause.GetType().Name}{(message.Length > 0 ? $": {message}" : "")}";
   }

   static string FirstLine(string message)
   {
      var end = message.IndexOfAny(new[] { '\r', '\n' });

      return (end < 0 ? message : message.Substring(0, end)).Trim();
   }
}
