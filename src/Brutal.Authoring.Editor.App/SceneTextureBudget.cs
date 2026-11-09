namespace Brutal.Authoring.Editor.App;

// Render-thread ledger used by material upload resources. Charges RGBA8 payload bytes, not driver allocator overhead.
// Reservations are held through replacement overlap; release is the owner's fence-safe responsibility.
internal sealed class SceneTextureBudget
{
    internal const long ImageLimit = 256L * 1024 * 1024;
    internal const long StagingLimit = 64L * 1024 * 1024;
    internal const int DescriptorLimit = 1024;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<object,int> descriptors = new(ReferenceEqualityComparer.Instance);
    internal long ImageBytes { get; private set; }
    internal long StagingBytes { get; private set; }
    internal int DescriptorCount(object view) { Check(); return descriptors.GetValueOrDefault(view); }
    internal Reservation Reserve(long imageBytes, long stagingBytes)
    {
        Check();
        if(imageBytes <= 0 || stagingBytes < 0) throw new ArgumentOutOfRangeException(nameof(imageBytes));
        if(imageBytes > ImageLimit - ImageBytes) throw new InvalidOperationException("Material texture image budget exceeds 256 MiB, including retained replacements.");
        if(stagingBytes > StagingLimit - StagingBytes) throw new InvalidOperationException("Material texture upload staging exceeds 64 MiB in flight.");
        ImageBytes += imageBytes; StagingBytes += stagingBytes;
        return new(this,imageBytes,stagingBytes);
    }
    internal Descriptor ReserveDescriptor(object view)
    {
        Check(); ArgumentNullException.ThrowIfNull(view);
        var count=descriptors.GetValueOrDefault(view);
        if(count >= DescriptorLimit) throw new InvalidOperationException("Material texture descriptors exceed 1,024 for this view.");
        descriptors[view]=count+1;return new(this,view);
    }
    internal sealed class Reservation(SceneTextureBudget owner,long image,long staging) : IDisposable
    {
        private bool disposed;
        internal void ReleaseStaging()
        {
            owner.Check();if(disposed)return;
            owner.StagingBytes-=staging;staging=0;
        }
        public void Dispose()
        {
            owner.Check();if(disposed)return;
            ReleaseStaging();owner.ImageBytes-=image;disposed=true;
        }
    }
    internal sealed class Descriptor(SceneTextureBudget owner,object view) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            owner.Check();if(disposed)return;disposed=true;
            var count=owner.descriptors[view]-1;
            if(count==0)owner.descriptors.Remove(view);else owner.descriptors[view]=count;
        }
    }
    private void Check()
    {
        if(Environment.CurrentManagedThreadId!=thread)throw new InvalidOperationException("Material texture budgets belong to the render thread.");
    }
}
