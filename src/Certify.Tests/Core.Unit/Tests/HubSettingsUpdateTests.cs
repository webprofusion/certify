using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for how an instance applies core settings saved from the management hub
    /// </summary>
    [TestClass]
    public class HubSettingsUpdateTests
    {
        /// <summary>
        /// Settings the hub does not manage. Every other setting must be applied from a hub update, so a new setting has
        /// to be added either to SettingsManager.ApplyHubSettingsUpdate or to this list.
        /// </summary>
        private static readonly HashSet<string> NotHubManaged =
        [
            nameof(Preferences.InstanceId),
            nameof(Preferences.Language),
            nameof(Preferences.IgnoreStoppedSites),
            nameof(Preferences.EnableDNSValidationChecks),
            nameof(Preferences.MaxRenewalRequests),
            nameof(Preferences.DefaultKeyCredentials),
            nameof(Preferences.IncludeExternalPlugins),
            nameof(Preferences.FeatureFlags),
            nameof(Preferences.ConfigDataStoreConnectionId),
            nameof(Preferences.EnableParallelRenewals),
            nameof(Preferences.EnableIssuerCache),

            // proxy settings are not persisted yet
            nameof(Preferences.ProxyEnabled),
            nameof(Preferences.ProxyMode),
            nameof(Preferences.ProxyUri),
            nameof(Preferences.ProxyBypassOnLocal),
            nameof(Preferences.ProxyBypassList),
            nameof(Preferences.ProxyUseDefaultCredentials),
            nameof(Preferences.ProxyCredentialKey)
        ];

        private static PropertyInfo[] GetSettingProperties() => typeof(Preferences)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.CanRead && p.CanWrite)
            .ToArray();

        private static object CreateDifferentValue(PropertyInfo property, object? current)
        {
            var type = property.PropertyType;
            var valueType = Nullable.GetUnderlyingType(type) ?? type;

            if (type == typeof(bool))
            {
                return !(bool)current!;
            }

            if (type == typeof(int))
            {
                return (int)current! + 7;
            }

            if (type == typeof(string))
            {
                return $"hub-{property.Name}";
            }

            if (type == typeof(string[]))
            {
                return new[] { $"hub-{property.Name}" };
            }

            if (valueType.IsEnum)
            {
                return Enum.GetValues(valueType).Cast<object>().First(v => !v.Equals(current));
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                return Activator.CreateInstance(type)!;
            }

            throw new NotSupportedException($"No test value for setting {property.Name} of type {type.Name}");
        }

        [TestMethod, Description("Every setting is either applied from a hub update or deliberately left to the instance")]
        public void TestHubUpdateAppliesEveryHubManagedSetting()
        {
            var properties = GetSettingProperties();

            var unknown = NotHubManaged.Where(n => properties.All(p => p.Name != n)).ToList();
            Assert.IsEmpty(unknown, $"Not a setting: {string.Join(", ", unknown)}");

            var current = new Preferences();
            var update = new Preferences();

            foreach (var property in properties)
            {
                property.SetValue(update, CreateDifferentValue(property, property.GetValue(current)));
            }

            var original = properties.ToDictionary(p => p.Name, p => p.GetValue(current));

            SettingsManager.ApplyHubSettingsUpdate(current, update);

            foreach (var property in properties)
            {
                if (NotHubManaged.Contains(property.Name))
                {
                    Assert.AreEqual(original[property.Name], property.GetValue(current), $"{property.Name} is not managed from the hub but was overwritten by the update");
                }
                else
                {
                    Assert.AreEqual(property.GetValue(update), property.GetValue(current), $"{property.Name} was not applied from the hub update. Add it to SettingsManager.ApplyHubSettingsUpdate, or to {nameof(NotHubManaged)} if the hub should not manage it.");
                }
            }
        }

        [TestMethod, Description("Choosing a cleanup mode also turns cleanup on or off, since cleanup is gated on the enabled flag")]
        [DataRow(CertificateCleanupMode.AfterExpiry, false, CertificateCleanupMode.AfterExpiry, true)]
        [DataRow(CertificateCleanupMode.FullCleanup, false, CertificateCleanupMode.FullCleanup, true)]
        [DataRow(CertificateCleanupMode.None, true, CertificateCleanupMode.None, false)]
        [DataRow(null, true, CertificateCleanupMode.AfterExpiry, true)]
        [DataRow(null, false, CertificateCleanupMode.None, false)]
        public void TestCertificateCleanupFlagFollowsMode(CertificateCleanupMode? mode, bool isEnabled, CertificateCleanupMode expectedMode, bool expectedEnabled)
        {
            var saved = CoreAppSettings.Current;
            CoreAppSettings.Current = JsonConvert.DeserializeObject<CoreAppSettings>(JsonConvert.SerializeObject(saved))!;

            try
            {
                SettingsManager.FromPreferences(new Preferences { CertificateCleanupMode = mode, EnableCertificateCleanup = isEnabled });

                Assert.AreEqual(expectedMode, CoreAppSettings.Current.CertificateCleanupMode);
                Assert.AreEqual(expectedEnabled, CoreAppSettings.Current.EnableCertificateCleanup);
            }
            finally
            {
                CoreAppSettings.Current = saved;
            }
        }
    }
}
