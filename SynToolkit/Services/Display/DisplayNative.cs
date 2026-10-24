#nullable enable

using System;
using System.Runtime.InteropServices;

namespace SynToolkit.Services.Display;

internal static class DisplayNative
{
    public const uint QdcOnlyActivePaths = 0x00000002;
    public const uint QdcDatabaseCurrent = 0x00000004;
    public const uint DisplayConfigPathActive = 0x00000001;
    public const uint DisplayConfigPathModeIdxInvalid = 0xffffffff;

    public const uint SdcTopologyInternal = 0x00000001;
    public const uint SdcTopologyClone = 0x00000002;
    public const uint SdcTopologyExtend = 0x00000004;
    public const uint SdcTopologyExternal = 0x00000008;
    public const uint SdcUseDatabaseCurrent = SdcTopologyInternal | SdcTopologyClone | SdcTopologyExtend | SdcTopologyExternal;
    public const uint SdcUseSuppliedDisplayConfig = 0x00000020;
    public const uint SdcValidate = 0x00000040;
    public const uint SdcApply = 0x00000080;
    public const uint SdcSaveToDatabase = 0x00000200;
    public const uint SdcAllowChanges = 0x00000400;
    public const uint SdcAllowPathOrderChanges = 0x00002000;

    public const int ErrorSuccess = 0;
    public const int ErrorInsufficientBuffer = 122;

    public const int DeviceInfoGetSourceName = 1;
    public const int DeviceInfoGetTargetName = 2;
    public const int DeviceInfoGetTargetPreferredMode = 3;
    public const int DeviceInfoGetSourceDpiScale = -3;
    public const int DeviceInfoSetSourceDpiScale = -4;
    public const int DeviceInfoGetAdvancedColorInfo = 9;
    public const int DeviceInfoSetAdvancedColorState = 10;
    public const int DeviceInfoGetAdvancedColorInfo2 = 15;
    public const int DeviceInfoSetHdrState = 16;

    public const int CchDeviceName = 32;
    public const int CchFormName = 32;

    public const uint EnumCurrentSettings = 0xFFFFFFFFu;
    public const uint EdsRotatedMode = 0x00000004;

    public const uint DmPosition = 0x00000020;
    public const uint DmDisplayOrientation = 0x00000080;
    public const uint DmBitsPerPel = 0x00040000;
    public const uint DmPelsWidth = 0x00080000;
    public const uint DmPelsHeight = 0x00100000;
    public const uint DmDisplayFrequency = 0x00400000;

    public const uint MonitorDefaultToNearest = 2;
    public const int MdtEffectiveDpi = 0;

    public const uint DisplayConfigRotationIdentity = 1;
    public const uint DisplayConfigRotationRotate90 = 2;
    public const uint DisplayConfigRotationRotate180 = 3;
    public const uint DisplayConfigRotationRotate270 = 4;

    public const uint DisplayConfigModeInfoTypeSource = 1;
    public const uint DisplayConfigModeInfoTypeTarget = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct PointL
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfig2DRegion
    {
        public uint Cx;
        public uint Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigVideoSignalInfo
    {
        public ulong PixelRate;
        public DisplayConfigRational HSyncFreq;
        public DisplayConfigRational VSyncFreq;
        public DisplayConfig2DRegion ActiveSize;
        public DisplayConfig2DRegion TotalSize;
        public uint VideoStandard;
        public uint ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigSourceMode
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public int PositionX;
        public int PositionY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigTargetMode
    {
        public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct DisplayConfigModeInfoUnion
    {
        [FieldOffset(0)] public DisplayConfigTargetMode TargetMode;
        [FieldOffset(0)] public DisplayConfigSourceMode SourceMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        public DisplayConfigModeInfoUnion Mode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public int OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigDeviceInfoHeader
    {
        public int Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public int OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigTargetPreferredMode
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Width;
        public uint Height;
        public DisplayConfigTargetMode TargetMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigGetSourceDpiScale
    {
        public DisplayConfigDeviceInfoHeader Header;
        public int MinRelativeScaleStep;
        public int CurRelativeScaleStep;
        public int MaxRelativeScaleStep;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigSetSourceDpiScale
    {
        public DisplayConfigDeviceInfoHeader Header;
        public int RelativeScaleStep;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigGetAdvancedColorInfo
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigSetAdvancedColorState
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigGetAdvancedColorInfo2
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
        public uint ActiveColorMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigSetHdrState
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DevModeW
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll")]
    internal static extern int GetDisplayConfigBufferSizes(
        uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    internal static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    internal static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        out uint currentTopologyId);

    [DllImport("user32.dll")]
    internal static extern int SetDisplayConfig(
        uint numPathArrayElements,
        [In] DisplayConfigPathInfo[]? pathArray,
        uint numModeInfoArrayElements,
        [In] DisplayConfigModeInfo[]? modeInfoArray,
        uint flags);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName deviceInfo);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName deviceInfo);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetPreferredMode deviceInfo);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigGetSourceDpiScale deviceInfo);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigSetSourceDpiScale deviceInfo);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigGetAdvancedColorInfo deviceInfo);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigSetAdvancedColorState deviceInfo);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigGetAdvancedColorInfo2 deviceInfo);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigSetHdrState deviceInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsExW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplaySettingsEx(
        string? lpszDeviceName,
        uint iModeNum,
        ref DevModeW lpDevMode,
        uint dwFlags);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(PointL pt, uint dwFlags);

    [DllImport("Shcore.dll")]
    internal static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    internal static DisplayConfigDeviceInfoHeader CreateHeader(int type, uint size, Luid adapterId, uint id) => new()
    {
        Type = type,
        Size = size,
        AdapterId = adapterId,
        Id = id
    };

    internal static uint SizeOf<T>() where T : struct => (uint)Marshal.SizeOf<T>();

    internal static DevModeW CreateDevMode() => new()
    {
        dmSize = (ushort)Marshal.SizeOf<DevModeW>(),
        dmDriverExtra = 0,
        dmDeviceName = string.Empty,
        dmFormName = string.Empty
    };

    internal static uint DmdoToCcdRotation(uint dmdo) => dmdo switch
    {
        1 => DisplayConfigRotationRotate90,
        2 => DisplayConfigRotationRotate180,
        3 => DisplayConfigRotationRotate270,
        _ => DisplayConfigRotationIdentity
    };
}
