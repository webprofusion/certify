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

            var allKnownInstances = await _client.GetHubManagedInstances(CurrentAuthContext);
            var matchingInstance = allKnownInstances.FirstOrDefault(c => c.InstanceId == requestingInstanceId);

            var results = await CheckSubscribableManagedCertsForInstance(matchingInstance, allKnownInstances);

            return Ok(results);
        }

        [HttpGet]
        [Route("subscription/available/securityprincipal/{id}")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ManagedCertificateSummary>))]
        public async Task<IActionResult> GetSubscribableManagedCertificatesBySecurityPrincipal(string id)
        {

            // if query is not from a managed instance could be admin UI
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedItem, StandardResourceActions.ManagedItemList));
            if (!accessCheck.IsSuccess)
            {
                return Problem(detail: accessCheck.Message, statusCode: StatusCodes.Status401Unauthorized);
            }

            var allKnownInstances = await _client.GetHubManagedInstances(CurrentAuthContext);
            var matchingInstance = allKnownInstances.FirstOrDefault(c => c.SecurityPrincipalId == id);
            if (matchingInstance == null)
            {
                return Ok(new List<ManagedCertificateSummary>());
            }

            var results = await CheckSubscribableManagedCertsForInstance(matchingInstance, allKnownInstances);

            return Ok(results);
        }

        private async Task<List<ManagedCertificateSummary>> CheckSubscribableManagedCertsForInstance(ManagedInstanceInfo requestingInstance, ICollection<ManagedInstanceInfo> allKnownInstances)
        {
            // check which items we can download, TODO: optimize based on tagged items

            if (requestingInstance == null
                || string.IsNullOrWhiteSpace(requestingInstance.InstanceId)
                || string.IsNullOrWhiteSpace(requestingInstance.SecurityPrincipalId))
            {
                return [];
            }

            var results = new List<ManagedCertificateSummary>();
            var allInstanceItems = _mgmtStateProvider.GetManagedInstanceItems();

            var certTagCache = new Dictionary<string, ICollection<TagSummary>>();
            foreach (var sourceItems in allInstanceItems.Values.ToList())
            {
                if (sourceItems.InstanceId == requestingInstance.InstanceId)
                {
                    //skip items from the requesting instance itself
                    continue;
                }

                var instance = allKnownInstances.FirstOrDefault(i => i.InstanceId == sourceItems.InstanceId);
                foreach (var cert in sourceItems.Items)
                {
                    if (string.IsNullOrWhiteSpace(cert.Id))
                    {
                        continue;
                    }

                    // if instance is not requesting certs on behalf of itself, then we need to check access for each cert, otherwise we can skip as we know the instance has access to all certs returned in the list
                    if (sourceItems.InstanceId != requestingInstance.InstanceId)
                    {
                        ICollection<TagSummary> tags = [];

                        if (certTagCache.TryGetValue(cert.Id, out var itemTags))
                        {
                            tags = itemTags;
                        }

                        tags = await _client.GetHubItemTags(TaggedItemTypes.ManagedCertificate, cert.Id, SystemAuthContext);

                        certTagCache[cert.Id] = tags;

                        var certAccessCheck = new AccessCheck
                        {
                            SecurityPrincipalId = requestingInstance.SecurityPrincipalId,
                            ResourceType = ResourceTypes.Certificate,
                            ResourceActionId = StandardResourceActions.CertificateDownload,
                            Identifier = cert.Id,
                            ResourceTags = tags?.ToList()
                        };

                        if (!await _client.CheckSecurityPrincipalHasAccess(certAccessCheck, new Client.AuthContext { UserId = requestingInstance.SecurityPrincipalId }))
                        {
                            continue;
                        }
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
