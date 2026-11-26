using System.Text.Json;
using Royder.Primary.Hosting;

namespace Royder.Primary.Worktrees;

/// <summary>Records which Git generation a private Rider project belongs to across an unexpected primary exit.</summary>
sealed class WorktreeJournal(string profile)
{
   readonly string _path = Path.Combine(profile, "worktrees.json");
   readonly Dictionary<string, Project> _projects = new(StringComparer.OrdinalIgnoreCase);
   public sealed record Project(RegisteredWorktree Registration, string Solution);

   public IEnumerable<Project> Read()
   {
      _projects.Clear();
      if(File.Exists(_path))
         foreach(var it in JsonSerializer.Deserialize<Project[]>(File.ReadAllText(_path)) ?? throw new InvalidDataException("Invalid worktree journal."))
            _projects.Add(it.Registration.Root, it);
      return _projects.Values.ToArray();
   }

   public async Task Recover(SharedRiderHost host, PrimarySettings settings, CancellationToken cancel)
   {
      var projects = Read().ToArray();
      foreach(var it in projects)
         if(RegisteredWorktree.Find(it.Registration.Root, settings.Trusted) != it.Registration)
            await host.CloseAsync(it.Solution, true, cancel);
      await host.StopAsync(cancel);
      if(projects.Length > 0) { _projects.Clear(); Save(); }
   }

   public void Opening(RegisteredWorktree registration, string solution)
   {
      _projects[registration.Root] = new(registration, solution);
      Save();
   }

   public void Closed(RegisteredWorktree registration)
   {
      _projects.Remove(registration.Root);
      Save();
   }

   void Save()
   {
      var pending = _path + ".pending";
      File.WriteAllText(pending, JsonSerializer.Serialize(_projects.Values));
      File.Move(pending, _path, true);
   }

   public static FileStream OwnProfile(string profile)
   {
      Directory.CreateDirectory(profile);
      return File.Open(Path.Combine(profile, "primary.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
   }
}
