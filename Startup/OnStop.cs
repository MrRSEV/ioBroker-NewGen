using RSEV.Utilities.Lifecycle;
using RSEV.Utilities.Logging;
using RSEV.Utilities.Runtime;
using ioBroker_NewGen.Controllers;

namespace ioBroker_NewGen.Startup
{
    /// <summary>
    /// Wird beim geordneten Herunterfahren des Systems ausgeführt.
    /// </summary>
    public class OnStop : IOnStop
    {
        public async Task OnStopAsync(IRuntimeContext context)
        {
            context.Logger.LogInfo("System wird heruntergefahren...");

            if (context is MainRuntimeContext mainContext)
            {
                await mainContext.ShutdownBridgeAndNodeRuntimeAsync();
                context.Logger.LogInfo("TCP-Bridge und Node.js-Adapter-Runtime gestoppt.");

                if (mainContext.Matrix is not null)
                {
                    mainContext.Matrix.StopCronSnapshot();
                    await mainContext.Matrix.WriteSnapshotAsync();
                    context.Logger.LogInfo("Snapshot der SemanticStateMatrix geschrieben.");
                }
            }

            context.IsRunning = false;
            context.Logger.LogInfo("System erfolgreich heruntergefahren.");
        }

        void IOnStop.OnStop(IRuntimeContext context)
        {
            context.Logger.LogInfo("System wird heruntergefahren (synchron)...");

            if (context is MainRuntimeContext mainContext)
            {
                mainContext.ShutdownBridgeAndNodeRuntimeAsync().GetAwaiter().GetResult();
                context.Logger.LogInfo("TCP-Bridge und Node.js-Adapter-Runtime gestoppt.");

                if (mainContext.Matrix is not null)
                {
                    mainContext.Matrix.StopCronSnapshot();
                    mainContext.Matrix.WriteSnapshotAsync().GetAwaiter().GetResult();
                    context.Logger.LogInfo("Snapshot der SemanticStateMatrix geschrieben.");
                }
            }

            context.IsRunning = false;
            context.Logger.LogInfo("System erfolgreich heruntergefahren.");
        }
    }
}
