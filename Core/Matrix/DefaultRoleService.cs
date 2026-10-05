using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ioBroker_NewGen.Core.Matrix
{
    /// <summary>
    /// Voll funktionsfähige, einfache Rollen-Implementierung auf Basis von
    /// Allow-/Deny-Listen pro Benutzer. Standardmäßig ("default"-Benutzer bzw.
    /// kein Eintrag vorhanden) wird voller Zugriff gewährt, damit das System
    /// sofort einsatzbereit ist. Für produktive Mehrbenutzer-Szenarien können
    /// über <see cref="SetPermissions"/> gezielt Einschränkungen hinterlegt werden.
    /// </summary>
    public sealed class DefaultRoleService : IRoleService
    {
        private sealed record Permission(bool CanRead, bool CanWrite);

        private readonly ConcurrentDictionary<string, Permission> _permissions = new();

        /// <summary>
        /// Hinterlegt explizite Lese-/Schreibrechte für einen Benutzer auf einen State.
        /// Ohne expliziten Eintrag wird voller Zugriff gewährt.
        /// </summary>
        public void SetPermissions(string user, string stateId, bool canRead, bool canWrite)
        {
            _permissions[BuildKey(user, stateId)] = new Permission(canRead, canWrite);
        }

        public bool CanRead(string user, string stateId)
        {
            return !_permissions.TryGetValue(BuildKey(user, stateId), out var permission) || permission.CanRead;
        }

        public bool CanWrite(string user, string stateId)
        {
            return !_permissions.TryGetValue(BuildKey(user, stateId), out var permission) || permission.CanWrite;
        }

        private static string BuildKey(string user, string stateId) => $"{user}:{stateId}";
    }
}
