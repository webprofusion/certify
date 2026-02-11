using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models.Hub;
using Newtonsoft.Json;

namespace Certify.Providers
{
    public interface IConfigurationStore
    {
        Task<T> Get<T>(string itemType, string id);
        Task Add<T>(string itemType, T item);
        Task Update<T>(string itemType, T item);
        Task<bool> Delete<T>(string itemType, string id);
        Task<List<T>> GetItems<T>(string itemType);
        Task<bool> IsInitialised();
        Task<List<SerializedConfigurationItem>> GetAllSerializedItems();
        Task UpsertSerializedItem(SerializedConfigurationItem item);
    }

    /// <summary>
    /// Base class for individual configuration items stored in the database as json format Config
    /// </summary>
    public class SerializedConfigurationItem
    {
        public string Id { get; set; }
        public string ItemType { get; set; }
        public string Config { get; set; }
        public string ItemValue { get; set; }
    }

    /// <summary>
    /// Strongly typed configuration item for individual objects
    /// </summary>
    /// <typeparam name="T">The type of object being stored</typeparam>
    public class TypedConfigurationItem<T> : SerializedConfigurationItem
    {
        public TypedConfigurationItem()
        {
            ItemType = typeof(T).Name.ToLowerInvariant();
        }

        public TypedConfigurationItem(string id, T item) : this()
        {
            Id = id;
            SetItem(item);
        }
        public TypedConfigurationItem(T item) : this()
        {
            if (item is IIdentifiable ci && !string.IsNullOrEmpty(ci.Id))
            {
                Id = ci.Id;
            }
            else
            {
                var idProperty = typeof(T).GetProperty("Id");
                if (idProperty != null && idProperty.PropertyType == typeof(string))
                {
                    Id = (string)idProperty.GetValue(item);
                }
                else
                {
                    Id = Guid.NewGuid().ToString();
                }
            }

            SetItem(item);
        }
        public void SetItem(T item)
        {
            Config = JsonConvert.SerializeObject(item, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
        }

        public T GetItem()
        {
            if (string.IsNullOrEmpty(Config))
            {
                return default(T);
            }

            return JsonConvert.DeserializeObject<T>(Config);
        }
    }
}
