using Microsoft.Extensions.DependencyInjection;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

internal sealed class WorkerIterationScopeFactory(IServiceScopeFactory scopes) : IServiceScopeFactory
{
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Completed => this._completed.Task;

    public IServiceScope CreateScope() => new ObservedScope(scopes.CreateScope(), this._completed);

    private sealed class ObservedScope(IServiceScope scope, TaskCompletionSource completed) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider => scope.ServiceProvider;

        public void Dispose()
        {
            scope.Dispose();
            completed.TrySetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (scope is IAsyncDisposable asyncScope)
            {
                await asyncScope.DisposeAsync();
            }
            else
            {
                scope.Dispose();
            }

            completed.TrySetResult();
        }
    }
}
