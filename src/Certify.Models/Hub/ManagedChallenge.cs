using System;
using System.Collections.Generic;
using Certify.Models.Config;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Configuration for a managed challenge, such as a DNS challenge for a specific domain/zone
    /// A managed challenge is one the management hub can complete on behalf of another ACME client
    /// </summary>
    public class ManagedChallenge : ConfigurationStoreItem
    {
        public CertRequestChallengeConfig? ChallengeConfig { get; set; }
    }

    /// <summary>
    /// Managed challenge with tag information for API responses
    /// </summary>
    public class ManagedChallengeSummary
    {
        /// <summary>
        /// Unique identifier for the managed challenge
        /// </summary>
        public string Id { get; set; } = default!;

        /// <summary>
        /// Title/description of the managed challenge
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Challenge configuration details
        /// </summary>
        public CertRequestChallengeConfig? ChallengeConfig { get; set; }

        /// <summary>
        /// Tags assigned to this managed challenge
        /// </summary>
        public List<TagSummary> Tags { get; set; } = [];
    }

    public class ManagedChallengeRequest
    {
        /// <summary>
        /// The type of challenge to perform (e.g. dns-01)
        /// </summary>
        public string ChallengeType { get; set; } = string.Empty;

        /// <summary>
        /// domain etc challenge is being performed for
        /// </summary>
        public string Identifier { get; set; } = string.Empty;
        public string ResponseKey { get; set; } = string.Empty;
        public string ResponseValue { get; set; } = string.Empty;
        public string AuthKey { get; set; } = string.Empty;
        public string AuthSecret { get; set; } = string.Empty;

        public DateTimeOffset? DateTimePerformed { get; set; }
        public string? ManagedCertId { get; set; }

        /// <summary>
        /// Optional security principal on whose behalf the challenge is being performed.
        /// When set, managed challenge selection honours that principal's scoped roles.
        /// </summary>
        public string? SecurityPrincipalId { get; set; }

        /// <summary>
        /// Optional assigned-role ids that further scope the principal (e.g. from EAB/API token).
        /// </summary>
        public List<string>? ScopedAssignedRoles { get; set; }

        /// <summary>
        /// What authorized this request, which decides the resource action fulfillment checks the principal against.
        ///
        /// Like <see cref="SecurityPrincipalId"/> this is authorization state set by the layer which authorized the
        /// request, never trusted from the caller: choosing the action would let a caller be checked against a role
        /// other than the one they were authorized under, which resolves a different set of accessible challenges.
        /// </summary>
        public string Origin { get; set; } = ManagedChallengeRequestOrigins.ManagedChallengeApi;
    }

    /// <summary>
    /// What authorized a managed challenge request. Fulfillment re-checks the requesting principal, and which
    /// resource action it checks depends on how the caller reached it.
    /// </summary>
    public static class ManagedChallengeRequestOrigins
    {
        /// <summary>
        /// An external consumer of the managed challenge API, authorized per operation against the managed
        /// challenge actions. This is the default because it is the more restrictive of the two: a request which
        /// arrives without an origin is checked against the action the API endpoint authorizes, not against the
        /// broader managed ACME order action.
        /// </summary>
        public const string ManagedChallengeApi = "managedchallenge_api";

        /// <summary>
        /// Managed ACME order fulfillment on behalf of a managed certificate. The order is authorized once, and
        /// every challenge performed for it is covered by that same action.
        /// </summary>
        public const string ManagedAcme = "managedacme";

        /// <summary>
        /// The resource action fulfillment must check the requesting principal against.
        ///
        /// This used to be inferred from whether the request happened to carry AuthKey/AuthSecret, which is empty
        /// for a caller presenting the same credentials as X-Client-ID/X-Client-Secret headers - so a managed
        /// challenge consumer authenticating by header was checked against the managed ACME order action instead,
        /// and denied against a role it was never meant to need.
        /// </summary>
        public static string GetRequiredResourceAction(string? origin, string managedChallengeApiAction)
            => origin == ManagedAcme
                ? StandardResourceActions.ManagedAcmePerformOrder
                : managedChallengeApiAction;
    }

    /// <summary>
    /// Asks whether a security principal may use managed challenges for a set of identifiers.
    ///
    /// This is the single authorization question behind managed challenges and managed ACME orders, wherever it
    /// is asked from: the ACME endpoints ask it before accepting an order, the managed challenge API asks it
    /// before performing one, and fulfillment asks it again for an order authorized earlier. It used to be
    /// answered twice by two implementations - one in the hub API built out of access control primitives, and one
    /// in the core - which could disagree, and did on domain restrictions.
    /// </summary>
    public class ManagedChallengeAuthorizationCheck
    {
        /// <summary>
        /// The principal whose access is being evaluated.
        /// </summary>
        public string? SecurityPrincipalId { get; set; }

        /// <summary>
        /// Identifiers (domains) the principal wants to act on.
        /// </summary>
        public List<string> Identifiers { get; set; } = [];

        /// <summary>
        /// Assigned role ids an API access token or ACME account is scoped to, narrowing the principal's roles.
        /// </summary>
        public List<string>? ScopedAssignedRoles { get; set; }

        /// <summary>
        /// The resource action the principal must hold. Managed ACME order fulfillment is authorized once for the
        /// whole order, so it checks a different action than a per-request managed challenge call.
        /// </summary>
        public string RequiredActionId { get; set; } = StandardResourceActions.ManagedChallengeRequest;

        /// <summary>
        /// When true, an accessible managed challenge must match every identifier, not merely be permitted.
        ///
        /// An unrestricted principal is authorized for any identifier, but that does not mean a challenge exists
        /// which can answer for it. Accepting an ACME order which cannot be fulfilled only defers the failure, so
        /// the ACME endpoints require a match; a direct managed challenge call does not, because fulfillment
        /// reports a missing challenge itself.
        /// </summary>
        public bool RequireSatisfiableChallenge { get; set; }
    }

    public static class ManagedChallengeOperationStates
    {
        public const string Pending = "pending";
        public const string Running = "running";
        public const string Succeeded = "succeeded";
        public const string Failed = "failed";
    }

    public class ManagedChallengeOperation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Status { get; set; } = ManagedChallengeOperationStates.Pending;
        public ManagedChallengeRequest Request { get; set; } = new ManagedChallengeRequest();
        public ActionResult? Result { get; set; }
        public DateTimeOffset DateCreated { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset DateLastUpdated { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? DateStarted { get; set; }
        public DateTimeOffset? DateCompleted { get; set; }

        public bool IsCompleted => Status == ManagedChallengeOperationStates.Succeeded || Status == ManagedChallengeOperationStates.Failed;
        public bool IsSuccess => Status == ManagedChallengeOperationStates.Succeeded;
    }
}
