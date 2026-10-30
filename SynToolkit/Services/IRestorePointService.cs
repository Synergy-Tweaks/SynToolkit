#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Services
{
    public enum RestorePointOperationStatus
    {
        Success,
        Throttled,
        ProtectionDisabled,
        AccessDenied,
        NotElevated,
        ServiceUnavailable,
        TimedOut,
        Cancelled,
        Failed,
        /// <summary>Windows accepted the request but the point list could not be read back.</summary>
        Unverified
    }

    public sealed record RestorePointEntry(
        uint SequenceNumber,
        string Description,
        uint RestorePointType,
        DateTime CreatedLocal);

    public sealed record RestorePointListResult(
        RestorePointOperationStatus Status,
        bool IsAdministrator,
        bool IsProtectionEnabled,
        IReadOnlyList<RestorePointEntry> Points);

    public sealed record RestorePointCreateResult(
        RestorePointOperationStatus Status,
        uint? SequenceNumber);

    public interface IRestorePointService
    {
        bool IsAdministrator { get; }

        Task<RestorePointListResult> ListAsync(CancellationToken cancellationToken = default);

        Task<RestorePointCreateResult> CreateAsync(string description, CancellationToken cancellationToken = default);
    }
}
