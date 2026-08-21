#nullable enable

using System;
using System.Runtime.InteropServices;

namespace SynToolkit.Services
{
    internal readonly record struct CpuLiveMetrics(uint? UtilizationPercent, decimal? AverageFrequencyMHz);

    /// <summary>
    /// Samples only Windows' aggregate CPU counters. It does not start a process, issue WMI
    /// queries, or change any power setting, so it is safe to call from the Specs timer.
    /// </summary>
    internal sealed class CpuUsageSampler
    {
        private const int ProcessorInformation = 11;
        private const uint PdhFmtDouble = 0x00000200;
        private IntPtr _performanceQuery;
        private IntPtr _processorFrequencyCounter;
        private IntPtr _processorPerformanceCounter;
        private bool _isPerformanceQueryUnavailable;
        private ulong? _previousIdleTime;
        private ulong? _previousKernelTime;
        private ulong? _previousUserTime;

        internal CpuLiveMetrics Sample() => new(ReadUtilizationPercent(), ReadLiveFrequencyMHz());

        private uint? ReadUtilizationPercent()
        {
            if (!GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime))
            {
                return null;
            }

            ulong idle = ToUInt64(idleTime);
            ulong kernel = ToUInt64(kernelTime);
            ulong user = ToUInt64(userTime);
            if (!_previousIdleTime.HasValue || !_previousKernelTime.HasValue || !_previousUserTime.HasValue)
            {
                _previousIdleTime = idle;
                _previousKernelTime = kernel;
                _previousUserTime = user;
                return null;
            }

            ulong totalDelta = (kernel - _previousKernelTime.Value) + (user - _previousUserTime.Value);
            ulong idleDelta = idle - _previousIdleTime.Value;
            _previousIdleTime = idle;
            _previousKernelTime = kernel;
            _previousUserTime = user;
            if (totalDelta == 0 || idleDelta > totalDelta)
            {
                return null;
            }

            return (uint)Math.Clamp(Math.Round((totalDelta - idleDelta) * 100d / totalDelta), 0, 100);
        }

        private decimal? ReadLiveFrequencyMHz()
        {
            if (!EnsurePerformanceQuery() ||
                PdhCollectQueryData(_performanceQuery) != 0 ||
                !TryReadCounterValue(_processorFrequencyCounter, out double nominalFrequencyMHz) ||
                !TryReadCounterValue(_processorPerformanceCounter, out double processorPerformancePercent))
            {
                return ReadAveragePowerInformationFrequencyMHz();
            }

            try
            {
                return Convert.ToDecimal(nominalFrequencyMHz * processorPerformancePercent / 100d);
            }
            catch (OverflowException)
            {
                return ReadAveragePowerInformationFrequencyMHz();
            }
        }

        private bool EnsurePerformanceQuery()
        {
            if (_performanceQuery != IntPtr.Zero)
            {
                return true;
            }

            if (_isPerformanceQueryUnavailable ||
                PdhOpenQueryW(null, IntPtr.Zero, out IntPtr query) != 0)
            {
                _isPerformanceQueryUnavailable = true;
                return false;
            }

            if (PdhAddEnglishCounterW(query, @"\Processor Information(_Total)\Processor Frequency", IntPtr.Zero, out IntPtr frequencyCounter) != 0 ||
                PdhAddEnglishCounterW(query, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out IntPtr performanceCounter) != 0)
            {
                PdhCloseQuery(query);
                _isPerformanceQueryUnavailable = true;
                return false;
            }

            _performanceQuery = query;
            _processorFrequencyCounter = frequencyCounter;
            _processorPerformanceCounter = performanceCounter;
            return true;
        }

        private static bool TryReadCounterValue(IntPtr counter, out double value)
        {
            value = 0;
            if (PdhGetFormattedCounterValue(counter, PdhFmtDouble, out _, out PdhFormattedCounterValue formattedValue) != 0 ||
                formattedValue.CStatus != 0 ||
                double.IsNaN(formattedValue.DoubleValue) ||
                double.IsInfinity(formattedValue.DoubleValue) ||
                formattedValue.DoubleValue <= 0)
            {
                return false;
            }

            value = formattedValue.DoubleValue;
            return true;
        }
        private static decimal? ReadAveragePowerInformationFrequencyMHz()
        {
            int processorCount = Math.Max(Environment.ProcessorCount, 1);
            int structureSize = Marshal.SizeOf<ProcessorPowerInformation>();
            IntPtr buffer = Marshal.AllocHGlobal(checked(structureSize * processorCount));
            try
            {
                if (CallNtPowerInformation(ProcessorInformation, IntPtr.Zero, 0, buffer, checked((uint)(structureSize * processorCount))) != 0)
                {
                    return null;
                }

                ulong totalMHz = 0;
                int validProcessors = 0;
                for (int index = 0; index < processorCount; index++)
                {
                    IntPtr current = IntPtr.Add(buffer, checked(index * structureSize));
                    ProcessorPowerInformation processor = Marshal.PtrToStructure<ProcessorPowerInformation>(current);
                    if (processor.CurrentMhz == 0)
                    {
                        continue;
                    }

                    totalMHz += processor.CurrentMhz;
                    validProcessors++;
                }

                return validProcessors == 0 ? null : (decimal)totalMHz / validProcessors;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static ulong ToUInt64(FileTime value) => ((ulong)value.HighDateTime << 32) | value.LowDateTime;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

        [DllImport("PowrProf.dll")]
        private static extern uint CallNtPowerInformation(
            int informationLevel,
            IntPtr inputBuffer,
            uint inputBufferLength,
            IntPtr outputBuffer,
            uint outputBufferLength);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll")]
        private static extern uint PdhGetFormattedCounterValue(
            IntPtr counter,
            uint format,
            out uint type,
            out PdhFormattedCounterValue value);

        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr query);

        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime
        {
            internal uint LowDateTime;
            internal uint HighDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessorPowerInformation
        {
            internal uint Number;
            internal uint MaxMhz;
            internal uint CurrentMhz;
            internal uint MhzLimit;
            internal uint MaxIdleState;
            internal uint CurrentIdleState;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PdhFormattedCounterValue
        {
            [FieldOffset(0)]
            internal uint CStatus;

            [FieldOffset(8)]
            internal double DoubleValue;
        }
    }
}
