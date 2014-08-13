using System;
using System.Windows;
using System.Windows.Threading;
using ThingModel.WebSockets;

namespace ThingModelBroadcastServer
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App
	{
		private Server _server;
		private DataContext _data;

		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);
			_server = new Server("ws://127.0.0.1:8083/", "/", false);


			OpenMainWindow();
		}

		private void OpenMainWindow()
		{
			_data = new DataContext(_server);
			_data.Dispatcher = Dispatcher;
			_data.Start();

			try
			{
				var main = new MainWindow();
				main.DataContext = _data;
				main.Show();
			}
			catch (Exception exc)
			{
				MessageBox.Show("View crash: "+exc.Message);
				OpenMainWindow();
			}
		}

		void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
		{
			MessageBox.Show("View crash: " + e.Exception.Message);
			e.Handled = true;
			OpenMainWindow();
		}

		private void OnExit(object sender, ExitEventArgs e)
		{
			_data.Stop();
		}
	}
}
