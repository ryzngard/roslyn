﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Remote;
using Microsoft.CodeAnalysis.Shared.TestHooks;

namespace Microsoft.VisualStudio.LanguageServices.Remote;

/// <summary>
/// Connects <see cref="VisualStudioWorkspace"/> to the ServiceHub services.
/// Launches ServiceHub if it is not running yet and starts services that push information from <see cref="VisualStudioWorkspace"/> to the ServiceHub process.
/// </summary>
[ExportEventListener(WellKnownEventListeners.Workspace, WorkspaceKind.Host), Shared]
[method: ImportingConstructor]
[method: Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
internal sealed class VisualStudioWorkspaceServiceHubConnector(
    IAsynchronousOperationListenerProvider listenerProvider) : IEventListener
{
    private readonly IAsynchronousOperationListenerProvider _listenerProvider = listenerProvider;
    private readonly CancellationTokenSource _disposalCancellationSource = new();

    private Task<RemoteHostClient?>? _remoteClientInitializationTask;
    private SolutionChecksumUpdater? _checksumUpdater;

    public void StartListening(Workspace workspace)
    {
        if (workspace is not VisualStudioWorkspace)
        {
            return;
        }

        // only push solution snapshot from primary (VS) workspace:
        _checksumUpdater = new SolutionChecksumUpdater(workspace, _listenerProvider, _disposalCancellationSource.Token);

        // start launching remote process, so that the first service that needs it doesn't need to wait for it:
        var service = workspace.Services.GetRequiredService<IRemoteHostClientProvider>();
        _remoteClientInitializationTask = service.TryGetRemoteHostClientAsync(_disposalCancellationSource.Token);
    }

    public void StopListening(Workspace workspace)
    {
        if (workspace is not VisualStudioWorkspace)
        {
            return;
        }

        _disposalCancellationSource.Cancel();
        _disposalCancellationSource.Dispose();

        _checksumUpdater?.Shutdown();

        // dispose remote client if its initialization has completed:
        _remoteClientInitializationTask?.ContinueWith(
            previousTask => previousTask.Result?.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }
}
