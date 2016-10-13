using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Classes
{
    public class APIResult
    {
        public bool IsOK { get; set; }
        public string Message { get; set; }
        public object Result { get; set; }
    }
}
