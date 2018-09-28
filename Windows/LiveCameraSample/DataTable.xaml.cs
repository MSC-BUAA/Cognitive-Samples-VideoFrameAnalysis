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
using System.Windows.Shapes;

namespace LiveCameraSample {
    /// <summary>
    /// Interaction logic for DataTable.xaml
    /// </summary>
    public partial class DataTable : Window {
        public DataTable() {
            InitializeComponent();
        }
    }

    public class PersonData {
        public string ID { get; set; }
        public string Name { get; set; }
        public int Times { get; set; }

        public PersonData() {
            this.ID = "";
            this.Name = "";
            Times = 0;
        }

        public PersonData(string ID, string Name) {
            this.ID = ID;
            this.Name = Name;

            Times = 0;
        }
    }
}
