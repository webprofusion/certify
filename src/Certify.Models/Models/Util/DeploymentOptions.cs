namespace Certify.Models
{
    public enum DeploymentOption
    {
        /// <summary>
        /// Use defaults/best guess for deployment 
        /// </summary>
        Auto = 5,

        /// <summary>
        /// Apply certificate to single site 
        /// </summary>
        SingleSite = 10,

        /// <summary>
        /// Apply certificate to all sites 
        /// </summary>
        AllSites = 20,

        /// <summary>
        /// Store in certificate store only 
        /// </summary>
        DeploymentStoreOnly = 30,

        /// <summary>
        /// No Deployment 
        /// </summary>
        NoDeployment = 40
    }

    public enum DeploymentBindingOption
    {
        /// <summary>
        /// Add or Update https bindings as required 
        /// </summary>
        AddOrUpdate = 10,

        /// <summary>
        /// Update existing https bindings only (as required) 
        /// </summary>
        UpdateOnly = 20
    }
}
