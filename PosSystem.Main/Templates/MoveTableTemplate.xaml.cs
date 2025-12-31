using System.Windows.Controls;

namespace PosSystem.Main.Templates
{
    public partial class MoveTableTemplate : UserControl
    {
        public MoveTableTemplate()
        {
            InitializeComponent();
        }

        public void SetData(string oldTableName, string newTableName)
        {
            txtOldTable.Text = oldTableName;
            txtNewTable.Text = newTableName;
            txtTime.Text = $"Thời gian: {System.DateTime.Now:HH:mm:ss}";
        }
    }
}
