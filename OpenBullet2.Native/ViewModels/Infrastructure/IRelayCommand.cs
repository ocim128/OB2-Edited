using System;
using System.Windows.Input;

namespace OpenBullet2.Native.ViewModels.Infrastructure
{
    /// <summary>
    /// Interface for a command that implements ICommand and supports parameter types
    /// </summary>
    public interface IRelayCommand : ICommand
    {
        /// <summary>
        /// Raises the CanExecuteChanged event
        /// </summary>
        void RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Generic interface for a command with a specific parameter type
    /// </summary>
    /// <typeparam name="T">The type of the command parameter</typeparam>
    public interface IRelayCommand<in T> : IRelayCommand
    {
        /// <summary>
        /// Determines whether the command can execute in its current state
        /// </summary>
        /// <param name="parameter">Data used by the command</param>
        /// <returns>true if this command can be executed; otherwise, false</returns>
        bool CanExecute(T parameter);

        /// <summary>
        /// Executes the command
        /// </summary>
        /// <param name="parameter">Data used by the command</param>
        void Execute(T parameter);
    }
}


