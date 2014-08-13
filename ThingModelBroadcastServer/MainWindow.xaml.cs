using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ThingModelBroadcastServer
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent();
			/*var canard = (Class1) DataContext;

			canard.Dispatcher = this.Dispatcher;
			canard.Start();
			Closing += (sender, args) => canard.Stop();*/

			
			Activated += OnActivated;
			Deactivated+= OnDeactivated;
		}

		private void OnDeactivated(object sender, EventArgs eventArgs)
		{
			((DataContext) DataContext).Draw = false;
		}

		private void OnActivated(object sender, EventArgs eventArgs)
		{
			((DataContext) DataContext).Draw = true;
		}
	}
}
