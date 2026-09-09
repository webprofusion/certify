using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Models.Acme;
using Certify.Server.Hub.Api.Services.Acme;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers
{
    /// <summary>
    /// Internal API controller for administering the ACME accounts which ACME clients have registered with the
    /// hub Managed ACME service.
    /// </summary>
    [ApiController]
    [Route("internal/v1/managedacme")]
    public partial class ManagedAcmeController : ApiControllerBase
    {
        private readonly ILogger<ManagedAcmeController> _logger;
        private readonly ICertifyInternalApiClient _client;
        private readonly AcmeServerConfig _config;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        /// <param name="config"></param>
        public ManagedAcmeController(ILogger<ManagedAcmeController> logger, ICertifyInternalApiClient client, AcmeServerConfig config)
        {
            _logger = logger;
            _client = client;
            _config = config;
        }

        /// <summary>
        /// Get the ACME accounts registered with the managed ACME service, including the identity each was
        /// registered against and when it was last used.
        /// </summary>
        /// <returns>List of managed ACME account summaries</returns>
        [HttpGet]
        [Route("accounts")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ICollection<ManagedAcmeAccountSummary>))]
        public async Task<IActionResult> GetManagedAcmeAccounts()
        {
            var accessCheck = new AccessCheck
            {
                ResourceType = ResourceTypes.ManagedAcme,
                ResourceActionId = StandardResourceActions.ManagedAcmeAccountList
            };

            if (!await IsAuthorized(_client, accessCheck))
            {
                return Forbid();
            }

            var accounts = await _config.GetAccounts();

            // resolve the owning identity for each account here rather than in the UI: the account holds the
            // principal and access token ids, and an operator needs the names those refer to
            var principals = await GetSecurityPrincipalsById();
            var accessTokenTitles = await GetAccessTokenTitlesById();

            var summaries = accounts
                .Select(a => CreateSummary(a.Key, a.Value, principals, accessTokenTitles))
                .OrderByDescending(a => a.DateLastUsed ?? a.DateCreated)
                .ThenBy(a => a.AccountId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new OkObjectResult(summaries);
        }

        /// <summary>
        /// Remove an ACME account registered with the managed ACME service, along with its account key. The ACME
        /// client which registered it can no longer sign requests and would have to register again.
        /// </summary>
        /// <param name="accountId">The id of the account to remove, as shown in the account url</param>
        /// <returns>Action result</returns>
        [HttpDelete]
        [Route("accounts/{accountId}")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Certify.Models.Config.ActionResult))]
        public async Task<IActionResult> RemoveManagedAcmeAccount(string accountId)
        {
            var accessCheck = new AccessCheck
            {
                ResourceType = ResourceTypes.ManagedAcme,
                ResourceActionId = StandardResourceActions.ManagedAcmeAccountDelete,
                Identifier = accountId
            };

            if (!await IsAuthorized(_client, accessCheck))
            {
                return Forbid();
            }

            // accounts are keyed by their full url, which is not addressable as a route value, so the account is
            // matched on the id within that url
            var accounts = await _config.GetAccounts();
            var matches = accounts.Where(a => string.Equals(GetAccountIdFromUrl(a.Key), accountId, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matches.Count == 0)
            {
                return new OkObjectResult(new Certify.Models.Config.ActionResult("ACME account not found", false));
            }

            foreach (var match in matches)
            {
                await _config.RemoveAcmeAccount(match.Key);
                await _config.RemoveAcmeAccountKey(match.Key);
            }

            _logger.LogInformation("Managed ACME account {AccountId} removed by {UserId}", accountId, CurrentAuthContext?.UserId);

            return new OkObjectResult(new Certify.Models.Config.ActionResult("ACME account removed", true));
        }

        private static ManagedAcmeAccountSummary CreateSummary(
            string accountUrl,
            AcmeAccount account,
            IDictionary<string, SecurityPrincipal> principals,
            IDictionary<string, string> accessTokenTitles)
        {
            var summary = new ManagedAcmeAccountSummary
            {
                AccountId = GetAccountIdFromUrl(accountUrl),
                AccountUrl = accountUrl,
                Status = account.Status.ToString(),
                Contacts = account.Contact?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? [],
                SecurityPrincipalId = account.SecurityPrincipalId,
                AccessTokenId = account.internalId,
                ScopedAssignedRoles = account.ScopedAssignedRoles ?? [],
                DateCreated = account.DateCreated,
                DateLastUsed = account.DateLastUsed
            };

            if (!string.IsNullOrWhiteSpace(account.SecurityPrincipalId)
                && principals.TryGetValue(account.SecurityPrincipalId, out var principal))
            {
                summary.SecurityPrincipalTitle = !string.IsNullOrWhiteSpace(principal.Username) ? principal.Username : principal.Title;
                summary.PrincipalType = principal.PrincipalType;
            }

            if (!string.IsNullOrWhiteSpace(account.internalId)
                && accessTokenTitles.TryGetValue(account.internalId, out var accessTokenTitle))
            {
                summary.AccessTokenTitle = accessTokenTitle;
            }

            return summary;
        }

        /// <summary>
        /// The trailing id in an account url, which is what identifies the account outside of the ACME protocol.
        /// </summary>
        private static string GetAccountIdFromUrl(string accountUrl)
        {
            if (string.IsNullOrWhiteSpace(accountUrl))
            {
                return string.Empty;
            }

            var value = accountUrl.TrimEnd('/');
            var separator = value.LastIndexOf('/');

            return separator >= 0 && separator < value.Length - 1 ? value[(separator + 1)..] : value;
        }

        /// <summary>
        /// Security principals by id. Resolved as the system principal because the account list reports who owns
        /// each account, which the caller is authorized for by the managed ACME list action itself.
        /// </summary>
        private async Task<IDictionary<string, SecurityPrincipal>> GetSecurityPrincipalsById()
        {
            try
            {
                var principals = await _client.GetSecurityPrincipals(SystemAuthContext) ?? [];

                return principals
                    .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                    .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exp)
            {
                // an account whose owner cannot be resolved is still worth listing, it just shows the raw id
                _logger.LogWarning(exp, "Could not resolve security principals for the managed ACME account list");
                return new Dictionary<string, SecurityPrincipal>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Titles of the assigned access tokens, by the id of each access token (EAB key id) they contain.
        /// </summary>
        private async Task<IDictionary<string, string>> GetAccessTokenTitlesById()
        {
            try
            {
                var assignedTokens = await _client.GetAssignedAccessTokens(SystemAuthContext) ?? [];

                return assignedTokens
                    .SelectMany(assigned => (assigned.AccessTokens ?? [])
                        .Where(t => !string.IsNullOrWhiteSpace(t.Id))
                        .Select(t => new KeyValuePair<string, string>(t.Id, assigned.Title)))
                    .GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exp)
            {
                _logger.LogWarning(exp, "Could not resolve access tokens for the managed ACME account list");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
