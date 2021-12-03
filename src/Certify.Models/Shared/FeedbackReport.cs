namespace Certify.Models.Shared
{
    public class FeedbackReport
    {
        public string EmailAddress { get; set; }
        public string Comment { get; set; }
        public object SupportingData { get; set; }
        public bool IsException { get; set; }
        public string AppVersion { get; set; }
    }
}
