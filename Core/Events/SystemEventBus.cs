using System.Threading.Tasks;
using RSEV.Utilities.Events;
using RSEV.Utilities.Runtime;

namespace ioBroker_NewGen.Core.Events
{
    /// <summary>
    /// Zentraler EventBus auf Basis der RSEV.Utilities-Implementierung.
    /// Es gibt keine lokale Handler-/Listener-Verwaltung.
    /// </summary>
    public sealed class SystemEventBus : BaseEventBus
    {
        public const string StateChangedEventName = "state.changed";
        public const string AdapterLifecycleEventName = "adapter.lifecycle";

        public SystemEventBus()
            : base("SystemEventBus", "RSEV.Utilities-basierter EventBus für State- und Adapterereignisse.")
        {
            StateChanged = new EventRegistry<StateChangedEventArgs>(StateChangedEventName);
            AdapterLifecycle = new EventRegistry<AdapterLifecycleEventArgs>(AdapterLifecycleEventName);
            RegisterEventRegistry(StateChanged);
            RegisterEventRegistry(AdapterLifecycle);
        }

        public EventRegistry<StateChangedEventArgs> StateChanged { get; }

        public EventRegistry<AdapterLifecycleEventArgs> AdapterLifecycle { get; }

        public void PublishStateChanged(object sender, StateChangedEventArgs args)
        {
            StateChanged.Publish(sender, args);
        }

        public Task PublishStateChangedAsync(object sender, StateChangedEventArgs args)
        {
            return StateChanged.PublishAsync(sender, args);
        }

        public void PublishAdapterLifecycle(object sender, AdapterLifecycleEventArgs args)
        {
            AdapterLifecycle.Publish(sender, args);
        }

        public Task PublishAdapterLifecycleAsync(object sender, AdapterLifecycleEventArgs args)
        {
            return AdapterLifecycle.PublishAsync(sender, args);
        }

        protected override void OnInitialize(IRuntimeContext context)
        {
            context.Logger.LogInfo("[SystemEventBus] RSEV.Utilities-EventBus initialisiert.");
        }

        protected override Task OnInitializeAsync(IRuntimeContext context)
        {
            OnInitialize(context);
            return Task.CompletedTask;
        }

        protected override void OnShutdown(IRuntimeContext context)
        {
            context.Logger.LogInfo("[SystemEventBus] Shutdown.");
        }

        protected override Task OnShutdownAsync(IRuntimeContext context)
        {
            OnShutdown(context);
            return Task.CompletedTask;
        }
    }
}
