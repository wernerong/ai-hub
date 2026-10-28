namespace AIHub;

internal sealed class WorkspaceActivation
{
    private bool pending;
    internal void Request() => pending = true;
    internal void Cancel() => pending = false;

    internal bool TakeRequest(bool eligible, bool inputInProgress)
    {
        if (!pending) return false;
        if (!eligible) { pending = false; return false; }
        if (inputInProgress) return false;
        pending = false;
        return true;
    }

}
