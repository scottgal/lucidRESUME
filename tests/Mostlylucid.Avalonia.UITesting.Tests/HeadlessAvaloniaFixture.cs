using Avalonia;
using Avalonia.Headless;

namespace Mostlylucid.Avalonia.UITesting.Tests;

/// <summary>
/// xUnit collection fixture that boots a single Avalonia headless session for the
/// whole test session. Tests opt in via <c>[Collection("Avalonia")]</c> and use
/// <see cref="DispatchAsync{T}(Func{T})"/> to marshal work onto the headless dispatcher.
/// </summary>
public sealed class HeadlessAvaloniaFixture : IDisposable
{
    private readonly HeadlessUnitTestSession _session;

    public HeadlessAvaloniaFixture()
    {
        _session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
    }

    public Task<T> DispatchAsync<T>(Func<T> work) => _session.Dispatch(work, CancellationToken.None);

    public Task<T> DispatchAsync<T>(Func<Task<T>> work) => _session.Dispatch(work, CancellationToken.None);

    public Task DispatchAsync(Action work) => _session.Dispatch(work, CancellationToken.None);

    public void Dispose() => _session.Dispose();
}

/// <summary>Empty Avalonia application used to bootstrap the headless platform.</summary>
public sealed class TestApp : Application
{
    public override void Initialize() { }
}

/// <summary>Marker collection so Avalonia tests share one headless session.</summary>
[CollectionDefinition("Avalonia")]
public sealed class AvaloniaCollection : ICollectionFixture<HeadlessAvaloniaFixture> { }
