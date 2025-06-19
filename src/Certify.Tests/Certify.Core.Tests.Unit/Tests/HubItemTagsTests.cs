using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models.Hub;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class HubItemTagsTests
    {
        private CertifyManager _manager;
        private MemoryObjectStore _store;

        [TestInitialize]
        public void Setup()
        {
            _store = new MemoryObjectStore();
            _manager = new CertifyManager();
            typeof(CertifyManager)
                .GetField("_configStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(_manager, _store);
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

            var allTags = await _manager.GetAllHubItemTags();
            Assert.AreEqual(2, allTags.Count);
        }

        [TestMethod]
        public async Task RemoveHubItemTags_RemovesTags()
        {
            var tag = new ItemTag("item1", "type1", "tag1", "value1");
            await _manager.AddHubItemTags(new List<ItemTag> { tag });
            var allTags = await _manager.GetAllHubItemTags();
            Assert.AreEqual(1, allTags.Count);

            var result = await _manager.RemoveHubItemTags(new List<string> { allTags.First().Id });
            Assert.IsTrue(result.IsSuccess);

            allTags = await _manager.GetAllHubItemTags();
            Assert.AreEqual(0, allTags.Count);
        }

        [TestMethod]
        public async Task GetAllHubItemTags_ReturnsAllTags()
        {
            await _manager.AddHubItemTags(new List<ItemTag>
            {
                new ItemTag("item1", "type1", "tag1", "value1"),
                new ItemTag("item2", "type2", "tag2", "value2")
            });
            var allTags = await _manager.GetAllHubItemTags();
            Assert.AreEqual(2, allTags.Count);
        }

        [TestMethod]
        public async Task GetHubItemTags_ReturnsTagsForItem()
        {
            await _manager.AddHubItemTags(new List<ItemTag>
            {
                new ItemTag("item1", "type1", "tag1", "value1"),
                new ItemTag("item1", "type1", "tag2", "value2"),
                new ItemTag("item2", "type2", "tag3", "value3")
            });
            var tagsForItem1 = await _manager.GetHubItemTags("item1", "type1");
            Assert.AreEqual(2, tagsForItem1.Count);
            Assert.IsTrue(tagsForItem1.All(t => t.TaggedItemId == "item1"));
        }
    }
}
