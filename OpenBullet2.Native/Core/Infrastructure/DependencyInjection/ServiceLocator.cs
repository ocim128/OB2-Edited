using Microsoft.Extensions.DependencyInjection;
using System;

namespace OpenBullet2.Native.Infrastructure.DependencyInjection
{
    /// <summary>
    /// Service Provider wrapper for dependency injection throughout the application.
    /// Provides a static access point to the DI container.
    /// </summary>
    public static class ServiceLocator
    {
        private static IServiceProvider _serviceProvider;
        private static readonly object _lock = new object();

        /// <summary>
        /// Initializes the service provider instance.
        /// </summary>
        /// <param name="serviceProvider">The service provider to use for dependency resolution.</param>
        /// <exception cref="ArgumentNullException">Thrown when serviceProvider is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when already initialized.</exception>
        public static void Initialize(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
                throw new ArgumentNullException(nameof(serviceProvider));

            lock (_lock)
            {
                if (_serviceProvider != null)
                    throw new InvalidOperationException("ServiceLocator has already been initialized.");

                _serviceProvider = serviceProvider;
            }
        }

        /// <summary>
        /// Gets a service of the specified type from the DI container.
        /// Creates a new scope for each service resolution to ensure proper lifetime management.
        /// </summary>
        /// <typeparam name="T">The type of service to retrieve.</typeparam>
        /// <returns>The requested service instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown when ServiceLocator is not initialized.</exception>
        public static T GetService<T>()
        {
            if (_serviceProvider == null)
                throw new InvalidOperationException("ServiceLocator has not been initialized. Call Initialize() first.");

            using var scope = _serviceProvider.GetService<IServiceScopeFactory>().CreateScope();
            return scope.ServiceProvider.GetRequiredService<T>();
        }

        /// <summary>
        /// Gets a service of the specified type from the DI container, or null if not found.
        /// </summary>
        /// <typeparam name="T">The type of service to retrieve.</typeparam>
        /// <returns>The requested service instance, or null if not found.</returns>
        public static T GetOptionalService<T>() where T : class
        {
            if (_serviceProvider == null)
                return null;

            using var scope = _serviceProvider.GetService<IServiceScopeFactory>().CreateScope();
            return scope.ServiceProvider.GetService<T>();
        }

        /// <summary>
        /// Checks if the ServiceLocator has been initialized.
        /// </summary>
        public static bool IsInitialized => _serviceProvider != null;
    }
}
