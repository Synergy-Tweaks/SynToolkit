#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SynToolkit.Services
{
    /// <summary>
    /// Creates and lists Windows restore points. A manual checkpoint uses
    /// MODIFY_SETTINGS rather than APPLICATION_INSTALL because the user is
    /// saving the current configuration, not installing software.
    /// </summary>
    public sealed class RestorePointService : IRestorePointService
    {
        private const int BeginSystemChange = 100;
        private const int EndSystemChange = 101;
        private const int ModifySettings = 12;
        private const int DescriptionCapacity = 256;
        private const int OperationTimeoutSeconds = 45;
        private const string SystemRestoreKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";

        private const uint ErrorSuccess = 0;
        private const uint ErrorAccessDenied = 5;
        private const uint ErrorServiceDisabled = 1058;
        private const uint ErrorServiceNotActive = 1062;

        public bool IsAdministrator => IsCurrentProcessElevated();

        public Task<RestorePointListResult> ListAsync(CancellationToken cancellationToken = default) =>
            Task.Run(() => List(cancellationToken), cancellationToken);

        public async Task<RestorePointCreateResult> CreateAsync(string description, CancellationToken cancellationToken = default)
        {
            string trimmed = (description ?? string.Empty).Trim();
            if (trimmed.Length == 0 || trimmed.Length > DescriptionCapacity - 1)
            {
                return new RestorePointCreateResult(RestorePointOperationStatus.Failed, null);
            }

            if (!IsAdministrator)
            {
                return new RestorePointCreateResult(RestorePointOperationStatus.NotElevated, null);
            }

            if (!IsProtectionEnabled())
            {
                return new RestorePointCreateResult(RestorePointOperationStatus.ProtectionDisabled, null);
            }

            try
            {
                HashSet<uint> before = ReadSequenceNumbers();
                return await Task.Run(() => CreateCore(trimmed, before, cancellationToken), cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(OperationTimeoutSeconds), cancellationToken);
            }
            catch (TimeoutException)
            {
                App.logger.Warn("[RestorePoint] Creating a restore point timed out.");
                return new RestorePointCreateResult(RestorePointOperationStatus.TimedOut, null);
            }
            catch (OperationCanceledException)
            {
                return new RestorePointCreateResult(RestorePointOperationStatus.Cancelled, null);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[RestorePoint] Creating a restore point failed.");
                return new RestorePointCreateResult(RestorePointOperationStatus.Failed, null);
            }
        }

        private static RestorePointListResult List(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool elevated = IsCurrentProcessElevated();
            bool protectionEnabled = IsProtectionEnabled();
            try
            {
                List<RestorePointEntry> points = QueryRestorePoints()
                    .OrderByDescending(point => point.CreatedLocal)
                    .ThenByDescending(point => point.SequenceNumber)
                    .ToList();
                return new RestorePointListResult(
                    RestorePointOperationStatus.Success,
                    elevated,
                    protectionEnabled,
                    points);
            }
            catch (UnauthorizedAccessException exception)
            {
                App.logger.Warn(exception, "[RestorePoint] Listing restore points was denied.");
                return new RestorePointListResult(RestorePointOperationStatus.AccessDenied, elevated, protectionEnabled, Array.Empty<RestorePointEntry>());
            }
            catch (ManagementException exception)
            {
                App.logger.Warn(exception, "[RestorePoint] Listing restore points failed. Status: {Status}.", exception.ErrorCode);
                RestorePointOperationStatus status = exception.ErrorCode == ManagementStatus.AccessDenied
                    ? RestorePointOperationStatus.AccessDenied
                    : RestorePointOperationStatus.ServiceUnavailable;
                bool enabled = status == RestorePointOperationStatus.ServiceUnavailable ? false : protectionEnabled;
                return new RestorePointListResult(status, elevated, enabled, Array.Empty<RestorePointEntry>());
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                App.logger.Error(exception, "[RestorePoint] Listing restore points failed.");
                return new RestorePointListResult(RestorePointOperationStatus.Failed, elevated, protectionEnabled, Array.Empty<RestorePointEntry>());
            }
        }

        private static RestorePointCreateResult CreateCore(string description, HashSet<uint> existingSequences, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestorePointNativeState begin = CallSetRestorePoint(BeginSystemChange, description, 0);
            if (begin.Status != ErrorSuccess)
            {
                App.logger.Warn("[RestorePoint] Begin restore point failed with status {Status}.", begin.Status);
                return new RestorePointCreateResult(MapStatus(begin.Status), null);
            }

            // Always close the change. Cancelling between begin and end would leave System Restore mid-operation.
            RestorePointNativeState end = CallSetRestorePoint(EndSystemChange, description, begin.SequenceNumber);
            HashSet<uint> after = ReadSequenceNumbers();
            uint? created = after.FirstOrDefault(sequence => !existingSequences.Contains(sequence));
            if (created is > 0)
            {
                return new RestorePointCreateResult(RestorePointOperationStatus.Success, created);
            }

            if (begin.SequenceNumber > 0 && !existingSequences.Contains((uint)begin.SequenceNumber) && after.Contains((uint)begin.SequenceNumber))
            {
                return new RestorePointCreateResult(RestorePointOperationStatus.Success, (uint)begin.SequenceNumber);
            }

            if (end.Status != ErrorSuccess)
            {
                App.logger.Warn("[RestorePoint] End restore point failed with status {Status}.", end.Status);
                return new RestorePointCreateResult(MapStatus(end.Status), null);
            }

            App.logger.Info(
                "[RestorePoint] Windows accepted the request but no new restore point appeared. Frequency minutes: {Frequency}.",
                ReadCreationFrequencyMinutes());
            return new RestorePointCreateResult(RestorePointOperationStatus.Throttled, null);
        }

        private static List<RestorePointEntry> QueryRestorePoints()
        {
            List<RestorePointEntry> points = new();
            using ManagementObjectSearcher searcher = new(@"root\default", "SELECT SequenceNumber, Description, RestorePointType, CreationTime FROM SystemRestore");
            using ManagementObjectCollection results = searcher.Get();
            foreach (ManagementObject point in results.Cast<ManagementObject>())
            {
                using (point)
                {
                    uint sequence = ReadUInt(point["SequenceNumber"]);
                    string description = point["Description"] as string ?? string.Empty;
                    uint type = ReadUInt(point["RestorePointType"]);
                    string createdRaw = point["CreationTime"] as string ?? string.Empty;
                    if (!TryParseLocalTime(createdRaw, out DateTime created))
                    {
                        created = DateTime.MinValue;
                    }

                    points.Add(new RestorePointEntry(sequence, description, type, created));
                }
            }

            return points;
        }

        private static HashSet<uint> ReadSequenceNumbers()
        {
            try
            {
                return QueryRestorePoints().Select(point => point.SequenceNumber).ToHashSet();
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[RestorePoint] Could not read restore point sequence numbers.");
                return new HashSet<uint>();
            }
        }

        private static uint ReadUInt(object? value) => value switch
        {
            uint number => number,
            int number when number > 0 => (uint)number,
            long number when number > 0 && number <= uint.MaxValue => (uint)number,
            _ => 0
        };

        private static bool TryParseLocalTime(string value, out DateTime createdLocal)
        {
            createdLocal = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            try
            {
                createdLocal = ManagementDateTimeConverter.ToDateTime(value);
                return true;
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[RestorePoint] Could not parse restore point time {Value}.", value);
                return false;
            }
        }

        private static bool IsProtectionEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(SystemRestoreKey);
                return key?.GetValue("DisableSR") is not int disableSr || disableSr == 0;
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[RestorePoint] Could not read System Protection state.");
                return true;
            }
        }

        private static int ReadCreationFrequencyMinutes()
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(SystemRestoreKey);
                return key?.GetValue("SystemRestorePointCreationFrequency") is int minutes && minutes >= 0
                    ? minutes
                    : 1440;
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[RestorePoint] Could not read the restore point frequency.");
                return 1440;
            }
        }

        private static bool IsCurrentProcessElevated()
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[RestorePoint] Unable to determine administrator access.");
                return false;
            }
        }

        private static RestorePointOperationStatus MapStatus(uint status) => status switch
        {
            ErrorAccessDenied => RestorePointOperationStatus.AccessDenied,
            ErrorServiceDisabled or ErrorServiceNotActive => RestorePointOperationStatus.ProtectionDisabled,
            _ => RestorePointOperationStatus.Failed
        };

        private static RestorePointNativeState CallSetRestorePoint(int eventType, string description, long sequenceNumber)
        {
            RestorePointInfo info = new()
            {
                EventType = eventType,
                RestorePointType = ModifySettings,
                SequenceNumber = sequenceNumber,
                Description = description
            };
            bool accepted = SRSetRestorePointW(ref info, out StateManagerStatus status);
            uint result = accepted && status.Status == ErrorSuccess ? ErrorSuccess : status.Status;
            if (!accepted && result == ErrorSuccess)
            {
                result = (uint)Marshal.GetLastWin32Error();
            }

            return new RestorePointNativeState(result, status.SequenceNumber);
        }

        private readonly record struct RestorePointNativeState(uint Status, long SequenceNumber);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RestorePointInfo
        {
            public int EventType;
            public int RestorePointType;
            public long SequenceNumber;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DescriptionCapacity)]
            public string Description;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StateManagerStatus
        {
            public uint Status;
            public long SequenceNumber;
        }

        [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SRSetRestorePointW(ref RestorePointInfo restorePoint, out StateManagerStatus status);
    }
}
