using System;
using System.ComponentModel;

namespace Certify.Locales
{
    [AttributeUsage(AttributeTargets.All)]
    public class SRDescription : DescriptionAttribute
    {
        private readonly string _key;

        public SRDescription(string key)
        {
            _key = key;
        }

        /// <summary>
        /// Gets the description stored in this attribute. 
        /// </summary>
        /// <returns> The description stored in this attribute. </returns>
        public override string Description => CoreSR.ResourceManager.GetString(_key);
    }
}