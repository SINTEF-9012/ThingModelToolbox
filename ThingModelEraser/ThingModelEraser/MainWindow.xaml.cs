using System;
using System.Collections.Generic;
using System.Configuration;
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
using ThingModel;
using ThingModel.WebSockets;
using ThingModelEraser.Properties;

namespace ThingModelEraser
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Warehouse _warehouse;
        private Client _client;

        public MainWindow()
        {
            InitializeComponent();
            _warehouse = new Warehouse();
            _client = new Client(Settings.Default.name, Settings.Default.endpoint, _warehouse);
        }

        private void EraseEverything(object sender, RoutedEventArgs e)
        {
            var things = _warehouse.Things;
            _warehouse.RemoveCollection(new HashSet<Thing>(things));
            _client.Send();
            MessageBox.Show(this, things.Count + (things.Count != 1 ? " things deleted" : " thing deleted"));
        }

        private void DeleteThing(object sender, RoutedEventArgs e)
        {
            var thing = _warehouse.GetThing(ID.Text);
            if (thing == null)
            {
                MessageBox.Show(this, "Thing " + ID.Text + " unfound");
            }
            else
            {
                _warehouse.RemoveThing(thing);
                _client.Send();
                MessageBox.Show(this, "Thing " + ID.Text + " has been deleted");
            }
        }
    }
}
