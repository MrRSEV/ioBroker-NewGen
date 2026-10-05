using RSEV.Utilities.Logging;
using RSEV.Utilities.Configuration;
using RSEV.Utilities.Runtime;
using ioBroker_NewGen.Core.Matrix;
using ioBroker_NewGen.Core.Bus;
using ioBroker_NewGen.Core.Routing;

namespace ioBroker_NewGen.Controllers
{
    /// <summary>
    /// Konkreter, applikationsweiter RuntimeContext.
    /// Wird als Singleton verwendet, damit alle Lifecycle-Hooks
    /// (IFirstRun, IOnStart, IOnStop) auf denselben Logger, dieselbe
    /// Config und denselben globalen Zustand zugreifen.
    /// </summary>
    public sealed class MainRuntimeContext : BaseRuntimeContext
    {
        private static readonly Lazy<MainRuntimeContext> _instance = new(() => new MainRuntimeContext());

        /// <summary>
        /// Die einzige Instanz des MainRuntimeContext für den gesamten Prozess.
        /// </summary>
        public static MainRuntimeContext Instance => _instance.Value;

        /// <summary>
        /// Zentrale semantische Zustands- und Referenzmatrix des Systems
        /// (Objekte, States, History, semantischer Index, Snapshot-Persistenz).
        /// Wird beim Systemstart (<see cref="ioBroker_NewGen.Startup.OnStart"/>) initialisiert.
        /// </summary>
        public SemanticStateMatrix Matrix { get; private set; } = null!;

        /// <summary>
        /// Zentraler, neutraler Datenbus des Systems (BusFrame-Transport).
        /// </summary>
        public SystemMessageBus SystemBus { get; private set; } = null!;

        /// <summary>
        /// Entscheidet, ob Frames intern (Custom-Adapter) oder über die
        /// TCP-Bridge (ioBroker-Legacy-Adapter) geroutet werden.
        /// </summary>
        public AdapterRouter AdapterRouter { get; private set; } = null!;

        private MainRuntimeContext()
        {
        }

        /// <summary>
        /// Initialisiert Matrix, Systembus und Router. Muss einmalig beim
        /// Systemstart aufgerufen werden, bevor diese Properties verwendet werden.
        /// </summary>
        public void InitializeMatrixAndBus(string snapshotPath)
        {
            Matrix ??= new SemanticStateMatrix(snapshotPath);

            if (SystemBus is null)
            {
                SystemBus = new SystemMessageBus();
                MessageBusRegistry.Register(SystemBus);
                SystemBus.Initialize(this);
            }

            AdapterRouter ??= new AdapterRouter(SystemBus, Logger);
        }

        /// <summary>
        /// Erstellt das Konfigurationsregister des RSEV.Utilities-Frameworks.
        /// Die Instanz wird von BaseRuntimeContext während der Initialisierung
        /// dieses RuntimeContext registriert.
        /// </summary>
        protected override IConfig CreateDefaultConfig()
        {
            return new ConfConfig();
        }

        /// <summary>
        /// Verwendet einen sprechenden Logger-Namen statt des Standardnamens.
        /// </summary>
        protected override ILogger CreateLogger()
        {
            var logger = new SystemLog(AppContext.BaseDirectory);
            logger.Init();
            return logger;
        }
    }
}
