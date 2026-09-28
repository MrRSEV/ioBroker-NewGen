using RSEV.Utilities.Lifecycle;
using RSEV.Utilities.Runtime;

namespace ioBroker_NewGen.Startup
{
    public class OnStop : IOnStop
    {
        public Task OnStopAsync(IRuntimeContext context)
        {
            throw new NotImplementedException();
        }

        void IOnStop.OnStop(IRuntimeContext context)
        {
            throw new NotImplementedException();
        }
    }
}
