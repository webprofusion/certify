using System.Collections.Generic;
using System.Threading.Tasks;

namespace Certify.Models.Providers
{
    public interface IBindingDeploymentTargetItem
    {
        string Id { get; set; }
        string Name { get; set; }
    }

    public interface IBindingDeploymentTarget
    {
        string GetTargetName();

        Task<IBindingDeploymentTargetItem> GetTargetItem(string id);

        Task<List<IBindingDeploymentTargetItem>> GetAllTargetItems();

        /// <summary>
        /// Creates a new binding with the given spec 
        /// </summary>
        /// <param name="targetBinding"></param>
        /// <returns></returns>
        Task<ActionStep> AddBinding(BindingInfo targetBinding);

        /// <summary>
        /// Updates the certificate hash and certificate store assignment for an existing binding 
        /// </summary>
        /// <param name="updatedBindingInfo"></param>
        /// <returns></returns>
        Task<ActionStep> UpdateBinding(BindingInfo targetBinding);

        Task<List<BindingInfo>> GetBindings(string targetItemId);
    }
}
