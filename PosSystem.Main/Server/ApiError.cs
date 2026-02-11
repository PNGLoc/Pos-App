using Microsoft.AspNetCore.Mvc;

namespace PosSystem.Main.Server
{
    internal static class ApiError
    {
        public static ObjectResult Result(int statusCode, string errorCode, string message)
        {
            return new ObjectResult(new
            {
                errorCode,
                message
            })
            {
                StatusCode = statusCode
            };
        }
    }
}
