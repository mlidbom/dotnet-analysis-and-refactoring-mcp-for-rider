#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.Util;
using JetBrains.Util;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   A project's folders as the namespaces ReSharper expects of the files in them, by the project's root namespace and
///   the folders' names (<see cref="NamespaceFolderUtil"/>, which its CheckNamespace inspection reads): the directory of a
///   namespace's files, which may not be there yet.
/// </summary>
sealed class NamespaceFolders(IProject project)
{
   readonly IProject _project = project;

   /// <summary>
   ///   The directory for files of <paramref name="ns"/>: the folder that expects it, the shallowest of several, or else
   ///   the folders it would take under the deepest folder whose namespace contains it, one for each further part of the
   ///   namespace. Null when <paramref name="ns"/> isn't inside the project's own namespace.
   /// </summary>
   public VirtualFileSystemPath? DirectoryFor(string ns)
   {
      var folders = Folders().Select(it => (Folder: it, Namespace: ExpectedIn(it))).ToList();
      var expecting = folders.Where(it => it.Namespace == ns).OrderBy(it => Depth(it.Folder)).Select(it => it.Folder).FirstOrDefault();
      if(expecting != null)
         return expecting.Location;

      var container = folders.Where(it => it.Namespace.Length == 0 || ns.StartsWith(it.Namespace + ".", StringComparison.Ordinal))
                             .OrderByDescending(it => it.Namespace.Length)
                             .FirstOrDefault();
      return container.Folder?.Location is {} location
                ? ns.Substring(container.Namespace.Length).Split(['.'], StringSplitOptions.RemoveEmptyEntries).Aggregate(location, (path, part) => path.Combine(part))
                : null;
   }

   IEnumerable<IProjectFolder> Folders()
   {
      var folders = new List<IProjectFolder>();
      Add(_project);
      return folders;

      void Add(IProjectFolder folder)
      {
         folders.Add(folder);
         foreach(var subfolder in folder.GetSubFolders()) Add(subfolder);
      }
   }

   static string ExpectedIn(IProjectFolder folder) => NamespaceFolderUtil.CalculateExpectedNamespace(folder, CSharpLanguage.Instance!, false) ?? "";

   static int Depth(IProjectFolder folder) => folder.GetParentFolders().Count();
}
