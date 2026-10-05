using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RSEV.Utilities.Messaging;
using RSEV.Utilities.Runtime;

namespace ioBroker_NewGen.Core.Bus
{
    /// <summary>
    /// Zentraler, neutraler Datenbus des Systems. Transportiert
    /// <see cref="BusFrame"/>-Payloads, ohne den Adaptertyp zu kennen.
    /// Basiert auf dem Lifecycle-Grundgerüst von
    /// <see cref="RSEV.Utilities.Messaging.BaseMessageBus"/> (Init/Shutdown)
    /// aus RSEV.Utilities und implementiert zusätzlich eigenes
    /// Publish/Subscribe-Verhalten für den Smarthome-Systembus.
    /// </summary>
    public sealed class SystemMessageBus : BaseMessageBus
    {
        public SystemMessageBus()
            : base("SystemBus", "Zentraler neutraler Datenbus – transportiert BusFrame-Payloads ohne Kenntnis des Adaptertyps.")
        {
        }

        private readonly ConcurrentDictionary<string, List<Action<BusFrame>>> _subscribers = new();
        private readonly object _subscriptionLock = new();

        private IRuntimeContext? _context;

        /// <summary>
        /// Veröffentlicht ein Frame an alle Subscriber der jeweiligen Adresse
        /// sowie an alle Wildcard-Subscriber (Adresse "*").
        /// </summary>
        public void Publish(BusFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);

            NotifySubscribers(frame.Address, frame);
            if (frame.Address != "*")
            {
                NotifySubscribers("*", frame);
            }
        }

        /// <summary>
        /// Registriert einen Callback für Frames einer bestimmten Adresse.
        /// Die Adresse "*" abonniert alle Frames (Wildcard).
        /// </summary>
        public void Subscribe(string address, Action<BusFrame> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);

            lock (_subscriptionLock)
            {
                if (!_subscribers.TryGetValue(address, out var callbacks))
                {
                    callbacks = new List<Action<BusFrame>>();
                    _subscribers[address] = callbacks;
                }

                callbacks.Add(callback);
            }
        }

        /// <summary>
        /// Entfernt einen zuvor registrierten Subscription-Callback.
        /// </summary>
        public void Unsubscribe(string address, Action<BusFrame> callback)
        {
            lock (_subscriptionLock)
            {
                if (_subscribers.TryGetValue(address, out var callbacks))
                {
                    callbacks.Remove(callback);
                }
            }
        }

        private void NotifySubscribers(string address, BusFrame frame)
        {
            List<Action<BusFrame>>? callbacks;
            lock (_subscriptionLock)
            {
                if (!_subscribers.TryGetValue(address, out var existing))
                {
                    return;
                }

                callbacks = existing.ToList();
            }

            foreach (var callback in callbacks)
            {
                try
                {
                    callback(frame);
                }
                catch (Exception ex)
                {
                    _context?.Logger.LogError($"[SystemBus] Fehler in Subscriber für '{address}': {ex}");
                }
            }
        }

        protected override void OnInitialize(IRuntimeContext context)
        {
            _context = context;
            context.Logger.LogInfo("[SystemBus] Initialisiert.");
        }

        protected override Task OnInitializeAsync(IRuntimeContext context)
        {
            OnInitialize(context);
            return Task.CompletedTask;
        }

        protected override void OnShutdown(IRuntimeContext context)
        {
            context.Logger.LogInfo("[SystemBus] Shutdown.");
            lock (_subscriptionLock)
            {
                _subscribers.Clear();
            }
        }

        protected override Task OnShutdownAsync(IRuntimeContext context)
        {
            OnShutdown(context);
            return Task.CompletedTask;
        }
    }
}
