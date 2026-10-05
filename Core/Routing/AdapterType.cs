namespace ioBroker_NewGen.Core.Routing
{
    /// <summary>
    /// Typ eines Adapters: bestimmt, wie Frames geroutet werden.
    /// </summary>
    public enum AdapterType
    {
        /// <summary>
        /// Nativer C#-Adapter, läuft Host-intern, direkt über den Systembus.
        /// </summary>
        Custom,

        /// <summary>
        /// Legacy ioBroker-Adapter, läuft isoliert als Node.js-Prozess,
        /// angebunden über die TCP-Bridge.
        /// </summary>
        IoBroker
    }
}
