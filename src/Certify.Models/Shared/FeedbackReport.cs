namespace Certify.Models.Shared
{
    public class FeedbackReport
    {
        public string EmailAddress { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public object? SupportingData { get; set; }
        public bool IsException { get; set; }
        public string AppVersion { get; set; } = string.Empty;
    }
}
