using System;
using System.Collections.Generic;

namespace Certify.Models
{
    /// <summary>
    /// Generic representation of a proof of ownership challenge 
    /// </summary>
    public class AuthorizationChallengeItem
    {
        public string? ChallengeType { get; set; }
        public object? ChallengeData { get; set; }

        public string? Key { get; set; }
        public string? Value { get; set; }
        public string? ResourcePath { get; set; }
        public string? ResourceUri { get; set; }
        public int HashIterationCount { get; set; }

        /// <summary>
        /// A challenge may already have been validated in a previous request and this may still be
        /// OK to proceed without further validation steps
        /// </summary>
        public bool IsValidated { get; set; }

        /// <summary>
        /// If true, wait for user intervention before proceeding with this challenge (i.e. manual
        /// DNS record creation)
        /// </summary>
        public bool IsAwaitingUser { get; set; }

        public int PropagationSeconds { get; set; }

        /// <summary>
        /// Depending on configuration we may perform a config check confirming we can meet the
        /// validation challenge requirements before performing request against ACME server
        /// </summary>
        public bool ConfigCheckedOK { get; set; }

        public string? ChallengeResultMsg { get; set; }

        public bool IsFailure { get; set; }
    }

    public class SimpleAuthorizationChallengeItem
    {
        public string? ChallengeType { get; set; } = string.Empty;
        public string? Key { get; set; }
        public string? Value { get; set; }
    }

    /// <summary>
    /// Fora given (domain) identifier, list of Challenges we can satisfy to prove ownership/control 
    /// </summary>
    public class PendingAuthorization
    {
        /// <summary>
        /// List of possible challenge we can attempt for this authorization 
        /// </summary>
        public List<AuthorizationChallengeItem>? Challenges { get; set; }

        /// <summary>
        /// Identifier (Dns domain) we are attempting to get authorization for 
        /// </summary>
        public IdentifierItem? Identifier { get; set; }

        public string? TempFilePath { get; set; }

        public Action Cleanup { get; set; } = () => { };
        public List<string>? LogItems { get; set; }
        public string? AuthorizationError { get; set; }
        public bool IsValidated { get; set; }
        public bool IsFailure { get; set; }

        /// <summary>
        /// The challenge we have attempted for this authorization request 
        /// </summary>
        public AuthorizationChallengeItem? AttemptedChallenge { get; set; }

        public object? AuthorizationContext { get; set; }

        public string? OrderUri { get; set; }
    }
}
