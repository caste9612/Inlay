using Quiver.App.Services;

namespace Quiver.App.Tests;

/// <summary>The thread the shell's operations run on, so that the window stays usable during a long copy.</summary>
public sealed class FileOperationQueueTests
{
    [Fact]
    public async Task Operations_RunOneAfterAnother_OnAnStaThreadOfTheirOwn()
    {
        using var queue = new FileOperationQueue();
        using var release = new ManualResetEventSlim();
        var order = new List<string>();
        var testThread = Environment.CurrentManagedThreadId;
        int? workThread = null;
        ApartmentState? apartment = null;

        var first = queue.Run(() =>
        {
            workThread = Environment.CurrentManagedThreadId;
            apartment = Thread.CurrentThread.GetApartmentState();
            release.Wait();
            lock (order)
                order.Add("first");
            return true;
        });
        var second = queue.Run(() =>
        {
            lock (order)
                order.Add("second");
            return false;
        });

        // The first one holds the thread: the second waits for its turn, and nobody else waits for either.
        await Task.Delay(50);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        release.Set();

        Assert.True(await first);
        Assert.False(await second);
        Assert.Equal(["first", "second"], order);
        Assert.NotEqual(testThread, workThread);
        Assert.Equal(ApartmentState.STA, apartment);
    }

    [Fact]
    public async Task AnOperationThatThrows_FailsItsTaskOnly()
    {
        using var queue = new FileOperationQueue();

        var failing = queue.Run(() => throw new IOException("in uso"));
        var next = queue.Run(() => true);

        var error = await Assert.ThrowsAsync<IOException>(() => failing);
        Assert.Equal("in uso", error.Message);
        Assert.True(await next);
    }
}
