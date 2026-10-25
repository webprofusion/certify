using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models.Hub;
using Certify.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    [TestClass]
    public class HubItemTagsTests
    {
        private CertifyManager _manager;
        private MemoryObjectStore _store;

        [TestInitialize]
        public async Task Setup()
        {
            _store = new MemoryObjectStore();
            _manager = new CertifyManager();

            // Use reflection to set the private _configStore field
            var field = typeof(CertifyManager)
                .GetField("_configStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(_manager, _store);

            // Create required tag categories for tests
            await _manager.AddOrUpdateTagCategory(new TagCategory
            {
                CategoryKey = "tag1",
                DisplayName = "Tag 1"
            });
            await _manager.AddOrUpdateTagCategory(new TagCategory
            {
                CategoryKey = "tag2",
                DisplayName = "Tag 2"
            });
            await _manager.AddOrUpdateTagCategory(new TagCategory
            {
                CategoryKey = "tag3",
                DisplayName = "Tag 3"
            });
        }

        [TestMethod]
        public async Task AddHubItemTags_AddsTags()
        {
            var tags = new List<ItemTag>
            {
                new ItemTag("item1", "type1", "tag1", "value1"),
                new ItemTag("item2", "type2", "tag2", "value2")
            };

            var result = await _manager.AddHubItemTags(tags);
            Assert.IsTrue(result.IsSuccess);

            var allTags = await _manager.GetAllHubItemTags(null, null, null, null);
            Assert.HasCount(2, allTags);
        }

        [TestMethod]
        public async Task RemoveHubItemTags_RemovesTags()
        {
            var tag = new ItemTag("item1", "type1", "tag1", "value1");
            await _manager.AddHubItemTags(new List<ItemTag> { tag });
            var allTags = await _manager.GetAllHubItemTags(null, null, null, null);
            Assert.HasCount(1, allTags);

            var result = await _manager.RemoveHubItemTags(new List<string> { allTags.First().Id });
            Assert.IsTrue(result.IsSuccess);

            allTags = await _manager.GetAllHubItemTags(null, null, null, null);
            Assert.IsEmpty(allTags);
        }

        [TestMethod]
        public async Task GetAllHubItemTags_ReturnsAllTags()
        {
            await _manager.AddHubItemTags(new List<ItemTag>
            {
                new ItemTag("item1", "type1", "tag1", "value1"),
                new ItemTag("item2", "type2", "tag2", "value2")
            });
            var allTags = await _manager.GetAllHubItemTags(null, null, null, null);
            Assert.HasCount(2, allTags);
        }

        [TestMethod]
        public async Task GetAllHubItemTags_FiltersByCategoryKey()
        {
            await _manager.AddHubItemTags(new List<ItemTag>
            {
                new ItemTag("item1", "type1", "tag1", "value1"),
                new ItemTag("item2", "type1", "tag2", "value2")
            });

            var filteredTags = await _manager.GetAllHubItemTags("tag1", null, null, null);
            Assert.HasCount(1, filteredTags);
            Assert.AreEqual("tag1", filteredTags.First().CategoryKey);
        }

        [TestMethod]
        public async Task GetAllHubItemTags_FiltersByItemType()
        {
            await _manager.AddHubItemTags(new List<ItemTag>
            {
                new ItemTag("item1", "type1", "tag1", "value1"),
                new ItemTag("item2", "type2", "tag2", "value2")
            });

            var filteredTags = await _manager.GetAllHubItemTags(null, null, "type1", null);
            Assert.HasCount(1, filteredTags);
            Assert.AreEqual("type1", filteredTags.First().TaggedItemType);
        }

        [TestMethod]
        public async Task GetAllHubItemTags_FiltersByInstanceId()
        {
            await _manager.AddHubItemTags(new List<ItemTag>
            {
                new ItemTag("item1", "type1", "tag1", "value1", "instance1"),
                new ItemTag("item2", "type1", "tag2", "value2", "instance2")
            });

            var filteredTags = await _manager.GetAllHubItemTags(null, null, null, "instance1");
            Assert.HasCount(1, filteredTags);
            Assert.AreEqual("instance1", filteredTags.First().InstanceId);
        }

        [TestMethod]
        public async Task GetHubItemTags_ReturnsTagSummariesForItem()
        {
            await _manager.AddHubItemTags(new List<ItemTag>
            {
                new ItemTag("item1", "type1", "tag1", "value1"),
                new ItemTag("item1", "type1", "tag2", "value2"),
                new ItemTag("item2", "type2", "tag3", "value3")
            });

            // GetHubItemTags takes (itemTypeId, itemId)
            var tagsForItem1 = await _manager.GetHubItemTags("type1", "item1");
            Assert.HasCount(2, tagsForItem1);

            // TagSummary contains CategoryKey and Value
            Assert.IsTrue(tagsForItem1.All(t => t.CategoryKey == "tag1" || t.CategoryKey == "tag2"));
        }

        [TestMethod]
        public async Task GetHubItemTags_ReturnsTagSummaryWithDisplayInfo()
        {
            await _manager.AddHubItemTags(new List<ItemTag>
            {
                new ItemTag("item1", "type1", "tag1", "testvalue")
            });

            var tags = await _manager.GetHubItemTags("type1", "item1");
            Assert.HasCount(1, tags);

            var tag = tags.First();
            Assert.AreEqual("tag1", tag.CategoryKey);
            Assert.AreEqual("Tag 1", tag.CategoryDisplayName); // From category setup
            Assert.AreEqual("testvalue", tag.Value);
        }

        [TestMethod]
        public async Task AddHubItemTags_AllowsDomainUnicodeAndEmojiValues()
        {
            var result = await _manager.AddHubItemTags(new List<ItemTag>
            {
                new ItemTag("item1", "type1", "tag1", "example.com"),
                new ItemTag("item2", "type2", "tag2", "東京"),
                new ItemTag("item3", "type3", "tag3", "🏳️‍🌈")
            });

            Assert.IsTrue(result.IsSuccess);

            var allTags = await _manager.GetAllHubItemTags(null, null, null, null);
            Assert.HasCount(3, allTags);
            Assert.IsTrue(allTags.Any(t => t.Value == "example.com"));
            Assert.IsTrue(allTags.Any(t => t.Value == "東京"));
            Assert.IsTrue(allTags.Any(t => t.Value == "🏳️‍🌈"));
        }

        [TestMethod]
        public async Task RemoveHubItemTagByKey_RemovesSpecificTag()
        {
            await _manager.AddHubItemTags(new List<ItemTag>
            {
                new ItemTag("item1", "type1", "tag1", "value1"),
                new ItemTag("item1", "type1", "tag2", "value2")
            });

            var result = await _manager.RemoveHubItemTagByKey("item1", "type1", "tag1", "value1", null);
            Assert.IsTrue(result.IsSuccess);

            var remainingTags = await _manager.GetHubItemTags("type1", "item1");
            Assert.HasCount(1, remainingTags);
            Assert.AreEqual("tag2", remainingTags.First().CategoryKey);
        }

        [TestMethod]
        public async Task AddHubItemTags_SkipsInvalidCategories()
        {
            var tags = new List<ItemTag>
            {
                new ItemTag("item1", "type1", "tag1", "value1"), // Valid
                new ItemTag("item2", "type2", "nonexistent", "value2") // Invalid category
            };

            var result = await _manager.AddHubItemTags(tags);
            Assert.IsTrue(result.IsSuccess);

            var allTags = await _manager.GetAllHubItemTags(null, null, null, null);
            Assert.HasCount(1, allTags); // Only valid tag added
        }

        [TestMethod]
        public async Task AddHubItemTags_PreventsDuplicates()
        {
            var tag = new ItemTag("item1", "type1", "tag1", "value1");

            await _manager.AddHubItemTags(new List<ItemTag> { tag });
            await _manager.AddHubItemTags(new List<ItemTag> { tag }); // Try to add same tag again

            var allTags = await _manager.GetAllHubItemTags(null, null, null, null);
            Assert.HasCount(1, allTags); // Should still be only 1
        }

        [TestMethod]
        public async Task AddOrUpdateTagCategory_ConcurrentCreatesStoreOneCategory()
        {
            var store = UseYieldingReadStore();

            await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _manager.AddOrUpdateTagCategory(new TagCategory
            {
                CategoryKey = "managed-by",
                DisplayName = "Managed By"
            })));

            Assert.AreEqual(5, store.Reads);
            var stored = await _store.GetItems<TagCategory>(nameof(TagCategory));
            Assert.HasCount(1, stored.Where(c => c.CategoryKey == "managed-by"));
        }

        [TestMethod]
        public async Task GetOrCreateTagValue_ConcurrentCreatesStoreOneValue()
        {
            UseYieldingReadStore();

            await Task.WhenAll(new[] { "Prod", "prod", "Prod", "PROD", "prod" }.Select(v => _manager.GetOrCreateTagValue("tag1", v)));

            Assert.HasCount(1, await _manager.GetTagValues("tag1"));
        }

        [TestMethod]
        public async Task AddHubItemTags_ConcurrentAddsStoreOneTag()
        {
            UseYieldingReadStore();

            await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _manager.AddHubItemTags(new List<ItemTag>
            {
                new ItemTag("item1", "type1", "tag1", "value1")
            })));

            Assert.HasCount(1, await _manager.GetAllHubItemTags(null, null, null, null));
        }

        [TestMethod]
        public async Task AddHubItemTags_ConcurrentAddsToSingleValueCategoryLeaveOneValue()
        {
            await _manager.AddOrUpdateTagCategory(new TagCategory { CategoryKey = "env", DisplayName = "Environment", IsSingleValue = true });
            UseYieldingReadStore();

            await Task.WhenAll(new[] { "prod", "test", "dev", "qa", "staging" }.Select(v => _manager.AddHubItemTags(new List<ItemTag>
            {
                new ItemTag("item1", "type1", "env", v)
            })));

            Assert.HasCount(1, await _manager.GetHubItemTags("type1", "item1"));
        }

        [TestMethod]
        public async Task AddHubItemTags_SingleValueCategoryReplacesAllExistingValues()
        {
            await _manager.AddOrUpdateTagCategory(new TagCategory { CategoryKey = "env", DisplayName = "Environment" });
            await _manager.AddHubItemTags(new List<ItemTag>
            {
                new ItemTag("item1", "type1", "env", "prod"),
                new ItemTag("item1", "type1", "env", "test")
            });

            // an item can hold several values from before its category became single-value
            await _manager.AddOrUpdateTagCategory(new TagCategory { CategoryKey = "env", DisplayName = "Environment", IsSingleValue = true });
            await _manager.AddHubItemTags(new List<ItemTag> { new ItemTag("item1", "type1", "env", "dev") });

            var itemTags = await _manager.GetHubItemTags("type1", "item1");
            Assert.HasCount(1, itemTags);
            Assert.AreEqual("dev", itemTags.Single().Value);
        }

        [TestMethod]
        public async Task EnsureDefaultTagCategories_MergesDuplicateTags()
        {
            await AddStoredWithRandomId(new TagCategory { CategoryKey = "managed-by", DisplayName = "Managed By" });
            await AddStoredWithRandomId(new TagCategory { CategoryKey = "managed-by", DisplayName = "Managed By" });
            await AddStoredWithRandomId(new TagCategory { CategoryKey = "tag1", DisplayName = "Tag 1" });
            await AddStoredWithRandomId(new TagValue { CategoryKey = "tag1", Value = "value1", UsageCount = 2 });
            await AddStoredWithRandomId(new TagValue { CategoryKey = "tag1", Value = "value1", UsageCount = 2 });
            await AddStoredWithRandomId(new ItemTag("item1", "type1", "tag1", "value1"));
            await AddStoredWithRandomId(new ItemTag("item1", "type1", "tag1", "value1"));
            await _manager.AddHubItemTags(new List<ItemTag> { new ItemTag("item2", "type1", "tag1", "value1") });

            await _manager.EnsureDefaultTagCategories();

            var categories = await _store.GetItems<TagCategory>(nameof(TagCategory));
            Assert.IsTrue(categories.GroupBy(c => c.CategoryKey).All(g => g.Count() == 1));
            Assert.HasCount(1, categories.Where(c => c.CategoryKey == "managed-by"));

            // the category created through the API keeps its id, and existing tags still resolve
            Assert.AreEqual("tagcategory:tag1", (await _manager.GetTagCategory("tag1"))?.Id);

            var values = await _manager.GetTagValues("tag1");
            Assert.HasCount(1, values);
            Assert.AreEqual(2, values.Single().UsageCount);

            var itemTags = await _manager.GetAllHubItemTags(null, null, null, null);
            Assert.HasCount(2, itemTags);
            Assert.IsTrue(itemTags.All(t => t.Id.StartsWith("itemtag:")));

            // a second start finds nothing left to change
            await _manager.EnsureDefaultTagCategories();
            Assert.HasCount(2, await _manager.GetAllHubItemTags(null, null, null, null));
        }

        [TestMethod]
        public async Task UpdateTagValue_RenamedValueCanBeAddedAgain()
        {
            await _manager.AddHubItemTags(new List<ItemTag> { new ItemTag("item1", "type1", "tag1", "old") });
            var oldValue = (await _manager.GetTagValues("tag1")).Single();

            var result = await _manager.UpdateTagValue(oldValue.Id, "new");
            Assert.IsTrue(result.IsSuccess);

            // the renamed value and tag no longer hold the ids for "old", so adding "old" again stores alongside them
            await _manager.AddHubItemTags(new List<ItemTag> { new ItemTag("item1", "type1", "tag1", "old") });

            CollectionAssert.AreEquivalent(new[] { "new", "old" }, (await _manager.GetTagValues("tag1")).Select(v => v.Value).ToArray());
            CollectionAssert.AreEquivalent(new[] { "new", "old" }, (await _manager.GetHubItemTags("type1", "item1")).Select(t => t.Value).ToArray());
        }

        [TestMethod]
        public async Task MergeTagValues_ItemWithBothValuesKeepsOneTag()
        {
            await _manager.AddHubItemTags(new List<ItemTag>
            {
                new ItemTag("item1", "type1", "tag1", "source"),
                new ItemTag("item1", "type1", "tag1", "target")
            });
            var values = await _manager.GetTagValues("tag1");

            var result = await _manager.MergeTagValues([values.Single(v => v.Value == "source").Id], values.Single(v => v.Value == "target").Id);
            Assert.IsTrue(result.IsSuccess);

            var itemTags = await _manager.GetHubItemTags("type1", "item1");
            Assert.HasCount(1, itemTags);
            Assert.AreEqual("target", itemTags.Single().Value);
        }

        /// <summary>
        /// Every read of the store yields before returning, so overlapping requests all read before any of them writes, as they can on a real store
        /// </summary>
        private YieldingReadStore UseYieldingReadStore()
        {
            var store = new YieldingReadStore(_store);
            typeof(CertifyManager)
                .GetField("_configStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_manager, store);
            return store;
        }

        /// <summary>
        /// Write an item with a random id, as older versions did, bypassing the duplicate checks
        /// </summary>
        private Task AddStoredWithRandomId<T>(T item) where T : ConfigurationStoreItem
        {
            item.Id = System.Guid.NewGuid().ToString();
            return _store.Add<T>(typeof(T).Name, item);
        }

        private sealed class YieldingReadStore(IConfigurationStore inner) : IConfigurationStore
        {
            private int _reads;

            public int Reads => _reads;

            public async Task<List<T>> GetItems<T>(string itemType)
            {
                var items = await inner.GetItems<T>(itemType);
                Interlocked.Increment(ref _reads);
                await Task.Delay(50);
                return items;
            }

            public Task<T> Get<T>(string itemType, string id) => inner.Get<T>(itemType, id);
            public Task Add<T>(string itemType, T item) => inner.Add(itemType, item);
            public Task Update<T>(string itemType, T item) => inner.Update(itemType, item);
            public Task<bool> Delete<T>(string itemType, string id) => inner.Delete<T>(itemType, id);
            public Task<bool> IsInitialised() => inner.IsInitialised();
            public Task<List<SerializedConfigurationItem>> GetAllSerializedItems() => inner.GetAllSerializedItems();
            public Task UpsertSerializedItem(SerializedConfigurationItem item) => inner.UpsertSerializedItem(item);
        }
    }
}
