namespace Certify.Models.Config
{
    public class ActionResult
    {
        public ActionResult()
        {
            Message = string.Empty;
        }
        public ActionResult(string msg, bool isSuccess)
        {
            Message = msg;
            IsSuccess = isSuccess;
        }

        public bool IsSuccess { get; set; }
        public bool IsWarning { get; set; }
        public string Message { get; set; }

        /// <summary>
        /// Optional field to hold related information such as required info or error details
        /// </summary>
        public object? Result { get; set; }
    }

    public class ActionResult<T> : ActionResult
    {
        public new T? Result { get; set; }

        public ActionResult()
        {

        }
        public ActionResult(string msg, bool isSuccess)
        {
            Message = msg;
            IsSuccess = isSuccess;
        }
        public ActionResult(string msg, bool isSuccess, T? result)
        {
            Message = msg;
            IsSuccess = isSuccess;
            Result = result;
        }
    }
}
