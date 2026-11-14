using System.Text.Json;

namespace ReSharperMcp.DevelopmentTools.Riders;

/// <summary>
///   The Rider installed on this machine, which both the <see cref="SandboxRider"/> and the <see cref="EverydayRider"/>
///   run.
/// </summary>
sealed class RiderInstallation
{
  readonly string _installationFolder;

  RiderInstallation(string installationFolder) => _installationFolder = installationFolder;

  /// <summary>The Rider that JetBrains Toolbox and Rider's own installer put in the user's programs folder.</summary>
  public static RiderInstallation OfThisUser() =>
    new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Rider"));

  /// <summary>The launcher that starts Rider's frontend, which in turn starts its .NET backend.</summary>
  public string Launcher => Path.Combine(_installationFolder, "bin", "rider64.exe");

  /// <summary>The Java runtime Rider ships with, which runs the Gradle build of the plugin's frontend jar.</summary>
  public string BundledJavaRuntime => Path.Combine(_installationFolder, "jbr");

  /// <summary>
  ///   The name of this version's settings folder, such as <c>Rider2026.3</c>, under which the
  ///   <see cref="EverydayRider"/> keeps its plugins.
  /// </summary>
  public string SettingsFolderName
  {
    get
    {
      using var productInformation = JsonDocument.Parse(File.ReadAllText(Path.Combine(_installationFolder, "product-info.json")));
      return productInformation.RootElement.GetProperty("dataDirectoryName").GetString()
             ?? throw new InvalidOperationException($"{_installationFolder}\\product-info.json has no dataDirectoryName.");
    }
  }
}
