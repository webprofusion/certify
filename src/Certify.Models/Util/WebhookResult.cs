namespace Certify.Models
{
    public class WebhookResult
    {
        public WebhookResult(bool success, int statusCode)
        {
            Success = success;
            StatusCode = statusCode;
        }

        public bool Success { get; set; }

        public int StatusCode { get; set; }
    }
}
