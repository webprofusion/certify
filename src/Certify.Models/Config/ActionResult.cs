namespace Certify.Models.Config
{
    public class ActionResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
    }

    public class ActionResult<T>: ActionResult
    {
        public T Result;
    }
}
