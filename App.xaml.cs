using System;
using System.Windows;
using System.Windows.Threading;

namespace XabboImager
{
    public partial class App : Application 
    { 
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (s, ex) => { var un = Unwrap(ex.Exception); MessageBox.Show(un.Message, "XabboImager", MessageBoxButton.OK, MessageBoxImage.Error); ex.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => { var err = ex.ExceptionObject as Exception; if (err != null) _ = Unwrap(err); };
            try
            {
                var w = new MainWindow();
                w.Hide();
            }
            catch (Exception ex2)
            {
                var un = Unwrap(ex2);
                MessageBox.Show(un.ToString(), "XabboImager startup", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        static Exception Unwrap(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }
    }
}
