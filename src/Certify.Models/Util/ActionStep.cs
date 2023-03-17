using System.Collections.Generic;

namespace Certify.Models
{
    public class ActionStep
    {
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool HasError { get; set; }
        public bool HasWarning { get; set; }
        public List<ActionStep>? Substeps { get; set; }
        public string Key { get; set; } = string.Empty;

        public ActionStep() { }
        public ActionStep(string title, string descripton, bool hasError)
        {
            Title = title;
            Description = descripton;
            HasError = hasError;
        }
    }
}
