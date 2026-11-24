namespace Roomy.Core;

/// <summary>Well-known locations, resolved once.</summary>
public static class Env
{
    public static string Local { get; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string Roaming { get; } = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string ProgramData { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    public static string Windows { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    public static string UserProfile { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string ProgramFiles { get; } = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    public static string ProgramFilesX86 { get; } = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    public static string Temp { get; } = Path.GetTempPath();
    public static string SystemRoot { get; } = Path.GetPathRoot(Windows) is { Length: > 0 } r ? r : @"C:\";
    public static string Downloads { get; } = Native.KnownFolder(Native.DownloadsFolder) ?? Path.Combine(UserProfile, "Downloads");

    static string Join(string root, string[] parts) => Path.Combine(new[] { root }.Concat(parts).ToArray());

    public static string L(params string[] parts) => Join(Local, parts);
    public static string R(params string[] parts) => Join(Roaming, parts);
    public static string PD(params string[] parts) => Join(ProgramData, parts);
    public static string W(params string[] parts) => Join(Windows, parts);
}
