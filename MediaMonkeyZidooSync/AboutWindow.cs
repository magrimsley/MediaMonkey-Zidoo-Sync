using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaMonkeyZidooSync.Services;

namespace MediaMonkeyZidooSync;

/// <summary>Product information with the supplied application artwork.</summary>
public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "About MM Zidoo Sync";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height * 0.85);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/MMZidooSync;component/Resources/Branding/Application.ico"));
        var layout = new StackPanel { Margin = new Thickness(20) };
        var artwork = new Image
        {
            Name = "AboutBrandImage", Height = 190, Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 0, 16),
            Source = new BitmapImage(new Uri("pack://application:,,,/MMZidooSync;component/Resources/Branding/Splash.jpg"))
        };
        NameScope.SetNameScope(this, new NameScope());
        RegisterName(artwork.Name, artwork);
        layout.Children.Add(artwork);
        layout.Children.Add(new TextBlock
        {
            Text = "MM Zidoo Sync", FontSize = 22, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });
        layout.Children.Add(new TextBlock
        {
            Text = $"Product {ApplicationVersionProvider.ProductVersion}\nAssembly {Assembly.GetExecutingAssembly().GetName().Version}\n\nMediaMonkey → Zidoo playlist synchronization\n\n.NET: {Environment.Version}\nOS: {Environment.OSVersion}",
            TextWrapping = TextWrapping.Wrap
        });
        var close = new Button
        {
            Content = "OK", IsDefault = true, IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(24, 6, 24, 6),
            Margin = new Thickness(0, 18, 0, 0)
        };
        close.Click += (_, _) => Close();
        layout.Children.Add(close);
        Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
