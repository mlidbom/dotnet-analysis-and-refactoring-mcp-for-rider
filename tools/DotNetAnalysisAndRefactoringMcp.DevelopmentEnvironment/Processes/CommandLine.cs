using System.Text;

namespace DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Processes;

/// <summary>
///   A Windows command line: the one string that <c>CreateProcess</c> takes for a program and its arguments, which the
///   started program splits into its arguments again.
/// </summary>
static class CommandLine
{
   /// <summary>
   ///   <paramref name="program"/> and <paramref name="arguments"/>, each quoted where it needs to be, so that the program
   ///   reads back exactly these arguments.
   /// </summary>
   public static string Of(string program, IEnumerable<string> arguments) => string.Join(' ', arguments.Prepend(program).Select(Quoted));

   /// <summary>
   ///   <paramref name="argument"/> as Microsoft's C runtime splits a command line: in quotes when it is empty or holds a
   ///   space, a tab or a quote; a quote inside escaped by a backslash, and the backslashes before a quote doubled.
   /// </summary>
   static string Quoted(string argument)
   {
      if(argument.Length > 0 && !argument.Any(it => it is ' ' or '\t' or '"')) return argument;

      var quoted = new StringBuilder("\"");
      var backslashes = 0;
      foreach(var character in argument)
      {
         if(character == '\\')
         {
            backslashes++;
            continue;
         }

         quoted.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes).Append(character);
         backslashes = 0;
      }

      return quoted.Append('\\', backslashes * 2).Append('"').ToString();
   }
}
