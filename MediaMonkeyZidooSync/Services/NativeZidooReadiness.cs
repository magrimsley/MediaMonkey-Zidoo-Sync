using System.Net.Http;

namespace MediaMonkeyZidooSync.Services;

/// <summary>Bounded model readiness with repeated idempotent wake requests.
/// A device may stop answering HTTP before shutdown has enabled its wake listener.</summary>
public static class NativeZidooReadiness
{
    public static async Task WaitAsync(Func<CancellationToken,Task> verify,
        Func<CancellationToken,Task> wake,TimeSpan timeout,Action<TimeSpan>? progress,
        CancellationToken ct,TimeProvider? clock=null,Func<TimeSpan,CancellationToken,Task>? delay=null)
    {
        ArgumentNullException.ThrowIfNull(verify);ArgumentNullException.ThrowIfNull(wake);
        if(timeout<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(timeout));
        clock??=TimeProvider.System;delay??=((duration,token)=>Task.Delay(duration,token));
        long started=clock.GetTimestamp();TimeSpan nextWake=TimeSpan.Zero,nextProgress=TimeSpan.Zero;
        Exception? last=null;
        while(clock.GetElapsedTime(started)<timeout)
        {
            ct.ThrowIfCancellationRequested();TimeSpan elapsed=clock.GetElapsedTime(started);
            if(elapsed>=nextWake){await wake(ct).ConfigureAwait(false);nextWake=clock.GetElapsedTime(started)+TimeSpan.FromSeconds(15);}
            if(elapsed>=nextProgress){progress?.Invoke(timeout-elapsed);nextProgress=elapsed+TimeSpan.FromSeconds(10);}
            try{await verify(ct).ConfigureAwait(false);return;}
            catch(HttpRequestException error){last=error;}
            catch(TaskCanceledException error)when(!ct.IsCancellationRequested){last=error;}
            TimeSpan remaining=timeout-clock.GetElapsedTime(started);
            if(remaining>TimeSpan.Zero)await delay(remaining<TimeSpan.FromSeconds(3)?remaining:TimeSpan.FromSeconds(3),ct).ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();
        throw new TimeoutException("Zidoo did not become ready before the configured timeout.",last);
    }
}
