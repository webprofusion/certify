using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Certify.Core.Management.Access;
using Certify.Management;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Services.Activity;

namespace Certify.Server.Hub.Api.Services.LocalAdmin
{
    /// <summary>
    /// Break-glass administration for an operating system administrator on the hub machine, used by the local admin
    /// channel (a named pipe or Unix socket the hub listens on, see LocalAdminHost).
    ///
    /// The channel's transport establishes who the caller is, so these operations act as the system rather than as a
    /// hub security principal, and use the access control bypasses intended for that. That is the point: they recover a
    /// hub nobody can sign in to. Passwords are only ever generated here and returned, never accepted from the caller.
    /// </summary>
    public class LocalAdminOperations
    {
        private const string ContextUserId = StandardSecurityPrincipals.System;

        private const string AuthSettingsSection = "AuthSettings";
        private const string EnablePasswordLoginProperty = "enablePasswordLogin";

        private static readonly Regex PasswordLoginDisabledPattern = new($"(\"{EnablePasswordLoginProperty}\"\\s*:\\s*)false", RegexOptions.IgnoreCase);

        /// <summary>
        /// Characters for generated passwords, leaving out ones easily confused when read back (0/O, 1/l/I).
        /// </summary>
        private const string PasswordCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789-_.!@#%+=";

        private const int GeneratedPasswordLength = 20;

        private readonly ICertifyManager _certifyManager;
        private readonly IConfiguration _configuration;
        private readonly string _settingsPath;
        private readonly ActivityRecorder? _activityRecorder;
        private readonly ILogger _logger;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="certifyManager">The hub's certify manager, whose access control the operations act on.</param>
        /// <param name="configuration">The hub's configuration, to read back settings the operations change.</param>
        /// <param name="settingsPath">Path of the hub settings file (hubservice.json).</param>
        /// <param name="activityRecorder">Records each operation in the hub activity history.</param>
        /// <param name="logger">Logs each operation.</param>
        public LocalAdminOperations(ICertifyManager certifyManager, IConfiguration configuration, string settingsPath, ActivityRecorder? activityRecorder, ILogger logger)
        {
            _certifyManager = certifyManager;
            _configuration = configuration;
            _settingsPath = settingsPath;
            _activityRecorder = activityRecorder;
            _logger = logger;
        }

        /// <summary>
        /// Generate a password from a cryptographic RNG.
        /// </summary>
        public static string GeneratePassword() => RandomNumberGenerator.GetString(PasswordCharacters, GeneratedPasswordLength);

        /// <summary>
        /// List security principals, noting which hold the Administrator role.
        /// </summary>
        public async Task<List<LocalAdminPrincipalSummary>> GetPrincipals()
        {
            // fetched per call rather than held, because access control is recreated when the data store changes
            var access = await _certifyManager.GetCurrentAccessControl();
            var principals = await access.GetSecurityPrincipals(ContextUserId) ?? [];

            var results = new List<LocalAdminPrincipalSummary>();

            foreach (var principal in principals.OrderBy(p => p.Username ?? p.Id, StringComparer.OrdinalIgnoreCase))
            {
                results.Add(new LocalAdminPrincipalSummary
                {
                    Id = principal.Id,
                    Username = principal.Username,
                    Title = principal.Title,
                    PrincipalType = principal.PrincipalType.ToString(),
                    Provider = principal.Provider,
                    IsBuiltIn = principal.IsBuiltIn,
                    IsAdministrator = await access.IsPrincipalInRole(principal.Id, StandardRoles.Administrator.Id)
                });
            }

            return results;
        }

        /// <summary>
        /// Give a user a newly generated password.
        /// </summary>
        public async Task<LocalAdminResult> ResetPassword(LocalAdminPrincipalRequest request, string caller)
        {
            var access = await _certifyManager.GetCurrentAccessControl();
            var (principal, error) = await FindUser(access, request.Principal);

            if (principal == null)
            {
                return await Complete(Failed(error), "Password reset", caller);
            }

            var password = GeneratePassword();

            var updated = await access.UpdateSecurityPrincipalPassword(
                ContextUserId,
                new SecurityPrincipalPasswordUpdate { SecurityPrincipalId = principal.Id, NewPassword = password },
                requirePasswordConfirmation: false,
                bypassIntegrityCheck: true);

            if (!updated)
            {
                return await Complete(Failed($"The password for {Describe(principal)} could not be updated, see the hub log for details."), "Password reset", caller);
            }

            return await Complete(new LocalAdminResult
            {
                IsSuccess = true,
                Message = $"Password reset for {Describe(principal)}.",
                PrincipalId = principal.Id,
                Username = principal.Username,
                GeneratedPassword = password
            }, "Password reset", caller);
        }

        /// <summary>
        /// Assign the Administrator role to a user, keeping any existing assignment.
        /// </summary>
        public async Task<LocalAdminResult> GrantAdministrator(LocalAdminPrincipalRequest request, string caller)
        {
            var access = await _certifyManager.GetCurrentAccessControl();
            var (principal, error) = await FindUser(access, request.Principal);

            if (principal == null)
            {
                return await Complete(Failed(error), "Administrator role grant", caller);
            }

            var result = new StandardAccessConfigResult();
            var assigned = await AccessControlConfig.EnsureAssignedRole(access, result, ContextUserId, principal.Id, StandardRoles.Administrator.Id);

            if (assigned == null)
            {
                return await Complete(Failed(string.Join(" ", result.Failures)), "Administrator role grant", caller);
            }

            return await Complete(new LocalAdminResult
            {
                IsSuccess = true,
                Message = $"{Describe(principal)} holds the Administrator role.",
                PrincipalId = principal.Id,
                Username = principal.Username
            }, "Administrator role grant", caller);
        }

        /// <summary>
        /// Add a new user with a generated password and the Administrator role, for a hub whose administrators have all
        /// been removed or locked out.
        /// </summary>
        public async Task<LocalAdminResult> CreateAdministrator(LocalAdminCreateAdministratorRequest request, string caller)
        {
            var username = request.Username?.Trim();

            if (string.IsNullOrWhiteSpace(username))
            {
                return await Complete(Failed("A username is required."), "Administrator creation", caller);
            }

            var access = await _certifyManager.GetCurrentAccessControl();

            var password = GeneratePassword();

            var principal = new SecurityPrincipal
            {
                Id = Guid.NewGuid().ToString(),
                Username = username,
                Title = string.IsNullOrWhiteSpace(request.Title) ? username : request.Title.Trim(),
                Description = "Created using the hub local admin channel",
                Password = password,
                PrincipalType = SecurityPrincipalType.User,
                Provider = StandardIdentityProviders.INTERNAL
            };

            if (!(await access.AddSecurityPrincipal(ContextUserId, principal, bypassIntegrityCheck: true)).IsSuccess)
            {
                // most likely because the username is in use, as no two principals may share one
                return await Complete(Failed($"The user '{username}' could not be added. If a user with this username already exists, use password-reset or grant-admin for it instead. See the hub log for details."), "Administrator creation", caller);
            }

            var result = new StandardAccessConfigResult();

            if (await AccessControlConfig.EnsureAssignedRole(access, result, ContextUserId, principal.Id, StandardRoles.Administrator.Id) == null)
            {
                return await Complete(Failed($"The user '{username}' was added but could not be given the Administrator role. {string.Join(" ", result.Failures)}"), "Administrator creation", caller);
            }

            return await Complete(new LocalAdminResult
            {
                IsSuccess = true,
                Message = $"Administrator '{username}' created.",
                PrincipalId = principal.Id,
                Username = username,
                GeneratedPassword = password
            }, "Administrator creation", caller);
        }

        /// <summary>
        /// Turn username/password sign in back on, for a hub configured for OIDC only whose provider is unavailable.
        /// </summary>
        public async Task<LocalAdminResult> EnablePasswordLogin(string caller)
        {
            if (AuthSettings.IsPasswordLoginEnabled(_configuration))
            {
                return await Complete(new LocalAdminResult { IsSuccess = true, Message = "Username and password sign in is already enabled." }, "Enable password sign in", caller);
            }

            try
            {
                var content = File.ReadAllText(_settingsPath);

                if (PasswordLoginDisabledPattern.IsMatch(content))
                {
                    // a text replacement keeps any comments in the file, which rewriting the document would discard
                    File.WriteAllText(_settingsPath, PasswordLoginDisabledPattern.Replace(content, "${1}true"));
                }
                else
                {
                    // disabled by another configuration source, which the hub settings file overrides
                    var root = HubSettingsFile.Parse(content);

                    if (root == null)
                    {
                        return await Complete(Failed($"Could not read {_settingsPath}, the file is not a JSON object."), "Enable password sign in", caller);
                    }

                    HubSettingsFile.EnsureSection(root, AuthSettingsSection)[EnablePasswordLoginProperty] = true;
                    HubSettingsFile.WriteWithBackup(_settingsPath, root);
                }
            }
            catch (Exception ex)
            {
                return await Complete(Failed($"Could not update {_settingsPath} - {ex.Message}"), "Enable password sign in", caller);
            }

            // the settings file is reloaded when it changes, so wait briefly to report what the hub actually applies
            for (var attempt = 0; attempt < 20 && !AuthSettings.IsPasswordLoginEnabled(_configuration); attempt++)
            {
                await Task.Delay(250);
            }

            if (!AuthSettings.IsPasswordLoginEnabled(_configuration))
            {
                return await Complete(Failed($"{_settingsPath} was updated, but the hub has not applied it yet. Restart the hub service if sign in is still disabled."), "Enable password sign in", caller);
            }

            return await Complete(new LocalAdminResult { IsSuccess = true, Message = $"Username and password sign in enabled in {_settingsPath}." }, "Enable password sign in", caller);
        }

        /// <summary>
        /// Find a user by id, or failing that by username. Only users are matched, so that the role and password of an
        /// application or managed instance principal cannot be changed here.
        /// </summary>
        private static async Task<(SecurityPrincipal? Principal, string Error)> FindUser(IAccessControl access, string? idOrUsername)
        {
            var key = idOrUsername?.Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                return (null, "A security principal id or username is required.");
            }

            var users = (await access.GetSecurityPrincipals(ContextUserId) ?? [])
                .Where(p => p.PrincipalType == SecurityPrincipalType.User)
                .ToList();

            var byId = users.FirstOrDefault(p => string.Equals(p.Id, key, StringComparison.Ordinal));

            if (byId != null)
            {
                return (byId, string.Empty);
            }

            var byUsername = users.Where(p => string.Equals(p.Username, key, StringComparison.OrdinalIgnoreCase)).ToList();

            return byUsername.Count switch
            {
                1 => (byUsername[0], string.Empty),
                0 => (null, $"No user found with the id or username '{key}'."),
                _ => (null, $"More than one user has the username '{key}', specify the security principal id instead.")
            };
        }

        private static string Describe(SecurityPrincipal principal) => string.IsNullOrWhiteSpace(principal.Username) ? principal.Id : $"'{principal.Username}' ({principal.Id})";

        private static LocalAdminResult Failed(string message) => new() { IsSuccess = false, Message = message };

        /// <summary>
        /// Log and record the outcome, then return it. The generated password is never logged.
        /// </summary>
        private async Task<LocalAdminResult> Complete(LocalAdminResult result, string action, string caller)
        {
            if (result.IsSuccess)
            {
                _logger.LogWarning("Local admin channel: {action} by {caller} succeeded. {message}", action, caller, result.Message);
            }
            else
            {
                _logger.LogWarning("Local admin channel: {action} by {caller} failed. {message}", action, caller, result.Message);
            }

            if (_activityRecorder != null)
            {
                await _activityRecorder.LocalAdminActionAsync($"{action} using the local admin channel", caller, result.IsSuccess, result.Message);
            }

            return result;
        }
    }
}
