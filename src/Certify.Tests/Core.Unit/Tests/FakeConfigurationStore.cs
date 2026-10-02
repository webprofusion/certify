using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models.Hub;
using Certify.Providers;
using Newtonsoft.Json;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// In memory stand in for the hub's own configuration store, round tripping items through json the way the
    /// real store does so that the stored form is exercised rather than the in memory object.
    /// </summary>
    internal sealed class FakeConfigurationStore : IConfigurationStore
    {
        private readonly Dictionary<(string ItemType, string Id), string> _items = [];

        /// <summary>Writes performed, so a caller which is meant to write sparingly can be held to it.</summary>
        public int WriteCount { get; private set; }

        public Task<T> Get<T>(string itemType, string id)
        {
            return Task.FromResult(_items.TryGetValue((Normalize<T>(itemType), id), out var json)
                ? JsonConvert.DeserializeObject<T>(json)
                : default);
        }

        public Task Add<T>(string itemType, T item) => Update(itemType, item);

        public Task Update<T>(string itemType, T item)
        {
            WriteCount++;
            _items[(Normalize<T>(itemType), GetId(item))] = JsonConvert.SerializeObject(item);
            return Task.CompletedTask;
        }

        public Task<bool> Delete<T>(string itemType, string id) => Task.FromResult(_items.Remove((Normalize<T>(itemType), id)));

        public Task<List<T>> GetItems<T>(string itemType)
        {
            var normalized = Normalize<T>(itemType);

            return Task.FromResult(_items
                .Where(i => i.Key.ItemType == normalized)
                .Select(i => JsonConvert.DeserializeObject<T>(i.Value))
                .ToList());
        }

        public Task<bool> IsInitialised() => Task.FromResult(true);

        public Task<List<SerializedConfigurationItem>> GetAllSerializedItems() => Task.FromResult(new List<SerializedConfigurationItem>());

        public Task UpsertSerializedItem(SerializedConfigurationItem item) => Task.CompletedTask;

        private static string Normalize<T>(string itemType)
            => string.IsNullOrEmpty(itemType) ? typeof(T).Name.ToLowerInvariant() : itemType.ToLowerInvariant();

        private static string GetId<T>(T item)
        {
            return item switch
            {
                SerializedConfigurationItem configItem => configItem.Id,
                IIdentifiable identifiable => identifiable.Id,
                _ => throw new ArgumentException($"Item of type {typeof(T).Name} has no id")
            };
        }
    }
}
