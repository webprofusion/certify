namespace Certify.Models.Config
{
    public class ProcessStepResult
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; } = string.Empty;
        public object? Result { get; set; }
    }
}
