namespace ioBroker_NewGen.Core.Matrix
{
    /// <summary>
    /// Rollen- und Berechtigungsprüfung für Lese-/Schreibzugriffe auf States.
    /// </summary>
    public interface IRoleService
    {
        /// <summary>
        /// Prüft, ob der übergebene Benutzer den angegebenen State schreiben darf.
        /// </summary>
        bool CanWrite(string user, string stateId);

        /// <summary>
        /// Prüft, ob der übergebene Benutzer den angegebenen State lesen darf.
        /// </summary>
        bool CanRead(string user, string stateId);
    }
}
