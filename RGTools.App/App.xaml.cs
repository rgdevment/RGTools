using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using RGTools.App.ViewModels;

namespace RGTools.App;

/// <summary>
/// Application entry point. Manages the lifecycle of the Tray Icon.
/// </summary>
public partial class App : Application
{
  private TaskbarIcon? _trayIcon;
  private TrayViewModel? _viewModel;

  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);

    // 1. Initialize ViewModel (Dependency Injection root)
    _viewModel = new TrayViewModel();

    // 2. Instantiate TaskbarIcon programmatically
    // This avoids XAML ResourceDictionary lookup failures.
    _trayIcon = new TaskbarIcon
    {
      ToolTipText = "RGTools",
      DataContext = _viewModel,
      Visibility = Visibility.Visible,

      // Critical: Use specific absolute Pack URI.
      // Format: pack://application:,,,/{AssemblyName};component/{Path}
      IconSource = new BitmapImage(new Uri("pack://application:,,,/RGTools.App;component/app.ico"))
    };

    // 3. Build Context Menu programmatically (Safe & Type-checked)
    var contextMenu = new ContextMenu();

    // Item: Status (Header)
    contextMenu.Items.Add(new MenuItem
    {
      Header = "RGTools: Active",
      IsEnabled = false,
      FontWeight = FontWeights.Bold
    });

    contextMenu.Items.Add(new Separator());

    // Item: Config (Bound to Command)
    var configItem = new MenuItem { Header = "Settings" };
    configItem.SetBinding(MenuItem.CommandProperty, new System.Windows.Data.Binding(nameof(TrayViewModel.OpenConfigCommand)));
    contextMenu.Items.Add(configItem);

    contextMenu.Items.Add(new Separator());

    // Item: Exit (Bound to Command)
    var exitItem = new MenuItem { Header = "Exit" };
    exitItem.SetBinding(MenuItem.CommandProperty, new System.Windows.Data.Binding(nameof(TrayViewModel.CloseCommand)));
    contextMenu.Items.Add(exitItem);

    _trayIcon.ContextMenu = contextMenu;

    // 4. Force rendering
    _trayIcon.ForceCreate();
  }

  protected override void OnExit(ExitEventArgs e)
  {
    _trayIcon?.Dispose();
    base.OnExit(e);
  }
}
