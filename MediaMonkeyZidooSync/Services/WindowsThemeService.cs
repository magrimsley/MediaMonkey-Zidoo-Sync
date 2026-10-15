using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace MediaMonkeyZidooSync.Services;

public static class WindowsThemeService
{
    public static void Apply(Application app)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        bool dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;

        Set(app,"AppWindowBrush",dark?Color.FromRgb(32,32,32):SystemColors.WindowColor);
        Set(app,"AppControlBrush",dark?Color.FromRgb(45,45,48):SystemColors.ControlColor);
        Set(app,"AppControlAltBrush",dark?Color.FromRgb(37,37,38):SystemColors.ControlLightColor);
        Set(app,"AppTextBrush",dark?Colors.White:SystemColors.WindowTextColor);
        Set(app,"AppBorderBrush",dark?Color.FromRgb(85,85,85):SystemColors.ActiveBorderColor);
        Set(app,"AppSelectionBrush",SystemColors.HighlightColor);
        Set(app,"AppSelectionTextBrush",SystemColors.HighlightTextColor);
    }
    private static void Set(Application app,string key,Color color)=>
        app.Resources[key]=new SolidColorBrush(color);
}