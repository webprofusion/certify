using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models.Hub;
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
    }
}
