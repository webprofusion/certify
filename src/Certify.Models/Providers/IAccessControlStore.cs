using System.Collections.Generic;
using System.Threading.Tasks;

namespace Certify.Providers
{
    public interface IAccessControlStore
    {
        Task<T> Get<T>(string itemType, string id);
        Task Add<T>(string itemType, T item);
        Task Update<T>(string itemType, T item);
        Task<bool> Delete<T>(string itemType, string id);
        Task<List<T>> GetItems<T>(string itemType);
    }
}
