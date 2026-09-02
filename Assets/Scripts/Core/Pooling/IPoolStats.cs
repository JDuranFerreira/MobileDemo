namespace MobileDemo.Core.Pooling
{
    // ObjectPool<Enemy> and ObjectPool<Projectile> are unrelated closed types, so without a
    // non-generic read surface nothing can hold a collection of pools. That is the whole reason
    // this interface exists -- it is structural, not anticipation of a tool.
    public interface IPoolStats
    {
        string Name { get; }

        int Prewarm { get; }

        int InstanceCount { get; }

        int ActiveCount { get; }

        int AvailableCount { get; }

        /// <summary>Whole-session high-water mark, never reset — the number to tune prewarm to.</summary>
        int PeakActive { get; }
    }
}
