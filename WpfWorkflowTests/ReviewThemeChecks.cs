using System;using System.IO;using System.Linq;using System.Reflection;using System.Windows;using System.Windows.Controls;using System.Windows.Media;using System.Windows.Threading;using MediaMonkeyZidooSync;using MediaMonkeyZidooSync.Services;
internal static class ReviewThemeChecks
{
 static System.Collections.Generic.IEnumerable<T> Find<T>(DependencyObject root)where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var c=VisualTreeHelper.GetChild(root,i);if(c is T t)yield return t;foreach(var x in Find<T>(c))yield return x;}}
 static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
 public static void Run(MainWindow owner,Action<bool,string> check)
 {
  const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
  var flow=(SynchronizationWorkflow)typeof(MainWindow).GetField("_workflow",flags)!.GetValue(owner)!;
  check(typeof(NativeProductionSyncService).GetMethod("PrepareRepairCandidatesAsync")==null&&typeof(NativeProductionSyncService.ReviewedChange).GetProperty("ReimportExisting")==null&&typeof(NativeProductionTransaction.Change).GetProperty("ReimportExisting")==null,"Deferred repair planner and executor authorization are absent from compiled application");
#pragma warning disable WPF0001
  var prior=Application.Current.ThemeMode;
  foreach(var mode in new[]{ThemeMode.Dark,ThemeMode.Light})
  {
   Application.Current.ThemeMode=mode;Pump();
   flow.BeginValidation();flow.EndValidation(true);flow.BeginVerification();flow.EndVerification(true);
   typeof(MainWindow).GetField("_preparedReview",flags)!.SetValue(owner,new[]{new NativeProductionSyncService.ReviewedChange("Playlist.m3u",2,"old","new","Update",0)});
   Exception? failure=null;
   Dispatcher.CurrentDispatcher.BeginInvoke(new Action(()=>{Window? dialog=null;try{
    dialog=Application.Current.Windows.OfType<Window>().Single(x=>x.Title=="Review Synchronization Changes");Pump();
    var buttons=Find<Button>(dialog).Where(x=>x.Content is string text && (text=="Back"||text=="Use Reviewed Plan"||text.Contains("Repair"))).ToArray();check(buttons.Length==2&&buttons.Any(x=>Equals(x.Content,"Back"))&&buttons.Any(x=>Equals(x.Content,"Use Reviewed Plan")),"Review offers only Back and Use Reviewed Plan in "+mode);
    var box=Find<TextBox>(dialog).Single();check(box.Text.Contains("Update: Playlist")&&!box.Text.Contains("Repair"),"Ordinary update review contains no deferred repair wording");
    var bg=((SolidColorBrush)dialog.Background).Color;var fg=((SolidColorBrush)box.Foreground).Color;var boxBg=((SolidColorBrush)box.Background).Color;
    Console.WriteLine($"Theme {mode}: dialog={bg}, details={boxBg}, text={fg}");
    check(mode==ThemeMode.Dark?bg.R<80&&boxBg.R<80&&fg.R>170:bg.R>170&&boxBg.R>170&&fg.R<80,"Review and details follow "+mode+" theme with readable contrast");
    string? output=Environment.GetEnvironmentVariable("MMZS_UI_ARTIFACT_DIRECTORY");if(!string.IsNullOrEmpty(output)){Directory.CreateDirectory(output);var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)dialog.ActualWidth,(int)dialog.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,"review-"+mode+".png"));encoder.Save(file);}
    buttons.Single(x=>Equals(x.Content,"Use Reviewed Plan")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   }catch(Exception e){failure=e;dialog?.Close();}}),DispatcherPriority.ApplicationIdle);
   typeof(MainWindow).GetMethod("ReviewSynchronization_Click",flags)!.Invoke(owner,new object[]{owner,new RoutedEventArgs()});if(failure!=null)throw failure;
   check(flow.CanRun,"Normal review approval enables synchronization in "+mode);typeof(MainWindow).GetMethod("InvalidateWorkflow",flags)!.Invoke(owner,null);
  }
  Application.Current.ThemeMode=prior;Pump();
  var help=new HelpWindow("Synchronization"){Owner=owner};help.Show();Pump();
  var document=((FlowDocumentScrollViewer)help.FindName("TopicBody")).Document;
  string text=new System.Windows.Documents.TextRange(document.ContentStart,document.ContentEnd).Text;
  check(text.Contains("Use Reviewed Plan approves")&&text.Contains("click Run Synchronization")&&!text.Contains("Repair Existing Playlists"),"Synchronization Help explains approval and execution without deferred repair guidance");help.Close();
#pragma warning restore WPF0001
 }
}
