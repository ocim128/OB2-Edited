using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace RuriLib.Functions.Http
{
    internal static class HttpExceptionClassifier
    {
        public static bool IsLikelyNetworkException(Exception ex)
        {
            if (ex == null)
            {
                return false;
            }

            var exType = ex.GetType();
            if (exType == typeof(HttpRequestException) ||
                exType == typeof(WebException) ||
                exType == typeof(SocketException) ||
                exType == typeof(TimeoutException))
            {
                return true;
            }

            if (exType == typeof(OperationCanceledException) || exType == typeof(IOException))
            {
                return NetworkExceptionHelper.IsNetworkException(ex);
            }

            return false;
        }
    }
}
