using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace ioBroker.Core.Security
{
    public sealed class RoleManager
    {
        private readonly SecurityDatabase _database;
        private readonly AuditManager _audit;
        private readonly ISecurityEventPublisher _events;

        public RoleManager(SecurityDatabase database, AuditManager audit, ISecurityEventPublisher events)
        {
            _database = database;
            _audit = audit;
            _events = events;
        }

        public async Task<IReadOnlyList<(string Name, string? Description)>> ListAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Name, Description FROM Roles ORDER BY Name";
            var roles = new List<(string, string?)>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                roles.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
            }

            return roles;
        }

        public async Task<bool> AssignAsync(string username, string role, CancellationToken cancellationToken = default)
        {
            await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
            var (userId, userName, roleId, roleName) = await ResolveAsync(connection, username, role, cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO UserRoles (UserId, RoleId) VALUES ($user, $role)";
            command.AddParameter("$user", userId);
            command.AddParameter("$role", roleId);
            var added = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
            if (added)
            {
                await _audit.WriteAsync(userId, "RoleAssigned", roleName, cancellationToken).ConfigureAwait(false);
                _events.Publish(this, new RoleAssignedEvent(userId, userName, roleName));
            }

            return added;
        }

        public async Task<bool> RemoveAsync(string username, string role, CancellationToken cancellationToken = default)
        {
            await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
            var (userId, userName, roleId, roleName) = await ResolveAsync(connection, username, role, cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM UserRoles WHERE UserId = $user AND RoleId = $role";
            command.AddParameter("$user", userId);
            command.AddParameter("$role", roleId);
            var removed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
            if (removed)
            {
                await _audit.WriteAsync(userId, "RoleRemoved", roleName, cancellationToken).ConfigureAwait(false);
                _events.Publish(this, new RoleRemovedEvent(userId, userName, roleName));
            }

            return removed;
        }

        private static async Task<(long UserId, string Username, long RoleId, string RoleName)> ResolveAsync(
            DbConnection connection,
            string username,
            string role,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT u.Id, u.Username, r.Id, r.Name FROM Users u, Roles r
                WHERE u.Username = $username AND r.Name = $role
                """;
            command.AddParameter("$username", username);
            command.AddParameter("$role", role);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"Benutzer '{username}' oder Rolle '{role}' existiert nicht.");
            }

            return (reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), reader.GetString(3));
        }
    }

    public sealed class PermissionManager : IAuthorizationProvider
    {
        private readonly SecurityDatabase _database;

        public PermissionManager(SecurityDatabase database)
        {
            _database = database;
        }

        public async Task<bool> HasPermissionAsync(long userId, string permission, CancellationToken cancellationToken = default)
        {
            await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT 1 FROM Users u
                JOIN UserRoles ur ON ur.UserId = u.Id
                JOIN RolePermissions rp ON rp.RoleId = ur.RoleId
                JOIN Permissions p ON p.Id = rp.PermissionId
                WHERE u.Id = $user AND u.IsEnabled = 1 AND p.Name = $permission
                LIMIT 1
                """;
            command.AddParameter("$user", userId);
            command.AddParameter("$permission", permission);
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
        }

        public async Task<IReadOnlyList<string>> GetPermissionsAsync(long userId, CancellationToken cancellationToken = default)
        {
            await using var connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT DISTINCT p.Name FROM Users u
                JOIN UserRoles ur ON ur.UserId = u.Id
                JOIN RolePermissions rp ON rp.RoleId = ur.RoleId
                JOIN Permissions p ON p.Id = rp.PermissionId
                WHERE u.Id = $user AND u.IsEnabled = 1 ORDER BY p.Name
                """;
            command.AddParameter("$user", userId);
            var result = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add(reader.GetString(0));
            }

            return result;
        }
    }
}
