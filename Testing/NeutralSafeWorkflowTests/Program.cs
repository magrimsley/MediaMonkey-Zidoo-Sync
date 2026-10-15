using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MediaMonkeyZidooSync;

internal static class Program {
 static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS: " + name); }
 static void Pump() { var frame=new DispatcherFrame(); var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(300) }; timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame); }
 static IEnumerable<DependencyObject> Walk(DependencyObject parent) { yield return parent; for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) foreach(var child in Walk(VisualTreeHelper.GetChild(parent,i))) yield return child; }
 [STAThread] static int Main() {
  var root=Path.Combine(Path.GetTempPath(),"MMZS-SafeWorkflow-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  Application? app=null;
  try {
#pragma warning disable WPF0001
   app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown, ThemeMode=ThemeMode.System };
#pragma warning restore WPF0001
   var main=new MainWindow(root);main.Show();Pump();
   var inspect=(Button)typeof(MainWindow).GetMethod("FindInspectNativePlanButton",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,null)!;
   Check(!inspect.IsEnabled,"Inspect Plan disabled before assessment");
   var menu=Walk(main).OfType<Menu>().Single();
   var help=menu.Items.OfType<MenuItem>().Single(m=>m.Header.ToString()!.Replace("_","")=="Help");
   var guide=help.Items.OfType<MenuItem>().Single(m=>m.Header.ToString()!.Contains("User Guide"));
   Check(guide.IsEnabled,"Help / User Guide is present and enabled");
   guide.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();
   var helpWindow=app.Windows.OfType<Window>().Single(w=>w.Title.Contains(" - Help"));
   Check(helpWindow.IsVisible,"Actual Help menu click handler opens local Help window");
   Check(Walk(helpWindow).OfType<TextBlock>().Any(t=>t.Text=="Getting Started"),"Help contains Getting Started content");
   helpWindow.Close();main.Close();app.Shutdown();
   Console.WriteLine("SCOPE: Actual candidate assembly; WPF routed menu event and isolated config root; no accessibility-popup dependency, assessment, device call, or sync.");return 0;
  } catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
  finally {if(app!=null) {foreach(var w in app.Windows.OfType<Window>().ToArray()) w.Close();app.Shutdown();}}
 }
}
