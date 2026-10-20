using System.Net.Http;
using MediaMonkeyZidooSync.Services;

internal static class ReadinessChecks
{
    private sealed class Clock:TimeProvider
    {
        public long Ticks;public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>Ticks;
        public Task Delay(TimeSpan duration,CancellationToken ct){ct.ThrowIfCancellationRequested();Ticks+=duration.Ticks;return Task.CompletedTask;}
    }
    public static async Task Run()
    {
        int checks=0;
        void Assert(bool value){if(!value)throw new Exception("Readiness assertion failed.");checks++;}
        var clock=new Clock();int sends=0,probes=0;
        await NativeZidooReadiness.WaitAsync(_=>{probes++;if(sends<2)throw new HttpRequestException("Wake listener not ready");return Task.CompletedTask;},
            _=>{sends++;return Task.CompletedTask;},TimeSpan.FromSeconds(40),null,default,clock,clock.Delay);
        Assert(sends==2 && probes==6 && clock.Ticks==TimeSpan.FromSeconds(15).Ticks);
        clock=new Clock();sends=0;
        await NativeZidooReadiness.WaitAsync(_=>Task.CompletedTask,_=>{sends++;return Task.CompletedTask;},TimeSpan.FromSeconds(40),null,default,clock,clock.Delay);
        Assert(sends==1 && clock.Ticks==0);
        clock=new Clock();sends=0;bool timedOut=false;
        try{await NativeZidooReadiness.WaitAsync(_=>throw new HttpRequestException(),_=>{sends++;return Task.CompletedTask;},TimeSpan.FromSeconds(31),null,default,clock,clock.Delay);}
        catch(TimeoutException){timedOut=true;}
        Assert(timedOut && sends==3 && clock.Ticks==TimeSpan.FromSeconds(31).Ticks);
        clock=new Clock();sends=0;bool refused=false;
        try{await NativeZidooReadiness.WaitAsync(_=>throw new InvalidDataException("Wrong device"),_=>{sends++;return Task.CompletedTask;},TimeSpan.FromSeconds(40),null,default,clock,clock.Delay);}
        catch(InvalidDataException){refused=true;}
        Assert(refused && sends==1 && clock.Ticks==0);
        using var cancel=new CancellationTokenSource();cancel.Cancel();sends=0;bool cancelled=false;
        try{await NativeZidooReadiness.WaitAsync(_=>Task.CompletedTask,_=>{sends++;return Task.CompletedTask;},TimeSpan.FromSeconds(40),null,cancel.Token);}
        catch(OperationCanceledException){cancelled=true;}
        Assert(cancelled && sends==0);
        Console.WriteLine($"PASS: {checks} bounded wake/readiness scenarios; simulated clock and transport only.");
    }
}
