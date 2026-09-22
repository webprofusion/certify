using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Certify.Models.Config
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Type names are intentional")]
    public enum OptionType
    {
        String = 1,
        MultiLineText = 2,
        Boolean = 3,
        Select = 4,
        MultiSelect = 5,
        RadioButton = 6,
        /// <summary>
        /// Value is the storage key of a stored credential, ExtendedConfig is the required credential type (e.g. StandardAuthTypes.STANDARD_AUTH_PASSWORD)
        /// </summary>
        StoredCredential = 8,
        Integer = 16
    }

    /// <summary>
    /// Previously (4.1.x and lower) parameters where stored as ProviderParameter, ProviderParameterSetting provides
    /// a simpler object for storage and remains compatible for serialize/deserialize
    /// </summary>
    public class ProviderParameterSetting
    {
        public ProviderParameterSetting(string key, string value)
        {
            Key = key;
            Value = value;
        }

        public string? Key { get; set; }
        public string? Value { get; set; }
    }

    public class ProviderParameter : ICloneable
    {
        public string? Key { get; set; } = string.Empty;
        public string? Name { get; set; } = string.Empty;
        public string? Description { get; set; } = string.Empty;
        public bool IsPassword { get; set; }
        public bool IsMultiLine { get; set; }
        public bool IsRequired { get; set; }
        public bool IsHidden { get; set; }
        public string? Value { get; set; }
        public bool IsCredential { get; set; } = true;

        /// <summary>
        /// Options list in the format key1=title1;key2=title2;key3;key4;
        /// </summary>
        public string? OptionsList { get; set; }

        public OptionType? Type { get; set; }

        /// <summary>
        /// used to store metadata such as a credential type for credential option selection
        /// </summary>
        public string? ExtendedConfig { get; set; }

        /// <summary>
        /// Optional key of another parameter this parameter depends on. If set, this parameter only applies (is shown, and its value kept on save) while that parameter has one of the <see cref="DependsOnValues"/>
        /// </summary>
        public string? DependsOnKey { get; set; }

        /// <summary>
        /// Values of the <see cref="DependsOnKey"/> parameter for which this parameter applies
        /// </summary>
        public string[]? DependsOnValues { get; set; }

        // NOTE: this object is cloneable, so any new properties have to be added to Clone()

        /// <summary>
        /// Returns true if this parameter applies given the current values of the other parameters in its set (see <see cref="DependsOnKey"/>)
        /// </summary>
        public bool IsApplicable(IEnumerable<ProviderParameter> parameters)
        {
            if (string.IsNullOrEmpty(DependsOnKey))
            {
                return true;
            }

            var dependencyValue = parameters.FirstOrDefault(p => p?.Key == DependsOnKey)?.Value;

            return dependencyValue != null && DependsOnValues?.Contains(dependencyValue) == true;
        }

        /// <summary>
        /// Get the settings to store for a set of edited parameters. Parameters which do not currently apply are omitted, so a value which no longer applies is cleared rather than kept
        /// </summary>
        public static List<ProviderParameterSetting> GetApplicableSettings(IEnumerable<ProviderParameter> parameters)
        {
            var list = parameters.Where(p => p != null).ToList();

            return list
                .Where(p => p.IsApplicable(list))
                .Select(p => new ProviderParameterSetting(p.Key!, p.Value!))
                .ToList();
        }

        /// <summary>
        /// Returns a parsed version of OptionsList converted into a key/value dictionary
        /// </summary>
        [JsonIgnore]
        public Dictionary<string, string> Options
        {
            get
            {
                var options = new Dictionary<string, string>();
                if (OptionsList != null && !string.IsNullOrEmpty(OptionsList))
                {
                    foreach (var o in OptionsList.Split(';'))
                    {
                        if (!string.IsNullOrEmpty(o))
                        {
                            var keyValuePair = o.Split('=');

                            if (keyValuePair.Length == 1)
                            {
                                // item has a key only
                                options.Add(keyValuePair[0].Trim(), keyValuePair[0].Trim());
                            }
                            else
                            {
                                // item has a key and description value
                                options.Add(keyValuePair[0].Trim(), keyValuePair[1].Trim());
                            }
                        }
                    }
                }

                return options;
            }
        }

        public object Clone()
        {
            return new ProviderParameter
            {
                Key = Key,
                Name = Name,
                Description = Description,
                IsPassword = IsPassword,
                IsHidden = IsHidden,
                Value = Value,
                IsCredential = IsCredential,
                OptionsList = OptionsList,
                Type = Type,
                ExtendedConfig = ExtendedConfig,
                DependsOnKey = DependsOnKey,
                DependsOnValues = DependsOnValues?.ToArray(),
                IsMultiLine = IsMultiLine,
                IsRequired = IsRequired
            };

        }
    }
}

