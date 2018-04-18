using System.Collections.Generic;

namespace Certify.Models
{
    public class ActionStep
    {
        public string Title { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public bool HasError { get; set; } = false;
        public List<ActionStep> Substeps { get; set; }
        public string Key { get; set; }
    }
}