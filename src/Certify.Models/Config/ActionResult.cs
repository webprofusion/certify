namespace Certify.Models.Config
{
    public class ActionResult
    {
        public ActionResult() { }
        public ActionResult(string msg, bool isSuccess)
        {
            Message = msg;
            IsSuccess = isSuccess;
        }

        public bool IsSuccess { get; set; }
        public string Message { get; set; }
    }

    public class ActionResult<T> : ActionResult
    {
        public T Result;

        public ActionResult() { }
        public ActionResult(string msg, bool isSuccess)
        {
            Message = msg;
            IsSuccess = isSuccess;
        }
        public ActionResult(string msg, bool isSuccess, T result)
        {
            Message = msg;
            IsSuccess = isSuccess;
            Result = result;
        }

    }
}
