using System.Xml.Linq;

namespace DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Riders;

/// <summary>
///   The few settings the <see cref="SandboxRider"/> needs so that nothing waits for a human: it exits without asking
///   for confirmation, and it trusts the solutions it's given without the "Trust and open?" dialog.
/// </summary>
/// <remarks>
///   Rider writes its settings files when it exits, over whatever was written meanwhile, so these are written only
///   while the sandbox isn't running.
/// </remarks>
sealed class SandboxRiderSettings
{
   readonly string _optionsFolder;

   public SandboxRiderSettings(string configurationFolder) => _optionsFolder = Path.Combine(configurationFolder, "options");

   public void Write(IReadOnlyList<string> trustedFolders)
   {
      Directory.CreateDirectory(_optionsFolder);
      Update("ide.general.xml", "GeneralSettings", component => SetOption(component, "confirmExit", "false"));
      Update(TrustedPathsFile, "Trusted.Paths.Settings", component => TrustEverythingUnder(component, trustedFolders));
   }

   /// <summary>Whether the settings written last trust <paramref name="path"/>: one of the folders they trust holds it.</summary>
   public bool Trust(string path)
   {
      var file = Path.Combine(_optionsFolder, TrustedPathsFile);
      if(!File.Exists(file)) return false;
      var fullPath = Path.GetFullPath(path);
      return XDocument.Load(file).Descendants("option")
                      .Where(it => (string?)it.Attribute("name") == TrustedPathsOption)
                      .Descendants("option")
                      .Select(it => (string?)it.Attribute("value"))
                      .OfType<string>()
                      .Any(folder => fullPath.StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
   }

   const string TrustedPathsFile = "trusted-paths.xml";
   const string TrustedPathsOption = "TRUSTED_PATHS";

   void Update(string fileName, string componentName, Action<XElement> change)
   {
      var file = Path.Combine(_optionsFolder, fileName);
      var settings = File.Exists(file) ? XDocument.Load(file) : new XDocument(new XElement("application"));
      var application = settings.Root ?? throw new InvalidOperationException($"{file} has no root element.");
      var component = application.Elements("component").FirstOrDefault(it => (string?)it.Attribute("name") == componentName);
      if(component is null) application.Add(component = new XElement("component", new XAttribute("name", componentName)));
      change(component);
      settings.Save(file);
   }

   static void SetOption(XElement component, string name, string value)
   {
      component.Elements("option").Where(it => (string?)it.Attribute("name") == name).Remove();
      component.Add(new XElement("option", new XAttribute("name", name), new XAttribute("value", value)));
   }

   static void TrustEverythingUnder(XElement component, IReadOnlyList<string> folders)
   {
      component.Elements("option").Where(it => (string?)it.Attribute("name") == TrustedPathsOption).Remove();
      component.Add(new XElement("option",
                                 new XAttribute("name", TrustedPathsOption),
                                 new XElement("list", folders.Select(folder => new XElement("option", new XAttribute("value", folder))))));
   }
}
