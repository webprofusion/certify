using System;
using System.Collections.Generic;
using System.Linq;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Certify.Plugins
{
    public class PluginProviderBase<TProviderInterface, TProviderDefinition> : IProviderPlugin<TProviderInterface, TProviderDefinition>
    {

        public PluginProviderBase()
        {
        }

        // optionally support dependency injection
        public PluginProviderBase(IServiceProvider serviceProvider)
        {
            _services = serviceProvider;
        }

        private IServiceProvider? _services { get; }

        public TProviderInterface GetProvider(Type pluginType, string? id)
        {

            id = id?.ToLowerInvariant();

            var baseAssembly = pluginType.Assembly;

            // we filter the defined classes according to the interfaces they implement
            var typeList = baseAssembly.GetTypes().Where(type => type.GetInterfaces().Any(inter => inter == typeof(TProviderInterface))).ToList();

            foreach (var t in typeList)
            {
                var def = (TProviderDefinition)t.GetProperty("Definition").GetValue(null);
                if (def != null && def is ProviderDefinition)
                {
                    if ((def as ProviderDefinition)?.Id?.ToLowerInvariant() == id)
                    {
                        if (_services == null)
                        {
                            return (TProviderInterface)Activator.CreateInstance(t);
                        }
                        else
                        {
                            return (TProviderInterface)ActivatorUtilities.CreateInstance(_services, t);
                        }
                    }
                }
            }

            // the requested provider id is not present in this provider plugin, could be in another assembly
#pragma warning disable CS8603 // Possible null reference return.
            return default;
#pragma warning restore CS8603 // Possible null reference return.
        }

        public List<TProviderDefinition> GetProviders(Type pluginType)
        {
            var list = new List<TProviderDefinition>();

            var baseAssembly = pluginType.Assembly;

            // we filter the defined classes according to the interfaces they implement
            var typeList = baseAssembly.GetTypes().Where(type => type.GetInterfaces().Any(inter => inter == typeof(TProviderInterface))).ToList();

            foreach (var t in typeList)
            {
                try
                {
                    var def = (TProviderDefinition)t.GetProperty("Definition").GetValue(null);
                    list.Add(def);
                }
                catch (Exception)
                {
                    System.Diagnostics.Debug.WriteLine($"Plugin Type {t.Name} does not implement a Provider Definition");
                }
            }

            return list;
        }
    }
}
