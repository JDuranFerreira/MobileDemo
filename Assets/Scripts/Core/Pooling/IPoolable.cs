namespace MobileDemo.Core.Pooling
{
    // Deliberately not OnEnable/OnDisable: those stay free for EventBus subscription, because a
    // pooled object is disabled rather than destroyed. OnSpawn is the sole initializer -- prewarm
    // never calls OnDespawn, so nothing may depend on it having run.
    public interface IPoolable
    {
        void OnSpawn();
        void OnDespawn();
    }
}
