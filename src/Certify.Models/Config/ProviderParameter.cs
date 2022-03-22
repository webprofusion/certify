using System;
using System.Collections.Generic;
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

        public string Key { get; set; }
        public string Value { get; set; }
    }

    public class ProviderParameter : ICloneable
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
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

        // NOTE: this object is cloneable, so any new properties have to be added to Clone()

        /// <summary>
        /// Returns a parsed version of OptionsList converted into a key/value dictionary
        /// </summary>
        [JsonIgnore]
        public Dictionary<string, string> Options
        {
            get
            {
                var options = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(OptionsList))
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
                IsMultiLine = IsMultiLine,
                IsRequired = IsRequired
            };

        }
    }
}

