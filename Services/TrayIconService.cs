using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Application = System.Windows.Application;
using DrawingBitmap = System.Drawing.Bitmap;
using FormsContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using FormsToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;
using FormsToolStripSeparator = System.Windows.Forms.ToolStripSeparator;
using ImageSource = System.Windows.Media.ImageSource;

namespace ASD4G.Services;

public sealed class TrayIconService : IDisposable
{
    private const string DarkIconResourcePath = "pack://application:,,,/Assets/app.ico";
    private const string LightIconResourcePath = "pack://application:,,,/Assets/app-light.png";
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string SystemUsesLightThemeValueName = "SystemUsesLightTheme";

    private readonly LocalizationService _localizationService;
    private readonly FormsNotifyIcon _notifyIcon;
    private readonly FormsToolStripMenuItem _showMenuItem;
    private readonly FormsToolStripMenuItem _exitMenuItem;
    private readonly Icon _darkIcon;
    private readonly Icon _lightIcon;
    private readonly ImageSource _darkWindowIcon;
    private readonly ImageSource _lightWindowIcon;
    private bool _disposed;
    private bool _usingDarkIcon;

    public TrayIconService(LocalizationService localizationService)
    {
        _localizationService = localizationService;
        _darkIcon = LoadIconFromResource(DarkIconResourcePath);
        _lightIcon = LoadIconFromBitmapResource(LightIconResourcePath);
        _darkWindowIcon = CreateImageSource(_darkIcon);
        _lightWindowIcon = CreateImageSource(_lightIcon);

        _showMenuItem = new FormsToolStripMenuItem();
        _showMenuItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        _exitMenuItem = new FormsToolStripMenuItem();
        _exitMenuItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new FormsContextMenuStrip();
        menu.Items.Add(_showMenuItem);
        menu.Items.Add(new FormsToolStripSeparator());
        menu.Items.Add(_exitMenuItem);

        _notifyIcon = new FormsNotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        _localizationService.LanguageChanged += OnLanguageChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        UpdateLocalization();
        RefreshIconTheme();
    }

    public event EventHandler? ShowRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler? IconThemeChanged;

    public ImageSource CurrentWindowIcon => _usingDarkIcon ? _darkWindowIcon : _lightWindowIcon;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _localizationService.LanguageChanged -= OnLanguageChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _darkIcon.Dispose();
        _lightIcon.Dispose();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(UpdateLocalization);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Color or
            UserPreferenceCategory.General or
            UserPreferenceCategory.VisualStyle or
            UserPreferenceCategory.Window)
        {
            RunOnUiThread(RefreshIconTheme);
        }
    }

    private void UpdateLocalization()
    {
        _notifyIcon.Text = LimitToolTipText(_localizationService.Translate("App.Name"));
        _showMenuItem.Text = _localizationService.Translate("Tray.Show");
        _exitMenuItem.Text = _localizationService.Translate("Tray.Exit");
    }

    private void RefreshIconTheme()
    {
        var shouldUseDarkIcon = IsLightTaskbar();
        if (_notifyIcon.Icon is not null && _usingDarkIcon == shouldUseDarkIcon)
        {
            return;
        }

        _usingDarkIcon = shouldUseDarkIcon;
        _notifyIcon.Icon = _usingDarkIcon ? _darkIcon : _lightIcon;
        IconThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            var value = key?.GetValue(SystemUsesLightThemeValueName);
            return value is int themeValue ? themeValue != 0 : true;
        }
        catch
        {
            return true;
        }
    }

    private static Icon LoadIconFromResource(string resourcePath)
    {
        using var stream = OpenResourceStream(resourcePath);
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    private static Icon LoadIconFromBitmapResource(string resourcePath)
    {
        using var stream = OpenResourceStream(resourcePath);
        using var bitmap = new DrawingBitmap(stream);
        var handle = bitmap.GetHicon();

        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Stream OpenResourceStream(string resourcePath)
    {
        var resourceStream = Application.GetResourceStream(new Uri(resourcePath));
        if (resourceStream?.Stream is null)
        {
            throw new InvalidOperationException($"Unable to load resource '{resourcePath}'.");
        }

        return resourceStream.Stream;
    }

    private static ImageSource CreateImageSource(Icon icon)
    {
        var imageSource = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(32, 32));
        imageSource.Freeze();
        return imageSource;
    }

    private static string LimitToolTipText(string text)
    {
        return text.Length <= 63 ? text : text[..63];
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
