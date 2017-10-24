using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Utils
{
    using System.ComponentModel;
    
    [AttributeUsage(AttributeTargets.All)]
    public class SRDescription : DescriptionAttribute
    {
        private readonly string _key;

        public SRDescription(string key)
        {
            _key = key;
        }

        /// <summary>Gets the description stored in this attribute.</summary>
        /// <returns>The description stored in this attribute.</returns>
        public override string Description => CoreSR.ResourceManager.GetString(_key);
    }
}
