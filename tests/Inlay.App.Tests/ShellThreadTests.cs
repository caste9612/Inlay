using Inlay.App.Services;

namespace Inlay.App.Tests;

/// <summary>The threads the shell's operations run on: the window stays usable during a long copy, and another one goes on meanwhile.</summary>
public sealed class ShellThreadTests
{
    [Fact]
    public async Task EachOperation_RunsOnAnStaThreadOfItsOwn_AlongsideTheOthers()
    {
        using var release = new ManualResetEventSlim();
        var testThread = Environment.CurrentManagedThreadId;
        var threads = new List<(int Id, ApartmentState Apartment)>();

        var first = ShellThread.Run(() =>
        {
            lock (threads)
                threads.Add((Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
            release.Wait();
            return true;
        });
        var second = ShellThread.Run(() =>
        {
            lock (threads)
                threads.Add((Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
            return false;
        });

        // The first holds its thread: the second does not wait for it.
        Assert.False(await second);
        Assert.False(first.IsCompleted);
        release.Set();
        Assert.True(await first);

        Assert.Equal(2, threads.Select(thread => thread.Id).Distinct().Count());
        Assert.DoesNotContain(testThread, threads.Select(thread => thread.Id));
        Assert.All(threads, thread => Assert.Equal(ApartmentState.STA, thread.Apartment));
    }

    [Fact]
    public async Task AnOperationThatThrows_FailsItsTaskOnly()
    {
        var failing = ShellThread.Run(() => throw new IOException("in uso"));
        var next = ShellThread.Run(() => true);

        var error = await Assert.ThrowsAsync<IOException>(() => failing);
        Assert.Equal("in uso", error.Message);
        Assert.True(await next);
    }
}
