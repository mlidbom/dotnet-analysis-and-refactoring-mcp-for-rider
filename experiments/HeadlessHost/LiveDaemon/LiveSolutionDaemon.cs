using JetBrains.Application.changes;
using JetBrains.Application.Components;
using JetBrains.Application.Parts;
using JetBrains.Application.Settings;
using JetBrains.Application.Threading;
using JetBrains.Application.UI.Actions.ActionManager;
using JetBrains.Application.UI.Controls.JetPopupMenu;
using JetBrains.CommandLine.InspectCode.Unattended;
using JetBrains.DocumentManagers;
using JetBrains.DocumentManagers.Transactions;
using JetBrains.DocumentModel;
using JetBrains.IDE.Common;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.Interfaces;
using JetBrains.ReSharper.Daemon;
using JetBrains.ReSharper.Daemon.Impl;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Feature.Services.Pencils;
using JetBrains.ReSharper.Feature.Services.TextControl;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Files;
using JetBrains.ReSharper.Psi.GeneratedCode;
using JetBrains.TextControl;
using JetBrains.TextControl.DocumentMarkup;

namespace Royder.LiveDaemon;

/// <summary>Selects <see cref="DaemonImpl"/> in place of <see cref="InspectCodeDaemonManual"/>
/// so that document changes and PSI transactions receive the platform daemon's ordinary handling.</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class LiveSolutionDaemon(
   ISolution solution,
   IPsiServices psiServices,
   DocumentManager documentManager,
   IShellLocks locks,
   ITextControlManager textControlManager,
   InvisibleTextControlsTracker invisibleTextControls,
   ChangeManager changeManager,
   HighlightingSettingsManager highlightingSettingsManager,
   DaemonExcludedFilesManager excludedFilesManager,
   SolutionAnalysisService solutionAnalysisService,
   IDocumentMarkupManager documentMarkupManager,
   AsyncCommitService asyncCommitService,
   ISolutionAnalysisIndicator solutionAnalysisIndicator,
   ISettingsStore settingsStore,
   IDaemonThread daemonThread,
   DaemonEnablePolicy daemonEnablePolicy,
   SolutionDocumentTransactionManager solutionDocumentTransactionManager,
   SolutionAnalysisConfiguration solutionAnalysisConfiguration,
   DocumentToProjectFileMappingStorage documentStorage,
   ProjectContext projectContext,
   ProjectModelUpdateStates projectModelUpdateStates,
   IHighlightingAttributeRegistry registry,
   PencilsManager pencilsManager,
   IHighlightingsCommitterEventsManager highlightingsCommitterEventsManager,
   ModifiedFilesCacheForDaemon modifiedFilesCache,
   IEditorManager editorManager,
   HighlighterCountsByKindsTrackerComponent highlighterCountsByKindsTrackerComponent,
   IActionManager actionManager,
   JetPopupMenus popupMenus,
   ChunkedMarkAsDirty chunkedMarkAsDirty,
   IReadyImmutableList<IDaemonResetOnCodeStyleChangeOverrider> resetOverriders)
   : DaemonImpl(
      solution, psiServices, documentManager, locks,
      textControlManager, invisibleTextControls, changeManager, highlightingSettingsManager,
      excludedFilesManager, solutionAnalysisService, documentMarkupManager, asyncCommitService,
      solutionAnalysisIndicator, settingsStore, daemonThread, daemonEnablePolicy,
      solutionDocumentTransactionManager, solutionAnalysisConfiguration, documentStorage, projectContext,
      projectModelUpdateStates, registry, pencilsManager, highlightingsCommitterEventsManager,
      modifiedFilesCache, editorManager, highlighterCountsByKindsTrackerComponent, actionManager,
      popupMenus, chunkedMarkAsDirty, resetOverriders), IHideImplementation<InspectCodeDaemonManual>;
