using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileDemo.Core.Pooling
{
    public sealed class ObjectPool<T> : IPoolStats where T : Component, IPoolable
    {
        readonly T prefab;
        readonly Transform parent;
        readonly Stack<T> available;
        readonly HashSet<T> active;
        bool hasWarnedOnGrowth;

        public ObjectPool(T prefab, int prewarm, Transform parent = null)
        {
            // A pool with no prefab cannot exist, so this throws. A negative prewarm is harmless
            // and clamps instead -- dying over a bad tuning number would contradict the
            // grow-and-warn stance Get() takes on the same mistake.
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            int count = Mathf.Max(0, prewarm);

            this.prefab = prefab;
            this.parent = parent;
            Name = prefab.name;
            Prewarm = count;
            available = new Stack<T>(count);
            active = new HashSet<T>(count);

            for (int i = 0; i < count; i++)
            {
                available.Push(CreateInstance());
            }
        }

        public string Name { get; }

        public int Prewarm { get; }

        public int PeakActive { get; private set; }

        public int ActiveCount => active.Count;

        public int AvailableCount => available.Count;

        public int InstanceCount => active.Count + available.Count;

        public T Get()
        {
            // Instances can be destroyed behind the pool's back -- scene unload, or a caller that
            // used Destroy where it should have used Release. Discard those rather than handing
            // back a reference that throws on first touch.
            T item = null;
            while (!IsAlive(item) && available.Count > 0)
            {
                item = available.Pop();
            }

            if (!IsAlive(item))
            {
                item = CreateInstance();
                WarnOnceOnGrowth();
            }

            // Book-keeping before the callback, so OnSpawn cannot observe a half-registered pool
            // -- an OnSpawn that releases the instance straight back would otherwise be rejected.
            active.Add(item);
            if (active.Count > PeakActive)
            {
                PeakActive = active.Count;
            }

            // SetActive before OnSpawn: a coroutine started in OnSpawn throws on an inactive
            // GameObject, and a tween on an inactive transform is meaningless.
            item.gameObject.SetActive(true);
            item.OnSpawn();

            return item;
        }

        public bool Release(T item)
        {
            if (!IsAlive(item))
            {
                // Still free the slot, or ActiveCount never drops and the pool grows forever.
                active.Remove(item);
                Debug.LogWarning(
                    $"Pool '{Name}': released a null or destroyed instance. "
                    + "Never Destroy a pooled object -- Release it.");
                return false;
            }

            if (!active.Remove(item))
            {
                // A double release or an instance belonging to somewhere else. Warn every time,
                // unlike growth's warn-once: growth is a tuning miss with a bounded cost, this is
                // a caller defect that hands the same instance to two owners. Leave the object
                // untouched -- deactivating something the pool does not own is a bug of our own.
                Debug.LogWarning(
                    $"Pool '{Name}': rejected the release of '{item.name}' -- it is not active "
                    + "in this pool. Double release, or it belongs to another pool.");
                return false;
            }

            // OnDespawn before SetActive(false), so it can stop coroutines, kill tweens and spawn
            // a death effect while the object is still active.
            item.OnDespawn();
            item.gameObject.SetActive(false);

            available.Push(item);
            return true;
        }

        T CreateInstance()
        {
            // One create path for prewarm and growth alike, so growth cannot drift from it. The
            // (prefab, parent) overload avoids a second reparent per instance.
            T instance = UnityEngine.Object.Instantiate(prefab, parent);
            instance.gameObject.SetActive(false);
            return instance;
        }

        void WarnOnceOnGrowth()
        {
            if (hasWarnedOnGrowth)
            {
                return;
            }

            hasWarnedOnGrowth = true;
            Debug.LogWarning(
                $"Pool '{Name}' grew past its prewarm of {Prewarm}. Raise it -- PeakActive after "
                + "a full run is the number to use. Warned once; InstanceCount > Prewarm lasts.");
        }

        // A generic T never binds UnityEngine.Object's overloaded ==, so a destroyed instance
        // would compare non-null here by plain reference equality. Casting to Object is what
        // brings the engine's check back into play, and it covers a literal null in the same test.
        static bool IsAlive(T item) => (UnityEngine.Object)item != null;
    }
}
