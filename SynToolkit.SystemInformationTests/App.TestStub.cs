#nullable enable

using System;
using System.Threading;

namespace SynToolkit;

internal static class App
{
    internal static TestLogger logger { get; } = new();
}

internal sealed class TestLogger
{
    private int _errorCount;

    internal int ErrorCount => Volatile.Read(ref _errorCount);

    internal void Debug(string message)
    {
    }

    internal void Debug(Exception exception, string message)
    {
    }

    internal void Info(string message)
    {
    }

    internal void Warn(string message)
    {
    }

    internal void Warn(Exception exception, string message)
    {
    }

    internal void Error(Exception exception, string message) =>
        Interlocked.Increment(ref _errorCount);

    internal void Error(string message) =>
        Interlocked.Increment(ref _errorCount);

    internal void ResetErrors() => Volatile.Write(ref _errorCount, 0);
}
