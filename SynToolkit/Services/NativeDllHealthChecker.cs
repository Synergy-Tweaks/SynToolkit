#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace SynToolkit.Services
{
    /// <summary>
    /// Performs startup self-check for native DLL dependencies that could cause
    /// STATUS_INVALID_IMAGE_FORMAT (0xC000007B) crashes. This validates architecture
    /// compatibility before the app fully initializes, allowing graceful handling
    /// of missing or incompatible native libraries.
    /// </summary>
    internal static class NativeDllHealthChecker
    {
        /// <summary>
        /// Expected machine type for the current process architecture.
        /// </summary>
        private static ushort ExpectedMachineType => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => 0x8664,   // IMAGE_FILE_MACHINE_AMD64
            Architecture.X86 => 0x014c,   // IMAGE_FILE_MACHINE_I386
            Architecture.Arm64 => 0xAA64, // IMAGE_FILE_MACHINE_ARM64
            _ => 0
        };

        /// <summary>
        /// Checks all native DLLs in the application directory for architecture compatibility.
        /// </summary>
        /// <returns>List of issues found, empty if all checks passed.</returns>
        internal static IReadOnlyList<string> CheckApplicationDlls()
        {
            List<string> issues = new();
            string appDirectory = AppContext.BaseDirectory;
            ushort expectedMachine = ExpectedMachineType;

            if (expectedMachine == 0)
            {
                issues.Add($"Unknown process architecture: {RuntimeInformation.ProcessArchitecture}");
                return issues;
            }

            try
            {
                foreach (string dllPath in Directory.EnumerateFiles(appDirectory, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if (!IsNativeDll(dllPath, out ushort actualMachine))
                        {
                            continue; // Skip managed assemblies
                        }

                        if (actualMachine != expectedMachine)
                        {
                            string actualArch = GetArchitectureName(actualMachine);
                            string expectedArch = GetArchitectureName(expectedMachine);
                            issues.Add($"{Path.GetFileName(dllPath)}: {actualArch} (expected {expectedArch})");
                        }
                    }
                    catch (Exception exception)
                    {
                        App.logger.Debug(exception, "[Startup] Unable to verify DLL: {0}", Path.GetFileName(dllPath));
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Startup] Unable to scan application directory for DLLs.");
            }

            return issues;
        }

        /// <summary>
        /// Probes whether a specific native DLL can be loaded without triggering STATUS_INVALID_IMAGE_FORMAT.
        /// </summary>
        /// <param name="dllName">Name of the DLL to probe (e.g., "nvapi64.dll").</param>
        /// <returns>True if the DLL can be loaded, false otherwise.</returns>
        internal static bool ProbeNativeDll(string dllName)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = LoadLibraryEx(dllName, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
                return handle != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    FreeLibrary(handle);
                }
            }
        }

        /// <summary>
        /// Checks if a file is a native (non-managed) DLL and returns its machine type.
        /// </summary>
        private static bool IsNativeDll(string path, out ushort machineType)
        {
            machineType = 0;

            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using BinaryReader reader = new(stream);

                // DOS header: "MZ"
                if (reader.ReadUInt16() != 0x5A4D)
                {
                    return false;
                }

                // e_lfanew: offset to PE header
                stream.Position = 0x3C;
                int peOffset = reader.ReadInt32();
                if (peOffset < 0 || peOffset > stream.Length - 6)
                {
                    return false;
                }

                // PE signature: "PE\0\0"
                stream.Position = peOffset;
                if (reader.ReadUInt32() != 0x00004550)
                {
                    return false;
                }

                // Machine type
                machineType = reader.ReadUInt16();

                // Check for managed assembly by looking for CLI header
                // Skip to optional header
                stream.Position = peOffset + 4 + 2; // After signature and machine
                ushort numberOfSections = reader.ReadUInt16();
                stream.Position = peOffset + 4 + 20; // Skip to optional header
                ushort optionalHeaderMagic = reader.ReadUInt16();

                int cliHeaderRva;
                if (optionalHeaderMagic == 0x10B) // PE32
                {
                    stream.Position = peOffset + 4 + 20 + 208; // CLI header directory
                    cliHeaderRva = reader.ReadInt32();
                }
                else if (optionalHeaderMagic == 0x20B) // PE32+
                {
                    stream.Position = peOffset + 4 + 20 + 224; // CLI header directory
                    cliHeaderRva = reader.ReadInt32();
                }
                else
                {
                    return true; // Unknown format, assume native
                }

                // If CLI header RVA is non-zero, it's a managed assembly
                return cliHeaderRva == 0;
            }
            catch
            {
                return false;
            }
        }

        private static string GetArchitectureName(ushort machineType) => machineType switch
        {
            0x014c => "x86",
            0x8664 => "x64",
            0xAA64 => "ARM64",
            0x01c4 => "ARM",
            _ => $"Unknown (0x{machineType:X4})"
        };

        private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hModule);
    }
}
