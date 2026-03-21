using System.Runtime.InteropServices;
using ASD4G.Models;

namespace ASD4G.Services;

public sealed class DisplayScalingService
{
    private const int EnumCurrentSettings = -1;
    private const int DispChangeSuccessful = 0;
    private const int DM_BITSPERPEL = 0x00040000;
    private const int DM_PELSWIDTH = 0x00080000;
    private const int DM_PELSHEIGHT = 0x00100000;
    private const int DM_DISPLAYFREQUENCY = 0x00400000;

    private DisplayMode? _originalMode;
    private DisplayMode? _appliedMode;

    public bool IsScalingApplied => _appliedMode is not null;

    public DisplayMode? OriginalMode => _originalMode;

    public DisplayMode? AppliedMode => _appliedMode;

    public DisplayMode? GetCurrentMode()
    {
        var mode = CreateDevMode();
        return EnumDisplaySettings(null, EnumCurrentSettings, ref mode) ? ToDisplayMode(mode) : null;
    }

    public bool IsTargetModeSupported(int width, int height)
    {
        return FindBestMatchingMode(width, height) is not null;
    }

    public DisplayOperationResult ApplyResolution(int width, int height)
    {
        var currentMode = GetCurrentMode();
        if (currentMode is null)
        {
            return new DisplayOperationResult
            {
                Failure = DisplayOperationFailure.CurrentModeUnavailable
            };
        }

        var targetMode = FindBestMatchingMode(width, height);
        if (targetMode is null)
        {
            return new DisplayOperationResult
            {
                Failure = DisplayOperationFailure.TargetModeUnsupported
            };
        }

        var shouldSaveOriginal = _originalMode is null;
        if (shouldSaveOriginal)
        {
            _originalMode = currentMode;
        }

        var devMode = ToDevMode(targetMode);
        var result = ChangeDisplaySettings(ref devMode, 0);
        if (result == DispChangeSuccessful)
        {
            _appliedMode = targetMode;
            return new DisplayOperationResult
            {
                Success = true,
                Mode = targetMode
            };
        }

        if (shouldSaveOriginal)
        {
            _originalMode = null;
        }

        return new DisplayOperationResult
        {
            Failure = DisplayOperationFailure.ApplyFailed
        };
    }

    public DisplayOperationResult RestoreResolution()
    {
        if (_originalMode is null)
        {
            _appliedMode = null;
            return new DisplayOperationResult
            {
                Success = true
            };
        }

        var devMode = ToDevMode(_originalMode);
        var result = ChangeDisplaySettings(ref devMode, 0);
        if (result == DispChangeSuccessful)
        {
            var restoredMode = _originalMode;
            _originalMode = null;
            _appliedMode = null;

            return new DisplayOperationResult
            {
                Success = true,
                Mode = restoredMode
            };
        }

        return new DisplayOperationResult
        {
            Failure = DisplayOperationFailure.RestoreFailed
        };
    }

    private DisplayMode? FindBestMatchingMode(int width, int height)
    {
        var currentMode = GetCurrentMode();

        return GetAvailableModes()
            .Where(mode => mode.Width == width && mode.Height == height)
            .OrderBy(mode =>
            {
                if (currentMode is null)
                {
                    return int.MaxValue;
                }

                var bitsPenalty = mode.BitsPerPixel == currentMode.BitsPerPixel ? 0 : 1000;
                return Math.Abs(mode.DisplayFrequency - currentMode.DisplayFrequency) + bitsPenalty;
            })
            .ThenByDescending(mode => mode.DisplayFrequency)
            .FirstOrDefault();
    }

    private List<DisplayMode> GetAvailableModes()
    {
        var modes = new Dictionary<string, DisplayMode>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; ; index++)
        {
            var devMode = CreateDevMode();
            if (!EnumDisplaySettings(null, index, ref devMode))
            {
                break;
            }

            var mode = ToDisplayMode(devMode);
            var key = $"{mode.Width}x{mode.Height}:{mode.DisplayFrequency}:{mode.BitsPerPixel}";
            modes[key] = mode;
        }

        return modes.Values.ToList();
    }

    private static DisplayMode ToDisplayMode(DEVMODE mode)
    {
        return new DisplayMode
        {
            Width = mode.dmPelsWidth,
            Height = mode.dmPelsHeight,
            BitsPerPixel = mode.dmBitsPerPel,
            DisplayFrequency = mode.dmDisplayFrequency
        };
    }

    private static DEVMODE ToDevMode(DisplayMode mode)
    {
        var devMode = CreateDevMode();
        devMode.dmPelsWidth = mode.Width;
        devMode.dmPelsHeight = mode.Height;
        devMode.dmBitsPerPel = mode.BitsPerPixel;
        devMode.dmDisplayFrequency = mode.DisplayFrequency;
        devMode.dmFields = DM_BITSPERPEL | DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        return devMode;
    }

    private static DEVMODE CreateDevMode()
    {
        return new DEVMODE
        {
            dmDeviceName = string.Empty,
            dmFormName = string.Empty,
            dmSize = (short)Marshal.SizeOf<DEVMODE>()
        };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNumber, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }
}
