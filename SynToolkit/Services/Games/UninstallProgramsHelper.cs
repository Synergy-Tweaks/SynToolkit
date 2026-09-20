#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace SynToolkit.Services.Games
{
    /// <summary>
    /// Lightweight port of Playnite's Programs.GetUnistallProgramsList registry scan
    /// used by GOG (and Epic launcher path fallback).
    /// </summary>
    internal static class UninstallProgramsHelper
    {
        internal sealed record UninstallProgram(
            string RegistryKeyName,
            string? DisplayName,
            string? Publisher,
            string? InstallLocation,
            string? DisplayIcon);

        public static List<UninstallProgram> GetUninstallPrograms()
        {
            var results = new List<UninstallProgram>();
            ReadHive(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", results);
            ReadHive(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", results);
            ReadHive(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", results);
            return results;
        }

        private static void ReadHive(RegistryKey root, string path, List<UninstallProgram> results)
        {
            try
            {
                using RegistryKey? key = root.OpenSubKey(path);
                if (key is null)
                {
                    return;
                }

                foreach (string subName in key.GetSubKeyNames())
                {
                    try
                    {
                        using RegistryKey? sub = key.OpenSubKey(subName);
                        if (sub is null)
                        {
                            continue;
                        }

                        string? displayName = sub.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName))
                        {
                            continue;
                        }

                        string? installLocation = sub.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrWhiteSpace(installLocation))
                        {
                            installLocation = installLocation.Trim().Trim('"');
                            try
                            {
                                installLocation = Path.GetFullPath(installLocation);
                            }
                            catch
                            {
                                // keep raw
                            }
                        }

                        results.Add(new UninstallProgram(
                            subName,
                            displayName.Trim(),
                            sub.GetValue("Publisher") as string,
                            installLocation,
                            sub.GetValue("DisplayIcon") as string));
                    }
                    catch
                    {
                        // skip malformed uninstall entries
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, $"Unable to read uninstall programs from {path}.");
            }
        }
    }
}
