using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace FdDraft.App
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                MessageBox.Show(e.Exception.Message + "\n\n" + e.Exception.GetType().Name, "FD-Draft", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            var window = new MainWindow();
            if (args.Length > 0 && File.Exists(args[0])) window.Loaded += (s, e) => window.OpenDrawing(args[0]);
            app.Run(window);
        }
    }

    /// <summary>A command from a lambda, for key bindings and menus.</summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _run;
        public RelayCommand(Action run) { _run = run; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _run();
    }
}
