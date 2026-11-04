#nullable enable

using System;
using System.Text.RegularExpressions;
using SynToolkit.Utils;

namespace SynToolkit.Services
{
    /// <summary>
    /// Adds or removes a keyboard language via the WinUserLanguageList PowerShell cmdlets.
    /// These act on the caller's own per-user profile, so the command is run in the console
    /// user's session (InteractiveUserProcessHelper) rather than inline while SynToolkit is
    /// elevated, which would otherwise silently apply to the Administrator token's profile.
    /// </summary>
    public static class KeyboardLanguageService
    {
        private static readonly Regex InputTipPattern = new(@"^[A-Za-z0-9\-]+:[0-9A-Fa-f]{8}$", RegexOptions.Compiled);

        public static void AddKeyboardLanguage(string inputTip, bool setAsDefault)
        {
            ValidateInputTip(inputTip);
            string tip = EscapeForPowerShell(inputTip);

            // The layout has to join the language entry it belongs to, not the first entry in
            // the list, and Windows reports tips as "<lcid>:<klid>" while the user may type
            // "<language tag>:<klid>" — N() puts both forms into the same shape.
            string command =
                NormalizeFunction +
                $"$List=Get-WinUserLanguageList;$K=N '{tip}';$Lcid=$K.Split(':')[0];" +
                "$Lang=$List | Where-Object { @($_.InputMethodTips | Where-Object { (N $_).Split(':')[0] -eq $Lcid }).Count -gt 0 } | Select-Object -First 1;" +
                "if($null -eq $Lang){" +
                "  $Lang=(New-WinUserLanguageList ([System.Globalization.CultureInfo]::GetCultureInfo([Convert]::ToInt32($Lcid,16)).Name))[0];" +
                "  [void]$List.Add($Lang)};" +
                "$Has=@($Lang.InputMethodTips | Where-Object { (N $_) -eq $K }).Count -gt 0;" +
                "if(-not $Has){" +
                "  $Hex=$Lang.InputMethodTips.Count -eq 0 -or $Lang.InputMethodTips[0].Split(':')[0] -match '^[0-9A-Fa-f]{4,8}$';" +
                $"  $Mine=if($Hex){{$K}}else{{('{{0}}:{{1}}' -f $Lang.LanguageTag,$K.Split(':')[1])}};" +
                "  [void]$Lang.InputMethodTips.Add($Mine)};" +
                "Set-WinUserLanguageList $List -Force";

            if (setAsDefault)
            {
                command += $";Set-WinDefaultInputMethodOverride -InputTip '{tip}'";
            }

            RunPowerShell(command);
        }

        public static void RemoveKeyboardLanguage(string inputTip)
        {
            ValidateInputTip(inputTip);
            string tip = EscapeForPowerShell(inputTip);

            // Drop only the matching layout from whichever language holds it. Removing the
            // language entry itself took every other layout of that language with it.
            string command =
                NormalizeFunction +
                $"$List=Get-WinUserLanguageList;$K=N '{tip}';$Hit=$false;" +
                "foreach($Lang in $List){" +
                "  for($i=$Lang.InputMethodTips.Count-1;$i -ge 0;$i--){" +
                "    if((N $Lang.InputMethodTips[$i]) -eq $K){$Lang.InputMethodTips.RemoveAt($i);$Hit=$true}}};" +
                "if(-not $Hit){exit 2};" +
                "$Keep=@($List | Where-Object { $_.InputMethodTips.Count -gt 0 });" +
                "if($Keep.Count -eq 0){exit 3};" +
                "Set-WinUserLanguageList $Keep -Force";

            RunPowerShell(command);
        }

        private const string NormalizeFunction =
            "function N([string]$t){$q=$t.Split(':');" +
            "$l=if($q[0] -match '^[0-9A-Fa-f]{4,8}$'){[Convert]::ToInt32($q[0],16)}else{[System.Globalization.CultureInfo]::GetCultureInfo($q[0]).LCID};" +
            "('{0:X4}:{1}' -f $l,$q[1].ToUpperInvariant())};";

        private static void ValidateInputTip(string inputTip)
        {
            if (string.IsNullOrWhiteSpace(inputTip) || !InputTipPattern.IsMatch(inputTip))
            {
                throw new ArgumentException(
                    "Enter a language tag and keyboard identifier in the form \"en-US:00000409\".",
                    nameof(inputTip));
            }
        }

        private static string EscapeForPowerShell(string value) => value.Replace("'", "''");

        private static void RunPowerShell(string command)
        {
            int exitCode = InteractiveUserProcessHelper.RunAsInteractiveUser(
                Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\WindowsPowerShell\v1.0\powershell.exe"),
                $"-NoProfile -Command \"{command}\"",
                60_000);

            if (exitCode != 0)
            {
                throw new InvalidOperationException($"PowerShell exited with code {exitCode} while updating the keyboard language list.");
            }
        }
    }
}
