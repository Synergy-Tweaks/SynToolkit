#nullable enable

using System;
using System.Threading;

namespace SynToolkit;

internal static class App
{
    internal static TestLogger logger { get; } = new();

    internal static void ShutdownApplication() { }
}

internal sealed class TestLogger
{
    private int _errorCount;

    internal int ErrorCount => Volatile.Read(ref _errorCount);

    internal void Debug(string message) { }
    internal void Debug(Exception exception, string message) { }
    internal void Debug<T>(Exception exception, string message, T arg) { }

    internal void Info(string message) { }
    internal void Info<T>(string message, T arg) { }
    internal void Info<T1, T2>(string message, T1 arg1, T2 arg2) { }

    internal void Warn(string message) { }
    internal void Warn(Exception exception, string message) { }
    internal void Warn<T>(Exception exception, string message, T arg) { }

    internal void Error(Exception exception, string message) =>
        Interlocked.Increment(ref _errorCount);
    internal void Error<T>(Exception exception, string message, T arg) =>
        Interlocked.Increment(ref _errorCount);
    internal void Error(string message) =>
        Interlocked.Increment(ref _errorCount);

    internal void ResetErrors() => Volatile.Write(ref _errorCount, 0);
}
