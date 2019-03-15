namespace Certify.Models
{
    public class WebhookResult
    {
        public WebhookResult(bool success, int statusCode)
        {
            Success = success;
            StatusCode = statusCode;
        }

        public bool Success = false;

        public int StatusCode = 0;
    }
}
