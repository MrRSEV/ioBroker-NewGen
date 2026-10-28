using ioBroker_NewGen.Core.Bus;
using ioBroker_NewGen.Core.Matrix;
using ioBroker_NewGen.Core.Registry;
using ioBroker_NewGen.Core.Routing;
using ioBroker_NewGen.Core.Deployment;
using RSEV.Utilities.Configuration;
using RSEV.Utilities.ApiBuilder;
using RSEV.Utilities.Runtime;
using System.Threading;
using System.Threading.Tasks;

namespace ioBroker_NewGen.Core.Adapters
{
    /// <summary>
    /// Begrenzter Host-Kontext für einen CustomAdapter.
    /// </summary>
    public interface IAdapterContext
    {
        SemanticStateMatrix Matrix { get; }

        IRoleService RoleService => Matrix.RoleService;

        SystemMessageBus SystemBus { get; }

        AdapterRouter Router { get; }

        AdapterRegistry AdapterRegistry { get; }

        AdapterInstaller? AdapterInstaller { get; }

        AdapterPackageInstaller? AdapterPackageInstaller { get; }

        AdapterRepositoryClient? AdapterRepositoryClient { get; }

        IConfig Config { get; }

        IRuntimeContext RuntimeContext { get; }

        RuntimeApiHost ApiHost { get; }

        Task<bool> StartAdapterAsync(string adapterId, CancellationToken cancellationToken = default);

        Task<bool> StopAdapterAsync(string adapterId, CancellationToken cancellationToken = default);

        void Publish(BusFrame frame);

        void Route(BusFrame frame);
    }
}
