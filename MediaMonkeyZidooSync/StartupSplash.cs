using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MediaMonkeyZidooSync;

/// <summary>Closes the nonactivating splash two seconds after the main UI first renders.</summary>
internal sealed class StartupSplash
{
    private readonly Window main;
    private readonly DispatcherTimer timer;
    private readonly Window splash;
    internal bool IsVisible => splash.IsVisible;
    internal StartupSplash(Window main)
    {
        this.main = main;
        splash = new Window
        {
            Title = "MM Zidoo Sync - Starting",
            Width = 704, Height = 384, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true, Cursor = Cursors.Arrow,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/MMZidooSync;component/Resources/Branding/Splash.jpg")),
                Stretch = Stretch.Uniform
            }
        };
        timer = new DispatcherTimer(DispatcherPriority.Normal, main.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Tick += TimerElapsed;
        main.ContentRendered += MainRendered;
        main.Closed += MainClosed;
        splash.Show();
    }
    private void MainRendered(object? sender, EventArgs e)
    {
        main.ContentRendered -= MainRendered;
        timer.Start();
    }
    private void TimerElapsed(object? sender, EventArgs e) => Close();
    private void MainClosed(object? sender, EventArgs e) => Close();
    private void Close()
    {
        timer.Stop();
        timer.Tick -= TimerElapsed;
        main.ContentRendered -= MainRendered;
        main.Closed -= MainClosed;
        splash.Close();
    }
}
