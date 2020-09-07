using System;
using System.Collections.Generic;
using System.Linq;
using Certify.Models.Config;
using Certify.Models.Plugins;

public class PluginProviderBase<TProviderInterface, TProviderDefinition> : IProviderPlugin<TProviderInterface, TProviderDefinition>
{


    public TProviderInterface GetProvider(Type provider, string id)
    {

        id = id?.ToLower();

        var baseAssembly = provider.Assembly;

        // we filter the defined classes according to the interfaces they implement
        var typeList = baseAssembly.GetTypes().Where(type => type.GetInterfaces().Any(inter => inter == typeof(TProviderInterface))).ToList();

        foreach (var t in typeList)
        {
            TProviderDefinition def = (TProviderDefinition)t.GetProperty("Definition").GetValue(null);
            if (def is ProviderDefinition)
            {
                if ((def as ProviderDefinition).Id.ToLower() == id)
                {
                    return (TProviderInterface)Activator.CreateInstance(t);
                }
            }
        }

        // the requested provider id is not present in this provider plugin, could be in another assembly
        return default(TProviderInterface);
    }

    public List<TProviderDefinition> GetProviders(Type provider)
    {
        var list = new List<TProviderDefinition>();

        var baseAssembly = provider.Assembly;

        // we filter the defined classes according to the interfaces they implement
        var typeList = baseAssembly.GetTypes().Where(type => type.GetInterfaces().Any(inter => inter == typeof(TProviderInterface))).ToList();

        foreach (var t in typeList)
        {
            var def = (TProviderDefinition)t.GetProperty("Definition").GetValue(null);
            list.Add(def);
        }

        return list;
    }
}
