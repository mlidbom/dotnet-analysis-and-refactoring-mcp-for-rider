using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Plugin;

/// <summary>
///   The plugin's frontend jar: the descriptor, whose <c>META-INF/plugin.xml</c> makes Rider load the host, and the
///   status-bar widget. One build of it is told from another by what Rider reads from it: its entries, by name and
///   content.
/// </summary>
/// <remarks>
///   Not by its bytes: two builds of the same sources differed in the zip's own records, with every entry the same
///   (2026-11-17). And not by the descriptor's change notes, which Gradle fills in from CHANGELOG.md: an edit there
///   doesn't change how Rider runs the plugin, and installing the plugin waits for a restart of every everyday Rider.
/// </remarks>
static class FrontendJar
{
   const string Descriptor = "META-INF/plugin.xml";

   static readonly Regex ChangeNotes = new("<change-notes>.*?</change-notes>", RegexOptions.Singleline);

   /// <summary>The SHA-256 of <paramref name="jar"/>'s entries, by name and content, without the descriptor's change notes.</summary>
   public static string IdentityOf(string jar)
   {
      using var archive = ZipFile.OpenRead(jar);
      using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
      foreach(var entry in archive.Entries.OrderBy(it => it.FullName, StringComparer.Ordinal))
      {
         hash.AppendData(Encoding.UTF8.GetBytes(entry.FullName + "\0"));
         hash.AppendData(ContentOf(entry));
      }

      return Convert.ToHexStringLower(hash.GetHashAndReset());
   }

   static byte[] ContentOf(ZipArchiveEntry entry)
   {
      using var stream = entry.Open();
      using var content = new MemoryStream();
      stream.CopyTo(content);
      return entry.FullName == Descriptor
                ? Encoding.UTF8.GetBytes(ChangeNotes.Replace(Encoding.UTF8.GetString(content.ToArray()), ""))
                : content.ToArray();
   }
}
