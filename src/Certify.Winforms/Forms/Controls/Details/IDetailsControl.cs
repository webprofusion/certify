using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Forms.Controls.Details
{
    internal interface IDetailsControl<T>
    {
        void Populate(T item);
    }
}