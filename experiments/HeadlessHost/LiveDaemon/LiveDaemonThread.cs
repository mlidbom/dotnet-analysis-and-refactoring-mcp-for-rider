using JetBrains.Application.Components;
using JetBrains.Application.Parts;
using JetBrains.Application.Threading;
using JetBrains.CommandLine.InspectCode.Unattended;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.Util;

namespace Royder.LiveDaemon;

/// <summary>Selects the platform's ordinary daemon thread for the editable solution.</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class LiveDaemonThread(Lifetime lifetime, IShellLocks locks, ILogger logger)
   : DaemonThread(lifetime, locks, logger), IHideImplementation<InspectCodeDaemonThread>;
