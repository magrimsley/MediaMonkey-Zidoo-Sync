using System.Net;
using System.Text.Json;
using MediaMonkeyZidooSync.Models;
using MediaMonkeyZidooSync.Services;

internal static class SortOrderChecks
{
    public static async Task Run()
    {
        int passed=0;
        foreach(string initial in new[]{"newest","az","za","stale-az"})
        {
            using var replay=new Grid(initial);
            using var http=new HttpClient(replay){BaseAddress=new Uri("http://offline.invalid:9529")};
            var device=new NativeZidooPlaylistDevice(http,Config(),(_,ct)=>{ct.ThrowIfCancellationRequested();return Task.CompletedTask;});
            await device.DeleteExactAsync(new("10","Alpha",1,0));
            if(replay.RemovedId!="10"||replay.Rows.Count!=4||!replay.SawReverse||!replay.SawName||replay.BackCount!=1)
                throw new Exception("Sort normalization failed from "+initial);
            passed++;
        }
        for(int boundary=1;boundary<=13;boundary++)
        {
            using var replay=new Grid("stale-az"){RejectNormalizationKey=boundary};
            using var http=new HttpClient(replay){BaseAddress=new Uri("http://offline.invalid:9529")};
            var device=new NativeZidooPlaylistDevice(http,Config(),(_,ct)=>{ct.ThrowIfCancellationRequested();return Task.CompletedTask;});
            try{await device.DeleteExactAsync(new("10","Alpha",1,0));throw new Exception("Rejected normalization key accepted.");}
            catch(InvalidDataException){if(replay.RemovedId is not null||replay.Keys.Contains("Key.Select"))throw new Exception("Mutation after failed sort normalization.");}
            passed++;
        }
        for(int boundary=1;boundary<=13;boundary++)
        {
            using var cancel=new CancellationTokenSource();
            using var replay=new Grid("stale-az");
            using var http=new HttpClient(replay){BaseAddress=new Uri("http://offline.invalid:9529")};
            int stop=24+boundary;
            var device=new NativeZidooPlaylistDevice(http,Config(),(_,ct)=>
            {
                if(replay.Keys.Count==stop)cancel.Cancel();
                ct.ThrowIfCancellationRequested();return Task.CompletedTask;
            });
            try{await device.DeleteExactAsync(new("10","Alpha",1,0),cancel.Token);throw new Exception("Sort cancellation accepted as success.");}
            catch(OperationCanceledException){if(replay.RemovedId is not null||replay.Keys.Contains("Key.Select"))throw new Exception("Mutation after sort cancellation.");}
            passed++;
        }
        Console.WriteLine($"PASS: {passed} stateful visible-sort, stale-preference, failure and cancellation scenarios; fake device only.");
    }
    private static AppConfig Config()
    {
        var config=new AppConfig();config.Zidoo.ExpectedModel="TEST";config.Zidoo.PlaylistGridColumns=5;return config;
    }
    private sealed class Grid:HttpMessageHandler
    {
        public List<ZidooPlaylistRecord> Rows=new(){new("10","Alpha",1,0),new("20","Beta",1,1),new("80","Gamma",1,2),new("100","Jazz",1,3),new("120","Zulu",1,4)};
        public List<string> Keys=new();
        public string? RemovedId;
        public bool SawReverse,SawName;
        public int BackCount,RejectNormalizationKey;
        private string mode="home",preference,rendered;
        private int menu,position,normalizationKeys;
        public Grid(string initial)
        {
            preference=initial=="stale-az"?"az":initial;
            rendered=initial=="stale-az"?"newest":initial;
        }
        private ZidooPlaylistRecord[] Visible()=>rendered switch
        {
            "az"=>Rows.OrderBy(x=>x.Name,StringComparer.Ordinal).ToArray(),
            "za"=>Rows.OrderByDescending(x=>x.Name,StringComparer.Ordinal).ToArray(),
            _=>Rows.OrderByDescending(x=>int.Parse(x.Id)).ToArray()
        };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();var uri=request.RequestUri!;string json="{\"status\":200}";
            if(uri.AbsolutePath.EndsWith("getModel"))json="{\"status\":200,\"model\":\"TEST\"}";
            else if(uri.AbsolutePath.EndsWith("getSongLists"))
                json=JsonSerializer.Serialize(Rows.Select(row=>new{obj=new{id=row.Id,name=row.Name},count=row.Count}));
            else if(uri.AbsolutePath.EndsWith("openApp")){mode="home";menu=0;}
            else if(uri.AbsolutePath.EndsWith("sendkey"))
            {
                string key=Uri.UnescapeDataString(uri.Query[5..]);Keys.Add(key);
                if(Keys.Count>24&&normalizationKeys<13)
                {
                    normalizationKeys++;
                    if(normalizationKeys==RejectNormalizationKey)return Reply("{\"status\":500}");
                }
                switch(mode)
                {
                    case "home":
                        if(key=="Key.Up")menu=0;
                        else if(key=="Key.Down")menu++;
                        else if(key=="Key.Ok"&&menu==2)mode="toolbar";
                        else throw new Exception("Unexpected home key "+key);
                        break;
                    case "toolbar":
                        if(key!="Key.Down")throw new Exception("Unexpected toolbar key "+key);
                        mode="grid";position=0;break;
                    case "grid":
                        if(key=="Key.Up"){position=Math.Max(0,position-5);break;}
                        if(key=="Key.Left"&&position==0)mode="sort-control";
                        else if(key=="Key.Down")position=Math.Min(position+5,Rows.Count-1);
                        else if(key=="Key.Right")position=Math.Min(position+1,Rows.Count-1);
                        else if(key=="Key.Ok")mode="details";
                        else throw new Exception("Unexpected grid key "+key);
                        break;
                    case "sort-control":
                        if(key=="Key.Right"){mode="grid";position=0;break;}
                        if(key!="Key.Ok")throw new Exception("Unexpected sort-control key "+key);
                        mode="sort-menu";menu=0;break;
                    case "sort-menu":
                        if(key=="Key.Up")menu=Math.Max(0,menu-1);
                        else if(key=="Key.Down")menu=Math.Min(3,menu+1);
                        else if(key=="Key.Ok")
                        {
                            string selected=menu==2?"az":menu==3?"za":"newest";
                            // Match the observed stale-preference condition: selecting
                            // the checked preference alone need not rebuild the grid.
                            if(selected!=preference)rendered=selected;
                            preference=selected;
                            if(selected=="za")SawReverse=true;
                            if(selected=="az")SawName=true;
                        }
                        else if(key=="Key.Back"){BackCount++;mode="sort-control";}
                        else throw new Exception("Unexpected sort-menu key "+key);
                        break;
                    case "details":
                        if(key!="Key.Left")throw new Exception("Unexpected details key "+key);
                        mode="details-control";break;
                    case "details-control":
                        if(key!="Key.Ok")throw new Exception("Unexpected details-control key "+key);
                        mode="actions";menu=0;break;
                    case "actions":
                        if(key=="Key.Down")menu++;
                        else if(key=="Key.Ok"&&menu==5)mode="confirm";
                        else throw new Exception("Unexpected actions key "+key);
                        break;
                    case "confirm":
                        if(key!="Key.Select")throw new Exception("Unexpected confirmation key "+key);
                        var selectedRow=Visible()[position];RemovedId=selectedRow.Id;Rows.Remove(selectedRow);mode="grid";break;
                    default:throw new Exception("Unknown UI mode.");
                }
            }
            else throw new Exception("Unexpected route "+uri);
            return Reply(json);
        }
        private static Task<HttpResponseMessage> Reply(string json)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
    }
}
