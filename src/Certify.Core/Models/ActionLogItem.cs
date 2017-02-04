using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public class ActionLogItem
    {
        public DateTime DateTime { get; set; }
        public string Command { get; set; }

        public string Result
        {
            get; set;
        }

        public override string ToString()
        {
            return "[" + DateTime.ToShortTimeString() + "] " + this.Command + (this.Result != null ? " : " + this.Result : "");
        }
    }
}
