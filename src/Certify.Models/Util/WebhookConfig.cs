namespace Certify.Models.Utils
{
    public class WebhookConfig
    {
        /// <summary>
        /// The trigger for the webhook (None, Success, Error) 
        /// </summary>
        public string Trigger { get; set; } = "None";

        /// <summary>
        /// The http method for the webhook request 
        /// </summary>
        public string? Method { get; set; }

        /// <summary>
        /// The http url for the webhook request 
        /// </summary>
        public string? Url { get; set; }

        /// <summary>
        /// The http content type header for the webhook request 
        /// </summary>
        public string? ContentType { get; set; }

        /// <summary>
        /// The http body template for the webhook request 
        /// </summary>
        public string? ContentBody { get; set; }
    }
}
