using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;

namespace RuriLib.Functions.Http
{
    /// <summary>
    /// Helper class to detect network-related exceptions
    /// </summary>
    internal static class NetworkExceptionHelper
    {
        /// <summary>
        /// Determines if an exception is a network-related exception that should trigger exponential backoff
        /// </summary>
        /// <param name="ex">The exception to check</param>
        /// <returns>True if the exception is network-related, false otherwise</returns>
        // Cache frequently used exception types for faster comparison
        private static readonly HashSet<Type> NetworkExceptionTypes = new()
        {
            typeof(HttpRequestException),
            typeof(WebException),
            typeof(SocketException),
            typeof(TimeoutException)
        };

        // Cache WebExceptionStatus values that indicate network issues
        private static readonly HashSet<WebExceptionStatus> NetworkWebStatuses = new()
        {
            WebExceptionStatus.ConnectFailure,
            WebExceptionStatus.NameResolutionFailure,
            WebExceptionStatus.ProxyNameResolutionFailure,
            WebExceptionStatus.SendFailure,
            WebExceptionStatus.ReceiveFailure,
            WebExceptionStatus.PipelineFailure,
            WebExceptionStatus.ConnectionClosed,
            WebExceptionStatus.Timeout,
            WebExceptionStatus.RequestCanceled
        };

        public static bool IsNetworkException(Exception ex)
        {
            if (ex == null)
                return false;

            // Fast path: Use type cache for faster comparison
            var exType = ex.GetType();
            if (NetworkExceptionTypes.Contains(exType))
                return true;

            // Special handling for OperationCanceledException (but not TimeoutException)
            if (ex is OperationCanceledException && !(ex is TimeoutException))
                return true;

            // Check for WebException with cached status values
            if (ex is WebException webEx && NetworkWebStatuses.Contains(webEx.Status))
                return true;

            // Check for IOException with SocketException as InnerException
            if (ex is IOException ioEx && ioEx.InnerException is SocketException)
                return true;

            // Optimized inner exception checking - iterative instead of recursive to avoid stack overhead
            // Limit depth to prevent infinite loops and improve performance
            var currentEx = ex.InnerException;
            int depth = 0;
            const int maxDepth = 5; // Reasonable limit for exception chain depth

            while (currentEx != null && depth < maxDepth)
            {
                var currentType = currentEx.GetType();

                // Fast type check
                if (NetworkExceptionTypes.Contains(currentType))
                    return true;

                // Special cases
                if (currentEx is OperationCanceledException && !(currentEx is TimeoutException))
                    return true;

                if (currentEx is WebException currentWebEx && NetworkWebStatuses.Contains(currentWebEx.Status))
                    return true;

                if (currentEx is IOException currentIoEx && currentIoEx.InnerException is SocketException)
                    return true;

                currentEx = currentEx.InnerException;
                depth++;
            }

            return false;
        }

        /// <summary>
        /// Calculates the delay for retry based on exponential backoff with jitter
        /// </summary>
        /// <param name="retryCount">The current retry attempt number (0-based)</param>
        /// <param name="baseDelayMs">The base delay in milliseconds (default: 100ms)</param>
        /// <param name="maxDelayMs">The maximum delay in milliseconds (default: 30s)</param>
        /// <returns>The delay in milliseconds for the next retry</returns>
        // Use a faster thread-safe random number generator
        private static int _seed = Environment.TickCount;

        [ThreadStatic]
        private static Random _localRandom;

        private static Random GetThreadRandom()
        {
            if (_localRandom == null)
            {
                int seed = Interlocked.Increment(ref _seed);
                _localRandom = new Random(seed);
            }
            return _localRandom;
        }

        // Cache powers of 2 for faster calculation - pre-calculated for better performance
        private static readonly int[] _powerOfTwoCache = new int[]
        {
            1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536
        };

        public static int CalculateBackoffDelay(int retryCount, int baseDelayMs = 100, int maxDelayMs = 30000)
        {
            // Fast path for common cases
            if (retryCount <= 0)
                return baseDelayMs;

            // Cap retry count to prevent array bounds checking and overflow
            var powerIndex = retryCount > 16 ? 16 : retryCount;

            // Calculate exponential delay using cached powers of 2
            var exponentialDelay = baseDelayMs * _powerOfTwoCache[powerIndex];

            // Add jitter using faster thread-local random
            var jitter = GetThreadRandom().Next(0, baseDelayMs);

            // Combine and cap at maximum delay
            var delay = exponentialDelay + jitter;
            return delay > maxDelayMs ? maxDelayMs : delay;
        }
    }
}