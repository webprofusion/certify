using System.Collections.Generic;
using System.Linq;
using Certify.SourceGenerators;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SourceGenerator;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class ApiMethodsTests
    {
        [TestMethod]
        public void EachApiMethod_HasRequiredPermissions_AndMockPrincipleCanAccess()
        {
            var apiDefs = ApiMethods.GetApiDefinitions();
            foreach (var api in apiDefs)
            {
                // For each endpoint, create a mock security principle with only the required permissions
                var requiredPerms = api.RequiredPermissions;
                Assert.IsNotNull(requiredPerms, $"API {api.OperationName} should have RequiredPermissions");
                Assert.IsTrue(requiredPerms.Count > 0, $"API {api.OperationName} should have at least one RequiredPermission");

                // Simulate a principle with exactly these permissions
                var mockPrinciple = new MockSecurityPrinciple(requiredPerms);

                // Simulate an authorization check for each required permission
                foreach (var perm in requiredPerms)
                {
                    Assert.IsTrue(mockPrinciple.HasPermission(perm), $"Mock principle should have permission {perm.ResourceType}:{perm.Action} for {api.OperationName}");
                }
            }
        }

        [TestMethod]
        public void EachApiMethod_RequiredPermissionsMatchesStandardAction()
        {
            var standardActions = Models.Hub.Policies.GetStandardResourceActions();

            var apiDefs = ApiMethods.GetApiDefinitions();
            foreach (var api in apiDefs)
            {

                // For each endpoint, create a mock security principle with only the required permissions
                var requiredPerms = api.RequiredPermissions;

                // check standard actions have a matching resource type and action defined
                foreach (var perm in requiredPerms)
                {
                    Assert.IsTrue(standardActions.Any(a => a.ResourceType == perm.ResourceType && a.Id == perm.Action),
                        $"Standard action {perm.ResourceType}:{perm.Action} not found for API {api.OperationName}");
                }
            }
        }
    }

    // Simple mock for demonstration
    public class MockSecurityPrinciple
    {
        private readonly List<PermissionSpec> _perms;
        public MockSecurityPrinciple(List<PermissionSpec> perms)
        {
            _perms = perms;
        }
        public bool HasPermission(PermissionSpec perm)
        {
            return _perms.Any(p => p.ResourceType == perm.ResourceType && p.Action == perm.Action);
        }
    }
}
