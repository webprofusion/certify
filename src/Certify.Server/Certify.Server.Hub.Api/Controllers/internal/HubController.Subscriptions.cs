using Certify.Models.Hub;
using Certify.Server.Hub.Api.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers
{
    public partial class HubController
    {
        /// <summary>
        /// Returns managed certificate summaries that the authenticated calling instance is permitted to pull.
        /// Authenticated via X-Client-ID / X-Client-Secret + X-Certify-HubAssignedId headers (hub joining credentials).
        /// </summary>
        [HttpGet]
        [Route("subscription/available")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ManagedCertificateSummary>))]
        public async Task<IActionResult> GetSubscribableManagedCertificates()
        {
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceJoin));
            if (!accessCheck.IsSuccess)
            {
                // if query is not from a managed instance could be admin UI
                accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedItem, StandardResourceActions.ManagedItemList));
                if (!accessCheck.IsSuccess)
                {
                    return Problem(detail: accessCheck.Message, statusCode: StatusCodes.Status401Unauthorized);
                }
            }

            // Identify requesting instance
            var requestingInstanceId = Request.Headers["X-Certify-HubAssignedId"].ToString();
            if (string.IsNullOrWhiteSpace(requestingInstanceId))
            {
                return Problem(detail: "X-Certify-HubAssignedId header is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var instanceAuth = await ValidateManagedInstanceRequestAuthAsync();
            if (!instanceAuth.IsSuccess)
            {
                return Problem(detail: instanceAuth.Message, statusCode: instanceAuth.StatusCode);
            }

            var allKnownInstances = await _client.GetHubManagedInstances(CurrentAuthContext);
            var matchingInstance = instanceAuth.ManagedInstance ?? allKnownInstances.FirstOrDefault(c => c.InstanceId == requestingInstanceId);

            if (matchingInstance == null
                || string.IsNullOrWhiteSpace(matchingInstance.InstanceId)
                || string.IsNullOrWhiteSpace(matchingInstance.SecurityPrincipalId))
            {
                // an instance the hub does not know cannot be resolved to a principal to evaluate access for
                return Ok(new List<ManagedCertificateSummary>());
            }

            var results = await CheckSubscribableManagedCerts(
                matchingInstance.SecurityPrincipalId,
                allKnownInstances,
                excludeInstanceId: matchingInstance.InstanceId);

            return Ok(results);
        }

        /// <summary>
        /// Managed certificate summaries a given security principal is permitted to pull. Any principal type can be
        /// previewed: a managed instance subscribes to them, while a user or application downloads them, and both
        /// are permitted by the same certificate download action.
        /// </summary>
        /// <param name="id">the security principal to preview access for</param>
        /// <param name="assignedAccessTokenId">optionally evaluate access as one of the principal's API access tokens, which narrows the preview to the role assignments that token is scoped to</param>
        [HttpGet]
        [Route("subscription/available/securityprincipal/{id}")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ManagedCertificateSummary>))]
        public async Task<IActionResult> GetSubscribableManagedCertificatesBySecurityPrincipal(string id, [FromQuery] string? assignedAccessTokenId = null)
        {

            // if query is not from a managed instance could be admin UI
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedItem, StandardResourceActions.ManagedItemList));
            if (!accessCheck.IsSuccess)
            {
                return Problem(detail: accessCheck.Message, statusCode: StatusCodes.Status401Unauthorized);
            }

            List<string> scopedAssignedRoles = [];

            if (!string.IsNullOrWhiteSpace(assignedAccessTokenId))
            {
                var tokenScope = await GetAssignedAccessTokenScope(_client, id, assignedAccessTokenId);

                if (!tokenScope.IsSuccess)
                {
                    return Problem(detail: tokenScope.Message, statusCode: StatusCodes.Status400BadRequest);
                }

                scopedAssignedRoles = tokenScope.Result ?? [];
            }

            var allKnownInstances = await _client.GetHubManagedInstances(CurrentAuthContext);

            // where the principal is a managed instance, its own items are excluded as they would be when it pulls.
            // A user, application or group principal has no instance of its own, so nothing is excluded for them.
            var ownInstanceId = allKnownInstances.FirstOrDefault(c => c.SecurityPrincipalId == id)?.InstanceId;

            var results = await CheckSubscribableManagedCerts(id, allKnownInstances, ownInstanceId, scopedAssignedRoles);

            return Ok(results);
        }

        /// <summary>
        /// The managed certificates a security principal is permitted to download, whichever principal type it is.
        /// </summary>
        /// <param name="securityPrincipalId">the principal whose access is being evaluated</param>
        /// <param name="allKnownInstances">the hub's managed instances, used to title the source of each item</param>
        /// <param name="excludeInstanceId">
        /// the principal's own managed instance, where it has one. Those items are already held there, so an instance
        /// is never offered its own certificates back. Principal types other than a managed instance have no such
        /// instance and nothing is excluded for them.
        /// </param>
        /// <param name="scopedAssignedRoles">the role assignments to narrow to, when evaluating access as an API token</param>
        private async Task<List<ManagedCertificateSummary>> CheckSubscribableManagedCerts(
            string? securityPrincipalId,
            ICollection<ManagedInstanceInfo> allKnownInstances,
            string? excludeInstanceId = null,
            ICollection<string>? scopedAssignedRoles = null)
        {
            // check which items we can download, TODO: optimize based on tagged items

            if (string.IsNullOrWhiteSpace(securityPrincipalId))
            {
                return [];
            }

            var results = new List<ManagedCertificateSummary>();
            var allInstanceItems = _mgmtStateProvider.GetManagedInstanceItems();

            // Domain restrictions on the principal's roles are Domain Match rules and apply to every identifier on
            // a cert, matching what the download endpoint enforces. Resolved once here rather than per item.
            var domainRules = await GetDomainRestrictionRulesForPrincipal(
                _client,
                securityPrincipalId,
                StandardResourceActions.CertificateDownload,
                scopedAssignedRoles);

            if (domainRules == null)
            {
                // scope could not be evaluated, fail closed rather than preview items the download would refuse
                return [];
            }

            var certTagCache = new Dictionary<string, ICollection<TagSummary>>();
            foreach (var sourceItems in allInstanceItems.Values.ToList())
            {
                if (!string.IsNullOrWhiteSpace(excludeInstanceId) && sourceItems.InstanceId == excludeInstanceId)
                {
                    //skip items from the principal's own instance
                    continue;
                }

                var instance = allKnownInstances.FirstOrDefault(i => i.InstanceId == sourceItems.InstanceId);
                foreach (var cert in sourceItems.Items)
                {
                    if (string.IsNullOrWhiteSpace(cert.Id))
                    {
                        continue;
                    }

                    ICollection<TagSummary> tags = [];

                    if (certTagCache.TryGetValue(cert.Id, out var itemTags))
                    {
                        tags = itemTags;
                    }

                    tags = await _client.GetHubItemTags(TaggedItemTypes.ManagedCertificate, cert.Id, SystemAuthContext);

                    certTagCache[cert.Id] = tags;

                    var certAccessCheck = new AccessCheck
                    {
                        SecurityPrincipalId = securityPrincipalId,
                        ResourceType = ResourceTypes.Certificate,
                        ResourceActionId = StandardResourceActions.CertificateDownload,
                        Identifier = cert.Id,
                        ResourceTags = tags?.ToList(),
                        ScopedAssignedRoles = scopedAssignedRoles?.ToList() ?? []
                    };

                    if (!await _client.CheckSecurityPrincipalHasAccess(certAccessCheck, new Client.AuthContext { UserId = securityPrincipalId }))
                    {
                        continue;
                    }

                    // the whole cert is downloaded, so every identifier on it must be within the domain scope
                    if (domainRules.Count > 0
                        && !cert.GetCertificateIdentifiers().All(i => ResourceAccess.IsIdentifierPermittedByDomainRules(domainRules, i.Value)))
                    {
                        continue;
                    }

                    results.Add(new ManagedCertificateSummary
                    {
                        InstanceId = sourceItems.InstanceId,
                        InstanceTitle = instance?.DisplayTitle,
                        Id = cert.Id ?? string.Empty,
                        Title = cert.Name ?? string.Empty,
                        PrimaryIdentifier = cert.GetCertificateIdentifiers().FirstOrDefault(p => p.Value == cert.RequestConfig.PrimaryDomain)
                                            ?? cert.GetCertificateIdentifiers().FirstOrDefault(),
                        Identifiers = cert.GetCertificateIdentifiers(),
                        DateRenewed = cert.DateRenewed,
                        DateExpiry = cert.DateExpiry,
                        Status = cert.Health.ToString(),
                        HasCertificate = !string.IsNullOrEmpty(cert.CertificatePath)
                    });

                }
            }

            return results;
        }
    }
}
