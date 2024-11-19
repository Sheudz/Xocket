using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xocket
{
    public class Result
    {
        public bool Success { get; }
        public string Message { get; }

        private Result(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        internal static Result Ok(string message = "successfully.")
        {
            return new Result(true, message);
        }

        internal static Result Fail(string message)
        {
            return new Result(false, message);
        }
    }
}
