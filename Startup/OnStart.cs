using RSEV.Utilities.Lifecycle;
using RSEV.Utilities.Runtime;
using ioBroker_NewGen.Controllers;

namespace ioBroker_NewGen.Startup
{
    /// <summary>
    /// Wird bei jedem Start des Systems ausgeführt (nach FirstRun, falls Erststart).
    /// </summary>
    public class OnStart : IOnStart
    {
        public async Task OnStartAsync(IRuntimeContext rtx)
        {
            rtx.Logger.LogInfo("System wird gestartet...");
            await ConfigLoader.LoadIfExistsAsync(rtx);

            if (rtx is MainRuntimeContext mainContext)
            {
                var snapshotPath = System.IO.Path.Combine(AppContext.BaseDirectory, "snapshot.jsonl");
                mainContext.InitializeMatrixAndBus(snapshotPath);
                await mainContext.Matrix.LoadSnapshotAsync();
                mainContext.Matrix.StartCronSnapshot(TimeSpan.FromMinutes(5));
                rtx.Logger.LogInfo("SemanticStateMatrix, SystemBus und AdapterRouter initialisiert.");
            }

            rtx.IsRunning = true;
            rtx.Logger.LogInfo("System erfolgreich gestartet.");
        }

        void IOnStart.OnStart(IRuntimeContext rtx)
        {
            rtx.Logger.LogInfo("System wird gestartet (synchron)...");
            ConfigLoader.LoadIfExistsAsync(rtx).GetAwaiter().GetResult();

            if (rtx is MainRuntimeContext mainContext)
            {
                var snapshotPath = System.IO.Path.Combine(AppContext.BaseDirectory, "snapshot.jsonl");
                mainContext.InitializeMatrixAndBus(snapshotPath);
                mainContext.Matrix.LoadSnapshotAsync().GetAwaiter().GetResult();
                mainContext.Matrix.StartCronSnapshot(TimeSpan.FromMinutes(5));
                rtx.Logger.LogInfo("SemanticStateMatrix, SystemBus und AdapterRouter initialisiert.");
            }

            rtx.IsRunning = true;
            rtx.Logger.LogInfo("System erfolgreich gestartet.");
        }
    }
}

