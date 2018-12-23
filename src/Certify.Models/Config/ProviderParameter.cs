using System.Collections.Generic;

namespace Certify.Models.Config
{
    public class ProviderParameter
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsPassword { get; set; }
        public bool IsRequired { get; set; }
        public string Value { get; set; }
        public bool IsCredential { get; set; } = true;
        public string OptionsList { get; set; }
        public List<string> Options
        {
            get
            {
                List<string> options = new List<string>();
                if (!string.IsNullOrEmpty(OptionsList))
                {
                    options.AddRange(OptionsList.Split(';'));
                }

                return options;
            }
        }
    }
}
