/// <summary>Forces a real save failure on Windows and Linux without relying on rename/share-lock semantics.</summary>
internal sealed class StateWriteBarrier : IDisposable
{
    private readonly string primary;
    private readonly string preserved;

    public StateWriteBarrier(string fixtureDirectory)
    {
        var root = Path.GetFullPath(fixtureDirectory);
        primary = Path.Combine(root,"state.json");
        preserved = Path.Combine(root,"state-before-block-" + Guid.NewGuid().ToString("N") + ".json");
        File.Move(primary,preserved);
        try { Directory.CreateDirectory(primary); }
        catch { File.Move(preserved,primary); throw; }
    }

    public void Dispose()
    {
        // The barrier directory is ours and empty; never recursively remove fixture/user state.
        Directory.Delete(primary);
        File.Move(preserved,primary);
    }
}
