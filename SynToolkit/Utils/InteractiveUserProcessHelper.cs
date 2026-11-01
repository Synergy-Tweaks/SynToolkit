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
    /// Launches a process as the signed-in desktop user rather than with SynToolkit's own
    /// elevated (Administrator) token. Some per-user operations (notably the
    /// Set-WinUserLanguageList PowerShell cmdlet, and winget packages that must not run
    /// elevated) act on the caller's own profile; running them inline while SynToolkit is
    /// elevated would silently apply to the Administrator token's profile instead of the
    /// signed-in user's.
    /// </summary>
    public static class InteractiveUserProcessHelper
    {
        // MAXIMUM_ALLOWED grants exactly the rights available on the token; requesting
        // GENERIC_ALL can fail if it asks for more than is actually grantable.
        private const uint MAXIMUM_ALLOWED = 0x02000000;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
        private const uint TOKEN_DUPLICATE = 0x0002;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint LOGON_WITH_PROFILE = 0x00000001;

        public static int RunAsInteractiveUser(string fileName, string arguments, int timeoutMilliseconds = 30_000)
        {
            IntPtr userToken = GetShellUserToken();

            // LOGON_WITH_PROFILE makes CreateProcessWithTokenW load the user's profile, so
            // HKEY_CURRENT_USER and %APPDATA% resolve to the signed-in user, not to the
            // elevated Administrator token SynToolkit is running under.
            IntPtr primaryToken = IntPtr.Zero;
            IntPtr environmentBlock = IntPtr.Zero;
            try
            {
                if (!DuplicateTokenEx(
                        userToken,
                        MAXIMUM_ALLOWED,
                        IntPtr.Zero,
                        SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
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

                string commandLine = string.IsNullOrEmpty(arguments) ? $"\"{fileName}\"" : $"\"{fileName}\" {arguments}";

                const uint CREATE_NO_WINDOW = 0x08000000;
                const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

                if (!CreateProcessWithTokenW(
                        primaryToken,
                        LOGON_WITH_PROFILE,
                        null,
                        commandLine,
                        CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT,
                        environmentBlock,
                        null,
                        ref startupInfo,
                        out PROCESS_INFORMATION processInformation))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to start the process as the signed-in user.");
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

        // WTSQueryUserToken requires SeTcbPrivilege (SYSTEM only), so an elevated-but-not-SYSTEM
        // app always fails it with error 1314. The desktop shell already runs as the signed-in
        // user at medium integrity, which is exactly the context these operations need.
        private static IntPtr GetShellUserToken()
        {
            IntPtr shellWindow = GetShellWindow();
            if (shellWindow == IntPtr.Zero)
            {
                throw new InvalidOperationException("The Windows desktop shell is not running, so the operation cannot be run as the signed-in user.");
            }

            GetWindowThreadProcessId(shellWindow, out uint shellProcessId);
            IntPtr shellProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, shellProcessId);
            if (shellProcess == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open the desktop shell process.");
            }

            try
            {
                if (!OpenProcessToken(shellProcess, TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_ASSIGN_PRIMARY, out IntPtr shellToken))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open the signed-in user's shell token.");
                }

                return shellToken;
            }
            finally
            {
                CloseHandle(shellProcess);
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

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessWithTokenW(
            IntPtr token,
            uint logonFlags,
            string? applicationName,
            string commandLine,
            uint creationFlags,
            IntPtr environment,
            string? currentDirectory,
            ref STARTUPINFO startupInfo,
            out PROCESS_INFORMATION processInformation);

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
