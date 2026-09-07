#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SynToolkit.Utils
{
    /// <summary>
    /// Launches a process in the interactive console user's desktop session rather than
    /// SynToolkit's own elevated (Administrator) session. Some per-user operations (notably
    /// the Set-WinUserLanguageList PowerShell cmdlet) act on the caller's own profile; running
    /// them inline while SynToolkit is elevated would silently apply to the Administrator
    /// token's profile instead of the signed-in user's. This uses the standard Windows
    /// technique for that: duplicate the console session's user token and pass it to
    /// CreateProcessAsUser.
    /// </summary>
    public static class InteractiveUserProcessHelper
    {
        // MAXIMUM_ALLOWED grants exactly the rights available on the token; requesting
        // GENERIC_ALL can fail if it asks for more than is actually grantable.
        private const uint MAXIMUM_ALLOWED = 0x02000000;

        public static int RunAsInteractiveUser(string fileName, string arguments, int timeoutMilliseconds = 30_000)
        {
            uint sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF)
            {
                throw new InvalidOperationException("No user is currently signed in to the active console session.");
            }

            if (!WTSQueryUserToken(sessionId, out IntPtr userToken))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to query the console user's token.");
            }

            // CreateProcessAsUser does not itself load the target user's registry hive into
            // HKEY_USERS, but LoadUserProfile is unnecessary here: this always targets the
            // *active console session* via WTSGetActiveConsoleSessionId, and per Microsoft's
            // LoadUserProfile documentation, "When a user logs on interactively, the system
            // automatically loads the user's profile" — that's already true for whoever is
            // signed in on the console we just queried, so HKEY_CURRENT_USER resolves
            // correctly for the launched process without an extra LoadUserProfile call.
            IntPtr primaryToken = IntPtr.Zero;
            IntPtr environmentBlock = IntPtr.Zero;
            try
            {
                if (!DuplicateTokenEx(
                        userToken,
                        MAXIMUM_ALLOWED,
                        IntPtr.Zero,
                        SECURITY_IMPERSONATION_LEVEL.SecurityIdentification,
                        TOKEN_TYPE.TokenPrimary,
                        out primaryToken))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to duplicate the console user's token.");
                }

                if (!CreateEnvironmentBlock(out environmentBlock, primaryToken, false))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to build an environment block for the console user.");
                }

                STARTUPINFO startupInfo = new();
                startupInfo.cb = Marshal.SizeOf<STARTUPINFO>();

                SECURITY_ATTRIBUTES processAttributes = new() { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
                SECURITY_ATTRIBUTES threadAttributes = new() { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };

                string commandLine = string.IsNullOrEmpty(arguments) ? $"\"{fileName}\"" : $"\"{fileName}\" {arguments}";

                const uint CREATE_NO_WINDOW = 0x08000000;
                const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

                if (!CreateProcessAsUser(
                        primaryToken,
                        null,
                        commandLine,
                        ref processAttributes,
                        ref threadAttributes,
                        false,
                        CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT,
                        environmentBlock,
                        null,
                        ref startupInfo,
                        out PROCESS_INFORMATION processInformation))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to start the process in the console user's session.");
                }

                try
                {
                    uint waitResult = WaitForSingleObject(processInformation.hProcess, (uint)timeoutMilliseconds);
                    if (waitResult != 0)
                    {
                        throw new TimeoutException("The process did not finish in the console user's session within the allotted time.");
                    }

                    return GetExitCodeProcess(processInformation.hProcess, out uint exitCode) ? (int)exitCode : -1;
                }
                finally
                {
                    CloseHandle(processInformation.hThread);
                    CloseHandle(processInformation.hProcess);
                }
            }
            finally
            {
                if (environmentBlock != IntPtr.Zero)
                {
                    DestroyEnvironmentBlock(environmentBlock);
                }
                if (primaryToken != IntPtr.Zero)
                {
                    CloseHandle(primaryToken);
                }
                CloseHandle(userToken);
            }
        }

        public static CommandResult RunAsInteractiveUserResult(
            string fileName,
            IEnumerable<string> arguments,
            int timeoutMilliseconds = 30_000)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            ArgumentNullException.ThrowIfNull(arguments);

            string tempDirectory = Path.Combine(Path.GetTempPath(), "SynToolkit", "InteractiveUserProcess");
            Directory.CreateDirectory(tempDirectory);

            string outputPath = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + ".log");
            string batchPath = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + ".cmd");

            try
            {
                string executableCommand = BuildBatchCommandLine(fileName, arguments);
                string batchContents = string.Join(
                    Environment.NewLine,
                    [
                        "@echo off",
                        $"{executableCommand} 1>\"{outputPath}\" 2>&1",
                        "exit /b %errorlevel%"
                    ]);
                File.WriteAllText(batchPath, batchContents, Encoding.ASCII);

                int exitCode = RunAsInteractiveUser("cmd.exe", $"/d /s /c call \"{batchPath}\"", timeoutMilliseconds);
                string output = File.Exists(outputPath)
                    ? File.ReadAllText(outputPath)
                    : string.Empty;

                return new CommandResult(exitCode, output.TrimEnd(), string.Empty, TimedOut: false);
            }
            catch (TimeoutException exception)
            {
                return new CommandResult(-1, string.Empty, exception.Message, TimedOut: true);
            }
            finally
            {
                TryDeleteFile(batchPath);
                TryDeleteFile(outputPath);
            }
        }

        private static string BuildBatchCommandLine(string fileName, IEnumerable<string> arguments) =>
            string.Join(
                " ",
                new[] { QuoteBatchArgument(fileName) }
                    .Concat(arguments.Select(QuoteBatchArgument)));

        private static string QuoteBatchArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
            {
                return "\"\"";
            }

            if (!argument.Any(character => char.IsWhiteSpace(character) || character is '"' or '^' or '&' or '|' or '<' or '>'))
            {
                return argument;
            }

            return "\"" + argument.Replace("\"", "\"\"") + "\"";
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        [DllImport("kernel32.dll")]
        private static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DuplicateTokenEx(
            IntPtr existingToken,
            uint desiredAccess,
            IntPtr tokenAttributes,
            SECURITY_IMPERSONATION_LEVEL impersonationLevel,
            TOKEN_TYPE tokenType,
            out IntPtr newToken);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool DestroyEnvironmentBlock(IntPtr environment);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessAsUser(
            IntPtr token,
            string? applicationName,
            string commandLine,
            ref SECURITY_ATTRIBUTES processAttributes,
            ref SECURITY_ATTRIBUTES threadAttributes,
            bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string? currentDirectory,
            ref STARTUPINFO startupInfo,
            out PROCESS_INFORMATION processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        private enum SECURITY_IMPERSONATION_LEVEL
        {
            SecurityAnonymous,
            SecurityIdentification,
            SecurityImpersonation,
            SecurityDelegation
        }

        private enum TOKEN_TYPE
        {
            TokenPrimary = 1,
            TokenImpersonation
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }
    }
}
