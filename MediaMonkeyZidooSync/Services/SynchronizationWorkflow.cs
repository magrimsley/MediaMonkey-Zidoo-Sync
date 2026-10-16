namespace MediaMonkeyZidooSync.Services;

/// <summary>Required user sequence; optional diagnostics cannot authorize synchronization.</summary>
public sealed class SynchronizationWorkflow
{
    public bool Validated { get; private set; }
    public bool Verified { get; private set; }
    public bool Reviewed { get; private set; }
    public bool Busy { get; private set; }
    private int _generation, _operationGeneration;
    public bool CanValidate => !Busy;
    public bool CanVerify => !Busy && Validated;
    public bool CanReview => !Busy && Verified;
    public bool CanRun => !Busy && Reviewed;
    public void Invalidate() { _generation++;Validated = Verified = Reviewed = false; }
    public void BeginValidation() { if(!CanValidate)throw new InvalidOperationException("Another step is running.");Invalidate();_operationGeneration=_generation;Busy=true; }
    public void EndValidation(bool passed) { Validated=passed && _operationGeneration==_generation;Busy=false; }
    public void BeginVerification() { if(!CanVerify)throw new InvalidOperationException("Validate MediaMonkey first.");Verified=Reviewed=false;_operationGeneration=_generation;Busy=true; }
    public void EndVerification(bool passed) { Verified=passed && Validated && _operationGeneration==_generation;Busy=false; }
    public void AcceptReview() { if(!CanReview)throw new InvalidOperationException("Verify the exporter first.");Reviewed=true; }
    public void BeginRun() { if(!CanRun)throw new InvalidOperationException("Review the changes first.");Busy=true; }
    public void EndRun() { Busy=false;Invalidate(); }
    public void SetDiagnosticBusy(bool busy) => Busy=busy;
    public string NextStep => Busy ? "Please wait for the current step to finish." :
        !Validated ? "Step 1: Validate MediaMonkey playlists." : !Verified ? "Step 2: Verify Native Exporter." :
        !Reviewed ? "Step 3: Review the proposed changes." : "Step 4: Run Synchronization when ready.";
}
