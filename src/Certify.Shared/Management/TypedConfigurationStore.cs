using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Providers;

namespace Certify.Shared.Core.Management
{
    public class TypedConfigurationStore<T>
    {
        IConfigurationStore _store;
        private string _itemType = typeof(T).Name.ToLowerInvariant();

        public TypedConfigurationStore(IConfigurationStore store)
        {
            _store = store;
        }
        public Task<TypedConfigurationItem<T>> Get(string id)
        {
            return _store.Get<TypedConfigurationItem<T>>(_itemType, id);
        }
        public Task Add(T item)
        {
            return _store.Add<TypedConfigurationItem<T>>(_itemType, new TypedConfigurationItem<T>(item));
        }
        public Task Update(string id, T item)
        {
            var typedItem = new TypedConfigurationItem<T>(item);
            typedItem.Id = id;
            return _store.Update<TypedConfigurationItem<T>>(_itemType, typedItem);
        }
        public Task<bool> Delete(string id)
        {
            return _store.Delete<TypedConfigurationItem<T>>(_itemType, id);
        }
        public Task<List<TypedConfigurationItem<T>>> GetItems()
        {
            return _store.GetItems<TypedConfigurationItem<T>>(_itemType);
        }
    }
}
