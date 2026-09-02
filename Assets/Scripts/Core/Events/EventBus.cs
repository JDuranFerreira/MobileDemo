using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileDemo.Core.Events
{
    public static class EventBus
    {
        static readonly List<Action> Resetters = new List<Action>();
        internal static void RegisterResetter(Action reset) => Resetters.Add(reset);
        public static void ClearAll()
        {
            for (int i = 0; i < Resetters.Count; i++)
            {
                Resetters[i]();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearOnEnterPlayMode() => ClearAll();
    }
    public static class EventBus<TEvent> where TEvent : struct, IEvent
    {
        static Action<TEvent> handlers;

        // Runs once, the first time this event type is used anywhere. Registering here rather
        // than at every Subscribe call means the reset list holds exactly one entry per type.
        static EventBus() => EventBus.RegisterResetter(Clear);
        public static void Subscribe(Action<TEvent> handler) => handlers += handler;
        public static void Unsubscribe(Action<TEvent> handler) => handlers -= handler;
        public static void Publish(in TEvent evt) => handlers?.Invoke(evt);
        public static void Clear() => handlers = null;
    }
}
