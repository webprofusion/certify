namespace Certify.Models
{
    public enum SupportedDeploymentModes
    {
        /// <summary>
        /// Apply certificate to bindings on a single site where the hostname bindings match domains
        /// in the certificate
        /// </summary>
        SingleSiteBindingsMatchingDomains,

        /// <summary>
        /// Apply certificate to bindings on a single site (including IP only bindings) 
        /// </summary>
        SingleSiteAllBindings,

        /// <summary>
        /// Apply certificate to bindings on all sites where the hostname bindings match 
        /// </summary>
        AllSitesBindingsMatchingDomains,

        /// <summary>
        /// Apply certificate to all bindings on all sites 
        /// </summary>
        AllSitesAllBindings,

        /// <summary>
        /// Don't update any bindings 
        /// </summary>
        NoDeployment
    }
}