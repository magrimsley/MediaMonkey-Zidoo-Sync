using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using MediaMonkeyZidooSync;
using MediaMonkeyZidooSync.Services;

internal static class RC9RepairUiChecks
{
    static System.Collections.Generic.IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T t)yield return t;foreach(var nested in Find<T>(child))yield return nested;}}
    static void Capture(Window window,string name)
    {
        string? output=Environment.GetEnvironmentVariable("MMZS_UI_ARTIFACT_DIRECTORY");if(string.IsNullOrWhiteSpace(output))return;
        System.IO.Directory.CreateDirectory(output);
        var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);window.UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file=System.IO.File.Create(System.IO.Path.Combine(output,name));encoder.Save(file);
    }
    public static void Run(MainWindow owner,Action<bool,string> check)
    {
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var flow=(SynchronizationWorkflow)typeof(MainWindow).GetField("_workflow",flags)!.GetValue(owner)!;
        void Prepare()
        {
            flow.BeginValidation();flow.EndValidation(true);flow.BeginVerification();flow.EndVerification(true,false);
            typeof(MainWindow).GetField("_preparedReview",flags)!.SetValue(owner,Array.Empty<NativeProductionSyncService.ReviewedChange>());
            typeof(MainWindow).GetField("_repairCandidates",flags)!.SetValue(owner,new[]{new NativeProductionSyncService.ReviewedChange("1960s.m3u",4,"original","candidate","Repair",0,true),new NativeProductionSyncService.ReviewedChange("Hootie & Smash Mouth.m3u",12,"original2","candidate2","Repair",0,true)});
        }
        Prepare();Exception? callbackFailure=null;
        var timeout=new DispatcherTimer{Interval=TimeSpan.FromSeconds(8)};
        timeout.Tick+=(_,_)=>{callbackFailure=new Exception("Repair review UI timed out");foreach(var w in Application.Current.Windows.OfType<Window>().Where(x=>x!=owner).ToArray())w.Close();timeout.Stop();};timeout.Start();
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(()=>
        {
            try
            {
                var review=Application.Current.Windows.OfType<Window>().Single(x=>x.Title=="Review Synchronization Changes");
                check(!Find<Button>(review).Single(x=>x.Name=="AcceptReviewedPlanButton").IsEnabled,"Empty matching-file review cannot enable Run");
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(()=>
                {
                    try
                    {
                        var picker=Application.Current.Windows.OfType<Window>().Single(x=>x.Title=="Repair Existing Zidoo Playlists");
                        var list=Find<ListBox>(picker).Single(x=>x.Name=="RepairPlaylistList");
                        var add=Find<Button>(picker).Single(x=>x.Name=="AddSelectedRepairsButton");
                        check(list.SelectedItems.Count==0&&!add.IsEnabled,"Repair picker selects no playlists by default");
                        list.SelectedIndex=0;Capture(picker,"rc9-repair-picker.png");add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch(Exception e){callbackFailure=e;foreach(var w in Application.Current.Windows.OfType<Window>().Where(x=>x!=owner).ToArray())w.Close();}
                }),DispatcherPriority.ApplicationIdle);
                Find<Button>(review).Single(x=>x.Name=="RepairExistingPlaylistsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(callbackFailure is not null)return;
                check(Find<TextBox>(review).Single().Text.Contains("Repair (delete and reimport): 1960s")&&!Find<TextBox>(review).Single().Text.Contains("Hootie"),"Review explicitly lists only selected repair");
                Capture(review,"rc9-selected-repair-review.png");
                Find<Button>(review).Single(x=>x.Name=="AcceptReviewedPlanButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch(Exception e){callbackFailure=e;foreach(var w in Application.Current.Windows.OfType<Window>().Where(x=>x!=owner).ToArray())w.Close();}
        }),DispatcherPriority.ApplicationIdle);
        typeof(MainWindow).GetMethod("ReviewSynchronization_Click",flags)!.Invoke(owner,new object[]{owner,new RoutedEventArgs()});
        timeout.Stop();if(callbackFailure is not null)throw callbackFailure;
        var approved=(System.Collections.Generic.IReadOnlyList<NativeProductionSyncService.ReviewedChange>)typeof(MainWindow).GetField("_approvedReview",flags)!.GetValue(owner)!;
        check(flow.CanRun&&approved.Count==1&&approved[0].ReimportExisting&&approved[0].FileName=="1960s.m3u","Explicit review authorizes selected repair when files match");
        typeof(MainWindow).GetMethod("InvalidateWorkflow",flags)!.Invoke(owner,null);
        check(!flow.CanRun,"UI invalidation revokes selected repair plan");
    }
}
