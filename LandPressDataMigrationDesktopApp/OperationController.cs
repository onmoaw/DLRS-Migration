using System;
using System.Threading;
using System.Threading.Tasks;

namespace LandPressDataMigrationDesktopApp
{
    public enum OperationState
    {
        Idle,
        Running,
        PauseRequested,
        Paused,
        StopRequested,
        Stopped
    }

    public class OperationStateChangedEventArgs : EventArgs
    {
        public OperationState OldState { get; }
        public OperationState NewState { get; }
        public string? Reason { get; }

        public OperationStateChangedEventArgs(OperationState oldState, OperationState newState, string? reason = null)
        {
            OldState = oldState;
            NewState = newState;
            Reason = reason;
        }
    }

    /// <summary>
    /// Thread-safe central controller for long-running pipeline and step operations.
    /// Manages Running, Paused, and Stopped states with cooperative checkpoints and no busy-waiting.
    /// </summary>
    public sealed class OperationController : IDisposable
    {
        private readonly object _syncRoot = new();
        private CancellationTokenSource _cts = new();
        private TaskCompletionSource<bool> _pauseTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private OperationState _state = OperationState.Idle;

        public event EventHandler<OperationStateChangedEventArgs>? StateChanged;

        public OperationState State
        {
            get { lock (_syncRoot) return _state; }
            private set
            {
                OperationState oldState;
                lock (_syncRoot)
                {
                    if (_state == value) return;
                    oldState = _state;
                    _state = value;
                }
                StateChanged?.Invoke(this, new OperationStateChangedEventArgs(oldState, value));
            }
        }

        public bool IsRunning => State is OperationState.Running or OperationState.PauseRequested or OperationState.Paused;
        public bool IsPaused => State == OperationState.Paused;
        public bool IsPauseRequested => State == OperationState.PauseRequested;
        public bool IsStopRequested => State == OperationState.StopRequested;
        public bool IsStopped => State == OperationState.Stopped;
        public bool IsIdle => State == OperationState.Idle;

        public CancellationToken Token => _cts.Token;

        public void Start()
        {
            lock (_syncRoot)
            {
                _cts.Dispose();
                _cts = new CancellationTokenSource();
                _pauseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pauseTcs.TrySetResult(true); // Unpaused initially
                State = OperationState.Running;
            }
        }

        public void RequestPause()
        {
            lock (_syncRoot)
            {
                if (_state != OperationState.Running) return;
                _pauseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                State = OperationState.PauseRequested;
            }
        }

        public void AcknowledgePaused()
        {
            lock (_syncRoot)
            {
                if (_state == OperationState.PauseRequested)
                {
                    State = OperationState.Paused;
                }
            }
        }

        public void Resume()
        {
            lock (_syncRoot)
            {
                if (_state is not (OperationState.Paused or OperationState.PauseRequested)) return;
                _pauseTcs.TrySetResult(true);
                State = OperationState.Running;
            }
        }

        public void RequestStop()
        {
            lock (_syncRoot)
            {
                if (_state is OperationState.Idle or OperationState.Stopped) return;
                State = OperationState.StopRequested;
                _cts.Cancel();
                // Release pause gate immediately so paused threads awaken and observe cancellation
                _pauseTcs.TrySetResult(false);
            }
        }

        public void AcknowledgeStopped()
        {
            lock (_syncRoot)
            {
                State = OperationState.Stopped;
                _pauseTcs.TrySetResult(false);
            }
        }

        public void Reset()
        {
            lock (_syncRoot)
            {
                _cts.Dispose();
                _cts = new CancellationTokenSource();
                _pauseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pauseTcs.TrySetResult(true);
                State = OperationState.Idle;
            }
        }

        /// <summary>
        /// Cooperative checkpoint called inside loops or between batches/steps.
        /// If paused, asynchronously waits until resumed or cancelled.
        /// Throws OperationCanceledException if stop was requested.
        /// </summary>
        public async Task CheckpointAsync(CancellationToken externalCt = default)
        {
            if (_cts.IsCancellationRequested || externalCt.IsCancellationRequested)
            {
                throw new OperationCanceledException(_cts.Token);
            }

            Task pauseTask;
            lock (_syncRoot)
            {
                if (_state == OperationState.PauseRequested)
                {
                    _state = OperationState.Paused;
                    StateChanged?.Invoke(this, new OperationStateChangedEventArgs(OperationState.PauseRequested, OperationState.Paused));
                }
                pauseTask = _pauseTcs.Task;
            }

            if (!pauseTask.IsCompleted)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, externalCt);
                using (linkedCts.Token.Register(() => _pauseTcs.TrySetResult(false)))
                {
                    await pauseTask.ConfigureAwait(false);
                }
            }

            if (_cts.IsCancellationRequested || externalCt.IsCancellationRequested)
            {
                throw new OperationCanceledException(_cts.Token);
            }
        }

        public void Dispose()
        {
            _cts.Dispose();
        }
    }
}
