using System.IO;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MediaMonkeyZidooSync.Services;

internal static class RC9ServiceChecks
{
    public static void Run(string root,Action<bool,string> check)
    {
        string folder=Path.Combine(root,"ReviewedRepairFixture");Directory.CreateDirectory(folder);
        byte[] bytes=Encoding.UTF8.GetBytes("#EXTM3U\r\n/Music/b.flac\r\n/Music/a.flac\r\n");
        string hash=Convert.ToHexString(SHA256.HashData(bytes));string path=Path.Combine(folder,"Target.m3u");File.WriteAllBytes(path,bytes);
        var approved=new NativeProductionSyncService.ReviewedChange("Target.m3u",2,hash,hash,"Repair",0,true);
        var expected=new Dictionary<string,(byte[] Content,int Count)>{{"Target.m3u",(bytes,2)}};
        var work=typeof(NativeProductionSyncService).GetNestedType("Work",BindingFlags.NonPublic)!;
        var listType=typeof(List<>).MakeGenericType(work);
        var add=typeof(NativeProductionSyncService).GetMethod("AddReviewedRepairs",BindingFlags.NonPublic|BindingFlags.Static)!;
        IList Apply(IReadOnlyList<NativeProductionSyncService.ReviewedChange> review)
        {
            var list=(IList)Activator.CreateInstance(listType)!;
            try{add.Invoke(null,new object[]{folder,review,expected,list});}catch(TargetInvocationException e)when(e.InnerException is not null){throw e.InnerException;}
            return list;
        }
        var result=Apply(new[]{approved});
        check(result.Count==1&&(bool)work.GetProperty("ReimportExisting")!.GetValue(result[0])!,"Explicit matching-file repair enters executable plan");
        check(Apply(Array.Empty<NativeProductionSyncService.ReviewedChange>()).Count==0,"No selection adds no repair and authorizes no device work");
        void Reject(NativeProductionSyncService.ReviewedChange[] review,string title)
        {bool refused=false;try{Apply(review);}catch(InvalidDataException){refused=true;}check(refused,title);}
        Reject(new[]{approved,approved},"Duplicate selected repairs are rejected");
        Reject(new[]{approved with {CandidateSHA256="wrong"}},"Changed candidate export is rejected before repair");
        Reject(new[]{approved with {FileName="..\\Target.m3u"}},"Unexported or traversing repair names are rejected");
        Reject(new[]{approved with {ExpectedCount=3}},"Changed reviewed repair count is rejected");
        File.SetAttributes(path,FileAttributes.Hidden);
        Reject(new[]{approved},"Metadata-only change since repair review is rejected");
        File.SetAttributes(path,FileAttributes.Normal);File.AppendAllText(path,"/Music/external.flac\r\n");
        Reject(new[]{approved},"File edit since repair review is rejected");
        var flow=new SynchronizationWorkflow();flow.BeginValidation();flow.EndValidation(true);flow.BeginVerification();flow.EndVerification(true,false);
        check(!flow.CanRun&&!flow.CanReview,"No differences cannot authorize a normal synchronization");
        bool stopped=false;try{flow.AuthorizeReviewedChanges(false);}catch(InvalidOperationException){stopped=true;}
        check(stopped&&!flow.CanRun,"Empty repair selection cannot authorize synchronization");
        flow.AuthorizeReviewedChanges(true);check(flow.CanRun&&flow.HasChanges,"Explicit repair review authorizes matching-file repair");
        flow.Invalidate();stopped=false;try{flow.AuthorizeReviewedChanges(true);}catch(InvalidOperationException){stopped=true;}
        check(stopped&&!flow.CanRun,"Configuration invalidation revokes repair authorization");
        check(UserMessages.Completed(2).Message.Contains("device track counts")&&UserMessages.Completed(2).Message.Contains("confirm song order"),"Completion describes verification limits");
    }
}
