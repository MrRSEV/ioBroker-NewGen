using ioBroker_NewGen.Core.Adapters.Protocol;

namespace ioBroker_NewGen.Adapters.Mqtt
{
    public sealed class MqttAdapter : MqttAdapterBase
    {
        public MqttAdapter()
            : base(new MqttAdapterOptions())
        {
        }

        public override string Name => "mqtt";
    }
}
