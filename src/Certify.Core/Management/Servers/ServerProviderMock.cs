using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Providers;

namespace Certify.Core.Management
{
    public class MockBindingDeploymentTargetItem : IBindingDeploymentTargetItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class MockBindingDeploymentTarget : IBindingDeploymentTarget
    {
        public List<BindingInfo> AllBindings { get; set; } = new List<BindingInfo>();

        public async Task<IBindingDeploymentTargetItem> GetTargetItem(string id)
        {
            var firstMatch = AllBindings.FirstOrDefault(f => f.SiteId == id);
            if (firstMatch != null)
            {
                return await Task.FromResult(new MockBindingDeploymentTargetItem { Id = firstMatch.SiteId, Name = firstMatch.SiteName });
            }
            else
            {
                return null;
            }
        }

        public async Task<List<IBindingDeploymentTargetItem>> GetAllTargetItems()
        {
            var all = new List<IBindingDeploymentTargetItem>();

            foreach (var b in AllBindings)
            {
                if (!all.Any(site => site.Id == b.SiteId))
                {
                    all.Add(new MockBindingDeploymentTargetItem { Id = b.SiteId, Name = b.SiteName });
                }
            }

            return await Task.FromResult(all);
        }

        public async Task<List<BindingInfo>> GetBindings(string targetItemId)
        {
            return await Task.FromResult(AllBindings.Where(b => b.SiteId == targetItemId).ToList());
        }

        public ICertifiedServer GetDeploymentManager()
        {
            return new ServerProviderMock();
        }

        public string GetTargetName()
        {
            return "Mock Binding Target";
        }

        public void Dispose()
        {
        }

        public async Task<ActionStep> AddBinding(BindingInfo targetBinding)
        {
            return await Task.FromResult(new ActionStep { Description = "Added Binding" });
        }

        public async Task<ActionStep> UpdateBinding(BindingInfo targetBinding)
        {
            return await Task.FromResult(new ActionStep { Description = "Updated Binding" });
        }
    }

    public class ServerProviderMock : ICertifiedServer
    {
        public Task<bool> CommitChanges()
        {
            return Task.FromResult(true);
        }

        public Task<bool> CreateManagementContext()
        {
            return Task.FromResult(true);
        }

        public void Dispose()
        {
        }

        public Task<List<BindingInfo>> GetPrimarySites(bool ignoreStoppedSites)
        {
            throw new NotImplementedException();
        }

        public Task<Version> GetServerVersion()
        {
            throw new NotImplementedException();
        }

        public Task<List<BindingInfo>> GetSiteBindingList(bool ignoreStoppedSites, string siteId = null)
        {
            throw new NotImplementedException();
        }

        public Task<SiteInfo> GetSiteById(string siteId)
        {
            throw new NotImplementedException();
        }

        public Task<bool> IsAvailable()
        {
            throw new NotImplementedException();
        }

        public Task<bool> IsSiteRunning(string id)
        {
            throw new NotImplementedException();
        }

        public Task RemoveHttpsBinding(string siteId, string sni)
        {
            throw new NotImplementedException();
        }

        public IBindingDeploymentTarget GetDeploymentTarget()
        {
            return new MockBindingDeploymentTarget();
        }

        public Task<List<ActionStep>> RunConfigurationDiagnostics(string siteId)
        {
            throw new NotImplementedException();
        }
    }
}
